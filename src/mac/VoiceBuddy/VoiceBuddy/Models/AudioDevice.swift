import Foundation

struct AudioDevice: Identifiable, Hashable {
    let id: String
    let name: String
    let kind: AudioDeviceKind
    let isDefault: Bool
}
