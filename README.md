# DeepL Voice Buddy

Real-time translated captions and voiceover, powered by the
[DeepL Voice API](https://developers.deepl.com/api-reference/voice).

Watch foreign-language videos with captions in your own language. Join a call
in German and read along in English. Speak into your mic and let other apps
hear your translated voice. DeepL Voice Buddy runs on Windows, macOS, and any
Chromium-based browser.

## Three ways to run it

| Client | What it hears | What it shows |
| --- | --- | --- |
| **Windows app** | Any system audio, any app, any mic, game voice chat | On-screen overlay, and optional translated voice out to any audio device (including virtual cables for OBS / Discord / Zoom) |
| **macOS app** | Any microphone, system audio via ScreenCaptureKit | Menu-bar overlay, and optional translated voice out to any output device (works with BlackHole for virtual mic routing) |
| **Chrome extension** | Audio from the active browser tab | Subtitle overlay anchored to the video element, and optional translated audio played back through the tab |

All three share a settings format and route through the same DeepL Voice
Realtime session protocol, so behaviour is consistent across platforms.

## Features

* Live captions that update as DeepL transcribes. Unfinished text shows dimmed
  while the model is still deciding; it sharpens into full brightness once a
  segment is finalised.
* Optional translated voiceover. DeepL streams the translated voice as
  audio; the app plays it through any output device you pick.
* Bring-your-own DeepL Pro key. Nothing is proxied through third parties,
  your audio goes directly to DeepL.
* Per-platform overlay styling: font, colour, size, background opacity,
  anchor point, max visible lines, click-through.
* Debug consoles that log every REST and WebSocket frame for easy
  troubleshooting (desktop apps).

## Getting started

You need a **DeepL API Pro** subscription. The Voice API is not currently on
the Free tier. Grab a key from [deepl.com/pro-api](https://www.deepl.com/pro-api).

### Windows

Two install options:

**Via 0install (recommended — auto-updates, smaller downloads):**

1. Download `VoiceBuddy-Setup.exe` from the
   [latest release](https://github.com/DeeJayTC/VoiceBuddy/releases/latest)
   and run it. It installs [Zero Install](https://0install.net/) and subscribes
   to the VoiceBuddy feed.
2. If you already have 0install, skip the bootstrap and just run:
   `0install run https://deejaytc.github.io/VoiceBuddy/voice-buddy.xml`

0install pulls in the .NET 10 Desktop Runtime as a shared dependency, so
app updates stay small and don't re-bundle the runtime each time.

**Manual download:**

1. Download the latest `voice-buddy-<version>-win-x64.zip` from the releases
   page, unzip anywhere, and run `VoiceBuddy.exe`.
   Requires the
   [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
   to be installed.

Once running:

1. Open the **Settings** tab and paste your DeepL API key.
2. On the **Overview** tab, pick an audio source from the **Device** dropdown.
   Loopback devices (system audio) show a speaker icon, microphones show a
   mic icon.
3. Click **Start capture** and play some audio through the selected source.
   Captions appear in the overlay.

Optional: check **Voice: stream translated audio** to also play the
translated voice. Pick an output device and, if you like, a voice gender.

### macOS

1. Download the latest `VoiceBuddy.app` from the releases page and move it
   to Applications.
2. On first launch, macOS will ask for **Microphone** and **Screen Recording**
   permissions. Screen Recording is how ScreenCaptureKit exposes system
   audio; no video is actually recorded. Grant both in
   **System Settings → Privacy & Security**.
3. Open **Settings** and paste your DeepL API key.
4. On the **Overview** tab, pick an input device and click **Start capture**.

The menu-bar icon gives you quick access to start, stop, and lock the
overlay.

### Chrome / Edge / Brave

1. Clone this repo (or download the latest release zip).
2. Open `chrome://extensions`, enable **Developer mode**, and click
   **Load unpacked**. Point it at `src/VoiceBuddy.Chrome/`.
3. Click the DeepL Voice Buddy icon in your toolbar, open **Settings**, and
   paste your DeepL API key.
4. Go to a tab with audio (YouTube, Twitch, Google Meet, Microsoft Teams web,
   a podcast, anything). Open the popup, choose the output language, and
   click **Start**.

Captions appear overlaid on the video. You can also enable
**Play translated audio** to have the tab speak in your chosen language
instead of the original. The extension follows your system's light/dark mode.

## Virtual microphone (OBS, Discord, Zoom)

On the desktop clients you can route the translated voice into other apps as
if it were a microphone. This needs a virtual audio cable:

* **Windows**: install [VB-CABLE](https://vb-audio.com/Cable/) (free, signed
  by VB-Audio).
  1. In DeepL Voice Buddy, pick **CABLE Input (VB-Audio Virtual Cable)** as
     the output device.
  2. In OBS / Discord / Zoom, set the microphone to **CABLE Output**.
* **macOS**: install [BlackHole 2ch](https://existential.audio/blackhole/)
  (free, signed).
  1. In DeepL Voice Buddy, pick **BlackHole 2ch** as the output device.
  2. In OBS / Zoom / Discord, set the microphone to **BlackHole 2ch**.

Speak into your real mic. DeepL translates, the translated voice streams
into the virtual cable, and other apps receive it as your microphone input.

The Chrome extension cannot expose a virtual microphone. Browsers do not
have that capability.

## Settings

| Platform | Location |
| --- | --- |
| Windows | `%AppData%\VoiceBuddy\settings.json` |
| macOS | `~/Library/Application Support/VoiceBuddy/settings.json` |
| Chrome | Browser-managed `chrome.storage.local` (per profile) |

Desktop settings files share the same JSON schema, so you can copy one
across machines. API keys are stored in plaintext inside a user-scoped
directory; don't commit the file.

Delete the file (or clear extension storage) to reset to defaults.

## System requirements

* Windows 10 version 1809 or newer, or Windows 11.
* macOS 14.0 or newer.
* For the extension: any Chromium-based browser from 2024 onwards (Chrome,
  Edge, Brave, Arc, Opera).
* An active DeepL API Pro subscription.

## Building from source

The three clients are independent projects:

* Windows: see the project files under `src/VoiceBuddy.App/`. Requires the
  .NET 10 SDK. Build with `dotnet build`, run with
  `dotnet run --project src/VoiceBuddy.App`.
* macOS: see `src/mac/VoiceBuddy/README.md`. Requires Xcode 15+ and
  xcodegen.
* Chrome: see `src/VoiceBuddy.Chrome/README.md`. Load unpacked from the
  folder. No build step, no bundler.

## Known limitations

* **DeepL Pro is required.** The Voice API is not on the Free tier today.
  The Free host option remains in the UI so it will keep working if DeepL
  ever adds Free-tier access.
* **30-second inactivity timeout.** If no audio reaches DeepL for 30
  seconds the server closes the session. The desktop apps surface this as
  a status message; restart capture to reconnect.
* **1-hour session cap** on the DeepL side. There is no auto-reconnect
  yet; restart capture.
* **Unsigned binaries** on both Windows and macOS until the projects are
  code-signed and notarised. Windows SmartScreen and macOS Gatekeeper will
  warn on first launch.
* **Chrome extension cannot capture non-browser audio.** It only sees the
  active tab.
* **No shipped virtual audio driver.** Virtual-mic routing relies on
  VB-CABLE or BlackHole, which the user installs.

## License

MIT.
