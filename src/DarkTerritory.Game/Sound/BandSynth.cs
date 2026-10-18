using Ballast.Audio;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// The band's instruments, made (ARCHITECTURE §8 note 700): each note of a part rendered once into a clip, as the clerk's lines
/// are, and played where its player stands. A kitchen party's sound, not a concert's: a fiddle bowed with a little vibrato, a
/// tin whistle's breath and chiff, a button accordion's beating reeds, a tenor banjo's bright pluck, a guitar's warmer one, a
/// bodhrán's goatskin under a tipper, the spoons' clack and an ugly stick's boot and bottle caps. Seeded, so every machine
/// (and every bench render) makes the same notes.
/// </summary>
public static class BandSynth
{
    const int Rate = Audio.SampleRate;

    /// <summary>A note's clip: <paramref name="pitch"/> (MIDI; for the percussion, which stroke) held <paramref name="seconds"/>.</summary>
    public static float[] Render(string instrument, int pitch, double seconds, double velocity)
    {
        // FNV over the instrument and the note (not HashCode, which is salted per process): the same note, the same noise.
        uint seed = 2166136261;
        foreach (char c in $"{instrument}/{pitch}/{(int)(seconds * 1000)}")
            seed = (seed ^ c) * 16777619;
        var noise = new Noise(seed);
        float[] note = instrument switch
        {
            "fiddle" => Fiddle(Hz(pitch), seconds, ref noise),
            "whistle" => Whistle(Hz(pitch), seconds, ref noise),
            "accordion" => Accordion(Hz(pitch), seconds),
            "banjo" => Pluck(Hz(pitch), seconds, pluckAt: 0.11, decay: 5.5, perPartial: 1.6, brightness: 7000, tail: 0.25, ref noise),
            "guitar" => Pluck(Hz(pitch), seconds, pluckAt: 0.2, decay: 2.2, perPartial: 0.7, brightness: 3800, tail: 0.35, ref noise),
            "bodhran" => Bodhran(pitch == 0, ref noise),
            "spoons" => Spoons(pitch == 0, ref noise),
            "uglyStick" => UglyStick(pitch == 0, ref noise),
            _ => Accordion(Hz(pitch), seconds),
        };
        // Every note to one peak, the stroke's weight on top: the band's levels are its sounds' (GameAudio's band-* sounds).
        float peak = 1e-6f;
        foreach (float x in note)
            peak = Math.Max(peak, Math.Abs(x));
        float scale = (float)(0.5 * Math.Clamp(velocity, 0, 1) / peak);
        for (int i = 0; i < note.Length; i++)
            note[i] *= scale;
        return note;
    }

    /// <summary>
    /// The La Cucaracha horn (note 701): the whole phrase every pull, on a musical car horn's trumpets (brassy, a little
    /// nasal): da-da-da DUM DUM, da-da-da DUM DUM, DUM-DUM-DUM-DUM-DUM-DUM-DUM. Traditional, public domain.
    /// </summary>
    public static float[] Cucaracha()
    {
        // (MIDI, start s, length s): G G G C E, G G G C E, C C B B A A G.
        var notes = new List<(int Pitch, double At, double Length)>();
        foreach (double phrase in new[] { 0.0, 1.35 })
        {
            for (int i = 0; i < 3; i++)
                notes.Add((67, phrase + 0.13 * i, 0.075));
            notes.Add((72, phrase + 0.39, 0.3));
            notes.Add((76, phrase + 0.75, 0.45));
        }
        int[] fall = [72, 72, 71, 71, 69, 69, 67];
        for (int i = 0; i < fall.Length; i++)
            notes.Add((fall[i], 2.75 + 0.2 * i, i == fall.Length - 1 ? 0.55 : 0.13));
        var o = new float[Samples(4.6)];
        foreach (var (pitch, at, length) in notes)
        {
            double hz = Hz(pitch);
            var note = new float[Samples(length + 0.04)];
            // Two horns a hair apart, a reed's buzz: odd harmonics strong, the top rolled off by the bell. Tongued: each note
            // stopped short, so the repeated ones are heard as three.
            Harmonics(note, hz, k => (k % 2 == 1 ? 1.0 : 0.6) / k, _ => 1, t => Held(t, length, 0.012, 0.02), 0.6);
            Harmonics(note, hz * 1.004, k => (k % 2 == 1 ? 1.0 : 0.6) / k, _ => 1, t => Held(t, length, 0.012, 0.02), 0.4);
            int from = (int)(at * Rate);
            for (int i = 0; i < note.Length && from + i < o.Length; i++)
                o[from + i] += note[i];
        }
        Filter(o, (FilterType.Peak, 1400, 1.2, 6), (FilterType.LowPass, 4500, 0.7, 0), (FilterType.HighPass, 250, 0.7, 0));
        float peak = 1e-6f;
        foreach (float x in o)
            peak = Math.Max(peak, Math.Abs(x));
        for (int i = 0; i < o.Length; i++)
            o[i] *= 0.6f / peak;
        return o;
    }

    /// <summary>
    /// The steam calliope's tune (note 715): "Pop Goes the Weasel" (traditional, public domain), a note a pull of the cord, round
    /// and round. MIDI pitches, the pipes' octave.
    /// </summary>
    public static readonly int[] CalliopeTune =
    [
        79, 84, 84, 86, 86, 88, 91, 88, 84, 79, 84, 84, 86, 86, 88, 84,
        79, 84, 84, 86, 86, 88, 91, 88, 84, 81, 86, 89, 88, 84,
    ];

    /// <summary>
    /// A steam calliope's pipe (note 715): a whistle voiced on steam, bright and breathy and a little out, its vibrato the
    /// steam's waver, held <paramref name="seconds"/> and cut off sharp at the valve.
    /// </summary>
    public static float[] Calliope(int pitch, double seconds)
    {
        var noise = new Noise((uint)(pitch * 7919));
        double hz = Hz(pitch), release = 0.08;
        var o = new float[Samples(seconds + release)];
        Harmonics(o, hz, k => k == 1 ? 1.0 : k == 2 ? 0.55 : k == 3 ? 0.3 : 0.12 / k, t => 1.003 + 0.004 * Math.Sin(2 * Math.PI * 6.3 * t),
            t => Held(t, seconds, 0.03, release));
        var steam = new Biquad();
        steam.Set(FilterType.BandPass, hz * 2, 2);
        for (int i = 0; i < o.Length; i++)
            o[i] += steam.Process(noise.Next()) * 0.22f * (float)Held((double)i / Rate, seconds, 0.01, release);
        Filter(o, (FilterType.HighPass, 300, 0.7, 0), (FilterType.Peak, 2200, 1.0, 3));
        Normalise(o, 0.7f);
        return o;
    }

    /// <summary>
    /// A canary's song (note 715): a run of quick rolling trills and chips up high, each phrase its own by <paramref name="seed"/>
    /// (synthesised: no recording of one came through the intake).
    /// </summary>
    public static float[] Canary(int seed)
    {
        var noise = new Noise((uint)(seed * 104729 + 1));
        var o = new float[Samples(2.6)];
        double at = 0.05;
        while (at < 2.3)
        {
            // A trill: one chirp repeated 6 to 14 times, falling or rising within it.
            int reps = 6 + (int)(Math.Abs(noise.Next()) * 9);
            double base_ = 2600 + Math.Abs(noise.Next()) * 2600, sweep = (noise.Next()) * 1400, len = 0.03 + Math.Abs(noise.Next()) * 0.04;
            for (int r = 0; r < reps && at < 2.4; r++)
            {
                int from = (int)(at * Rate), n = Samples(len);
                double phase = 0;
                for (int i = 0; i < n && from + i < o.Length; i++)
                {
                    double t = (double)i / n;
                    phase += 2 * Math.PI * (base_ + sweep * t) / Rate;
                    o[from + i] += (float)(Math.Sin(phase) * Math.Sin(Math.PI * t) * 0.6);
                }
                at += len + 0.012;
            }
            at += 0.05 + Math.Abs(noise.Next()) * 0.12;
        }
        Normalise(o, 0.5f);
        return o;
    }

    /// <summary>The yard cat's hiss (note 745): a long breathy rasp, its top bright, swelling and cut off.</summary>
    public static float[] Hiss(int seed)
    {
        var noise = new Noise((uint)(seed * 7 + 3));
        var o = new float[Samples(0.9)];
        var band = new Biquad();
        band.Set(FilterType.BandPass, 4200, 0.9);
        for (int i = 0; i < o.Length; i++)
        {
            double t = (double)i / Rate;
            o[i] = band.Process(noise.Next()) * (float)(Math.Min(1, t / 0.08) * Math.Max(0, 1 - Math.Max(0, t - 0.7) / 0.2));
        }
        Normalise(o, 0.6f);
        return o;
    }

    /// <summary>The yard cat's meow (note 745): a rising then falling cry, nasal.</summary>
    public static float[] Meow(int seed)
    {
        var o = new float[Samples(0.75)];
        double hz = 520 + seed % 3 * 60;
        Harmonics(o, hz, k => 1.0 / (k * 0.8), t => 1 + 0.35 * Math.Sin(Math.PI * Math.Min(1, t / 0.6)), t => Held(t, 0.6, 0.06, 0.12), 0.8);
        Filter(o, (FilterType.Peak, 1600, 1.5, 8), (FilterType.HighPass, 300, 0.7, 0));
        Normalise(o, 0.5f);
        return o;
    }

    /// <summary>The dog's bark (note 745): a short gruff "wuff", a pitch drop under a noisy chest.</summary>
    public static float[] Bark(int seed)
    {
        var noise = new Noise((uint)(seed * 13 + 5));
        var o = new float[Samples(0.3)];
        double hz = 210 + seed % 3 * 25;
        Harmonics(o, hz, k => 1.0 / k, t => 1.25 - 0.5 * Math.Min(1, t / 0.18), t => Held(t, 0.16, 0.01, 0.1), 0.8);
        var chest = new Biquad();
        chest.Set(FilterType.BandPass, 900, 1.2);
        for (int i = 0; i < o.Length; i++)
            o[i] += chest.Process(noise.Next()) * 0.5f * (float)Held((double)i / Rate, 0.12, 0.005, 0.08);
        Filter(o, (FilterType.Peak, 700, 1.0, 6), (FilterType.LowPass, 3500, 0.7, 0));
        Normalise(o, 0.7f);
        return o;
    }

    static void Normalise(float[] o, float to)
    {
        float peak = 1e-6f;
        foreach (float x in o)
            peak = Math.Max(peak, Math.Abs(x));
        for (int i = 0; i < o.Length; i++)
            o[i] *= to / peak;
    }

    static double Hz(int midi) => 440 * Math.Pow(2, (midi - 69) / 12.0);

    static int Samples(double seconds) => Math.Max(1, (int)(seconds * Rate));

    /// <summary>A held note's level: up over <paramref name="attack"/>, held, and down over <paramref name="release"/> after it.</summary>
    static double Held(double t, double seconds, double attack, double release) =>
        t < attack ? Math.Sin(t / attack * Math.PI / 2) : t < seconds ? 1 : Math.Max(0, 1 - (t - seconds) / release);

    /// <summary>
    /// A sum of harmonics, each <paramref name="amp"/>(k) of the fundamental, the pitch moved by <paramref name="vibrato"/>(t)
    /// (a ratio), up to what a tune's top notes leave under 9 kHz. The sines by recurrence: one sin and cos a sample.
    /// </summary>
    static void Harmonics(float[] into, double hz, Func<int, double> amp, Func<double, double> vibrato, Func<double, double> level, double gain = 1)
    {
        int top = Math.Max(1, Math.Min(24, (int)(9000 / hz)));
        var a = new double[top + 1];
        for (int k = 1; k <= top; k++)
            a[k] = amp(k);
        double phase = 0;
        for (int i = 0; i < into.Length; i++)
        {
            double t = (double)i / Rate, l = level(t);
            phase += 2 * Math.PI * hz * vibrato(t) / Rate;
            if (l <= 0)
                continue;
            double s1 = Math.Sin(phase), c2 = 2 * Math.Cos(phase), prev = 0, cur = s1, sum = 0;
            for (int k = 1; k <= top; k++)
            {
                sum += a[k] * cur;
                (prev, cur) = (cur, c2 * cur - prev);
            }
            into[i] += (float)(sum * l * gain);
        }
    }

    static float[] Fiddle(double hz, double seconds, ref Noise noise)
    {
        double release = 0.12, attack = Math.Min(0.06, seconds * 0.3);
        var o = new float[Samples(seconds + release)];
        // A bowed string's saw, its vibrato coming in once the note's held a moment.
        Harmonics(o, hz, k => 1.0 / k, t => 1 + 0.0035 * Math.Min(1, Math.Max(0, (t - 0.12) / 0.15)) * Math.Sin(2 * Math.PI * 5.6 * t),
            t => Held(t, seconds, attack, release) * (0.85 + 0.15 * Math.Min(1, t / 0.2)));
        // The bow's hair on the string, then the box's two bumps (its air and its plate) and its top rolled off.
        var hair = new Biquad();
        hair.Set(FilterType.BandPass, 3000, 1.2);
        for (int i = 0; i < o.Length; i++)
            o[i] += hair.Process(noise.Next()) * 0.05f * (float)Held((double)i / Rate, seconds, attack, release);
        Filter(o, (FilterType.Peak, 450, 1.0, 4), (FilterType.Peak, 2800, 1.4, 5), (FilterType.LowPass, 7000, 0.7, 0));
        return o;
    }

    static float[] Whistle(double hz, double seconds, ref Noise noise)
    {
        double release = 0.06, attack = 0.02;
        var o = new float[Samples(seconds + release)];
        Harmonics(o, hz, k => k switch { 1 => 1, 2 => 0.12, 3 => 0.05, _ => 0 },
            t => 1 + 0.0025 * Math.Min(1, Math.Max(0, (t - 0.2) / 0.2)) * Math.Sin(2 * Math.PI * 5.2 * t), t => Held(t, seconds, attack, release));
        // Breath through the windway, round the note; and the chiff as the tongue lets it go.
        var breath = new Biquad();
        breath.Set(FilterType.BandPass, hz, 6);
        var chiff = new Biquad();
        chiff.Set(FilterType.BandPass, hz * 2, 2);
        for (int i = 0; i < o.Length; i++)
        {
            double t = (double)i / Rate;
            float n = noise.Next();
            o[i] += breath.Process(n) * 0.35f * (float)Held(t, seconds, attack, release) + n * 0.012f * (float)Held(t, seconds, attack, release)
                + chiff.Process(n) * (float)(0.5 * Math.Exp(-t / 0.012));
        }
        return o;
    }

    static float[] Accordion(double hz, double seconds)
    {
        double release = 0.08, attack = 0.035;
        var o = new float[Samples(seconds + release)];
        // Two reeds a hair apart (the "wet" tuning that makes it beat) and one an octave down; a reed's odd harmonics strong.
        static double Reed(int k) => (k % 2 == 1 ? 1 : 0.55) / Math.Pow(k, 0.8);
        Harmonics(o, hz * 0.9975, Reed, _ => 1, t => Held(t, seconds, attack, release), 0.5);
        Harmonics(o, hz * 1.0025, Reed, _ => 1, t => Held(t, seconds, attack, release), 0.5);
        Harmonics(o, hz / 2, Reed, _ => 1, t => Held(t, seconds, attack, release), 0.3);
        Filter(o, (FilterType.LowPass, 5000, 0.7, 0), (FilterType.Peak, 1200, 0.8, 3));
        return o;
    }

    /// <summary>
    /// A plucked string: its partials as a pluck at <paramref name="pluckAt"/> of its length leaves them, each dying faster
    /// the higher it is; a pick's click; let ring <paramref name="tail"/> past the note and then damped.
    /// </summary>
    static float[] Pluck(double hz, double seconds, double pluckAt, double decay, double perPartial, double brightness, double tail, ref Noise noise)
    {
        double ring = seconds + tail;
        var o = new float[Samples(ring + 0.05)];
        int top = Math.Max(1, Math.Min(20, (int)(brightness / hz)));
        for (int k = 1; k <= top; k++)
        {
            double a = Math.Sin(k * Math.PI * pluckAt) / k, d = decay + perPartial * k, f = hz * k * (1 + 0.0004 * k * k);
            for (int i = 0; i < o.Length; i++)
            {
                double t = (double)i / Rate;
                o[i] += (float)(a * Math.Exp(-d * t) * Math.Sin(2 * Math.PI * f * t));
            }
        }
        var click = new Biquad();
        click.Set(FilterType.HighPass, 2500, 0.7);
        for (int i = 0; i < o.Length; i++)
        {
            double t = (double)i / Rate;
            o[i] += click.Process(noise.Next()) * (float)(0.25 * Math.Exp(-t / 0.003));
            // Damped once the note's done: the hand on the strings.
            if (t > ring)
                o[i] *= (float)Math.Max(0, 1 - (t - ring) / 0.05);
        }
        return o;
    }

    /// <summary>The bodhrán: a down-stroke's deep thump of the skin, or the tipper's lighter, higher tap.</summary>
    static float[] Bodhran(bool down, ref Noise noise)
    {
        var o = new float[Samples(down ? 0.5 : 0.25)];
        double low = down ? 70 : 105, drop = down ? 55 : 60, ring = down ? 0.16 : 0.07;
        var skin = new Biquad();
        skin.Set(FilterType.LowPass, down ? 900 : 2200, 0.7);
        double phase = 0, phase2 = 0;
        for (int i = 0; i < o.Length; i++)
        {
            double t = (double)i / Rate, f = low + drop * Math.Exp(-t / 0.03);
            phase += 2 * Math.PI * f / Rate;
            phase2 += 2 * Math.PI * f * 1.59 / Rate;
            o[i] = (float)(Math.Sin(phase) * Math.Exp(-t / ring) + 0.3 * Math.Sin(phase2) * Math.Exp(-t / (ring * 0.5)))
                + skin.Process(noise.Next()) * (float)((down ? 0.5 : 0.7) * Math.Exp(-t / (down ? 0.02 : 0.012)));
        }
        return o;
    }

    /// <summary>Two spoons back to back, on the knee and the hand: a clack (two hits a moment apart), or the light one.</summary>
    static float[] Spoons(bool clack, ref Noise noise)
    {
        var o = new float[Samples(0.15)];
        foreach (double at in clack ? new[] { 0.0, 0.014 } : [0.0])
            Strike(o, at, [(2350, 1.0), (3700, 0.6), (5900, 0.35)], 0.025, clack ? 1 : 0.6, 4200, 0.006, ref noise);
        return o;
    }

    /// <summary>
    /// The ugly stick (Newfoundland's: a mop handle, a rubber boot, bottle caps and a tin can): the boot's thump on the floor
    /// with the caps, or the caps and the can alone, struck with the stick.
    /// </summary>
    static float[] UglyStick(bool boot, ref Noise noise)
    {
        var o = new float[Samples(0.35)];
        if (boot)
        {
            double phase = 0;
            for (int i = 0; i < o.Length; i++)
            {
                double t = (double)i / Rate;
                phase += 2 * Math.PI * (55 + 40 * Math.Exp(-t / 0.025)) / Rate;
                o[i] += (float)(Math.Sin(phase) * Math.Exp(-t / 0.12));
            }
        }
        else
            Strike(o, 0, [(1180, 0.5), (2870, 0.3)], 0.05, 0.6, 3000, 0.004, ref noise);
        // The caps: a scatter of little jingles over the first moments.
        for (int c = 0; c < 6; c++)
            Strike(o, 0.004 + 0.007 * c + 0.003 * (noise.Next() + 1), [(5200 + 500 * c, 0.3)], 0.02, boot ? 0.4 : 0.55, 6500, 0.008, ref noise);
        return o;
    }

    /// <summary>A hit at <paramref name="at"/> s: some ringing modes, and a burst of noise round <paramref name="snap"/> Hz.</summary>
    static void Strike(float[] o, double at, (double Hz, double Amp)[] modes, double ring, double gain, double snap, double snapSeconds, ref Noise noise)
    {
        var bp = new Biquad();
        bp.Set(FilterType.BandPass, snap, 1.5);
        for (int i = (int)(at * Rate); i < o.Length; i++)
        {
            double t = (double)i / Rate - at;
            double sum = 0;
            foreach (var (hz, amp) in modes)
                sum += amp * Math.Sin(2 * Math.PI * hz * t);
            o[i] += (float)(gain * (sum * Math.Exp(-t / ring) + 2.5 * bp.Process(noise.Next()) * Math.Exp(-t / snapSeconds)));
        }
    }

    static void Filter(float[] o, params (FilterType Type, double Hz, double Q, double Db)[] chain)
    {
        foreach (var (type, hz, q, db) in chain)
        {
            var f = new Biquad();
            f.Set(type, hz, q, db);
            f.Process(o);
        }
    }
}
