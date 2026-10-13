using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ballast;
using DarkTerritory.Sim.Music;

namespace DarkTerritory.Game;

/// <summary>
/// The credits (note 390; GDD App. E.6, whose screen lists the opera's performers though CC0 asks for none): everyone whose
/// work ships, read from the content's own provenance, so what's in the build is what's credited. The sourced models
/// (art/textures/index.models.json), the voices and the sound packs (audio/samples/index.json), the textures and the
/// lettering's fonts (art/textures/index.json), and the code (credits/credits.json, which also names the packs and the
/// licences). The credits screen lists them after the opera; THIRD-PARTY-NOTICES.txt carries them, with the licence
/// texts, beside the game.
/// </summary>
public static partial class Credits
{
    public const string Folder = "credits";
    public const string File = "credits/credits.json";
    public const string NoticesFile = "credits/THIRD-PARTY-NOTICES.txt";

    /// <summary>One credit: whose work and what, under which licence, and where it came from and what in the game has it.</summary>
    /// <param name="Owed">The licence asks for the credit (CC BY, MIT and the rest); false for CC0 and the like, credited anyway.</param>
    public sealed record Line(string Title, string Licence, string Detail, bool Owed);

    /// <summary>A heading on the credits screen and in the notices, its line, and its credits: the owed first.</summary>
    public sealed record Section(string Name, string Blurb, IReadOnlyList<Line> Lines);

    sealed record LicenceInfo(string Name, string Url, string? Text = null, bool Free = false);

    sealed record CodeEntry(string Name, string Licence, string Holder, string Url, string? Version = null, string? Note = null,
        string[]? Packages = null);

    sealed record Data(Dictionary<string, string> Sections, Dictionary<string, string> Packs, Dictionary<string, LicenceInfo> Licences,
        List<CodeEntry> Code);

    static readonly JsonDocumentOptions Json = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>The opera's line under a track: its performers, licence and where it came from (a recording's Commons page, or the script that made it).</summary>
    public static string TrackLine(MusicTrack t) =>
        $"{t.Performers}. {t.Licence}. {(t.Recorded ? t.Source.Replace("https://", "") : "Made by " + Path.GetFileName(t.Source))}";

    /// <summary>The opera's line under its heading (E.6): every work and recording is free, and its performers listed anyway.</summary>
    public const string OperaLine = "Works in the public domain; recordings CC0 1.0.";

    /// <summary>
    /// Everything after the opera, in the screen's order: the models, the sounds and voices, the textures, the type and the
    /// code. A content with no credits.json (a test's, a mod's partial tree) has none.
    /// </summary>
    public static IReadOnlyList<Section> Load(string content)
    {
        if (!System.IO.File.Exists(Path.Combine(content, File)))
            return [];
        var data = Read(content);
        string Blurb(string key) => data.Sections.GetValueOrDefault(key, "");
        return
        [
            new("THE MODELS", Blurb("models"), Models(content, data)),
            new("THE SOUNDS", Blurb("sounds"), Sounds(content, data)),
            new("THE TEXTURES", Blurb("textures"), Textures(content, data)),
            new("THE TYPE", Blurb("type"), Type(content, data)),
            new("THE CODE", Blurb("code"), [.. data.Code.Select(c => Code(c, data))]),
        ];
    }

    /// <summary>
    /// Every licence the provenance names that credits.json doesn't describe. Empty, or a source ships under a licence the
    /// credits can't name or say whether it's owed (CreditsTests fails on any).
    /// </summary>
    public static IReadOnlyList<string> Undescribed(string content)
    {
        var data = Read(content);
        var named = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (_, s) in ModelSources(content))
            named.Add(Str(s, "license") ?? "");
        foreach (var (_, s) in TextureSources(content))
            named.Add(Str(s, "license") ?? "");
        foreach (var (_, c) in Samples(content))
            named.Add(SampleLicence(Str(c, "licence") ?? "").Licence);
        foreach (var c in data.Code)
            named.Add(c.Licence);
        return [.. named.Where(n => !data.Licences.ContainsKey(n))];
    }

    /// <summary>The NuGet ids credits.json's code section covers: every package the game ships with must be one.</summary>
    public static IReadOnlySet<string> Packages(string content) =>
        Read(content).Code.SelectMany(c => c.Packages ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// THIRD-PARTY-NOTICES.txt: the opera and every section's credits with each licence's name and where to read it, then
    /// the texts the code's licences ask to be carried (MIT's under its holders' copyright lines). Deterministic: the test
    /// compares it with the file in content/credits, and <c>dt credits --write</c> rewrites it.
    /// </summary>
    public static string Notices(string content)
    {
        var data = Read(content);
        var b = new StringBuilder();
        b.Append("DARK TERRITORY: THIRD-PARTY NOTICES\n\n");
        b.Append("Everyone whose work is in the game, and the licences it's under. The game shows the same on its CREDITS screen.\n");
        b.Append("Generated by `dt credits --write` from the content's own provenance (note 390); don't edit it by hand.\n");
        var opera = MusicManifest.Load(content).Tracks;
        Heading(b, "THE OPERA", OperaLine);
        foreach (var t in opera)
            Entry(b, $"{t.Work} - {t.Composer}, {t.Year}", TrackLine(t));
        foreach (var s in Load(content))
        {
            Heading(b, s.Name, s.Blurb);
            foreach (var l in s.Lines)
                Entry(b, l.Title, l.Detail);
        }
        Heading(b, "THE LICENCES", "Where to read each licence named above, and the texts the code's licences ask to be carried.");
        var used = Load(content).SelectMany(s => s.Lines).Select(l => l.Licence).Concat(opera.Select(t => t.Licence)).ToHashSet(StringComparer.Ordinal);
        foreach (var (key, info) in data.Licences.Where(l => used.Contains(l.Value.Name) || used.Contains(l.Key)).DistinctBy(l => l.Value.Name)
                     .OrderBy(l => l.Value.Name, StringComparer.Ordinal))
            b.Append($"- {info.Name}: {info.Url}\n");
        foreach (var (key, info) in data.Licences.Where(l => l.Value.Text is not null).OrderBy(l => l.Key, StringComparer.Ordinal))
        {
            var holders = data.Code.Where(c => c.Licence == key).ToList();
            if (holders.Count == 0)
                continue;
            b.Append($"\n=== {info.Name.ToUpperInvariant()} ===\nFor {string.Join("; ", holders.Select(c => $"{c.Name} ({c.Holder.TrimEnd('.')})"))}.\n\n");
            // MIT asks that the copyright notice go with the permission notice; Opus's BSD text carries its own, and Apache's
            // and zlib's ask for the licence alone.
            if (key == "MIT")
            {
                foreach (var holder in holders.Select(c => c.Holder).Distinct())
                    b.Append($"Copyright (c) {holder}\n");
                b.Append('\n');
            }
            b.Append(System.IO.File.ReadAllText(Path.Combine(content, Folder, info.Text!)).Replace("\r\n", "\n").Trim('\n')).Append('\n');
        }
        return b.ToString();
    }

    static void Heading(StringBuilder b, string name, string blurb) => b.Append($"\n{name}\n{blurb}\n\n");

    static void Entry(StringBuilder b, string title, string detail) => b.Append($"- {title}\n  {detail}\n");

    static Data Read(string content)
    {
        string path = Path.Combine(content, File);
        return System.IO.File.Exists(path) ? DataFile.Load<Data>(path) : new([], [], [], []);
    }

    static (string Name, bool Owed) Licence(Data data, string key) =>
        data.Licences.TryGetValue(key, out var l) ? (l.Name, !l.Free) : (key, true);

    // The sourced models: a source with an id is a model taken in (tools/models/sources.json); the rest of a model's
    // sources are the texture library's, credited under the textures, or the game's own.
    static IEnumerable<(string Entry, JsonElement Source)> ModelSources(string content) =>
        Sources(Path.Combine(content, "art", "textures", "index.models.json")).Where(s => s.Source.TryGetProperty("id", out _));

    static IEnumerable<(string Entry, JsonElement Source)> TextureSources(string content) =>
        Sources(Path.Combine(content, "art", "textures", "index.json"));

    static IEnumerable<(string Entry, JsonElement Source)> Sources(string index)
    {
        if (!System.IO.File.Exists(index))
            yield break;
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(index), Json);
        foreach (var e in doc.RootElement.EnumerateArray())
            if (e.TryGetProperty("sources", out var sources) && sources.ValueKind == JsonValueKind.Array)
                foreach (var s in sources.EnumerateArray())
                    yield return (Str(e, "name") ?? "", s.Clone());
    }

    /// <summary>Each sample cue's chosen candidate (the one it picked by key, or its first): what plays in the game.</summary>
    static IEnumerable<(string Cue, JsonElement Candidate)> Samples(string content)
    {
        string index = Path.Combine(content, "audio", "samples", "index.json");
        if (!System.IO.File.Exists(index))
            yield break;
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(index), Json);
        foreach (var cue in doc.RootElement.EnumerateObject().OrderBy(c => c.Name, StringComparer.Ordinal))
        {
            if (!cue.Value.TryGetProperty("candidates", out var cs) || cs.GetArrayLength() == 0)
                continue;
            string? picked = Str(cue.Value, "picked");
            var chosen = cs.EnumerateArray().FirstOrDefault(c => Str(c, "key") == picked);
            yield return (cue.Name, (chosen.ValueKind == JsonValueKind.Object ? chosen : cs[0]).Clone());
        }
    }

    static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static string Short(string url) => url.Replace("https://", "").Replace("http://", "").TrimEnd('/');

    static string Things(IEnumerable<string> names) => string.Join(", ", names.Select(Thing).Distinct().Order(StringComparer.Ordinal));

    /// <summary>A texture or cue name as words: "boy_room_0" is the boy room, "voice-clerk" the voice clerk.</summary>
    static string Thing(string name) => TrailingNumber().Replace(name, "").Replace('_', ' ').Replace('-', ' ');

    [GeneratedRegex(@"_\d+$")]
    private static partial Regex TrailingNumber();

    static IReadOnlyList<Line> Ordered(IEnumerable<Line> lines) =>
        [.. lines.OrderBy(l => l.Owed ? 0 : 1).ThenBy(l => l.Title, StringComparer.Ordinal)];

    // CC BY's attribution as each source gives it (title, author, where, licence), kept whole: it's what was asked for.
    static IReadOnlyList<Line> Models(string content, Data data) => Ordered(ModelSources(content)
        .GroupBy(s => Str(s.Source, "id"))
        .Select(g =>
        {
            var s = g.First().Source;
            var (name, owed) = Licence(data, Str(s, "license") ?? "");
            return new Line(Str(s, "attribution") ?? g.Key!, name, $"{name}. From {Short(Str(s, "repo") ?? "")}. In: {Things(g.Select(x => x.Entry))}.", owed);
        }));

    /// <summary>A sample's licence as the index writes it ("CC BY 4.0: LibriTTS, Zen et al. 2019"): the licence, and whose.</summary>
    static (string Licence, string? Work) SampleLicence(string text) =>
        text.IndexOf(": ", StringComparison.Ordinal) is var i and > 0 ? (text[..i], text[(i + 2)..]) : (text, null);

    // The voices (a licence that names its work, owed) and the sound packs, each with the cues that use it.
    static IReadOnlyList<Line> Sounds(string content, Data data)
    {
        var lines = new List<Line>();
        var samples = Samples(content).ToList();
        foreach (var g in samples.GroupBy(s => Str(s.Candidate, "licence") ?? ""))
        {
            var (licence, work) = SampleLicence(g.Key);
            var (name, owed) = Licence(data, licence);
            if (work is null)
                continue;
            var through = g.SelectMany(s => s.Candidate.TryGetProperty("sources", out var src) ? src.EnumerateArray().Select(x => x.GetString() ?? "") : [])
                .Where(x => x.Length > 0).Distinct().Order(StringComparer.Ordinal).ToList();
            lines.Add(new Line(work, name, $"{name}.{(through.Count > 0 ? $" Spoken through {string.Join(", ", through)}." : "")} In: {Things(g.Select(s => s.Cue.Split('/')[0]))}.", owed));
        }
        // A pack is the part of a source before its ':' (kenney_impact-sounds:impactMetal_light_000).
        var packs = samples
            .Where(s => SampleLicence(Str(s.Candidate, "licence") ?? "").Work is null)
            .SelectMany(s => (s.Candidate.TryGetProperty("sources", out var src) ? src.EnumerateArray().Select(x => x.GetString() ?? "") : [])
                .Where(x => x.Contains(':')).Select(x => (Pack: x[..x.IndexOf(':')], s.Cue, Licence: Str(s.Candidate, "licence") ?? "")))
            .Distinct()
            .GroupBy(x => x.Pack);
        foreach (var g in packs)
        {
            if (data.Packs.GetValueOrDefault(g.Key) is "")
                continue;
            var (name, owed) = Licence(data, g.First().Licence);
            int cues = g.Select(x => x.Cue).Distinct().Count();
            lines.Add(new Line(data.Packs.GetValueOrDefault(g.Key, g.Key), name, $"{name}. In {cues} of the game's sounds.", owed));
        }
        return Ordered(lines);
    }

    static IReadOnlyList<Line> Textures(string content, Data data) => Ordered(TextureSources(content)
        .Where(s => Str(s.Source, "author") is { } a && Str(s.Source, "generator") is null && a != "Dark Territory")
        .GroupBy(s => (Author: Str(s.Source, "author")!, Repo: Str(s.Source, "repo") ?? "", Licence: Str(s.Source, "license") ?? ""))
        .Select(g =>
        {
            var (name, owed) = Licence(data, g.Key.Licence);
            int textures = g.Select(s => s.Entry).Distinct().Count();
            return new Line(g.Key.Author, name, $"{name}.{(g.Key.Repo.Length > 0 ? $" By way of {Short(g.Key.Repo)}." : "")} In {textures} of the game's textures.", owed);
        }));

    static IReadOnlyList<Line> Type(string content, Data data) => Ordered(TextureSources(content)
        .Where(s => Str(s.Source, "font") is not null)
        .GroupBy(s => (Font: Path.GetFileNameWithoutExtension(Str(s.Source, "font")!), Licence: Str(s.Source, "license") ?? ""))
        .Select(g =>
        {
            var (name, owed) = Licence(data, g.Key.Licence);
            return new Line(g.Key.Font, name, $"{name}. Lettering on: {Things(g.Select(s => s.Entry))}.", owed);
        }));

    static Line Code(CodeEntry c, Data data)
    {
        var (name, owed) = Licence(data, c.Licence);
        return new Line(c.Version is null ? c.Name : $"{c.Name} {c.Version}", name,
            $"{name}. {c.Holder.TrimEnd('.')}. {Short(c.Url)}.{(c.Note is null ? "" : " " + c.Note)}", owed);
    }
}
