using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using VoiceBuddy.Models;

namespace VoiceBuddy.Services;

/// <summary>
/// DeepL Voice real-time client.
///
/// Flow:
///   1. POST /v3/voice/realtime → ephemeral {streaming_url, token} (one-time use).
///   2. Connect wss://{streaming_url}?token={token}.
///   3. Stream 16 kHz mono 16-bit-PCM chunks as JSON {source_media_chunk:{data}}.
///   4. Receive {source_transcript_update, target_transcript_update, ...} events.
///   5. Publish SubtitleEvents once a concluded target segment arrives.
///
/// Intentionally minimal for v0: no reconnect, no partial/tentative rendering,
/// no session pooling, no bandwidth trimming on silence. Ship it, iterate.
/// </summary>
public sealed class DeepLVoiceService : IDisposable
{
    public DeepLVoiceService(DebugLog debug) => _debug = debug;

    public bool IsRunning => _ws?.State == WebSocketState.Open;

    public event EventHandler<string>? StatusChanged;

    private readonly DebugLog _debug;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private Task? _receiveLoop;
    private Task? _sendLoop;
    private Channel<byte[]>? _outgoing;

    private readonly List<TranscriptSegment> _srcConcluded = new();
    private readonly List<TranscriptSegment> _tgtConcluded = new();
    private string _srcLang = "auto";
    private string _targetLang = string.Empty;

    public event EventHandler<TranscriptSnapshot>? SourceUpdated;
    public event EventHandler<TranscriptSnapshot>? TargetUpdated;

    // --- chunking: 120 ms at 16 kHz mono int16 = 1920 samples = 3840 bytes. Well within
    //     DeepL's "50–250 ms / ≤100 kB / ≤1 s per chunk" window.
    private const int TargetRate = 16_000;
    private const int ChunkDurationMs = 120;
    private const int ChunkSamples = TargetRate * ChunkDurationMs / 1000;

    private readonly List<short> _pcm = new(capacity: TargetRate);
    private readonly object _pcmLock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<bool> StartAsync(string host, string apiKey, string sourceLang, string targetLang, CancellationToken ct = default)
    {
        // Always run a full cleanup first. IsRunning only reflects the WebSocket state;
        // a previous session that the server dropped (e.g. 30 s inactivity) leaves the
        // socket in a closed state but loops/channels may still need tearing down.
        Stop();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            StatusChanged?.Invoke(this, "DeepL API key missing");
            return false;
        }

        _srcLang = sourceLang;
        _targetLang = targetLang;
        _srcConcluded.Clear();
        _tgtConcluded.Clear();
        SourceUpdated?.Invoke(this, TranscriptSnapshot.Empty(_srcLang));
        TargetUpdated?.Invoke(this, TranscriptSnapshot.Empty(_targetLang));

        SessionInfo session;
        try
        {
            session = await RequestSessionAsync(host, apiKey, sourceLang, targetLang, ct);
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Session request failed: {ex.Message}");
            return false;
        }

        try
        {
            var wsUri = new Uri($"{session.StreamingUrl}?token={Uri.EscapeDataString(session.Token)}");
            _debug.Log(LogDirection.Out, "ws connect", wsUri.GetLeftPart(UriPartial.Path));
            _ws = new ClientWebSocket();
            await _ws.ConnectAsync(wsUri, ct);
            _debug.Log(LogDirection.In, "ws open", wsUri.Host);
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"WebSocket connect failed: {ex.Message}");
            _ws?.Dispose();
            _ws = null;
            return false;
        }

        _outgoing = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
        _cts = new CancellationTokenSource();
        _receiveLoop = Task.Run(() => ReceiveLoop(_cts.Token));
        _sendLoop = Task.Run(() => SendLoop(_cts.Token));

        StatusChanged?.Invoke(this, $"DeepL Voice session {session.SessionId[..8]}… connected");
        return true;
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try
        {
            if (_ws?.State == WebSocketState.Open)
            {
                const string term = "{\"end_of_source_media\":{}}";
                _debug.Log(LogDirection.Out, "end_of_source_media", term);
                _ = _ws.SendAsync(
                    Encoding.UTF8.GetBytes(term),
                    WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
            }
        }
        catch { }
        try { _ws?.Abort(); } catch { }
        _ws?.Dispose();
        _ws = null;

        _outgoing?.Writer.TryComplete();
        try { _sendLoop?.Wait(TimeSpan.FromSeconds(1)); } catch { }
        try { _receiveLoop?.Wait(TimeSpan.FromSeconds(1)); } catch { }
        _outgoing = null;

        _cts?.Dispose();
        _cts = null;

        lock (_pcmLock) _pcm.Clear();
        _srcConcluded.Clear();
        _tgtConcluded.Clear();

        StatusChanged?.Invoke(this, "Idle");
    }

    /// <summary>
    /// Called from the audio capture thread. Resamples to 16 kHz, converts to int16,
    /// accumulates, and enqueues completed 120-ms chunks on the outgoing channel.
    /// </summary>
    public void AddFrame(MonoFrame frame)
    {
        if (!IsRunning || _outgoing is null) return;

        var samples16k = Resampler.Linear(frame.Samples, frame.SampleRate, TargetRate);
        if (samples16k.Length == 0) return;

        lock (_pcmLock)
        {
            for (int i = 0; i < samples16k.Length; i++)
            {
                var s = samples16k[i] * 32767f;
                if (s > 32767f) s = 32767f; else if (s < -32768f) s = -32768f;
                _pcm.Add((short)s);
            }

            while (_pcm.Count >= ChunkSamples)
            {
                var bytes = new byte[ChunkSamples * 2];
                // Little-endian int16 pack
                for (int i = 0; i < ChunkSamples; i++)
                {
                    short v = _pcm[i];
                    bytes[i * 2] = (byte)(v & 0xFF);
                    bytes[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
                }
                _pcm.RemoveRange(0, ChunkSamples);
                _outgoing.Writer.TryWrite(bytes);
            }
        }
    }

    // ---------- REST session request ----------

    private sealed record SessionInfo(string StreamingUrl, string Token, string SessionId);

    private async Task<SessionInfo> RequestSessionAsync(
        string host, string apiKey, string sourceLang, string targetLang, CancellationToken ct)
    {
        var url = $"https://{host}/v3/voice/realtime";
        var auto = sourceLang.Equals("auto", StringComparison.OrdinalIgnoreCase);

        var payload = new SessionRequest(
            // Exact token DeepL accepts; see /api-reference/voice/request-session.md schema enum.
            SourceMediaContentType: "audio/pcm;encoding=s16le;rate=16000",
            SourceLanguage: auto ? null : sourceLang.ToLowerInvariant(),
            SourceLanguageMode: auto ? "auto" : "fixed",
            TargetLanguages: new[] { targetLang },
            MessageFormat: "json");

        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        _debug.Log(LogDirection.Out, $"POST {url}", payloadJson);

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.TryAddWithoutValidation("Authorization", $"DeepL-Auth-Key {apiKey}");
        req.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        _debug.Log(LogDirection.In, $"HTTP {(int)resp.StatusCode}", body);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"{(int)resp.StatusCode}: {body}");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        return new SessionInfo(
            StreamingUrl: root.GetProperty("streaming_url").GetString() ?? throw new InvalidOperationException("no streaming_url"),
            Token: root.GetProperty("token").GetString() ?? throw new InvalidOperationException("no token"),
            SessionId: root.GetProperty("session_id").GetString() ?? "");
    }

    private sealed record SessionRequest(
        string SourceMediaContentType,
        string? SourceLanguage,
        string SourceLanguageMode,
        string[] TargetLanguages,
        string MessageFormat);

    // ---------- WebSocket send loop ----------

    private async Task SendLoop(CancellationToken ct)
    {
        if (_outgoing is null || _ws is null) return;
        try
        {
            await foreach (var chunk in _outgoing.Reader.ReadAllAsync(ct))
            {
                if (_ws.State != WebSocketState.Open) break;
                var msg = BuildSourceMediaChunkJson(chunk);
                await _ws.SendAsync(msg, WebSocketMessageType.Text, endOfMessage: true, ct);
                _debug.LogAudioChunk(chunk.Length);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { /* mirrored in receive loop */ }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested) return;
            StatusChanged?.Invoke(this, $"Send error: {ex.Message}");
        }
    }

    private static byte[] BuildSourceMediaChunkJson(byte[] pcm)
    {
        var b64 = Convert.ToBase64String(pcm);
        // Hand-assembled — tiny fixed envelope, avoids a serializer allocation per chunk.
        var s = "{\"source_media_chunk\":{\"data\":\"" + b64 + "\"}}";
        return Encoding.UTF8.GetBytes(s);
    }

    // ---------- WebSocket receive loop ----------

    private async Task ReceiveLoop(CancellationToken ct)
    {
        if (_ws is null) return;
        var buffer = new byte[16 * 1024];
        var sb = new StringBuilder();

        try
        {
            while (_ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                sb.Clear();
                WebSocketReceiveResult result;
                do
                {
                    result = await _ws.ReceiveAsync(buffer, ct);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                } while (!result.EndOfMessage);

                var text = sb.ToString();
                _debug.Log(LogDirection.In, "ws", text);
                HandleServerMessage(text);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException ex)
        {
            // All WebSocket-level errors while the session is alive are treated as
            // a graceful disconnect. The DeepL Voice API terminates the session after
            // 30 s without audio ("inactivity timeout") — that's the expected path here.
            if (!ct.IsCancellationRequested)
                StatusChanged?.Invoke(this, $"DeepL Voice disconnected: {ex.Message}");
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
                StatusChanged?.Invoke(this, $"Receive error: {ex.Message}");
        }
    }

    private void HandleServerMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("source_transcript_update", out var src))
            {
                HandleTranscriptUpdate(src, _srcConcluded, _srcLang, isSource: true);
                return;
            }

            if (root.TryGetProperty("target_transcript_update", out var tgt))
            {
                HandleTranscriptUpdate(tgt, _tgtConcluded, _targetLang, isSource: false);
                return;
            }

            if (root.TryGetProperty("error", out var err))
            {
                var msg = err.TryGetProperty("error_message", out var mEl) ? mEl.GetString() : "(no message)";
                var code = err.TryGetProperty("reason_code", out var rcEl) ? rcEl.GetInt32() : 0;
                StatusChanged?.Invoke(this, $"DeepL error {code}: {msg}");
                Stop();
                return;
            }

            if (root.TryGetProperty("end_of_stream", out _))
            {
                StatusChanged?.Invoke(this, "DeepL session ended");
                Stop();
                return;
            }
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Parse error: {ex.Message}");
        }
    }

    private void HandleTranscriptUpdate(JsonElement body, List<TranscriptSegment> accumulator, string fallbackLang, bool isSource)
    {
        // Concluded arrives as a delta — each new message only carries the newly-finalized
        // segments — so accumulate them across updates.
        if (body.TryGetProperty("concluded", out var concluded) && concluded.ValueKind == JsonValueKind.Array)
        {
            foreach (var seg in concluded.EnumerateArray())
            {
                if (TryParseSegment(seg, out var parsed))
                    accumulator.Add(parsed);
            }
        }

        // Tentative arrives as a full replacement.
        var tentative = new List<TranscriptSegment>();
        if (body.TryGetProperty("tentative", out var tent) && tent.ValueKind == JsonValueKind.Array)
        {
            foreach (var seg in tent.EnumerateArray())
            {
                if (TryParseSegment(seg, out var parsed))
                    tentative.Add(parsed);
            }
        }

        var lang = body.TryGetProperty("language", out var lEl) ? lEl.GetString() ?? fallbackLang : fallbackLang;
        var snapshot = new TranscriptSnapshot(lang, accumulator.ToArray(), tentative);

        if (isSource) SourceUpdated?.Invoke(this, snapshot);
        else TargetUpdated?.Invoke(this, snapshot);
    }

    private static bool TryParseSegment(JsonElement seg, out TranscriptSegment parsed)
    {
        parsed = new TranscriptSegment("", 0, 0);
        if (seg.ValueKind != JsonValueKind.Object) return false;
        var text = seg.TryGetProperty("text", out var tEl) ? tEl.GetString() ?? "" : "";
        if (string.IsNullOrEmpty(text)) return false;
        var t0 = seg.TryGetProperty("start_time", out var s0) ? s0.GetDouble() / 1000.0 : 0;
        var t1 = seg.TryGetProperty("end_time", out var s1) ? s1.GetDouble() / 1000.0 : 0;
        parsed = new TranscriptSegment(text, t0, t1);
        return true;
    }

    public void Dispose()
    {
        Stop();
        _http.Dispose();
    }
}
