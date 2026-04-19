import SwiftUI
import AppKit

struct MainWindow: View {
    @EnvironmentObject var container: ServiceContainer
    @State private var tab: Tab = .overview

    enum Tab: String, CaseIterable, Identifiable {
        case overview, settings, style, layout, debug
        var id: String { rawValue }
        var title: String {
            switch self {
            case .overview: return "Overview"
            case .settings: return "Settings"
            case .style:    return "Overlay style"
            case .layout:   return "Overlay layout"
            case .debug:    return "Debug"
            }
        }
    }

    var body: some View {
        VStack(spacing: 0) {
            HeaderView()
            Divider()
            Picker("", selection: $tab) {
                ForEach(Tab.allCases) { t in
                    Text(t.title).tag(t)
                }
            }
            .pickerStyle(.segmented)
            .padding(.horizontal)
            .padding(.vertical, 8)
            Divider()
            ScrollView {
                Group {
                    switch tab {
                    case .overview: OverviewTab()
                    case .settings: SettingsTab()
                    case .style:    StyleTab()
                    case .layout:   LayoutTab()
                    case .debug:    DebugTab()
                    }
                }
                .padding()
                .frame(maxWidth: .infinity, alignment: .topLeading)
            }
        }
    }
}

private struct HeaderView: View {
    @EnvironmentObject var container: ServiceContainer

    var body: some View {
        HStack(spacing: 12) {
            Image(systemName: "text.bubble.fill")
                .font(.system(size: 28))
                .foregroundStyle(.tint)
            VStack(alignment: .leading, spacing: 2) {
                Text("VoiceBuddy").font(.title2.bold())
                Text(container.status)
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            Spacer()
            if let err = container.lastError {
                Text(err)
                    .font(.caption)
                    .foregroundStyle(.red)
                    .lineLimit(2)
                    .frame(maxWidth: 280, alignment: .trailing)
            }
        }
        .padding(.horizontal)
        .padding(.vertical, 12)
    }
}

// MARK: - Overview

private struct OverviewTab: View {
    @EnvironmentObject var container: ServiceContainer
    @State private var inputs: [AudioDevice] = []
    @State private var outputs: [AudioDevice] = []

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            GroupBox("Audio source") {
                VStack(alignment: .leading, spacing: 8) {
                    HStack {
                        Picker("Device", selection: Binding(
                            get: { container.settings.current.audio.deviceId ?? "" },
                            set: { newId in
                                if let dev = inputs.first(where: { $0.id == newId }) {
                                    container.settings.update {
                                        $0.audio.deviceId = newId
                                        $0.audio.deviceKind = dev.kind
                                    }
                                } else if newId.isEmpty {
                                    container.settings.update {
                                        $0.audio.deviceId = nil
                                    }
                                }
                            }
                        )) {
                            Text("(default)").tag("")
                            ForEach(inputs) { d in
                                Label {
                                    Text(d.name)
                                } icon: {
                                    Image(systemName: d.kind == .render ? "speaker.wave.2.fill" : "mic.fill")
                                }.tag(d.id)
                            }
                        }
                        Button("Refresh") { Task { await refresh() } }
                    }
                    HStack {
                        Button(container.isCapturing ? "Stop capture" : "Start capture") {
                            Task {
                                if container.isCapturing { await container.stopCapture() }
                                else { await container.startCapture() }
                            }
                        }
                        .buttonStyle(.borderedProminent)
                        Spacer()
                        ProgressView(value: min(max(Double(container.audio.rms) * 3, 0), 1))
                            .frame(width: 180)
                        Text(container.audio.currentFormat)
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }
                }
                .padding(8)
            }

            GroupBox("Voice out") {
                VStack(alignment: .leading, spacing: 8) {
                    Toggle("Stream translated voice",
                           isOn: container.binding(\.translation.voiceOutEnabled))
                    Picker("Output device", selection: Binding(
                        get: { container.settings.current.translation.voiceOutDeviceId ?? "" },
                        set: { newId in
                            container.settings.update {
                                $0.translation.voiceOutDeviceId = newId.isEmpty ? nil : newId
                            }
                        }
                    )) {
                        Text("(default)").tag("")
                        ForEach(outputs) { d in Text(d.name).tag(d.id) }
                    }
                    Picker("Voice",
                           selection: container.binding(\.translation.voiceGender)) {
                        Text("auto").tag("")
                        Text("female").tag("female")
                        Text("male").tag("male")
                    }
                }
                .padding(8)
            }

            Toggle("Show original text under translations",
                   isOn: container.binding(\.showOriginalText))
        }
        .task { await refresh() }
    }

    private func refresh() async {
        inputs = await container.audio.enumerateDevices()
        outputs = AudioCaptureService.enumerateOutputDevices()
    }
}

// MARK: - Settings

private struct SettingsTab: View {
    @EnvironmentObject var container: ServiceContainer

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            GroupBox("DeepL") {
                VStack(alignment: .leading, spacing: 8) {
                    Picker("API host",
                           selection: container.binding(\.translation.deepLApiHost)) {
                        Text("api.deepl.com (Pro)").tag("api.deepl.com")
                        Text("api-free.deepl.com (Free — Voice unsupported)").tag("api-free.deepl.com")
                    }
                    SecureField("API key",
                                text: container.binding(\.translation.deepLApiKey),
                                prompt: Text("DeepL API key"))
                        .textFieldStyle(.roundedBorder)
                    TextField("Source language (auto or BCP-47)",
                              text: container.binding(\.translation.sourceLang))
                        .textFieldStyle(.roundedBorder)
                    TextField("Target language (BCP-47)",
                              text: container.binding(\.translation.targetLang))
                        .textFieldStyle(.roundedBorder)
                    Toggle("Captions enabled",
                           isOn: container.binding(\.translation.captionsEnabled))
                }
                .padding(8)
            }
        }
    }
}

// MARK: - Style

private struct StyleTab: View {
    @EnvironmentObject var container: ServiceContainer

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            GroupBox("Text") {
                VStack(alignment: .leading, spacing: 8) {
                    TextField("Font family",
                              text: container.binding(\.overlayStyle.fontFamily))
                        .textFieldStyle(.roundedBorder)
                    stepper("Font size", container.binding(\.overlayStyle.fontSize), min: 10, max: 96)
                    stepper("Font weight", container.binding(\.overlayStyle.fontWeight), min: 100, max: 900, step: 100)
                    ColorHex("Text color",
                             container.binding(\.overlayStyle.textColor))
                    Picker("Alignment",
                           selection: container.binding(\.overlayStyle.textAlign)) {
                        ForEach(OverlayStyle.TextAlign.allCases) {
                            Text($0.rawValue.capitalized).tag($0)
                        }
                    }
                    slider("Line height", container.binding(\.overlayStyle.lineHeight), range: 0.8...2.2)
                }
                .padding(8)
            }
            GroupBox("Bubble") {
                VStack(alignment: .leading, spacing: 8) {
                    stepper("Max visible sentences", container.binding(\.overlayStyle.maxVisibleSentences), min: 1, max: 10)
                    stepper("Max lines per bubble", container.binding(\.overlayStyle.maxLines), min: 1, max: 8)
                    stepper("Padding X", container.binding(\.overlayStyle.paddingX), min: 0, max: 60)
                    stepper("Padding Y", container.binding(\.overlayStyle.paddingY), min: 0, max: 40)
                    stepper("Border radius", container.binding(\.overlayStyle.borderRadius), min: 0, max: 40)
                    ColorHex("Background color",
                             container.binding(\.overlayStyle.backgroundColor))
                    slider("Background opacity", container.binding(\.overlayStyle.backgroundOpacity), range: 0...1)
                    ColorHex("Outline color",
                             container.binding(\.overlayStyle.outlineColor))
                    stepper("Outline width", container.binding(\.overlayStyle.outlineWidth), min: 0, max: 12)
                }
                .padding(8)
            }
            GroupBox("Panel") {
                VStack(alignment: .leading, spacing: 8) {
                    ColorHex("Panel background color",
                             container.binding(\.overlayStyle.panelBackgroundColor))
                    slider("Panel opacity", container.binding(\.overlayStyle.panelBackgroundOpacity), range: 0...1)
                }
                .padding(8)
            }
        }
    }

    private func stepper(_ label: String, _ value: Binding<Int>, min: Int, max: Int, step: Int = 1) -> some View {
        HStack {
            Text(label).frame(width: 160, alignment: .leading)
            Stepper(value: value, in: min...max, step: step) {
                Text("\(value.wrappedValue)")
            }
        }
    }

    private func slider(_ label: String, _ value: Binding<Double>, range: ClosedRange<Double>) -> some View {
        HStack {
            Text(label).frame(width: 160, alignment: .leading)
            Slider(value: value, in: range)
            Text(String(format: "%.2f", value.wrappedValue))
                .font(.caption.monospacedDigit())
                .frame(width: 42, alignment: .trailing)
        }
    }
}

private struct ColorHex: View {
    let label: String
    @Binding var text: String

    init(_ label: String, _ text: Binding<String>) {
        self.label = label
        self._text = text
    }

    var body: some View {
        HStack {
            Text(label).frame(width: 160, alignment: .leading)
            TextField("#RRGGBB", text: $text)
                .textFieldStyle(.roundedBorder)
                .frame(width: 120)
            RoundedRectangle(cornerRadius: 4)
                .fill(hexColor(text))
                .frame(width: 24, height: 20)
                .overlay(RoundedRectangle(cornerRadius: 4).stroke(.secondary.opacity(0.4)))
        }
    }
}

// MARK: - Layout

private struct LayoutTab: View {
    @EnvironmentObject var container: ServiceContainer

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            GroupBox("Position") {
                VStack(alignment: .leading, spacing: 8) {
                    Picker("Mode",
                           selection: container.binding(\.overlayLayout.mode)) {
                        Text("Anchored to screen").tag(OverlayLayout.Mode.anchored)
                        Text("Free-drag").tag(OverlayLayout.Mode.free)
                    }
                    Picker("Anchor",
                           selection: container.binding(\.overlayLayout.anchor)) {
                        ForEach(OverlayLayout.Anchor.allCases) {
                            Text($0.rawValue).tag($0)
                        }
                    }
                    HStack {
                        Text("Offset").frame(width: 80, alignment: .leading)
                        Stepper(value: container.binding(\.overlayLayout.offsetX), in: -2000...2000, step: 10) {
                            Text("X: \(container.settings.current.overlayLayout.offsetX)")
                        }
                        Stepper(value: container.binding(\.overlayLayout.offsetY), in: -2000...2000, step: 10) {
                            Text("Y: \(container.settings.current.overlayLayout.offsetY)")
                        }
                    }
                    HStack {
                        Text("Size").frame(width: 80, alignment: .leading)
                        Stepper(value: container.binding(\.overlayLayout.width), in: 200...3000, step: 20) {
                            Text("W: \(container.settings.current.overlayLayout.width)")
                        }
                        Stepper(value: container.binding(\.overlayLayout.height), in: 80...1500, step: 20) {
                            Text("H: \(container.settings.current.overlayLayout.height)")
                        }
                    }
                    Toggle("Lock overlay (click-through)",
                           isOn: container.binding(\.overlayLayout.locked))
                }
                .padding(8)
            }
        }
    }
}

// MARK: - Debug

private struct DebugTab: View {
    @EnvironmentObject var container: ServiceContainer
    @State private var autoScroll = true

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Toggle("Enable debug log", isOn: Binding(
                    get: { container.debug.enabled },
                    set: { container.debug.enabled = $0 }
                ))
                Toggle("Log audio chunks", isOn: Binding(
                    get: { container.debug.logAudioChunks },
                    set: { container.debug.logAudioChunks = $0 }
                ))
                Toggle("Auto-scroll", isOn: $autoScroll)
                Spacer()
                Button("Test fire") {
                    let (src, tgt) = container.fakeFeed.next()
                    container.subtitles.publishSource(src)
                    container.subtitles.publishTarget(tgt)
                }
                Button("Clear") { container.debug.clear() }
            }
            Text("\(container.debug.entries.count) entries")
                .font(.caption)
                .foregroundStyle(.secondary)

            ScrollViewReader { proxy in
                ScrollView {
                    LazyVStack(alignment: .leading, spacing: 2) {
                        ForEach(container.debug.entries) { e in
                            HStack(alignment: .top, spacing: 6) {
                                Text(Self.timeFormat.string(from: e.timestamp))
                                    .foregroundStyle(.secondary)
                                Text(e.direction.rawValue)
                                Text(e.kind).bold()
                                Text(e.payload)
                                    .foregroundStyle(.secondary)
                                    .lineLimit(2)
                            }
                            .font(.caption.monospaced())
                            .id(e.id)
                        }
                    }
                    .padding(6)
                }
                .frame(minHeight: 300)
                .background(Color.black.opacity(0.06))
                .clipShape(RoundedRectangle(cornerRadius: 6))
                .onChange(of: container.debug.entries.count) { _, _ in
                    guard autoScroll, let id = container.debug.entries.last?.id else { return }
                    withAnimation { proxy.scrollTo(id, anchor: .bottom) }
                }
            }
        }
    }

    private static let timeFormat: DateFormatter = {
        let f = DateFormatter()
        f.dateFormat = "HH:mm:ss.SSS"
        return f
    }()
}

// MARK: - Binding glue for stepper arguments

extension ServiceContainer {
    // Needed because SwiftUI's Binding init from a $published path on a nested
    // object requires the nested object itself to be @Published. We rebuild the
    // binding manually when views need a `Binding<Int>` pointing into settings.
}
