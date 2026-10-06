using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The director's decision of 6 Oct 2026 (GDD App. F.1, "The Track Doll escalates if ignored"; ARCHITECTURE §8 note 268):
/// she's no problem at first, gets worse only for being left alone, telegraphs each stage before it comes, and only at her
/// last can she let a standing train off its brake. Getting her off the train ends it.
/// </summary>
public class TrackDollEscalationTests
{
    static readonly EnemyTuning E = Tuning.Enemies;
    static readonly TrackDollTuning T = E.TrackDoll;
    static readonly PlayerTuning P = Tuning.Player;

    /// <summary>
    /// A 4-car train strikes a doll (nobody aboard, so the cab's empty) and is brought to a stand on its held brake, the way
    /// a crew leaves it at a stop. The director's kept quiet so nothing else comes.
    /// </summary>
    static (Night Night, TrackDoll Doll, double At) StruckAndStanding()
    {
        var n = new Night(4, speed: 10);
        n.World.EnableEnemies(E with { Director = E.Director with { GraceSeconds = 1e9, PaceSeconds = 1e9 } }, null, 1, crew: 4, authority: true);
        var doll = n.World.AddEnemy(id => TrackDoll.Ahead(id, n.Train, 60, T));
        n.Run(8);
        Assert.True(doll.Haunting);
        n.Train.Dynamics.Velocity = 0;
        n.Controls = new TrainControls { Reverser = 1, Brake = 1 };
        n.Run(1, holdSpeed: false);
        return (n, doll, n.Train.Dynamics.Distance);
    }

    /// <summary>When each thing first happened, a second at a time, until <paramref name="until"/> or the seconds run out.</summary>
    sealed class Watch
    {
        public double? Restless1, Stage2, Restless2, Stage3, Tampering2, Tampering3, Moved;
        public bool TamperedAtStage1;
        public int MovedAtStage;

        public void Run(Night n, TrackDoll doll, double at, double seconds, Action<Night>? each = null, Func<Watch, bool>? until = null)
        {
            for (int s = 0; s < seconds && until?.Invoke(this) != true; s++)
            {
                each?.Invoke(n);
                n.Run(1, holdSpeed: false);
                double t = n.World.ElapsedSeconds;
                if (doll.Stage == 1 && doll.Restless) Restless1 ??= t;
                if (doll.Stage == 2) Stage2 ??= t;
                if (doll.Stage == 2 && doll.Restless) Restless2 ??= t;
                if (doll.Stage == 3) Stage3 ??= t;
                if (doll.Tampering && doll.Stage == 1) TamperedAtStage1 = true;
                if (doll.Tampering && doll.Stage == 2) Tampering2 ??= t;
                if (doll.Tampering && doll.Stage == 3) Tampering3 ??= t;
                if (Moved is null && Math.Abs(n.Train.Dynamics.Distance - at) > 0.05)
                {
                    Moved = t;
                    MovedAtStage = doll.Stage;
                }
            }
        }
    }

    [Fact]
    public void AtFirstSheOnlyHauntsTheCarsEvenWithTheCabEmpty()
    {
        var (n, doll, at) = StruckAndStanding();
        var w = new Watch();
        // Short of her first telegraph: the cab's been empty the whole time, and she's never gone near it.
        w.Run(n, doll, at, T.ControlsAfter - T.WarnSeconds - 5);
        Assert.Equal(1, doll.Stage);
        Assert.False(doll.Restless);
        Assert.False(w.TamperedAtStage1);
        Assert.Null(w.Tampering2);
        Assert.True(doll.Attached > 0, $"in car {doll.Attached}");
        Assert.Null(w.Moved);
        Assert.True(n.World.CabEmptySeconds > T.TamperAfterEmpty);
    }

    [Fact]
    public void MindedSheNeverEscalates()
    {
        var (n, doll, at) = StruckAndStanding();
        n.Crew[1] = PlayerMotor.SpawnInCab(n.Train, P);
        // Someone keeps her company: in her car, a few paces off (closer and she vanishes), wherever she's gone.
        void Mind(Night night)
        {
            if (doll.Attached <= 0 || night.Train.Frames[doll.Attached].Shape.Interior is not { } room)
                return;
            double z = doll.Local.Z + 5 <= room.Max.Z - 0.5 ? doll.Local.Z + 5 : doll.Local.Z - 5;
            night.Crew[1] = night.Crew[1] with { Parent = doll.Attached, Position = doll.Local with { Z = z }, Surface = Surface.Deck };
        }
        var w = new Watch();
        // Well past when, left alone, she'd have been at her last stage.
        w.Run(n, doll, at, T.ReleaseAfter + 60, Mind);
        Assert.True(n.Crew[1].Alive);
        Assert.True(doll.Haunting);
        Assert.Equal(1, doll.Stage);
        Assert.Null(w.Stage2);
        Assert.False(w.TamperedAtStage1);
        Assert.Null(w.Moved);
    }

    [Fact]
    public void IgnoredSheTakesTheCabAfterHerTelegraphAndOnlyNudgesTheRegulator()
    {
        var (n, doll, at) = StruckAndStanding();
        var w = new Watch();
        w.Run(n, doll, at, T.ReleaseAfter - T.WarnSeconds - 5);
        // Restless a full warning before the controls, then at them (the cab's long empty).
        Assert.NotNull(w.Restless1);
        Assert.NotNull(w.Stage2);
        Assert.True(w.Stage2 - w.Restless1 >= T.WarnSeconds - 1, $"restless {w.Restless1:0} s, stage 2 {w.Stage2:0} s");
        Assert.NotNull(w.Tampering2);
        Assert.True(doll.Tampering);
        Assert.Equal(2, doll.Stage);
        // A standing train on its brake hasn't moved: at stage 2 her hands never touch the brake.
        Assert.Null(w.Moved);
        Assert.False(doll.ReleasesStandingBrake(n.World));
        for (double s = 0; s < 9; s += 0.25)
        {
            var set = new TrainControls { Reverser = 1, Throttle = 0.1, Brake = 0.6 };
            var hands = TrackDoll.Hands(2.5, s, T.NudgeThrottle, set);
            Assert.Equal(set.Brake, hands.Brake);
            Assert.InRange(hands.Throttle, set.Throttle, T.NudgeThrottle);
        }
    }

    [Fact]
    public void AStandingTrainLosesItsBrakeOnlyAtHerLastStageAfterTheTelegraphs()
    {
        var (n, doll, at) = StruckAndStanding();
        var w = new Watch();
        w.Run(n, doll, at, T.ReleaseAfter + 90, until: x => x.Moved is not null);
        Assert.NotNull(w.Moved);
        Assert.Equal(3, w.MovedAtStage);
        // Every step telegraphed in turn: restless, the controls nudged, restless again, the last stage, a while at the
        // controls in it, and only then the brake.
        Assert.True(w.Stage2 - w.Restless1 >= T.WarnSeconds - 1);
        Assert.True(w.Tampering2 < w.Restless2);
        Assert.True(w.Stage3 - w.Restless2 >= T.WarnSeconds - 1, $"restless {w.Restless2:0} s, stage 3 {w.Stage3:0} s");
        Assert.True(w.Moved - w.Tampering3 >= T.ReleaseAfterAtControls - 1, $"at the controls {w.Tampering3:0} s, moved {w.Moved:0} s");
        Assert.True(doll.ReleasesStandingBrake(n.World));
    }

    [Fact]
    public void WithTheDirectorsExceptionOffNotEvenHerLastStageTakesAStandingBrake()
    {
        var (n, doll, at) = StruckAndStanding();
        n.World.EnableEnemies(E with
        {
            Director = E.Director with { GraceSeconds = 1e9, PaceSeconds = 1e9 },
            TrackDoll = T with { FinalStageReleasesBrake = false },
        }, null, 1, crew: 4, authority: true);
        var w = new Watch();
        w.Run(n, doll, at, T.ReleaseAfter + 60);
        Assert.NotNull(w.Tampering3);
        Assert.Null(w.Moved);
    }

    [Fact]
    public void GettingHerOffTheTrainEndsItAndTheNextDollStartsOver()
    {
        var (n, doll, at) = StruckAndStanding();
        var w = new Watch();
        w.Run(n, doll, at, T.ControlsAfter + 5);
        Assert.Equal(2, doll.Stage);
        // Whoever comes back holding a toy out to her (she leaves the cab when they come in, and takes it in her car).
        if (doll.Attached == 0)
        {
            n.Crew[1] = PlayerMotor.SpawnInCab(n.Train, P);
            n.Run(0.2, holdSpeed: false);
            n.Crew.Remove(1);
        }
        Assert.True(doll.Attached > 0);
        var toy = n.World.Bodies.SpawnCrate(n.Train, doll.Attached, doll.Local + new Double3(0, 0.1, 1), BodyKind.Toy);
        n.Crew[2] = PlayerMotor.SpawnOnRoof(n.Train, doll.Attached, 0, P) with { Position = doll.Local + new Double3(0, 0.1, 1), Surface = Surface.Deck };
        toy.Carrier = 2;
        n.Run(0.2, holdSpeed: false);
        Assert.True(doll.Gone);
        Assert.Equal(0, doll.Stage);
        n.Crew.Remove(2);
        // Nothing's at the controls any more, however long the train stands.
        w = new Watch();
        w.Run(n, doll, n.Train.Dynamics.Distance, T.ReleaseAfter);
        Assert.Null(w.Moved);
        // A doll struck later starts again from nothing.
        n.Controls = new TrainControls { Reverser = 1 };
        n.Train.Dynamics.Velocity = 10;
        var next = n.World.AddEnemy(id => TrackDoll.Ahead(id, n.Train, 60, T));
        n.Run(8);
        Assert.True(next.Haunting);
        Assert.Equal(1, next.Stage);
        Assert.False(next.Restless);
    }

    [Fact]
    public void AClientSeesHerStageAndLetsTheBrakeOffAsTheHostDoes()
    {
        var (n, doll, at) = StruckAndStanding();
        new Watch().Run(n, doll, at, T.ReleaseAfter + 90, until: x => x.Moved is not null);
        var client = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 1)),
            new Rail.RailLine(new Rail.LineDefinition("t", [new Rail.TrackSegment(40_000)])), 2_000), Tuning.Combat);
        client.EnableEnemies(E, null, 1, crew: 4, authority: false);
        var controls = new TrainControls();
        WorldRecords.Apply(WorldRecords.Capture(n.World, n.Controls, []), client, ref controls, []);
        var mirror = Assert.Single(client.ActiveEnemies.OfType<TrackDoll>());
        Assert.Equal(doll.Stage, mirror.Stage);
        Assert.Equal(doll.Restless, mirror.Restless);
        Assert.Equal(doll.Tampering, mirror.Tampering);
        Assert.Equal(doll.ReleasesStandingBrake(n.World), mirror.ReleasesStandingBrake(client));
    }
}
