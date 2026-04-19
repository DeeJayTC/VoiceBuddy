import Foundation

struct Settings: Codable, Equatable {
    var overlayStyle = OverlayStyle()
    var overlayLayout = OverlayLayout()
    var translation = TranslationConfig()
    var audio = AudioConfig()
    var showOriginalText: Bool = false
}

struct OverlayStyle: Codable, Equatable {
    var fontFamily: String = "Helvetica Neue"
    var fontSize: Int = 28
    var fontWeight: Int = 600
    var textColor: String = "#FFFFFF"
    var backgroundColor: String = "#000000"
    var backgroundOpacity: Double = 0.55
    var outlineColor: String = "#000000"
    var outlineWidth: Int = 2
    var textAlign: TextAlign = .center
    var lineHeight: Double = 1.25
    var paddingX: Int = 18
    var paddingY: Int = 10
    var borderRadius: Int = 8
    var maxLines: Int = 3
    var maxVisibleSentences: Int = 5
    var panelBackgroundColor: String = "#000000"
    var panelBackgroundOpacity: Double = 0.0

    enum TextAlign: String, Codable, CaseIterable, Identifiable {
        case left, center, right
        var id: String { rawValue }
    }
}

struct OverlayLayout: Codable, Equatable {
    var mode: Mode = .anchored
    var anchor: Anchor = .bottomCenter
    var offsetX: Int = 0
    var offsetY: Int = 80
    var width: Int = 960
    var height: Int = 220
    var locked: Bool = false
    var freeLeft: Double = 0
    var freeTop: Double = 0

    enum Mode: String, Codable, CaseIterable, Identifiable {
        case anchored, free
        var id: String { rawValue }
    }

    enum Anchor: String, Codable, CaseIterable, Identifiable {
        case topLeft, topCenter, topRight
        case middleLeft, middleCenter, middleRight
        case bottomLeft, bottomCenter, bottomRight
        var id: String { rawValue }
    }
}

struct TranslationConfig: Codable, Equatable {
    var sourceLang: String = "auto"
    var targetLang: String = "EN-US"
    var deepLApiKey: String = ""
    var deepLApiHost: String = "api.deepl.com"
    var captionsEnabled: Bool = true
    var voiceOutEnabled: Bool = false
    var voiceOutDeviceId: String? = nil
    var voiceGender: String = ""
}

struct AudioConfig: Codable, Equatable {
    var deviceId: String? = nil
    var deviceKind: AudioDeviceKind = .render
}

enum AudioDeviceKind: String, Codable {
    case render   // system audio (ScreenCaptureKit)
    case capture  // microphone (AVAudioEngine)
}
