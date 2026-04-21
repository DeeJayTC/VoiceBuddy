// AudioWorkletProcessor that downmixes to mono Float32 and posts frames
// to the main thread in ~20 ms batches. Resampling happens main-thread
// (DeepLVoiceClient.addFrame) so we can keep this hot-path trivial.
class DownmixProcessor extends AudioWorkletProcessor {
  constructor() {
    super();
    this._buf = [];
    // ~20 ms at 48 kHz = 960 samples; flush when we cross this threshold.
    this._batchSize = Math.floor(sampleRate * 0.02);
  }

  process(inputs) {
    const input = inputs[0];
    if (!input || input.length === 0) return true;
    const ch0 = input[0];
    const ch1 = input.length > 1 ? input[1] : null;
    if (!ch0) return true;

    for (let i = 0; i < ch0.length; i++) {
      const s = ch1 ? 0.5 * (ch0[i] + ch1[i]) : ch0[i];
      this._buf.push(s);
    }

    if (this._buf.length >= this._batchSize) {
      const out = new Float32Array(this._buf);
      this._buf.length = 0;
      this.port.postMessage({ samples: out, sampleRate }, [out.buffer]);
    }
    return true;
  }
}

registerProcessor('downmix', DownmixProcessor);
