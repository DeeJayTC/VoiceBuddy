// Plays back 16 kHz / 16-bit / mono PCM that DeepL Voice streams back as
// target_media_chunk. Ported in spirit from src/VoiceBuddy.App/Services/VoiceOutPlayer.cs,
// but Web Audio has no BufferedWaveProvider equivalent — we schedule each chunk
// as its own AudioBufferSourceNode and chain their start times so they play
// gap-free. If we fall behind (nextStart > now + MAX_LEAD), we drop the excess
// backlog to keep in sync with the stream (the desktop version discards on
// overflow instead — same effect, different mechanism).

const SAMPLE_RATE = 16_000;
const MAX_LEAD_S = 4.0;

export class VoiceOutPlayer {
  constructor(audioCtx) {
    this.ctx = audioCtx;
    this.gain = audioCtx.createGain();
    this.gain.gain.value = 1.0;
    this.gain.connect(audioCtx.destination);
    this._nextStart = 0;
  }

  enqueuePcm(arrayBuffer) {
    if (!arrayBuffer || arrayBuffer.byteLength === 0) return;
    const i16 = new Int16Array(arrayBuffer);
    const f32 = new Float32Array(i16.length);
    for (let i = 0; i < i16.length; i++) f32[i] = i16[i] / 32768;

    const buf = this.ctx.createBuffer(1, f32.length, SAMPLE_RATE);
    buf.copyToChannel(f32, 0);

    const now = this.ctx.currentTime;
    // If the scheduling cursor has run away from "now", reset it — prevents
    // unbounded drift when the network buffers up a burst of chunks.
    if (this._nextStart > now + MAX_LEAD_S) this._nextStart = now + 0.05;
    const startAt = Math.max(now, this._nextStart);

    const src = this.ctx.createBufferSource();
    src.buffer = buf;
    src.connect(this.gain);
    src.start(startAt);
    this._nextStart = startAt + buf.duration;
  }

  stop() {
    try { this.gain.disconnect(); } catch { }
    this._nextStart = 0;
  }
}
