// Linear resampler for mono Float32 PCM — direct port of
// src/VoiceBuddy.App/Services/Resampler.cs. Good enough for speech.
export function linearResample(input, inputRate, outputRate) {
  if (inputRate === outputRate || input.length === 0) return input;
  const ratio = inputRate / outputRate;
  const outLen = Math.floor(input.length / ratio);
  if (outLen <= 0) return new Float32Array(0);

  const out = new Float32Array(outLen);
  for (let i = 0; i < outLen; i++) {
    const srcIdx = i * ratio;
    const idx0 = srcIdx | 0;
    const idx1 = idx0 + 1 < input.length ? idx0 + 1 : input.length - 1;
    const t = srcIdx - idx0;
    out[i] = input[idx0] * (1 - t) + input[idx1] * t;
  }
  return out;
}
