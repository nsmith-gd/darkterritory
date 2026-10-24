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

    /// <summary>
    /// A talker who doesn't repeat (note 1019's echo bench): syllables of uneven length and vowel, pauses between words, the pitch
    /// wandering about <paramref name="pitchHz"/>, brought to <paramref name="levelDb"/> (dBFS RMS while talking). The
    /// <see cref="Generate"/> voice repeats every quarter second, which an echo's delay can't be measured against.
    /// </summary>
    public static float[] Talker(double seconds, int seed, double pitchHz = 140, double levelDb = -20)
    {
        (double F1, double F2, double F3)[] vowels = [(730, 1090, 2440), (270, 2290, 3010), (530, 1840, 2480), (570, 840, 2410), (300, 870, 2240), (660, 1720, 2410)];
        var rng = new Random(seed);
        var s = new float[(int)(seconds * VoiceFormat.SampleRate)];
        double phase = 0, talking = 0;
        int i = 0, voiced = 0;
        while (i < s.Length)
        {
            int length = (int)((0.12 + 0.2 * rng.NextDouble()) * VoiceFormat.SampleRate);
            var (f1, f2, f3) = vowels[rng.Next(vowels.Length)];
            double p0 = pitchHz * (0.85 + 0.3 * rng.NextDouble()), p1 = pitchHz * (0.85 + 0.3 * rng.NextDouble());
            for (int n = 0; n < length && i < s.Length; n++, i++)
            {
                double u = (double)n / length, f0 = p0 + (p1 - p0) * u;
                phase += 2 * Math.PI * f0 / VoiceFormat.SampleRate;
                double v = 0;
                for (int h = 1; h * f0 < 4000; h++)
                {
                    double f = h * f0;
                    double formant = Math.Exp(-Math.Pow((f - f1) / 120, 2)) + 0.6 * Math.Exp(-Math.Pow((f - f2) / 180, 2)) + 0.3 * Math.Exp(-Math.Pow((f - f3) / 250, 2));
                    v += formant * Math.Sin(h * phase);
                }
                double envelope = Math.Sin(Math.PI * u);
                s[i] = (float)(envelope * v);
                talking += s[i] * s[i];
                voiced++;
            }
            // A pause between words, now and then.
            if (rng.NextDouble() < 0.3)
                i += (int)((0.08 + 0.25 * rng.NextDouble()) * VoiceFormat.SampleRate);
        }
        double gain = Math.Pow(10, levelDb / 20) / Math.Sqrt(talking / Math.Max(1, voiced));
        for (int n = 0; n < s.Length; n++)
            s[n] = (float)(s[n] * gain + 0.0005 * (rng.NextDouble() - 0.5));
        return s;
    }
}
