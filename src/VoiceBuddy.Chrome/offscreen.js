// Offscreen document — owns the actual audio pipeline and the DeepL WebSocket.
// The service worker can't run MediaStream / WebAudio code, so it delegates
// here via chrome.runtime messages.
//
// Lifecycle per session:
//   background → START {streamId, host, apiKey, sourceLang, targetLang, voiceOut, ...}
//     → we getUserMedia(tab-capture) → AudioContext + AudioWorklet → DeepLVoiceClient
//     → passthrough keeps the tab audible unless voiceOut is on, in which case
//       the original is muted and the translated audio plays instead.
//   background → STOP → tear everything down.
//
// Transcript updates go back out as runtime messages; background forwards to
// the content script for overlay rendering (when captions are enabled).

import { DeepLVoiceClient } from './lib/deepl-voice.js';
import { VoiceOutPlayer } from './lib/voice-out-player.js';

let audioCtx = null;
let mediaStream = null;
let sourceNode = null;
let workletNode = null;
let passthroughNode = null;
let deepl = null;
let voiceOut = null;

chrome.runtime.onMessage.addListener((msg, _sender, sendResponse) => {
  if (msg?.target !== 'offscreen') return;
  (async () => {
    try {
      if (msg.type === 'START') {
        await start(msg.payload);
        sendResponse({ ok: true });
      } else if (msg.type === 'STOP') {
        await stop();
        sendResponse({ ok: true });
      }
    } catch (e) {
      sendResponse({ ok: false, error: String(e?.message ?? e) });
    }
  })();
  return true;
});

async function start({ streamId, host, apiKey, sourceLang, targetLang,
                        voiceOut: wantVoiceOut = false, voiceGender = '' }) {
  await stop();

  mediaStream = await navigator.mediaDevices.getUserMedia({
    audio: {
      mandatory: {
        chromeMediaSource: 'tab',
        chromeMediaSourceId: streamId,
      },
    },
    video: false,
  });

  audioCtx = new AudioContext();
  await audioCtx.audioWorklet.addModule(chrome.runtime.getURL('worklet.js'));

  sourceNode = audioCtx.createMediaStreamSource(mediaStream);
  workletNode = new AudioWorkletNode(audioCtx, 'downmix');

  if (wantVoiceOut) voiceOut = new VoiceOutPlayer(audioCtx);

  deepl = new DeepLVoiceClient({
    onStatus: (text) => send({ type: 'STATUS', text }),
    onSource: (snap) => send({ type: 'TRANSCRIPT', which: 'source', snapshot: snap }),
    onTarget: (snap) => send({ type: 'TRANSCRIPT', which: 'target', snapshot: snap }),
    onTargetMedia: (buf) => voiceOut?.enqueuePcm(buf),
  });

  const ok = await deepl.start({ host, apiKey, sourceLang, targetLang, wantVoiceOut, voiceGender });
  if (!ok) {
    await stop();
    throw new Error('DeepL start failed');
  }

  workletNode.port.onmessage = (ev) => {
    const { samples, sampleRate } = ev.data;
    deepl.addFrame(samples, sampleRate);
  };

  sourceNode.connect(workletNode);

  // Passthrough branch: tabCapture mutes the tab by default; route audio back
  // so the user still hears the page. When voiceOut is on we squelch this
  // branch — otherwise the original and the translation overlap.
  passthroughNode = audioCtx.createGain();
  passthroughNode.gain.value = wantVoiceOut ? 0 : 1;
  sourceNode.connect(passthroughNode).connect(audioCtx.destination);
}

async function stop() {
  try { deepl?.stop(); } catch { }
  deepl = null;
  try { voiceOut?.stop(); } catch { }
  voiceOut = null;

  try { workletNode?.disconnect(); } catch { }
  try { passthroughNode?.disconnect(); } catch { }
  try { sourceNode?.disconnect(); } catch { }
  workletNode = null;
  passthroughNode = null;
  sourceNode = null;

  if (mediaStream) {
    for (const t of mediaStream.getTracks()) { try { t.stop(); } catch { } }
    mediaStream = null;
  }
  if (audioCtx) {
    try { await audioCtx.close(); } catch { }
    audioCtx = null;
  }
}

function send(msg) {
  chrome.runtime.sendMessage({ target: 'background', ...msg }).catch(() => { });
}
