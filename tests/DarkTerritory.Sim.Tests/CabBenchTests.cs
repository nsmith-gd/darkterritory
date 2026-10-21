using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The director's playtest of 16 Oct 2026 (note 905), lines 2 and 3: "The benches in the cabin are kind of obstructing ...
/// move them to like side benches so that we keep the middle clear. And then let's have them be really thin. And if we're
/// going to have benches at all, we need players to be able to actually sit down on them"; and "We still have this step from
/// car one to the locomotive car".
/// </summary>
public class CabBenchTests
{
    static readonly PlayerTuning P = Tuning.Player;
    static readonly GeometryTuning G = Tuning.Train.Geometry;
    static readonly EnginePlan Plan = EnginePlan.Of(G);

    static World Night(double speed = 0, int cars = 3)
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(20_000)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, cars, 1)), line, 1_000);
        if (speed > 0)
            train.Dynamics.Velocity = speed;
        var world = new World(train, Tuning.Combat);
        world.EnableBodies();
        return world;
    }

    /// <summary>A crewmate stood on the gallery in front of <paramref name="seat"/>, facing it.</summary>
    static PlayerState Before(World w, Lounge seat) =>
        PlayerMotor.SpawnInCab(w.Train, P) with { Position = Plan.SeatStand(seat), Yaw = -seat.Yaw, HeldSlot = 0 };

    static void Tick(World w, ref PlayerState s, PlayerIntent intent, int id = 1)
    {
        w.BeginTick();
        w.CrewAct(ref s, intent, id);
        w.Step(new TrainControls { Reverser = 1, Brake = 1 });
        PlayerMotor.Step(ref s, intent, w.Train, P, w.Train.Dynamics.Tuning, SimConstants.TickSeconds, applyLook: false);
    }

    static void Hold(World w, ref PlayerState s, PlayerIntent intent, double seconds)
    {
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
            Tick(w, ref s, intent);
    }

    static readonly PlayerIntent Seat = new() { Actions = PlayerActions.Seat };

    [Fact]
    public void TheBenchesAreThinAlongTheSideWallsAndTheGallerysMiddleIsClear()
    {
        var shape = CarShape.Build(G, VehicleKind.Engine, hasCarBehind: true);
        var c = G.Engine.CrewCar;
        double inner = G.RoofWidth / 2 - 0.1;
        Assert.Equal(2, Plan.Benches.Count);
        foreach (var (bench, side) in Plan.Benches.Zip(new[] { -1, 1 }))
        {
            Assert.Contains(shape.Solids, s => s.Part == PartKind.Bench && s.Box == bench);
            // Against its wall, thin (a seat's depth, no more), on the gallery's floor, the gallery's length.
            Assert.Equal(side * inner, side < 0 ? bench.Min.X : bench.Max.X, 6);
            Assert.Equal(c.BenchDepth, bench.Max.X - bench.Min.X, 6);
            Assert.InRange(c.BenchDepth, 0.15, 0.3);
            Assert.Equal(Plan.Gallery.Max.Y, bench.Min.Y, 6);
            Assert.True(bench.Min.Z >= Plan.Gallery.Min.Z && bench.Max.Z <= Plan.Gallery.Max.Z);
            Assert.True(bench.Max.Z - bench.Min.Z >= 2.5, "a bench the gallery's length");
        }
        // The middle clear between them: wider than two crewmates passing, and nothing of the furniture in it on the gallery.
        double clear = Plan.Benches[1].Min.X - Plan.Benches[0].Max.X;
        Assert.True(clear >= 2.0, $"{clear:0.00} m clear");
        Assert.DoesNotContain(shape.Solids, s => s.Part is PartKind.Bench or PartKind.ChartTable && s.Box.Min.Z < Plan.Gallery.Max.Z
            && s.Box.Max.Z > Plan.Gallery.Min.Z && s.Box.Min.X < 0.5 && s.Box.Max.X > -0.5);
        // The way down the cab is clear of the chart table and of the benches by a body's radius: nobody walking it meets a bench.
        Assert.True(Plan.Lane - P.Radius >= Plan.ChartTable.Max.X - 1e-9, "the way's clear of the table");
        Assert.True(Plan.Lane + P.Radius <= Plan.Benches[1].Min.X + 1e-9, "the way's clear of the bench");
    }

    [Fact]
    public void EachBenchHasItsSeatsOnItsTopFacingAcrossTheCab()
    {
        var shape = Night().Train.Frames[0].Shape;
        var seats = shape.Lounges.Where(l => l.Kind == LoungeKind.Bench).ToList();
        Assert.Equal(2 * G.Engine.CrewCar.BenchSeats, seats.Count);
        Assert.Equal(Plan.Seats, seats);
        foreach (var seat in seats)
        {
            var bench = Plan.Benches.Single(b => b.ContainsXZ(seat.At));
            Assert.Equal(bench.Max.Y, seat.At.Y, 6);
            // Facing the middle: a sitter's forward (−sin yaw, −cos yaw) points across, at the other bench.
            Assert.Equal(-Math.Sign(seat.At.X), Math.Sign(-Math.Sin(seat.Yaw)));
            Assert.True(Math.Abs(Math.Cos(seat.Yaw)) < 1e-9);
            // Each with its Seat press in reach of the floor in front of it, and nobody's seat too close to the next.
            var press = Assert.Single(shape.Interactables, i => i.Kind == InteractableKind.Bench && shape.Lounges[i.Index] == seat);
            Assert.Equal(Plan.Gallery.Max.Y, press.Position.Y, 6);
            Assert.False(bench.ContainsXZ(press.Position));
        }
        foreach (var a in seats)
            Assert.All(seats.Where(b => b != a && Math.Sign(b.At.X) == Math.Sign(a.At.X)), b => Assert.True(Math.Abs(a.At.Z - b.At.Z) >= G.Engine.CrewCar.SeatEvery - 1e-9));
    }

    [Fact]
    public void EverySeatIsSatOnFromInFrontOfItAndOnlyThatOne()
    {
        var w = Night(speed: 15);
        var shape = w.Train.Frames[0].Shape;
        foreach (var seat in shape.Lounges.Where(l => l.Kind == LoungeKind.Bench))
        {
            var s = Before(w, seat);
            Tick(w, ref s, Seat);
            Assert.True(s.Has(PlayerFlags.Lounging), $"not sat at {seat.At}");
            Assert.True((s.Position - seat.At).Length < 1e-6, $"sat at {s.Position}, not {seat.At}");
            Assert.Equal(seat.Yaw, s.Yaw, 6);
            Assert.Equal(seat, Bbq.LoungingIn(s, w.Train));
        }
    }

    [Fact]
    public void SatOnABenchYouRideTheTrainAndGetUpOntoTheFloorInFrontOfIt()
    {
        var w = Night(speed: 15);
        var seat = Plan.Seats[1];
        var s = Before(w, seat);
        Tick(w, ref s, Seat);
        Assert.True(s.Has(PlayerFlags.Lounging));
        // Sat, the stick moves nothing; the train running carries you on the bench.
        Hold(w, ref s, new PlayerIntent { MoveZ = 1, MoveX = 1 }, 2);
        Assert.True(s.Has(PlayerFlags.Lounging));
        Assert.Equal(0, s.Parent);
        Assert.True((s.Position - seat.At).Length < 0.05);
        // Jump gets you up: stood on the gallery's floor in front of the bench, not on top of it.
        Tick(w, ref s, new PlayerIntent { Buttons = PlayerButtons.Jump });
        Assert.False(s.Has(PlayerFlags.Lounging));
        Hold(w, ref s, default, 0.5);
        Assert.Equal(Plan.Gallery.Max.Y, s.Position.Y, 3);
        Assert.True(PlayerMotor.InCab(s, w.Train));
        Assert.False(Plan.Benches.Any(b => b.ContainsXZ(s.Position)), $"up onto the bench at {s.Position}");
        // The press sits you again, and the press again gets you up, onto the floor too.
        Tick(w, ref s, Seat);
        Assert.True(s.Has(PlayerFlags.Lounging));
        Tick(w, ref s, Seat);
        Assert.False(s.Has(PlayerFlags.Lounging));
        Hold(w, ref s, default, 0.5);
        Assert.Equal(Plan.Gallery.Max.Y, s.Position.Y, 3);
    }

    [Fact]
    public void SomebodySatOnASeatKeepsIt()
    {
        var w = Night();
        var seat = Plan.Seats[0];
        var sitter = Before(w, seat);
        var other = Before(w, seat);
        w.BeginTick();
        w.CrewAct(ref sitter, Seat, 1);
        w.StepBodies([(1, sitter), (2, other)]);
        w.CrewAct(ref other, Seat, 2);
        Assert.True(sitter.Has(PlayerFlags.Lounging));
        Assert.False(other.Has(PlayerFlags.Lounging));
    }

    [Fact]
    public void FromCarOnesFloorToTheEnginesDeckEveryStepIsAPebbleNotAStep()
    {
        // Note 905: the plate behind the engine is car 1's floor's height and the engine's deck 0.3 m over it. Its ramp's
        // treads take it up in rises of RampRise at most, across the plate's width, from the plate's top to the deck's.
        var g = G;
        var shape = CarShape.Build(g, VehicleKind.Engine, hasCarBehind: true);
        var plate = shape.Solids.Single(x => x.Part == PartKind.Coupler).Box;
        var treads = shape.Solids.Where(x => x.Part == PartKind.Ramp).Select(x => x.Box).OrderBy(b => b.Min.Z).ToList();
        Assert.NotEmpty(treads);
        Assert.Equal(g.Interior!.FloorHeight, plate.Max.Y, 6);
        double deck = g.Engine.DeckHeight, was = deck;
        Assert.Equal(deck, treads[0].Max.Y, 6);
        Assert.Equal(shape.HalfLength, treads[0].Min.Z, 6);
        foreach (var t in treads)
        {
            Assert.Equal(plate.Min.X, t.Min.X, 6);
            Assert.Equal(plate.Max.X, t.Max.X, 6);
            Assert.True(was - t.Max.Y <= CarShape.RampRise + 1e-9);
            was = t.Max.Y;
        }
        Assert.True(was - plate.Max.Y <= CarShape.RampRise + 1e-9 && was > plate.Max.Y);
        Assert.True(treads[^1].Max.Z < plate.Max.Z, "the plate's own end at car 1's door");
        // No ramp on an engine with nothing behind it, nor across a Knotter's back.
        Assert.DoesNotContain(CarShape.Build(g, VehicleKind.Engine, hasCarBehind: false).Solids, x => x.Part == PartKind.Ramp);
        Assert.DoesNotContain(CarShape.Knotted(shape, g, 0.6).Solids, x => x.Part == PartKind.Ramp);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public void WalkedFromCarOneOntoTheEngineNoTickRisesMoreThanAPebble(double speed)
    {
        var w = Night(speed);
        var train = w.Train;
        var car = train.Frames[1].Shape;
        var floor = G.Interior!.FloorHeight;
        // Inside car 1 at its front door, walking forward (−Z) out over the plate and up onto the engine's deck.
        var door = car.DoorList.First(d => d.Box.Min.Z <= -car.HalfLength + 1e-6);
        var s = PlayerMotor.SpawnInCab(train, P) with
        {
            Parent = 1,
            Position = new Double3((door.Box.Min.X + door.Box.Max.X) / 2, floor, -car.HalfLength + 1.0),
            Yaw = 0,
            Surface = Surface.Deck,
        };
        train.Vehicles[1].DoorsOpen = 0xFF;
        double highest = s.Position.Y, maxRise = 0;
        for (int i = 0; i < 4 * SimConstants.TickRate; i++)
        {
            int parent = s.Parent;
            double y = s.Position.Y;
            Tick(w, ref s, new PlayerIntent { MoveZ = 1 });
            if (s.Parent == parent)
                maxRise = Math.Max(maxRise, s.Position.Y - y);
            highest = Math.Max(highest, s.Position.Y);
            Assert.NotEqual(Surface.Air, s.Surface);
        }
        Assert.Equal(0, s.Parent);
        Assert.Equal(G.Engine.DeckHeight, s.Position.Y, 3);
        Assert.True(maxRise <= CarShape.RampRise + 1e-6, $"a rise of {maxRise:0.000} m in a tick");
    }

    [Fact]
    public void ABotWalksFromAnywhereInTheCabToAnywhereElseRoundTheTableAndPastTheBenches()
    {
        // Note 905: the side benches narrow the gallery's ways; every walk a bot makes in the cab (WarmUp.SteerCab) still
        // arrives: the footplate, either side of the chart table, the gallery's middle and in front of each bench, the back
        // wall's doorways and the corridors along the boiler.
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 3, 1)), new RailLine(new LineDefinition("t", [new TrackSegment(2000)])), 500);
        double inner = G.RoofWidth / 2 - 0.1, mid = (Plan.Gallery.Min.Z + Plan.Gallery.Max.Z) / 2;
        double corridor = G.Engine.BoilerHalfWidth + (inner - G.Engine.BoilerHalfWidth) / 2, door = Plan.DoorFront + G.Doorway.Width / 2;
        var places = new List<Double3>
        {
            new(0, 0, PlayerMotor.CabFloorZ(train.Frames[0].Shape)), new(0.8, 0, Plan.ChartTable.Min.Z - 0.4), new(-0.52, 0, Plan.ChartTable.Min.Z - 0.4),
            new(0, 0, mid), new(1.1, 0, door), new(-1.1, 0, door), new(corridor, 0, Plan.CabBack - 0.6), new(-corridor, 0, Plan.CabBack - 0.6),
        };
        foreach (int side in new[] { -1, 1 })
            places.Add(new(side * Plan.Lane, 0, (Plan.ChartTable.Min.Z + Plan.ChartTable.Max.Z) / 2));
        places.AddRange(Plan.Seats.Where((_, i) => i % 3 == 0).Select(Plan.SeatStand));
        var stuck = new List<string>();
        foreach (var start in places)
            // (Not straight across from one of the chart table's ways to the other, abreast of it: CabWay's hemmed-in case, as
            // it has been since note 716.)
            foreach (var to in places.Where(p => p != start && !(Math.Abs(p.Z - start.Z) < 1e-9 && p.Z == (Plan.ChartTable.Min.Z + Plan.ChartTable.Max.Z) / 2)))
            {
                var s = PlayerMotor.SpawnInCab(train, P, 0);
                s.Position = start with { Y = train.Frames[0].Shape.TopAt(start.X, start.Z, Plan.Gallery.Max.Y + 0.01)!.Value.Top };
                for (int i = 0; i < 3 * SimConstants.TickRate; i++)
                    PlayerMotor.Step(ref s, default, train, P, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
                bool there = false;
                for (int i = 0; i < 20 * SimConstants.TickRate && !there; i++)
                {
                    var (step, arrived) = Bots.WarmUp.SteerCab(s, train, to, s.Yaw);
                    there = arrived || (Flat(s.Position) - Flat(to)).Length < 0.2;
                    PlayerMotor.Step(ref s, step, train, P, Tuning.Train, SimConstants.TickSeconds);
                }
                if (!there)
                    stuck.Add($"({start.X:0.00},{start.Z:0.00}) -> ({to.X:0.00},{to.Z:0.00}): at ({s.Position.X:0.00},{s.Position.Z:0.00})");
            }
        Assert.True(stuck.Count == 0, string.Join("\n", stuck));
    }

    static Double3 Flat(Double3 v) => v with { Y = 0 };
}
