using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace VoiceBuddy.Services;

/// <summary>
/// Plays 16 kHz / 16-bit / mono PCM that the DeepL Voice API streams back as
/// <c>target_media_chunk</c>. Output goes to any WASAPI render endpoint — ordinary
/// speakers for monitoring, or a virtual cable (VB-CABLE's "CABLE Input") so apps
/// like OBS or Discord can pick the translated voice as their microphone.
///
/// Buffer is generous (10 s) and overflow-discards so we never block the
/// DeepL receive loop if playback falls behind.
/// </summary>
public sealed class VoiceOutPlayer : IDisposable
{
    private static readonly WaveFormat PcmFormat = new(16_000, 16, 1);

    private WasapiOut? _output;
    private BufferedWaveProvider? _buffer;
    private MMDevice? _device;

    public event EventHandler<string>? StatusChanged;

    public bool IsRunning => _output?.PlaybackState == PlaybackState.Playing;

    public void Start(string? deviceId)
    {
        Stop();

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            _device = string.IsNullOrEmpty(deviceId)
                ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                : enumerator.GetDevice(deviceId);
            if (_device is null)
            {
                StatusChanged?.Invoke(this, "Output device not found");
                return;
            }

            _buffer = new BufferedWaveProvider(PcmFormat)
            {
                BufferDuration = TimeSpan.FromSeconds(10),
                DiscardOnBufferOverflow = true,
            };
            _output = new WasapiOut(_device, AudioClientShareMode.Shared, useEventSync: true, latency: 100);
            _output.Init(_buffer);
            _output.Play();
            StatusChanged?.Invoke(this, $"Playing to {_device.FriendlyName}");
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Output failed: {ex.Message}");
            Stop();
        }
    }

    public void AddPcmChunk(ReadOnlySpan<byte> pcm)
    {
        if (_buffer is null || pcm.Length == 0) return;
        _buffer.AddSamples(pcm.ToArray(), 0, pcm.Length);
    }

    public void Stop()
    {
        try { _output?.Stop(); } catch { }
        _output?.Dispose();
        _output = null;
        _buffer?.ClearBuffer();
        _buffer = null;
        _device?.Dispose();
        _device = null;
    }

    public void Dispose() => Stop();
}
