using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The fireman (T75): a crew big enough keeps a second pair of hands in the cab, which is the only other driver the crew
/// has at speed (nobody gets over the tender to the cab). And with a Climber in the cab, the cab's crew keep out of its reach.
/// </summary>
public class FiremanTests
{
    static readonly PlayerTuning P = Tuning.Player;
    static readonly RailLine Line = new(new LineDefinition("t", [new TrackSegment(80_000)]));

    sealed class Cab
    {
        public readonly World World;
        public readonly CrewCalls Calls = new();
        public readonly ConductorBot Driver, Fireman;
        public PlayerState DriverState, FiremanState;

        public Cab(bool boiler = false)
        {
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 5, 1)), Line, 2_000, boiler ? Tuning.Boiler : null);
            train.Dynamics.Velocity = 12;
            World = new World(train, Tuning.Combat);
            var quiet = Tuning.Enemies with { Director = Tuning.Enemies.Director with { GraceMinSeconds = 1e9, GraceMaxSeconds = 1e9 } };
            World.EnableEnemies(quiet, route: null, 1, crew: 2, authority: true);
            Driver = new ConductorBot(Calls, 0);
            Fireman = new ConductorBot(Calls, 1) { Fireman = true };
            DriverState = PlayerMotor.SpawnInCab(train, P);
            FiremanState = PlayerMotor.SpawnInCab(train, P, -0.8);
        }

        public TrainOnLine Train => World.Train;

        /// <summary>Both decide, act and walk, the driver first (as the harness has it); damage lands.</summary>
        public void Run(double seconds)
        {
            for (int i = 0; i < seconds * SimConstants.TickRate; i++)
            {
                World.BeginTick();
                var d = Driver.Decide(DriverState, World, World.Tick, out _);
                var f = Fireman.Decide(FiremanState, World, World.Tick, out _);
                World.CrewAct(ref DriverState, d, 1);
                World.CrewAct(ref FiremanState, f, 2);
                World.Step(World.Controls);
                World.ApplyDamage(id => id == 1 ? DriverState : FiremanState, (id, s) => { if (id == 1) DriverState = s; else FiremanState = s; }, [1, 2]);
                PlayerMotor.Step(ref DriverState, d, Train, P, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
                PlayerMotor.Step(ref FiremanState, f, Train, P, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
            }
        }
    }

    [Fact]
    public void TheFiremanStandsByWhileTheDriverLivesAndTakesTheControlsWhenItDies()
    {
        var cab = new Cab();
        cab.Run(2);
        Assert.True(cab.Driver.Driving);
        Assert.False(cab.Fireman.Driving);
        Assert.True(PlayerMotor.InCab(cab.FiremanState, cab.Train));

        cab.DriverState = cab.DriverState with { Death = DeathCause.Climbed };
        cab.Run(1);
        Assert.True(cab.Fireman.Driving);
        Assert.True(cab.Calls.Has(StopJob.Driver));
    }

    [Fact]
    public void TheFiremanKeepsTheFireFromItsOwnSideOfTheFireboxDoor()
    {
        // Standing by with the fire low and no driver heard (deadLines:3: walking straight at the firebox from the left, it
        // fetched up at the vent's valve and never shovelled, and the Hollow came down a cold stack).
        var cab = new Cab(boiler: true);
        cab.DriverState = cab.DriverState with { Death = DeathCause.Climbed };
        var bt = Tuning.Boiler;
        var b = cab.Train.Boiler;
        b.Firebox = bt.FireboxCapacity * bt.LowFireFraction * 0.8;
        cab.Train.Boiler = b;
        double tender = b.Tender;
        cab.Run(30);
        Assert.False(cab.Fireman.Driving);
        // (With steam driving, T97, and the pressure up, only as much as keeps the fire off low: the rest would be speed.)
        Assert.True(cab.Train.Boiler.Tender < tender - 2, $"shovelled {tender - cab.Train.Boiler.Tender:0}");
        Assert.False(cab.Train.Boiler.LowFire(bt));
    }

    [Fact]
    public void WithAClimberInTheCabTheyKeepToTheFrontCornersOutOfItsReach()
    {
        var cab = new Cab();
        var cabBox = cab.Train.Frames[0].Shape.Cab!.Value;
        // In the cab where it comes down (App. A.4): the middle of it, where the driver was standing.
        var climber = cab.World.AddEnemy(id => new Climber(id));
        climber.Restore(SpinePhase.Commit, 0, Tuning.Enemies.Climbers.Health, 0, cabBox.Centre, 0, 0, 0, -1, 1);
        cab.Run(20);
        Assert.True(climber.Inside);
        Assert.True(cab.DriverState.Alive && cab.FiremanState.Alive);
        Assert.Equal(P.Health, cab.DriverState.Health);
        // Still in the cab, both of them (an empty cab's the Track Doll's), and out of its reach.
        foreach (var s in new[] { cab.DriverState, cab.FiremanState })
        {
            Assert.True(PlayerMotor.InCab(s, cab.Train));
            Assert.True((s.Position - cabBox.Centre).Length > Tuning.Enemies.Climbers.Reach);
        }
    }

    [Fact]
    public void TheHeadlampTheClimberSmashedWaitsTillItsGone()
    {
        // Note 301's smashed headlamp is the wrench's, mended from the middle of the cab: with the Climber that smashed it
        // still in there, the driver keeps to its corner, not across the cab into its reach (a harness night's driver was
        // taken at km 3 doing it, and the train stood the rest of the night).
        var cab = new Cab();
        var cabBox = cab.Train.Frames[0].Shape.Cab!.Value;
        cab.World.SmashLamp(Tuning.Enemies.Climbers.LampOutSeconds);
        var climber = cab.World.AddEnemy(id => new Climber(id));
        climber.Restore(SpinePhase.Commit, 0, Tuning.Enemies.Climbers.Health, 0, cabBox.Centre, 0, 0, 0, -1, 1);
        // While it's in the cab (it may move on into car 1 after a while), the glass waits.
        for (int t = 0; t < 20 && climber.Inside && climber.Attached == 0; t++)
        {
            cab.Run(1);
            if (climber.Inside && climber.Attached == 0)
                Assert.True(Repairs.LampSmashed(cab.Train), $"mended at {t + 1} s with the Climber in the cab");
        }
        Assert.True(cab.DriverState.Alive);
        Assert.Equal(P.Health, cab.DriverState.Health);
        Assert.True((cab.DriverState.Position - cabBox.Centre).Length > Tuning.Enemies.Climbers.Reach);
    }

    /// <summary>A driver bot in the cab and a walker bot on car 2's roof, on the calls; both decide, act and walk.</summary>
    sealed class DriverAndWalker
    {
        public readonly World World;
        public readonly CrewCalls Calls = new();
        public readonly ConductorBot Driver;
        public readonly RoofWalkerBot Walker;
        public readonly Dictionary<int, PlayerState> Crew;

        public DriverAndWalker()
        {
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 1)), Line, 2_000);
            train.Dynamics.Velocity = 8;
            World = new World(train, Tuning.Combat);
            var quiet = Tuning.Enemies with { Director = Tuning.Enemies.Director with { GraceMinSeconds = 1e9, GraceMaxSeconds = 1e9 } };
            World.EnableEnemies(quiet, route: null, 1, crew: 2, authority: true);
            Driver = new ConductorBot(Calls, 0);
            Walker = new RoofWalkerBot(7) { Me = 2, Calls = Calls };
            Crew = new() { [1] = PlayerMotor.SpawnInCab(train, P), [2] = PlayerMotor.SpawnOnRoof(train, 3, 0, P) };
        }

        public TrainOnLine Train => World.Train;

        public void Run(double seconds)
        {
            for (int i = 0; i < seconds * SimConstants.TickRate; i++)
            {
                World.BeginTick();
                Walker.Crew = [(1, Crew[1])];
                Driver.Crewmates = [Crew[2]];
                var d = Driver.Decide(Crew[1], World, World.Tick, out _); // dead too, as the harness has it: it says so
                var w = Walker.Decide(Crew[2], World, World.Tick, out _);
                foreach (var (id, intent) in new[] { (1, d), (2, w) })
                {
                    var s = Crew[id];
                    World.CrewAct(ref s, intent, id);
                    Crew[id] = s;
                }
                World.Step(World.Controls);
                World.ApplyDamage(id => Crew[id], (id, s) => Crew[id] = s, [1, 2]);
                foreach (var (id, intent) in new[] { (1, d), (2, w) })
                {
                    var s = Crew[id];
                    PlayerMotor.Step(ref s, intent, Train, P, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
                    Crew[id] = s;
                }
            }
        }

        /// <summary>Runs until the walker's in the cab, at most <paramref name="seconds"/>; how long it took, or null.</summary>
        public double? UntilInCab(double seconds)
        {
            for (int t = 0; t < seconds; t++)
            {
                Run(1);
                if (PlayerMotor.InCab(Crew[2], Train))
                    return t + 1;
            }
            return null;
        }
    }

    [Fact]
    public void WithTheDriverDeadAWalkerGoesForwardAndTakesTheControls()
    {
        // Note 399 (a harness night's driver taken by a Climber at km 3, and the train stood the rest of the night: nobody
        // else drives since the fireman went, note 280): a walker on the roofs hears the driver's gone, goes forward over the
        // engine's hood and down its roof hatch into the cab, and drives on.
        var n = new DriverAndWalker();
        n.Run(5);
        Assert.False(n.Walker.Relieving);
        Assert.True(n.Calls.Has(StopJob.Driver));
        // The driver dies at the controls; the train brakes to a stand with nobody at them (the Deadman's).
        n.Crew[1] = n.Crew[1] with { Death = DeathCause.Climbed, Health = 0 };
        var inCab = n.UntilInCab(120);
        Assert.True(inCab is not null, $"never got to the cab: at {n.Crew[2].Parent}/{n.Crew[2].Surface} {n.Crew[2].Position}");
        Assert.True(n.Walker.Relieving);
        double from = n.Train.Dynamics.Distance;
        n.Run(60);
        Assert.True(n.Calls.Has(StopJob.Driver), "the crew hears a driver again");
        Assert.True(n.Train.Dynamics.Distance - from > 100, $"drove on {n.Train.Dynamics.Distance - from:0} m in a minute");
    }

    [Fact]
    public void AClimberInTheCabBringsAWalkerForwardAndTheTwoClubItOut()
    {
        // Note 399: alone in the cab with a Climber, the driver keeps clear of it (a lone blow doesn't hurt it, note 288), and
        // its fire goes unworked till the train stands. A walker comes forward into the cab, and the two of them club it;
        // then the walker goes back to the train.
        var n = new DriverAndWalker();
        n.Run(2);
        var cab = n.Train.Frames[0].Shape.Cab!.Value;
        var climber = n.World.AddEnemy(id => new Climber(id));
        climber.Restore(SpinePhase.Commit, 0, Tuning.Enemies.Climbers.Health, 0, cab.Centre, 0, 0, 0, -1, 1);
        n.Run(1);
        Assert.True(climber.Inside);
        Assert.True(n.Walker.Relieving);
        // Forward to the cab, and out of it the Climber goes: clubbed dead, or off to the cars (where nobody's alone with it).
        for (int t = 0; t < 120 && climber.Attached == 0 && !climber.Gone; t++)
            n.Run(1);
        Assert.True(climber.Gone || climber.Attached != 0, $"the Climber's still in the cab; the walker's at {n.Crew[2].Parent}/{n.Crew[2].Surface} {n.Crew[2].Position}");
        Assert.True(n.Crew[1].Alive && n.Crew[2].Alive);
        // Then back to the train: off the engine, nobody's but the driver's.
        n.Run(40);
        Assert.False(n.Walker.Relieving);
        Assert.True(n.Crew[2].Parent != 0, $"still on the engine: {n.Crew[2].Surface} {n.Crew[2].Position}");
        Assert.True(n.Calls.Has(StopJob.Driver));
    [Fact]
    public void TheHeadlampsMendedFromTheFloorNotTheVentsCorner()
    {
        // The corner the driver keeps to while a Climber's in the cab is by the vent: Use there is the vent's. With the
        // Climber gone and the glass to mend, a harness night's driver held the vent open from there with the wrench, the
        // lamp never mended and the boiler drained to nothing. It steps to the floor behind the fire first.
        var cab = new Cab(boiler: true);
        cab.FiremanState = cab.FiremanState with { Death = DeathCause.Climbed, Health = 0 };
        var box = cab.Train.Frames[0].Shape.Cab!.Value;
        cab.DriverState.Position = new Ballast.Double3(box.Max.X - 0.4, cab.DriverState.Position.Y, box.Min.Z + 0.9);
        cab.World.SmashLamp(Tuning.Enemies.Climbers.LampOutSeconds);
        double pressure = cab.Train.Boiler.Pressure;
        cab.Run(25);
        Assert.False(Repairs.LampSmashed(cab.Train));
        Assert.False(cab.Train.Boiler.Venting);
        Assert.True(cab.Train.Boiler.Pressure > pressure - 10, $"pressure {pressure:0} to {cab.Train.Boiler.Pressure:0}");
    }
}
