using Ballast;
using Ballast.Net;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>A VR player's reaching hand (T29): reach is tested from where the hand is, through the same intent path.</summary>
public class HandTests
{
    static readonly PlayerTuning P = Tuning.Player;
    static readonly HandTuning H = Tuning.Player.Hand;

    static World Standing(int cars = 6)
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(50_000)]));
        var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, cars, 1)), line, 1_000, Tuning.Boiler), Tuning.Combat) { Hand = H };
        world.EnableBodies();
        return world;
    }

    /// <summary>An intent with the hand at <paramref name="hand"/> in the player's parent frame (as a headset reports it).</summary>
    static PlayerIntent Reaching(in PlayerState s, Double3 hand, PlayerButtons buttons = PlayerButtons.Use)
    {
        var d = hand - s.Position;
        double c = Math.Cos(-s.Yaw), n = Math.Sin(-s.Yaw);
        var intent = new PlayerIntent { Buttons = buttons };
        intent.Reach(new Double3(d.X * c + d.Z * n, d.Y, -d.X * n + d.Z * c));
        return intent;
    }

    static void Hold(World world, ref PlayerState s, Func<PlayerState, PlayerIntent> intent, double seconds)
    {
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
            world.CrewAct(ref s, intent(s), 1);
    }

    [Fact]
    public void TheHandCrossesTheWireOnItsCentimetreGrid()
    {
        var reaching = new PlayerIntent { Buttons = PlayerButtons.Use, MoveZ = 0.5f };
        reaching.Reach(new Double3(0.123, 1.4567, -0.5));
        Assert.Equal(0.12f, reaching.HandX);
        Assert.Equal(1.46f, reaching.HandY);
        var w = new NetWriter();
        Messages.WriteInput(w, [new InputFrame(7, reaching)], 3);
        int withHand = w.Length;
        var r = new NetReader(w.Written);
        Assert.Equal((byte)MessageType.Input, r.U8());
        var read = new List<InputFrame>();
        Messages.ReadInput(ref r, read, out _);
        Assert.Equal(reaching, read[0].Intent);
        // A keyboard's intent carries nothing more than it did; a hand is three centimetre shorts and a byte for the other.
        Messages.WriteInput(w, [new InputFrame(7, reaching with { Buttons = PlayerButtons.Use })], 3);
        Assert.Equal(7, withHand - w.Length);
        // Both hands (T43): three more.
        var both = new PlayerIntent { Buttons = PlayerButtons.Use };
        both.Reach(new Double3(0.2, 1.1, -0.4), new Double3(-0.2, 1.1, -0.4));
        Messages.WriteInput(w, [new InputFrame(7, both)], 3);
        Assert.Equal(13, w.Length - (withHand - 7));
        r = new NetReader(w.Written);
        r.U8();
        read.Clear();
        Messages.ReadInput(ref r, read, out _);
        Assert.Equal(both, read[0].Intent);
        Assert.Equal(-0.2f, read[0].Intent.OtherX);
    }

    [Fact]
    public void AHandIsHeldToAnArmsLength()
    {
        var world = Standing();
        var s = PlayerMotor.SpawnOnRoof(world.Train, 2, 0, P);
        var far = new PlayerIntent();
        far.Reach(new Double3(0, 1.4, -3));
        world.CrewAct(ref s, far, 1);
        Assert.Equal(new Double3(0, 1.4, -H.Arm), s.Hand);
        var high = new PlayerIntent();
        high.Reach(new Double3(0.3, 9, 0));
        world.CrewAct(ref s, high, 1);
        Assert.Equal(new Double3(0.3, H.Overhead, 0), s.Hand);
        // Nonsense, or no hand at all, is no hand: that player reaches from the body.
        world.CrewAct(ref s, new PlayerIntent { Buttons = PlayerButtons.Hand, HandX = float.NaN }, 1);
        Assert.Null(PlayerMotor.HandAt(s));
        world.CrewAct(ref s, default, 1);
        Assert.Null(PlayerMotor.HandAt(s));
        // Nor is there one in a world that isn't taking hands.
        world.Hand = null;
        world.CrewAct(ref s, far, 1);
        Assert.Equal(default, s.Hand);
    }

    static (World World, PlayerState Fireman, Double3 Coal, Double3 Firebox) Footplate()
    {
        var world = Standing();
        var train = world.Train;
        var shape = train.Frames[0].Shape;
        var s = PlayerMotor.SpawnInCab(train, P);
        var firebox = shape.Interactables.First(i => i.Kind == InteractableKind.Firebox);
        var coal = shape.Interactables.First(i => i.Kind == InteractableKind.Coal);
        // The near edges of the two, at waist height, and the fireman between them: each is within an arm's length.
        var shovel = coal.Position + new Double3(0, 0.9, -coal.Radius + 0.1);
        var fire = firebox.Position + new Double3(0, 0.9, firebox.Radius - 0.1);
        s.Position = s.Position with { Z = (shovel.Z + fire.Z) / 2 };
        Assert.True(Math.Abs(shovel.Z - s.Position.Z) < P.Hand.Arm);
        train.Boiler.Firebox = 0;
        return (world, s, shovel, fire);
    }

    [Fact]
    public void TheShovelFillsAtTheTenderAndEmptiesIntoTheFirebox()
    {
        var (world, s, coal, firebox) = Footplate();
        double tender = world.Train.Boiler.Tender;
        // Gripping at the firebox with nothing on the shovel puts nothing in.
        Hold(world, ref s, x => Reaching(x, firebox), 3);
        Assert.Equal(tender, world.Train.Boiler.Tender);
        Hold(world, ref s, _ => default, 0.1);

        // A steady swing: scoop, carry, in. One shovelful a stroke.
        for (int stroke = 0; stroke < 4; stroke++)
        {
            Hold(world, ref s, x => Reaching(x, coal), 0.8);
            Assert.True(s.Has(PlayerFlags.Shovelful));
            Hold(world, ref s, x => Reaching(x, firebox), 0.6);
            Assert.False(s.Has(PlayerFlags.Shovelful));
        }
        Assert.Equal(tender - 4, world.Train.Boiler.Tender, 6);
        Assert.Equal(4, world.Train.Boiler.Firebox, 6);
    }

    [Fact]
    public void AFranticSwingIsNoFasterThanTheKeyboard()
    {
        var (world, s, coal, firebox) = Footplate();
        double tender = world.Train.Boiler.Tender;
        // Strokes of 0.4 s for 6 s: the keyboard's 1.2 s a shovelful (spec B.6) is still the most there is.
        for (int i = 0; i < 15; i++)
        {
            Hold(world, ref s, x => Reaching(x, coal), 0.2);
            Hold(world, ref s, x => Reaching(x, firebox), 0.2);
        }
        Assert.InRange(tender - world.Train.Boiler.Tender, 3, 6.0 / Tuning.Boiler.ShovelSeconds);
    }

    [Fact]
    public void LetGoOfTheShovelAndTheCoalIsSpilled()
    {
        var (world, s, coal, firebox) = Footplate();
        double tender = world.Train.Boiler.Tender;
        Hold(world, ref s, x => Reaching(x, coal), 0.8);
        Hold(world, ref s, x => Reaching(x, firebox, PlayerButtons.None), 0.3);
        Assert.False(s.Has(PlayerFlags.Shovelful));
        Hold(world, ref s, x => Reaching(x, firebox), 2);
        Assert.Equal(tender, world.Train.Boiler.Tender);
    }

    [Fact]
    public void AKeyboardNeverFindsTheCoalFace()
    {
        var (world, s, coal, _) = Footplate();
        s.Position = s.Position with { Z = coal.Z - 0.1 };
        Assert.Null(CrewActions.Nearest(s, world.Train, H));
        Assert.Equal(InteractableKind.Coal, CrewActions.Nearest(s with { Hand = new Double3(0, 1, 0) }, world.Train, H));
    }

    [Fact]
    public void AHandOnTheLadderTakesHoldOfIt()
    {
        var world = Standing();
        var train = world.Train;
        // The cab steps, from the ballast beside them.
        var ladder = train.Frames[0].Shape.Ladders[0];
        var foot = train.Frames[0].ToWorld(ladder.Foot);
        var s = PlayerMotor.SpawnOnGround(foot + ladder.Inward * -0.5, train.Line, train.Cars[0].FrontDistance, P);
        var rung = train.Frames[0].ToWorld(ladder.Foot + Double3.Up * 1.1);

        // Use with the hand at the man's side, not on it: nothing (the keyboard needs a push towards it).
        var beside = s;
        world.CrewAct(ref beside, Reaching(beside, beside.Position + new Double3(0, 1, 0)), 1);
        PlayerMotor.Step(ref beside, Reaching(s, s.Position + new Double3(0, 1, 0)), train, P, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
        Assert.NotEqual(Surface.Ladder, beside.Surface);

        var intent = Reaching(s, rung);
        world.CrewAct(ref s, intent, 1);
        PlayerMotor.Step(ref s, intent, train, P, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
        Assert.Equal(Surface.Ladder, s.Surface);
        Assert.Equal(0, s.Parent);
    }

    [Fact]
    public void AHandReachesTheBrakeWheelFromTheEndLadder()
    {
        var world = Standing();
        var train = world.Train;
        // Car 2's end ladder, near its top: the wheel on the roof beside it is a hand's reach away, not the feet's.
        var shape = train.Frames[2].Shape;
        var ladder = shape.Ladders.First(l => l.Foot.Z > shape.HalfLength);
        var wheel = shape.Interactables.First(i => i.Kind == InteractableKind.Handbrake).Position;
        // Cut off from the engine, whose own brake is the driver's.
        train.Uncouple(1);
        var s = new PlayerState { Parent = 2, Position = ladder.Foot with { Y = ladder.Top - 0.6 }, Surface = Surface.Ladder, Health = P.Health };
        Assert.Null(CrewActions.Nearest(s, train, H));
        var hand = wheel + new Double3(0, 0.4, 0.2);
        Assert.Equal(InteractableKind.Handbrake, CrewActions.Nearest(s with { Hand = hand - s.Position }, train, H));
        // Cut at a stand, they're parked with the brakes wound on; a hand on the wheel winds them off.
        Assert.True(train.RakeOf(2).Handbrake);
        Hold(world, ref s, x => Reaching(x, hand), Tuning.Train.Couplings.HandbrakeSeconds + 0.2);
        Assert.False(train.RakeOf(2).Handbrake);
    }

    [Fact]
    public void AHandTakesTheLampItsOn()
    {
        var world = Standing();
        var train = world.Train;
        var s = PlayerMotor.SpawnOnRoof(train, 2, 0, P);
        var roof = s.Position.Y;
        // One lamp at the feet ahead (the body's reach takes that), one out to the side where the hand is.
        var ahead = world.Bodies.SpawnCrate(train, 2, new Double3(0, roof, -0.6), BodyKind.Lamp);
        var side = world.Bodies.SpawnCrate(train, 2, new Double3(0.9, roof, 0.3), BodyKind.Lamp);
        Assert.Same(ahead, world.Bodies.InReach(s, train));
        var reaching = s with { Hand = side.Centre - s.Position + new Double3(0, 0.1, 0) };
        Assert.Same(side, world.Bodies.InReach(reaching, train, H));
        world.CrewAct(ref s, Reaching(s, side.Centre + new Double3(0, 0.1, 0)), 1);
        Assert.Equal(1, side.Carrier);
    }

    [Fact]
    public void ASwitchWantsTheHandOnItsLever()
    {
        var line = new RailLine(new LineDefinition("switch", [new TrackSegment(4000)]),
            [new BranchDefinition(BranchKind.DeadLine, 1000, +1,
                [new TrackSegment(Tuning.Route.Junctions.DivergeLength, -Tuning.Route.Junctions.DivergeRadius),
                 new TrackSegment(Tuning.Route.Junctions.DivergeLength, Tuning.Route.Junctions.DivergeRadius), new TrackSegment(500)])]);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 1)), line, 700);
        var stands = new SwitchStands(Tuning.Route.Junctions);
        var lever = stands.LeverAt(line, 0);
        var s = PlayerMotor.SpawnOnGround(lever with { X = lever.X + 0.8 } - Double3.Up * 0.9, line, 1000, P);
        // Standing at it is enough for the body; a hand has to be on it.
        Assert.Equal(0, stands.InReach(s, train));
        Assert.Null(stands.InReach(s with { Hand = new Double3(0.3, 1.2, 0) }, train, H));
        Assert.Equal(0, stands.InReach(s with { Hand = lever - s.Position + new Double3(0.1, 0, 0) }, train, H));
    }

    [Fact]
    public void AClientShovellingByHandPredictsWhatTheHostDoes()
    {
        // Over loopback, the hand's intent reaches the host on the same grid the client predicted with: no corrections.
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(50_000)]));
        TrainOnLine Train() => new(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 1)), line, 1_000, Tuning.Boiler);
        var net = new LoopbackNetwork();
        var host = new HostSession(net.CreateHost(), Train(), Tuning.Train, P);
        var client = new ClientSession(net.CreateClient(), Train(), Tuning.Train, P);
        void Step(PlayerIntent intent)
        {
            net.Advance(SimConstants.TickSeconds);
            host.Step();
            client.Step(intent);
        }
        for (int i = 0; i < 20; i++)
            Step(default);
        var shape = host.Train.Frames[0].Shape;
        var coal = shape.Interactables.First(i => i.Kind == InteractableKind.Coal).Position + new Double3(0, 0.6, 0.1);
        var firebox = shape.Interactables.First(i => i.Kind == InteractableKind.Firebox).Position + new Double3(0, 0.7, 0.1);
        // A fire with room in it, so every shovelful goes in.
        host.Train.Boiler.Firebox = 0;
        double tender = host.Train.Boiler.Tender;
        for (int i = 0; i < SimConstants.TickRate * 8; i++)
        {
            var s = client.Predicted;
            bool scooping = i % 45 < 25;
            Step(PlayerMotor.InCab(s, client.Train) ? Reaching(s, scooping ? coal : firebox) : default);
        }
        Assert.True(PlayerMotor.InCab(client.Predicted, client.Train));
        Assert.True(host.Train.Boiler.Tender < tender - 3, $"shovelled {tender - host.Train.Boiler.Tender}");
        Assert.Equal(0, client.Corrections);
    }
}
