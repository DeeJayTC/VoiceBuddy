const $ = (id) => document.getElementById(id);

const FIELDS = {
  deepLApiKey: { el: $('apiKey'), default: '' },
  deepLApiHost: { el: $('apiHost'), default: 'api-free.deepl.com' },
  captionAnchor: { el: $('captionAnchor'), default: 'video' },
  captionPosition: { el: $('captionPosition'), default: 'bottom' },
  captionOffset: { el: $('captionOffset'), default: 60, number: true, valEl: $('captionOffsetVal'), suffix: 'px' },
  captionFontSize: { el: $('captionFontSize'), default: 28, number: true, valEl: $('captionFontSizeVal'), suffix: 'px' },
  captionBgOpacity: { el: $('captionBgOpacity'), default: 0.55, number: true, scale: 100, valEl: $('captionBgOpacityVal'), suffix: '%' },
  captionMaxWidthPct: { el: $('captionMaxWidthPct'), default: 90, number: true, valEl: $('captionMaxWidthPctVal'), suffix: '%' },
};

function readField(cfg) {
  const raw = cfg.el.value;
  if (!cfg.number) return raw;
  const n = Number(raw);
  if (cfg.scale) return n / cfg.scale;
  return n;
}

function writeField(cfg, value) {
  let v = value ?? cfg.default;
  if (cfg.number && cfg.scale) v = Math.round(v * cfg.scale);
  cfg.el.value = String(v);
  updateValLabel(cfg);
}

function updateValLabel(cfg) {
  if (!cfg.valEl) return;
  let display = cfg.el.value;
  if (cfg.suffix) display += cfg.suffix;
  cfg.valEl.textContent = display;
}

(async () => {
  const stored = await chrome.storage.local.get(Object.keys(FIELDS));
  for (const [key, cfg] of Object.entries(FIELDS)) {
    writeField(cfg, stored[key]);
  }
})();

// Live-update the numeric display next to sliders.
for (const cfg of Object.values(FIELDS)) {
  if (cfg.valEl) cfg.el.addEventListener('input', () => updateValLabel(cfg));
}

$('save').addEventListener('click', async () => {
  const patch = {};
  for (const [key, cfg] of Object.entries(FIELDS)) {
    let v = readField(cfg);
    if (typeof v === 'string') v = v.trim();
    patch[key] = v;
  }
  await chrome.storage.local.set(patch);
  const saved = $('saved');
  saved.classList.add('visible');
  setTimeout(() => saved.classList.remove('visible'), 1400);
});
