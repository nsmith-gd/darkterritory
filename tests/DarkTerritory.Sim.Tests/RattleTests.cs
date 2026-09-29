using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>The Rattle (T51, App. A.5, B.5): it lives in the couplings. Rule: don't cross between cars rattling.</summary>
public class RattleTests
{
    static readonly RattleTuning R = Tuning.Enemies.Rattle;
    static readonly RailLine Line = new(new LineDefinition("t", [new TrackSegment(80_000)]));
    const int Car = 2;

    /// <summary>A train stood at a stop, a Rattle in the gap behind car 2, and whoever's about.</summary>
    sealed class Stop
    {
        public readonly World World;
        public readonly List<PlayerState> Crew = [];
        public readonly List<PlayerIntent> Intents = [];

        public Stop()
        {
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 5, 1)), Line, 2_000);
            World = new World(train, Tuning.Combat);
            var quiet = Tuning.Enemies with { Director = Tuning.Enemies.Director with { GraceSeconds = 1e9 } };
            World.EnableEnemies(quiet, route: null, 1, crew: 2, authority: true);
        }

        public TrainOnLine Train => World.Train;
        public double Gap => Tuning.Train.Geometry.CouplingGap;

        public Rattle Nest() => World.AddEnemy(id => Rattle.In(id, Train, Car, Gap));

        /// <summary>On the ground at the gap behind car 2, this far out to its right (0 is in the gap), facing forward.</summary>
        public int Beside(double outward)
        {
            var frame = Train.Frames[Car];
            var at = frame.ToWorld(new Double3(outward, 0, frame.Shape.HalfLength + Gap * 0.5));
            var s = PlayerMotor.SpawnOnGround(at, Train.Line, Train.Dynamics.Distance, Tuning.Player);
            s.Yaw = 0;
            Crew.Add(s);
            Intents.Add(default);
            return Crew.Count;
        }

        public void Run(double seconds)
        {
            for (int i = 0; i < Math.Max(1, seconds * SimConstants.TickRate); i++)
            {
                Train.Dynamics.Velocity = 0;
                World.BeginTick();
                for (int c = 0; c < Crew.Count; c++)
                {
                    var s = Crew[c];
                    World.CrewAct(ref s, Intents[c], c + 1);
                    Crew[c] = s;
                }
                World.Step(new TrainControls { Brake = 1 });
                World.ApplyDamage(id => id <= Crew.Count ? Crew[id - 1] : null, (id, s) => Crew[id - 1] = s, Enumerable.Range(1, Crew.Count));
                for (int c = 0; c < Crew.Count; c++)
                {
                    var s = Crew[c];
                    PlayerMotor.Step(ref s, Intents[c], Train, Tuning.Player, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
                    Crew[c] = s;
                }
            }
        }
    }

    [Fact]
    public void ItsSilentUntilSomeoneComesNearBelowTheRoofs()
    {
        var stop = new Stop();
        var rattle = stop.Nest();
        stop.Run(2);
        Assert.Equal(SpinePhase.Dormant, rattle.Phase);
        // Up on the roof over its gap is the way round it: that doesn't wake it.
        stop.Crew.Add(PlayerMotor.SpawnOnRoof(stop.Train, Car, stop.Train.Frames[Car].Shape.HalfLength - 0.5, Tuning.Player));
        stop.Intents.Add(default);
        stop.Run(2);
        Assert.Equal(SpinePhase.Dormant, rattle.Phase);
        // Walking up to it on the ground does.
        stop.Beside(R.ArmRadius - 1);
        stop.Run(SimConstants.TickSeconds);
        Assert.True(rattle.Rattling);
    }

    [Fact]
    public void StepIntoTheGapWhileItRattlesAndYoureDragged()
    {
        var stop = new Stop();
        var rattle = stop.Nest();
        stop.Beside(3);
        stop.Run(SimConstants.TickSeconds);
        Assert.True(rattle.Rattling);
        // Heard it, and crossed anyway.
        stop.Run(Tuning.Enemies.MinReactionSeconds);
        stop.Intents[0] = new PlayerIntent { MoveX = -1 };
        stop.Run(2);
        Assert.False(stop.Crew[0].Alive);
        Assert.Equal(DeathCause.PulledUnder, stop.Crew[0].Death);
    }

    [Fact]
    public void ItNeverGrabsBeforeItsBeenHeardForTheReactionWindow()
    {
        var stop = new Stop();
        var rattle = stop.Nest();
        // Already standing in the gap when it wakes: the rattle is the warning, and there's time to get out.
        stop.Beside(-0.9);
        stop.Run(Tuning.Enemies.MinReactionSeconds - 0.6);
        Assert.True(stop.Crew[0].Alive);
        Assert.True(rattle.Rattling);
        stop.Intents[0] = new PlayerIntent { MoveX = -1 };
        stop.Run(1.5);
        Assert.True(stop.Crew[0].Alive);
        Assert.False(rattle.InGap(PlayerMotor.WorldPosition(stop.Crew[0], stop.Train), stop.Train));
    }

    [Fact]
    public void WaitItOutAwayFromTheGapAndItGoesQuiet()
    {
        var stop = new Stop();
        var rattle = stop.Nest();
        stop.Beside(3);
        stop.Run(1);
        Assert.True(rattle.Rattling);
        // Still near it, it keeps rattling.
        stop.Run(R.QuietSeconds + 1);
        Assert.True(rattle.Rattling);
        // Walk off out of its reach and wait.
        stop.Intents[0] = new PlayerIntent { MoveX = 1 };
        stop.Run(2);
        stop.Intents[0] = default;
        stop.Run(R.QuietSeconds + 0.5);
        Assert.Equal(SpinePhase.Dormant, rattle.Phase);
        Assert.True(stop.Crew[0].Alive);
    }

    [Fact]
    public void CutTheCarsAtItsGapAndItsGone()
    {
        var stop = new Stop();
        var rattle = stop.Nest();
        stop.Run(0.5);
        stop.Train.Uncouple(Car);
        stop.Run(0.5);
        Assert.DoesNotContain(rattle, stop.World.ActiveEnemies);
    }

    [Fact]
    public void ItNestsMidTrainAndNeverOnSomeone()
    {
        var stop = new Stop();
        var nests = Rattle.Nests(stop.World);
        // Engine and five cars: five gaps, the middle one wanted most.
        Assert.Equal(5, nests.Count);
        Assert.Equal(2, nests.MaxBy(n => n.Weight).Car);
        // Someone standing in that gap as it would come: it takes another.
        stop.Beside(-0.9);
        stop.World.BeginTick();
        var s0 = stop.Crew[0];
        stop.World.CrewAct(ref s0, default, 1);
        Assert.DoesNotContain(Rattle.Nests(stop.World), n => n.Car == Car);
    }

    [Fact]
    public void ABotWaitsItOutRatherThanCross()
    {
        var stop = new Stop();
        var rattle = stop.Nest();
        stop.Beside(1.6);
        stop.Run(SimConstants.TickSeconds);
        Assert.True(rattle.Rattling);
        // Walking across the gap, as a bot's job might have it: heeding it, it stops at the edge and stays alive.
        for (int i = 0; i < 3 * SimConstants.TickRate; i++)
        {
            stop.Intents[0] = Heed.Rattles(new PlayerIntent { MoveX = -1 }, stop.Crew[0], stop.World, Tuning.Player);
            stop.Run(SimConstants.TickSeconds);
        }
        Assert.True(stop.Crew[0].Alive);
        Assert.False(rattle.InGap(PlayerMotor.WorldPosition(stop.Crew[0], stop.Train), stop.Train));
    }

    /// <summary>Enemy tuning whose director can afford the Rattle and nothing else, straight away.</summary>
    static EnemyTuning OnlyTheRattle()
    {
        var d = Tuning.Enemies.Director;
        return Tuning.Enemies with
        {
            Director = d with { GraceSeconds = 0, CooldownSeconds = [1, 1], Costs = d.Costs.ToDictionary(c => c.Key, c => c.Key == "rattle" ? c.Value : 1e9) },
        };
    }

    [Fact]
    public void TheDirectorPutsOneInTheCouplingsOnlyAtAFacilityStop()
    {
        // Stood between stops, never.
        var between = new Stop();
        between.World.EnableEnemies(OnlyTheRattle(), route: null, 1, crew: 2, authority: true);
        between.Run(60);
        Assert.DoesNotContain(between.World.ActiveEnemies, e => e.Kind == EnemyKind.Rattle);

        // Loading at a facility: one, in a gap of the engine's rake.
        var stop = new FacilityTests.Stop(Run.ModuleKind.Crates);
        stop.World.EnableEnemies(OnlyTheRattle(), route: null, 1, crew: 2, authority: true);
        stop.Step(60, []);
        var rattle = Assert.IsType<Rattle>(Assert.Single(stop.World.ActiveEnemies, e => e.Kind == EnemyKind.Rattle));
        var rake = stop.Train.Dynamics.Consist.Vehicles.Select(v => v.Id).ToList();
        Assert.Contains(rattle.Attached, rake[..^1]);
        Assert.Equal(1, stop.World.Director!.Log.Count(l => l.Kind == EnemyKind.Rattle));
    }

    [Fact]
    public void AClientHearsWhichGapIsRattling()
    {
        var stop = new Stop();
        var rattle = stop.Nest();
        stop.Beside(3);
        stop.Run(0.5);
        var client = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 5, 1)), Line, 2_000), Tuning.Combat);
        client.EnableEnemies(Tuning.Enemies, route: null, 1, crew: 1, authority: false);
        var controls = new TrainControls();
        WorldRecords.Apply(WorldRecords.Capture(stop.World, controls, []), client, ref controls, []);
        var seen = Assert.IsType<Rattle>(Assert.Single(client.ActiveEnemies));
        Assert.True(seen.Rattling);
        Assert.Equal(rattle.Attached, seen.Attached);
        Assert.Equal(rattle.Local.Z, seen.Local.Z, 2);
    }
}
