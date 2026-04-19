# VoiceBuddy

Real-time translated captions and translated-voice output for Windows, powered by the
[DeepL Voice API](https://developers.deepl.com/api-reference/voice).

Capture any audio source on your system — system output, a specific microphone, a game's
voice chat — and see translated subtitles appear in a transparent overlay, or pipe the
translated voice into OBS / Discord / Zoom as a virtual microphone.

---

## Features

- **Live captions** — translated subtitles in an always-on-top overlay. Each sentence
  renders in its own box; while DeepL is still refining it, the text shows dimmed
  ("tentative"), then crystallizes into full brightness when DeepL finalizes. A new
  pending box slides in below for the next utterance. Configurable cap on how many
  boxes stay on screen.
- **Voice translation** — DeepL streams back the translated voice as 16 kHz PCM.
  VoiceBuddy plays it through any WASAPI output device, including virtual cables
  (VB-CABLE) for OBS / Discord integration.
- **Any Windows audio source** — WASAPI loopback for system audio (game, browser,
  Discord call playback) or direct capture from any microphone. Single device picker
  lists both render (loopback) and capture endpoints.
- **Fully configurable overlay** — font family / size / weight / color, outline, text
  alignment, line height, per-bubble padding + radius, panel background color and
  opacity, max visible sentences, anchored or free-drag positioning, resize grips.
- **System tray** — minimize-to-tray, quick input-device switch, start / stop capture,
  lock / unlock overlay, quit. Context menu rebuilds on every open so it always
  reflects live state.
- **Debug console** — toggleable tab that logs every DeepL REST + WebSocket frame for
  troubleshooting. Audio chunks are opt-in (they come ~8× / second).
- **Settings persist** across sessions in `%AppData%\VoiceBuddy\settings.json`.
  Including overlay position, size, and free-drag coordinates per monitor.

---

## Requirements

- Windows 10 version 1809+ or Windows 11.
- A DeepL API key — the free tier (`api-free.deepl.com`) works.
  [Get one here](https://www.deepl.com/pro-api).
- .NET 10 SDK — only required to build from source. Prebuilt single-file releases
  bundle the runtime.
- *Optional:* [VB-CABLE](https://vb-audio.com/Cable/) if you want to use the
  translated voice as a virtual microphone in OBS / Discord / Zoom.

---

## Quick start

1. Download the latest release (or build from source — see below), run `VoiceBuddy.exe`.
2. Go to the **Settings** tab. Paste your DeepL API key. Set target language as a
   BCP 47 tag (`EN-US`, `DE`, `FR-CA`, …). Leave source language as `auto` unless you
   have a reason to pin it.
3. Go to the **Overview** tab. Pick an audio source from the **Device** dropdown
   (loopback devices are prefixed with a speaker icon, microphones with a mic icon).
4. Click **Start capture**. Talk or play audio through the chosen source.
5. Translated captions appear in the overlay.

### Enabling translated voice output

1. On the **Overview** tab, check **Voice — stream translated audio**.
2. Pick an output device. *Default output device* uses whatever Windows currently has
   set; pick a specific device to route the translated voice there.
3. Optionally pick a voice (female / male / auto). The voice availability depends on
   the target language.
4. Start capture. The translated voice streams out of the selected device alongside
   (or instead of) the captions.

### Using VoiceBuddy as a virtual microphone for OBS / Discord

1. Install [VB-CABLE](https://vb-audio.com/Cable/) (free, signed by VB-Audio).
2. In VoiceBuddy's **Output device** dropdown, pick *CABLE Input (VB-Audio Virtual Cable)*.
3. In OBS / Discord / Zoom, set the microphone input to *CABLE Output
   (VB-Audio Virtual Cable)*.
4. Talk in your real mic. VoiceBuddy captures it, DeepL translates + synthesizes, the
   translated voice streams into CABLE Input, and OBS / Discord receives it on
   CABLE Output as if it were your microphone.

---

## Build from source

```bash
git clone <repo>
cd VoiceBuddy
dotnet build
dotnet run --project src/VoiceBuddy.App
```

For a self-contained single-file release:

```bash
dotnet publish src/VoiceBuddy.App -c Release
```

Output lands in
`src/VoiceBuddy.App/bin/Release/net10.0-windows/win-x64/publish/VoiceBuddy.exe`.
The resulting exe is ~70 MB (includes the .NET runtime and NAudio native libs), runs
on any Windows 10 1809+ machine with no prerequisites.

Hot reload works during development:

```bash
dotnet watch --project src/VoiceBuddy.App
```

Most XAML / style / resource edits apply live; constructor or new-named-element
changes trigger a rude-edit prompt and a restart.

---

## Walkthrough of the UI

### Header
A brand mark, app title, and a live session chip on the right that turns green when
DeepL Voice is connected, red on error, muted when idle.

### Overview tab
Where the running controls live.

- **Audio source** — device picker, VU meter, captured-format readout, refresh,
  start / stop.
- **Translation output** — independent **Captions** and **Voice** toggles. Turning
  voice on reveals an output-device picker and voice (male / female / auto) selector,
  plus a hint on how to set up OBS / Discord routing with VB-CABLE.
- **DeepL Voice session** status line.
- **Fire test subtitle** — pushes a fake German → English pair into the overlay for
  styling tuning without burning session quota.

### Settings tab
DeepL credentials (target lang, source lang, host, API key) and subtitle behavior
toggles (`Show original text under subtitle`).

### Layout tab
All subtitle styling — font family, size, weight, alignment, line height, text color,
outline color + width, background color + opacity, padding X / Y, border radius, max
lines per bubble, **max visible sentences**, plus the overall **Panel background**
color + opacity (fills the entire overlay area behind the bubbles; set opacity > 0 if
you want a solid caption bar across the screen).

Preview surface renders a sample subtitle that updates live as you adjust the style.

Position controls: anchor (9 positions), offset X/Y, width, height. **Mode** readout
tells you whether the overlay is Anchored or Free; drag the overlay itself to flip to
Free; Reset to anchor puts it back.

### Debug tab
Toggle `Log API traffic` to start capturing. Shows a monospace console with every REST
+ WebSocket frame in and out of DeepL:

```
14:38:05.097  →  POST https://api.deepl.com/v3/voice/realtime  {"source_media_content_type":…}
14:38:05.471  ←  HTTP 200  {"streaming_url":"wss://…","token":"…","session_id":"…"}
14:38:05.472  →  ws connect  wss://api.deepl.com/v3/voice/realtime/connect
14:38:05.814  ←  ws open  api.deepl.com
14:38:07.096  ←  ws  {"source_transcript_update":{"concluded":[],"tentative":[{"text":" are","language":"en",…}]}}
```

Audio chunks (source_media_chunk, ~8× / second) are gated behind a separate checkbox
because they'd drown out everything else. Log is capped at 1000 entries and auto-scrolls.

### System tray
Minimize the main window and it disappears to the tray instead of sitting in the taskbar.
Right-click the tray icon for:

- Show VoiceBuddy (opens the main window)
- Start / Stop capture
- Input source submenu (every available device, current one checked)
- Lock overlay (click-through)
- Unlock & reposition overlay (unlocks + shows a balloon hint)
- Quit

Double-click (or left-click when hidden) reopens the main window.

---

## The overlay window

A transparent, borderless, always-on-top window. Two states:

- **Unlocked** — you can drag it anywhere on any monitor, resize with the edge or
  corner grips, and click the in-overlay **Lock** button in the top-right to flip back
  to locked. Drag / resize positions and dimensions persist.
- **Locked** — click-through (mouse events pass to whatever's underneath). Ideal for
  gaming, streaming, or any focused task.

Each incoming DeepL target utterance lives in one "bubble" — a rounded rectangle with
your configured styling. While DeepL is still streaming tentative words, the same
bubble updates in place with dimmer text. When DeepL finalizes the utterance, the
bubble switches to full brightness and a new dim pending bubble spawns below for the
next utterance. When the stack exceeds `Visible sentences`, the oldest box at the top
drops off.

With **Show original text** on, each bubble also shows the source text underneath at
62% size. Source and target are paired by index (usually 1:1 in DeepL Voice; when they
drift you may see slight misalignment).

Idle-fade: after 8 seconds with no updates, the overlay fades out and clears state, so
stale text doesn't linger.

---

## Architecture

```
WASAPI capture ──▶ AudioCaptureService ──▶ Resampler (→ 16 kHz mono) ──▶ DeepLVoiceService
                                                                             │
                        ┌─── source_transcript_update  ──▶ SubtitleBus ──▶ OverlayWindow
                        ├─── target_transcript_update  ──▶ SubtitleBus ──▶ OverlayWindow
                        └─── target_media_chunk        ──▶ VoiceOutPlayer ──▶ WasapiOut
```

Single-process WPF + NAudio. No sidecar processes, no IPC, no external services other
than DeepL's WebSocket. Components communicate through in-process events and a
`SubtitleBus` pub/sub for transcript snapshots.

### Transcript model

`TranscriptSnapshot { Lang, Concluded[], Tentative[] }` — the DeepL server sends
concluded segments as deltas (accumulated by the service) and tentative segments as
full replacements. The overlay maintains a bookmark per side; when tentative empties
and unfinalized concluded text exists, that span becomes a finalized sentence and the
bookmark advances. This lets the overlay show "the current utterance in progress"
(pending box) separately from completed utterances (concluded boxes).

---

## Project layout

```
VoiceBuddy/
├── VoiceBuddy.sln
└── src/
    └── VoiceBuddy.App/
        ├── App.xaml[.cs]                  root composition, Start/StopCapture helpers
        ├── Assets/                        logo PNG + app icon
        ├── app.manifest                   DPI awareness
        ├── Interop/
        │   └── WindowNative.cs            click-through + tool-window P/Invoke
        ├── Models/
        │   ├── Settings.cs                persisted config model
        │   ├── TranscriptSnapshot.cs      streaming transcript state
        │   └── AudioDevice.cs
        ├── Services/
        │   ├── AudioCaptureService.cs     WASAPI loopback / mic + downmix to mono
        │   ├── Resampler.cs               linear downsample to 16 kHz
        │   ├── DeepLVoiceService.cs       /v3/voice/realtime + WebSocket client
        │   ├── VoiceOutPlayer.cs          PCM playback via NAudio WasapiOut
        │   ├── SubtitleBus.cs             in-process pub/sub
        │   ├── SettingsStore.cs           JSON persistence under %AppData%
        │   ├── DebugLog.cs                bounded API-traffic log
        │   ├── TrayService.cs             NotifyIcon context menu
        │   └── FakeSubtitleFeed.cs        test-fire sample pairs
        └── Views/
            ├── MainWindow.xaml[.cs]       header + tabs (Overview/Settings/Layout/Debug)
            └── OverlayWindow.xaml[.cs]    transparent subtitle overlay
```

---

## Settings file

Path: `%AppData%\VoiceBuddy\settings.json`.

Contains overlay style, layout, position, DeepL credentials, audio device choice, and
output-mode toggles. The API key is stored in plain text — the file is protected only
by the OS-level permissions on your `%AppData%` folder.

Delete the file to reset to defaults.

**Logo drop-in:** drop a PNG at `%AppData%\VoiceBuddy\Assets\deepl-logo.png` (or
`src/VoiceBuddy.App/Assets/deepl-logo.png` when building from source) and the app
loads it as the header logo + taskbar icon on next launch.

---

## Known limitations

- **30-second DeepL inactivity timeout.** If no audio arrives for 30 seconds the
  server closes the session. VoiceBuddy catches the close cleanly and surfaces
  `DeepL Voice disconnected:` in the status chip — just hit Start capture again to
  reconnect. Silence-suppression to prevent the timeout is a planned improvement.
- **One-hour session cap** at the DeepL side. No auto-reconnect yet; restart capture.
- **Windows only.** The UI is WPF. macOS / Linux ports would be separate codebases.
- **Unsigned binaries.** Windows SmartScreen warns on first run ("More info → Run
  anyway"). An EV code-signing cert is the fix but costs money and paperwork.
- **No shipped virtual audio driver.** Virtual-mic routing relies on the user
  installing VB-CABLE (or BlackHole equivalents on other OSes — but there's no other
  OS anyway).
- **Source / target sentence alignment** when *Show original text* is on uses
  index-pairing, which is usually 1:1 but can drift if DeepL merges or splits segments
  differently between source and target.
- **API key in plaintext** in `settings.json`. File lives under your `%AppData%`, which
  is user-scoped by default, but don't commit the file.

---

## License

MIT.
