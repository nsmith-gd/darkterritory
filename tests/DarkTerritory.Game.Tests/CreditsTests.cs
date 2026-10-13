using System.Text.Json;
using System.Text.RegularExpressions;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The credits (note 390): everyone whose work ships is credited, from the content's own provenance, so nothing CC BY,
/// MIT or the like goes out without its line; and the notices beside the game are current.
/// </summary>
public sealed partial class CreditsTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly string Repo = Path.GetDirectoryName(Content)!;
    static readonly JsonDocumentOptions Json = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    static readonly IReadOnlyList<Credits.Section> Sections = Credits.Load(Content);

    static IEnumerable<Credits.Line> In(string section) => Sections.Single(s => s.Name == section).Lines;

    [Fact]
    public void EveryLicenceTheProvenanceNamesIsDescribed()
    {
        // A source under a licence credits.json doesn't name can't be said to be owed or not: describe it there first.
        Assert.Empty(Credits.Undescribed(Content));
    }

    [Fact]
    public void EverySourcedModelIsCreditedAsItsSourceAsked()
    {
        // index.models.json: a source with an id is a model taken in; its attribution (title, author, where, licence) is
        // the line, whole, and a CC BY one is owed.
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Content, "art", "textures", "index.models.json")), Json);
        var sources = doc.RootElement.EnumerateArray()
            .SelectMany(e => e.TryGetProperty("sources", out var s) ? s.EnumerateArray() : [])
            .Where(s => s.TryGetProperty("id", out _))
            .Select(s => (Attribution: s.GetProperty("attribution").GetString()!, Licence: s.GetProperty("license").GetString()!))
            .Distinct().ToList();
        Assert.NotEmpty(sources);
        var models = In("THE MODELS").ToList();
        foreach (var (attribution, licence) in sources)
        {
            var line = Assert.Single(models, l => l.Title == attribution);
            Assert.Equal(licence.StartsWith("CC-BY", StringComparison.Ordinal), line.Owed);
        }
        Assert.Contains(models, l => l.Owed);
        Assert.Equal(models.OrderBy(l => l.Owed ? 0 : 1).ToList(), models);
    }

    [Fact]
    public void EveryVoiceOwedACreditHasOne()
    {
        // audio/samples/index.json: a cue's licence that names its work ("CC BY 4.0: LibriTTS, Zen et al. 2019") is owed,
        // and named in THE SOUNDS with the cues that speak through it.
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Content, "audio", "samples", "index.json")), Json);
        var works = doc.RootElement.EnumerateObject()
            .SelectMany(c => c.Value.GetProperty("candidates").EnumerateArray())
            .Select(c => c.GetProperty("licence").GetString()!)
            .Where(l => l.Contains(": "))
            .Select(l => l[(l.IndexOf(": ", StringComparison.Ordinal) + 2)..])
            .Distinct().ToList();
        Assert.NotEmpty(works);
        var sounds = In("THE SOUNDS").ToList();
        foreach (var work in works)
            Assert.True(Assert.Single(sounds, l => l.Title == work).Owed);
        // The packs are named, the game's own takes (earlier:) aren't credited as a pack.
        Assert.Contains(sounds, l => l.Title.Contains("Kenney", StringComparison.Ordinal));
        Assert.DoesNotContain(sounds, l => l.Title == "earlier");
    }

    [Fact]
    public void EveryPackageTheGameIsBuiltWithIsCredited()
    {
        // Every PackageReference in src/ ships with the game or the tools; credits.json's code covers each, so a library
        // can't come in without its licence going with it.
        var shipped = Credits.Packages(Content);
        var referenced = Directory.EnumerateFiles(Path.Combine(Repo, "src"), "*.csproj", SearchOption.AllDirectories)
            .SelectMany(p => PackageReference().Matches(File.ReadAllText(p)).Select(m => m.Groups[1].Value))
            .Distinct().Order(StringComparer.Ordinal).ToList();
        Assert.NotEmpty(referenced);
        Assert.All(referenced, p => Assert.True(shipped.Contains(p), $"{p} isn't in {Credits.File}'s code"));
        Assert.All(In("THE CODE"), l => Assert.True(l.Owed, l.Title));
    }

    [GeneratedRegex(@"<PackageReference\s+Include=""([^""]+)""")]
    private static partial Regex PackageReference();

    [Fact]
    public void TheNoticesBesideTheGameAreCurrent()
    {
        // tools/package.sh puts content/credits/THIRD-PARTY-NOTICES.txt at the top of the build. Regenerated from the
        // provenance, it's the same, or a model, pack or library came or went without it: `dt credits --write`.
        string written = File.ReadAllText(Path.Combine(Content, Credits.NoticesFile)).Replace("\r\n", "\n");
        string now = Credits.Notices(Content);
        Assert.True(now == written, $"{Credits.NoticesFile} is stale: run `dt credits --write`. {FirstDifference(written, now)}");
        // Every owed line is in it, and the texts the code's licences ask to be carried.
        foreach (var line in Sections.SelectMany(s => s.Lines))
            Assert.Contains(line.Title, written);
        Assert.Contains("Permission is hereby granted, free of charge", written);
        Assert.Contains("Apache License", written);
        Assert.Contains("https://creativecommons.org/licenses/by/4.0/", written);
    }

    [Fact]
    public void TheCreditsScreenListsEveryoneUnderTheirHeadingsAfterTheOpera()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"dt-credits-{Guid.NewGuid():N}");
        try
        {
            var c = DataFile.Load<CampaignTuning>(Path.Combine(Content, CampaignTuning.File));
            var r = DataFile.Load<RunTuning>(Path.Combine(Content, RunTuning.File));
            var m = new FrontEnd(c, r, new SaveSlots(Path.Combine(dir, "saves"), c.SaveSlots), Path.Combine(dir, "settings.json"), () => 42)
            {
                Music = DarkTerritory.Sim.Music.MusicManifest.Load(Content).Tracks,
                CreditSections = Sections,
            };
            int at = m.Items.ToList().FindIndex(x => x.Label == "CREDITS");
            while (m.Selected != at)
                m.Down();
            m.Select();
            Assert.Equal(Screen.Credits, m.Screen);
            var items = m.Items;
            // A heading a section, greyed (the selection steps over it), then its credits; the opera first; BACK last.
            var headings = items.Where(i => !i.Enabled).Select(i => i.Label).ToList();
            Assert.Equal(["THE OPERA", .. Sections.Where(s => s.Lines.Count > 0).Select(s => s.Name)], headings);
            Assert.Equal(1 + m.Music.Count + Sections.Sum(s => s.Lines.Count + (s.Lines.Count > 0 ? 1 : 0)) + 1, items.Count);
            Assert.Equal(1, m.Selected);
            foreach (var line in Sections.SelectMany(s => s.Lines))
                Assert.Contains(items, i => i.Enabled && i.Label == line.Title && i.Detail == line.Detail);
            // All the way down and back, never resting on a heading; it draws at every size the menus do (note 347).
            for (int i = 0; i < items.Count * 2; i++)
            {
                m.Down();
                Assert.True(items[m.Selected].Enabled, items[m.Selected].Label);
                foreach (var (w, h) in new[] { (480, 270), (384, 216), (320, 180) })
                    m.Draw(new Overlay(), w, h);
            }
            m.Back();
            Assert.Equal(Screen.Title, m.Screen);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>Where two texts first part, line by line: what a stale file says, and what it'd say regenerated.</summary>
    static string FirstDifference(string written, string now)
    {
        var a = written.Split('\n');
        var b = now.Split('\n');
        for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            string x = i < a.Length ? a[i] : "(end)", y = i < b.Length ? b[i] : "(end)";
            if (x != y)
                return $"Line {i + 1}: the file has \"{x}\", regenerated it's \"{y}\" ({a.Length} lines against {b.Length}).";
        }
        return "";
    }
}
