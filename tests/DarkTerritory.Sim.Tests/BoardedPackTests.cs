using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// A lone crew answers a boarded hound pack (ARCHITECTURE §8 note 343; found in note 336): Cinder Hounds that board stay
/// (note 269), and a crew of one bot never fought them, so the pack and its fires held the caps all night. The lone driver
/// stands the train and cuts their car loose (v1.1 App. A.3: "they go only dead, or with their car cut loose").
/// </summary>
public class BoardedPackTests
{
    static readonly PlayerTuning P = Tuning.Player;
    static readonly TrainTuning T = Tuning.Train;
    static readonly EnemyTuning E = HoundRunTests.Quiet;

    sealed class Lone
    {
        public readonly World World;
        public readonly ConductorBot Driver = new(new CrewCalls(), 0);
        public PlayerState Self;
        /// <summary>Another crewmate aboard, standing still (player 2), if any.</summary>
        public PlayerState? Mate;
        TrainControls _controls = new() { Reverser = 1 };
        /// <summary>Where the driver's been, each time its surface or car changed ("12s:Roof2").</summary>
        public readonly List<string> Path = [];
        uint _tick;

        public Lone(int cars, double speed)
        {
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, cars, 1)), new RailLine(new LineDefinition("t", [new TrackSegment(20_000)])), 2_000, Tuning.Boiler);
            train.Dynamics.Velocity = speed;
            World = new World(train, Tuning.Combat);
            World.EnableBodies();
            World.EnableEnemies(E, null, 1, crew: 1, authority: true);
            Self = PlayerMotor.SpawnInCab(train, P);
        }

        public TrainOnLine Train => World.Train;

        /// <summary>A pack of <paramref name="size"/> aboard <paramref name="car"/> (the consist's place), as one that leapt on.</summary>
        public List<CinderHound> Pack(int car, int size)
        {
            int id = Train.Dynamics.Consist.Vehicles[car].Id;
            var shape = Train.Frames[id].Shape;
            var pack = new List<CinderHound>();
            for (int i = 0; i < size; i++)
            {
                var h = World.AddEnemy(e => new CinderHound(e, 900) { Health = E.CinderHounds.Health });
                h.Restore(SpinePhase.Commit, 0, E.CinderHounds.Health, id, new Double3(i % 2 == 0 ? 0.6 : -0.6, shape.RoofHeight, shape.HalfLength - 2.5 - i * 0.8), 0, 0, 0, 900, 0);
                pack.Add(h);
            }
            return pack;
        }

        public void Until(Func<bool> done, double seconds)
        {
            for (int t = 0; t < seconds * SimConstants.TickRate && !done(); t++)
            {
                World.BeginTick();
                if (CabControls.CanDrive(Self, Train) && CabControls.Clears(_controls, Train, false))
                    _controls.Brake = 0;
                Driver.Crewmates = Mate is { } seen ? [seen] : [];
                var intent = Driver.Decide(Self, World, _tick, out _);
                if (CabControls.ReleasesBrake(intent, Self, Train))
                    _controls.Brake = 0;
                CabControls.Apply(ref _controls, intent, Self, Train);
                var s = Self;
                World.CrewAct(ref s, intent, 1);
                if (Mate is { } m)
                {
                    World.CrewAct(ref m, default, 2);
                    Mate = m;
                }
                World.Step(_controls);
                World.ApplyDamage(id => id == 1 ? s : null, (_, v) => s = v, [1]);
                PlayerMotor.Step(ref s, intent, Train, P, T, SimConstants.TickSeconds, applyLook: false);
                Self = s;
                string here = $"{s.Surface}{s.Parent}";
                if (Path.Count == 0 || !Path[^1].EndsWith(here))
                    Path.Add($"{_tick / 30}s:{here}");
                _tick++;
            }
        }
    }

    [Fact]
    public void ALoneDriverStandsTheTrainAndCutsTheBoardedPacksCarLoose()
    {
        var n = new Lone(cars: 4, speed: 12);
        int rear = n.Train.Dynamics.Consist.Vehicles[^1].Id;
        var pack = n.Pack(n.Train.Dynamics.Consist.Vehicles.Count - 1, 3);
        n.Until(() => pack.All(h => h.Gone), 300);
        Assert.True(pack.All(h => h.Gone), $"driver {n.Self.Surface} on {n.Self.Parent}, health {n.Self.Health}; cutting {n.Driver.CuttingAlone}; path {string.Join(" ", n.Path.Take(40))}");
        Assert.True(n.Train.Dynamics.Consist.IndexOf(rear) < 0, "the pack's car is cut loose");
        // The engine and the two cars ahead of the pack's ground (note 472: the car it boarded and the one ahead it patrols to).
        Assert.Equal(3, n.Train.Dynamics.Consist.Vehicles.Count);
        Assert.True(n.Self.Alive, $"died of {n.Self.Death}");
        // Back up into the cab, and away without it.
        n.Until(() => n.Train.Dynamics.Speed > 3, 120);
        Assert.True(PlayerMotor.InCab(n.Self, n.Train), $"{n.Self.Surface} on {n.Self.Parent}");
        Assert.True(n.Train.Dynamics.Speed > 3);
        Assert.False(n.Driver.CuttingAlone);
    }

    [Fact]
    public void WithACrewmateAliveTheDriverLeavesThePackToThem()
    {
        // A crew's walkers and gunner go at a pack aboard (Heed.Hounds); the driver keeps driving.
        var n = new Lone(cars: 4, speed: 12);
        var pack = n.Pack(n.Train.Dynamics.Consist.Vehicles.Count - 1, 3);
        n.Mate = PlayerMotor.SpawnOnRoof(n.Train, 1, 0, P);
        bool stood = false;
        for (int s = 0; s < 30; s++)
        {
            n.Until(() => false, 1);
            stood |= n.Train.Dynamics.Speed < 1;
        }
        Assert.False(n.Driver.CuttingAlone);
        Assert.False(stood);
    }

    [Fact]
    public void WithOnlyAHurtCrewmateTheDriverCutsThePackLooseItself()
    {
        // Note 484 (D1.3's frontier:7 seed 2, a crew of two): the walker hurt under Heed.PackFightHealth keeps clear of a pack, so
        // nobody fought it and nobody cut it, and the train stood 750 s with a fire every 20 s. Nobody fit to fight it is alone.
        var n = new Lone(cars: 4, speed: 12);
        int rear = n.Train.Dynamics.Consist.Vehicles[^1].Id;
        var pack = n.Pack(n.Train.Dynamics.Consist.Vehicles.Count - 1, 3);
        n.Mate = PlayerMotor.SpawnOnRoof(n.Train, 1, 0, P) with { Health = Heed.PackFightHealth - 25 };
        n.Until(() => pack.All(h => h.Gone), 300);
        Assert.True(pack.All(h => h.Gone), $"driver {n.Self.Surface} on {n.Self.Parent}; cutting {n.Driver.CuttingAlone}");
        Assert.True(n.Train.Dynamics.Consist.IndexOf(rear) < 0, "the pack's car is cut loose");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFitCrewmateWhoDoesntFightThePackStillHasItCut(bool onTheTrain)
    {
        // Note 484 (D1.3's repro again): the gunner, at full health, stood at the castings for the stop's loading while the pack
        // burned cars 2 to 6 for 500 s, and the driver held off the cut for it. Down off the train, it isn't fighting: cut now.
        // Up on it but not going at the pack (standing here): cut once the pack's been aboard Heed.PackUnfoughtSeconds.
        var n = new Lone(cars: 4, speed: 12);
        int rear = n.Train.Dynamics.Consist.Vehicles[^1].Id;
        var pack = n.Pack(n.Train.Dynamics.Consist.Vehicles.Count - 1, 3);
        n.Mate = onTheTrain ? PlayerMotor.SpawnOnRoof(n.Train, 1, 0, P)
            : new PlayerState { Parent = PlayerState.World, Surface = Surface.Ground, Position = new Double3(3, 0, -2_000), Health = P.Health };
        double held = onTheTrain ? Heed.PackUnfoughtSeconds - 5 : 0;
        if (held > 0)
        {
            n.Until(() => n.Driver.CuttingAlone, held);
            Assert.False(n.Driver.CuttingAlone, "a fit crewmate aboard gets its while to go at the pack");
        }
        n.Until(() => pack.All(h => h.Gone), 300);
        Assert.True(pack.All(h => h.Gone), $"driver {n.Self.Surface} on {n.Self.Parent}; cutting {n.Driver.CuttingAlone}");
        Assert.True(n.Train.Dynamics.Consist.IndexOf(rear) < 0, "the pack's car is cut loose");
    }
}
