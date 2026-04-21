// DeepL Voice Realtime client — port of src/VoiceBuddy.App/Services/DeepLVoiceService.cs.
//
// Flow:
//   1. POST https://{host}/v3/voice/realtime → ephemeral {streaming_url, token}.
//   2. Connect wss://{streaming_url}?token={token}.
//   3. Stream 16 kHz mono 16-bit-PCM as JSON {source_media_chunk:{data: base64}}.
//   4. Receive {source_transcript_update, target_transcript_update, ...}.
//
// Callbacks: onStatus(text), onSource({language, concluded, tentative}),
// onTarget({language, concluded, tentative}), onTargetMedia(ArrayBuffer).

import { linearResample } from './resampler.js';

const TARGET_RATE = 16_000;
const CHUNK_MS = 120;
const CHUNK_SAMPLES = (TARGET_RATE * CHUNK_MS) / 1000; // 1920

export class DeepLVoiceClient {
  constructor({ onStatus, onSource, onTarget, onTargetMedia } = {}) {
    this.onStatus = onStatus ?? (() => {});
    this.onSource = onSource ?? (() => {});
    this.onTarget = onTarget ?? (() => {});
    this.onTargetMedia = onTargetMedia ?? (() => {});

    this._ws = null;
    this._pcm = [];
    this._srcConcluded = [];
    this._tgtConcluded = [];
    this._srcLang = 'auto';
    this._targetLang = '';
  }

  get isRunning() {
    return this._ws?.readyState === WebSocket.OPEN;
  }

  async start({ host, apiKey, sourceLang, targetLang, wantVoiceOut = false, voiceGender = '' }) {
    this.stop();
    if (!apiKey) {
      this.onStatus('DeepL API key missing');
      return false;
    }
    this._srcLang = sourceLang;
    this._targetLang = targetLang;
    this._srcConcluded = [];
    this._tgtConcluded = [];
    this.onSource({ language: sourceLang, concluded: [], tentative: [] });
    this.onTarget({ language: targetLang, concluded: [], tentative: [] });

    let session;
    try {
      session = await this._requestSession({ host, apiKey, sourceLang, targetLang, wantVoiceOut, voiceGender });
    } catch (e) {
      this.onStatus(`Session request failed: ${e.message}`);
      return false;
    }

    try {
      const wsUrl = `${session.streaming_url}?token=${encodeURIComponent(session.token)}`;
      this._ws = new WebSocket(wsUrl);
      this._ws.binaryType = 'arraybuffer';
      await new Promise((resolve, reject) => {
        this._ws.addEventListener('open', resolve, { once: true });
        this._ws.addEventListener('error', () => reject(new Error('ws error')), { once: true });
      });
    } catch (e) {
      this.onStatus(`WebSocket connect failed: ${e.message}`);
      try { this._ws?.close(); } catch { }
      this._ws = null;
      return false;
    }

    this._ws.addEventListener('message', (ev) => this._onMessage(ev.data));
    this._ws.addEventListener('close', () => {
      if (this._ws) this.onStatus('DeepL Voice disconnected');
      this._ws = null;
    });

    this.onStatus(`DeepL Voice session ${session.session_id?.slice(0, 8)}… connected`);
    return true;
  }

  stop() {
    const ws = this._ws;
    this._ws = null;
    if (ws && ws.readyState === WebSocket.OPEN) {
      try { ws.send('{"end_of_source_media":{}}'); } catch { }
      try { ws.close(); } catch { }
    }
    this._pcm = [];
    this._srcConcluded = [];
    this._tgtConcluded = [];
    this.onStatus('Idle');
  }

  // Feed mono Float32 samples at any rate; we resample and pack internally.
  addFrame(samples, inputRate) {
    if (!this.isRunning) return;
    const s16k = linearResample(samples, inputRate, TARGET_RATE);
    for (let i = 0; i < s16k.length; i++) {
      let v = s16k[i] * 32767;
      if (v > 32767) v = 32767; else if (v < -32768) v = -32768;
      this._pcm.push(v | 0);
    }
    while (this._pcm.length >= CHUNK_SAMPLES) {
      const buf = new ArrayBuffer(CHUNK_SAMPLES * 2);
      const view = new DataView(buf);
      for (let i = 0; i < CHUNK_SAMPLES; i++) {
        view.setInt16(i * 2, this._pcm[i], true);
      }
      this._pcm.splice(0, CHUNK_SAMPLES);
      this._sendChunk(buf);
    }
  }

  _sendChunk(buf) {
    if (!this.isRunning) return;
    const b64 = arrayBufferToBase64(buf);
    const msg = `{"source_media_chunk":{"data":"${b64}"}}`;
    try { this._ws.send(msg); } catch { /* socket died, receive-side handles it */ }
  }

  async _requestSession({ host, apiKey, sourceLang, targetLang, wantVoiceOut, voiceGender }) {
    const auto = sourceLang.toLowerCase() === 'auto';
    const payload = {
      source_media_content_type: 'audio/pcm;encoding=s16le;rate=16000',
      source_language_mode: auto ? 'auto' : 'fixed',
      target_languages: [targetLang],
      message_format: 'json',
    };
    if (!auto) payload.source_language = sourceLang.toLowerCase();
    if (wantVoiceOut) {
      payload.target_media_languages = [targetLang];
      payload.target_media_content_type = 'audio/pcm;encoding=s16le;rate=16000';
      if (voiceGender) payload.target_media_voice = voiceGender;
    }

    const resp = await fetch(`https://${host}/v3/voice/realtime`, {
      method: 'POST',
      headers: {
        'Authorization': `DeepL-Auth-Key ${apiKey}`,
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(payload),
    });
    const body = await resp.text();
    if (!resp.ok) throw new Error(`${resp.status}: ${body}`);
    const json = JSON.parse(body);
    if (!json.streaming_url || !json.token) throw new Error('missing streaming_url or token');
    return json;
  }

  _onMessage(raw) {
    let data;
    try { data = JSON.parse(typeof raw === 'string' ? raw : new TextDecoder().decode(raw)); }
    catch { return; }

    if (data.source_transcript_update) {
      this._handleTranscript(data.source_transcript_update, this._srcConcluded, this._srcLang, true);
    } else if (data.target_transcript_update) {
      this._handleTranscript(data.target_transcript_update, this._tgtConcluded, this._targetLang, false);
    } else if (data.target_media_chunk) {
      this._handleTargetMedia(data.target_media_chunk);
    } else if (data.error) {
      this.onStatus(`DeepL error ${data.error.reason_code ?? 0}: ${data.error.error_message ?? '(no message)'}`);
      this.stop();
    } else if (data.end_of_stream) {
      this.onStatus('DeepL session ended');
      this.stop();
    }
  }

  _handleTranscript(body, accumulator, fallbackLang, isSource) {
    if (Array.isArray(body.concluded)) {
      for (const seg of body.concluded) {
        const parsed = parseSegment(seg);
        if (parsed) accumulator.push(parsed);
      }
    }
    const tentative = [];
    if (Array.isArray(body.tentative)) {
      for (const seg of body.tentative) {
        const parsed = parseSegment(seg);
        if (parsed) tentative.push(parsed);
      }
    }
    const language = body.language ?? fallbackLang;
    const snapshot = { language, concluded: accumulator.slice(), tentative };
    if (isSource) this.onSource(snapshot); else this.onTarget(snapshot);
  }

  _handleTargetMedia(body) {
    if (!Array.isArray(body.data)) return;
    const headers = typeof body.headers === 'number' ? body.headers : 0;
    const parts = [];
    let total = 0;
    for (let i = 0; i < body.data.length; i++) {
      if (i < headers) continue;
      const s = body.data[i];
      if (!s) continue;
      try {
        const bytes = base64ToBytes(s);
        parts.push(bytes);
        total += bytes.length;
      } catch { /* skip malformed */ }
    }
    if (total === 0) return;
    const combined = new Uint8Array(total);
    let off = 0;
    for (const p of parts) { combined.set(p, off); off += p.length; }
    this.onTargetMedia(combined.buffer);
  }
}

function parseSegment(seg) {
  if (!seg || typeof seg !== 'object') return null;
  const text = typeof seg.text === 'string' ? seg.text : '';
  if (!text) return null;
  const t0 = (seg.start_time ?? 0) / 1000;
  const t1 = (seg.end_time ?? 0) / 1000;
  return { text, startTime: t0, endTime: t1 };
}

function arrayBufferToBase64(buf) {
  const bytes = new Uint8Array(buf);
  let s = '';
  const chunk = 0x8000;
  for (let i = 0; i < bytes.length; i += chunk) {
    s += String.fromCharCode.apply(null, bytes.subarray(i, i + chunk));
  }
  return btoa(s);
}

function base64ToBytes(s) {
  const bin = atob(s);
  const out = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
  return out;
}
