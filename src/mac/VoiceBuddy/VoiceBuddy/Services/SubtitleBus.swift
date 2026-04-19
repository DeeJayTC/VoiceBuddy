import Foundation
import Combine

@MainActor
final class SubtitleBus: ObservableObject {
    let sourceUpdates = PassthroughSubject<TranscriptSnapshot, Never>()
    let targetUpdates = PassthroughSubject<TranscriptSnapshot, Never>()

    @Published private(set) var latestSource: TranscriptSnapshot = .empty()
    @Published private(set) var latestTarget: TranscriptSnapshot = .empty()

    func publishSource(_ snap: TranscriptSnapshot) {
        latestSource = snap
        sourceUpdates.send(snap)
    }

    func publishTarget(_ snap: TranscriptSnapshot) {
        latestTarget = snap
        targetUpdates.send(snap)
    }

    func reset() {
        latestSource = .empty()
        latestTarget = .empty()
        sourceUpdates.send(.empty())
        targetUpdates.send(.empty())
    }
}
