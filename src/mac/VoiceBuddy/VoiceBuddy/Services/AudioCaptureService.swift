import Foundation
import AVFoundation
import ScreenCaptureKit
import CoreAudio
import CoreMedia

struct MonoFrame {
    let samples: [Float]
    let sampleRate: Int
}

@MainActor
final class AudioCaptureService: NSObject, ObservableObject {
    @Published private(set) var isRunning = false
    @Published private(set) var rms: Float = 0
    @Published private(set) var currentFormat: String = ""

    var onFrame: ((MonoFrame) -> Void)?
    var onError: ((String) -> Void)?

    private var engine: AVAudioEngine?
    private var scStream: SCStream?
    private var scDelegate: SCAudioDelegate?
    private var levelAccumulator: [Float] = []

    // MARK: - Enumeration

    func enumerateDevices() async -> [AudioDevice] {
        var out: [AudioDevice] = []

        // Microphones (CoreAudio input endpoints).
        out.append(contentsOf: Self.enumerateCoreAudioDevices(input: true))

        // System audio / per-app audio (ScreenCaptureKit).
        do {
            let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: false)
            if let main = content.displays.first(where: { $0.displayID == CGMainDisplayID() }) ?? content.displays.first {
                out.append(AudioDevice(
                    id: "system:display:\(main.displayID)",
                    name: "System audio (all apps)",
                    kind: .render,
                    isDefault: true
                ))
            }
            for app in content.applications
                where !app.bundleIdentifier.isEmpty
                   && app.bundleIdentifier != Bundle.main.bundleIdentifier
            {
                out.append(AudioDevice(
                    id: "system:app:\(app.bundleIdentifier)",
                    name: "App — \(app.applicationName)",
                    kind: .render,
                    isDefault: false
                ))
            }
        } catch {
            // Permission likely not granted yet; don't surface loopback entries.
        }

        return out
    }

    static func enumerateOutputDevices() -> [AudioDevice] {
        enumerateCoreAudioDevices(input: false)
    }

    private static func enumerateCoreAudioDevices(input: Bool) -> [AudioDevice] {
        var out: [AudioDevice] = []
        var size: UInt32 = 0
        var addr = AudioObjectPropertyAddress(
            mSelector: kAudioHardwarePropertyDevices,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain
        )
        guard AudioObjectGetPropertyDataSize(AudioObjectID(kAudioObjectSystemObject), &addr, 0, nil, &size) == 0 else {
            return out
        }
        let count = Int(size) / MemoryLayout<AudioDeviceID>.size
        var ids = [AudioDeviceID](repeating: 0, count: count)
        guard AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject), &addr, 0, nil, &size, &ids) == 0 else {
            return out
        }

        var defaultID: AudioDeviceID = 0
        var defSize = UInt32(MemoryLayout<AudioDeviceID>.size)
        var defAddr = AudioObjectPropertyAddress(
            mSelector: input ? kAudioHardwarePropertyDefaultInputDevice : kAudioHardwarePropertyDefaultOutputDevice,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain
        )
        AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject), &defAddr, 0, nil, &defSize, &defaultID)

        for id in ids {
            guard hasChannels(id, input: input) else { continue }
            guard let name = deviceName(id), let uid = deviceUID(id) else { continue }
            out.append(AudioDevice(
                id: "coreaudio:\(uid)",
                name: name,
                kind: input ? .capture : .render,
                isDefault: id == defaultID
            ))
        }
        return out
    }

    private static func hasChannels(_ id: AudioDeviceID, input: Bool) -> Bool {
        var addr = AudioObjectPropertyAddress(
            mSelector: kAudioDevicePropertyStreamConfiguration,
            mScope: input ? kAudioDevicePropertyScopeInput : kAudioDevicePropertyScopeOutput,
            mElement: kAudioObjectPropertyElementMain
        )
        var size: UInt32 = 0
        guard AudioObjectGetPropertyDataSize(id, &addr, 0, nil, &size) == 0, size > 0 else { return false }
        let buf = UnsafeMutableRawPointer.allocate(byteCount: Int(size), alignment: MemoryLayout<AudioBufferList>.alignment)
        defer { buf.deallocate() }
        let listPtr = buf.assumingMemoryBound(to: AudioBufferList.self)
        guard AudioObjectGetPropertyData(id, &addr, 0, nil, &size, listPtr) == 0 else { return false }
        let list = UnsafeMutableAudioBufferListPointer(listPtr)
        var channels: UInt32 = 0
        for buffer in list { channels += buffer.mNumberChannels }
        return channels > 0
    }

    private static func deviceName(_ id: AudioDeviceID) -> String? {
        var addr = AudioObjectPropertyAddress(
            mSelector: kAudioDevicePropertyDeviceNameCFString,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain
        )
        var size = UInt32(MemoryLayout<CFString?>.size)
        var name: CFString?
        guard AudioObjectGetPropertyData(id, &addr, 0, nil, &size, &name) == 0 else { return nil }
        return name as String?
    }

    private static func deviceUID(_ id: AudioDeviceID) -> String? {
        var addr = AudioObjectPropertyAddress(
            mSelector: kAudioDevicePropertyDeviceUID,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain
        )
        var size = UInt32(MemoryLayout<CFString?>.size)
        var uid: CFString?
        guard AudioObjectGetPropertyData(id, &addr, 0, nil, &size, &uid) == 0 else { return nil }
        return uid as String?
    }

    static func deviceID(forUID uid: String) -> AudioDeviceID? {
        var addr = AudioObjectPropertyAddress(
            mSelector: kAudioHardwarePropertyTranslateUIDToDevice,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain
        )
        var cf = uid as CFString
        var id: AudioDeviceID = 0
        var outSize = UInt32(MemoryLayout<AudioDeviceID>.size)
        let r = withUnsafeMutablePointer(to: &cf) { cfPtr -> OSStatus in
            AudioObjectGetPropertyData(
                AudioObjectID(kAudioObjectSystemObject), &addr,
                UInt32(MemoryLayout<CFString>.size), cfPtr,
                &outSize, &id
            )
        }
        return r == 0 && id != 0 ? id : nil
    }

    // MARK: - Start / stop

    func start(deviceId: String?, kind: AudioDeviceKind) async throws {
        stop()
        switch kind {
        case .capture:
            try startMic(deviceId: deviceId)
        case .render:
            try await startSystemAudio(selector: deviceId)
        }
        isRunning = true
    }

    private func startMic(deviceId: String?) throws {
        let engine = AVAudioEngine()

        if let id = deviceId, id.hasPrefix("coreaudio:") {
            let uid = String(id.dropFirst("coreaudio:".count))
            if let devID = Self.deviceID(forUID: uid), let au = engine.inputNode.audioUnit {
                var ref = devID
                let status = AudioUnitSetProperty(
                    au,
                    kAudioOutputUnitProperty_CurrentDevice,
                    kAudioUnitScope_Global, 0,
                    &ref, UInt32(MemoryLayout<AudioDeviceID>.size)
                )
                if status != 0 {
                    NSLog("AudioCaptureService: failed to set input device \(uid): \(status)")
                }
            }
        }

        let input = engine.inputNode
        let format = input.outputFormat(forBus: 0)
        let sr = Int(format.sampleRate)
        currentFormat = "\(sr) Hz · \(format.channelCount) ch (mic)"

        input.installTap(onBus: 0, bufferSize: 2048, format: format) { [weak self] buffer, _ in
            let mono = Self.downmixFloat(buffer)
            Task { @MainActor [weak self] in
                self?.emit(mono: mono, rate: sr)
            }
        }

        try engine.start()
        self.engine = engine
    }

    private func startSystemAudio(selector: String?) async throws {
        let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: false)
        guard let display = content.displays.first(where: { $0.displayID == CGMainDisplayID() }) ?? content.displays.first else {
            throw NSError(domain: "VoiceBuddy", code: 1, userInfo: [NSLocalizedDescriptionKey: "No display available for system-audio capture."])
        }

        let filter: SCContentFilter
        if let sel = selector, sel.hasPrefix("system:app:") {
            let bid = String(sel.dropFirst("system:app:".count))
            if let app = content.applications.first(where: { $0.bundleIdentifier == bid }) {
                filter = SCContentFilter(display: display, including: [app], exceptingWindows: [])
            } else {
                filter = SCContentFilter(display: display, excludingWindows: [])
            }
        } else {
            filter = SCContentFilter(display: display, excludingWindows: [])
        }

        let cfg = SCStreamConfiguration()
        cfg.capturesAudio = true
        cfg.sampleRate = 48_000
        cfg.channelCount = 2
        // We only want audio; run video track at 2×2 / 1 fps to minimize cost.
        cfg.width = 2
        cfg.height = 2
        cfg.minimumFrameInterval = CMTime(value: 1, timescale: 1)
        cfg.queueDepth = 6
        cfg.showsCursor = false

        let stream = SCStream(filter: filter, configuration: cfg, delegate: nil)
        let delegate = SCAudioDelegate { [weak self] mono, rate in
            Task { @MainActor [weak self] in
                self?.emit(mono: mono, rate: rate)
            }
        }
        try stream.addStreamOutput(delegate, type: .audio, sampleHandlerQueue: DispatchQueue(label: "com.deepl.voicebuddy.sc-audio"))
        // ScreenCaptureKit requires a video sink too, even when only audio is consumed.
        try stream.addStreamOutput(delegate, type: .screen, sampleHandlerQueue: DispatchQueue(label: "com.deepl.voicebuddy.sc-video"))
        try await stream.startCapture()

        currentFormat = "48000 Hz · 2 ch (system)"
        scStream = stream
        scDelegate = delegate
    }

    func stop() {
        engine?.inputNode.removeTap(onBus: 0)
        engine?.stop()
        engine = nil

        if let s = scStream {
            Task.detached {
                try? await s.stopCapture()
            }
        }
        scStream = nil
        scDelegate = nil

        rms = 0
        levelAccumulator.removeAll(keepingCapacity: true)
        isRunning = false
    }

    // MARK: - Frame emit / level meter

    private func emit(mono: [Float], rate: Int) {
        onFrame?(MonoFrame(samples: mono, sampleRate: rate))
        levelAccumulator.append(contentsOf: mono)
        let window = max(1, rate / 30)   // ~33 ms window
        if levelAccumulator.count >= window {
            var acc: Float = 0
            for s in levelAccumulator { acc += s * s }
            rms = (acc / Float(levelAccumulator.count)).squareRoot()
            levelAccumulator.removeAll(keepingCapacity: true)
        }
    }

    // MARK: - Downmix (AVAudioEngine tap)

    static func downmixFloat(_ buffer: AVAudioPCMBuffer) -> [Float] {
        let frames = Int(buffer.frameLength)
        guard frames > 0, let data = buffer.floatChannelData else { return [] }
        let channels = Int(buffer.format.channelCount)
        var out = [Float](repeating: 0, count: frames)
        if channels == 1 {
            memcpy(&out, data[0], frames * MemoryLayout<Float>.size)
        } else {
            for f in 0..<frames {
                var sum: Float = 0
                for c in 0..<channels { sum += data[c][f] }
                out[f] = sum / Float(channels)
            }
        }
        return out
    }
}

// MARK: - ScreenCaptureKit audio callback

private final class SCAudioDelegate: NSObject, SCStreamOutput {
    let onAudio: ([Float], Int) -> Void

    init(onAudio: @escaping ([Float], Int) -> Void) {
        self.onAudio = onAudio
    }

    func stream(_ stream: SCStream,
                didOutputSampleBuffer sampleBuffer: CMSampleBuffer,
                of outputType: SCStreamOutputType)
    {
        guard outputType == .audio else { return }
        guard CMSampleBufferIsValid(sampleBuffer) else { return }
        guard let desc = CMSampleBufferGetFormatDescription(sampleBuffer),
              let asbdPtr = CMAudioFormatDescriptionGetStreamBasicDescription(desc) else { return }
        let asbd = asbdPtr.pointee
        let rate = Int(asbd.mSampleRate)
        let channels = Int(asbd.mChannelsPerFrame)
        guard rate > 0, channels > 0 else { return }

        var bufferList = AudioBufferList()
        var blockBuffer: CMBlockBuffer?
        let status = CMSampleBufferGetAudioBufferListWithRetainedBlockBuffer(
            sampleBuffer,
            bufferListSizeNeededOut: nil,
            bufferListOut: &bufferList,
            bufferListSize: MemoryLayout<AudioBufferList>.size,
            blockBufferAllocator: nil,
            blockBufferMemoryAllocator: nil,
            flags: kCMSampleBufferFlag_AudioBufferList_Assure16ByteAlignment,
            blockBufferOut: &blockBuffer
        )
        guard status == noErr else { return }

        let abl = UnsafeMutableAudioBufferListPointer(&bufferList)
        let isFloat = (asbd.mFormatFlags & kAudioFormatFlagIsFloat) != 0
        let bitsPerSample = Int(asbd.mBitsPerChannel)
        let isNonInterleaved = (asbd.mFormatFlags & kAudioFormatFlagIsNonInterleaved) != 0

        if isNonInterleaved {
            // One buffer per channel.
            guard let first = abl.first, let _ = first.mData else { return }
            let framesPerBuffer = Int(first.mDataByteSize) / (bitsPerSample / 8)
            var mono = [Float](repeating: 0, count: framesPerBuffer)
            let divisor = Float(abl.count)
            for channel in abl {
                guard let base = channel.mData else { continue }
                if isFloat && bitsPerSample == 32 {
                    let src = base.bindMemory(to: Float32.self, capacity: framesPerBuffer)
                    for f in 0..<framesPerBuffer { mono[f] += src[f] / divisor }
                } else if bitsPerSample == 16 {
                    let src = base.bindMemory(to: Int16.self, capacity: framesPerBuffer)
                    for f in 0..<framesPerBuffer { mono[f] += Float(src[f]) / 32767.0 / divisor }
                }
            }
            onAudio(mono, rate)
        } else {
            guard let first = abl.first, let base = first.mData else { return }
            let bytes = Int(first.mDataByteSize)
            if isFloat && bitsPerSample == 32 {
                let sampleCount = bytes / MemoryLayout<Float32>.size
                let frames = sampleCount / channels
                let src = base.bindMemory(to: Float32.self, capacity: sampleCount)
                var mono = [Float](repeating: 0, count: frames)
                if channels == 1 {
                    for f in 0..<frames { mono[f] = src[f] }
                } else {
                    for f in 0..<frames {
                        var sum: Float = 0
                        for c in 0..<channels { sum += src[f * channels + c] }
                        mono[f] = sum / Float(channels)
                    }
                }
                onAudio(mono, rate)
            } else if bitsPerSample == 16 {
                let sampleCount = bytes / MemoryLayout<Int16>.size
                let frames = sampleCount / channels
                let src = base.bindMemory(to: Int16.self, capacity: sampleCount)
                var mono = [Float](repeating: 0, count: frames)
                for f in 0..<frames {
                    var sum: Float = 0
                    for c in 0..<channels { sum += Float(src[f * channels + c]) / 32767.0 }
                    mono[f] = sum / Float(channels)
                }
                onAudio(mono, rate)
            }
        }
    }
}
