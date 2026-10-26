using Ballast;
using Ballast.Net;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Emotes for two, and more of them (GDD §9; the director, 14 Oct 2026, App. F.6; ARCHITECTURE §8 note 722): a high five or
/// fist bump offered and answered, the hands meeting on one tick both records carry; a dance-off's lead going round by the
/// tick; sitting on an edge and leaning out of a doorway, held in a moving car's frame till a move; a bot answering. Emotes
/// still move nothing.
/// </summary>
public class EmotesForTwoTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly EmoteTuning E = P.Emotes;
    const double Dt = SimConstants.TickSeconds;

    /// <summary>A night's crew stepped the way the host steps them (as RescueTests' Crew: HostSession.Step's order).</summary>
    sealed class Crew(World world)
    {
        public readonly World World = world;
        public readonly Dictionary<int, PlayerState> Players = [];
        public double? Speed;
        public TrainOnLine Train => World.Train;

        public void Run(double seconds, Func<int, int, PlayerIntent>? intent = null)
        {
            World.Movement = P;
            World.EmoteTuning = E;
            int ticks = (int)Math.Round(seconds * SimConstants.TickRate);
            for (int t = 0; t < ticks; t++)
            {
                if (Speed is { } v)
                    Train.Dynamics.Velocity = v;
                World.Mates = [.. Players.OrderBy(c => c.Key).Select(c => (c.Key, c.Value))];
                var crowd = Horseplay.Crowd(World.Mates);
                var ids = Players.Keys.Order().ToList();
                var intents = ids.ToDictionary(id => id, id => Rescue.CarriedIntent(Players[id], Horseplay.Staggered(Players[id], intent?.Invoke(id, t) ?? default)));
                World.BeginTick();
                foreach (var id in ids)
                {
                    var s = Players[id];
                    World.CrewAct(ref s, intents[id], id);
                    Players[id] = s;
                }
                World.Step(new TrainControls { Reverser = 1 });
                foreach (var id in ids)
                {
                    var s = Players[id];
                    PlayerMotor.Step(ref s, intents[id], Train, P, T, Dt, applyLook: false, id, crowd);
                    Players[id] = s;
                }
                World.StepRescue(id => Players.TryGetValue(id, out var s) ? s : null, (id, s) => Players[id] = s);
            }
        }

        public EmoteEvent? Of(int id) => World.Emotes.Where(e => e.By == id).Select(e => (EmoteEvent?)e).FirstOrDefault();
    }

    static Crew Night(double speed = 0, int cars = 4)
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(40_000)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, cars, 1)), line, 2_000);
        train.Dynamics.Velocity = speed;
        var world = new World(train, Tuning.Combat);
        world.EnableBodies();
        return new Crew(world) { Speed = speed };
    }

    /// <summary>Dave (1) and Priya (2) face to face on car 2's roof, <paramref name="apart"/> m apart down its middle.</summary>
    static Crew FaceToFace(double apart = 1.0, double speed = 0)
    {
        var n = Night(speed);
        n.Players[1] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P) with { Yaw = 0 };
        n.Players[2] = PlayerMotor.SpawnOnRoof(n.Train, 2, -apart, P) with { Yaw = Math.PI };
        return n;
    }

    static Func<int, int, PlayerIntent> Pick(int who, Emote e, int atTick = 0) => (id, t) => id == who && t == atTick ? new PlayerIntent { Emote = e } : default;

    /// <summary>The host's records applied to a client, as a snapshot is.</summary>
    static World Mirror(World host)
    {
        var client = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 1)), host.Train.Line, 2_000), Tuning.Combat);
        var controls = default(TrainControls);
        WorldRecords.Apply(WorldRecords.Capture(host, controls, []), client, ref controls, []);
        return client;
    }

    [Fact]
    public void EveryEmoteTheByteHoldsRidesTheWireWithTheWheelsStep()
    {
        // The wheel's eight, U1's three kept (9-11), and the byte's last (63): six bits, the step in the top two.
        foreach (int value in Enumerable.Range(0, 12).Append(63))
            foreach (sbyte cycle in new sbyte[] { -1, 0, 1 })
            {
                var w = new NetWriter();
                Messages.WriteInput(w, [new InputFrame(7, new PlayerIntent { Emote = (Emote)value, Cycle = cycle, Select = 2 })], 0);
                var r = new NetReader(w.Written);
                r.U8();
                var frames = new List<InputFrame>();
                Messages.ReadInput(ref r, frames, out _);
                Assert.Equal((Emote)value, frames[0].Intent.Emote);
                Assert.Equal(cycle, frames[0].Intent.Cycle);
                Assert.Equal(2, frames[0].Intent.Select);
            }
        Assert.Equal(95, Protocol.Version);
    }

    [Fact]
    public void AHighFiveOfferedAndAnsweredMeetsOnOneTickForBoth()
    {
        var n = FaceToFace();
        n.Run(1.0, Pick(1, Emote.HighFive));
        var offer = Assert.IsType<EmoteEvent>(n.Of(1));
        Assert.False(offer.Met);
        Assert.Null(n.Of(2));
        // Priya picks it too: the hands meet, on the tick she does, both records saying so and with whom.
        uint answered = n.World.Tick;
        n.Run(0.1, Pick(2, Emote.HighFive));
        var dave = n.Of(1)!.Value;
        var priya = n.Of(2)!.Value;
        Assert.Equal(answered, dave.Meet);
        Assert.Equal(dave.Meet, priya.Meet);
        Assert.Equal((2, 1), (dave.With, priya.With));
        Assert.Equal(offer.Id, dave.Id);
        // Every client sees the same meet.
        var seen = Mirror(n.World);
        Assert.Equal(n.World.Emotes.OrderBy(e => e.Id), seen.Emotes.OrderBy(e => e.Id));
        // And it lasts emotes.met from the meet, not the offer's length.
        n.Run(E.Met + 0.1);
        Assert.Empty(n.World.Emotes);
    }

    [Fact]
    public void AnUnansweredHighFiveHangsThereTillItRunsOut()
    {
        // Priya turned away: her pick is an offer of her own, not an answer.
        var n = FaceToFace();
        n.Players[2] = n.Players[2] with { Yaw = 0 };
        n.Run(0.5, Pick(1, Emote.HighFive));
        n.Run(0.1, Pick(2, Emote.HighFive));
        Assert.False(n.Of(1)!.Value.Met);
        Assert.False(n.Of(2)!.Value.Met);
        // Too far apart, the same.
        var far = FaceToFace(apart: E.Reach + 0.5);
        far.Run(0.5, Pick(1, Emote.HighFive));
        far.Run(0.1, Pick(2, Emote.HighFive));
        Assert.False(far.Of(1)!.Value.Met);
        // Left hanging, it's still up near its end, and gone after.
        n.Run(E.HighFive - 0.8);
        Assert.NotNull(n.Of(1));
        n.Run(0.4);
        Assert.Null(n.Of(1));
    }

    [Fact]
    public void AFistBumpIsTheSameShapeAndDoesntAnswerAHighFive()
    {
        var n = FaceToFace();
        n.Run(0.5, Pick(1, Emote.FistBump));
        n.Run(0.1, Pick(2, Emote.HighFive));
        Assert.False(n.Of(1)!.Value.Met);
        Assert.Equal(Emote.HighFive, n.Of(2)!.Value.Kind);
        // A fist bump answered (after the cooldown) meets.
        n.Run(E.Cooldown + 0.2, Pick(2, Emote.FistBump, (int)Math.Ceiling(E.Cooldown * SimConstants.TickRate) + 1));
        Assert.True(n.Of(1)!.Value.Met);
        Assert.Equal(n.Of(1)!.Value.Meet, n.Of(2)!.Value.Meet);
        Assert.Equal(Emote.FistBump, n.Of(2)!.Value.Kind);
    }

    [Fact]
    public void SittingOnARoofEdgeHoldsInAMovingCarsFrameAndJumpStandsYouUp()
    {
        var n = Night(speed: 15);
        double w = n.Train.Frames[2].Shape.HalfWidth;
        n.Players[1] = PlayerMotor.SpawnOnRoof(n.Train, 2, 2, P, w - 0.35) with { Yaw = -Math.PI / 2 };
        // Down the roof's middle there's no edge: not taken.
        n.Players[2] = PlayerMotor.SpawnOnRoof(n.Train, 2, -3, P, 0);
        Assert.Equal(EdgeKind.Roof, EmoteRules.SitAt(n.Players[1], n.Train, E)?.Kind);
        Assert.Null(EmoteRules.SitAt(n.Players[2], n.Train, E));
        n.Run(0.1, (id, t) => t == 0 ? new PlayerIntent { Emote = Emote.SitEdge } : default);
        Assert.Equal(Emote.SitEdge, n.Of(1)?.Kind);
        Assert.Null(n.Of(2));
        // Held for as long as they sit, in the car's frame at 15 m/s; looking about doesn't stand them.
        var sat = n.Players[1].Position;
        n.Run(4, (_, t) => new PlayerIntent { LookYaw = 0.01f });
        Assert.Equal(Emote.SitEdge, n.Of(1)?.Kind);
        Assert.Equal(2, n.Players[1].Parent);
        Assert.True((n.Players[1].Position - sat).Length < 1e-6, $"{(n.Players[1].Position - sat).Length} m");
        // Jump stands them up.
        n.Run(0.1, (id, t) => id == 1 && t == 0 ? new PlayerIntent { Buttons = PlayerButtons.Jump } : default);
        Assert.Null(n.Of(1));
        // And so does any move.
        n.Players[1] = PlayerMotor.SpawnOnRoof(n.Train, 2, 2, P, w - 0.35);
        n.Run(E.Cooldown + 0.1, Pick(1, Emote.SitEdge));
        Assert.NotNull(n.Of(1));
        n.Run(0.1, (id, _) => id == 1 ? new PlayerIntent { MoveX = -1 } : default);
        Assert.Null(n.Of(1));
    }

    /// <summary>Car c with side doors, its right-hand one open, and a crewmate just inside it facing out.</summary>
    static (int Car, PlayerState InDoor) AtTheDoor(Crew n, bool open = true)
    {
        int car = Enumerable.Range(1, n.Train.Frames.Count - 1).First(c => n.Train.Frames[c].Shape.DoorList.Count >= 4);
        if (open)
            n.Train.Vehicles[car].DoorsOpen |= 1 << 3;
        var shape = n.Train.Frames[car].Shape;
        var door = shape.DoorList.First(d => d.Index == 3).Box;
        var at = new Double3(shape.Interior!.Value.Max.X - 0.3, T.Geometry.Interior!.FloorHeight, (door.Min.Z + door.Max.Z) / 2);
        return (car, PlayerMotor.StandUp(n.Train, car, at, n.Train.Cars[car].FrontDistance, P) with { Yaw = -Math.PI / 2 });
    }

    [Fact]
    public void AnOpenSideDoorIsSatInAndLeantOutOfAndAShutOneIsnt()
    {
        var n = Night();
        var (car, inDoor) = AtTheDoor(n);
        Assert.Equal(new Edge(EdgeKind.Doorway, 1), EmoteRules.LeanAt(inDoor, n.Train, E));
        // Sat on: out over the sill and the landing outside it, to where it drops to the ballast.
        Assert.Equal(new Edge(EdgeKind.Sill, 1), EmoteRules.SitAt(inDoor, n.Train, E));
        var (edgeX, ended) = EmoteRules.EdgeX(inDoor, n.Train, 1, E.EdgeReach + E.SillReach);
        Assert.True(ended);
        Assert.True(edgeX > n.Train.Frames[car].Shape.HalfWidth, $"the edge at {edgeX:0.00}, the side at {n.Train.Frames[car].Shape.HalfWidth:0.00}");
        // The aisle's middle, no doorway.
        var aisle = inDoor with { Position = inDoor.Position with { X = 0 } };
        Assert.Null(EmoteRules.LeanAt(aisle, n.Train, E));
        Assert.Null(EmoteRules.SitAt(aisle, n.Train, E));
        // The door shut: neither.
        n.Train.Vehicles[car].DoorsOpen &= unchecked((byte)~(1 << 3));
        Assert.Null(EmoteRules.LeanAt(inDoor, n.Train, E));
        Assert.Null(EmoteRules.SitAt(inDoor, n.Train, E));
    }

    [Fact]
    public void TheCabsDoorwayIsLeantOutOfButNotItsSidesUnderTheWindows()
    {
        var n = Night();
        var engine = n.Train.Frames[0].Shape;
        var cab = engine.Cab!.Value;
        var steps = engine.Ladders.First(l => l.Foot.X > 0);
        var floor = cab.Min.Y + 0.1;
        var doorway = PlayerMotor.StandUp(n.Train, 0, new Double3(cab.Max.X - 0.35, floor, steps.Foot.Z), n.Train.Cars[0].FrontDistance, P);
        Assert.True(PlayerMotor.InCab(doorway, n.Train));
        Assert.Equal(new Edge(EdgeKind.Doorway, 1), EmoteRules.LeanAt(doorway, n.Train, E));
        // Forward along the side, by the waist-high wall under the windows: no.
        var wall = doorway with { Position = doorway.Position with { Z = cab.Min.Z + 1.2 } };
        if (PlayerMotor.InCab(wall, n.Train))
            Assert.Null(EmoteRules.LeanAt(wall, n.Train, E));
    }

    [Fact]
    public void LeantOutOfTheCabsDoorwayTheDriverStillBrakesAndStaysLeant()
    {
        // Note 977 (the director, 14 Oct playtest line 9: "another brake I could throw at the sides of the train so that I
        // could actually look at the side and see my position and then throw the brake when I feel like I'm sort of lined up
        // with a car"): leant out of the cab's doorway, looking back along the train at the car under the chute, the brake
        // key brakes, and holding it doesn't stand you up (only a move or a jump does).
        var n = Night(speed: 2);
        var engine = n.Train.Frames[0].Shape;
        var cab = engine.Cab!.Value;
        var steps = engine.Ladders.First(l => l.Foot.X > 0);
        n.Players[1] = PlayerMotor.StandUp(n.Train, 0, new Double3(cab.Max.X - 0.35, cab.Min.Y + 0.1, steps.Foot.Z), n.Train.Cars[0].FrontDistance, P) with { Yaw = -1.2 };
        n.Run(0.1, Pick(1, Emote.LeanOut));
        Assert.Equal(Emote.LeanOut, n.Of(1)?.Kind);
        var brake = new PlayerIntent { Buttons = PlayerButtons.Brake };
        Assert.False(EmoteRules.StandsUp(Emote.LeanOut, n.Players[1], brake, n.Train, E));
        n.Run(1.0, (id, _) => id == 1 ? brake : default);
        Assert.Equal(Emote.LeanOut, n.Of(1)?.Kind);
        Assert.True(CabControls.CanDrive(n.Players[1], n.Train));
        var controls = new TrainControls { Reverser = 1 };
        CabControls.Apply(ref controls, brake, n.Players[1], n.Train);
        Assert.Equal(1, controls.Brake);
        // A step back in stands them up, as any move does.
        Assert.True(EmoteRules.StandsUp(Emote.LeanOut, n.Players[1], new PlayerIntent { MoveZ = 1 }, n.Train, E));
    }

    [Fact]
    public void ALiftTakesASitterOffTheEdgeAndTheCarriedCantEmote()
    {
        var n = Night();
        double w = n.Train.Frames[2].Shape.HalfWidth;
        n.Players[1] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P, w - 0.35) with { Yaw = 0 };
        n.Players[2] = PlayerMotor.SpawnOnRoof(n.Train, 2, -0.8, P, w - 0.35) with { Yaw = Math.PI };
        n.Run(0.1, Pick(2, Emote.SitEdge));
        Assert.Equal(Emote.SitEdge, n.Of(2)?.Kind);
        // Dave lifts her (Use held on her): she's off the edge and over his shoulder.
        n.Run(P.Rescue.Carry.LiftSeconds + 0.2, (id, _) => id == 1 ? new PlayerIntent { Buttons = PlayerButtons.Use } : default);
        Assert.True(n.Players[2].Has(PlayerFlags.Carried));
        Assert.Null(n.Of(2));
        // Carried, she can't emote at all.
        n.Run(0.1, Pick(2, Emote.Wave));
        Assert.Null(n.Of(2));
        Assert.False(EmoteRules.CanEmote(n.Players[2]));
    }

    [Fact]
    public void AShoveStillTakesASitterOffTheirEdge()
    {
        // A sitter is as shoveable as anyone stood (note 717), and off their footing the sit's over.
        var n = Night();
        double w = n.Train.Frames[2].Shape.HalfWidth;
        var sitter = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P, w - 0.35);
        Assert.True(Horseplay.Shoveable(sitter));
        Assert.True(EmoteRules.StandsUp(Emote.SitEdge, sitter with { Flags = PlayerFlags.Shoved }, default, n.Train, E));
        Assert.True(EmoteRules.StandsUp(Emote.SitEdge, sitter with { Surface = Surface.Air }, default, n.Train, E));
        Assert.False(EmoteRules.StandsUp(Emote.SitEdge, sitter, new PlayerIntent { LookYaw = 0.3f }, n.Train, E));
    }

    [Fact]
    public void ADanceOffsLeadGoesRoundTheDancersByTheTick()
    {
        // Three dancing in a ring on car 2's roof, each facing its middle.
        var n = Night();
        for (int i = 0; i < 3; i++)
        {
            double a = i * Math.Tau / 3;
            double x = 0.5 * Math.Sin(a), z = 0.9 * Math.Cos(a);
            n.Players[i + 1] = PlayerMotor.SpawnOnRoof(n.Train, 2, z, P, x) with { Yaw = Math.Atan2(x, z) };
        }
        n.Run(0.1, (_, t) => t == 0 ? new PlayerIntent { Emote = Emote.Dance } : default);
        var crew = n.Players.OrderBy(p => p.Key).Select(p => (p.Key, p.Value)).ToList();
        long beat = (long)Math.Round(E.DanceOffBeat * SimConstants.TickRate);
        var leaders = new List<int>();
        for (long tick = n.World.Tick; tick < n.World.Tick + 3 * beat; tick += beat)
        {
            var offs = Enumerable.Range(1, 3).Select(id => EmoteRules.DanceOff(id, n.World.Emotes, crew, n.Train, E, tick)).ToList();
            Assert.All(offs, o => Assert.Equal(3, o?.Count));
            // Everyone in it sees the same lead.
            Assert.Single(offs.Select(o => o!.Value.Leader).Distinct());
            leaders.Add(offs[0]!.Value.Leader);
        }
        Assert.Equal([1, 2, 3], leaders.Order());
        // A dancer on their own isn't in one.
        n.Players[3] = n.Players[3] with { Position = n.Players[3].Position with { Z = -8 } };
        crew = [.. n.Players.OrderBy(p => p.Key).Select(p => (p.Key, p.Value))];
        Assert.Null(EmoteRules.DanceOff(3, n.World.Emotes, crew, n.Train, E, n.World.Tick));
        Assert.Equal(2, EmoteRules.DanceOff(1, n.World.Emotes, crew, n.Train, E, n.World.Tick)?.Count);
    }

    [Fact]
    public void ABotAnswersAHighFiveHeldUpToItAndNeverOffers()
    {
        // Dave holds one up to the bot (2), who's turned half away.
        var n = FaceToFace();
        n.Players[2] = n.Players[2] with { Yaw = Math.PI - 0.6 };
        n.Run(0.1, Pick(1, Emote.HighFive));
        var crew = n.Players.OrderBy(p => p.Key).Select(p => (p.Key, p.Value)).ToList();
        // At once it stands and turns to him, but waits its beat.
        var first = Heed.HighFive(new PlayerIntent { MoveZ = 1 }, n.Players[2], n.World, 2, crew);
        Assert.Equal(0, first.MoveZ);
        Assert.True(first.LookYaw > 0, $"turns {first.LookYaw}");
        Assert.Equal(Emote.None, first.Emote);
        // Faced and the beat gone by, it answers; the host meets them.
        n.Players[2] = n.Players[2] with { Yaw = Math.PI };
        n.Run(E.BotAnswer);
        crew = [.. n.Players.OrderBy(p => p.Key).Select(p => (p.Key, p.Value))];
        var answer = Heed.HighFive(default, n.Players[2], n.World, 2, crew);
        Assert.Equal(Emote.HighFive, answer.Emote);
        n.Run(0.1, (id, t) => id == 2 && t == 0 ? answer : default);
        Assert.True(n.Of(1)!.Value.Met);
        // Nothing held up to it: nothing.
        var none = Night();
        none.Players[2] = PlayerMotor.SpawnOnRoof(none.Train, 2, 0, P);
        Assert.Equal(default, Heed.HighFive(default, none.Players[2], none.World, 2, [(2, none.Players[2])]));
    }

    [Fact]
    public void EmotesForTwoMoveNothing()
    {
        // The same few seconds with and without a high five met, a fist bump offered and a sit: everyone ends the same.
        Dictionary<int, PlayerState> Run(bool emote)
        {
            var n = FaceToFace(speed: 10);
            n.Run(2, (id, t) => !emote ? default : (id, t) switch
            {
                (1, 0) => new PlayerIntent { Emote = Emote.HighFive },
                (2, 10) => new PlayerIntent { Emote = Emote.HighFive },
                (1, 40) => new PlayerIntent { Emote = Emote.FistBump },
                (2, 50) => new PlayerIntent { Emote = Emote.SitEdge },
                _ => default,
            });
            return n.Players;
        }
        Assert.Equal(Run(false), Run(true));
    }
}
