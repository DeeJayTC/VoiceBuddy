import SwiftUI
import AppKit
import Combine

@main
struct VoiceBuddyApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var delegate
    @Environment(\.openWindow) private var openWindow

    var body: some Scene {
        WindowGroup("VoiceBuddy", id: "main") {
            MainWindow()
                .environmentObject(delegate.container)
                .frame(minWidth: 760, minHeight: 580)
                .onReceive(NotificationCenter.default.publisher(for: .voiceBuddyShowMain)) { _ in
                    openWindow(id: "main")
                }
        }
        .windowResizability(.contentSize)
        .commands {
            CommandGroup(replacing: .newItem) {}
        }
    }
}

// MARK: - AppDelegate

final class AppDelegate: NSObject, NSApplicationDelegate {
    let container = ServiceContainer()
    private var overlayController: OverlayController?
    private var statusItemService: StatusItemService?

    func applicationDidFinishLaunching(_ notification: Notification) {
        let overlay = OverlayController(container: container)
        overlay.show()
        self.overlayController = overlay
        self.statusItemService = StatusItemService(container: container, overlay: overlay)
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        // Menu-bar agent: closing the main window does not quit the app.
        false
    }
}

// MARK: - ServiceContainer (composition root)

@MainActor
final class ServiceContainer: ObservableObject {
    let settings: SettingsStore
    let subtitles: SubtitleBus
    let debug: DebugLog
    let audio: AudioCaptureService
    let voice: DeepLVoiceService
    let voiceOut: VoiceOutPlayer
    let fakeFeed: FakeSubtitleFeed

    @Published private(set) var isCapturing: Bool = false
    @Published var status: String = "Idle"
    @Published var lastError: String?

    private var cancellables = Set<AnyCancellable>()

    init() {
        let settings = SettingsStore()
        let bus = SubtitleBus()
        let debug = DebugLog()
        let audio = AudioCaptureService()
        let voice = DeepLVoiceService(debug: debug)
        let voiceOut = VoiceOutPlayer()

        self.settings = settings
        self.subtitles = bus
        self.debug = debug
        self.audio = audio
        self.voice = voice
        self.voiceOut = voiceOut
        self.fakeFeed = FakeSubtitleFeed()

        // Wire the capture → translate → subtitles/voice pipeline.
        audio.onFrame = { [weak self] frame in
            Task { @MainActor [weak self] in
                self?.voice.addFrame(frame)
            }
        }
        voice.onSourceUpdate = { [weak self] snap in
            Task { @MainActor [weak self] in
                self?.subtitles.publishSource(snap)
            }
        }
        voice.onTargetUpdate = { [weak self] snap in
            Task { @MainActor [weak self] in
                self?.subtitles.publishTarget(snap)
            }
        }
        voice.onTargetMedia = { [weak self] pcm in
            Task { @MainActor [weak self] in
                self?.voiceOut.addPcm(pcm)
            }
        }
        voice.onStatus = { [weak self] s in
            Task { @MainActor [weak self] in
                self?.status = s
            }
        }

        // Forward published changes from child services so SwiftUI views observing
        // the container re-render when settings / capture state / rms change.
        settings.objectWillChange
            .sink { [weak self] in self?.objectWillChange.send() }
            .store(in: &cancellables)
        audio.objectWillChange
            .sink { [weak self] in self?.objectWillChange.send() }
            .store(in: &cancellables)
        debug.objectWillChange
            .sink { [weak self] in self?.objectWillChange.send() }
            .store(in: &cancellables)
    }

    // MARK: - Start / Stop

    func startCapture() async {
        let cfg = settings.current
        lastError = nil
        do {
            try await audio.start(deviceId: cfg.audio.deviceId, kind: cfg.audio.deviceKind)

            if cfg.translation.voiceOutEnabled {
                try voiceOut.start(deviceId: cfg.translation.voiceOutDeviceId)
            }

            if cfg.translation.captionsEnabled || cfg.translation.voiceOutEnabled {
                guard !cfg.translation.deepLApiKey.isEmpty else {
                    throw NSError(domain: "VoiceBuddy", code: 1,
                                  userInfo: [NSLocalizedDescriptionKey: "DeepL API key is not set — Settings → DeepL API key."])
                }
                try await voice.start(
                    host: cfg.translation.deepLApiHost,
                    apiKey: cfg.translation.deepLApiKey,
                    sourceLang: cfg.translation.sourceLang,
                    targetLang: cfg.translation.targetLang,
                    wantVoice: cfg.translation.voiceOutEnabled,
                    voice: cfg.translation.voiceGender
                )
            }
            isCapturing = true
            status = "Running"
        } catch {
            lastError = error.localizedDescription
            status = "Error: \(error.localizedDescription)"
            await stopCapture()
        }
    }

    func stopCapture() async {
        audio.stop()
        await voice.stop()
        voiceOut.stop()
        isCapturing = false
        if lastError == nil { status = "Stopped" }
    }

    // MARK: - Settings binding helper

    func binding<T>(_ keyPath: WritableKeyPath<Settings, T>) -> Binding<T> {
        Binding(
            get: { self.settings.current[keyPath: keyPath] },
            set: { newValue in
                self.settings.update { $0[keyPath: keyPath] = newValue }
            }
        )
    }
}
