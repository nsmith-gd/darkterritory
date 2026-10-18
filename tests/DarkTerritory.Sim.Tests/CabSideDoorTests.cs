using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The side doors at the big cab's front (ARCHITECTURE §8 note 856; the director, 16 Oct 2026: "I do think we want a door like
/// next to or closer to where we have the, the actual coal ... and I guess on the other side of the front of the cab as
/// well"): one in the left side beside the coal, one in the right across from it; each solid shut, walked through open both
/// ways, worked from inside or from its landing, up the cab steps from the ballast. The rest of the cab as it was.
/// </summary>
public class CabSideDoorTests
{
    static TrainOnLine Train() =>
        new(new TrainDynamics(Consist.Uniform(Tuning.Train, 3, 1)), new RailLine(new LineDefinition("t", [new TrackSegment(2000)])), 500);

    static CabSideDoor DoorOn(int side) => EnginePlan.Of(Tuning.Train.Geometry).SideDoors.Single(d => d.Side == side);

    [Fact]
    public void ThereIsOneBesideTheCoalAndOneAcrossTheCabAtItsFront()
    {
        var g = Tuning.Train.Geometry;
        var plan = EnginePlan.Of(g);
        var shape = CarShape.Build(g, VehicleKind.Engine, hasCarBehind: true);
        Assert.Equal(2, plan.SideDoors.Count);
        var coal = DoorOn(-1);
        var coalFace = shape.Interactables.Single(i => i.Kind == InteractableKind.Coal).Position;
        var hatch = shape.Ladders.Single(l => l.Foot.Y > 0.2);
        // The coal's side, just behind the bunker and the hatch ladder up to the gun (each left where it was), the coal two
        // steps from its middle.
        Assert.True(coal.Z0 >= plan.Bunker.Max.Z && coal.Z0 > hatch.Foot.Z + 0.1, $"the coal door from {coal.Z0:0.00} (bunker to {plan.Bunker.Max.Z:0.00}, ladder at {hatch.Foot.Z:0.00})");
        Assert.True((new Double3(-shape.HalfWidth, 0, coal.Middle) - coalFace with { Y = 0 }).Length < 2.0);
        // The other in the right side, in the cab's front half, clear of the points lever and of the gallery's tread.
        var front = DoorOn(1);
        var points = shape.Interactables.Single(i => i.Kind == InteractableKind.Points).Position;
        Assert.True(front.Z0 >= points.Z + 0.5 && front.Z1 <= plan.FrontTread.Min.Z + 1e-6, $"the front door {front.Z0:0.00} to {front.Z1:0.00}");
        Assert.True(front.Z1 < (plan.CabFront + plan.CabBack) / 2);
        foreach (var d in plan.SideDoors)
        {
            Assert.Equal(g.Doorway.Width, d.Z1 - d.Z0, 6);
            var door = shape.DoorList.Single(x => x.Index == d.Index);
            Assert.Equal(d.Side, Math.Sign(door.Box.Centre.X));
            Assert.Equal(g.Engine.DeckHeight + g.Doorway.Height, door.Box.Max.Y, 6);
            Assert.Contains(shape.Interactables, i => i.Kind == InteractableKind.Door && i.Index == d.Index);
            // No wall across it: the side's waist stops at each jamb.
            Assert.DoesNotContain(shape.Solids, s => s.Part == PartKind.CabWall && s.Box.Min.Y < g.Engine.DeckHeight + 1
                && s.Box.ContainsXZ(new Double3(d.Side * (shape.HalfWidth - 0.05), 0, d.Middle)));
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void ShutItKeepsYouInOpenYouWalkOutOntoItsLandingAndBackIn(int side)
    {
        var train = Train();
        var shape = train.Frames[0].Shape;
        var d = DoorOn(side);
        double w = shape.HalfWidth, deck = Tuning.Train.Geometry.Engine.DeckHeight;
        var landing = new Double3(side * (w + Tuning.Train.Geometry.Engine.RunningBoardWidth / 2), 0, d.Middle);
        var inside = new Double3(side * 0.9, 0, d.Middle);
        var s = PlayerMotor.SpawnInCab(train, Tuning.Player, side * 0.3);
        Walk(ref s, train, new Double3(side * 0.9, 0, PlayerMotor.CabFloorZ(shape)), 2);
        Walk(ref s, train, inside, 4);
        Assert.True((s.Position - inside with { Y = s.Position.Y }).Length < 0.2, $"not at the door: {s.Position}");

        // Shut: the door's the wall.
        Walk(ref s, train, landing, 2);
        Assert.True(PlayerMotor.InCab(s, train), $"through the shut door to {s.Position}");
        Assert.True(Math.Abs(s.Position.X) < w - 0.1, $"in the shut door at {s.Position}");

        // Opened from inside, as anyone works a door (Use held at it, looking at it), and out onto the landing.
        Use(ref s, train, side);
        Assert.True(train.Vehicles[0].DoorOpen(d.Index));
        Walk(ref s, train, landing, 2);
        Assert.False(PlayerMotor.InCab(s, train));
        Assert.Equal((0, Surface.Deck), (s.Parent, s.Surface));
        Assert.Equal(deck, s.Position.Y, 3);
        Assert.True(Math.Abs(s.Position.X - landing.X) < 0.2, $"at {s.Position}");

        // Shut from the landing, and it keeps them out; open again, and back in.
        Use(ref s, train, -side);
        Assert.False(train.Vehicles[0].DoorOpen(d.Index));
        Walk(ref s, train, inside, 2);
        Assert.False(PlayerMotor.InCab(s, train), $"in through the shut door to {s.Position}");
        Use(ref s, train, -side);
        Walk(ref s, train, inside, 2);
        Assert.True(PlayerMotor.InCab(s, train), $"not back in: {s.Position}");
        Assert.Equal(deck, s.Position.Y, 3);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void UpTheCabStepsFromTheBallastOntoItsLandingNotThroughTheShutDoor(int side)
    {
        var train = Train();
        var frame = train.Frames[0];
        var d = DoorOn(side);
        var steps = frame.Shape.Ladders.Single(l => l.Foot.Y < 0.2 && Math.Sign(l.Foot.X) == side && Math.Abs(l.Foot.Z - d.Middle) < 0.05);
        Assert.Equal(-side, Math.Sign(steps.Inward.X));
        var at = frame.ToWorld(steps.Foot - steps.Inward * 0.25);
        var s = PlayerMotor.SpawnOnGround(at, train.Line, train.Dynamics.Distance - frame.Shape.HalfLength, Tuning.Player);
        s.Yaw = frame.Heading + side * Math.PI / 2;
        var up = new PlayerIntent { MoveZ = 1 };
        for (int i = 0; i < 6 * SimConstants.TickRate && !(s.Parent == 0 && s.Surface == Surface.Deck); i++)
            PlayerMotor.Step(ref s, up, train, Tuning.Player, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
        Assert.Equal((0, Surface.Deck), (s.Parent, s.Surface));
        Assert.False(PlayerMotor.InCab(s, train));
        Assert.True(Math.Abs(s.Position.X) > frame.Shape.HalfWidth, $"up into the shut door at {s.Position}");
        Assert.InRange(s.Position.Z, d.Z0, d.Z1);
        // And from there, opened and in.
        Use(ref s, train, -side);
        Walk(ref s, train, new Double3(side * 0.9, 0, d.Middle), 2);
        Assert.True(PlayerMotor.InCab(s, train), $"not in: {s.Position}");
    }

    [Fact]
    public void WithTheBbqDeckFittedItsBoardIsTheLanding()
    {
        // Note 698's boards run on forward along the cab's sides to the deck: the landings are under them, one board, not two.
        var g = Tuning.Train.Geometry;
        var shape = CarShape.WithBbqDeck(CarShape.Build(g, VehicleKind.Engine, hasCarBehind: true), g, Tuning.Train.Composition.Bbq);
        foreach (var d in EnginePlan.Of(g).SideDoors)
        {
            var p = new Double3(d.Side * (shape.HalfWidth + 0.3), 0, d.Middle);
            Assert.Single(shape.Solids, s => s.Part == PartKind.RunningBoard && s.Box.ContainsXZ(p));
            Assert.Equal(g.Engine.DeckHeight, shape.TopAt(p.X, p.Z, g.Engine.DeckHeight + 0.5)!.Value.Top, 6);
        }
    }

    [Fact]
    public void TheOldFiveMetreCabHasOnlyTheDoorThatFits()
    {
        var g = Tuning.Train.Geometry;
        var old = EnginePlan.Of(g with { Engine = g.Engine with { CabLength = 5 } });
        Assert.All(old.SideDoors, d => Assert.True(d.Z1 <= old.DoorFront - 0.5));
        Assert.DoesNotContain(old.SideDoors, d => d.Side > 0);
    }

    /// <summary>Use held facing the cab's <paramref name="facing"/> side till the door's worked (on the train's next tick: a door moves once a tick).</summary>
    static void Use(ref PlayerState s, TrainOnLine train, int facing)
    {
        train.Step(SimConstants.TickSeconds, default);
        s.Yaw = facing * -Math.PI / 2;
        s.Pitch = 0;
        var use = new PlayerIntent { Buttons = PlayerButtons.Use };
        double seconds = (Tuning.Train.Geometry.Interior?.DoorSeconds ?? 0.4) + 0.2;
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
            CrewActions.Apply(ref s, use, train, SimConstants.TickSeconds);
        CrewActions.Apply(ref s, default, train, SimConstants.TickSeconds);
    }

    static void Walk(ref PlayerState s, TrainOnLine train, Double3 to, double seconds)
    {
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
            PlayerMotor.Step(ref s, Toward(s, to), train, Tuning.Player, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
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
