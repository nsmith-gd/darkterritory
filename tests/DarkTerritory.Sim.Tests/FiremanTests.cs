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

        public Cab()
        {
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 5, 1)), Line, 2_000);
            train.Dynamics.Velocity = 12;
            World = new World(train, Tuning.Combat);
            var quiet = Tuning.Enemies with { Director = Tuning.Enemies.Director with { GraceSeconds = 1e9 } };
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
    public void WithAClimberInTheCabTheyKeepToTheFrontCornersOutOfItsReach()
    {
        var cab = new Cab();
        var cabBox = cab.Train.Frames[0].Shape.Cab!.Value;
        // In the cab where it comes down (App. A.4): the middle of it, where the driver was standing.
        var climber = cab.World.AddEnemy(id => new Climber(id));
        climber.Restore(SpinePhase.Punish, 0, Tuning.Enemies.Climbers.Health, 0, cabBox.Centre, 0, 0, 0, 1, 1);
        cab.Run(20);
        Assert.True(climber.Inside);
        Assert.True(cab.DriverState.Alive && cab.FiremanState.Alive);
        Assert.Equal(P.Health, cab.DriverState.Health);
        // Still in the cab, both of them (an empty cab's the Deadman's), and out of its reach.
        foreach (var s in new[] { cab.DriverState, cab.FiremanState })
        {
            Assert.True(PlayerMotor.InCab(s, cab.Train));
            Assert.True((s.Position - cabBox.Centre).Length > Tuning.Enemies.Climbers.Reach);
        }
    }
}
