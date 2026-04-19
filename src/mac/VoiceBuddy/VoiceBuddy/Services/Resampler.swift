import Foundation

enum Resampler {
    /// Linear-interpolation resample for mono float PCM. Mirrors the Windows build's
    /// Resampler.Linear — fine for speech, avoids the cost of a polyphase kernel.
    static func linear(_ input: [Float], from inRate: Int, to outRate: Int) -> [Float] {
        guard inRate != outRate, !input.isEmpty else { return input }
        let ratio = Double(inRate) / Double(outRate)
        let outLen = Int(Double(input.count) / ratio)
        guard outLen > 0 else { return [] }
        var out = [Float](repeating: 0, count: outLen)
        for i in 0..<outLen {
            let srcIdx = Double(i) * ratio
            let i0 = Int(srcIdx)
            let i1 = min(i0 + 1, input.count - 1)
            let t = Float(srcIdx - Double(i0))
            out[i] = input[i0] * (1 - t) + input[i1] * t
        }
        return out
    }
}
