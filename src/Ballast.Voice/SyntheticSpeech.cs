namespace Ballast.Voice;

/// <summary>Speech-shaped test signal for headless voice tests and tools: no microphone in CI.</summary>
public static class SyntheticSpeech
{
    /// <summary>Something speech-shaped: a 140 Hz voice with formants, in syllables.</summary>
    public static float[] Generate(double seconds, int seed = 1)
    {
        var rng = new Random(seed);
        var s = new float[(int)(seconds * VoiceFormat.SampleRate)];
        for (int i = 0; i < s.Length; i++)
        {
            double t = (double)i / VoiceFormat.SampleRate;
            double syllable = Math.Max(0, Math.Sin(2 * Math.PI * 4 * t));
            double v = 0;
            for (int h = 1; h < 25; h++)
            {
                double f = 140 * h;
                double formant = Math.Exp(-Math.Pow((f - 700) / 250, 2)) + 0.6 * Math.Exp(-Math.Pow((f - 1200) / 300, 2)) + 0.3 * Math.Exp(-Math.Pow((f - 2600) / 400, 2));
                v += formant * Math.Sin(2 * Math.PI * f * t);
            }
            s[i] = (float)(0.25 * syllable * v + 0.002 * (rng.NextDouble() - 0.5));
        }
        return s;
    }

}
