using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using VoiceBuddy.Models;

namespace VoiceBuddy.Services;

/// <summary>
/// WASAPI capture: render-endpoint loopback (system audio) or capture-endpoint (microphone).
/// Downmixes to mono and emits frame events. Raw device format (rate/bit-depth) is preserved
/// on <see cref="CurrentFormat"/> so downstream STT/translate stages can resample as needed.
/// </summary>
public sealed class AudioCaptureService : IDisposable
{
    public event EventHandler<float>? LevelChanged;      // 0..1 RMS
    public event EventHandler<MonoFrame>? FrameAvailable; // mono float32 frames, native sample rate
    public event EventHandler<string>? Failed;
    public event EventHandler? StateChanged;             // fires whenever IsRunning flips

    public WaveFormat? CurrentFormat { get; private set; }
    public bool IsRunning => _capture is not null;

    private IWaveIn? _capture;
    private MMDevice? _device;
    private readonly Stopwatch _levelClock = new();
    private const double LevelIntervalMs = 33; // ~30 Hz

    public IReadOnlyList<AudioDevice> EnumerateDevices()
    {
        var list = new List<AudioDevice>();
        using var enumerator = new MMDeviceEnumerator();

        string? defaultRenderId = TryGetDefaultId(enumerator, DataFlow.Render);
        string? defaultCaptureId = TryGetDefaultId(enumerator, DataFlow.Capture);

        foreach (var d in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            list.Add(new AudioDevice(d.ID, d.FriendlyName, AudioDeviceKind.Render, d.ID == defaultRenderId));
        }
        foreach (var d in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            list.Add(new AudioDevice(d.ID, d.FriendlyName, AudioDeviceKind.Capture, d.ID == defaultCaptureId));
        }
        return list;
    }

    private static string? TryGetDefaultId(MMDeviceEnumerator enumerator, DataFlow flow)
    {
        try { return enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia).ID; }
        catch { return null; }
    }

    public void Start(string deviceId, AudioDeviceKind kind)
    {
        Stop();

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            _device = enumerator.GetDevice(deviceId);
            if (_device is null) { Failed?.Invoke(this, "Device not found"); return; }

            _capture = kind == AudioDeviceKind.Render
                ? new WasapiLoopbackCapture(_device)
                : new WasapiCapture(_device);

            CurrentFormat = _capture.WaveFormat;
            _capture.DataAvailable += OnData;
            _capture.RecordingStopped += OnStopped;
            _levelClock.Restart();
            _capture.StartRecording();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Failed?.Invoke(this, ex.Message);
            Stop();
        }
    }

    public void Stop()
    {
        var wasRunning = _capture is not null;
        if (_capture is not null)
        {
            try { _capture.StopRecording(); } catch { }
            _capture.DataAvailable -= OnData;
            _capture.RecordingStopped -= OnStopped;
            _capture.Dispose();
            _capture = null;
        }
        _device?.Dispose();
        _device = null;
        CurrentFormat = null;
        LevelChanged?.Invoke(this, 0f);
        if (wasRunning) StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        if (CurrentFormat is null || e.BytesRecorded == 0) return;
        var mono = Downmix(e.Buffer, e.BytesRecorded, CurrentFormat);
        if (mono.Length == 0) return;

        FrameAvailable?.Invoke(this, new MonoFrame(mono, CurrentFormat.SampleRate));

        if (_levelClock.Elapsed.TotalMilliseconds >= LevelIntervalMs)
        {
            _levelClock.Restart();
            LevelChanged?.Invoke(this, Rms(mono));
        }
    }

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null) Failed?.Invoke(this, e.Exception.Message);
    }

    private static float Rms(ReadOnlySpan<float> samples)
    {
        if (samples.Length == 0) return 0f;
        double sum = 0;
        for (int i = 0; i < samples.Length; i++) sum += samples[i] * samples[i];
        return (float)Math.Sqrt(sum / samples.Length);
    }

    /// <summary>Downmix interleaved PCM (16-bit int or 32-bit float) to mono float32.</summary>
    private static float[] Downmix(byte[] buffer, int byteCount, WaveFormat fmt)
    {
        var channels = fmt.Channels;
        var bits = fmt.BitsPerSample;
        var encoding = fmt.Encoding;

        if (encoding == WaveFormatEncoding.IeeeFloat && bits == 32)
        {
            var sampleCount = byteCount / 4;
            var frames = sampleCount / channels;
            var mono = new float[frames];
            for (int f = 0; f < frames; f++)
            {
                float sum = 0;
                for (int c = 0; c < channels; c++)
                {
                    int idx = (f * channels + c) * 4;
                    sum += BitConverter.ToSingle(buffer, idx);
                }
                mono[f] = sum / channels;
            }
            return mono;
        }

        if (encoding == WaveFormatEncoding.Pcm && bits == 16)
        {
            var sampleCount = byteCount / 2;
            var frames = sampleCount / channels;
            var mono = new float[frames];
            for (int f = 0; f < frames; f++)
            {
                float sum = 0;
                for (int c = 0; c < channels; c++)
                {
                    int idx = (f * channels + c) * 2;
                    short s = (short)(buffer[idx] | (buffer[idx + 1] << 8));
                    sum += s / 32768f;
                }
                mono[f] = sum / channels;
            }
            return mono;
        }

        if (encoding == WaveFormatEncoding.Pcm && bits == 32)
        {
            var sampleCount = byteCount / 4;
            var frames = sampleCount / channels;
            var mono = new float[frames];
            for (int f = 0; f < frames; f++)
            {
                float sum = 0;
                for (int c = 0; c < channels; c++)
                {
                    int idx = (f * channels + c) * 4;
                    int s = BitConverter.ToInt32(buffer, idx);
                    sum += s / 2147483648f;
                }
                mono[f] = sum / channels;
            }
            return mono;
        }

        // Unsupported format — give up quietly with empty frame.
        return Array.Empty<float>();
    }

    public void Dispose() => Stop();
}

public readonly record struct MonoFrame(float[] Samples, int SampleRate);
