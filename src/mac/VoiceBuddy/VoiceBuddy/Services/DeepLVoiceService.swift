import Foundation

@MainActor
final class DeepLVoiceService: ObservableObject {
    var onSourceUpdate: ((TranscriptSnapshot) -> Void)?
    var onTargetUpdate: ((TranscriptSnapshot) -> Void)?
    var onTargetMedia: ((Data) -> Void)?
    var onStatus: ((String) -> Void)?

    private let debug: DebugLog
    private var task: URLSessionWebSocketTask?
    private var receiveTask: Task<Void, Never>?
    private var running = false

    private var sourceAccumulator: [TranscriptSegment] = []
    private var targetAccumulator: [TranscriptSegment] = []

    // 16 kHz, 120 ms = 1920 samples (s16 little-endian).
    private let targetRate = 16_000
    private let chunkSamples = 1920
    private var pcmQueue: [Int16] = []

    init(debug: DebugLog) {
        self.debug = debug
    }

    // MARK: - Start / stop

    func start(host: String,
               apiKey: String,
               sourceLang: String,
               targetLang: String,
               wantVoice: Bool,
               voice: String) async throws
    {
        await stop()
        sourceAccumulator.removeAll()
        targetAccumulator.removeAll()
        pcmQueue.removeAll()

        let session = try await requestSession(
            host: host,
            apiKey: apiKey,
            sourceLang: sourceLang,
            targetLang: targetLang,
            wantVoice: wantVoice,
            voice: voice
        )

        guard var comps = URLComponents(string: session.streaming_url) else {
            throw NSError(domain: "DeepL", code: -2, userInfo: [NSLocalizedDescriptionKey: "Invalid streaming_url."])
        }
        var items = comps.queryItems ?? []
        items.append(URLQueryItem(name: "token", value: session.token))
        comps.queryItems = items
        guard let url = comps.url else {
            throw NSError(domain: "DeepL", code: -3, userInfo: [NSLocalizedDescriptionKey: "Cannot build WebSocket URL."])
        }

        debug.log(.out, kind: "ws connect", payload: url.absoluteString)
        let ws = URLSession.shared.webSocketTask(with: url)
        task = ws
        running = true
        ws.resume()
        debug.log(.in, kind: "ws open", payload: url.host ?? "")
        onStatus?("Connected")

        receiveTask = Task { [weak self] in
            await self?.receiveLoop()
        }
    }

    func stop() async {
        running = false
        pcmQueue.removeAll()
        receiveTask?.cancel()
        receiveTask = nil
        if let ws = task {
            ws.send(.string("{\"end_of_source_media\":{}}")) { _ in }
            ws.cancel(with: .goingAway, reason: nil)
        }
        task = nil
        onStatus?("Idle")
    }

    // MARK: - REST session

    private struct Session: Decodable {
        let streaming_url: String
        let token: String
        let session_id: String
    }

    private struct SessionRequest: Encodable {
        var source_media_content_type = "audio/pcm;encoding=s16le;rate=16000"
        var source_language: String?
        var source_language_mode: String
        var target_languages: [String]
        var target_media_languages: [String]?
        var target_media_content_type: String?
        var target_media_voice: String?
        var message_format = "json"
    }

    private func requestSession(host: String,
                                apiKey: String,
                                sourceLang: String,
                                targetLang: String,
                                wantVoice: Bool,
                                voice: String) async throws -> Session
    {
        guard let url = URL(string: "https://\(host)/v3/voice/realtime") else {
            throw NSError(domain: "DeepL", code: -1, userInfo: [NSLocalizedDescriptionKey: "Invalid host."])
        }
        var req = URLRequest(url: url)
        req.httpMethod = "POST"
        req.setValue("DeepL-Auth-Key \(apiKey)", forHTTPHeaderField: "Authorization")
        req.setValue("application/json", forHTTPHeaderField: "Content-Type")

        let tgt = String(targetLang.split(separator: "-").first ?? Substring(targetLang))
        let body = SessionRequest(
            source_language: sourceLang.lowercased() == "auto" ? nil : sourceLang,
            source_language_mode: sourceLang.lowercased() == "auto" ? "auto" : "fixed",
            target_languages: [tgt],
            target_media_languages: wantVoice ? [tgt] : nil,
            target_media_content_type: wantVoice ? "audio/pcm;encoding=s16le;rate=16000" : nil,
            target_media_voice: voice.isEmpty ? nil : voice
        )
        let encoder = JSONEncoder()
        encoder.outputFormatting = []
        let data = try encoder.encode(body)
        req.httpBody = data
        debug.log(.out, kind: "POST \(url.path)", payload: String(data: data, encoding: .utf8) ?? "")

        let (respData, response) = try await URLSession.shared.data(for: req)
        let http = response as? HTTPURLResponse
        let code = http?.statusCode ?? -1
        let respString = String(data: respData, encoding: .utf8) ?? ""
        debug.log(.in, kind: "HTTP \(code)", payload: respString)
        guard code == 200 else {
            throw NSError(domain: "DeepL", code: code, userInfo: [NSLocalizedDescriptionKey: respString])
        }
        return try JSONDecoder().decode(Session.self, from: respData)
    }

    // MARK: - Audio upload

    func addFrame(_ frame: MonoFrame) {
        guard running else { return }
        let resampled = Resampler.linear(frame.samples, from: frame.sampleRate, to: targetRate)
        pcmQueue.reserveCapacity(pcmQueue.count + resampled.count)
        for v in resampled {
            let clamped = max(-1.0, min(1.0, Double(v)))
            pcmQueue.append(Int16(clamped * 32767))
        }
        while pcmQueue.count >= chunkSamples {
            let slice = Array(pcmQueue.prefix(chunkSamples))
            pcmQueue.removeFirst(chunkSamples)
            sendChunk(slice)
        }
    }

    private func sendChunk(_ samples: [Int16]) {
        var data = Data(capacity: samples.count * 2)
        for s in samples {
            var le = s.littleEndian
            withUnsafeBytes(of: &le) { data.append(contentsOf: $0) }
        }
        let b64 = data.base64EncodedString()
        let json = "{\"source_media_chunk\":{\"data\":\"\(b64)\"}}"
        debug.audioChunk(bytes: data.count)
        task?.send(.string(json)) { err in
            if let err { NSLog("DeepL send error: \(err)") }
        }
    }

    // MARK: - Receive loop

    private func receiveLoop() async {
        guard let ws = task else { return }
        while running {
            do {
                let msg = try await ws.receive()
                switch msg {
                case .string(let s):
                    handleMessage(s)
                case .data(let d):
                    if let s = String(data: d, encoding: .utf8) { handleMessage(s) }
                @unknown default:
                    break
                }
            } catch {
                if running {
                    let msg = "DeepL Voice disconnected: \(error.localizedDescription)"
                    debug.log(.in, kind: "ws error", payload: msg)
                    onStatus?(msg)
                }
                running = false
                break
            }
        }
    }

    private func handleMessage(_ json: String) {
        let trimmed = json.count > 800 ? String(json.prefix(800)) + "…" : json
        debug.log(.in, kind: "ws", payload: trimmed)

        guard let data = json.data(using: .utf8),
              let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return }

        if let body = obj["source_transcript_update"] as? [String: Any] {
            handleTranscript(body, isSource: true)
        } else if let body = obj["target_transcript_update"] as? [String: Any] {
            handleTranscript(body, isSource: false)
        } else if let body = obj["target_media_chunk"] as? [String: Any] {
            handleTargetMedia(body)
        } else if let body = obj["error"] as? [String: Any] {
            let msg = body["error_message"] as? String ?? "unknown error"
            onStatus?("DeepL error: \(msg)")
        } else if obj["end_of_stream"] != nil {
            onStatus?("DeepL: end of stream")
            running = false
        }
    }

    private func handleTranscript(_ body: [String: Any], isSource: Bool) {
        let lang = body["language"] as? String ?? ""
        if let concluded = body["concluded"] as? [[String: Any]] {
            for seg in concluded {
                let parsed = parseSegment(seg)
                if isSource { sourceAccumulator.append(parsed) }
                else        { targetAccumulator.append(parsed) }
            }
        }
        var tentative: [TranscriptSegment] = []
        if let arr = body["tentative"] as? [[String: Any]] {
            for seg in arr { tentative.append(parseSegment(seg)) }
        }
        let acc = isSource ? sourceAccumulator : targetAccumulator
        let snap = TranscriptSnapshot(lang: lang, concluded: acc, tentative: tentative)
        if isSource { onSourceUpdate?(snap) } else { onTargetUpdate?(snap) }
    }

    private func parseSegment(_ seg: [String: Any]) -> TranscriptSegment {
        let text = seg["text"] as? String ?? ""
        let t0Ms = (seg["start_time"] as? Double) ?? Double((seg["start_time"] as? Int) ?? 0)
        let t1Ms = (seg["end_time"] as? Double) ?? Double((seg["end_time"] as? Int) ?? 0)
        return .init(text: text, t0: t0Ms / 1000, t1: t1Ms / 1000)
    }

    private func handleTargetMedia(_ body: [String: Any]) {
        let headers = (body["headers"] as? Int) ?? 0
        guard let arr = body["data"] as? [String] else { return }
        for (i, b64) in arr.enumerated() {
            if i < headers { continue }
            if let data = Data(base64Encoded: b64) {
                onTargetMedia?(data)
            }
        }
    }
}
