import Foundation
import Combine

@MainActor
final class SettingsStore: ObservableObject {
    @Published var current: Settings
    private let fileURL: URL

    init() {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("VoiceBuddy", isDirectory: true)
        try? FileManager.default.createDirectory(at: base, withIntermediateDirectories: true)
        fileURL = base.appendingPathComponent("settings.json")

        if let data = try? Data(contentsOf: fileURL),
           let s = try? Self.decoder.decode(Settings.self, from: data) {
            current = s
        } else {
            current = Settings()
        }
    }

    func save(_ next: Settings) {
        current = next
        do {
            let data = try Self.encoder.encode(next)
            let tmp = fileURL.appendingPathExtension("tmp")
            try data.write(to: tmp, options: .atomic)
            if FileManager.default.fileExists(atPath: fileURL.path) {
                _ = try? FileManager.default.replaceItemAt(fileURL, withItemAt: tmp)
            } else {
                try FileManager.default.moveItem(at: tmp, to: fileURL)
            }
        } catch {
            NSLog("SettingsStore save failed: \(error)")
        }
    }

    func update(_ mutate: (inout Settings) -> Void) {
        var next = current
        mutate(&next)
        save(next)
    }

    static let encoder: JSONEncoder = {
        let e = JSONEncoder()
        e.outputFormatting = [.prettyPrinted, .sortedKeys]
        return e
    }()

    static let decoder = JSONDecoder()
}
