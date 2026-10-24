using System.Numerics;
using Ballast;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// Note 1014 (the Look Review's verdict of 23 Oct on the failing cars: "holes and bits of board missing and progressively
/// getting worse"): a failing car's holes are cut through its body, more each stage, the same for the same car every time,
/// and what's left of its walls and roof stays on the car.
/// </summary>
public class CarHolesTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);
    static readonly TrainTuning Tuning = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
    static readonly CarShape Cargo = CarShape.Build(Tuning.Geometry, VehicleKind.Cargo, hasCarBehind: true);
    static readonly CarShape Guard = CarShape.Build(Tuning.Geometry, VehicleKind.Guard, hasCarBehind: false);

    [Fact]
    public void EachStageHasMoreOpenThanTheLastAndHoldsItsHoles()
    {
        foreach (var shape in new[] { Cargo, Guard })
            for (int seed = 0; seed < 16; seed++)
            {
                Assert.False(DamageKit.CarHoles(shape, 0, seed).Any);
                float last = 0;
                DamageKit.Holes? before = null;
                for (int level = 1; level <= DamageKit.MostHoles; level++)
                {
                    var holes = DamageKit.CarHoles(shape, level, seed);
                    float open = DamageKit.OpenArea(shape, holes);
                    Assert.True(open > last + 0.1f, $"seed {seed} level {level}: {open:0.00} m² open vs {last:0.00}");
                    // Getting worse never closes a hole: every bit of the last level's is open still.
                    if (before is not null)
                        foreach (var g in before.Walls)
                            Assert.Contains(holes.Walls, o => o.Side == g.Side && o.Z0 <= g.Z0 + 1e-4f && o.Z1 >= g.Z1 - 1e-4f && o.Y0 <= g.Y0 + 1e-4f && o.Y1 >= g.Y1 - 1e-4f);
                    last = open;
                    before = holes;
                }
                // It reads: a failing car's more than a board or two gone, a car come apart's a good part of its side.
                Assert.InRange(DamageKit.OpenArea(shape, DamageKit.CarHoles(shape, 1, seed)), 0.4f, 6);
                Assert.InRange(DamageKit.OpenArea(shape, DamageKit.CarHoles(shape, DamageKit.MostHoles, seed)), 2.5f, 30);
                Assert.NotEmpty(DamageKit.CarHoles(shape, 2, seed).Roof);
            }
    }

    [Fact]
    public void ACarBeingEatenIsHoledMoreTheFurtherInTheMouthIs()
    {
        var damage = Look.Tuning.Damage;
        Assert.Equal(0, damage.HolesOfEaten(0));
        int last = 0;
        for (double f = 0; f <= 1; f += 0.05)
        {
            int level = damage.HolesOfEaten(f);
            Assert.InRange(level, last, DamageKit.MostHoles);
            last = level;
        }
        Assert.Equal(DamageKit.MostHoles, damage.HolesOfEaten(1));
    }

    [Fact]
    public void TheSameCarHasTheSameHolesAndAnotherCarOthers()
    {
        var a = DamageKit.CarHoles(Cargo, 2, 5);
        var b = DamageKit.CarHoles(Cargo, 2, 5);
        Assert.Equal(a.Walls, b.Walls);
        Assert.Equal(a.Roof, b.Roof);
        Assert.Equal(a.Peel, b.Peel);
        Assert.NotEqual(a.Walls, DamageKit.CarHoles(Cargo, 2, 6).Walls);
        // And the body cut by them is the same mesh every time.
        var one = TrainKit.Car(Look, Cargo, TrainKit.Livery.Planked, 0, holes: a).Vertices.Select(v => v.Position);
        var two = TrainKit.Car(Look, Cargo, TrainKit.Livery.Planked, 0, holes: b).Vertices.Select(v => v.Position);
        Assert.Equal(one, two);
    }

    [Fact]
    public void HolesKeepToTheWallsOffTheDoorsAndTheEndsAndTheRoofOffItsWalk()
    {
        float w = (float)Cargo.HalfWidth, l = (float)Cargo.HalfLength;
        var room = Cargo.Interior!.Value;
        var doors = Cargo.DoorList.Select(d => d.Box).Where(b => b.Max.Z - b.Min.Z > b.Max.X - b.Min.X).ToArray();
        for (int seed = 0; seed < 24; seed++)
        {
            var holes = DamageKit.CarHoles(Cargo, DamageKit.MostHoles, seed);
            foreach (var g in holes.Walls)
            {
                Assert.InRange(g.Z0, -l + 0.79f, l - 0.79f);
                Assert.InRange(g.Z1, -l + 0.79f, l - 0.79f);
                Assert.InRange(g.Y0, room.Min.Y, room.Max.Y);
                Assert.InRange(g.Y1, room.Min.Y, room.Max.Y);
                Assert.DoesNotContain(doors, d => (d.Min.X < 0 ? -1 : 1) == g.Side && g.Z1 > d.Min.Z - 0.1 && g.Z0 < d.Max.Z + 0.1);
            }
            Assert.All(holes.Roof, g => Assert.InRange(g.Inner, 0.45f, w));
        }
    }

    [Fact]
    public void TheHoledBodyIsOpenThereAndWhatsLeftStaysOnTheCar()
    {
        var holes = DamageKit.CarHoles(Cargo, DamageKit.MostHoles, 3);
        var whole = TrainKit.Car(Look, Cargo, TrainKit.Livery.Planked, 0).Vertices;
        var holed = TrainKit.Car(Look, Cargo, TrainKit.Livery.Planked, 0, holes: holes).Vertices;
        float w = (float)Cargo.HalfWidth;
        // Through the middle of each gap, nothing of the wall's outer face is left: no triangle of the side plane covers it.
        foreach (var g in holes.Walls.Take(4))
        {
            var mid = new Vector2((g.Z0 + g.Z1) / 2, (g.Y0 + g.Y1) / 2);
            Assert.True(Covers(whole, g.Side * w, mid), "the whole car's wall is there");
            Assert.False(Covers(holed, g.Side * w, mid), $"the wall's still there at z {mid.X:0.00}, y {mid.Y:0.00}");
        }
        // Within the car's bounds as ever, and within its budget.
        var b = Cargo.Bounds;
        Assert.All(holed, v => Assert.InRange(v.Position.Y, (float)b.Min.Y - 0.5f, (float)Cargo.RoofHeight + 0.6f));
        Assert.InRange(holed.Length / 3, whole.Length / 3, whole.Length / 3 + 2500);
    }

    [Fact]
    public void TheRoofIsOpenToTheSkyWhereItsSheetsAreGone()
    {
        var holes = DamageKit.CarHoles(Cargo, 2, 2);
        Assert.NotEmpty(holes.Roof);
        var whole = TrainKit.Car(Look, Cargo, TrainKit.Livery.Armoured, 0).Vertices;
        var holed = TrainKit.Car(Look, Cargo, TrainKit.Livery.Armoured, 0, holes: holes).Vertices;
        float w = (float)Cargo.HalfWidth, ceiling = (float)Cargo.Interior!.Value.Max.Y;
        foreach (var g in holes.Roof)
        {
            // Looking down at the middle of the gone sheet, from above the roof to the ceiling: nothing in the way.
            var at = new Vector2(g.Side * (g.Inner + w) / 2, (g.Z0 + g.Z1) / 2);
            Assert.True(Above(whole, at, ceiling - 0.05f), "the whole car's roof is there");
            Assert.False(Above(holed, at, ceiling - 0.05f), $"the roof's still there at x {at.X:0.00}, z {at.Y:0.00}");
        }
    }

    /// <summary>Whether a triangle above <paramref name="floor"/> covers (x, z) seen from overhead.</summary>
    static bool Above(Ballast.Render.Vertex[] v, Vector2 at, float floor)
    {
        for (int i = 0; i + 2 < v.Length; i += 3)
        {
            var (a, b, c) = (v[i].Position, v[i + 1].Position, v[i + 2].Position);
            if (a.Y < floor || b.Y < floor || c.Y < floor)
                continue;
            var (p, q, r) = (new Vector2(a.X, a.Z), new Vector2(b.X, b.Z), new Vector2(c.X, c.Z));
            float d1 = Cross(q - p, at - p), d2 = Cross(r - q, at - q), d3 = Cross(p - r, at - r);
            if (MathF.Abs(d1) + MathF.Abs(d2) + MathF.Abs(d3) < 1e-6f)
                continue;
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            if (!(neg && pos))
                return true;
        }
        return false;
    }

    [Fact]
    public void TheRupturedEnginesHoodIsOpenRoundTheTear()
    {
        var engine = CarShape.Build(Tuning.Geometry, VehicleKind.Engine, hasCarBehind: true);
        var gaps = DamageKit.HoodHole(engine);
        Assert.NotEmpty(gaps);
        var seam = TrainKit.RuptureSeam(engine);
        Assert.Contains(gaps, g => g.Holds(seam.Z, seam.Y));
        var hood = TrainKit.HoodFace(engine, -1, seam.Z)!.Value;
        var whole = TrainKit.Engine(Look, engine, 0).Vertices;
        var burst = TrainKit.Engine(Look, engine, 0, holes: gaps).Vertices;
        Assert.True(Covers(whole, -hood.X, new Vector2(seam.Z, seam.Y)));
        Assert.False(Covers(burst, -hood.X, new Vector2(seam.Z, seam.Y)));
        // The tear drawn over it then has nothing in its middle (the black disc's centre is the shut tear's).
        Assert.Contains(TrainKit.RuptureTear(Look).Vertices, v => MathF.Abs(v.Position.Y) < 0.01f && MathF.Abs(v.Position.Z) < 0.01f && v.Position.X < 0);
        Assert.DoesNotContain(TrainKit.RuptureTear(Look, open: true).Vertices, v => MathF.Abs(v.Position.Y) < 0.05f && MathF.Abs(v.Position.Z) < 0.05f && v.Position.X < 0);
    }

    /// <summary>Whether a triangle in the plane x = <paramref name="x"/> (within 2 cm) covers the point (z, y).</summary>
    static bool Covers(Ballast.Render.Vertex[] v, float x, Vector2 at)
    {
        for (int i = 0; i + 2 < v.Length; i += 3)
        {
            var (a, b, c) = (v[i].Position, v[i + 1].Position, v[i + 2].Position);
            if (MathF.Abs(a.X - x) > 0.02f || MathF.Abs(b.X - x) > 0.02f || MathF.Abs(c.X - x) > 0.02f)
                continue;
            var (p, q, r) = (new Vector2(a.Z, a.Y), new Vector2(b.Z, b.Y), new Vector2(c.Z, c.Y));
            float d1 = Cross(q - p, at - p), d2 = Cross(r - q, at - q), d3 = Cross(p - r, at - r);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            if (!(neg && pos))
                return true;
        }
        return false;
    }

    static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
}
