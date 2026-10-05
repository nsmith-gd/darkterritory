using System.Text.RegularExpressions;
using Ballast;
using Ballast.Audio;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// The yard's voice on the radio (GDD §9; ARCHITECTURE §8 notes 178, 233): the dispatcher's manifest and the clerk's tally,
/// said in one man's voice the crew learn. The lines are made from the night's own numbers (Sim.Run.Radio), so there's no
/// script: <c>tools/audio/clerk.py</c> builds the clerk's vocabulary (each phrase and word a take, with its length in
/// <c>bank.json</c>) and a line is strung together from it as it goes on air: numbers said as words, the longest phrase
/// the bank has taken first, a beat at each full stop. A word the bank doesn't have (a name a player typed) is the set
/// breaking up over it; the card on screen still says it.
/// </summary>
public sealed partial class ClerkVoice
{
    /// <summary>Where the bank is under the samples: its takes and <c>bank.json</c>.</summary>
    public const string Folder = "voice-clerk/bank";

    public sealed record Entry(string File, double Seconds, string? Said = null);
    public sealed record BankFile(int Speaker, string Voice, Dictionary<string, Entry> Entries);

    public enum PieceKind { Word, Pause, Breakup }

    /// <summary>One piece of a line said: a take from the bank (its phrase), a silence, or the set breaking up.</summary>
    public readonly record struct Piece(PieceKind Kind, string Text, double Seconds);

    // Between two words, and after each mark: the clerk's even, clipped pace.
    const double WordGap = 0.04, StopPause = 0.3, ColonPause = 0.12, CommaPause = 0.14;
    // The set breaking up over a word it can't say: about this long a syllable, and never shorter than this.
    const double BreakupSyllable = 0.16, BreakupLeast = 0.3;

    readonly SampleLibrary _samples;
    readonly Dictionary<string, Entry> _entries;
    readonly int _longest;

    public ClerkVoice(SampleLibrary samples)
    {
        _samples = samples;
        string? path = samples.Root is null ? null : Path.Combine(samples.Root, Folder, "bank.json");
        _entries = path is not null && File.Exists(path) ? DataFile.Load<BankFile>(path).Entries : [];
        _longest = _entries.Count == 0 ? 1 : _entries.Keys.Max(k => k.Split(' ').Length);
    }

    /// <summary>There's a bank to say anything with (without one the reading is the card alone, over static).</summary>
    public bool Speaks => _entries.Count > 0;

    /// <summary>Whether the bank can say this phrase or word (lower case).</summary>
    public bool Knows(string phrase) => _entries.ContainsKey(phrase);

    // Any letters, so a name in any alphabet is a word the set breaks up over, not nothing. Brackets are a comma's beat.
    [GeneratedRegex(@"-?\d+|\p{L}[\p{L}'\-]*|[.,:;()]")]
    private static partial Regex Tokens();

    // The cause card's units (IncidentLog), said as words: "68 km/h", "at km 12", "unattended 40 s", "12 m from the train".
    [GeneratedRegex(@"\bkm/h\b")]
    private static partial Regex Kmh();
    [GeneratedRegex(@"\bkm (?=-?\d)")]
    private static partial Regex Km();
    [GeneratedRegex(@"(?<=\d) s\b")]
    private static partial Regex Secs();
    [GeneratedRegex(@"(?<=\d) m\b")]
    private static partial Regex Metres();

    /// <summary>A line with its units spelled out as they're said.</summary>
    static string Spelled(string line) =>
        Metres().Replace(Secs().Replace(Km().Replace(Kmh().Replace(line, "kilometres an hour"), "kilometre "), " seconds"), " metres");

    /// <summary>A line as it's said: the bank's phrases and words, the pauses, and the set breaking up over the rest.</summary>
    public List<Piece> Pieces(string line)
    {
        // Words (numbers as theirs), and the marks between them.
        var tokens = new List<string>();
        foreach (Match m in Tokens().Matches(Spelled(line)))
        {
            string t = m.Value;
            if (char.IsDigit(t[^1]))
                tokens.AddRange(Say(long.Parse(t, System.Globalization.CultureInfo.InvariantCulture)).Split(' '));
            else
                tokens.Add(t.ToLowerInvariant());
        }
        var pieces = new List<Piece>();
        var unknown = new List<string>();
        void Flush()
        {
            if (unknown.Count == 0)
                return;
            string said = string.Join(' ', unknown);
            pieces.Add(new Piece(PieceKind.Breakup, said, Math.Max(BreakupLeast, Syllables(said) * BreakupSyllable)));
            unknown.Clear();
        }
        for (int i = 0; i < tokens.Count;)
        {
            string t = tokens[i];
            if (t is "." or "," or ":" or ";" or "(" or ")")
            {
                Flush();
                pieces.Add(new Piece(PieceKind.Pause, t, t switch { "." => StopPause, ":" => ColonPause, ";" => StopPause, _ => CommaPause }));
                i++;
                continue;
            }
            // The longest phrase the bank has, from here, that doesn't run across a mark.
            int took = 0;
            for (int n = Math.Min(_longest, tokens.Count - i); n >= 1 && took == 0; n--)
            {
                var run = tokens.GetRange(i, n);
                if (run.Any(x => x is "." or "," or ":" or ";" or "(" or ")"))
                    continue;
                string phrase = string.Join(' ', run);
                if (_entries.TryGetValue(phrase, out var e))
                {
                    Flush();
                    if (pieces.Count > 0 && pieces[^1].Kind != PieceKind.Pause)
                        pieces.Add(new Piece(PieceKind.Pause, "", WordGap));
                    pieces.Add(new Piece(PieceKind.Word, phrase, e.Seconds));
                    took = n;
                }
            }
            if (took == 0)
            {
                unknown.Add(t);
                took = 1;
            }
            i += took;
        }
        Flush();
        // A line ends on its last word: the pause after it is the reading's (RadioTuning.PauseSeconds).
        while (pieces.Count > 0 && pieces[^1].Kind == PieceKind.Pause)
            pieces.RemoveAt(pieces.Count - 1);
        return pieces;
    }

    /// <summary>How long saying a line takes.</summary>
    public double Seconds(string line) => Speaks ? Pieces(line).Sum(p => p.Seconds) : 0;

    /// <summary>The line said, as one clip at the mixer's rate (null with no bank): the takes end to end, the gaps silent.</summary>
    public AudioClip? Render(string line)
    {
        if (!Speaks)
            return null;
        var pieces = Pieces(line);
        var output = new List<float>((int)(pieces.Sum(p => p.Seconds) * Audio.SampleRate) + Audio.SampleRate / 10);
        var noise = new Noise((uint)line.Length * 2654435761u + 17);
        foreach (var p in pieces)
        {
            int n = (int)Math.Round(p.Seconds * Audio.SampleRate);
            switch (p.Kind)
            {
                case PieceKind.Word:
                    var clip = _samples.Clip($"{Folder}/{_entries[p.Text].File}{SampleLibrary.Extension}");
                    foreach (short s in clip.Pcm)
                        output.Add(s * clip.Scale);
                    break;
                case PieceKind.Pause:
                    output.AddRange(new float[n]);
                    break;
                case PieceKind.Breakup:
                    Breakup(output, n, ref noise);
                    break;
            }
        }
        return new AudioClip([.. output], Audio.SampleRate);
    }

    /// <summary>
    /// The set breaking up over a word: hiss and crackle in bursts at about a syllable's pace, as loud as the voice, so the
    /// shape of a name comes through and the name doesn't.
    /// </summary>
    static void Breakup(List<float> output, int n, ref Noise noise)
    {
        Biquad band = default;
        band.Set(FilterType.BandPass, 1700, 0.9);
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / Audio.SampleRate;
            double syllable = Math.Pow(Math.Sin(Math.PI * Math.Min(1, t / BreakupSyllable % 1 * 1.25)), 2);
            double edge = Math.Min(1, Math.Min(i, n - i) / (0.01 * Audio.SampleRate));
            float crackle = noise.Next() > 0.97f ? 3 * noise.Next() : 0;
            output.Add((float)(edge * (0.35 + 0.65 * syllable) * 0.5 * band.Process(noise.Next() + crackle)));
        }
    }

    /// <summary>A rough count of a word's syllables (its vowel groups): how long the set breaks up over it.</summary>
    static int Syllables(string words) => Math.Max(1, Regex.Matches(words, "[aeiouy]+", RegexOptions.IgnoreCase).Count);

    static readonly string[] Ones = ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven",
        "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen"];
    static readonly string[] Tens = ["", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"];

    /// <summary>A number as the clerk says it, flat: 2450 is "two thousand four hundred fifty", −350 "minus three hundred fifty".</summary>
    public static string Say(long n)
    {
        if (n < 0)
            return "minus " + Say(-n);
        if (n < 20)
            return Ones[n];
        if (n < 100)
            return Tens[n / 10] + (n % 10 == 0 ? "" : " " + Ones[n % 10]);
        if (n < 1000)
            return Ones[n / 100] + " hundred" + (n % 100 == 0 ? "" : " " + Say(n % 100));
        if (n < 1_000_000)
            return Say(n / 1000) + " thousand" + (n % 1000 == 0 ? "" : " " + Say(n % 1000));
        return Say(n / 1_000_000) + " million" + (n % 1_000_000 == 0 ? "" : " " + Say(n % 1_000_000));
    }
}
