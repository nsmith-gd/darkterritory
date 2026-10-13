using Ballast;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// T101 (playtest: "there also needs to be a way for the driver to see the track ahead and the front of the train on
/// their own ... players should clearly see the firebox, map, gauges, speed, brake, and vent"), cab forward (ARCHITECTURE
/// §8 note 276, the director's sketch: "controls at the front with full vis of the rail"): from the driver's place the line
/// ahead is in sight through the front window from a few metres past the plough, with nothing of the engine in the way;
/// the driver's gauges, the map and the brake are in front of them, and (note 280) so are the fire and the coal: one person
/// runs the cab facing forward.
/// </summary>
public class CabSightTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TrainTuning Tuning = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
    static readonly CarShape Engine = CarShape.Build(Tuning.Geometry, VehicleKind.Engine, hasCarBehind: true);
    static double CabFront => Engine.Cab!.Value.Min.Z;

    /// <summary>Where a sightline from the eye crosses the cab's front, and whether that's through its open window.</summary>
    static bool ThroughWindow(Double3 eye, Double3 at, int side)
    {
        var w = TrainKit.FrontWindow(Engine, side);
        double t = (CabFront - eye.Z) / (at.Z - eye.Z);
        var p = eye + (at - eye) * t;
        return t is > 0 and < 1 && p.X > w.X0 && p.X < w.X1 && p.Y > w.Y0 && p.Y < w.Y1;
    }

    /// <summary>Whether the sightline is clear of the engine's solids that could stand in it (the boiler, its stack, the cab walls, the bunker).</summary>
    static bool Clear(Double3 eye, Double3 at)
    {
        foreach (var s in Engine.Solids)
            if (s.Part is PartKind.Boiler or PartKind.Stack or PartKind.CabWall or PartKind.Tender or PartKind.Firebox && Hits(eye, at, s.Box))
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
    // The near rail and the middle of the line, from 8 m past the plough (with the boiler ahead it was 30 m: T101), and on.
    [InlineData(TrainKit.HalfGauge, 8)]
    [InlineData(0, 10)]
    [InlineData(TrainKit.HalfGauge, 30)]
    [InlineData(0, 60)]
    [InlineData(0, 150)]
    public void FromTheDriversPlaceTheLineAheadIsInSightThroughTheWindow(double lateral, double ahead)
    {
        var eye = Views.CabEye(Engine);
        var rail = new Double3(lateral, 0.15, -Engine.HalfLength - ahead);
        Assert.True(ThroughWindow(eye, rail, 1), "it's seen through the right-hand window");
        Assert.True(Clear(eye, rail), "with nothing of the engine in the way");
    }

    [Fact]
    public void TheDriversPlaceIsAtTheFrontOfTheTrain()
    {
        // Cab forward (note 276): nothing of the engine ahead of the cab but the pilot under its nose.
        Assert.True(CabFront - -Engine.HalfLength <= Tuning.Geometry.Engine.PilotLength + 1e-9);
        foreach (var s in Engine.Solids.Where(s => s.Part is PartKind.Boiler or PartKind.Stack))
            Assert.True(s.Box.Min.Z >= Engine.Cab!.Value.Max.Z - 1e-9, $"the {s.Part} is behind the cab");
        Assert.True(Views.CabEye(Engine).Z - CabFront < 2.0, "the driver stands at the front windows");
    }

    [Fact]
    public void FromTheDriversPlaceTheVentIsInTheCabAFewStepsAcross()
    {
        // T109 playtest: the vent in the cab, so one player works it all from the footplate; seen from the driver's place.
        var eye = Views.CabEye(Engine);
        var valve = Engine.Interactables.Single(i => i.Kind == InteractableKind.Vent).Position;
        Assert.True(Engine.Cab!.Value.Contains(valve + new Double3(0, 0.2, 0)));
        Assert.True(((valve - eye) with { Y = 0 }).Length < 3.5);
        Assert.True(Clear(eye, valve + new Double3(0, 1.1, 0)));
    }

    [Fact]
    public void TheGaugesTheMapAndTheBrakeAreAllInFrontOfTheDriver()
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
            var c = TrainKit.DriverGauge(Engine, i);
            InView(new Double3(c.X, c.Y, c.Z), i == 3 ? "speed gauge" : $"gauge {i}");
        }
        var map = TrainKit.MapPlate(Engine);
        InView(new Double3(map.Corner.X + map.Width / 2, map.Corner.Y + map.Height / 2, map.Corner.Z), "map");
        InView(Engine.Levers!.Value.Brake, "brake");
        // And big enough to read from there: a dial's face over six degrees across.
        var g = TrainKit.DriverGauge(Engine, 3);
        double distance = (new Double3(g.X, g.Y, g.Z) - eye).Length;
        Assert.True(2 * Math.Atan(TrainKit.DriverGaugeRadius / distance) * 180 / Math.PI > 6, "the speed dial reads from the driver's place");
        // Over the window: the line's view isn't cut by them.
        Assert.True(g.Y - TrainKit.DriverGaugeRadius > TrainKit.FrontWindow(Engine, 1).Y1);
        // And under the ceiling as it comes down over the front (the director, 8 Oct: "the map and gauges are still
        // covered"): every dial whole below it, the map's top too, and none of them over another.
        for (int i = 0; i < 4; i++)
        {
            var c = TrainKit.DriverGauge(Engine, i);
            Assert.True(c.Y + TrainKit.DriverGaugeRadius + 0.02f < TrainKit.CeilingAt(Engine, c.Z), $"gauge {i} is up in the ceiling");
            Assert.True(c.Y - TrainKit.DriverGaugeRadius > TrainKit.FrontWindow(Engine, 1).Y1, $"gauge {i} is down over the window");
            Assert.True(c.X - TrainKit.DriverGaugeRadius > map.Corner.X + map.Width, $"gauge {i} is over the map");
        }
        Assert.True(map.Corner.Y + map.Height < TrainKit.CeilingAt(Engine, map.Corner.Z), "the map is up in the ceiling");
    }

    [Fact]
    public void OnePersonRunsTheCabFacingForwardTheCoalTheFireAndTheControlsInReach()
    {
        // Note 280, the director: "firebox at the front but no need to turn around for coal, the whole cab being operable by
        // one person". The fire door faces back into the cab from its front: whoever fires faces down the line.
        Assert.True(TrainKit.OutOfBackhead(Engine).Z > 0.99, "the fire door faces back into the cab");
        var fire = Engine.Interactables.Single(i => i.Kind == InteractableKind.Firebox);
        var coal = Engine.Interactables.Single(i => i.Kind == InteractableKind.Coal);
        var vent = Engine.Interactables.Single(i => i.Kind == InteractableKind.Vent);
        var firebox = Engine.Solids.Single(s => s.Part == PartKind.Firebox).Box;
        Assert.True(firebox.Min.Z - CabFront < 0.2, "against the front wall");
        Assert.True(firebox.Max.Y < TrainKit.FrontWindow(Engine, 1).Y0 + 1e-6, "under the windows");
        // One place behind the fire door where the fire and the coal are both in reach, the coal ahead or beside, never behind.
        var stand = new Double3(fire.Position.X + 0.4, fire.Position.Y, fire.Position.Z + 0.3);
        static double Flat(Double3 a, Double3 b) => ((a - b) with { Y = 0 }).Length;
        Assert.True(Flat(stand, fire.Position) < fire.Radius);
        Assert.True(Flat(stand, coal.Position) < coal.Radius + 0.4, $"the coal {Flat(stand, coal.Position):0.00} m off");
        Assert.True(coal.Position.Z <= stand.Z + 0.1, "the coal isn't behind whoever's firing");
        // The levers a step to the right, the vent a step further, all forward of the doorways.
        var levers = Engine.Levers!.Value;
        foreach (var lever in new[] { levers.Regulator, levers.Brake, levers.Reverser, vent.Position })
            Assert.True(Flat(stand, lever) < 1.6, $"{lever} is {Flat(stand, lever):0.00} m off");
        // And the line's view over it all: the fire door from the driver's place, nothing between.
        var eye = Views.CabEye(Engine);
        var door = TrainKit.FireDoor(Engine);
        Assert.True(door.Z < eye.Z, "ahead of the driver");
        Assert.True(Clear(eye, new Double3(door.X, door.Y, door.Z + 0.05)));
    }
}
