using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ballast;
using Ballast.Audio;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim.Music;
using DarkTerritory.Sim.Train;

/// <summary>
/// `dt audio music --drafts intake/_sources/music/drafts.json [--folder content/audio/music]`: the last step of the music
/// intake (GDD v1.4 App. E.6; ARCHITECTURE §8 note 194). `tools/audio/fetch_music.py` takes in CC0 recordings from Wikimedia
/// Commons with their evidence, cuts a window round each one's climax, normalises and encodes it, and leaves a draft per
/// track; this finishes them the way the game and its tests see them: each hit moved onto the sharpest onset nearby
/// (<see cref="AudioBench.SharpestOnset"/>, what <c>MusicManifestTests</c> checks), the loudness measured (BS.1770, as
/// <see cref="Loudness"/> does) and its gain to −16 LUFS, the file's SHA-256, and the manifest and CREDITS.md written.
/// A track whose hit won't settle, or that's too short for the sequence, is left out, and says why.
/// </summary>
static class MusicCommands
{
    /// <summary>What fetch_music.py leaves for each track.</summary>
    sealed record Draft(string Id, string File, string Work, string Composer, int Year, MusicMood Mood, string Performers, string Source,
        double HitGuess, DraftEvidence Evidence);

    sealed record DraftEvidence(string SourceSha256, string Page, string Record, string Note);

    sealed record Drafts(Draft[] Tracks);

    /// <summary>How far the hit may move from where the intake's analysis put it (fetch_music.py's REFINE).</summary>
    const double MaxMove = 1.0;

    public static int Run(string content, string[] args)
    {
        string draftsPath = Arg(args, "--drafts") ?? Path.Combine("intake", "_sources", "music", "drafts.json");
        string dir = Arg(args, "--folder") ?? Path.Combine(content, "audio", "music");
        var wreck = DataFile.Load<WreckTuning>(Path.Combine(content, WreckTuning.File));
        var drafts = DataFile.Load<Drafts>(draftsPath).Tracks;
        var taken = new List<MusicTrack>();
        var results = new List<object>();
        foreach (var d in drafts)
        {
            string path = Path.Combine(dir, d.File);
            if (!File.Exists(path))
            {
                results.Add(new { d.Id, left = $"{d.File} isn't in {dir}" });
                continue;
            }
            var bytes = File.ReadAllBytes(path);
            var clip = AudioClip.Load(path);
            var (hit, settled) = Refine(clip, d.HitGuess);
            double lufs = Math.Round(Loudness.Integrated(clip.Samples, clip.SampleRate), 2);
            var track = new MusicTrack(d.Id, d.File, d.Work, d.Composer, d.Year, d.Performers, d.Source, MusicManifest.Licence,
                new MusicEvidence(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), d.Evidence.Note, d.Evidence.SourceSha256,
                    d.Evidence.Page, d.Evidence.Record),
                d.Mood, 0, hit, Math.Round(clip.Seconds, 3), lufs, 0);
            // The gain to -16 LUFS, but never so much the peak passes the headroom (run 3: a piano sonata's peaks went over
            // the top); what it falls short by is recorded.
            var (gain, shortfall) = MusicFiles.Gain(lufs, clip.Samples.Max(Math.Abs));
            track = track with { GainDb = gain, ShortfallDb = shortfall };
            string? why = !settled ? $"its hit didn't settle on an onset within {MaxMove} s of {d.HitGuess} s"
                : MusicFiles.Unfit(track, wreck) is { } unfit ? unfit
                : !File.Exists(Path.Combine(dir, d.Evidence.Record)) ? $"no evidence record at {d.Evidence.Record}"
                : null;
            results.Add(new { d.Id, d.File, hit, guess = d.HitGuess, seconds = track.OutPoint, lufs, track.GainDb, track.ShortfallDb, bytes = bytes.Length, left = why });
            if (why is null)
                taken.Add(track);
        }
        // --replace (a full intake): the recordings are what this run took, and earlier ones it didn't retake go.
        var (pool, dropped) = MusicFiles.Write(dir, taken, synthesised: null, replace: args.Contains("--replace"));
        var summary = new
        {
            dir = Path.GetFullPath(dir),
            recorded = pool.Count(t => t.Recorded),
            synthesised = pool.Count(t => !t.Recorded),
            replacedTheFallback = pool.Count(t => t.Recorded) >= MusicFiles.DemoPool,
            removed = dropped,
            totalBytes = pool.Sum(t => new FileInfo(Path.Combine(dir, t.File)).Length),
            tracks = results,
        };
        Console.WriteLine(JsonSerializer.Serialize(summary, DataFile.Options));
        return taken.Count > 0 ? 0 : 1;
    }

    /// <summary>
    /// The hit on the sharpest onset within a second of it, as the manifest test finds it: moved there until it stays put
    /// (each move is to a sharper onset, so it ends), and no further than <see cref="MaxMove"/> from the guess.
    /// </summary>
    static (double Hit, bool Settled) Refine(AudioClip clip, double guess)
    {
        var stereo = MusicFiles.AtMixerRate(clip);
        double h = guess;
        for (int i = 0; i < 32; i++)
        {
            double next = AudioBench.SharpestOnset(stereo, h - 1, h + 1);
            if (double.IsNaN(next) || Math.Abs(next - guess) > MaxMove)
                return (Math.Round(h, 3), false);
            if (Math.Abs(next - h) < 0.005)
            {
                double hit = Math.Round(next, 3);
                // Exactly as the test asks it, at the rounded time.
                double check = AudioBench.SharpestOnset(stereo, hit - 1, hit + 1);
                return (hit, Math.Abs(check - hit) <= 0.05);
            }
            h = next;
        }
        return (Math.Round(h, 3), false);
    }

    static string? Arg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}

/// <summary>
/// The music folder's manifest and credits (E.6 "Manifest"), written by both ways a track gets in: `dt audio music` (CC0
/// recordings taken in from Commons) and `dt audio opera` (E.6's fallback, our own). E.6's demo bar decides between them.
/// </summary>
static class MusicFiles
{
    /// <summary>E.6 "Mix": every track normalised to this through its manifest gain.</summary>
    public const double TargetLufs = -16;
    /// <summary>E.6 "Pool size": at least 4 tracks for the demo. With that many CC0 recordings the synthesised fallback goes.</summary>
    public const int DemoPool = 4;
    static readonly string[] NotMusic = ["manifest.json", "CREDITS.md"];

    /// <summary>The highest a track's peak may sit after its gain: −1 dBFS, clear of the mixer's soft clip.</summary>
    public const double PeakCeilingDb = -1;
    /// <summary>How far under −16 LUFS a peak-limited track may stay before it's left out (it would sound quieter than the rest).</summary>
    public const double MaxShortfallDb = 3;

    /// <summary>
    /// The gain to −16 LUFS for a file measuring <paramref name="lufs"/>, capped so its <paramref name="peak"/> (linear)
    /// stays at or under <see cref="PeakCeilingDb"/>; and how far short of the target the cap leaves it (null: not short).
    /// </summary>
    public static (double Gain, double? Shortfall) Gain(double lufs, float peak)
    {
        double want = TargetLufs - lufs;
        double most = PeakCeilingDb - 20 * Math.Log10(Math.Max(peak, 1e-6));
        if (want <= most)
            return (Math.Round(want, 2), null);
        double gain = Math.Floor(most * 100) / 100;
        return (gain, Math.Round(want - gain, 2));
    }

    /// <summary>A clip at the mixer's rate in stereo, as the game plays it and the manifest test measures it.</summary>
    public static float[] AtMixerRate(AudioClip clip)
    {
        var stereo = new float[(int)(clip.Seconds * Audio.SampleRate) * 2];
        for (int i = 0; i < stereo.Length / 2; i++)
            stereo[i * 2] = stereo[i * 2 + 1] = clip.At(i * (double)clip.SampleRate / Audio.SampleRate);
        return stereo;
    }

    /// <summary>Why a track can't play the derailment as the build cues it (note 174), or null: the hit's lead, and its length.</summary>
    public static string? Unfit(MusicTrack t, WreckTuning wreck)
    {
        double start = t.StartFor(wreck.ReplayLeadSeconds);
        if (t.Hit - t.InPoint < wreck.ReplayLeadSeconds)
            return $"its hit is {t.Hit - t.InPoint:0.00} s in, under the replay's {wreck.ReplayLeadSeconds} s lead";
        if (t.OutPoint - start < wreck.SequenceSeconds - wreck.FirstPersonSeconds)
            return $"it runs out {wreck.SequenceSeconds - wreck.FirstPersonSeconds - (t.OutPoint - start):0.00} s before the sequence's end";
        if (Math.Abs(t.LoudnessLufs + t.GainDb + (t.ShortfallDb ?? 0) - TargetLufs) > 0.05 || double.IsInfinity(t.LoudnessLufs))
            return "its loudness doesn't measure";
        if (t.ShortfallDb > MaxShortfallDb)
            return $"its peaks leave it {t.ShortfallDb:0.0} dB short of {TargetLufs} LUFS (more than {MaxShortfallDb} dB)";
        return null;
    }

    /// <summary>
    /// E.6's pool from the two kinds: with at least <see cref="DemoPool"/> recordings they replace the fallback, but a mood
    /// none of them covers keeps its synthesised track (the speed weighting wants every mood); with fewer, all of both.
    /// </summary>
    public static List<MusicTrack> Pool(IReadOnlyList<MusicTrack> recorded, IReadOnlyList<MusicTrack> synthesised)
    {
        if (recorded.Count < DemoPool)
            return [.. synthesised, .. recorded];
        var covered = recorded.Select(t => t.Mood).ToHashSet();
        return [.. recorded, .. synthesised.Where(t => !covered.Contains(t.Mood))];
    }

    /// <summary>
    /// Writes the manifest and credits for the pool made from <paramref name="recorded"/> (new or retaken recordings; those
    /// already in the manifest and not retaken stay) and <paramref name="synthesised"/> (null: those already in the
    /// manifest), and deletes the music files the pool no longer has. Returns the pool and what was deleted.
    /// </summary>
    public static (List<MusicTrack> Pool, List<string> Removed) Write(string dir, IReadOnlyList<MusicTrack> recorded, IReadOnlyList<MusicTrack>? synthesised,
        bool check = false, bool replace = false)
    {
        var before = Load(dir);
        var retaken = recorded.Select(t => t.Id).ToHashSet();
        var allRecorded = replace ? [.. recorded]
            : recorded.Concat(before.Where(t => t.Recorded && !retaken.Contains(t.Id) && File.Exists(Path.Combine(dir, t.File)))).ToList();
        var pool = Pool(allRecorded, synthesised ?? [.. before.Where(t => !t.Recorded)]);
        var removed = new List<string>();
        if (check)
            return (pool, removed);
        var keep = pool.Select(t => t.File).ToHashSet();
        foreach (var file in Directory.EnumerateFiles(dir).Select(Path.GetFileName).OfType<string>())
            if (!NotMusic.Contains(file) && !keep.Contains(file))
            {
                File.Delete(Path.Combine(dir, file));
                removed.Add(file);
            }
        // An evidence record whose track has gone goes with it.
        var records = pool.Select(t => t.Evidence.Record).OfType<string>().Select(r => Path.GetFullPath(Path.Combine(dir, r))).ToHashSet();
        string evidence = Path.Combine(dir, "evidence");
        if (Directory.Exists(evidence))
            foreach (var file in Directory.EnumerateFiles(evidence))
                if (!records.Contains(Path.GetFullPath(file)))
                {
                    File.Delete(file);
                    removed.Add(Path.GetRelativePath(dir, file));
                }
        File.WriteAllText(Path.Combine(dir, "manifest.json"), ManifestText(pool));
        File.WriteAllText(Path.Combine(dir, "CREDITS.md"), CreditsText(pool));
        return (pool, removed);
    }

    public static MusicTrack[] Load(string dir)
    {
        string path = Path.Combine(dir, "manifest.json");
        return File.Exists(path) ? DataFile.Load<MusicManifest>(path).Tracks : [];
    }

    public static string ManifestText(IReadOnlyList<MusicTrack> tracks) =>
        "// GDD v1.4 App. E.6, the derailment's music. GENERATED by `dt audio music` (CC0 recordings taken in by tools/audio/fetch_music.py)\n"
        + "// and `dt audio opera` (E.6's fallback, our own): don't edit by hand.\n"
        + "// One rule: a CC0 1.0 recording of a public-domain composition. Every file in this folder needs an entry here reading\n"
        + "// CC0-1.0 whose SHA-256 matches it, or the build fails (MusicManifestTests). Times are seconds into the file; the hit\n"
        + "// (the big note or crash) is what the derailment lines up with. gainDb brings the measured loudness (BS.1770, LUFS) to -16 LUFS.\n"
        + JsonSerializer.Serialize(new MusicManifest([.. tracks]), Readable) + "\n";

    // Quotes and accents as they are ("La donna è mobile"), not as \u escapes: it's text people read. A recording's extra
    // evidence fields are left out of a synthesised track's entry rather than written as nulls.
    static readonly JsonSerializerOptions Readable = new(DataFile.Options)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static string CreditsText(IReadOnlyList<MusicTrack> tracks)
    {
        var sb = new StringBuilder();
        sb.Append("# Music\n\n");
        sb.Append("Generated by `dt audio music` and `dt audio opera` from `manifest.json`; don't edit by hand.\n\n");
        sb.Append("The opera that plays when the train comes off the rails (GDD v1.4 App. E.6). Every composition is in the public domain, ");
        sb.Append("and every recording is dedicated to the public domain under [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/): ");
        bool recorded = tracks.Any(t => t.Recorded), ours = tracks.Any(t => !t.Recorded);
        if (recorded)
            sb.Append("a recording by its performers or publisher, on its own Wikimedia Commons file page (linked; the evidence is in `evidence/`)");
        if (recorded && ours)
            sb.Append("; ");
        if (ours)
            sb.Append(recorded ? "ours, synthesised from the score, by Dark Territory" : "every recording is our own, synthesised from the score, dedicated by Dark Territory");
        sb.Append(". CC0 asks for no credit; we list the performers anyway.\n\n");
        foreach (var t in tracks)
            sb.Append(t.Recorded
                ? $"- **{t.Work}**: {t.Composer}, {t.Year}. Performed by {t.Performers}. {t.Licence}, from [Wikimedia Commons]({t.Source}). ({t.Mood}; `{t.File}`)\n"
                : $"- **{t.Work}**: {t.Composer}, {t.Year}. Performed by {t.Performers}. {t.Licence}. ({t.Mood}; `{t.File}`)\n");
        return sb.ToString();
    }
}
