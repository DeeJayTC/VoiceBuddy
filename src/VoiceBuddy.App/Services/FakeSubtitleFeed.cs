using VoiceBuddy.Models;

namespace VoiceBuddy.Services;

public sealed class FakeSubtitleFeed
{
    private static readonly (string Src, string Dst)[] Samples =
    {
        ("Ich denke, das funktioniert ziemlich gut.", "I think this is working pretty well."),
        ("Pass auf den rechten Flügel auf!",          "Watch the right flank!"),
        ("Sie versuchen, uns zu umgehen.",            "They are trying to flank us."),
        ("Bereit für den nächsten Angriff?",          "Ready for the next push?"),
        ("Ich brauche Heilung, schnell!",             "I need healing, fast!"),
    };

    private int _index;

    /// <summary>
    /// Produces a (source, target) snapshot pair with a single concluded segment each.
    /// Useful for tuning styling without burning a live session.
    /// </summary>
    public (TranscriptSnapshot Source, TranscriptSnapshot Target) Next()
    {
        var (src, dst) = Samples[_index++ % Samples.Length];
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        return (
            new TranscriptSnapshot("DE",
                Concluded: new[] { new TranscriptSegment(src, now, now + 1.6) },
                Tentative: Array.Empty<TranscriptSegment>()),
            new TranscriptSnapshot("EN",
                Concluded: new[] { new TranscriptSegment(dst, now, now + 1.6) },
                Tentative: Array.Empty<TranscriptSegment>()));
    }
}
