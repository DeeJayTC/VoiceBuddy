import AppKit
import SwiftUI

extension Notification.Name {
    static let voiceBuddyShowMain = Notification.Name("com.deepl.voicebuddy.showMain")
}

@MainActor
final class StatusItemService: NSObject, NSMenuDelegate {
    private let container: ServiceContainer
    private weak var overlay: OverlayController?
    private let statusItem: NSStatusItem
    private let menu = NSMenu()

    init(container: ServiceContainer, overlay: OverlayController) {
        self.container = container
        self.overlay = overlay
        self.statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        super.init()

        if let button = statusItem.button {
            button.image = NSImage(systemSymbolName: "text.bubble.fill", accessibilityDescription: "VoiceBuddy")
            button.toolTip = "VoiceBuddy"
        }
        menu.delegate = self
        statusItem.menu = menu
    }

    func menuWillOpen(_ menu: NSMenu) {
        rebuildMenu()
    }

    private func rebuildMenu() {
        menu.removeAllItems()

        addItem(title: "Show VoiceBuddy", action: #selector(showMain))
        menu.addItem(.separator())

        addItem(
            title: container.isCapturing ? "Stop capture" : "Start capture",
            action: #selector(toggleCapture)
        )
        menu.addItem(.separator())

        let lockTitle = container.settings.current.overlayLayout.locked
            ? "Unlock overlay & reposition"
            : "Lock overlay (click-through)"
        addItem(title: lockTitle, action: #selector(toggleLock))

        menu.addItem(.separator())
        let quit = NSMenuItem(title: "Quit VoiceBuddy", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        menu.addItem(quit)
    }

    private func addItem(title: String, action: Selector) {
        let item = NSMenuItem(title: title, action: action, keyEquivalent: "")
        item.target = self
        menu.addItem(item)
    }

    @objc private func showMain() { openMainWindow() }

    private func openMainWindow() {
        NSApp.activate(ignoringOtherApps: true)
        for w in NSApp.windows where !(w is NSPanel) && w.canBecomeMain {
            w.makeKeyAndOrderFront(nil)
            return
        }
        // WindowGroup not yet materialized — notify SwiftUI to reopen via its
        // registered handler (see VoiceBuddyApp).
        NotificationCenter.default.post(name: .voiceBuddyShowMain, object: nil)
    }

    @objc private func toggleCapture() {
        Task {
            if container.isCapturing {
                await container.stopCapture()
            } else {
                await container.startCapture()
            }
        }
    }

    @objc private func toggleLock() {
        container.settings.update { $0.overlayLayout.locked.toggle() }
        overlay?.applyLock()
    }
}

