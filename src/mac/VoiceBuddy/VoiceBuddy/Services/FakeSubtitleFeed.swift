import Foundation

final class FakeSubtitleFeed {
    private static let samples: [(src: String, tgt: String)] = [
        ("Ich denke, das funktioniert ziemlich gut.", "I think this is working pretty well."),
        ("Pass auf den rechten Flügel auf!", "Watch the right flank!"),
        ("Sie versuchen, uns zu umgehen.", "They are trying to flank us."),
        ("Bereit für den nächsten Angriff?", "Ready for the next push?"),
        ("Ich brauche Heilung, schnell!", "I need healing, fast!")
    ]
    private var index = 0

    func next() -> (source: TranscriptSnapshot, target: TranscriptSnapshot) {
        let (src, tgt) = Self.samples[index % Self.samples.count]
        index += 1
        let now = Date().timeIntervalSince1970
        let source = TranscriptSnapshot(
            lang: "DE",
            concluded: [.init(text: src, t0: now, t1: now + 1.6)],
            tentative: []
        )
        let target = TranscriptSnapshot(
            lang: "EN",
            concluded: [.init(text: tgt, t0: now, t1: now + 1.6)],
            tentative: []
        )
        return (source, target)
    }
}
