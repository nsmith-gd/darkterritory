using System.Security.Cryptography;
using Ballast;
using Ballast.Audio;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim.Music;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// GDD v1.4 App. E.6 "Manifest" and E.11 "Licensing": the derailment's music is CC0 1.0 recordings of public-domain works,
/// and nothing else gets in. A music file without a CC0-1.0 manifest entry whose SHA-256 matches it fails the build.
/// </summary>
public class MusicManifestTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly string Folder = Path.Combine(Content, "audio", "music");
    static readonly MusicManifest Manifest = MusicManifest.Load(Content);
    static readonly WreckTuning Wreck = DataFile.Load<WreckTuning>(Path.Combine(Content, WreckTuning.File));
    // What can sit in the folder besides recordings: the manifest and the credits it writes.
    // (The evidence records sit in a folder of their own, evidence/.)
    static readonly string[] NotMusic = ["manifest.json", "CREDITS.md"];

    [Fact]
    public void EveryFileInTheMusicFolderHasACc0EntryWithItsHash()
    {
        var files = Directory.EnumerateFiles(Folder).Select(Path.GetFileName).Where(f => !NotMusic.Contains(f)).Order().ToList();
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var entry = Manifest.Tracks.SingleOrDefault(t => t.File == file);
            Assert.True(entry is not null, $"{file} has no entry in {MusicManifest.File}: E.6 accepts only a CC0 1.0 recording of a public-domain work");
            Assert.Equal(MusicManifest.Licence, entry.Licence);
            string sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(Folder, file!)))).ToLowerInvariant();
            Assert.True(sha == entry.Evidence.Sha256, $"{file}'s SHA-256 is {sha}, the manifest says {entry.Evidence.Sha256}: regenerate with `dt audio opera`");
            Assert.False(string.IsNullOrWhiteSpace(entry.Evidence.Note), $"{file}: no evidence for its licence");
        }
        // And no entry for a file that isn't there.
        Assert.All(Manifest.Tracks, t => Assert.True(File.Exists(Path.Combine(Folder, t.File)), $"{t.Id}: {t.File} is missing"));
        Assert.Equal(Manifest.Tracks.Length, Manifest.Tracks.Select(t => t.Id).Distinct().Count());
    }

    [Fact]
    public void EveryEntryCarriesE6sFields()
    {
        Assert.All(Manifest.Tracks, t =>
        {
            Assert.All(new[] { t.Id, t.File, t.Work, t.Composer, t.Performers, t.Source }, f => Assert.False(string.IsNullOrWhiteSpace(f), $"{t.Id}: a field is empty"));
            // A public-domain composition: long out of copyright everywhere (E.6 rules out Orff's 1937 "O Fortuna").
            Assert.InRange(t.Year, 1700, 1926);
            // A recording names the Commons file page it came from (note 194); our own, the script in the repo that made it.
            if (!t.Recorded)
                Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(Content)!, t.Source.Split(' ')[0])), $"{t.Id}: its source {t.Source} isn't in the repo");
        });
    }

    [Fact]
    public void EveryRecordingCarriesItsLicenceEvidence()
    {
        // E.6 "Manifest": a recording taken in from elsewhere has its source URL, the SHA-256 of the file as downloaded, and
        // an archived copy of the licence: the record fetch_music.py writes beside the music, whose wikitext carries the
        // page's own CC0 dedication (a "public domain" mark on an old recording isn't enough).
        foreach (var t in Manifest.Tracks.Where(t => t.Recorded))
        {
            Assert.StartsWith("https://commons.wikimedia.org/", t.Source);
            Assert.Matches("^[0-9a-f]{64}$", t.Evidence.SourceSha256 ?? "");
            Assert.Equal(t.Source, t.Evidence.Page);
            string record = Path.Combine(Folder, t.Evidence.Record ?? "-");
            Assert.True(File.Exists(record), $"{t.Id}: no evidence record at {t.Evidence.Record}");
            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(record));
            var root = json.RootElement;
            Assert.Equal(t.Evidence.SourceSha256, root.GetProperty("sourceSha256").GetString());
            Assert.True(root.GetProperty("cc0").GetBoolean(), $"{t.Id}: its record doesn't say CC0");
            Assert.NotEqual(0, root.GetProperty("licenceTemplates").GetArrayLength());
            Assert.Matches("(?i)cc-zero|cc0", root.GetProperty("licenceSection").GetString() ?? "");
        }
    }

    [Fact]
    public void EveryHitLiesInsideItsTrackWithTheLeadTheSequenceNeeds()
    {
        // E.6 "Alignment": the hit is inside [inPoint, outPoint], at least the replay's lead past the in-point (what the
        // build lines up before the moment it came off, note 174), so started at StartFor it lands on time, not early.
        foreach (var t in Manifest.Tracks)
        {
            Assert.InRange(t.InPoint, 0, t.Hit);
            Assert.InRange(t.Hit, t.InPoint, t.OutPoint);
            Assert.True(t.Hit - t.InPoint >= Wreck.ReplayLeadSeconds - 1e-9, $"{t.Id}: its hit is {t.Hit - t.InPoint:0.00} s past its in-point, under the {Wreck.ReplayLeadSeconds} s lead");
            Assert.Equal(t.Hit - Wreck.ReplayLeadSeconds, t.StartFor(Wreck.ReplayLeadSeconds), 9);
        }
    }

    [Fact]
    public void TheDemoPoolHasEveryMood()
    {
        // E.6 "Pool size": at least 4 tracks for the December demo; one for each mood, so the speed weighting has something to weigh.
        Assert.True(Manifest.Tracks.Length >= 4);
        Assert.All(Enum.GetValues<MusicMood>(), m => Assert.Contains(Manifest.Tracks, t => t.Mood == m));
    }

    [Fact]
    public void EachTrackIsMeasuredAndNormalisedToMinusSixteenLufs()
    {
        foreach (var t in Manifest.Tracks)
        {
            var clip = AudioClip.Load(Path.Combine(Folder, t.File));
            double measured = Loudness.Integrated(clip.Samples, clip.SampleRate);
            Assert.InRange(measured, t.LoudnessLufs - 0.1, t.LoudnessLufs + 0.1);
            // To -16 LUFS; or, where the full gain would put the peak over -1 dBFS, as far as that allows, and the shortfall
            // recorded (at most 3 dB: note 194).
            Assert.InRange(t.LoudnessLufs + t.GainDb + (t.ShortfallDb ?? 0), -16.05, -15.95);
            Assert.InRange(t.ShortfallDb ?? 0, 0, 3);
            float peak = clip.Samples.Max(Math.Abs);
            if (t.ShortfallDb is > 0)
                Assert.True(20 * Math.Log10(peak) + t.GainDb > -1.05, $"{t.Id} is short of -16 LUFS but its peak isn't what stops it");
            // Its peak after the gain stays out of the mixer's soft clip.
            Assert.True(peak * Audio.DbToGain(t.GainDb) < 1.2, $"{t.Id} peaks over the top at its gain ({20 * Math.Log10(peak) + t.GainDb:0.0} dBFS)");
            Assert.InRange(t.OutPoint, t.Hit, clip.Seconds + 1e-3);
        }
    }

    [Fact]
    public void EachTracksHitIsWhereItSaysAndCoversTheSequence()
    {
        foreach (var t in Manifest.Tracks)
        {
            // The hit (the crash on the big note) is the sharpest onset in the file around where the manifest puts it.
            var clip = AudioClip.Load(Path.Combine(Folder, t.File));
            var stereo = new float[(int)(clip.Seconds * Audio.SampleRate) * 2];
            for (int i = 0; i < stereo.Length / 2; i++)
                stereo[i * 2] = stereo[i * 2 + 1] = clip.At(i * (double)clip.SampleRate / Audio.SampleRate);
            Assert.InRange(AudioBench.SharpestOnset(stereo, t.Hit - 1, t.Hit + 1), t.Hit - 0.1, t.Hit + 0.1);
            // Started at the replay's first frame, the hit comes on the replay's moment of derailment: the hit's that far
            // past the in-point. And the track runs to the sequence's end.
            double start = t.StartFor(Wreck.ReplayLeadSeconds);
            Assert.True(t.Hit - start >= Wreck.ReplayLeadSeconds - 1e-9, $"{t.Id}: its hit is under {Wreck.ReplayLeadSeconds} s past its in-point");
            Assert.True(t.OutPoint - start >= Wreck.SequenceSeconds - Wreck.FirstPersonSeconds, $"{t.Id} runs out before the orbit ends");
        }
    }

    [Fact]
    public void TheCreditsListEveryPerformer()
    {
        // CC0 asks for no credit; E.6's credits screen lists every performer anyway.
        var credits = File.ReadAllText(Path.Combine(Folder, "CREDITS.md"));
        Assert.All(Manifest.Tracks, t =>
        {
            Assert.Contains(t.Work, credits);
            Assert.Contains(t.Performers, credits);
        });
    }

    [Fact]
    public void TheHitLandsOnTheReplaysMomentOfDerailment()
    {
        // Note 167: the replay starts firstPersonSeconds in and shows the train coming off replayLeadSeconds into it. The
        // music starts on the replay's first frame, with the hit there to within E.6's ±0.1 s, and fades to nothing by the end.
        foreach (var t in Manifest.Tracks)
        {
            Assert.Null(Opera.Cue(t, Wreck.FirstPersonSeconds - 0.05, Wreck));
            var first = Opera.Cue(t, Wreck.FirstPersonSeconds, Wreck);
            Assert.NotNull(first);
            var moment = Opera.Cue(t, Wreck.FirstPersonSeconds + Wreck.ReplayLeadSeconds, Wreck)!.Value;
            Assert.InRange(moment.ClipSeconds, t.Hit - 0.1, t.Hit + 0.1);
            Assert.Equal(Audio.DbToGain(t.GainDb), moment.Gain, 4);
            // E.6 "Out": at most 1.5 s of fade, to silence at the sequence's end.
            Assert.True(Wreck.Music.FadeOutSeconds <= 1.5);
            Assert.Equal(Audio.DbToGain(t.GainDb), Opera.Cue(t, Wreck.SequenceSeconds - Wreck.Music.FadeOutSeconds - 0.01, Wreck)!.Value.Gain, 4);
            Assert.True(Opera.Cue(t, Wreck.SequenceSeconds - 0.01, Wreck)!.Value.Gain < 0.01);
            Assert.Null(Opera.Cue(t, Wreck.SequenceSeconds, Wreck));
        }
    }

    [Fact]
    public void WithAFilmTheHitLandsOnTheFinalPlayersApex()
    {
        // E.6 "Alignment" (note 245): the planner puts the hit within ±0.1 s of the final player's apex (E.5's last shot,
        // the biggest flight). A hit further into the track than the apex is starts mid-track; one nearer its in-point waits,
        // and the track comes in late, in time for it.
        var (_, film) = FilmPlaybackTests.Shot();
        double apex = Wreck.FirstPersonSeconds + Wreck.ReplaySeconds + film.FinalApexAt!.Value;
        double end = Wreck.FirstPersonSeconds + Wreck.ReplaySeconds + film.CauseAt;
        foreach (var t in Manifest.Tracks)
        {
            var hit = Opera.Cue(t, apex, Wreck, end, apex);
            Assert.NotNull(hit);
            Assert.InRange(hit!.Value.ClipSeconds, t.Hit - 0.1, t.Hit + 0.1);
            double comesIn = apex - (t.Hit - t.InPoint);
            if (comesIn > Wreck.FirstPersonSeconds + 0.05)
                Assert.Null(Opera.Cue(t, comesIn - 0.05, Wreck, end, apex));
            else
                Assert.NotNull(Opera.Cue(t, Wreck.FirstPersonSeconds, Wreck, end, apex));
        }
        // The last player's shot shows their peak at the apex.
        var last = film.Cut.Last(s => s.Kind == ShotKind.Player);
        double into = film.FinalApexAt.Value - film.Cut.TakeWhile(s => s != last).Sum(s => s.Real);
        Assert.InRange(into, 0, last.Real);
        Assert.Equal(film.Peaks[last.Subject].At, last.At(into), 3);
    }
}
