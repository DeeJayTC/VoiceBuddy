# VoiceBuddy — macOS

Native Swift/SwiftUI port of the Windows WPF VoiceBuddy app.

Same feature set:

- Live translated captions in a transparent, always-on-top, click-through overlay.
- Optional translated-voice output routed to any output device (use BlackHole to feed
  Zoom/Discord/OBS as a virtual mic).
- Capture from any microphone or from system / per-app audio (ScreenCaptureKit).
- Menu-bar agent (no Dock icon), context-menu quick controls.
- Debug console of every DeepL REST + WebSocket frame.
- Settings persist in `~/Library/Application Support/VoiceBuddy/settings.json`
  (same schema as the Windows build).

## Requirements

- macOS **14.0** or newer.
- **DeepL API Pro** subscription (DeepL Voice is not on the Free tier).
- Xcode 15+.
- [xcodegen](https://github.com/yonaskolb/XcodeGen): `brew install xcodegen`.
- *Optional:* [BlackHole](https://existential.audio/blackhole/) for virtual-mic routing.

## Build

```bash
cd src/mac/VoiceBuddy
xcodegen generate
open VoiceBuddy.xcodeproj
# ⌘R to run
```

From the command line:

```bash
xcodebuild -project VoiceBuddy.xcodeproj -scheme VoiceBuddy -configuration Release \
           -derivedDataPath build
open build/Build/Products/Release/VoiceBuddy.app
```

## First-run permissions

macOS will prompt for:

1. **Microphone** — needed if you capture a mic input.
2. **Screen Recording** — needed for system / per-app audio (ScreenCaptureKit). No
   video is recorded; the stream is configured for 2×2 pixels so the video track
   is a no-op.

Both prompts come from the OS the first time capture starts; grant them in
`System Settings → Privacy & Security`, then restart VoiceBuddy.

## Virtual microphone (Zoom / Discord / OBS)

1. Install BlackHole (free, signed).
2. In VoiceBuddy → Overview → *Output device*, pick **BlackHole 2ch**.
3. In Zoom / Discord / OBS, set microphone to **BlackHole 2ch**.

## Settings file

`~/Library/Application Support/VoiceBuddy/settings.json`

Shares the JSON schema with the Windows build. API key is stored in plaintext
in this file — the directory is user-scoped by default; don't commit it.

## Layout

```
VoiceBuddy/
├── VoiceBuddyApp.swift            composition root (≙ App.xaml.cs)
├── Info.plist                     LSUIElement, mic/screen-capture permissions
├── VoiceBuddy.entitlements        non-sandboxed, mic + network
├── Models/
│   ├── Settings.swift             Codable, same JSON shape as Windows
│   ├── TranscriptSnapshot.swift   concluded + tentative segments
│   └── AudioDevice.swift
├── Services/
│   ├── AudioCaptureService.swift  AVAudioEngine mic + SCStream system audio
│   ├── Resampler.swift            linear resample to 16 kHz
│   ├── DeepLVoiceService.swift    /v3/voice/realtime REST + WebSocket
│   ├── VoiceOutPlayer.swift       AVAudioEngine + AVAudioPlayerNode sink
│   ├── SubtitleBus.swift          Combine pub/sub
│   ├── SettingsStore.swift        JSON persistence under Application Support
│   ├── DebugLog.swift             bounded API-traffic log
│   ├── StatusItemService.swift    NSStatusBar menu (≙ TrayService)
│   └── FakeSubtitleFeed.swift     sample DE↔EN pairs for UI tuning
└── Views/
    ├── MainWindow.swift           SwiftUI tabs (Overview/Settings/Style/Layout/Debug)
    └── OverlayView.swift          NSPanel + SwiftUI transcript rendering
```

## Known limitations

- **Screen Recording permission required** for system audio (same story as Windows
  needing loopback); falls back to mic-only cleanly if denied.
- **DeepL Pro required** — Voice API not on Free tier.
- **No virtual audio driver shipped** — user installs BlackHole (same story as
  VB-CABLE on Windows).
- **Unsigned binaries** out of the box — Gatekeeper will warn on first launch
  until the app is notarized.
