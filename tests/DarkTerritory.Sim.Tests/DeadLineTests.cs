using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The director's decision of 7 Oct 2026 (ARCHITECTURE §8 note 286): "The switch itself shouldn't cause derail, it should
/// be lines that lead nowhere." A junction set against the train (the Switchman's work, until note 852 cut it; a hand's
/// now) sends it down a dead line: the clock's cost. It derails only when the driver runs off the end through the buffers,
/// warned in the cab a full lead ahead.
/// </summary>
public class DeadLineTests
{
    static readonly EnemyTuning E = Tuning.Enemies;
    static readonly Lazy<Rail.RailLine> Frontier7 = new(() => LineGen.Routes.Generate(DataFile.FindContentRoot(), "frontier:7", 4).Build());

    /// <summary>A dead line's points 600 m ahead of a train at <paramref name="speed"/>, the director quiet.</summary>
    static (Night Night, int Branch) AtADeadLine(double speed)
    {
        var line = Frontier7.Value;
        int branch = Enumerable.Range(0, line.Branches.Count).Where(i => line.Branches[i].Kind == Rail.BranchKind.DeadLine)
            .OrderBy(i => Math.Abs(line.Branches[i].Toe - 6850)).First();
        var quiet = E with { Director = E.Director with { GraceMinSeconds = 1e9, GraceMaxSeconds = 1e9 } };
        var n = new Night(4, speed, enemies: quiet, line: line);
        n.Train.Dynamics.Distance = line.Branches[branch].Toe - 600;
        return (n, branch);
    }

    /// <summary>The dead line's points set against the train, as a hand at the stand would (World.SetSwitch).</summary>
    static void Thrown(Night n, int branch) => Assert.True(n.World.SetSwitch(branch, true));

    [Fact]
    public void ThrownAgainstTheTrainTheJunctionSendsItDownALineThatLeadsNowhere()
    {
        var (n, branch) = AtADeadLine(10);
        Thrown(n, branch);
        for (int i = 0; i < 120 && n.Train.Dynamics.Path != branch; i++)
            n.Run(1);
        Assert.Equal(branch, n.Train.Dynamics.Path);
        Assert.False(n.World.Derailed);
        Assert.True(DeadEnds.Assess(n.Train, n.Train.Dynamics.Tuning.Overspeed).OnDeadLine);
        n.AssertFair();
    }

    [Fact]
    public void RunOffTheEndAndItDerailsButOnlyAfterTheCabWasWarnedAFullLead()
    {
        var (n, branch) = AtADeadLine(10);
        Thrown(n, branch);
        var t = n.Train.Dynamics.Tuning.Overspeed;
        double warned = 0;
        for (int i = 0; i < 1200 && !n.World.Derailed; i++)
        {
            n.Run(0.5);
            warned = Math.Max(warned, n.World.DeadEndWarnSeconds);
        }
        Assert.True(n.World.Derailed);
        Assert.Matches(@"^ran off the end of a dead line at \d+ km/h \(over 25 km/h it goes through the buffers\)$", n.World.DerailCause);
        Assert.True(warned >= t.LeadSeconds, $"warned {warned:0.0} s");
        Assert.Equal(0, n.World.DeadEndsSpared);
    }

    [Fact]
    public void ADriverWhoBrakesOnTheWarningStopsShortOfTheBuffers()
    {
        var (n, branch) = AtADeadLine(18);
        Thrown(n, branch);
        var t = n.Train.Dynamics.Tuning.Overspeed;
        double warned = 0;
        bool braking = false;
        for (int i = 0; i < 600 * SimConstants.TickRate && !n.World.Derailed; i++)
        {
            if (!braking && DeadEnds.Assess(n.Train, t).Warning)
                warned += SimConstants.TickSeconds;
            // Reacts 3.5 s after the warning goes up: shuts off and brakes.
            braking |= warned >= t.LeadSeconds - 0.5;
            if (braking)
                n.Controls = new TrainControls { Reverser = 1, Brake = 1 };
            n.Run(SimConstants.TickSeconds, holdSpeed: !braking);
            if (braking && n.Train.Dynamics.Speed < 0.05)
                break;
        }
        Assert.False(n.World.Derailed, n.World.DerailCause);
        Assert.Equal(branch, n.Train.Dynamics.Path);
    }

    [Fact]
    public void BuffersHitBeforeTheWarningsLeadAreTheBufferStopsDamageAlone()
    {
        var (n, branch) = AtADeadLine(10);
        n.Train.Dynamics.Path = branch;
        n.Train.Dynamics.Distance = n.Train.Line.PathLength(branch) - 15;
        n.Run(3);
        Assert.False(n.World.Derailed, n.World.DerailCause);
        Assert.True(n.World.DeadEndsSpared >= 1);
    }
}
