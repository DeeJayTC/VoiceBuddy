import Foundation
import AVFoundation
import CoreAudio

@MainActor
final class VoiceOutPlayer {
    private var engine: AVAudioEngine?
    private var player: AVAudioPlayerNode?
    private let sourceFormat = AVAudioFormat(
        commonFormat: .pcmFormatInt16,
        sampleRate: 16_000,
        channels: 1,
        interleaved: true
    )!
    private var scheduledFrames: Int = 0
    private let maxBackloggedFrames = 16_000 * 10   // 10 s cap — mirrors WASAPI BufferedWaveProvider discard-on-overflow.

    func start(deviceId: String?) throws {
        stop()
        let engine = AVAudioEngine()

        if let id = deviceId, id.hasPrefix("coreaudio:") {
            let uid = String(id.dropFirst("coreaudio:".count))
            if let devID = AudioCaptureService.deviceID(forUID: uid), let au = engine.outputNode.audioUnit {
                var ref = devID
                let status = AudioUnitSetProperty(
                    au,
                    kAudioOutputUnitProperty_CurrentDevice,
                    kAudioUnitScope_Global, 0,
                    &ref, UInt32(MemoryLayout<AudioDeviceID>.size)
                )
                if status != 0 {
                    NSLog("VoiceOutPlayer: failed to set output device \(uid): \(status)")
                }
            }
        }

        let player = AVAudioPlayerNode()
        engine.attach(player)
        // Connect the player at the DeepL output rate; mainMixer will resample if needed.
        engine.connect(player, to: engine.mainMixerNode, format: sourceFormat)

        try engine.start()
        player.play()

        self.engine = engine
        self.player = player
        scheduledFrames = 0
    }

    func addPcm(_ data: Data) {
        guard let player else { return }
        let frameCount = data.count / 2
        guard frameCount > 0 else { return }
        if scheduledFrames > maxBackloggedFrames { return }

        guard let buffer = AVAudioPCMBuffer(pcmFormat: sourceFormat, frameCapacity: AVAudioFrameCount(frameCount)) else { return }
        buffer.frameLength = AVAudioFrameCount(frameCount)

        data.withUnsafeBytes { raw in
            guard let srcBase = raw.baseAddress else { return }
            let src = srcBase.assumingMemoryBound(to: Int16.self)
            if let dst = buffer.int16ChannelData?[0] {
                for i in 0..<frameCount {
                    dst[i] = Int16(littleEndian: src[i])
                }
            }
        }

        scheduledFrames += frameCount
        player.scheduleBuffer(buffer) { [weak self] in
            Task { @MainActor [weak self] in
                guard let self else { return }
                self.scheduledFrames = max(0, self.scheduledFrames - frameCount)
            }
        }
    }

    func stop() {
        player?.stop()
        engine?.stop()
        engine = nil
        player = nil
        scheduledFrames = 0
    }
}
