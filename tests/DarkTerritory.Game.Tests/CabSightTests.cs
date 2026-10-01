using Ballast;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// T101 (playtest: "there also needs to be a way for the driver to see the track ahead and the front of the train on
/// their own ... players should clearly see the firebox, map, gauges, speed, brake, and vent"): from the driver's place
/// the line ahead is in sight through the window beside the boiler, past the boiler and its stack, and every working of
/// the cab is in front of the driver; from the fireman's side, the blow-off on the running board.
/// </summary>
public class CabSightTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TrainTuning Tuning = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
    static readonly CarShape Engine = CarShape.Build(Tuning.Geometry, VehicleKind.Engine, hasCarBehind: true);
    static double CabFront => Engine.Cab!.Value.Min.Z;

    /// <summary>Where a sightline from the eye crosses the spectacle plate, and whether that's through its open window.</summary>
    static bool ThroughWindow(Double3 eye, Double3 at, int side)
    {
        var w = TrainKit.SpectacleWindow(Engine, side);
        double t = (CabFront - eye.Z) / (at.Z - eye.Z);
        var p = eye + (at - eye) * t;
        return t is > 0 and < 1 && p.X > w.X0 && p.X < w.X1 && p.Y > w.Y0 && p.Y < w.Y1;
    }

    /// <summary>Whether the sightline is clear of every solid ahead of the cab (the boiler, its stack, the cab walls).</summary>
    static bool Clear(Double3 eye, Double3 at)
    {
        foreach (var s in Engine.Solids)
            if (s.Part is PartKind.Boiler or PartKind.Stack or PartKind.CabWall && Hits(eye, at, s.Box))
                return false;
        return true;
    }

    static bool Hits(Double3 a, Double3 b, Box box)
    {
        double t0 = 0, t1 = 1;
        var d = b - a;
        for (int axis = 0; axis < 3; axis++)
        {
            double o = axis == 0 ? a.X : axis == 1 ? a.Y : a.Z, dd = axis == 0 ? d.X : axis == 1 ? d.Y : d.Z;
            double lo = axis == 0 ? box.Min.X : axis == 1 ? box.Min.Y : box.Min.Z, hi = axis == 0 ? box.Max.X : axis == 1 ? box.Max.Y : box.Max.Z;
            if (Math.Abs(dd) < 1e-12)
            {
                if (o < lo || o > hi)
                    return false;
                continue;
            }
            double ta = (lo - o) / dd, tb = (hi - o) / dd;
            t0 = Math.Max(t0, Math.Min(ta, tb));
            t1 = Math.Min(t1, Math.Max(ta, tb));
            if (t0 > t1)
                return false;
        }
        return true;
    }

    [Theory]
    // The near rail, 30 m on; and the middle of the line from 60 m.
    [InlineData(TrainKit.HalfGauge, 30)]
    [InlineData(0, 60)]
    [InlineData(0, 150)]
    public void FromTheDriversPlaceTheLineAheadIsInSightThroughTheWindow(double lateral, double ahead)
    {
        var eye = Views.CabEye(Engine);
        var rail = new Double3(lateral, 0.15, -Engine.HalfLength - ahead);
        Assert.True(ThroughWindow(eye, rail, 1), "it's seen through the right-hand window");
        Assert.True(Clear(eye, rail), "past the boiler and its stack");
    }

    [Fact]
    public void FromTheDriversPlaceTheRunningBoardRunsToTheFrontOfTheEngine()
    {
        // The right running board's outer edge at the buffer beam: the front of the train.
        var eye = Views.CabEye(Engine);
        var front = new Double3(Engine.HalfWidth + Tuning.Geometry.Engine.RunningBoardWidth, Tuning.Geometry.Engine.DeckHeight, -Engine.HalfLength + 0.6);
        Assert.True(ThroughWindow(eye, front, 1));
        Assert.True(Clear(eye, front));
    }

    [Fact]
    public void FromTheFiremansSideTheBlowOffIsInSight()
    {
        var eye = Views.CabEye(Engine, -1);
        var vent = Engine.Interactables.Single(i => i.Kind == InteractableKind.Vent).Position + new Double3(0, 1.1, 0);
        Assert.True(ThroughWindow(eye, vent, -1));
        Assert.True(Clear(eye, vent));
    }

    [Fact]
    public void TheGaugesTheMapTheFireboxAndTheBrakeAreAllInFrontOfTheDriver()
    {
        var eye = Views.CabEye(Engine);
        // Within a comfortable look from straight ahead: 50 degrees off, as far as eyes go without turning the head.
        void InView(Double3 at, string what)
        {
            var d = (at - eye).Normalized;
            double off = Math.Acos(-d.Z) * 180 / Math.PI;
            Assert.True(off < 50, $"the {what} is {off:0} degrees off straight ahead");
        }
        for (int i = 0; i < 4; i++)
        {
            var c = TrainKit.GaugeCentre(Engine, i);
            InView(new Double3(c.X, c.Y, c.Z), i == 3 ? "speed gauge" : $"gauge {i}");
        }
        var map = TrainKit.MapPlate(Engine);
        InView(new Double3(map.Corner.X + map.Width / 2, map.Corner.Y + map.Height / 2, map.Corner.Z), "map");
        InView(Engine.Interactables.Single(i => i.Kind == InteractableKind.Firebox).Position + new Double3(0, 0.7, 0), "firebox door");
        InView(Engine.Levers!.Value.Brake, "brake");
        // And big enough to read from there: a dial's face over six degrees across.
        var g = TrainKit.GaugeCentre(Engine, 3);
        double distance = (new Double3(g.X, g.Y, g.Z) - eye).Length;
        Assert.True(2 * Math.Atan(TrainKit.GaugeRadius / distance) * 180 / Math.PI > 6, "the speed dial reads from the driver's place");
    }
}
