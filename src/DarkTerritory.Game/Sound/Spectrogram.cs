using Ballast.Audio;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// A picture of a render: time across, log frequency up (50 Hz to 12 kHz), level as brightness. The audio
/// counterpart of a screenshot, so an agent can see the bands spec A.4 allocates without listening.
/// </summary>
public static class Spectrogram
{
    const int Window = 2048;

    /// <summary>RGBA pixels, <paramref name="width"/> × <paramref name="height"/>.</summary>
    public static byte[] Render(ReadOnlySpan<float> stereo, int width = 800, int height = 300, double floorDb = -75)
    {
        int frames = stereo.Length / 2;
        var mono = new float[frames];
        for (int i = 0; i < frames; i++)
            mono[i] = 0.5f * (stereo[i * 2] + stereo[i * 2 + 1]);
        var pixels = new byte[width * height * 4];
        var re = new double[Window];
        var im = new double[Window];
        for (int x = 0; x < width; x++)
        {
            int start = (int)((long)x * Math.Max(0, frames - Window) / Math.Max(1, width - 1));
            for (int i = 0; i < Window; i++)
            {
                double hann = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (Window - 1));
                re[i] = start + i < frames ? mono[start + i] * hann : 0;
                im[i] = 0;
            }
            Fft(re, im);
            for (int y = 0; y < height; y++)
            {
                // Log frequency axis, low at the bottom.
                double f = 50 * Math.Pow(12000.0 / 50, (double)(height - 1 - y) / (height - 1));
                int bin = Math.Clamp((int)Math.Round(f * Window / Audio.SampleRate), 1, Window / 2 - 1);
                double mag = Math.Sqrt(re[bin] * re[bin] + im[bin] * im[bin]) * 4 / Window;
                double t = Math.Clamp((Audio.GainToDb(mag) - floorDb) / -floorDb, 0, 1);
                var (r, g, b) = Heat(t);
                int p = (y * width + x) * 4;
                pixels[p] = r;
                pixels[p + 1] = g;
                pixels[p + 2] = b;
                pixels[p + 3] = 255;
            }
        }
        // Gridlines at 100 Hz, 1 kHz and 10 kHz.
        foreach (double f in new[] { 100.0, 1000, 10000 })
        {
            int y = (int)Math.Round((height - 1) * (1 - Math.Log(f / 50) / Math.Log(12000.0 / 50)));
            for (int x = 0; x < width; x += 3)
            {
                int p = (y * width + x) * 4;
                pixels[p] = pixels[p + 1] = pixels[p + 2] = 160;
            }
        }
        return pixels;
    }

    static (byte, byte, byte) Heat(double t) => (
        (byte)(255 * Math.Clamp(t * 2.2, 0, 1)),
        (byte)(255 * Math.Clamp(t * 2.2 - 0.9, 0, 1)),
        (byte)(255 * Math.Clamp(t * 3 - 2, 0, 1) * 0.8 + 40 * (1 - t)));

    /// <summary>In-place radix-2 FFT.</summary>
    static void Fft(double[] re, double[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
                j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double angle = -2 * Math.PI / len;
            for (int i = 0; i < n; i += len)
                for (int k = 0; k < len / 2; k++)
                {
                    double wr = Math.Cos(angle * k), wi = Math.Sin(angle * k);
                    int a = i + k, b = a + len / 2;
                    double xr = re[b] * wr - im[b] * wi, xi = re[b] * wi + im[b] * wr;
                    re[b] = re[a] - xr;
                    im[b] = im[a] - xi;
                    re[a] += xr;
                    im[a] += xi;
                }
        }
    }
}
