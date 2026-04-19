namespace VoiceBuddy.Models;

public sealed record TranscriptSegment(string Text, double T0, double T1);

/// <summary>
/// State of one side of a transcript (source or target) at a given moment.
/// DeepL Voice streams <c>concluded</c> segments as deltas and <c>tentative</c> segments
/// as full replacements, so the client accumulates concluded and swaps tentative wholesale.
/// The snapshot represents the merged "current view" — render this and you get live updates.
/// </summary>
public sealed record TranscriptSnapshot(
    string Lang,
    IReadOnlyList<TranscriptSegment> Concluded,
    IReadOnlyList<TranscriptSegment> Tentative)
{
    public static TranscriptSnapshot Empty(string lang) =>
        new(lang, Array.Empty<TranscriptSegment>(), Array.Empty<TranscriptSegment>());

    public bool IsEmpty => Concluded.Count == 0 && Tentative.Count == 0;
}
