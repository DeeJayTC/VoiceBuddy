// Service worker — orchestrates a session:
//   popup START → ensure offscreen doc → grab tabCapture stream id → tell
//   offscreen to begin → inject content script into the target tab → forward
//   transcript snapshots to the content script.
//
// We keep per-tab state in memory; the worker can be killed between events,
// but a live session keeps it alive via the pending message port.

const OFFSCREEN_URL = 'offscreen.html';
let activeTabId = null;
let captionsActive = false; // whether the current session is rendering captions

async function hasOffscreen() {
  // chrome.offscreen.hasDocument exists on Chrome 116+. getContexts is the
  // fallback that works everywhere MV3 offscreen is available.
  if (typeof chrome.offscreen?.hasDocument === 'function') {
    return chrome.offscreen.hasDocument();
  }
  const contexts = await chrome.runtime.getContexts({ contextTypes: ['OFFSCREEN_DOCUMENT'] });
  return contexts.length > 0;
}

async function ensureOffscreen() {
  if (await hasOffscreen()) return;
  await chrome.offscreen.createDocument({
    url: OFFSCREEN_URL,
    reasons: ['USER_MEDIA'],
    justification: 'Capture tab audio and stream to DeepL Voice for real-time translation.',
  });
}

async function closeOffscreen() {
  if (await hasOffscreen()) {
    try { await chrome.offscreen.closeDocument(); } catch { }
  }
}

chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  if (msg?.target === 'background') {
    if (msg.type === 'STATUS') {
      relay({ type: 'STATUS', text: msg.text });
    } else if (msg.type === 'TRANSCRIPT') {
      if (captionsActive && activeTabId != null) {
        chrome.tabs.sendMessage(activeTabId, {
          target: 'content',
          type: 'TRANSCRIPT',
          which: msg.which,
          snapshot: msg.snapshot,
        }).catch(() => { });
      }
      relay({ type: 'TRANSCRIPT', which: msg.which, snapshot: msg.snapshot });
    }
    return;
  }

  if (msg?.target !== 'service-worker') return;

  (async () => {
    try {
      if (msg.type === 'START') {
        const tabId = msg.tabId ?? (await getActiveTabId());
        const {
          deepLApiKey = '', deepLApiHost = 'api-free.deepl.com',
          sourceLang = 'auto', targetLang = 'EN-US',
          captionsEnabled = true, voiceOutEnabled = false,
        } = await chrome.storage.local.get(
          ['deepLApiKey', 'deepLApiHost', 'sourceLang', 'targetLang',
           'captionsEnabled', 'voiceOutEnabled']);
        if (!deepLApiKey) throw new Error('Set your DeepL API key in extension options first.');
        if (!captionsEnabled && !voiceOutEnabled) {
          throw new Error('Enable captions or translated audio before starting.');
        }

        await ensureOffscreen();
        const streamId = await new Promise((resolve, reject) => {
          chrome.tabCapture.getMediaStreamId({ targetTabId: tabId }, (id) => {
            if (chrome.runtime.lastError || !id) {
              reject(new Error(chrome.runtime.lastError?.message || 'no stream id'));
            } else resolve(id);
          });
        });

        if (captionsEnabled) await injectContent(tabId);

        const res = await chrome.runtime.sendMessage({
          target: 'offscreen',
          type: 'START',
          payload: {
            streamId, host: deepLApiHost, apiKey: deepLApiKey,
            sourceLang, targetLang, voiceOut: voiceOutEnabled,
          },
        });
        if (!res?.ok) throw new Error(res?.error || 'offscreen start failed');

        activeTabId = tabId;
        captionsActive = captionsEnabled;
        await chrome.action.setBadgeText({ text: 'ON' });
        await chrome.action.setBadgeBackgroundColor({ color: '#0f7b4d' });
        sendResponse({ ok: true });
      } else if (msg.type === 'STOP') {
        await chrome.runtime.sendMessage({ target: 'offscreen', type: 'STOP' }).catch(() => { });
        await closeOffscreen();
        if (activeTabId != null && captionsActive) {
          chrome.tabs.sendMessage(activeTabId, { target: 'content', type: 'CLEAR' }).catch(() => { });
        }
        activeTabId = null;
        captionsActive = false;
        await chrome.action.setBadgeText({ text: '' });
        sendResponse({ ok: true });
      } else if (msg.type === 'STATE') {
        sendResponse({ ok: true, running: activeTabId != null, activeTabId });
      }
    } catch (e) {
      sendResponse({ ok: false, error: String(e?.message ?? e) });
    }
  })();
  return true;
});

async function getActiveTabId() {
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  if (!tab?.id) throw new Error('No active tab');
  return tab.id;
}

async function injectContent(tabId) {
  try {
    await chrome.scripting.insertCSS({ target: { tabId }, files: ['content.css'] });
  } catch { }
  try {
    await chrome.scripting.executeScript({ target: { tabId }, files: ['content.js'] });
  } catch (e) {
    throw new Error(`Couldn't inject into this page (chrome://, web store, and PDF viewers are off-limits): ${e.message}`);
  }
}

function relay(msg) {
  // Best-effort forward to the popup if it happens to be open.
  chrome.runtime.sendMessage({ target: 'popup', ...msg }).catch(() => { });
}

chrome.tabs.onRemoved.addListener(async (tabId) => {
  if (tabId === activeTabId) {
    await chrome.runtime.sendMessage({ target: 'offscreen', type: 'STOP' }).catch(() => { });
    await closeOffscreen();
    activeTabId = null;
    captionsActive = false;
    await chrome.action.setBadgeText({ text: '' });
  }
});
