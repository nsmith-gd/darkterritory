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

    /// <summary>Walking towards a point on the footplate (engine frame), as a keyboard does; nothing once there.</summary>
    static PlayerIntent Walk(in PlayerState s, Double3 to)
    {
        var d = (to - s.Position) with { Y = 0 };
        double len = d.Length;
        if (len < 0.08)
            return default;
        double y = s.Yaw;
        double z = (d.X * -Math.Sin(y) + d.Z * -Math.Cos(y)) / len, x = (d.X * Math.Cos(y) + d.Z * -Math.Sin(y)) / len;
        return new PlayerIntent { MoveX = (float)(x * Math.Min(1, len)), MoveZ = (float)(z * Math.Min(1, len)) };
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
        // Each of the two, at waist height, and the fireman between them: each is within an arm's length. (Cab
        // forward, note 268, the coal's ahead of the fire door and to its left: along the way from one to the other.)
        var toFire = (firebox.Position - coal.Position) with { Y = 0 };
        toFire = toFire * (1 / toFire.Length);
        // (Their reaches overlap: the hand over each, a little towards the other.)
        var shovel = coal.Position + toFire * 0.1 + new Double3(0, 0.9, 0);
        var fire = firebox.Position - toFire * 0.1 + new Double3(0, 0.9, 0);
        s.Position = ((shovel + fire) * 0.5) with { Y = s.Position.Y };
        Assert.True(((shovel - s.Position) with { Y = 0 }).Length < P.Hand.Arm);
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
        var (world, s, _, _) = Footplate();
        // In front of the bunker, in the coal's reach and out of the fire door's.
        var coal = world.Train.Frames[0].Shape.Interactables.First(i => i.Kind == InteractableKind.Coal).Position;
        s.Position = coal + new Double3(0.4, 0, -0.5);
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
        // A fire with room in it, so every shovelful goes in. First across the footplate to between the coal and the fire
        // door (cab forward, note 268: they're at the fireman's end, the crew comes in at the driver's).
        var between = ((coal + firebox) * 0.5) with { Y = 0 };
        for (int i = 0; i < SimConstants.TickRate * 4; i++)
            Step(Walk(client.Predicted, between with { Y = client.Predicted.Position.Y }));
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

    [Fact]
    public void TheRestOfTheCrewSeeAHeadsetsHands()
    {
        // T47: the host sends each player's hands with the rest of them, so everyone else can draw their arms.
        var net = new LoopbackNetwork();
        TrainOnLine Train() => new(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), new RailLine(new LineDefinition("t", [new TrackSegment(50_000)])), 1_000);
        var host = new HostSession(net.CreateHost(), Train(), Tuning.Train, P);
        var headset = new ClientSession(net.CreateClient(), Train(), Tuning.Train, P);
        var watcher = new ClientSession(net.CreateClient(), Train(), Tuning.Train, P);
        var reach = new PlayerIntent();
        reach.Reach(new Double3(0.35, 2.05, -0.45), new Double3(-0.2, 1.1, -0.4));
        for (int i = 0; i < 40; i++)
        {
            net.Advance(SimConstants.TickSeconds);
            host.Step();
            headset.Step(i > 20 ? reach : default);
            watcher.Step(default);
        }
        Assert.True(watcher.TryGetRemote(headset.PlayerId!.Value, 1, out var seen));
        Assert.Equal(new Double3(0.35, 2.05, -0.45), seen.Hand);
        Assert.Equal(new Double3(-0.2, 1.1, -0.4), seen.OtherHand);
        // Put the controllers down, and the arms come down with them.
        for (int i = 0; i < 20; i++)
        {
            net.Advance(SimConstants.TickSeconds);
            host.Step();
            headset.Step(default);
            watcher.Step(default);
        }
        Assert.True(watcher.TryGetRemote(headset.PlayerId!.Value, 1, out seen));
        Assert.Equal(default, seen.Hand);
    }

    [Fact]
    public void TheRestOfTheCrewSeeAHeadsetsHeadHeight()
    {
        // T82: the head's height rides with the hands, so another client can lean and crouch the body under it. A keyboard
        // player's is zero, and their record is the size it was.
        var net = new LoopbackNetwork();
        TrainOnLine Train() => new(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), new RailLine(new LineDefinition("t", [new TrackSegment(50_000)])), 1_000);
        var host = new HostSession(net.CreateHost(), Train(), Tuning.Train, P);
        var headset = new ClientSession(net.CreateClient(), Train(), Tuning.Train, P);
        var watcher = new ClientSession(net.CreateClient(), Train(), Tuning.Train, P);
        var crouched = new PlayerIntent();
        crouched.Reach(new Double3(0.3, 0.9, -0.4), head: 1.123);
        for (int i = 0; i < 40; i++)
        {
            net.Advance(SimConstants.TickSeconds);
            host.Step();
            headset.Step(i > 20 ? crouched : default);
            watcher.Step(default);
        }
        Assert.True(watcher.TryGetRemote(headset.PlayerId!.Value, 1, out var seen));
        Assert.Equal(1.12, seen.Head, 6);
        Assert.Equal(1.12, headset.Predicted.Head, 6);
        // A head below a crouch is held at kneeling height; none without a hand to carry it.
        var s = new PlayerState();
        var low = new PlayerIntent();
        low.Reach(new Double3(0.3, 0.5, -0.4), head: 0.1);
        PlayerMotor.TakeHand(ref s, low, H);
        Assert.Equal(PlayerMotor.MinHead, s.Head);
        PlayerMotor.TakeHand(ref s, default, H);
        Assert.Equal(0, s.Head);
    }
}
