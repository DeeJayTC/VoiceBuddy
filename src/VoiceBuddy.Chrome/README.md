# VoiceBuddy.Chrome

Chrome/Edge Manifest V3 companion to the desktop VoiceBuddy app. Captures the
**active tab's audio** and streams it to DeepL Voice Realtime for live
translated subtitles overlaid on the page. Works on YouTube, Twitch, Google
Meet, Teams web, and anything else that plays audio in a tab.

## Scope vs. the desktop app

Desktop app handles system-wide and per-app audio plus virtual-mic output.
The extension is deliberately narrower: it only sees audio playing in the
current tab and only renders subtitles in that tab. Browser sandbox limits
prevent capturing audio from other apps or exposing a virtual microphone.

## Architecture

```
popup  ──START──▶ service worker (background.js)
                      │
                      ├── chrome.tabCapture.getMediaStreamId(tab)
                      ├── chrome.offscreen.createDocument(...)
                      ├── chrome.scripting.executeScript(content.js)
                      │
                      ▼
               offscreen.html (offscreen.js)
                      │
                      │ getUserMedia(chromeMediaSourceId)
                      │    │
                      │    ├──▶ AudioContext.destination  (passthrough so the user still hears the tab)
                      │    └──▶ AudioWorklet (downmix) ──▶ DeepLVoiceClient ── wss://…
                      │
                      ▼ transcript snapshots
                 service worker ──▶ content.js ──▶ overlay <div>
```

### Wire-format parity with the desktop client

`lib/deepl-voice.js` is a straight port of
`src/VoiceBuddy.App/Services/DeepLVoiceService.cs` — same REST handshake
(`POST /v3/voice/realtime`), same `source_media_chunk` / `target_transcript_update`
envelope, same 120 ms / 16 kHz / mono / s16le chunking. If the C# client
works, this one works.

## Install (unpacked, dev)

1. Open `chrome://extensions` (or `edge://extensions`).
2. Enable **Developer mode**.
3. Click **Load unpacked** and select this folder (`src/VoiceBuddy.Chrome`).
4. Click the VoiceBuddy toolbar icon → **Settings**, paste your DeepL API key.
5. Go to a tab with audio (YouTube, Twitch, Meet, …). Open the popup, pick
   target language, click **Start**. Subtitles appear over the page.

## Known limits

- `chrome://…`, the Chrome Web Store, and the built-in PDF viewer reject
  content-script injection; the overlay won't render there. The translation
  still runs but you won't see anything.
- The first session on a page needs a user gesture (the popup click is that
  gesture) — so you can't auto-start on load.
- Fullscreen is handled by reparenting the overlay to `document.fullscreenElement`.
  Sites that use a custom non-fullscreen "theater" mode render the overlay
  behind their own chrome; YouTube and Twitch native fullscreen are fine.
- DeepL Voice Realtime has a 30 s inactivity timeout — a quiet tab will drop
  the session. Currently we surface that as a status message; reconnect is
  manual.

## Files

- `manifest.json` — MV3 permissions (tabCapture, offscreen, storage, scripting)
- `background.js` — service worker, session orchestration
- `offscreen.html` / `offscreen.js` — audio pipeline + DeepL WebSocket owner
- `worklet.js` — AudioWorkletProcessor that downmixes stereo → mono
- `content.js` / `content.css` — overlay renderer, fullscreen-aware
- `popup.html` / `popup.js` — start/stop + language pickers
- `options.html` / `options.js` — API key + host
- `lib/deepl-voice.js` — DeepL Voice Realtime client
- `lib/resampler.js` — linear resampler (port of `Resampler.cs`)
