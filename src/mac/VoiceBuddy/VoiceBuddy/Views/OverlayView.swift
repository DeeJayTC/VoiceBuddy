import SwiftUI
import AppKit
import Combine

// MARK: - OverlayController (owns the NSPanel)

@MainActor
final class OverlayController {
    private let container: ServiceContainer
    private let panel: OverlayPanel
    private let viewModel: OverlayViewModel
    private var settingsSub: AnyCancellable?

    init(container: ServiceContainer) {
        self.container = container
        self.viewModel = OverlayViewModel(container: container)
        self.panel = OverlayPanel(contentRect: Self.frame(for: container.settings.current.overlayLayout))
        panel.contentView = NSHostingView(rootView: OverlayView(vm: viewModel))
        panel.didMove = { [weak self] newOrigin in
            guard let self else { return }
            self.container.settings.update { s in
                s.overlayLayout.mode = .free
                s.overlayLayout.freeLeft = Double(newOrigin.x)
                s.overlayLayout.freeTop = Double(newOrigin.y)
            }
        }
        panel.didResize = { [weak self] newSize in
            guard let self else { return }
            self.container.settings.update { s in
                s.overlayLayout.width = Int(newSize.width)
                s.overlayLayout.height = Int(newSize.height)
            }
        }

        applyLock()

        settingsSub = container.settings.objectWillChange.sink { [weak self] _ in
            DispatchQueue.main.async { self?.applyLayoutIfNeeded() }
        }
    }

    func show() {
        panel.orderFrontRegardless()
    }

    func applyLock() {
        let locked = container.settings.current.overlayLayout.locked
        panel.ignoresMouseEvents = locked
    }

    private var lastAppliedLayout: OverlayLayout?

    func applyLayoutIfNeeded() {
        let layout = container.settings.current.overlayLayout
        applyLock()
        if lastAppliedLayout == layout { return }
        // If we're in free mode and the user is dragging, don't fight the drag.
        if layout.mode == .free, panel.isInInteractiveMove {
            lastAppliedLayout = layout
            return
        }
        panel.setFrameProgrammatically(Self.frame(for: layout))
        lastAppliedLayout = layout
    }

    private static func frame(for layout: OverlayLayout) -> NSRect {
        let screen = NSScreen.main?.visibleFrame ?? NSRect(x: 0, y: 0, width: 1920, height: 1080)
        let w = CGFloat(layout.width)
        let h = CGFloat(layout.height)

        if layout.mode == .free {
            return NSRect(x: CGFloat(layout.freeLeft), y: CGFloat(layout.freeTop), width: w, height: h)
        }

        let ox = CGFloat(layout.offsetX)
        let oy = CGFloat(layout.offsetY)
        var x: CGFloat = screen.minX
        var y: CGFloat = screen.minY
        switch layout.anchor {
        case .topLeft:
            x = screen.minX + ox
            y = screen.maxY - h - oy
        case .topCenter:
            x = screen.midX - w / 2 + ox
            y = screen.maxY - h - oy
        case .topRight:
            x = screen.maxX - w - ox
            y = screen.maxY - h - oy
        case .middleLeft:
            x = screen.minX + ox
            y = screen.midY - h / 2 + oy
        case .middleCenter:
            x = screen.midX - w / 2 + ox
            y = screen.midY - h / 2 + oy
        case .middleRight:
            x = screen.maxX - w - ox
            y = screen.midY - h / 2 + oy
        case .bottomLeft:
            x = screen.minX + ox
            y = screen.minY + oy
        case .bottomCenter:
            x = screen.midX - w / 2 + ox
            y = screen.minY + oy
        case .bottomRight:
            x = screen.maxX - w - ox
            y = screen.minY + oy
        }
        return NSRect(x: x, y: y, width: w, height: h)
    }
}

// MARK: - NSPanel subclass

final class OverlayPanel: NSPanel {
    var didMove: ((NSPoint) -> Void)?
    var didResize: ((NSSize) -> Void)?

    private(set) var isInInteractiveMove = false
    private var suppressFrameCallbacks = false

    init(contentRect: NSRect) {
        super.init(
            contentRect: contentRect,
            styleMask: [.nonactivatingPanel, .borderless, .resizable],
            backing: .buffered,
            defer: false
        )
        self.isOpaque = false
        self.backgroundColor = .clear
        self.hasShadow = false
        self.level = .floating
        self.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary]
        self.isMovableByWindowBackground = true
        self.hidesOnDeactivate = false
        self.acceptsMouseMovedEvents = false
        self.titleVisibility = .hidden
        self.titlebarAppearsTransparent = true
    }

    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }

    override func mouseDown(with event: NSEvent) {
        isInInteractiveMove = true
        super.mouseDown(with: event)
    }

    override func mouseUp(with event: NSEvent) {
        super.mouseUp(with: event)
        isInInteractiveMove = false
        didMove?(frame.origin)
    }

    func setFrameProgrammatically(_ frameRect: NSRect) {
        suppressFrameCallbacks = true
        setFrame(frameRect, display: true)
        suppressFrameCallbacks = false
    }

    override func setFrame(_ frameRect: NSRect, display flag: Bool) {
        let oldSize = frame.size
        super.setFrame(frameRect, display: flag)
        if oldSize != frameRect.size, !suppressFrameCallbacks {
            didResize?(frameRect.size)
        }
    }
}

// MARK: - OverlayViewModel

@MainActor
final class OverlayViewModel: ObservableObject {
    @Published var finalizedTarget: [String] = []
    @Published var finalizedSource: [String] = []
    @Published var pendingTarget: String?
    @Published var pendingSource: String?
    @Published var style: OverlayStyle
    @Published var showOriginal: Bool

    private var srcStart = 0
    private var tgtStart = 0
    private var subs = Set<AnyCancellable>()
    private var idleTimer: Timer?

    init(container: ServiceContainer) {
        self.style = container.settings.current.overlayStyle
        self.showOriginal = container.settings.current.showOriginalText

        container.subtitles.targetUpdates.sink { [weak self] snap in
            self?.ingestTarget(snap)
            self?.kickIdleTimer()
        }.store(in: &subs)

        container.subtitles.sourceUpdates.sink { [weak self] snap in
            self?.ingestSource(snap)
        }.store(in: &subs)

        container.settings.$current.sink { [weak self] s in
            self?.style = s.overlayStyle
            self?.showOriginal = s.showOriginalText
            self?.cap()
        }.store(in: &subs)
    }

    private func kickIdleTimer() {
        idleTimer?.invalidate()
        idleTimer = Timer.scheduledTimer(withTimeInterval: 8, repeats: false) { [weak self] _ in
            Task { @MainActor [weak self] in
                self?.finalizedTarget.removeAll()
                self?.finalizedSource.removeAll()
                self?.pendingTarget = nil
                self?.pendingSource = nil
            }
        }
    }

    private func ingestTarget(_ snap: TranscriptSnapshot) {
        var pending = pendingTarget
        ingest(snap: snap, start: &tgtStart, finalized: &finalizedTarget, pending: &pending)
        pendingTarget = pending
        cap()
    }

    private func ingestSource(_ snap: TranscriptSnapshot) {
        var pending = pendingSource
        ingest(snap: snap, start: &srcStart, finalized: &finalizedSource, pending: &pending)
        pendingSource = pending
        cap()
    }

    private func ingest(snap: TranscriptSnapshot,
                        start: inout Int,
                        finalized: inout [String],
                        pending: inout String?)
    {
        if snap.concluded.count < start {
            finalized.removeAll()
            start = 0
        }
        let pendingText = (snap.concluded.dropFirst(start) + snap.tentative)
            .map(\.text).joined()
            .trimmingCharacters(in: .whitespaces)

        if snap.tentative.isEmpty && snap.concluded.count > start {
            let text = snap.concluded[start...].map(\.text).joined()
                .trimmingCharacters(in: .whitespaces)
            if !text.isEmpty { finalized.append(text) }
            start = snap.concluded.count
            pending = nil
        } else {
            pending = pendingText.isEmpty ? nil : pendingText
        }
    }

    private func cap() {
        let max = Swift.max(1, style.maxVisibleSentences)
        while finalizedTarget.count > max { finalizedTarget.removeFirst() }
        while finalizedSource.count > max { finalizedSource.removeFirst() }
    }

    var visibleTarget: [(text: String, pending: Bool)] {
        var out: [(String, Bool)] = finalizedTarget.map { ($0, false) }
        if let p = pendingTarget { out.append((p, true)) }
        let cap = Swift.max(1, style.maxVisibleSentences)
        while out.count > cap { out.removeFirst() }
        return out
    }

    var sourceByIndex: [String] {
        var all = finalizedSource
        if let p = pendingSource { all.append(p) }
        return all
    }
}

// MARK: - SwiftUI OverlayView

struct OverlayView: View {
    @ObservedObject var vm: OverlayViewModel

    var body: some View {
        ZStack {
            hexColor(vm.style.panelBackgroundColor)
                .opacity(vm.style.panelBackgroundOpacity)
                .ignoresSafeArea()

            VStack(alignment: hAlignment, spacing: 6) {
                Spacer(minLength: 0)
                ForEach(Array(vm.visibleTarget.enumerated()), id: \.offset) { idx, entry in
                    let src: String? = (vm.showOriginal && idx < vm.sourceByIndex.count) ? vm.sourceByIndex[idx] : nil
                    BubbleView(target: entry.text, source: src, pending: entry.pending, style: vm.style)
                        .transition(.move(edge: .bottom).combined(with: .opacity))
                }
            }
            .padding(8)
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: frameAlignment)
            .animation(.easeOut(duration: 0.18), value: vm.visibleTarget.count)
        }
    }

    private var hAlignment: HorizontalAlignment {
        switch vm.style.textAlign {
        case .left:   return .leading
        case .center: return .center
        case .right:  return .trailing
        }
    }

    private var frameAlignment: Alignment {
        switch vm.style.textAlign {
        case .left:   return .bottomLeading
        case .center: return .bottom
        case .right:  return .bottomTrailing
        }
    }
}

private struct BubbleView: View {
    let target: String
    let source: String?
    let pending: Bool
    let style: OverlayStyle

    var body: some View {
        VStack(alignment: hAlign, spacing: 2) {
            Text(target)
                .font(.custom(style.fontFamily, size: CGFloat(style.fontSize)))
                .fontWeight(.init(rawWeight: style.fontWeight))
                .foregroundStyle(hexColor(style.textColor).opacity(pending ? 0.55 : 1.0))
                .shadow(color: hexColor(style.outlineColor), radius: CGFloat(style.outlineWidth))
                .multilineTextAlignment(tAlign)
                .lineLimit(style.maxLines)
                .lineSpacing(CGFloat(style.lineHeight - 1) * CGFloat(style.fontSize))
                .fixedSize(horizontal: false, vertical: true)
            if let source {
                Text(source)
                    .font(.custom(style.fontFamily, size: CGFloat(Double(style.fontSize) * 0.62)))
                    .foregroundStyle(hexColor(style.textColor).opacity(pending ? 0.40 : 0.72))
                    .multilineTextAlignment(tAlign)
                    .lineLimit(style.maxLines)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
        .padding(.horizontal, CGFloat(style.paddingX))
        .padding(.vertical, CGFloat(style.paddingY))
        .background(
            RoundedRectangle(cornerRadius: CGFloat(style.borderRadius))
                .fill(hexColor(style.backgroundColor).opacity(style.backgroundOpacity))
        )
    }

    private var hAlign: HorizontalAlignment {
        switch style.textAlign {
        case .left:   return .leading
        case .center: return .center
        case .right:  return .trailing
        }
    }

    private var tAlign: TextAlignment {
        switch style.textAlign {
        case .left:   return .leading
        case .center: return .center
        case .right:  return .trailing
        }
    }
}

// MARK: - Color / weight helpers

func hexColor(_ hex: String) -> Color {
    var s = hex
    if s.hasPrefix("#") { s.removeFirst() }
    var value: UInt64 = 0
    Scanner(string: s).scanHexInt64(&value)
    let r = Double((value >> 16) & 0xFF) / 255.0
    let g = Double((value >>  8) & 0xFF) / 255.0
    let b = Double((value >>  0) & 0xFF) / 255.0
    return Color(red: r, green: g, blue: b)
}

extension Font.Weight {
    init(rawWeight: Int) {
        switch rawWeight {
        case ..<200: self = .ultraLight
        case ..<300: self = .thin
        case ..<400: self = .light
        case ..<500: self = .regular
        case ..<600: self = .medium
        case ..<700: self = .semibold
        case ..<800: self = .bold
        case ..<900: self = .heavy
        default:     self = .black
        }
    }
}
