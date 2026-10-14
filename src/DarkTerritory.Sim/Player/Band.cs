using System.Text.Json.Serialization;

namespace DarkTerritory.Sim.Player;

/// <summary>One of the band's instruments (band.json <c>instruments</c>): its part of every tune, and an octave from the tune.</summary>
public sealed record BandInstrument(string Id, string Name, string Part, int Octave = 0);

/// <summary>
/// A tune in the band's repertoire (band.json <c>repertoire</c>): traditional, in abc notation (L:1/8, its key and mode),
/// a chord a bar once its repeats are played out, and its pace in beats a minute (a jig's dotted crotchet, a reel's crotchet).
/// </summary>
public sealed record BandTune(string Title, string Metre, string Key, double Bpm, string Melody, string Chords);

/// <summary>The band (band.json; ARCHITECTURE §8 note 700). Field docs live in that file.</summary>
public sealed record BandTuning(double HoldSeconds, double Loudness, double GapSeconds, BandInstrument[] Instruments, BandTune[] Repertoire)
{
    public const string File = "tuning/band.json";

    IReadOnlyList<BandScore>? _scores;

    /// <summary>Each tune of the repertoire read into notes, in its order.</summary>
    [JsonIgnore]
    public IReadOnlyList<BandScore> Scores => _scores ??= [.. Repertoire.Select(BandScore.Parse)];

    /// <summary>The instrument with this id, or −1.</summary>
    public int IndexOf(string id) => Array.FindIndex(Instruments, i => i.Id == id);

    /// <summary>How many ticks a tune takes, with the breath before the next: the host moves on to the next tune after it.</summary>
    public uint Ticks(int tune) => (uint)Math.Ceiling((Scores[tune].Seconds + GapSeconds) / SimConstants.TickSeconds);
}

/// <summary>
/// What the band's playing, the host's, sent to every machine (the world record): which tune of the repertoire (−1: none),
/// and the host's tick its first bar began. Every machine plays the same bar at the same host tick from it.
/// </summary>
public readonly record struct BandState(int Tune, uint StartTick)
{
    public static readonly BandState Silent = new(-1, 0);
    public bool Playing => Tune >= 0;
}

/// <summary>
/// The band (ARCHITECTURE §8 note 700; docs/design/upgrades.md §8.4; the director, 13 Oct 2026: "synced up by the first
/// person who starts playing ... the song that is selected from a repertoire ... as people play different instruments or the
/// same instrument, they should sync up to whoever played first. And then that should all stop once the last person stops
/// playing"). The host's clock: the first crewmate to play starts the next tune of the repertoire on this tick; everyone who
/// plays after comes in on it (the presentation brings each in on its next bar); played out, the next tune follows after a
/// breath, as a session's set does; the last to stop stops it, and the next to start begins the tune after.
/// </summary>
public static class Band
{
    /// <summary>The band after this tick: <paramref name="playing"/> is how many of the crew are playing now.</summary>
    public static BandState Step(BandState band, int playing, BandTuning? tuning, ref int next, uint tick)
    {
        int tunes = tuning?.Repertoire.Length ?? 0;
        if (tunes <= 0 || playing <= 0)
            return BandState.Silent;
        if (band.Playing && band.Tune < tunes)
        {
            uint end = band.StartTick + tuning!.Ticks(band.Tune);
            if (tick < end)
                return band;
            // Into the next tune of the set, on the tick this one's breath ran out.
            var after = new BandState(next % tunes, end);
            next = (next + 1) % tunes;
            return after;
        }
        var started = new BandState(next % tunes, tick);
        next = (next + 1) % tunes;
        return started;
    }
}

/// <summary>A note of a part (in eighths from the tune's top): a pitch (MIDI; for the percussion, which stroke), and how hard.</summary>
public readonly record struct BandNote(double At, double Length, int Pitch, double Velocity = 1);

/// <summary>
/// A tune read (note 700): its melody as notes with its repeats played out, its chords a bar, and how long an eighth is.
/// The parts each instrument plays come from it (<see cref="Part"/>). Read alike on every machine, so the band's in time.
/// </summary>
public sealed record BandScore(BandTune Tune, int BarEighths, double EighthSeconds, IReadOnlyList<BandNote> Melody, IReadOnlyList<string> Chords,
    IReadOnlyList<double> BarLengths)
{
    public int Bars => BarLengths.Count;
    public double BarSeconds => BarEighths * EighthSeconds;
    public double Seconds => Bars * BarSeconds;
    /// <summary>A jig's six-eight (two beats of three), not a reel's or a song's four-four.</summary>
    public bool Jig => BarEighths == 6;

    /// <summary>
    /// What <paramref name="instrument"/> plays of it. The melody's instruments the tune (an octave up or down); the rhythm,
    /// a bass on the beat and a strum between ("boom-chuck", or a jig's "boom-chuck-a"); the percussion, a stroke an eighth,
    /// the beats hardest (pitch 0 the beat's stroke, 1 the light one).
    /// </summary>
    public IReadOnlyList<BandNote> Part(BandInstrument instrument) => instrument.Part switch
    {
        "melody" => [.. Melody.Select(n => n with { Pitch = n.Pitch + 12 * instrument.Octave })],
        "rhythm" => Rhythm(instrument.Octave),
        "percussion" => Percussion(instrument.Id),
        _ => [],
    };

    IReadOnlyList<BandNote> Rhythm(int octave)
    {
        var notes = new List<BandNote>();
        for (int bar = 0; bar < Bars; bar++)
        {
            var (root, triad) = Chord(Chords[Math.Min(bar, Chords.Count - 1)]);
            int bass = 48 + 12 * octave + root, fifth = bass + 7 > 48 + 12 * octave + 11 ? bass - 5 : bass + 7;
            double at = bar * BarEighths;
            // The strings of a chord, a little after one another as a pick crosses them.
            void Strum(double when, double velocity)
            {
                int i = 0;
                foreach (int pc in triad)
                    notes.Add(new(at + when + 0.06 * i++, 1, 55 + 12 * (octave + 1) + Mod(pc - 55, 12), velocity));
            }
            if (Jig)
            {
                notes.Add(new(at, 1, bass, 1));
                Strum(1, 0.55);
                Strum(2, 0.45);
                notes.Add(new(at + 3, 1, fifth, 0.9));
                Strum(4, 0.55);
                Strum(5, 0.45);
            }
            else
            {
                notes.Add(new(at, 2, bass, 1));
                Strum(2, 0.7);
                notes.Add(new(at + 4, 2, fifth, 0.9));
                Strum(6, 0.7);
            }
        }
        return notes;
    }

    IReadOnlyList<BandNote> Percussion(string id)
    {
        var notes = new List<BandNote>();
        for (int bar = 0; bar < Bars; bar++)
            for (int e = 0; e < BarEighths; e++)
            {
                double at = bar * BarEighths + e;
                bool beat = Jig ? e % 3 == 0 : e % 2 == 0, downbeat = e == 0 || (!Jig && e == 4);
                // The ugly stick's boot thumps the beats and its caps rattle the off-beats; the spoons clack the back-beat
                // (a reel's two and four, a jig's every beat); the bodhrán's tipper never stops, its down-strokes on the beats.
                switch (id)
                {
                    case "uglyStick":
                        if (downbeat)
                            notes.Add(new(at, 1, 0, 1));
                        else if (beat)
                            notes.Add(new(at, 1, 1, 0.9));
                        else
                            notes.Add(new(at, 1, 1, 0.35));
                        break;
                    case "spoons":
                        notes.Add(new(at, 1, beat ? 0 : 1, Jig ? (beat ? 1 : 0.5) : e is 2 or 6 ? 1 : beat ? 0.7 : 0.45));
                        break;
                    default:
                        notes.Add(new(at, 1, beat ? 0 : 1, downbeat ? 1 : beat ? 0.75 : 0.45));
                        break;
                }
            }
        return notes;
    }

    static int Mod(int a, int m) => ((a % m) + m) % m;

    static readonly int[] Naturals = [9, 11, 0, 2, 4, 5, 7]; // A B C D E F G

    static int Letter(char c) => char.ToUpperInvariant(c) - 'A';

    /// <summary>A chord's root (pitch class) and its triad's pitch classes: "D", "Em", "F#m", "Bb".</summary>
    public static (int Root, int[] Triad) Chord(string name)
    {
        int root = Naturals[Letter(name[0])], i = 1;
        if (i < name.Length && name[i] == '#') { root++; i++; }
        else if (i < name.Length && name[i] == 'b') { root--; i++; }
        root = Mod(root, 12);
        bool minor = i < name.Length && name[i] == 'm';
        return (root, [root, Mod(root + (minor ? 3 : 4), 12), Mod(root + 7, 12)]);
    }

    /// <summary>A key's sharps and flats (abc's K: "D", "Ddor", "Edor", "Am", "Gmix"): each letter's alteration, A to G.</summary>
    public static int[] Signature(string key)
    {
        int tonic = Naturals[Letter(key[0])], i = 1;
        if (i < key.Length && key[i] == '#') { tonic++; i++; }
        else if (i < key.Length && key[i] == 'b') { tonic--; i++; }
        string mode = key[i..].ToLowerInvariant();
        int offset = mode switch
        {
            "" or "maj" or "major" or "ion" => 0,
            "dor" => 2,
            "phr" => 4,
            "lyd" => 5,
            "mix" => 7,
            "m" or "min" or "aeo" => 9,
            "loc" => 11,
            _ => throw new FormatException($"the band: no mode \"{mode}\" in key {key}"),
        };
        // The relative major, round the circle of fifths: how many sharps (or, below nought, flats).
        int major = Mod(tonic - offset, 12);
        int fifths = major switch { 0 => 0, 7 => 1, 2 => 2, 9 => 3, 4 => 4, 11 => 5, 6 => 6, 5 => -1, 10 => -2, 3 => -3, 8 => -4, 1 => -5, _ => 0 };
        var sig = new int[7];
        foreach (char c in "FCGDAEB"[..Math.Max(0, fifths)])
            sig[Letter(c)] = 1;
        foreach (char c in "BEADGCF"[..Math.Max(0, -fifths)])
            sig[Letter(c)] = -1;
        return sig;
    }

    /// <summary>
    /// Reads a tune's abc (the subset the repertoire uses: notes, ^ _ = accidentals for the bar, ' and , octaves, lengths
    /// like 2, 3/2 and /, z rests, bars, |: :| repeats) and its metre's eighths and pace.
    /// </summary>
    public static BandScore Parse(BandTune tune)
    {
        int barEighths = tune.Metre switch { "6/8" => 6, "4/4" or "C" => 8, "2/4" => 4, "3/4" => 6, "9/8" => 9, "12/8" => 12, _ => throw new FormatException($"the band: no metre {tune.Metre}") };
        bool compound = tune.Metre is "6/8" or "9/8" or "12/8";
        // A reel's beat is a crotchet (two eighths), a jig's a dotted crotchet (three).
        double eighth = 60.0 / tune.Bpm / (compound ? 3 : 2);
        var sig = Signature(tune.Key);
        var bars = new List<List<(double Length, int Pitch)>>();
        var bar = new List<(double Length, int Pitch)>();
        var accidentals = new Dictionary<int, int>();
        int repeatFrom = 0;
        void Close()
        {
            if (bar.Count > 0)
                bars.Add(bar);
            bar = [];
            accidentals.Clear();
        }
        string m = tune.Melody;
        for (int i = 0; i < m.Length;)
        {
            char c = m[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == ':' && i + 1 < m.Length && m[i + 1] == '|')
            {
                Close();
                bars.AddRange(bars.GetRange(repeatFrom, bars.Count - repeatFrom));
                repeatFrom = bars.Count;
                i += 2;
                continue;
            }
            if (c == '|')
            {
                Close();
                i++;
                if (i < m.Length && m[i] == ':')
                {
                    repeatFrom = bars.Count;
                    i++;
                }
                continue;
            }
            int? accidental = null;
            while (i < m.Length && m[i] is '^' or '_' or '=')
            {
                accidental = (accidental ?? 0) + m[i] switch { '^' => 1, '_' => -1, _ => 0 };
                i++;
            }
            if (i >= m.Length)
                break;
            c = m[i++];
            int pitch = -1;
            if (c is 'z' or 'x')
                pitch = -1;
            else if (c is >= 'A' and <= 'G' or >= 'a' and <= 'g')
            {
                int letter = Letter(c);
                pitch = 60 + Naturals[letter] + (char.IsLower(c) ? 12 : 0);
                while (i < m.Length && m[i] is '\'' or ',')
                    pitch += m[i++] == '\'' ? 12 : -12;
                int slot = pitch / 12 * 7 + letter;
                if (accidental is { } a)
                    accidentals[slot] = a;
                pitch += accidentals.TryGetValue(slot, out int held) ? held : sig[letter];
            }
            else
                throw new FormatException($"the band: \"{c}\" in {tune.Title}");
            // The length, in eighths: "2", "3/2", "/", "/2".
            double length = 1;
            int start = i;
            while (i < m.Length && char.IsDigit(m[i]))
                i++;
            if (i > start)
                length = int.Parse(m[start..i], System.Globalization.CultureInfo.InvariantCulture);
            if (i < m.Length && m[i] == '/')
            {
                i++;
                int d = i;
                while (i < m.Length && char.IsDigit(m[i]))
                    i++;
                length /= i > d ? int.Parse(m[d..i], System.Globalization.CultureInfo.InvariantCulture) : 2;
            }
            bar.Add((length, pitch));
        }
        Close();
        var melody = new List<BandNote>();
        double at = 0;
        foreach (var b in bars)
            foreach (var (length, pitch) in b)
            {
                if (pitch >= 0)
                    melody.Add(new(at, length, pitch));
                at += length;
            }
        var chords = tune.Chords.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return new BandScore(tune, barEighths, eighth, melody, chords, [.. bars.Select(b => b.Sum(n => n.Length))]);
    }
}
