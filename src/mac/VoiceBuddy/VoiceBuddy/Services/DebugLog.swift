import Foundation
import Combine

enum LogDirection: String {
    case out = "→"
    case `in` = "←"
}

struct LogEntry: Identifiable {
    let id = UUID()
    let timestamp: Date
    let direction: LogDirection
    let kind: String
    let payload: String
}

@MainActor
final class DebugLog: ObservableObject {
    @Published var enabled: Bool = false
    @Published var logAudioChunks: Bool = false
    @Published private(set) var entries: [LogEntry] = []
    private let cap = 1000

    func log(_ dir: LogDirection, kind: String, payload: String) {
        guard enabled else { return }
        let entry = LogEntry(timestamp: Date(), direction: dir, kind: kind, payload: payload)
        entries.append(entry)
        if entries.count > cap {
            entries.removeFirst(entries.count - cap)
        }
    }

    func audioChunk(bytes: Int) {
        guard enabled, logAudioChunks else { return }
        log(.out, kind: "source_media_chunk", payload: "\(bytes) bytes")
    }

    func clear() {
        entries.removeAll()
    }
}
