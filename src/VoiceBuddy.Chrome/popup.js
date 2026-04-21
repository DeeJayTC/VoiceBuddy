const $ = (id) => document.getElementById(id);
const sourceSel = $('sourceLang');
const targetSel = $('targetLang');
const captionsEl = $('captionsEnabled');
const showOrigEl = $('showOriginal');
const showOrigWrap = $('showOriginalWrap');
const voiceOutEl = $('voiceOutEnabled');
const toggleBtn = $('toggle');
const statusEl = $('status');

async function load() {
  const stored = await chrome.storage.local.get([
    'sourceLang', 'targetLang', 'showOriginalText', 'captionsEnabled', 'voiceOutEnabled',
  ]);
  sourceSel.value = stored.sourceLang ?? 'auto';
  targetSel.value = stored.targetLang ?? 'EN-US';
  captionsEl.checked = stored.captionsEnabled ?? true;
  showOrigEl.checked = !!stored.showOriginalText;
  voiceOutEl.checked = !!stored.voiceOutEnabled;
  syncShowOriginalVisibility();

  const state = await chrome.runtime.sendMessage({ target: 'service-worker', type: 'STATE' });
  setRunning(!!state?.running);
}

function setRunning(running) {
  toggleBtn.textContent = running ? 'Stop' : 'Start';
  toggleBtn.classList.toggle('stop', running);
}

function syncShowOriginalVisibility() {
  showOrigWrap.classList.toggle('hidden', !captionsEl.checked);
}

async function persist() {
  await chrome.storage.local.set({
    sourceLang: sourceSel.value,
    targetLang: targetSel.value,
    captionsEnabled: captionsEl.checked,
    showOriginalText: showOrigEl.checked,
    voiceOutEnabled: voiceOutEl.checked,
  });
}

[sourceSel, targetSel].forEach((el) => el.addEventListener('change', persist));

captionsEl.addEventListener('change', async () => {
  syncShowOriginalVisibility();
  await persist();
});

showOrigEl.addEventListener('change', async () => {
  await persist();
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  if (tab?.id) {
    chrome.tabs.sendMessage(tab.id, { target: 'content', type: 'SHOW_ORIGINAL', value: showOrigEl.checked }).catch(() => { });
  }
});

voiceOutEl.addEventListener('change', persist);

toggleBtn.addEventListener('click', async () => {
  await persist();
  const state = await chrome.runtime.sendMessage({ target: 'service-worker', type: 'STATE' });
  if (state?.running) {
    statusEl.textContent = 'Stopping…';
    const r = await chrome.runtime.sendMessage({ target: 'service-worker', type: 'STOP' });
    statusEl.textContent = r?.ok ? '' : (r?.error ?? 'Failed to stop');
    setRunning(false);
  } else {
    if (!captionsEl.checked && !voiceOutEl.checked) {
      statusEl.textContent = 'Turn on captions or translated audio first.';
      return;
    }
    statusEl.textContent = 'Starting…';
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    const r = await chrome.runtime.sendMessage({ target: 'service-worker', type: 'START', tabId: tab?.id });
    if (r?.ok) {
      setRunning(true);
      statusEl.textContent = 'Running';
      if (tab?.id && captionsEl.checked) {
        chrome.tabs.sendMessage(tab.id, { target: 'content', type: 'SHOW_ORIGINAL', value: showOrigEl.checked }).catch(() => { });
      }
    } else {
      statusEl.textContent = r?.error ?? 'Failed to start';
    }
  }
});

chrome.runtime.onMessage.addListener((msg) => {
  if (msg?.target !== 'popup') return;
  if (msg.type === 'STATUS') statusEl.textContent = msg.text;
});

$('openOptions').addEventListener('click', () => chrome.runtime.openOptionsPage());

load();
