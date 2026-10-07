using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// A car breached (decided 1 Oct, checklist state-breach; spec B.9): its shell given way to the outside, by the Car Hugger
/// chewing through its end wall or Climbers forcing their way in through its roof. Until it's boarded up it shuts nobody in:
/// not the cold, not the night's sound, not the Choir.
/// </summary>
public class BreachTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly EnemyTuning E = Tuning.Enemies;
    static readonly PlayerIntent Use = new() { Buttons = PlayerButtons.Use };

    /// <summary>A host-side night with a quiet director: only what a test puts there happens.</summary>
    sealed class Rig
    {
        public readonly World World;
        public readonly Dictionary<int, PlayerState> Crew = new();
        public double Speed;

        public Rig(int cars = 5, double speed = 10, TrainTuning? train = null)
        {
            Speed = speed;
            var line = new RailLine(new LineDefinition("t", [new TrackSegment(80_000)]));
            var t = new TrainOnLine(new TrainDynamics(Consist.Uniform(train ?? T, cars, 1)), line, 2_000);
            t.Dynamics.Velocity = speed;
            World = new World(t, Tuning.Combat);
            World.EnableEnemies(E with { Director = E.Director with { GraceMinSeconds = 1e9, GraceMaxSeconds = 1e9 } }, null, 1, crew: 2, authority: true);
        }

        public TrainOnLine Train => World.Train;

        /// <summary>On a car's floor, in the aisle, this far along it.</summary>
        public PlayerState Inside(int car, double z) => new()
        {
            Parent = car,
            Position = new Double3(T.Geometry.Interior!.DoorX, T.Geometry.Interior.FloorHeight, z),
            Surface = Surface.Deck,
            Health = P.Health,
            Yaw = Math.PI, // facing +Z, the car's rear end
            LineHint = Train.Cars[car].FrontDistance,
        };

        public void Run(double seconds, Func<int, PlayerIntent>? intent = null)
        {
            for (int i = 0; i < seconds * SimConstants.TickRate; i++)
            {
                Train.Dynamics.Velocity = Speed;
                World.BeginTick();
                var intents = Crew.Keys.ToDictionary(id => id, id => intent?.Invoke(id) ?? default);
                foreach (var id in Crew.Keys.ToList())
                {
                    var s = Crew[id];
                    World.CrewAct(ref s, intents[id], id);
                    Crew[id] = s;
                }
                World.Step(new TrainControls { Reverser = 1 });
                World.ApplyDamage(id => Crew.TryGetValue(id, out var s) ? s : null, (id, s) => Crew[id] = s, Crew.Keys);
                foreach (var id in Crew.Keys.ToList())
                {
                    var s = Crew[id];
                    PlayerMotor.Step(ref s, intents[id], Train, P, T, SimConstants.TickSeconds, applyLook: false);
                    Crew[id] = s;
                }
            }
        }
    }

    [Fact]
    public void ABreachedCarShutsNobodyIn()
    {
        var r = new Rig();
        var s = r.Inside(2, 0);
        Assert.Equal(2, PlayerMotor.Space(s, r.Train));
        Assert.True(PlayerMotor.NearHeat(s, r.Train));
        Assert.True(r.Train.Vehicles[2].Breach(Breaches.EndWall(r.Train.Frames[2].Shape)!.Value));
        // Every door shut, and it's the outside: the cold, the sound, voices and the Choir come in through the hole.
        Assert.Equal(0, r.Train.Vehicles[2].DoorsOpen);
        Assert.Equal(PlayerMotor.Outside, PlayerMotor.Space(s, r.Train));
        Assert.False(PlayerMotor.NearHeat(s, r.Train));
        // Out of the wind's full force still (spec B.2's ¼ rate, as with a door open): it's still a car around you.
        Assert.True(PlayerMotor.Indoors(s, r.Train));
        // Breached already, it's the hole there is.
        Assert.False(r.Train.Vehicles[2].Breach(Double3.Zero));
        Assert.Equal(Breaches.EndWall(r.Train.Frames[2].Shape)!.Value, r.Train.Vehicles[2].BreachAt);
        r.Train.Vehicles[2].Breached = false;
        Assert.Equal(2, PlayerMotor.Space(s, r.Train));
    }

    [Fact]
    public void ThroughABreachVoicesArentMuffled()
    {
        // Spec A.5: shut in a car, a crewmate out on its roof is heard through the walls; breached, there are no walls to it.
        var r = new Rig();
        var inside = r.Inside(2, 0);
        var roof = PlayerMotor.SpawnOnRoof(r.Train, 2, 2, P);
        Assert.True(VoiceRouting.Route(roof, inside, radio: false, r.Train).HasFlag(VoicePath.Occluded));
        r.Train.Vehicles[2].Breach(Breaches.Roof(r.Train.Frames[2].Shape)!.Value);
        var path = VoiceRouting.Route(roof, inside, radio: false, r.Train);
        Assert.True(path.HasFlag(VoicePath.Proximity));
        Assert.False(path.HasFlag(VoicePath.Occluded));
    }

    [Fact]
    public void BoardedUpFromInsideByHoldingUseAtTheHole()
    {
        var r = new Rig(speed: 0);
        const int car = 2;
        var room = r.Train.Frames[car].Shape.Interior!.Value;
        r.Train.Vehicles[car].Breach(Breaches.EndWall(r.Train.Frames[car].Shape)!.Value);
        double board = T.Breach.BoardSeconds;
        // Too far down the car: nothing.
        r.Crew[1] = r.Inside(car, room.Max.Z - 4);
        Assert.Null(Breaches.Within(r.Crew[1], r.Train));
        r.Run(board + 1, _ => Use);
        Assert.True(r.Train.Vehicles[car].Breached);
        // At the hole: most of the way, then let go, and that board's started over.
        r.Crew[1] = r.Inside(car, room.Max.Z - 0.7);
        Assert.Equal(car, Breaches.Within(r.Crew[1], r.Train));
        r.Run(board - 1, _ => Use);
        Assert.True(r.Train.Vehicles[car].Breached);
        r.Run(0.1);
        Assert.Equal(0, r.Crew[1].ActionProgress);
        r.Run(board - 0.5, _ => Use);
        Assert.True(r.Train.Vehicles[car].Breached);
        r.Run(0.6, _ => Use);
        Assert.False(r.Train.Vehicles[car].Breached);
        Assert.Equal(car, PlayerMotor.Space(r.Crew[1], r.Train));
        // Still holding on after: the end door beside the hole isn't opened by the same hold.
        r.Run(1, _ => Use);
        Assert.Equal(0, r.Train.Vehicles[car].DoorsOpen);
    }

    [Fact]
    public void FromTheRoofOverItTheHoleIsntBoardedUp()
    {
        var r = new Rig(speed: 0);
        const int car = 3;
        var roof = Breaches.Roof(r.Train.Frames[car].Shape)!.Value;
        r.Train.Vehicles[car].Breach(roof);
        r.Crew[1] = PlayerMotor.SpawnOnRoof(r.Train, car, roof.Z, P);
        Assert.Null(Breaches.Within(r.Crew[1], r.Train));
        r.Run(T.Breach.BoardSeconds + 1, _ => Use);
        Assert.True(r.Train.Vehicles[car].Breached);
        // From the floor under it, it is.
        r.Crew[1] = r.Inside(car, roof.Z);
        r.Run(T.Breach.BoardSeconds + 0.2, _ => Use);
        Assert.False(r.Train.Vehicles[car].Breached);
    }

    [Fact]
    public void WithTheKitFlagOnlyTheRepairKitCarriedBoardsItUp()
    {
        // needsKit: only someone carrying the repair kit (note 150: an item) boards it up; a wrench in hand isn't it.
        var kit = T with { Breach = T.Breach with { NeedsKit = true } };
        var r = new Rig(speed: 0, train: kit);
        const int car = 2;
        var room = r.Train.Frames[car].Shape.Interior!.Value;
        r.Train.Vehicles[car].Breach(Breaches.EndWall(r.Train.Frames[car].Shape)!.Value);
        r.Crew[1] = r.Inside(car, room.Max.Z - 0.7);
        r.Run(kit.Breach.BoardSeconds + 1, _ => Use);
        Assert.True(r.Train.Vehicles[car].Breached);
        r.Crew[1] = r.Crew[1] with { Kit = Kit.Of([Tool.Wrench]), HeldSlot = 0 };
        r.Run(kit.Breach.BoardSeconds + 1, _ => Use);
        Assert.True(r.Train.Vehicles[car].Breached);
        // The kit in their hands (the host sets PlayerFlags.RepairKit from what's carried).
        r.World.Bodies.SpawnCrate(r.Train, car, new Double3(0.4, room.Min.Y + 0.1, room.Centre.Z), Physics.BodyKind.RepairKit).Carrier = 1;
        r.Run(kit.Breach.BoardSeconds + 0.2, _ => Use);
        Assert.False(r.Train.Vehicles[car].Breached);
    }

    [Fact]
    public void TheCarHuggerChewsThroughTheEndWallAndAgainOnceItsBoarded()
    {
        var r = new Rig(cars: 4, speed: 8);
        int rear = r.Train.Dynamics.Consist.Vehicles[^1].Id;
        var hugger = r.World.AddEnemy(id => CarHugger.Lurking(id, r.Train.Dynamics.RearDistance + 1, 1, E.CarHugger));
        r.Run(0.5);
        Assert.True(hugger.Latched);
        double through = E.CarHugger.BreachEaten / E.CarHugger.ShellPerSecond;
        r.Run(through - 1.5);
        Assert.False(r.Train.Vehicles[rear].Breached);
        r.Run(1.5);
        Assert.True(r.Train.Vehicles[rear].Breached);
        Assert.Equal(Breaches.EndWall(r.Train.Frames[rear].Shape)!.Value, r.Train.Vehicles[rear].BreachAt);
        // Past the guard van's platform and into the end wall, as the art eats it (Look.BiteTuning's platform share).
        var v = r.Train.Vehicles[rear];
        Assert.True(v.Eaten / (v.Eaten + v.Integrity) > 0.08);
        // Boarded up while it's still at it: through again, the next breachEaten on.
        v.Breached = false;
        r.Run(through - 1.5);
        Assert.False(v.Breached);
        r.Run(2);
        Assert.True(v.Breached);
    }

    [Fact]
    public void ClimbersForcingTheirWayIntoAShutUnlitCarBreachItsRoof()
    {
        var r = new Rig(speed: 12);
        const int car = 3;
        r.Train.Vehicles[car].LampLit = false;
        var c = r.World.AddEnemy(id => Climber.Pacing(id, r.Train, car, 1, E.Climbers));
        r.Run(E.Climbers.PaceSeconds + E.Climbers.ScrabbleSeconds + 20);
        Assert.True(c.Inside);
        Assert.Equal(car, c.Attached);
        Assert.True(r.Train.Vehicles[car].Breached);
        // In through the hatch (torn off), on a cargo car with one.
        var shape = r.Train.Frames[car].Shape;
        Assert.NotNull(shape.Hatch);
        Assert.Equal(shape.Hatch!.Value.Centre.Z, r.Train.Vehicles[car].BreachAt.Z, 6);
        Assert.Equal(shape.Interior!.Value.Max.Y, r.Train.Vehicles[car].BreachAt.Y, 6);
    }

    [Theory]
    [InlineData(true, false)] // lit (and empty): in, but the decision's breach is the unlit car's (climbers.breachLitCars)
    [InlineData(false, true)] // a door left open: in that way, nothing forced
    public void ClimbersInThroughNoForcingLeaveItWhole(bool lit, bool doorOpen)
    {
        var r = new Rig(speed: 12);
        const int car = 3;
        r.Train.Vehicles[car].LampLit = lit;
        if (doorOpen)
            r.Train.Vehicles[car].ToggleDoor(0);
        var c = r.World.AddEnemy(id => Climber.Pacing(id, r.Train, car, 1, E.Climbers));
        r.Run(E.Climbers.PaceSeconds + E.Climbers.ScrabbleSeconds + 20);
        Assert.True(c.Inside);
        Assert.Equal(car, c.Attached);
        Assert.False(r.Train.Vehicles[car].Breached);
    }

    [Fact]
    public void BehindABreachedCarsShutDoorsTheChoirStillTakesYou()
    {
        // The decision's Choir rule: "a breached car no longer counts as 'behind a closed door' for the Choir until it's
        // boarded up". Shut in a whole car, nobody's taken (EnemyTests); the same car breached, they are.
        foreach (bool breached in new[] { false, true })
        {
            var n = new Night(6, speed: 10);
            n.Crew[1] = new PlayerState { Parent = 3, Position = new Double3(-0.45, T.Geometry.Interior!.FloorHeight, 0), Surface = Surface.Deck, Health = P.Health };
            if (breached)
                n.Train.Vehicles[3].Breach(Breaches.EndWall(n.Train.Frames[3].Shape)!.Value);
            n.World.Choir = new Combat.ChoirState { Build = 0.9999, Loudness = Tuning.Combat.Choir.MaxLoudness };
            n.Run(E.Choir.SeizeSeconds + 15);
            Assert.Equal(breached ? DeathCause.Seized : DeathCause.None, n.Crew[1].Death);
        }
    }

    [Fact]
    public void AWalkerWarmingUpInABreachedCarBoardsItUpFirst()
    {
        // A car only warms you shut, and a breached one isn't, however its doors are: the walker boards the hole up, then
        // it's warm in there (else a bot-crewed night freezes in a car the Car Hugger's been at).
        var line = RailLine.Load(Path.Combine(DataFile.FindContentRoot(), "lines/test-loop.json"));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 1)), line, 1500);
        var world = new World(train);
        const int car = 2;
        train.Vehicles[car].Breach(Breaches.EndWall(train.Frames[car].Shape)!.Value);
        var bot = new RoofWalkerBot(7, P.Cold);
        var s = new PlayerState
        {
            Parent = car,
            Position = new Double3(T.Geometry.Interior!.DoorX, T.Geometry.Interior.FloorHeight, -2),
            Surface = Surface.Deck,
            Health = P.Health,
            Cold = P.Cold.OnsetSeconds * 0.8,
            LineHint = train.Cars[car].FrontDistance,
        };
        for (uint tick = 0; tick < SimConstants.TickRate * 40 && train.Vehicles[car].Breached; tick++)
        {
            world.BeginTick();
            var intent = bot.Decide(s, world, tick, out _);
            world.CrewAct(ref s, intent, 1);
            world.Step(new TrainControls { Brake = 1, Reverser = 1 });
            PlayerMotor.Step(ref s, intent, train, P, T, SimConstants.TickSeconds, applyLook: false);
        }
        Assert.False(train.Vehicles[car].Breached, $"{s.Surface} on {s.Parent} at {s.Position}");
        Assert.Equal(0, train.Vehicles[car].DoorsOpen);
        Assert.True(PlayerMotor.NearHeat(s, train), $"{s.Surface} on {s.Parent} at {s.Position}");
    }

    [Fact]
    public void ABreachReachesEveryClient()
    {
        var r = new Rig();
        var at = Breaches.Roof(r.Train.Frames[3].Shape)!.Value;
        r.Train.Vehicles[3].Breach(at);
        var client = new Rig();
        var controls = new TrainControls();
        WorldRecords.Apply(WorldRecords.Capture(r.World, controls, []), client.World, ref controls, []);
        Assert.True(client.Train.Vehicles[3].Breached);
        Assert.True((client.Train.Vehicles[3].BreachAt - at).Length < 1e-3);
        Assert.False(client.Train.Vehicles[2].Breached);
        // Boarded up on the host, it's whole on the client.
        r.Train.Vehicles[3].Breached = false;
        WorldRecords.Apply(WorldRecords.Capture(r.World, controls, []), client.World, ref controls, []);
        Assert.False(client.Train.Vehicles[3].Breached);
    }
}
