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
  lock / unlock overlay, quit.
- **Debug console** — toggleable log of every DeepL REST + WebSocket frame for
  troubleshooting (see below).
- **Settings persist** across sessions in `%AppData%\VoiceBuddy\settings.json`.

---

## Requirements

- Windows 10 version 1809+ or Windows 11.
- A **DeepL API Pro** subscription — DeepL Voice is not currently available on the
  Free tier. [Sign up here](https://www.deepl.com/pro-api).
- .NET 10 SDK — only required to build from source. Prebuilt single-file releases
  bundle the runtime.
- *Optional:* [VB-CABLE](https://vb-audio.com/Cable/) if you want to use the
  translated voice as a virtual microphone in OBS / Discord / Zoom.

---

## Quick start

1. Download the latest release (or build from source — see below), run `VoiceBuddy.exe`.
2. Go to the **Settings** tab. Paste your DeepL API **Pro** key. Set target language
   as a BCP 47 tag (`EN-US`, `DE`, `FR-CA`, …). Leave source language as `auto` unless
   you have a reason to pin it.
3. Go to the **Overview** tab. Pick an audio source from the **Device** dropdown
   (loopback devices are prefixed with a speaker icon, microphones with a mic icon).
4. Click **Start capture**. Talk or play audio through the chosen source.
5. Translated captions appear in the overlay.

### Enabling translated voice output

1. On the **Overview** tab, check **Voice — stream translated audio**.
2. Pick an output device.
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

Hot reload during development:

```bash
dotnet watch --project src/VoiceBuddy.App
```

---

## Debug console

A dedicated **Debug** tab logs every DeepL REST + WebSocket frame for troubleshooting.
Sample output:

```
14:38:05.097  →  POST https://api.deepl.com/v3/voice/realtime  {"source_media_content_type":…}
14:38:05.471  ←  HTTP 200  {"streaming_url":"wss://…","token":"…","session_id":"…"}
14:38:05.472  →  ws connect  wss://api.deepl.com/v3/voice/realtime/connect
14:38:05.814  ←  ws open  api.deepl.com
14:38:07.096  ←  ws  {"source_transcript_update":{"concluded":[],"tentative":[{"text":" are",…}]}}
```

Log is off by default (zero cost when disabled), capped at 1000 entries, auto-scrolls.
Audio chunks (`source_media_chunk`, ~8× / second) are gated behind a separate checkbox
because they would drown out everything else.

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

- **DeepL Pro required.** DeepL Voice is not available on the Free tier at this time.
  The Free host option remains in the UI dropdown for future compatibility, but
  sessions against `api-free.deepl.com` will fail today.
- **30-second DeepL inactivity timeout.** If no audio arrives for 30 seconds the
  server closes the session. VoiceBuddy catches the close cleanly and surfaces
  `DeepL Voice disconnected:` in the status chip — just hit Start capture again to
  reconnect. Silence-suppression to prevent the timeout is a planned improvement.
- **One-hour session cap** at the DeepL side. No auto-reconnect yet; restart capture.
- **Windows only.** The UI is WPF. macOS / Linux ports would be separate codebases.
- **Unsigned binaries.** Windows SmartScreen warns on first run ("More info → Run
  anyway"). An EV code-signing cert is the fix but costs money and paperwork.
- **No shipped virtual audio driver.** Virtual-mic routing relies on the user
  installing VB-CABLE (or an equivalent).
- **Source / target sentence alignment** when *Show original text* is on uses
  index-pairing, which is usually 1:1 but can drift if DeepL merges or splits segments
  differently between source and target.
- **API key in plaintext** in `settings.json`. File lives under your `%AppData%`, which
  is user-scoped by default, but don't commit the file.

---

## License

MIT.
