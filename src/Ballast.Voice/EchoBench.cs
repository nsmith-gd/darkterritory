namespace Ballast.Voice;

/// <summary>What <see cref="EchoBench.Run"/> measured: how much of A's own voice comes back to A from B's room, and how much of B is kept.</summary>
/// <param name="EchoDb">A's voice in B's mic, against A's voice as B's speakers played it (dB).</param>
/// <param name="SuppressionDb">How far under that echo what B sends is, A talking alone, once the room's learned (dB).</param>
/// <param name="ConvergedSeconds">How long A talks before a tenth of a second's echo is first 20 dB down (s).</param>
/// <param name="RoomDelaySeconds">The echo's delay in B's mic after A's voice left B's speakers, as measured (s).</param>
/// <param name="ReturnsAfterSeconds">When what's left of it would reach A after A said it: the room, the canceller and the network both ways (s).</param>
/// <param name="NearKeptDb">B's voice in what B sends, B talking alone (dB: 0 all of it).</param>
/// <param name="DoubleTalkKeptDb">B's voice in what B sends, both talking at once (dB).</param>
/// <param name="AfterDoubleTalkDb">How far under the echo what B sends is in the first second A talks alone again after both talked:
/// the near voice over the echo mustn't have thrown the canceller off (dB).</param>
/// <param name="OpenBefore">The share of A-alone frames B's open mic sent with no canceller: the bug.</param>
/// <param name="OpenOnEcho">The same with the canceller and the gate.</param>
/// <param name="OpenOnNear">The share of B-alone frames sent (the voice activity's own share of a talker's frames, pauses and all).</param>
/// <param name="OpenOnNearBefore">The same, no canceller: what the gate's to keep.</param>
public sealed record EchoBenchReport(double EchoDb, double SuppressionDb, double ConvergedSeconds, double RoomDelaySeconds, double ReturnsAfterSeconds,
    double NearKeptDb, double DoubleTalkKeptDb, double AfterDoubleTalkDb, double OpenBefore, double OpenOnEcho, double OpenOnNear, double OpenOnNearBefore);

/// <summary>
/// Note 1019's check, headless: crewmate A talks; B's speakers play it into B's room, where it comes back into B's open mic
/// <paramref name="roomDelay"/> later (the devices' latency and the air), <paramref name="couplingDb"/> under what was played,
/// with a room's tail (<paramref name="rt60"/>). For six seconds A talks alone, then B alone for three, then both for three,
/// then A alone for two.
/// B's capture runs as the game's does (the canceller, the gate, the voice activity), and the report says what reached A.
/// </summary>
public static class EchoBench
{
    public static EchoBenchReport Run(EchoSettings? settings = null, double roomDelay = 0.05, double rt60 = 0.3, double couplingDb = -10,
        double networkSeconds = 0.1, double jitterHold = 0.06, int seed = 1)
    {
        settings ??= new EchoSettings();
        const int Rate = VoiceFormat.SampleRate, Frame = VoiceFormat.FrameSamples;
        int total = 14 * Rate, aEnd = 6 * Rate, bStart = 6 * Rate, dtStart = 9 * Rate, dtEnd = 12 * Rate;
        var a = SyntheticSpeech.Talker(14, seed, 140, -20);
        var b = SyntheticSpeech.Talker(14, seed + 100, 210, -20);
        for (int i = aEnd; i < dtStart; i++)
            a[i] = 0;
        Array.Clear(b, 0, bStart);
        Array.Clear(b, dtEnd, total - dtEnd);

        // The room: the direct path, then a sparse decaying tail.
        var rng = new Random(seed);
        var taps = new List<(int At, float Gain)> { ((int)(roomDelay * Rate), (float)Math.Pow(10, couplingDb / 20)) };
        for (int t = 0; t < 400; t++)
        {
            double after = rng.NextDouble() * rt60;
            taps.Add(((int)((roomDelay + 0.002 + after) * Rate), (float)(taps[0].Gain * 0.25 * (rng.NextDouble() * 2 - 1) * Math.Pow(10, -3 * after / rt60))));
        }
        var echo = new float[total];
        foreach (var (at, gain) in taps)
            for (int i = at; i < total; i++)
                echo[i] += gain * a[i - at];
        var mic = new float[total];
        for (int i = 0; i < total; i++)
            mic[i] = echo[i] + b[i];

        var canceller = new EchoCanceller(settings);
        var activity = new VoiceActivity();
        var raw = new VoiceActivity();
        var silence = new float[Frame];
        var output = (float[])mic.Clone();
        var open = new bool[total / Frame];
        var openRaw = new bool[total / Frame];
        for (int f = 0; f < open.Length; f++)
        {
            var frame = output.AsSpan(f * Frame, Frame);
            openRaw[f] = raw.Update(frame);
            canceller.Process(frame, a.AsSpan(f * Frame, Frame));
            open[f] = activity.Update(canceller.MostlyEcho ? silence : frame);
        }
        int late = canceller.Latency;
        double Share(bool[] which, int from, int to) => which[(from / Frame)..(to / Frame)].Count(o => o) / (double)((to - from) / Frame);
        float Out(int i) => i + late < total ? output[i + late] : 0;

        double Ratio(Func<int, double> num, Func<int, double> den, int from, int to)
        {
            double n = 0, d = 0;
            for (int i = from; i < to; i++)
            {
                n += num(i);
                d += den(i);
            }
            return 10 * Math.Log10(Math.Max(n, 1e-20) / Math.Max(d, 1e-20));
        }
        // B's voice's share of what's sent, by projection, so A's echo under it doesn't count as kept.
        double Kept(int from, int to)
        {
            double ob = 0, bb = 0;
            for (int i = from; i < to; i++)
            {
                ob += Out(i) * b[i];
                bb += b[i] * b[i];
            }
            return 20 * Math.Log10(Math.Max(ob / Math.Max(bb, 1e-20), 1e-6));
        }
        double echoDb = Ratio(i => echo[i] * echo[i], i => a[i] * a[i], 0, aEnd);
        double suppression = Ratio(i => echo[i] * echo[i], i => Out(i) * Out(i), 2 * Rate, aEnd);
        double converged = double.NaN;
        for (int w = 0; w + Rate / 10 <= aEnd; w += Rate / 20)
            if (Ratio(i => echo[i] * echo[i], _ => 1, w, w + Rate / 10) > -60
                && Ratio(i => echo[i] * echo[i], i => Out(i) * Out(i), w, w + Rate / 10) >= 20)
            {
                converged = (w + Rate / 10.0) / Rate;
                break;
            }
        double nearKept = Kept(bStart + Rate / 2, dtStart);
        double dtKept = Kept(dtStart + Rate / 2, dtEnd);
        // Past B's last word's own echo-free tail: the room's echo of A only.
        double recovered = Ratio(i => echo[i] * echo[i], i => Out(i) * Out(i), dtEnd + Rate / 10, dtEnd + Rate);
        double delay = Delay(a, mic, aEnd, (int)(0.5 * Rate));
        return new EchoBenchReport(Math.Round(echoDb, 1), Math.Round(suppression, 1), converged, Math.Round(delay, 4),
            Math.Round(delay + (double)late / Rate + 2 * networkSeconds + jitterHold + (double)Frame / Rate, 3),
            Math.Round(nearKept, 2), Math.Round(dtKept, 2), Math.Round(recovered, 1),
            Math.Round(Share(openRaw, 2 * Rate, aEnd), 3), Math.Round(Share(open, 2 * Rate, aEnd), 3),
            Math.Round(Share(open, bStart + Rate / 2, dtStart), 3), Math.Round(Share(openRaw, bStart + Rate / 2, dtStart), 3));
    }

    /// <summary>Where <paramref name="heard"/> best matches <paramref name="said"/> delayed (s), searched to <paramref name="maxLag"/> at a sixth of the rate.</summary>
    static double Delay(float[] said, float[] heard, int length, int maxLag)
    {
        const int Step = 6;
        double best = double.MinValue;
        int at = 0;
        for (int lag = 0; lag < maxLag; lag += Step)
        {
            double sum = 0;
            for (int i = lag; i < length; i += Step)
                sum += said[i - lag] * heard[i];
            if (sum > best)
                (best, at) = (sum, lag);
        }
        // Then to the sample around it.
        best = double.MinValue;
        int coarse = at;
        for (int lag = Math.Max(0, coarse - Step); lag <= coarse + Step; lag++)
        {
            double sum = 0;
            for (int i = lag; i < length; i++)
                sum += said[i - lag] * heard[i];
            if (sum > best)
                (best, at) = (sum, lag);
        }
        return (double)at / VoiceFormat.SampleRate;
    }
}
