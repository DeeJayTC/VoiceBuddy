using VoiceBuddy.Models;

namespace VoiceBuddy.Services;

/// <summary>
/// Sink for the Dictation app mode. Consumes target-transcript snapshots from
/// <see cref="DeepLVoiceService"/> and types newly-concluded segments into whatever window
/// currently has focus. Tentative segments are ignored — we only inject text the model
/// has committed to, so the user never sees characters rewritten mid-phrase.
/// </summary>
public sealed class DictationService
{
    private readonly DictationConfig _config;
    private int _injectedCount;

    public DictationService(DictationConfig config) => _config = config;

    /// <summary>
    /// Called each time the session resets (start/stop). Forgets any previously-typed
    /// segment count so the next session starts from segment 0.
    /// </summary>
    public void ResetSession() => _injectedCount = 0;

    /// <summary>
    /// Processes a fresh target snapshot. Any concluded segments past the ones already
    /// typed are injected immediately. Safe to call from the WS receive thread — SendInput
    /// is thread-safe and blocks only briefly.
    /// </summary>
    public void Consume(TranscriptSnapshot snapshot)
    {
        var concluded = snapshot.Concluded;

        // A session reset (StartAsync fires an empty snapshot after clearing its own
        // accumulator) or any new concluded list with fewer segments means we're on a
        // fresh transcript; rewind the counter so the next segment gets typed.
        if (concluded.Count < _injectedCount) _injectedCount = 0;

        if (concluded.Count <= _injectedCount) return;

        for (int i = _injectedCount; i < concluded.Count; i++)
        {
            var text = concluded[i].Text;
            if (string.IsNullOrEmpty(text)) continue;
            if (_config.AppendSpace && !text.EndsWith(' ')) text += ' ';
            TextInjector.Type(text);
        }
        _injectedCount = concluded.Count;
    }
}
