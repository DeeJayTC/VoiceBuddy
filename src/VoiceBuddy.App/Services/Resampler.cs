namespace VoiceBuddy.Services;

/// <summary>
/// Linear resampler for mono float PCM. Good enough for speech in the 16 kHz Whisper pipeline;
/// a polyphase filter would be better but not worth the complexity before the STT stage exists.
/// </summary>
public static class Resampler
{
    public static float[] Linear(float[] input, int inputRate, int outputRate)
    {
        if (inputRate == outputRate || input.Length == 0) return input;
        double ratio = (double)inputRate / outputRate;
        int outLen = (int)(input.Length / ratio);
        if (outLen <= 0) return Array.Empty<float>();

        var output = new float[outLen];
        for (int i = 0; i < outLen; i++)
        {
            double srcIdx = i * ratio;
            int idx0 = (int)srcIdx;
            int idx1 = idx0 + 1 < input.Length ? idx0 + 1 : input.Length - 1;
            float t = (float)(srcIdx - idx0);
            output[i] = input[idx0] * (1 - t) + input[idx1] * t;
        }
        return output;
    }
}
