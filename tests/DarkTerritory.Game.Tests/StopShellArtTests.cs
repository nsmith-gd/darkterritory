using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Stops;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// ARCHITECTURE §8 note 387: the stop buildings the sim stands as walls with a door (note 279: a yard's sheds and hero, the
/// Holdouts) are drawn so: where the sim's door is the art is open, and walking in you're in a room, its walls there
/// from inside too.
/// </summary>
public class StopShellArtTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);

    /// <summary>frontier:7 has sheds, a hero with a gantry's cut, a pump house, prison cars and lockups; frontier:1 signal boxes and lamp rooms.</summary>
    public static TheoryData<string> Nights => ["frontier:7", "frontier:1"];

    /// <summary>Every shelled building of a night, drawn alone, its triangles relative to an eye at its middle.</summary>
    static IEnumerable<(RouteFeature Feature, int Index, Double3 Eye, List<(Vector3, Vector3, Vector3)> Triangles, RailLine Line)> Shells(string spec)
    {
        var night = Sim.LineGen.Routes.Generate(Content, spec, 6);
        var line = night.Build();
        var art = new WorldArt(Look);
        foreach (var f in night.Features.Where(f => f.Stop is not null))
            for (int i = 0; i < f.Stop!.Buildings.Count; i++)
            {
                if (!StopWalls.Shelled(f.Stop, i))
                    continue;
                var eye = Run.StopWorld(line, f, f.Stop.Buildings[i].Centre);
                var mesh = new MeshBuilder();
                art.StopBuilding(mesh, line, night, f, i, eye, 18);
                var v = mesh.Vertices.ToArray();
                var tris = new List<(Vector3, Vector3, Vector3)>();
                for (int t = 0; t + 2 < v.Length; t += 3)
                    tris.Add((v[t].Position, v[t + 1].Position, v[t + 2].Position));
                yield return (f, i, eye, tris, line);
            }
    }

    /// <summary>A point in a building's own frame (x along it, y across), <paramref name="up"/> over the rail's height, relative to <paramref name="eye"/>.</summary>
    static Vector3 At(RailLine line, RouteFeature f, StopBuilding b, double x, double y, double up, Double3 eye)
    {
        var p = new Pt(b.S + x * Math.Cos(b.Yaw) - y * Math.Sin(b.Yaw), b.D + x * Math.Sin(b.Yaw) + y * Math.Cos(b.Yaw));
        var w = Run.StopWorld(line, f, p, up) - eye;
        return new Vector3((float)w.X, (float)w.Y, (float)w.Z);
    }

    /// <summary>The triangles a segment passes through (Möller–Trumbore), each with whether it faces back along the segment.</summary>
    static IEnumerable<bool> Hits(List<(Vector3 A, Vector3 B, Vector3 C)> tris, Vector3 from, Vector3 to)
    {
        var d = to - from;
        foreach (var (a, b, c) in tris)
        {
            var e1 = b - a;
            var e2 = c - a;
            var p = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, p);
            if (MathF.Abs(det) < 1e-9f)
                continue;
            var s = from - a;
            float u = Vector3.Dot(s, p) / det;
            if (u < 0 || u > 1)
                continue;
            var q = Vector3.Cross(s, e1);
            float w = Vector3.Dot(d, q) / det;
            if (w < 0 || u + w > 1)
                continue;
            float t = Vector3.Dot(e2, q) / det;
            if (t is < 0 or > 1)
                continue;
            yield return Vector3.Dot(Vector3.Cross(e1, e2), d) < 0;
        }
    }

    [Theory]
    [MemberData(nameof(Nights))]
    public void WhereTheSimsDoorIsTheArtIsOpenAndBehindItIsARoom(string spec)
    {
        int doors = 0;
        var kinds = new HashSet<BuildingKind>();
        foreach (var (f, i, eye, tris, line) in Shells(spec))
        {
            var b = f.Stop!.Buildings[i];
            kinds.Add(b.Kind);
            string what = $"{spec} {b.Kind} at {f.Start + b.S:0}";
            foreach (var door in StopWalls.Doors(f.Stop, i, Look.Walls))
            {
                doors++;
                // The door's middle on its face, and out of it, in the building's frame.
                var (x, y, ox, oy) = door.Side != 0 ? (door.At, door.Side * b.Width / 2, 0.0, (double)door.Side) : (door.End * b.Length / 2, door.At, (double)door.End, 0.0);
                // Walked in, at chest height (over a shelter's step and the prison van's floor): nothing in the way.
                var outside = At(line, f, b, x + ox * 2, y + oy * 2, 1.5, eye);
                var inside = At(line, f, b, x - ox * 1.2, y - oy * 1.2, 1.5, eye);
                Assert.False(Hits(tris, outside, inside).Any(), $"{what}: its door is drawn shut");
                // Beside the door, the wall: and it's there from inside, facing into the room (where the face runs on that far:
                // a shed's length either side of a gantry's cut can be little more than its door).
                if (b.Kind == BuildingKind.Lockup)
                    continue;
                var (lo, hi) = door.Side == 0 ? (-b.Width / 2, b.Width / 2)
                    : StopWalls.Shelled(f.Stop, i) && f.Stop.Holdouts.All(h => h.Building != i)
                        ? StopWalls.Roofed(f.Stop, i).First(r => door.At > r.Lo && door.At < r.Hi) : (-b.Length / 2, b.Length / 2);
                double beside = door.At + door.Width / 2 + 0.5 < hi - 0.4 ? door.At + door.Width / 2 + 0.5
                    : door.At - door.Width / 2 - 0.5 > lo + 0.4 ? door.At - door.Width / 2 - 0.5 : double.NaN;
                if (double.IsNaN(beside))
                    continue;
                var (bx, by) = door.Side != 0 ? (beside, y) : (x, beside);
                var wall = Hits(tris, At(line, f, b, bx - ox * 1.2, by - oy * 1.2, 1.5, eye), At(line, f, b, bx + ox * 2, by + oy * 2, 1.5, eye)).ToList();
                Assert.True(wall.Contains(true), $"{what}: no wall from inside beside its door ({door}, {wall.Count} crossed, {string.Join(",", wall)})");
            }
        }
        Assert.True(doors >= 4, $"{spec}: only {doors} doors");
        BuildingKind[] expected = spec == "frontier:7"
            ? [BuildingKind.Shed, BuildingKind.Hero, BuildingKind.WaterTower, BuildingKind.PrisonCar, BuildingKind.Lockup]
            : [BuildingKind.SignalBox, BuildingKind.LampRoom];
        Assert.Subset(kinds, expected.ToHashSet());
    }

    [Theory]
    [InlineData("frontier:7")]
    [InlineData("deadLines:2")]
    public void AnOpenBarnsHayloftAndAShedsBenchStandAtItsBackWall(string spec)
    {
        // Note 417: where the sim keeps an open barn's or shed's find (StopWalls.ShedKept), against the wall across from its
        // door, the art stands its hayloft (the loft's boards overhead) or its workbench (its top at a hand's height). Not
        // at the door's wall: the same place mirrored across the shed has nothing at the bench's height.
        int benches = 0, lofts = 0;
        foreach (var (f, i, eye, tris, line) in Shells(spec))
        {
            var b = f.Stop!.Buildings[i];
            if (!StopWalls.OpenShed(b))
                continue;
            string what = $"{spec} {b.Kind} at {f.Start + b.S:0}";
            foreach (var c in f.Stop.Containers.Where(c => c.Building == i))
            {
                var (x, y, _, _) = StopWalls.ShedKept(b, c.Index);
                if (c.Kind == ContainerKind.Bench)
                {
                    benches++;
                    Assert.True(Hits(tris, At(line, f, b, x, y, 1.6, eye), At(line, f, b, x, y, 0.6, eye)).Any(), $"{what}: no bench where its find's kept");
                    Assert.False(Hits(tris, At(line, f, b, x, -y, 1.6, eye), At(line, f, b, x, -y, 0.6, eye)).Any(), $"{what}: a bench at the door's wall");
                }
                else if (c.Kind == ContainerKind.Hayloft)
                {
                    lofts++;
                    double under = y + Math.Sign(-y) * (StopWalls.LoftDepth / 2 - StopWalls.BenchDepth);
                    Assert.True(Hits(tris, At(line, f, b, x, under, 1.8, eye), At(line, f, b, x, under, 3.4, eye)).Any(), $"{what}: no loft over its hayloft's ladder");
                }
            }
        }
        Assert.True(benches + lofts > 0, $"{spec}: no open barn or shed with a find");
    }
}
