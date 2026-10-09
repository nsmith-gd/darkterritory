using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game.Art;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// Dave modelled (ARCHITECTURE §8 note 491): his own figure, his five waistcoats its variants and his painting among its
/// clips; his brush's tip on the canvas where the easel stands it, and in the paint on his palette, as crew_clips' paint
/// has it; and his five hats on his head without his head through any of them.
/// </summary>
public class DaveArtTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);

    [Fact]
    public void HisFigureWearsFiveWaistcoatsAndPaints()
    {
        var model = Look.Art.Creatures.Get(DaveArt.Figure) ?? throw new FileNotFoundException("content/art/models/dave.glb: tools/models/build.sh dave");
        Assert.Equal(DaveArt.Vests, model.VariantCount);
        Assert.Contains(DaveArt.Painting, model.ClipNames);
        // Each waistcoat its own part, worn alone in its variant.
        for (int v = 0; v < DaveArt.Vests; v++)
            Assert.Equal([$"vest_{v}"], model.Parts.Where(p => p.DrawnFor(v, null) && p.Name.Contains("vest_")).Select(p => p.Name[p.Name.IndexOf("vest_")..]).Distinct());
        for (int h = 0; h < DaveArt.Hats; h++)
            Assert.NotNull(PropArt.Of(Look).Get("dave_hat_" + h));
    }

    /// <summary>His hands at <paramref name="frame"/> of paint (30 fps), drawn at the origin.</summary>
    static (Vector3 Brush, Vector3 Palette) At(int frame)
    {
        var mesh = new MeshBuilder();
        Assert.True(DaveArt.Draw(Look, mesh, Matrix4x4.Identity, DaveArt.Painting, frame / 30.0, 0, 0, painting: true));
        return DaveArt.Hands(Look.Art.Creatures, Matrix4x4.Identity);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(14)]
    [InlineData(22)]
    [InlineData(31)]
    [InlineData(45)]
    public void HisBrushIsOnTheCanvasAtEachDab(int frame)
    {
        var (brush, _) = At(frame);
        Assert.InRange(brush.Z, -DaveArt.CanvasOut - 0.02f, -DaveArt.CanvasOut + 0.02f);
        Assert.InRange(brush.X, -DaveArt.CanvasHalfWidth, DaveArt.CanvasHalfWidth);
        Assert.InRange(brush.Y, DaveArt.CanvasFoot, DaveArt.CanvasTop);
    }

    [Fact]
    public void BetweenTheDabsAndOnTheWayToThePaletteItsOffTheCanvas()
    {
        Assert.True(At(10).Brush.Z > -DaveArt.CanvasOut + 0.02f, "lifted between the dabs");
        Assert.True(At(100).Brush.Z > -DaveArt.CanvasOut + 0.2f, "stood back, looking");
        // In the paint on the palette, working it round (crew_clips: two small circles, 3 cm across, about its middle).
        foreach (int f in (int[])[63, 68, 73, 78])
        {
            var (brush, palette) = At(f);
            Assert.True(Vector3.Distance(brush, palette) < 0.05f, $"frame {f}: the brush {Vector3.Distance(brush, palette):0.000} m from the palette's paint");
        }
    }

    /// <summary>The scan's head on the figure (its vertices that ride the head bone), in the bind pose.</summary>
    static List<Vector3> Head()
    {
        var model = Look.Art.Creatures.Get(DaveArt.Figure)!;
        int head = model.Skeleton.IndexOf("head");
        var out_ = new List<Vector3>();
        foreach (var p in model.Parts)
            for (int i = 0; i < p.Positions.Length; i++)
            {
                int best = 0;
                for (int k = 1; k < 4; k++)
                    if (p.Weights[i * 4 + k] > p.Weights[i * 4 + best])
                        best = k;
                if (p.Joints[i * 4 + best] == head && p.Weights[i * 4 + best] > 0.5f)
                    out_.Add(p.Positions[i]);
            }
        return out_;
    }

    static float? Hit(Vector3 o, Vector3 d, ReadOnlySpan<Vertex> tris)
    {
        float? best = null;
        for (int i = 0; i + 2 < tris.Length; i += 3)
        {
            Vector3 a = tris[i].Position, b = tris[i + 1].Position, c = tris[i + 2].Position;
            var e1 = b - a;
            var e2 = c - a;
            var p = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, p);
            if (MathF.Abs(det) < 1e-9f)
                continue;
            var t0 = o - a;
            float u = Vector3.Dot(t0, p) / det;
            if (u is < 0 or > 1)
                continue;
            var q = Vector3.Cross(t0, e1);
            float v = Vector3.Dot(d, q) / det;
            if (v < 0 || u + v > 1)
                continue;
            float t = Vector3.Dot(e2, q) / det;
            if (t > 0 && (best is null || t < best))
                best = t;
        }
        return best;
    }

    [Fact]
    public void EveryHatGoesRoundHisHeadNotThroughIt()
    {
        var head = Head();
        Assert.True(head.Count > 200, $"{head.Count} vertices on the head bone");
        float crown = head.Max(v => v.Y);
        foreach (int h in Enumerable.Range(0, DaveArt.Hats))
        {
            var hat = PropArt.Of(Look).Get("dave_hat_" + h)!;
            var tris = hat.Vertices;
            // Over his crown: nothing of the head above the hat, and the hat sits down on it (not floating off it).
            float top = tris.Max(v => v.Position.Y), low = tris.Min(v => v.Position.Y);
            Assert.True(top > crown + 0.005f, $"hat {h}: its top {top:0.000} under the crown {crown:0.000}");
            Assert.True(low < crown - 0.06f, $"hat {h}: its lowest {low:0.000} doesn't come down round the head");
            // Out from the head's middle through every point of it the hat goes round (above its lowest edge, less a
            // little), the hat's surface is further out, if only just (the hats stand 9 mm off the scan, less where
            // the game's head is proud of it and where a hat's flat between its rings): nothing of the head's through.
            var middle = new Vector3(0, 1.69f, -0.016f);
            int through = 0;
            foreach (var v in head.Where(v => v.Y > low + 0.02f))
            {
                var d = Vector3.Normalize(v - middle);
                if (Hit(middle, d, tris) is { } t && t < Vector3.Distance(v, middle) + 0.0005f)
                    through++;
            }
            Assert.True(through == 0, $"hat {h}: {through} of the head's vertices are through it");
        }
    }
}
