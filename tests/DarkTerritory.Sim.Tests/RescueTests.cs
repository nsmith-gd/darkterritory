using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Bringing them home (GDD §17, §23.5; ARCHITECTURE §8 note 723; the friendslop audit's "nobody gets left", the director, 14
/// Oct 2026): the hand-up onto a moving car, carrying a living friend (put down, wriggled out of, thrown), and a bandage on
/// someone else.
/// </summary>
public class RescueTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly RescueTuning R = P.Rescue;
    static readonly Stops.LootTuning L = DataFile.Load<Stops.LootTuning>(Path.Combine(DataFile.FindContentRoot(), Stops.LootTuning.File));
    const double Dt = SimConstants.TickSeconds;

    /// <summary>
    /// A night's crew stepped the way the host steps them (HostSession.Step): the crew at the tick's start, the stagger's and the
    /// shoulder's intents stripped, the acts, the world, the knocks, everyone's feet, and then the rescues and the carried.
    /// </summary>
    sealed class Crew(World world)
    {
        public readonly World World = world;
        public readonly Dictionary<int, PlayerState> Players = [];
        public double? Speed;
        public TrainOnLine Train => World.Train;
        /// <summary>Called each tick before anyone acts (a runner kept beside a door, say).</summary>
        public Action<Crew>? Before;

        public void Run(double seconds, Func<int, PlayerIntent>? intent = null) => Ticks((int)Math.Round(seconds * SimConstants.TickRate), intent);

        public void Ticks(int ticks, Func<int, PlayerIntent>? intent = null)
        {
            World.Movement = P;
            for (int i = 0; i < ticks; i++)
            {
                if (Speed is { } v)
                    Train.Dynamics.Velocity = v;
                Before?.Invoke(this);
                World.Mates = [.. Players.OrderBy(c => c.Key).Select(c => (c.Key, c.Value))];
                var crowd = Horseplay.Crowd(World.Mates);
                var ids = Players.Keys.Order().ToList();
                var intents = ids.ToDictionary(id => id, id => Rescue.CarriedIntent(Players[id], Horseplay.Staggered(Players[id], intent?.Invoke(id) ?? default)));
                World.BeginTick();
                foreach (var id in ids)
                {
                    var s = Players[id];
                    World.CrewAct(ref s, intents[id], id);
                    Players[id] = s;
                }
                World.Step(new TrainControls { Reverser = 1 });
                World.ApplyDamage(id => Players.TryGetValue(id, out var s) ? s : null, (id, s) => Players[id] = s, ids);
                foreach (var id in ids)
                {
                    var s = Players[id];
                    PlayerMotor.Step(ref s, intents[id], Train, P, T, Dt, applyLook: false, id, crowd);
                    Players[id] = s;
                }
                World.StepRescue(id => Players.TryGetValue(id, out var s) ? s : null, (id, s) => Players[id] = s);
                World.StepBodies([.. ids.Select(id => (id, Players[id]))]);
            }
        }

        public Double3 At(int id) => PlayerMotor.WorldPosition(Players[id], Train);
    }

    static Crew Night(int cars, double speed)
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(40_000)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, cars, 1)), line, 2_000);
        train.Dynamics.Velocity = speed;
        var world = new World(train, Tuning.Combat);
        world.EnableBodies();
        world.Names[1] = "Dave";
        world.Names[2] = "Priya";
        return new Crew(world) { Speed = speed };
    }

    static readonly PlayerIntent Use = new() { Buttons = PlayerButtons.Use };

    // ---- The hand-up.

    /// <summary>A car of the train with sliding doors in its sides (a cargo car's), its right-hand one open.</summary>
    static int SideDoorCar(Crew n, bool open = true)
    {
        int car = Enumerable.Range(1, n.Train.Frames.Count - 1).First(c => n.Train.Frames[c].Shape.DoorList.Count >= 4);
        if (open)
            n.Train.Vehicles[car].DoorsOpen |= 1 << 3;
        return car;
    }

    /// <summary>
    /// Dave in car <paramref name="car"/>'s open right-hand door (just inside it, facing out and looking down), and Priya on the
    /// ballast beside it, level with the door.
    /// </summary>
    static (PlayerState Helper, PlayerState Runner) AtTheDoor(Crew n, int car)
    {
        var frame = n.Train.Frames[car];
        double w = frame.Shape.HalfWidth, floor = T.Geometry.Interior!.FloorHeight;
        var helper = PlayerMotor.StandUp(n.Train, car, new Double3(w - 0.3, floor, 0), n.Train.Cars[car].FrontDistance, P) with { Yaw = -Math.PI / 2, Pitch = -0.85 };
        var runner = Beside(n, car);
        return (helper, runner);
    }

    static PlayerState Beside(Crew n, int car)
    {
        var frame = n.Train.Frames[car];
        var spot = frame.ToWorld(new Double3(frame.Shape.HalfWidth + T.Geometry.Interior!.StepWidth + 0.35, 0, 0.2));
        double hint = n.Train.Cars[car].FrontDistance;
        return PlayerMotor.SpawnOnGround(spot, n.Train.Line, hint, P) with { Yaw = frame.Heading };
    }

    [Fact]
    public void ARunnerBesideAMovingCarIsHauledAboardIntoTheCarsFrame()
    {
        // The train at a runner's speed, Priya running beside car c's open door, Dave in it holding Use on her.
        var n = Night(4, P.Run);
        int car = SideDoorCar(n);
        (n.Players[1], n.Players[2]) = AtTheDoor(n, car);
        Assert.Equal(2, Rescue.HandUpTarget(n.Players[1], 1, [(2, n.Players[2])], n.Train, R)?.Id);
        n.Run(R.HandUp.Seconds - 3 * Dt, id => id == 1 ? Use : new PlayerIntent { MoveZ = 1, Buttons = PlayerButtons.Run });
        Assert.Equal(PlayerState.World, n.Players[2].Parent);
        n.Run(0.2, id => id == 1 ? Use : new PlayerIntent { MoveZ = 1, Buttons = PlayerButtons.Run });
        var up = n.Players[2];
        Assert.Equal(car, up.Parent);
        Assert.Equal(Surface.Deck, up.Surface);
        Assert.True(n.Train.Frames[car].Shape.Interior!.Value.Contains(up.Position), $"at {up.Position}");
        // In the car's frame, it carries her: a second on, she's still in it, where she was put, whole.
        var put = up.Position;
        n.Run(1);
        Assert.Equal(car, n.Players[2].Parent);
        Assert.True((n.Players[2].Position - put).Length < 0.6, $"{(n.Players[2].Position - put).Length:0.00} m on");
        Assert.Equal(P.Health, n.Players[2].Health);
        // The report says who came back for her.
        var line = Assert.Single(n.World.Attribution.Log, i => i.Kind == IncidentKind.Rescue);
        Assert.Equal((2, 1), (line.Victim, line.Actor));
        Assert.Equal($"Hauled up by Dave at {P.Run * 3.6:0} km/h.", line.Action.Replace("{actor}", "Dave"));
    }

    /// <summary>Priya kept beside the door each tick, standing (her speed over the ground nothing), the train at <paramref name="speed"/>.</summary>
    static Crew Standing(double speed, bool open = true)
    {
        var n = Night(4, speed);
        int car = SideDoorCar(n, open);
        (n.Players[1], n.Players[2]) = AtTheDoor(n, car);
        n.Before = c =>
        {
            if (c.Players[2].Parent == PlayerState.World)
                c.Players[2] = Beside(c, car);
        };
        return n;
    }

    [Fact]
    public void OverTheSpeedLimitThereIsNoHandUp()
    {
        // Within maxRelativeSpeed of the car (a runner's sprint and a little), hauled; past it, not however long it's held.
        var under = Standing(R.HandUp.MaxRelativeSpeed - 1);
        under.Run(R.HandUp.Seconds + 0.2, id => id == 1 ? Use : default);
        Assert.NotEqual(PlayerState.World, under.Players[2].Parent);
        var over = Standing(R.HandUp.MaxRelativeSpeed + 1);
        over.Run(R.HandUp.Seconds * 3, id => id == 1 ? Use : default);
        Assert.Equal(PlayerState.World, over.Players[2].Parent);
        Assert.Empty(over.World.Attribution.Log);
    }

    [Fact]
    public void NoHandUpThroughAShutDoorOrAtAStand()
    {
        // (Use held on at a shut door works the door, as ever: once it's open, she's in reach.)
        var shut = Standing(4, open: false);
        Assert.Null(Rescue.HandUpTarget(shut.Players[1], 1, [(2, shut.Players[2])], shut.Train, R));
        shut.Train.Vehicles[SideDoorCar(shut, open: false)].DoorsOpen |= 1 << 3;
        Assert.Equal(2, Rescue.HandUpTarget(shut.Players[1], 1, [(2, shut.Players[2])], shut.Train, R)?.Id);
        // At a stand the steps are there to climb: Use at the door is the door's.
        var stood = Standing(0);
        Assert.Null(Rescue.HandUpTarget(stood.Players[1], 1, [(2, stood.Players[2])], stood.Train, R));
    }

    [Fact]
    public void ABotOnTheCarHaulsUpACrewmatePlayingButNotABot()
    {
        // Note 723: the bot's own intent (Heed.HandUp), from its look round: turned to her, Use held, she's up.
        var calls = new CrewCalls();
        calls.Bot(1);
        var n = Standing(5);
        n.World.BotIds.Add(1);
        n.Players[1] = n.Players[1] with { Yaw = 0, Pitch = 0 };
        n.Run(R.HandUp.Seconds + 0.5, id => id == 1 ? Heed.HandUp(default, n.Players[1], n.World, 1, [(2, n.Players[2])], calls, R) : default);
        Assert.NotEqual(PlayerState.World, n.Players[2].Parent);
        // Another bot running beside it is left to the steps.
        calls.Bot(2);
        var m = Standing(5);
        Assert.Equal(default, Heed.HandUp(default, m.Players[1], m.World, 1, [(2, m.Players[2])], calls, R));
    }

    // ---- Carrying a friend.

    /// <summary>Dave and Priya face to face on car 2's roof, a step apart down its middle.</summary>
    static Crew OnTheRoof(double speed)
    {
        var n = Night(4, speed);
        n.Players[1] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P) with { Yaw = 0 };
        n.Players[2] = PlayerMotor.SpawnOnRoof(n.Train, 2, -0.8, P) with { Yaw = Math.PI };
        return n;
    }

    static void Lift(Crew n)
    {
        n.Run(R.Carry.LiftSeconds + 0.1, id => id == 1 ? Use : default);
        n.Run(0.1);
    }

    [Fact]
    public void ACarriedFriendMovesWithTheCarrierInTheCarsFrame()
    {
        var n = OnTheRoof(15);
        n.Run(R.Carry.LiftSeconds * 0.5, id => id == 1 ? Use : default);
        Assert.False(n.Players[2].Has(PlayerFlags.Carried));
        n.Run(R.Carry.LiftSeconds * 0.5 + 0.1, id => id == 1 ? Use : default);
        Assert.True(n.Players[2].Has(PlayerFlags.Carried));
        Assert.True(n.Players[1].Has(PlayerFlags.Carrying));
        Assert.True(n.Players[1].Has(PlayerFlags.Heavy));
        Assert.Equal(1, n.World.CarrierOf(2));
        // Walked along the roof at 15 m/s: at the carrier's pace (carryHeavy, or the roof's safe walk under it), and she's on
        // the shoulder, in the car's frame.
        var from = n.Players[1].Position;
        n.Run(2, id => id == 1 ? new PlayerIntent { MoveZ = 1, Buttons = PlayerButtons.Run } : default);
        var carrier = n.Players[1];
        Assert.Equal((2, Surface.Roof), (carrier.Parent, carrier.Surface));
        double walked = (carrier.Position - from).Length, pace = Math.Min(P.CarryHeavy, P.RoofRun);
        Assert.InRange(walked, pace * 2 * 0.8, pace * 2 * 1.05);
        var her = n.Players[2];
        Assert.Equal(2, her.Parent);
        var shoulder = Rescue.Shoulder(carrier, R);
        // (A tick behind at most: the carried go where the carrier went this tick.)
        Assert.True((her.Position - shoulder).Length < P.CarryHeavy * Dt + 1e-6, $"{(her.Position - shoulder).Length:0.000} m off the shoulder");
        Assert.Equal(P.Health, her.Health);
        // She can't walk, work or drive anything; she can look.
        double yaw = her.Yaw;
        n.Run(0.5, id => id == 2 ? new PlayerIntent { MoveZ = 1, LookYaw = 0.02f, Buttons = PlayerButtons.Use } : default);
        Assert.True(n.Players[2].Has(PlayerFlags.Carried));
        Assert.True(n.Players[2].Yaw > yaw + 0.2);
    }

    [Fact]
    public void JumpWrigglesFreeToTheCarriersFeetAndUsePutsThemDown()
    {
        var n = OnTheRoof(10);
        Lift(n);
        Assert.True(n.Players[2].Has(PlayerFlags.Carried));
        n.Ticks(1, id => id == 2 ? new PlayerIntent { Buttons = PlayerButtons.Jump } : default);
        var her = n.Players[2];
        Assert.False(her.Has(PlayerFlags.Carried));
        Assert.False(n.Players[1].Has(PlayerFlags.Carrying));
        Assert.Null(n.World.CarrierOf(2));
        n.Run(1);
        her = n.Players[2];
        Assert.Equal((2, Surface.Roof), (her.Parent, her.Surface));
        Assert.True((her.Position - n.Players[1].Position).Length < 1.0);
        Assert.Equal(P.Health, her.Health);
        // Lifted again (stood in front of him once more), and put down with a press of Use (a fresh press: the lift's own hold
        // doesn't put her down).
        n.Players[2] = n.Players[2] with { Position = n.Players[1].Position + new Double3(0, 0, -0.8) };
        n.Ticks(1);
        Lift(n);
        Assert.True(n.Players[2].Has(PlayerFlags.Carried));
        n.Ticks(1, id => id == 1 ? Use : default);
        Assert.False(n.Players[2].Has(PlayerFlags.Carried));
        // And the hold that put her down doesn't lift her again.
        n.Run(R.Carry.LiftSeconds * 2, id => id == 1 ? Use : default);
        Assert.False(n.Players[2].Has(PlayerFlags.Carried));
        Assert.Equal((2, Surface.Roof), (n.Players[2].Parent, n.Players[2].Surface));
    }

    [Fact]
    public void BotsCanBeCarriedButNeverCarry()
    {
        var n = OnTheRoof(0);
        n.World.BotIds.Add(1);
        Lift(n);
        Assert.False(n.Players[2].Has(PlayerFlags.Carried));
        n.World.BotIds.Clear();
        n.World.BotIds.Add(2);
        Lift(n);
        Assert.True(n.Players[2].Has(PlayerFlags.Carried));
    }

    [Fact]
    public void AShoveDropsWhoeverTheyCarry()
    {
        // "A carried player is safe from nothing": knocked off their feet, the carrier drops her.
        var n = OnTheRoof(0);
        n.Players[3] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0.8, P) with { Yaw = 0 };
        Lift(n);
        Assert.True(n.Players[2].Has(PlayerFlags.Carried));
        n.Ticks(1, id => id == 3 ? new PlayerIntent { Moves = PlayerMoves.Shove } : default);
        Assert.False(n.Players[2].Has(PlayerFlags.Carried));
        Assert.Null(n.World.CarrierOf(2));
    }

    /// <summary>Dave carrying Priya at car 2's roof edge, facing out over its side.</summary>
    static Crew CarryingAtTheEdge(double speed)
    {
        var n = Night(4, speed);
        double edge = n.Train.Frames[2].Shape.HalfWidth - P.Edge.Lip - 0.2;
        n.Players[1] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P, edge - 0.8) with { Yaw = 0 };
        n.Players[2] = PlayerMotor.SpawnOnRoof(n.Train, 2, -0.8, P, edge - 0.8) with { Yaw = Math.PI };
        Lift(n);
        Assert.True(n.Players[2].Has(PlayerFlags.Carried));
        n.Players[1] = n.Players[1] with { Yaw = -Math.PI / 2, Pitch = 0.2 };
        return n;
    }

    static readonly PlayerIntent Throw = new() { Buttons = PlayerButtons.Throw };

    [Fact]
    public void AFriendThrownOffARoofAtAStandFallsByTheHeight()
    {
        // A car's roof and a shoulder's height is under note 573's fall knock: whole on the ballast.
        var n = CarryingAtTheEdge(0);
        n.Ticks(1, id => id == 1 ? Throw : default);
        Assert.False(n.Players[2].Has(PlayerFlags.Carried));
        Assert.Equal(Surface.Air, n.Players[2].Surface);
        Assert.True(n.Players[2].Has(PlayerFlags.Shoved));
        Assert.Equal(1, n.World.ShovedBy(2)?.By);
        n.Run(3);
        var her = n.Players[2];
        Assert.True(her.Alive);
        Assert.Equal((PlayerState.World, Surface.Ground), (her.Parent, her.Surface));
        Assert.Equal(P.Health, her.Health);
    }

    [Fact]
    public void AFriendThrownOffAtSpeedIsKilledByTheLandingAndTheReportNamesTheThrower()
    {
        var n = CarryingAtTheEdge(18);
        n.Ticks(1, id => id == 1 ? Throw : default);
        n.Run(3);
        var her = n.Players[2];
        Assert.False(her.Alive);
        Assert.Equal(DeathCause.Shoved, her.Death);
        var line = IncidentLog.Death(n.World, 2, her, null, n.Players.Select(c => (c.Key, c.Value)));
        Assert.Equal(1, line.Actor);
        Assert.Equal("Thrown off by Dave at 65 km/h.", line.Action.Replace("{actor}", "Dave"));
    }

    [Fact]
    public void WithCrewMischiefOffThrowPutsThemDown()
    {
        var n = CarryingAtTheEdge(18);
        n.World.CrewMischief = false;
        n.Ticks(1, id => id == 1 ? Throw : default);
        n.Run(1);
        var her = n.Players[2];
        Assert.False(her.Has(PlayerFlags.Carried));
        Assert.True(her.Alive);
        Assert.Equal((2, Surface.Roof), (her.Parent, her.Surface));
    }

    // ---- A bandage on a friend.

    static Crew Patient(int health)
    {
        var n = Night(3, 0);
        var spot = n.Train.Frames[1].ToWorld(new Double3(n.Train.Frames[1].Shape.HalfWidth + 4, 0, 0));
        double hint = n.Train.Cars[1].FrontDistance;
        n.Players[1] = PlayerMotor.SpawnOnGround(spot, n.Train.Line, hint, P) with { Yaw = 0 };
        n.Players[2] = PlayerMotor.SpawnOnGround(spot + new Double3(0, 0, -0.9), n.Train.Line, hint, P) with { Health = health };
        n.World.Bodies.Heals = b => b.Kind == BodyKind.Loot ? 30 : 0;
        n.World.Bodies.FullHealth = P.Health;
        n.World.Bodies.OnAFriend = L.Healing!.OnAFriend;
        var find = n.World.Bodies.SpawnLoot(spot + Double3.Up, hint, 0, 0.1);
        find.Carrier = 1;
        find.Claimed = true;
        return n;
    }

    [Fact]
    public void ABandageOnAFriendHealsHalfAgainAsMuch()
    {
        Assert.Equal(1.5, L.Healing!.OnAFriend);
        var n = Patient(40);
        n.Run(n.World.Bodies.HealSeconds + 0.2, id => id == 1 ? Use : default);
        Assert.Equal(40 + (int)Math.Round(30 * 1.5), n.Players[2].Health);
        Assert.Equal(P.Health, n.Players[1].Health);
        Assert.DoesNotContain(n.World.Bodies.All, b => b.Kind == BodyKind.Loot);
        // Capped at their full health.
        var nearly = Patient(P.Health - 10);
        nearly.Run(nearly.World.Bodies.HealSeconds + 0.2, id => id == 1 ? Use : default);
        Assert.Equal(P.Health, nearly.Players[2].Health);
    }

    [Fact]
    public void ABandageUsedOnYourselfHealsWhatItAlwaysDid()
    {
        var n = Patient(P.Health);
        n.Players[1] = n.Players[1] with { Health = 40, Yaw = Math.PI };
        n.Run(n.World.Bodies.HealSeconds + 0.2, id => id == 1 ? Use : default);
        Assert.Equal(70, n.Players[1].Health);
    }
}
