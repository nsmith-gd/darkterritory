using System.Numerics;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

/// <summary>
/// The kit pieces the sim deals beside a generated line (Sim.Run.LinesideProps, notes 371 and 389), by the name the sim knows
/// each by: how many variants the art has, how each is built, and what it stands on. The sim can't build a mesh, so what
/// each piece stands on is measured here off the mesh itself and written to content/linegen/footprints.json
/// (<c>dt art footprints --write</c>); LinesideFootprintTests holds the file to the meshes, so a piece that changes shape
/// can't leave its walls behind.
/// </summary>
public static class LinesideFootprints
{
    /// <summary>How a piece's footprint is read off its mesh.</summary>
    public enum Shape : byte
    {
        /// <summary>One box round the whole mesh.</summary>
        Bounds,
        /// <summary>Round: a tank on its legs, as two squares a quarter turn apart (an octagon round the drum).</summary>
        Round,
        /// <summary>Only what's below a crewmate's head: a pole's post, not the crossarm over it.</summary>
        Post,
        /// <summary>Each foot standing on the ground on its own: a headframe's four legs and its back stay, open between.</summary>
        Feet,
        /// <summary>The church: its nave, and the tower and steeple in front of it.</summary>
        NaveAndTower,
        /// <summary>Walked through: a burying ground's thin stones and half-down fence, a wharf's deck out over the water.</summary>
        Passable,
    }

    /// <summary>A kind of piece: the sim's name, the art's cache name for a variant, how many, how it's built, how it stands.</summary>
    public sealed record Kind(string Name, Func<int, string> Cached, int Variants, Func<Look?, int, MeshAsset> Make, Shape Shape, bool Instanced);

    /// <summary>Every piece, in the sim's names (biomes.json's props, and the road's and the shore's own).</summary>
    public static readonly Kind[] Kinds =
    [
        new("saltbox", v => $"saltbox-{v}", 8, NovaKit.Saltbox, Shape.Bounds, true),
        new("barn", v => $"barn-{v}", 2, NovaKit.Barn, Shape.Bounds, true),
        new("church", _ => "nova-church", 1, (l, _) => NovaKit.Church(l), Shape.NaveAndTower, true),
        new("buryingGround", v => $"burying-{v}", 3, NovaKit.BuryingGround, Shape.Passable, true),
        new("fishShed", v => $"fishshed-{v}", 3, NovaKit.FishShed, Shape.Bounds, true),
        new("ruin", v => $"ruin-{v}", 3, SettingKit.RuinWall, Shape.Bounds, true),
        new("chimney", v => $"chimney-{v}", 3, SettingKit.Chimney, Shape.Bounds, true),
        new("tank", v => $"tank-{v}", 3, SettingKit.Tank, Shape.Round, true),
        new("headframe", _ => "headframe", 1, (l, _) => SettingKit.Headframe(l), Shape.Feet, true),
        new("stoneWall", v => $"stonewall-{v}", 3, NovaKit.StoneWall, Shape.Bounds, true),
        new("woodpile", v => $"woodpile-{v}", 3, NovaKit.Woodpile, Shape.Bounds, false),
        new("fencePost", v => $"fencepost-{v}", 1, WorldKit.FencePost, Shape.Post, false),
        new("car", v => $"car-{v}", 4, NovaKit.Car, Shape.Bounds, true),
        new("pole", v => $"pole-{v}", 3, WorldKit.Pole, Shape.Post, true),
        new("wharf", v => $"wharf-{v}", 2, NovaKit.Wharf, Shape.Passable, true),
        new("lighthouse", v => $"lighthouse-{v}", 2, NovaKit.Lighthouse, Shape.Bounds, true),
        new("rock", v => $"rock-{v}", 3, (l, v) => WorldKit.Rock(l, v, 1), Shape.Bounds, false),
    ];

    public static readonly IReadOnlyDictionary<string, Kind> ByName = Kinds.ToDictionary(k => k.Name);

    /// <summary>A post's height: a crewmate's head, about (what's over it, a crossarm, is out of reach).</summary>
    const float PostHigh = 2;
    /// <summary>A foot is what of a frame stands below this, and it stands this tall.</summary>
    const float FootHigh = 2.5f;

    /// <summary>
    /// Every piece's footprint, each variant's boxes as [centre x, centre z, half x, half z, turn, top] in the piece's own
    /// frame (its mesh's: x across, z along, turned about y as the art turns it; the top over its origin).
    /// </summary>
    public static SortedDictionary<string, double[][][]> Measure(Look? look)
    {
        var all = new SortedDictionary<string, double[][][]>(StringComparer.Ordinal);
        foreach (var kind in Kinds)
            all[kind.Name] = [.. Enumerable.Range(0, kind.Variants).Select(v => Boxes(kind.Make(look, v), kind.Shape))];
        return all;
    }

    static double[][] Boxes(MeshAsset mesh, Shape shape)
    {
        var points = mesh.Vertices.Select(v => v.Position).ToArray();
        float top = points.Max(p => p.Y);
        return shape switch
        {
            Shape.Passable => [],
            Shape.Bounds => [Box(points, top)],
            Shape.Post => [Box([.. points.Where(p => p.Y < PostHigh)], top)],
            Shape.Round => Round(points, top),
            Shape.Feet => [.. Feet(points)],
            Shape.NaveAndTower => NaveAndTower(points, top),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
    }

    /// <summary>
    /// The tower and steeple stand in front of the nave's gable (NovaKit.Church: the nave from z −6, its roof's eaves out to
    /// −6.3, to its ridge at 11.55): the nave to its ridge, and the tower run back to meet it, so there's no slot between.
    /// </summary>
    static double[][] NaveAndTower(Vector3[] points, float top)
    {
        var nave = Box([.. points.Where(p => p.Z >= -6.35f)], 11.6f);
        var tower = Box([.. points.Where(p => p.Z < -6.35f)], top);
        double front = tower[1] - tower[3], back = nave[1] - nave[3];
        return [nave, [tower[0], Math.Round((front + back) / 2, 2), tower[2], Math.Round((back - front) / 2, 2), 0, tower[5]]];
    }

    static double[] Box(Vector3[] points, float top)
    {
        float x0 = points.Min(p => p.X), x1 = points.Max(p => p.X), z0 = points.Min(p => p.Z), z1 = points.Max(p => p.Z);
        return [R((x0 + x1) / 2), R((z0 + z1) / 2), R((x1 - x0) / 2), R((z1 - z0) / 2), 0, R(top)];
    }

    /// <summary>A drum (SettingKit.Tank: its radius the z extent; its ladder runs up the +x side): two squares round it, a quarter turn apart.</summary>
    static double[][] Round(Vector3[] points, float top)
    {
        float r = MathF.Max(-points.Min(p => p.Z), points.Max(p => p.Z));
        return [[0, 0, R(r), R(r), 0, R(top)], [0, 0, R(r), R(r), R(MathF.PI / 4), R(top)]];
    }

    /// <summary>What stands on the ground under the frame, each foot its own box: points within a metre of each other are one foot.</summary>
    static IEnumerable<double[]> Feet(Vector3[] points)
    {
        var low = points.Where(p => p.Y < FootHigh).ToList();
        var feet = new List<List<Vector3>>();
        foreach (var p in low)
        {
            var near = feet.Where(f => f.Any(q => MathF.Abs(q.X - p.X) < 1 && MathF.Abs(q.Z - p.Z) < 1)).ToList();
            if (near.Count == 0)
                feet.Add([p]);
            else
            {
                near[0].Add(p);
                foreach (var other in near.Skip(1))
                {
                    near[0].AddRange(other);
                    feet.Remove(other);
                }
            }
        }
        return feet.Select(f => Box([.. f], FootHigh)).OrderBy(b => b[0]).ThenBy(b => b[1]);
    }

    /// <summary>To the centimetre: enough for a wall, and the same file on every machine that writes it.</summary>
    static double R(float v) => Math.Round(v, 2) is var r && r == 0 ? 0 : r;

    /// <summary>content/linegen/footprints.json as <c>dt art footprints --write</c> writes it: a variant to a line.</summary>
    public static string Json(SortedDictionary<string, double[][][]> all)
    {
        static string N(double v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        var sb = new System.Text.StringBuilder();
        sb.Append("// What each kit piece the lineside deals stands on (ARCHITECTURE §8 note 389): measured off the art's meshes by\n");
        sb.Append("// `dt art footprints --write` (src/DarkTerritory.Game/Art/LinesideFootprints.cs), and held to them by\n");
        sb.Append("// LinesideFootprintTests. Don't edit it by hand: change the piece, then write it again.\n");
        sb.Append("// Each variant's boxes, in the piece's own frame (its mesh's: x across, z along the line before it's turned):\n");
        sb.Append("// [centre x, centre z, half x, half z, turn about y (radians), top over its foot]. No boxes: walked through.\n");
        sb.Append("{\n  \"version\": \"1\",\n  \"pieces\": {\n");
        int i = 0;
        foreach (var (name, variants) in all)
        {
            sb.Append($"    \"{name}\": [\n");
            for (int v = 0; v < variants.Length; v++)
                sb.Append("      [").Append(string.Join(", ", variants[v].Select(b => "[" + string.Join(", ", b.Select(N)) + "]")))
                    .Append(v + 1 < variants.Length ? "],\n" : "]\n");
            sb.Append(++i < all.Count ? "    ],\n" : "    ]\n");
        }
        sb.Append("  }\n}\n");
        return sb.ToString();
    }
}
