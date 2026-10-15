using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The big cab, the crew car (ARCHITECTURE §8 note 716; the director, 14 Oct 2026: "big cab, 11 meters, smaller boiler. I
/// still want to keep a bunch of the controls the way that we have it set up because we are going to have this game
/// playable by a single player"): the cab 11 m with the footplate at its front exactly as the 5 m cab had it, then the chart
/// table, the gallery a step up, and the back wall with its doors and its gimmicks.
/// </summary>
public class CrewCarTests
{
    static TrainOnLine Train() =>
        new(new TrainDynamics(Consist.Uniform(Tuning.Train, 3, 1)), new RailLine(new LineDefinition("t", [new TrackSegment(2000)])), 500);

    [Fact]
    public void TheCabIsElevenMetresOfTheTwentyAndTheBoilerTheRest()
    {
        var g = Tuning.Train.Geometry;
        var plan = EnginePlan.Of(g);
        Assert.Equal(20, g.EngineLength);
        Assert.Equal(11, plan.CabBack - plan.CabFront, 6);
        var shape = CarShape.Build(g, VehicleKind.Engine, hasCarBehind: true);
        var boiler = shape.Solids.Single(s => s.Part == PartKind.Boiler).Box;
        Assert.InRange(boiler.Max.Z - boiler.Min.Z, 7.5, 8.5);
        Assert.Equal(plan.CabBack, boiler.Min.Z, 6);
    }

    [Fact]
    public void TheFootplateIsWhereItWasInTheFiveMetreCab()
    {
        // Everything one person drives the train from (note 280): the coal, the fire, the controls, the cord, the vent, the
        // rack, the points lever, the ladder up to the gun and the gun: where it was behind the cab's front, to the millimetre.
        var g = Tuning.Train.Geometry;
        var old = g with { Engine = g.Engine with { CabLength = 5 } };
        var big = CarShape.Build(g, VehicleKind.Engine, hasCarBehind: true);
        var was = CarShape.Build(old, VehicleKind.Engine, hasCarBehind: true);
        double front = big.Cab!.Value.Min.Z, wasFront = was.Cab!.Value.Min.Z;
        Assert.Equal(wasFront, front, 6);
        foreach (var part in new[] { PartKind.Tender, PartKind.Firebox })
        {
            var a = big.Solids.Single(s => s.Part == part).Box;
            var b = was.Solids.Single(s => s.Part == part).Box;
            Assert.Equal(b, a);
        }
        foreach (var kind in new[] { InteractableKind.Firebox, InteractableKind.Coal, InteractableKind.Vent, InteractableKind.ToolRack, InteractableKind.Points, InteractableKind.Whistle })
            Assert.Equal(was.Interactables.Single(i => i.Kind == kind), big.Interactables.Single(i => i.Kind == kind));
        Assert.Equal(was.Levers, big.Levers);
        Assert.Equal(was.Gun, big.Gun);
        Assert.Equal(was.Ladders.Single(l => l.Foot.Y > 0.2), big.Ladders.Single(l => l.Foot.Y > 0.2));
        Assert.Equal(PlayerMotor.CabFloorZ(was), PlayerMotor.CabFloorZ(big), 6);
        // And nothing of the crew car stands in it.
        var plan = EnginePlan.Of(g);
        Assert.All(plan.Furniture.Append(plan.FrontTread), f => Assert.True(f.Min.Z >= front + g.Engine.CrewCar.FootplateLength, $"{f} is on the footplate"));
        // The coal still pours where it did (the bunker hasn't moved).
        Assert.Equal(EnginePlan.Of(old).CoalFromFront, plan.CoalFromFront, 6);
    }

    [Fact]
    public void TheChartTableTheGalleryAndItsBenchesStandBehindTheFootplate()
    {
        var g = Tuning.Train.Geometry;
        var plan = EnginePlan.Of(g);
        var shape = CarShape.Build(g, VehicleKind.Engine, hasCarBehind: true);
        double deck = g.Engine.DeckHeight;
        Assert.Contains(shape.Solids, s => s.Part == PartKind.ChartTable && s.Box == plan.ChartTable);
        Assert.Equal(deck + g.Engine.CrewCar.TableHeight, plan.ChartTable.Max.Y, 6);
        Assert.Equal(deck + g.Engine.CrewCar.GalleryRise, plan.Gallery.Max.Y, 6);
        // Each tread no more than a step (player.json stepUp), and the gallery wall to wall.
        Assert.True(plan.FrontTread.Max.Y - deck <= Tuning.Player.StepUp && plan.Gallery.Max.Y - plan.FrontTread.Max.Y <= Tuning.Player.StepUp);
        Assert.Equal(shape.Cab!.Value.Min.X, plan.Gallery.Min.X, 6);
        Assert.Equal(2, plan.Benches.Count);
        Assert.All(plan.Benches, b => Assert.True(b.Min.Y == plan.Gallery.Max.Y && b.Max.Z <= plan.Gallery.Max.Z));
        // The ways either side of the furniture are a body's breadth and more.
        foreach (var f in plan.Furniture)
        {
            Assert.True(shape.Cab!.Value.Max.X - f.Max.X >= 2 * Tuning.Player.Radius + 0.2);
            Assert.True(f.Min.X - shape.Cab!.Value.Min.X >= 2 * Tuning.Player.Radius + 0.2);
        }
        // Behind the gallery, the back wall's floor at the cab's, the doorways in it.
        Assert.True(plan.BackTread.Max.Z <= plan.DoorFront - 0.5, "the doorways open onto the cab floor behind the gallery");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void FromTheFootplateYouWalkUpOntoTheGalleryDownToTheBackWallAndOutOfItsDoor(int side)
    {
        var train = Train();
        var g = Tuning.Train.Geometry;
        var plan = EnginePlan.Of(g);
        var shape = train.Frames[0].Shape;
        var s = PlayerMotor.SpawnInCab(train, Tuning.Player, side * 0.3);
        double lane = side * plan.Lane, deck = g.Engine.DeckHeight;
        // Out into the way past the chart table, up onto the gallery.
        Walk(ref s, train, new Double3(lane, 0, plan.ChartTable.Min.Z - 0.35), 2);
        Walk(ref s, train, new Double3(lane, 0, (plan.Gallery.Min.Z + plan.Gallery.Max.Z) / 2), 4);
        Assert.Equal(plan.Gallery.Max.Y, s.Position.Y, 3);
        Assert.True(PlayerMotor.InCab(s, train));
        // In front of the benches to the middle, and back into the way.
        Walk(ref s, train, new Double3(0, 0, plan.Benches[0].Min.Z - 0.4), 2);
        Assert.Equal(plan.Gallery.Max.Y, s.Position.Y, 3);
        Walk(ref s, train, new Double3(lane, 0, plan.Benches[0].Min.Z - 0.4), 2);
        // Down off its back to the back wall's floor, and out of its door onto the running board.
        double door = plan.DoorFront + g.Doorway.Width / 2, board = side * (shape.HalfWidth + g.Engine.RunningBoardWidth / 2);
        Walk(ref s, train, new Double3(lane, 0, door), 3);
        Assert.Equal(deck, s.Position.Y, 3);
        Walk(ref s, train, new Double3(board, 0, door), 2);
        Assert.False(PlayerMotor.InCab(s, train));
        Assert.Equal(Surface.Deck, s.Surface);
        Assert.True(Math.Abs(s.Position.X - board) < 0.2, $"at {s.Position}");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void DownTheGallerysTreadsYouStepNotFallAndStayInTheCab(int side)
    {
        // Off the treads into the air for a few ticks was out of the cab (Surface.Air, the world's frame): its space the
        // outside, a bot's sanding called off. Down a tread is a step down, as up one is a step up.
        var train = Train();
        var plan = EnginePlan.Of(Tuning.Train.Geometry);
        var s = PlayerMotor.SpawnInCab(train, Tuning.Player, side * 0.3);
        double lane = side * plan.Lane;
        Walk(ref s, train, new Double3(lane, 0, plan.ChartTable.Min.Z - 0.35), 2);
        foreach (var to in new[] { new Double3(lane, 0, plan.CabBack - 1), new Double3(lane, 0, plan.ChartTable.Max.Z + 0.2) })
            for (int i = 0; i < 5 * SimConstants.TickRate; i++)
            {
                PlayerMotor.Step(ref s, Toward(s, to), train, Tuning.Player, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
                Assert.True(PlayerMotor.InCab(s, train) && s.Surface == Surface.Deck, $"out of the cab at {s.Position} ({s.Surface})");
            }
    }

    [Fact]
    public void OnTheGalleryTheWaistIsAWaist()
    {
        // Up a step, its sides stand as high over its floor as the footplate's over the cab's: nobody steps out over a sill.
        var g = Tuning.Train.Geometry;
        var plan = EnginePlan.Of(g);
        var shape = CarShape.Build(g, VehicleKind.Engine, hasCarBehind: true);
        double z = (plan.Gallery.Min.Z + plan.Gallery.Max.Z) / 2;
        foreach (int side in new[] { -1, 1 })
        {
            double top = shape.Solids.Where(s => s.Part == PartKind.CabWall && s.Box.ContainsXZ(new Double3(side * (shape.HalfWidth - 0.05), 0, z)) && s.Box.Max.Y < deck(g) + 2)
                .Max(s => s.Box.Max.Y);
            Assert.Equal(plan.Gallery.Max.Y + g.Engine.CrewCar.GalleryWaist, top, 6);
        }
        static double deck(GeometryTuning g) => g.Engine.DeckHeight;
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void ABotGoesRoundTheTableAndTheBenchesToTheBackWall(int side)
    {
        // The bots walk the cab by its ways (WarmUp.SteerCab): from the fire to the corridor's mouth at the back, and back.
        var train = Train();
        var g = Tuning.Train.Geometry;
        var plan = EnginePlan.Of(g);
        var s = PlayerMotor.SpawnInCab(train, Tuning.Player, 0);
        double corridor = side * (g.Engine.BoilerHalfWidth + (g.RoofWidth / 2 - 0.1 - g.Engine.BoilerHalfWidth) / 2);
        var back = new Double3(corridor, 0, plan.CabBack - 0.6);
        Assert.True(Steer(ref s, train, back, 12), $"stuck at {s.Position} on the way back");
        var front = new Double3(0, 0, PlayerMotor.CabFloorZ(train.Frames[0].Shape));
        Assert.True(Steer(ref s, train, front, 12), $"stuck at {s.Position} on the way forward");
        Assert.Equal(g.Engine.DeckHeight, s.Position.Y, 3);
    }

    [Fact]
    public void TheWayRoundIsStraightWhenNothingsInTheWay()
    {
        var plan = EnginePlan.Of(Tuning.Train.Geometry);
        var a = new Double3(plan.Lane, 0, plan.CabFront + 1);
        var b = new Double3(plan.Lane, 0, plan.CabBack - 1);
        Assert.Equal(b, plan.CabWay(a, b));
        // Through the table, it's out into a way first.
        var mid = new Double3(0, 0, plan.CabFront + 2.5);
        var way = plan.CabWay(mid, new Double3(0, 0, plan.CabBack - 1));
        Assert.Equal(plan.Lane, Math.Abs(way.X), 6);
    }

    [Fact]
    public void TheChartTablesLampsShowWhosAboard()
    {
        // Lit standing in any of the train's vehicles, alive; dark on the ground, or dead.
        Assert.True(CrewCar.Aboard(true, 0, 7));
        Assert.True(CrewCar.Aboard(true, 6, 7));
        Assert.False(CrewCar.Aboard(true, PlayerState.World, 7));
        Assert.False(CrewCar.Aboard(false, 2, 7));
        Assert.False(CrewCar.Aboard(true, 7, 7));
    }

    [Fact]
    public void TheKettleSingsAsTheBoilerNearsItsRedline()
    {
        var b = Tuning.Boiler;
        var c = Tuning.Train.Geometry.Engine.CrewCar;
        Assert.Equal(0, CrewCar.KettleSong(b.WorkingBandMin, b, c));
        Assert.Equal(0, CrewCar.KettleSong(b.Redline - c.KettleFrom, b, c));
        Assert.InRange(CrewCar.KettleSong(b.Redline - c.KettleFrom / 2, b, c), 0.45, 0.55);
        Assert.Equal(1, CrewCar.KettleSong(b.Redline, b, c));
        Assert.Equal(1, CrewCar.KettleSong(b.PressureMax, b, c));
        // Working the train in its band (60 to 90) it's quiet most of the way.
        Assert.True(CrewCar.KettleSong(b.WorkingBandMax, b, c) < 0.6);
    }

    [Fact]
    public void TheCuckooCallsOnTheMinuteOverTheLastMinutesBeforeDawn()
    {
        var c = Tuning.Train.Geometry.Engine.CrewCar;
        double stir = Tuning.Enemies.Wakers.StirSeconds;
        Assert.Null(CrewCar.Cuckoo(stir + 30, stir, c));
        Assert.Null(CrewCar.Cuckoo(stir + 1, stir, c));
        int calls = 0;
        // Second by second down to dawn: out at each minute for its seconds, three times in three minutes.
        bool wasOut = false;
        for (double t = stir + 5; t > 0; t -= 0.25)
        {
            bool isOut = CrewCar.Cuckoo(t, stir, c) is not null;
            if (isOut && !wasOut)
                calls++;
            wasOut = isOut;
        }
        Assert.Equal((int)(stir / c.CuckooEvery), calls);
        Assert.Equal(0, CrewCar.Cuckoo(stir, stir, c));
        Assert.Equal(0, CrewCar.Cuckoo(c.CuckooEvery, stir, c));
        Assert.InRange(CrewCar.Cuckoo(c.CuckooEvery - c.CuckooOutSeconds / 2, stir, c)!.Value, 0.45, 0.55);
        Assert.Null(CrewCar.Cuckoo(c.CuckooEvery - c.CuckooOutSeconds - 0.1, stir, c));
        Assert.Null(CrewCar.Cuckoo(0, stir, c));
    }

    static void Walk(ref PlayerState s, TrainOnLine train, Double3 to, double seconds)
    {
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
            PlayerMotor.Step(ref s, Toward(s, to), train, Tuning.Player, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
    }

    static bool Steer(ref PlayerState s, TrainOnLine train, Double3 to, double seconds)
    {
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
        {
            var (step, there) = WarmUp.SteerCab(s, train, to, s.Yaw);
            if (there)
                return true;
            PlayerMotor.Step(ref s, step, train, Tuning.Player, Tuning.Train, SimConstants.TickSeconds);
        }
        return false;
    }

    static PlayerIntent Toward(in PlayerState s, Double3 to)
    {
        var d = (to - s.Position) with { Y = 0 };
        double len = d.Length;
        if (len < 0.05)
            return default;
        double y = s.Yaw;
        double z = (d.X * -Math.Sin(y) + d.Z * -Math.Cos(y)) / len, x = (d.X * Math.Cos(y) + d.Z * -Math.Sin(y)) / len;
        return new PlayerIntent { MoveX = (float)x, MoveZ = (float)z };
    }
}
