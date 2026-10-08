using System.Reflection;
using System.Text.Json;
using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Route;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The line generator's numbers live in content/linegen (plan §19.2), never in code. So every property of the config
/// records must be given in the files: one left out would come out zero and silently change the line.
/// </summary>
public class LineGenConfigTests
{
    static readonly string Content = DataFile.FindContentRoot();

    static readonly Dictionary<string, Type> Files = new()
    {
        ["tiers.json"] = typeof(TiersFile),
        ["setpieces.json"] = typeof(SetPiecesFile),
        ["biomes.json"] = typeof(BiomesFile),
        ["facilities.json"] = typeof(FacilitiesFile),
        ["signage.json"] = typeof(SignageFile),
        ["names.json"] = typeof(NamesFile),
        ["fallback_seeds.json"] = typeof(FallbackSeedsFile),
        ["footprints.json"] = typeof(FootprintsFile),
    };

    [Fact]
    public void EveryConfigFieldIsInTheFiles()
    {
        var missing = new List<string>();
        foreach (var (file, type) in Files)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Content, LineGenConfig.Directory, file)),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            Check(doc.RootElement, type, file, missing);
        }
        Assert.True(missing.Count == 0, "not in content/linegen: " + string.Join(", ", missing));
    }

    static void Check(JsonElement element, Type type, string at, List<string> missing)
    {
        if (element.ValueKind != JsonValueKind.Object || !IsRecord(type))
            return;
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite || HasCtorParam(type, p.Name)))
        {
            if (prop.GetIndexParameters().Length > 0)
                continue;
            string key = JsonNamingPolicy.CamelCase.ConvertName(prop.Name);
            if (!element.TryGetProperty(key, out var value))
            {
                missing.Add($"{at}.{key}");
                continue;
            }
            var t = prop.PropertyType;
            if (IsRecord(t))
                Check(value, t, $"{at}.{key}", missing);
            else if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>) && IsRecord(t.GetGenericArguments()[1]))
                foreach (var entry in value.EnumerateObject())
                    Check(entry.Value, t.GetGenericArguments()[1], $"{at}.{key}.{entry.Name}", missing);
            else if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IReadOnlyList<>) && IsRecord(t.GetGenericArguments()[0]))
                foreach (var (item, i) in value.EnumerateArray().Select((v, i) => (v, i)))
                    Check(item, t.GetGenericArguments()[0], $"{at}.{key}[{i}]", missing);
        }
    }

    static bool IsRecord(Type t) => t.IsClass && t != typeof(string) && t.Namespace == typeof(LineGenConfig).Namespace;
    static bool HasCtorParam(Type t, string name) => t.GetConstructors().Any(c => c.GetParameters().Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)));

    [Fact]
    public void TheFilesLoad()
    {
        var c = LineGenContent.Load(Content);
        Assert.Equal("linegen-1.0.0", c.Config.Tiers.Version);
        Assert.Equal(3, c.Config.Tiers.Columns.Local.Facilities);
        Assert.Contains(c.Config.SetPieces.Pieces, p => p.Id == "theDrop");
    }

    [Fact]
    public void TheTierTableLerpsBySeverityAndTheConsistCapsTheGrade()
    {
        var c = LineGenContent.Load(Content);
        // §3.1: Frontier at severity 0.4 is 40% of the way to Dead lines.
        var f = new Limits(c, new RunParameters(RouteTier.Frontier, 1, 0.4, 3, []));
        // Spec B.8 (note 270): one night length whatever the tier or severity.
        Assert.Equal(c.Route.NightLengthKm, f.LengthKm, 6);
        Assert.Equal(300 + 0.4 * (220 - 300), f.MinRadius, 6);
        // §3.4: a 20-car train gets a ruling grade of at most 85% of the 1.8% it can climb.
        var heavy = new Limits(c, new RunParameters(RouteTier.DeepTerritory, 1, 0.5, 20, []));
        Assert.InRange(heavy.ClimbMax, 1.7, 1.9);
        Assert.Equal(0.85 * heavy.ClimbMax, heavy.MainGrade, 6);
        Assert.InRange(heavy.MainGrade, 1.45, 1.6);
        // Deep territory lerps toward the "deep max" column.
        Assert.Equal(3, heavy.Facilities, 6);
        Assert.Equal(c.Route.NightLengthKm, heavy.LengthKm, 6);
    }

    [Fact]
    public void StreamsAreFixedByDefinition()
    {
        // Same inputs, same draws; a different stage, edge or index, a different stream.
        var a = Streams.Rng(42, "script", "main", 3);
        var b = Streams.Rng(42, "script", "main", 3);
        Assert.Equal(a.NextUInt(), b.NextUInt());
        Assert.NotEqual(Streams.Mix(42, "script", "main", 3), Streams.Mix(42, "script", "main", 4));
        Assert.NotEqual(Streams.Mix(42, "script", "main", 3), Streams.Mix(42, "align", "main", 3));
        // Pinned values: a change here would reshuffle every line ever generated.
        Assert.Equal(0xAF63DC4C8601EC8CUL, Streams.Hash("a"));
        Assert.Equal(0xE220A8397B1DCDAFUL, Streams.SplitMix64(0));
    }
}
