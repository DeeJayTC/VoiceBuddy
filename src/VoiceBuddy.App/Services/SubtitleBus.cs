using VoiceBuddy.Models;

namespace VoiceBuddy.Services;

/// <summary>
/// Transport for live transcript state. Source and target fire independently whenever
/// the upstream (DeepL Voice or the fake feed) changes either side.
/// </summary>
public sealed class SubtitleBus
{
    public event EventHandler<TranscriptSnapshot>? SourceUpdated;
    public event EventHandler<TranscriptSnapshot>? TargetUpdated;

    public void PublishSource(TranscriptSnapshot snapshot) => SourceUpdated?.Invoke(this, snapshot);
    public void PublishTarget(TranscriptSnapshot snapshot) => TargetUpdated?.Invoke(this, snapshot);
}
