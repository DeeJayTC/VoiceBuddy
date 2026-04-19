import Foundation

struct TranscriptSegment: Equatable, Hashable {
    let text: String
    let t0: Double
    let t1: Double
}

struct TranscriptSnapshot: Equatable {
    let lang: String
    let concluded: [TranscriptSegment]
    let tentative: [TranscriptSegment]

    static func empty(_ lang: String = "") -> TranscriptSnapshot {
        .init(lang: lang, concluded: [], tentative: [])
    }

    var isEmpty: Bool { concluded.isEmpty && tentative.isEmpty }
}
