using System.Text.Json;
using Ballast;
using DarkTerritory.Game.Art;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The sourced props (tools/models: CC0 and CC-BY models cooked and kitbashed in Blender, ARCHITECTURE §8 note 58): each
/// one loads, wears its own layers, has the sockets the scene hangs and lights it by, and says where it came from under
/// a licence the intake rule allows.
/// </summary>
public class PropArtTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);

    [Fact]
    public void EveryPropLoadsWearingItsOwnLayers()
    {
        var props = PropArt.Of(Look);
        var names = props.Names.ToArray();
        Assert.Contains("hand_lantern", names);
        Assert.Contains("skull_lantern", names);
        foreach (var name in names)
        {
            var mesh = props.Get(name);
            Assert.NotNull(mesh);
            Assert.True(mesh!.Triangles > 100, $"{name}: {mesh.Triangles} triangles");
            // Textured with its own maps, not the flat fallback.
            Assert.All(mesh.Vertices, v => Assert.True(v.Layer >= 0, $"{name}: a vertex with no layer"));
            Assert.All(mesh.Vertices, v => Assert.True(float.IsFinite(v.Position.X + v.Position.Y + v.Position.Z)));
        }
    }

    [Fact]
    public void LanternsHangByTheirRingAndBurnInsideTheirGlass()
    {
        var props = PropArt.Of(Look);
        var mesh = props.Get("hand_lantern")!;
        var top = mesh.Vertices.Max(v => v.Position.Y);
        var hang = props.Socket("hand_lantern", "hang")!.Value;
        var lamp = props.Socket("hand_lantern", "lamp")!.Value;
        Assert.InRange(hang.Y, top - 0.03f, top + 0.03f);
        Assert.InRange(lamp.Y, 0.05f, top * 0.7f);
        // The skull lantern's flame is up in its cage, not at its foot.
        Assert.True(props.Socket("skull_lantern", "lamp")!.Value.Y > 2);
    }

    [Fact]
    public void EveryLayerSaysWhereItCameFromUnderAnAllowedLicence()
    {
        var path = Path.Combine(Content, "art", "textures", "index.models.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var layer in doc.RootElement.EnumerateArray())
        {
            var name = layer.GetProperty("name").GetString();
            Assert.True(File.Exists(Path.Combine(Content, "art", "textures", layer.GetProperty("diffuse").GetString()!)), $"{name}: no diffuse");
            var sources = layer.GetProperty("sources").EnumerateArray().ToArray();
            Assert.NotEmpty(sources);
            foreach (var s in sources)
            {
                var licence = s.GetProperty("license").GetString();
                // CC0 or CC-BY, or Three D Scans' own terms (threedscans.com: free to use, no copyright restrictions;
                // intake/README.md).
                Assert.Contains(licence, new[] { "CC0-1.0", "CC-BY-4.0", "CC-BY-3.0", "LicenseRef-ThreeDScans" });
                Assert.False(string.IsNullOrWhiteSpace(s.GetProperty("attribution").GetString()), $"{name}: no attribution");
                Assert.StartsWith("https://github.com/", s.GetProperty("repo").GetString());
                Assert.Matches("^[0-9a-f]{40}$", s.GetProperty("commit").GetString()!);
            }
        }
    }
}
