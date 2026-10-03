using Ballast;

namespace DarkTerritory.Sim.Music;

/// <summary>GDD v1.4 App. E.6's mood tags: what kind of derailment a track suits.</summary>
public enum MusicMood : byte { Lament, Gallop, Doom, Swagger }

/// <summary>
/// E.6 "Manifest" evidence: the file's SHA-256 as it is in the repo, and what makes the licence true. A recording taken in
/// from Wikimedia Commons (`tools/audio/fetch_music.py`, note 194) also has the SHA-256 of the file as downloaded, its file
/// page (where the CC0 dedication is), and the licence metadata and wikitext as fetched, in a record beside the music
/// (<paramref name="Record"/>, relative to content/audio/music).
/// </summary>
public sealed record MusicEvidence(string Sha256, string Note, string? SourceSha256 = null, string? Page = null, string? Record = null);

/// <summary>
/// One track in <c>content/audio/music/manifest.json</c>, with E.6's fields. Times are seconds into the file: the
/// planner starts it at <see cref="StartFor"/>, so <see cref="Hit"/> (the big note or crash) lands on the moment of
/// derailment; nothing past <see cref="OutPoint"/> is played.
/// </summary>
public sealed record MusicTrack(string Id, string File, string Work, string Composer, int Year, string Performers, string Source,
    string Licence, MusicEvidence Evidence, MusicMood Mood, double InPoint, double Hit, double OutPoint, double LoudnessLufs, double GainDb,
    double? ShortfallDb = null)
{
    // ShortfallDb: how far short of -16 LUFS the gain stops, when the full gain would push the file's peak past the
    // headroom (note 194: a piano recording's peaks); null when it reaches the target.

    /// <summary>
    /// Where play starts so the hit comes <paramref name="leadSeconds"/> after it (the replay's lead into the moment the
    /// train came off): never before the in-point. A hit too close to the in-point would land early; the manifest's test
    /// rules that out for every track.
    /// </summary>
    public double StartFor(double leadSeconds) => Math.Max(InPoint, Hit - leadSeconds);

    /// <summary>
    /// Someone else's recording, taken in from the web with its licence evidence (note 194): its source is the file page
    /// it came from. Otherwise it's E.6's fallback, made in the repo (its source is the script that made it).
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Recorded => Source.StartsWith("https://", StringComparison.Ordinal);
}

/// <summary>The music manifest (E.6): every track the derailment can play, each a CC0 1.0 recording of a public-domain work.</summary>
public sealed record MusicManifest(MusicTrack[] Tracks)
{
    public const string File = "audio/music/manifest.json";
    /// <summary>E.6 "one rule for licensing": a track is accepted only under this.</summary>
    public const string Licence = "CC0-1.0";

    public static MusicManifest Load(string content)
    {
        var path = Path.Combine(content, File);
        return System.IO.File.Exists(path) ? DataFile.Load<MusicManifest>(path) : new MusicManifest([]);
    }

    /// <summary>The track a replicated key names (<see cref="Key"/>), or null.</summary>
    public MusicTrack? ByKey(uint key) => key == 0 ? null : Tracks.FirstOrDefault(t => Key(t.Id) == key);

    /// <summary>
    /// A track's id on the wire: FNV-1a of the id, 31 bits, never 0 (0 is "no music"). The host sends this with the wreck,
    /// and every client looks it up in its own manifest, so they all play the host's draw.
    /// </summary>
    public static uint Key(string id)
    {
        uint h = 2166136261;
        foreach (byte b in System.Text.Encoding.UTF8.GetBytes(id))
            h = (h ^ b) * 16777619;
        h &= 0x7FFFFFFF;
        return h == 0 ? 1 : h;
    }
}

/// <summary>
/// E.6's shuffle bag, as saved: the tracks still to be drawn this bag, and the last one played. Kept with the host's
/// campaign save (<see cref="Campaign.CampaignState.Music"/>), and in the app's data for quick nights.
/// </summary>
public sealed record MusicBag
{
    public IReadOnlyList<string> Left { get; init; } = [];
    public string? Last { get; init; }
}

/// <summary>Mirror of wreck.json's <c>music</c> (E.6 "Rotation" and "Out", E.10 "Lament / Gallop speed thresholds").</summary>
public sealed record MusicTuning
{
    public double LamentBelow { get; init; } = 8;
    public double GallopAbove { get; init; } = 16;
    public double Favoured { get; init; } = 3;
    public double Swagger { get; init; } = 2;
    public double FadeOutSeconds { get; init; } = 1.5;
}

/// <summary>
/// The derailment's track draw (GDD v1.4 App. E.6 "Rotation"), host-side. Every derail draws one track from the bag
/// without replacement; an empty bag refills with every track in the manifest; the first draw from a refilled bag can't
/// be the last track played. Within what's allowed, the draw is weighted by the speed it came off at: slow favours
/// Lament, fast favours Gallop and Doom, Swagger fits any. Deterministic from the seed it's given.
/// </summary>
public sealed class MusicRotation(IReadOnlyList<MusicTrack> tracks, MusicTuning tuning, MusicBag? bag = null)
{
    public IReadOnlyList<MusicTrack> Tracks { get; } = tracks;
    public MusicTuning Tuning { get; } = tuning;
    public MusicBag Bag { get; private set; } = bag ?? new MusicBag();

    public static MusicRotation Load(string content, MusicTuning tuning, MusicBag? bag) => new(MusicManifest.Load(content).Tracks, tuning, bag);

    /// <summary>How much a track's mood suits a derail at <paramref name="speed"/> m/s.</summary>
    public double Weight(MusicMood mood, double speed) => mood switch
    {
        MusicMood.Swagger => Tuning.Swagger,
        MusicMood.Lament when speed < Tuning.LamentBelow => Tuning.Favoured,
        MusicMood.Gallop or MusicMood.Doom when speed > Tuning.GallopAbove => Tuning.Favoured,
        _ => 1,
    };

    /// <summary>Draws tonight's track for a derail at <paramref name="speed"/>, and takes it out of the bag. Null with no tracks.</summary>
    public MusicTrack? Draw(double speed, ulong seed)
    {
        // Ids in manifest order (not the save's, nor a dictionary's), so the same bag and seed draw the same on any machine.
        var ids = Tracks.Select(t => t.Id).Distinct().ToList();
        if (ids.Count == 0)
            return null;
        // What the save holds that the manifest still has; a track added since joins at the next refill.
        var left = ids.Where(Bag.Left.Contains).ToList();
        if (left.Count == 0)
            left = [.. ids];
        // No back-to-back, at a refill or anywhere else: the last track played is never the next while there's another.
        var candidates = left.Count > 1 ? left.Where(id => id != Bag.Last).ToList() : left;
        var rng = new Pcg32(seed, 0xE6E6E6E6UL);
        var weights = candidates.Select(id => Weight(Tracks.First(t => t.Id == id).Mood, speed)).ToList();
        double pick = rng.NextDouble() * weights.Sum();
        int chosen = candidates.Count - 1;
        for (int i = 0; i < candidates.Count; i++)
        {
            pick -= weights[i];
            if (pick < 0)
            {
                chosen = i;
                break;
            }
        }
        string id = candidates[chosen];
        left.Remove(id);
        Bag = new MusicBag { Left = left, Last = id };
        return Tracks.First(t => t.Id == id);
    }
}
