using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Trouble inside the cars (after the 100-night playtest): a fire, a loose load, Gnawers. Each is answered from inside its
/// car, each can hurt or kill whoever's in there, and each goes with the car if it's cut loose.
/// </summary>
public class IncidentTests
{
    static readonly EnemyTuning E = Tuning.Enemies;
    static readonly RailLine Line = new(new LineDefinition("t", [new TrackSegment(80_000)]));
    const int Car = 2;

    sealed class Night
    {
        public readonly World World;
        public readonly List<PlayerState> Crew = [];
        public readonly List<PlayerIntent> Intents = [];
        public double Speed = 10;

        public Night(EnemyTuning? tuning = null)
        {
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 5, 1)), Line, 2_000);
            World = new World(train, Tuning.Combat);
            var quiet = (tuning ?? E) with { Director = E.Director with { GraceSeconds = 1e9, PaceSeconds = 1e9 } };
            World.EnableEnemies(quiet, route: null, 1, crew: 2, authority: true);
        }

        public TrainOnLine Train => World.Train;
        public Vehicle Cargo => Train.Vehicles[Car];

        /// <summary>Someone in car 2's aisle, this far along it, holding Use or not.</summary>
        public int Inside(double z, bool use = false)
        {
            var layout = Tuning.Train.Geometry.Interior!;
            Crew.Add(new PlayerState
            {
                Parent = Car, Position = new Double3(layout.DoorX, layout.FloorHeight, z), Surface = Surface.Deck,
                Health = Tuning.Player.Health, LineHint = Train.Cars[Car].FrontDistance,
            });
            Intents.Add(use ? new PlayerIntent { Buttons = PlayerButtons.Use } : default);
            return Crew.Count;
        }

        public void Run(double seconds, Func<bool>? until = null)
        {
            for (int i = 0; i < Math.Max(1, seconds * SimConstants.TickRate) && until?.Invoke() != true; i++)
            {
                Train.Dynamics.Velocity = Speed;
                World.BeginTick();
                for (int c = 0; c < Crew.Count; c++)
                {
                    var s = Crew[c];
                    World.CrewAct(ref s, Intents[c], c + 1);
                    Crew[c] = s;
                }
                World.Step(new TrainControls { Reverser = 1 });
                World.ApplyDamage(id => id <= Crew.Count ? Crew[id - 1] : null, (id, s) => Crew[id - 1] = s, Enumerable.Range(1, Crew.Count));
                for (int c = 0; c < Crew.Count; c++)
                {
                    var s = Crew[c];
                    PlayerMotor.Step(ref s, Intents[c], Train, Tuning.Player, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
                    Crew[c] = s;
                }
            }
        }

        public bool Present(EnemyKind kind) => World.ActiveEnemies.Any(e => e.Kind == kind && !e.Gone);
    }

    [Fact]
    public void AFireLeftAloneBurnsTheCargoAndTakesTheNextCar()
    {
        // Room for a second fire (the tuning allows one at a time; a left-alone fire spreads into any room there is).
        var n = new Night(E with { CarFire = E.CarFire with { MaxActive = 2 } });
        var fire = n.World.AddEnemy(id => CarFire.In(id, n.Train, Car, 0, E.CarFire));
        n.Run(1);
        Assert.Equal(SpinePhase.Telegraph, fire.Phase);
        n.Run(150);
        Assert.Equal(SpinePhase.Punish, fire.Phase);
        Assert.True(n.Cargo.CargoIntegrity < 0.9, $"cargo {n.Cargo.CargoIntegrity:0.00}");
        Assert.True(n.Cargo.Integrity < 1);
        Assert.True(n.World.ActiveEnemies.Count(e => e.Kind == EnemyKind.CarFire) >= 2, "it didn't spread");
    }

    [Fact]
    public void StandingInABlazingCarKillsAndBeatingItOutEndsIt()
    {
        var n = new Night();
        var fire = n.World.AddEnemy(id => CarFire.In(id, n.Train, Car, 0, E.CarFire));
        fire.Extra = 1;
        int idle = n.Inside(-3);
        n.Run(60);
        Assert.Equal(DeathCause.Burned, n.Crew[idle - 1].Death);

        var m = new Night();
        var smoke = m.World.AddEnemy(id => CarFire.In(id, m.Train, Car, 0.5, E.CarFire));
        int beater = m.Inside(0, use: true);
        m.Run(30, () => smoke.Gone);
        Assert.True(smoke.Gone);
        Assert.True(m.Crew[beater - 1].Alive);
        Assert.False(m.Present(EnemyKind.CarFire));
    }

    [Fact]
    public void ALooseLoadComesDownOnAHardBrakeAndCrushesWhoevesBesideIt()
    {
        var n = new Night();
        var load = n.World.AddEnemy(id => LooseLoad.In(id, n.Train, Car, 0));
        int beside = n.Inside(0.5);
        n.Run(3);
        Assert.Equal(SpinePhase.Telegraph, load.Phase);
        n.Speed = 9.8; // a hard brake: 0.2 m/s in a tick
        n.Run(0.1);
        Assert.True(load.Gone);
        Assert.True(n.Crew[beside - 1].Health <= Tuning.Player.Health - E.LooseLoad.CrushDamage);
        Assert.True(n.Cargo.CargoIntegrity <= 1 - E.LooseLoad.Breakage + 1e-9);
    }

    [Fact]
    public void ALooseLoadIsLashedFromInside()
    {
        var n = new Night();
        var load = n.World.AddEnemy(id => LooseLoad.In(id, n.Train, Car, 0));
        n.Inside(0.3, use: true);
        n.Run(E.LooseLoad.LashSeconds + 1, () => load.Gone);
        Assert.True(load.Gone);
        Assert.Equal(1, n.Cargo.CargoIntegrity, 9);
        Assert.All(n.Crew, c => Assert.Equal(Tuning.Player.Health, c.Health));
    }

    [Fact]
    public void GnawersEatTheCargoAndAreStampedOutAtACost()
    {
        var n = new Night();
        var nest = n.World.AddEnemy(id => Gnawers.In(id, n.Train, Car, 0, E.Gnawers));
        n.Run(20);
        Assert.Equal(SpinePhase.Punish, nest.Phase);
        Assert.True(n.Cargo.CargoIntegrity < 1);
        int stamper = n.Inside(0.2, use: true);
        n.Run(30, () => nest.Gone);
        Assert.True(nest.Gone);
        var s = n.Crew[stamper - 1];
        Assert.True(s.Alive);
        Assert.True(s.Health < Tuning.Player.Health, "stamping them out didn't cost anything");
    }

    [Fact]
    public void TroubleGoesWithACarCutLoose()
    {
        var n = new Night();
        var fire = n.World.AddEnemy(id => CarFire.In(id, n.Train, 3, 0, E.CarFire));
        n.Run(1);
        n.Train.Uncouple(2);
        n.Run(1);
        Assert.True(fire.Gone);
    }

    [Fact]
    public void ADraggerStampedOnTwiceLetsGoOfTheCar()
    {
        var n = new Night();
        var d = n.World.AddEnemy(id => Dragger.Under(id, n.Train, Car, 1, 0));
        var shape = n.Train.Frames[Car].Shape;
        n.Crew.Add(PlayerMotor.SpawnOnRoof(n.Train, Car, 0, Tuning.Player, shape.HalfWidth - 0.3));
        n.Intents.Add(new PlayerIntent { Buttons = PlayerButtons.Use });
        n.Run(E.Draggers.RearmSeconds * 3, () => d.Gone);
        Assert.True(d.Gone);
        Assert.True(n.Crew[0].Alive);
    }

    [Fact]
    public void TheDirectorSendsTroubleIntoTheCars()
    {
        // Nobody aboard answers anything here, so each night soon fills its caps: many short nights, not one long one.
        var seen = new HashSet<EnemyKind>();
        for (ulong seed = 1; seed <= 40 && !(seen.Contains(EnemyKind.CarFire) && seen.Contains(EnemyKind.LooseLoad) && seen.Contains(EnemyKind.Gnawers)); seed++)
        {
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), Line, 2_000);
            var world = new World(train, Tuning.Combat);
            world.EnableEnemies(E, route: null, seed, crew: 4, authority: true);
            for (int i = 0; i < 4 * 60 * SimConstants.TickRate; i++)
            {
                train.Dynamics.Velocity = 12;
                world.BeginTick();
                world.Step(new TrainControls { Reverser = 1 });
                foreach (var e in world.ActiveEnemies)
                    seen.Add(e.Kind);
            }
            Assert.All(world.ActiveEnemies.OfType<Incident>(), e => Assert.Equal(VehicleKind.Cargo, train.Vehicles[e.Attached].Kind));
        }
        Assert.Contains(EnemyKind.CarFire, seen);
        Assert.Contains(EnemyKind.LooseLoad, seen);
        Assert.Contains(EnemyKind.Gnawers, seen);
    }

    [Fact]
    public void AWalkerGoesInAndPutsOutAFire()
    {
        var n = new Night();
        var fire = n.World.AddEnemy(id => CarFire.In(id, n.Train, 3, 0, E.CarFire));
        var bot = new RoofWalkerBot(4, Tuning.Player.Cold);
        n.Crew.Add(PlayerMotor.SpawnOnRoof(n.Train, 1, 0, Tuning.Player));
        n.Intents.Add(default);
        for (uint tick = 0; tick < 90 * SimConstants.TickRate && !fire.Gone; tick++)
        {
            n.Intents[0] = bot.Decide(n.Crew[0], n.World, tick, out _);
            n.Run(0);
        }
        Assert.True(fire.Gone, $"the walker's {n.Crew[0].Surface} on {n.Crew[0].Parent} ({bot.WarmUpStep}); fire at {fire.Extra:0.00}");
        Assert.True(n.Crew[0].Alive);
    }
}
