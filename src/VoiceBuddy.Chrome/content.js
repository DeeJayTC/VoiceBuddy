// Content script — renders subtitle snapshots into a fixed overlay that
// tracks the page's primary <video> element (or the viewport in page mode).
// Re-injection-safe: bails if already installed on this page.
(() => {
  if (window.__voicebuddyInstalled) return;
  window.__voicebuddyInstalled = true;

  const DEFAULTS = {
    captionAnchor: 'video',      // 'video' | 'page'
    captionPosition: 'bottom',   // 'bottom' | 'top'
    captionOffset: 60,           // px inset from anchor edge
    captionFontSize: 28,         // px
    captionBgOpacity: 0.55,      // 0–1
    captionMaxWidthPct: 90,      // % of anchor width
    showOriginalText: false,
  };
  let settings = { ...DEFAULTS };

  const root = document.createElement('div');
  root.id = 'voicebuddy-overlay-root';
  const sourceLine = document.createElement('div');
  sourceLine.className = 'vb-line vb-source';
  sourceLine.style.display = 'none';
  const targetLine = document.createElement('div');
  targetLine.className = 'vb-line vb-target';
  root.append(sourceLine, targetLine);

  function mount() {
    const host = document.fullscreenElement || document.body;
    if (root.parentElement !== host) host.appendChild(root);
  }
  mount();
  document.addEventListener('fullscreenchange', mount);

  // ---- settings plumbing ----------------------------------------------

  chrome.storage.local.get(Object.keys(DEFAULTS)).then((stored) => {
    settings = { ...DEFAULTS, ...stored };
    applyStyleSettings();
  });

  chrome.storage.onChanged.addListener((changes, area) => {
    if (area !== 'local') return;
    for (const [key, { newValue }] of Object.entries(changes)) {
      if (key in DEFAULTS) settings[key] = newValue ?? DEFAULTS[key];
    }
    applyStyleSettings();
    if ('showOriginalText' in changes && !settings.showOriginalText) {
      sourceLine.style.display = 'none';
    }
  });

  function applyStyleSettings() {
    root.style.setProperty('--vb-font-size', `${settings.captionFontSize}px`);
    root.style.setProperty('--vb-bg-opacity', String(settings.captionBgOpacity));
  }

  // ---- positioning ----------------------------------------------------
  //
  // In video mode we scan for the largest visible <video> and anchor the
  // overlay to its bounding rect. We run this on a rAF loop while the
  // overlay has content — cheap, always correct against theater-mode
  // toggles, resizes, and scroll.

  function findBestVideo() {
    const vids = document.querySelectorAll('video');
    let best = null;
    let bestArea = 0;
    for (const v of vids) {
      const r = v.getBoundingClientRect();
      if (r.width < 200 || r.height < 150) continue;
      const area = r.width * r.height;
      if (area > bestArea) { best = v; bestArea = area; }
    }
    return best;
  }

  let rafHandle = null;
  function reposition() {
    let rect = null;
    const fs = document.fullscreenElement;
    if (fs) {
      rect = fs.getBoundingClientRect();
    } else if (settings.captionAnchor === 'video') {
      const v = findBestVideo();
      if (v) rect = v.getBoundingClientRect();
    }
    if (!rect) {
      rect = { left: 0, top: 0, right: window.innerWidth, bottom: window.innerHeight,
               width: window.innerWidth, height: window.innerHeight };
    }

    const maxW = Math.min(rect.width, rect.width * (settings.captionMaxWidthPct / 100));
    root.style.width = `${maxW}px`;
    root.style.left = `${rect.left + (rect.width - maxW) / 2}px`;

    if (settings.captionPosition === 'bottom') {
      const h = root.offsetHeight || 0;
      root.style.top = `${rect.bottom - settings.captionOffset - h}px`;
    } else {
      root.style.top = `${rect.top + settings.captionOffset}px`;
    }
  }

  function startRaf() {
    if (rafHandle) return;
    const loop = () => { reposition(); rafHandle = requestAnimationFrame(loop); };
    rafHandle = requestAnimationFrame(loop);
  }
  function stopRaf() {
    if (rafHandle) { cancelAnimationFrame(rafHandle); rafHandle = null; }
  }

  // ---- rendering -------------------------------------------------------

  const CLEAR_AFTER_MS = 30_000;
  const MAX_CHARS = 220;
  let clearTimer = null;
  function scheduleClear() {
    if (clearTimer) clearTimeout(clearTimer);
    clearTimer = setTimeout(() => {
      targetLine.textContent = '';
      sourceLine.textContent = '';
      sourceLine.style.display = 'none';
      stopRaf();
    }, CLEAR_AFTER_MS);
  }

  function render(line, snap) {
    if (!snap) return;
    const concluded = snap.concluded?.map((s) => s.text).join(' ') ?? '';
    const tentative = snap.tentative?.map((s) => s.text).join(' ') ?? '';
    let text = (concluded + ' ' + tentative).trim();
    if (text.length > MAX_CHARS) text = '…' + text.slice(-MAX_CHARS);
    line.textContent = text;
    line.classList.toggle('vb-tentative', !concluded && !!tentative);
    if (line === sourceLine) {
      sourceLine.style.display = settings.showOriginalText && text ? '' : 'none';
    }
  }

  chrome.runtime.onMessage.addListener((msg) => {
    if (msg?.target !== 'content') return;
    if (msg.type === 'TRANSCRIPT') {
      mount();
      if (msg.which === 'target') render(targetLine, msg.snapshot);
      else render(sourceLine, msg.snapshot);
      startRaf();
      scheduleClear();
    } else if (msg.type === 'CLEAR') {
      targetLine.textContent = '';
      sourceLine.textContent = '';
      sourceLine.style.display = 'none';
      if (clearTimer) { clearTimeout(clearTimer); clearTimer = null; }
      stopRaf();
    } else if (msg.type === 'SHOW_ORIGINAL') {
      settings.showOriginalText = !!msg.value;
      if (!settings.showOriginalText) sourceLine.style.display = 'none';
    }
  });
})();
