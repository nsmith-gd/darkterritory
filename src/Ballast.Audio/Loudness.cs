namespace Ballast.Audio;

/// <summary>
/// Integrated loudness in LUFS (ITU-R BS.1770-4 / EBU R128): K-weighting (a high shelf for the head, then a high-pass),
/// mean square over 400 ms blocks overlapping by 75%, an absolute gate at −70 LUFS and a relative gate 10 LU under the
/// ungated level. GDD v1.4 App. E.6 normalises every music track to −16 LUFS through its manifest gain; this is what
/// measures it, for the generator that writes the manifest and the test that holds the manifest to the files.
/// </summary>
public static class Loudness
{
    /// <summary>Integrated loudness of a mono signal; −70 or below reads as silence (<see cref="double.NegativeInfinity"/>).</summary>
    public static double Integrated(ReadOnlySpan<float> mono, int sampleRate)
    {
        // The two K-weighting stages, designed for this rate by the bilinear transform (the standard gives 48 kHz
        // coefficients; these reproduce them there and stay right at 22.05 kHz).
        var shelf = Design(sampleRate, 1681.974450955533, 3.999843853973347, 0.7071752369554196, shelf: true);
        var highPass = Design(sampleRate, 38.13547087602444, 0, 0.5003270373238773, shelf: false);
        var weighted = new double[mono.Length];
        double s1 = 0, s2 = 0, h1 = 0, h2 = 0;
        for (int i = 0; i < mono.Length; i++)
        {
            double x = mono[i];
            double y = shelf.B0 * x + s1;
            s1 = shelf.B1 * x - shelf.A1 * y + s2;
            s2 = shelf.B2 * x - shelf.A2 * y;
            double z = highPass.B0 * y + h1;
            h1 = highPass.B1 * y - highPass.A1 * z + h2;
            h2 = highPass.B2 * y - highPass.A2 * z;
            weighted[i] = z * z;
        }

        int block = (int)Math.Round(0.4 * sampleRate), hop = Math.Max(1, block / 4);
        var powers = new List<double>();
        for (int start = 0; start + block <= weighted.Length; start += hop)
        {
            double sum = 0;
            for (int i = start; i < start + block; i++)
                sum += weighted[i];
            powers.Add(sum / block);
        }
        static double Lufs(double power) => -0.691 + 10 * Math.Log10(Math.Max(power, 1e-30));
        var loud = powers.Where(p => Lufs(p) > -70).ToList();
        if (loud.Count == 0)
            return double.NegativeInfinity;
        double relative = Lufs(loud.Average()) - 10;
        var gated = loud.Where(p => Lufs(p) > relative).ToList();
        return Lufs(gated.Average());
    }

    readonly record struct Coefficients(double B0, double B1, double B2, double A1, double A2);

    static Coefficients Design(int rate, double frequency, double gainDb, double q, bool shelf)
    {
        double k = Math.Tan(Math.PI * frequency / rate);
        if (shelf)
        {
            double vh = Math.Pow(10, gainDb / 20), vb = Math.Pow(vh, 0.4996667741545416);
            double a0 = 1 + k / q + k * k;
            return new((vh + vb * k / q + k * k) / a0, 2 * (k * k - vh) / a0, (vh - vb * k / q + k * k) / a0,
                2 * (k * k - 1) / a0, (1 - k / q + k * k) / a0);
        }
        double d = 1 + k / q + k * k;
        return new(1, -2, 1, 2 * (k * k - 1) / d, (1 - k / q + k * k) / d);
    }
}
