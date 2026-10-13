using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// T128 (build 1121: "a creature carried the director up a mountainside, a destination that makes no sense"; ARCHITECTURE §8
/// note 273). The Whistler is the one creature that carries a player away from the train (the Corrupted drag theirs along
/// the cars; the Gaunt carries bodies off, over the ground already). Its nest is beside the line, inside the walkable
/// corridor, on land a person can run over, and the run there is over the ground.
/// </summary>
public class GrabDestinationTests
{
    static readonly EnemyTuning E = Tuning.Enemies;
    static readonly PlayerTuning P = Tuning.Player;

    /// <summary>Flat to <paramref name="flat"/> m out from the line, then rising at <paramref name="right"/> (rise over run) on its right and <paramref name="left"/> on its left.</summary>
    sealed class Land(Double3 origin, Double3 across, double right, double left, double flat = 4) : ITrackConditions
    {
        public double Ground(Double3 world)
        {
            double lateral = Double3.Dot(world - origin, across);
            double out_ = Math.Max(0, Math.Abs(lateral) - flat);
            return origin.Y + out_ * (lateral >= 0 ? right : left);
        }
        public double Adhesion(int path, double distance) => 1;
        public double Drag(int path, double distance, double speed) => 0;
    }

    static (Night Night, Double3 Gap) Stopped(double right, double left)
    {
        var n = new Night(4, speed: 0);
        var frame = n.Train.Frames[2];
        var rail = n.Train.Line.Sample(RailLine.MainPath, n.Train.Dynamics.Distance);
        n.Train.Line.Conditions = new Land(rail.Position, (frame.Right with { Y = 0 }).Normalized, right, left);
        return (n, frame.ToWorld(CrewSense.GapLocal(n.Train, 2)));
    }

    /// <summary>Snatched alone at the gap, on its right-hand side, and carried until the nest; where they end up.</summary>
    static (Double3 At, double Lateral, PlayerState State) Carried(Night n, Double3 gap)
    {
        var w = n.World.AddEnemy(id => Whistler.InGap(id, n.Train, 2, E.Whistler));
        n.Run(E.Whistler.WhistleAfterStop + E.Whistler.WhistleSeconds + 1);
        Assert.Equal(SpinePhase.Commit, w.Phase);
        var s = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P);
        s.Position = CrewSense.GapLocal(n.Train, 2) with { X = 0.4, Y = 0.9 };
        s.Surface = Surface.Coupler;
        n.Crew[1] = s;
        n.Run(0.5);
        Assert.Equal(SpinePhase.Grab, w.Phase);
        n.Run(E.Whistler.NestDistance / E.Whistler.RunSpeed + 2);
        Assert.Equal(SpinePhase.Grab, w.Phase);
        var at = PlayerMotor.WorldPosition(n.Crew[1], n.Train);
        double lateral = Double3.Dot(at - gap, (n.Train.Frames[2].Right with { Y = 0 }).Normalized);
        return (at, lateral, n.Crew[1]);
    }

    [Fact]
    public void OnFlatLandTheNestIsBesideTheLineInsideTheCorridor()
    {
        var (n, gap) = Stopped(0, 0);
        var (at, lateral, _) = Carried(n, gap);
        Assert.InRange(lateral, E.Whistler.NestDistance - 1, E.Whistler.NestDistance + 1);
        Assert.True(E.Whistler.NestDistance <= Tuning.Train.Recovery.CorridorM, "the nest is inside Line Plan §12.6's corridor");
        Assert.Equal(n.Train.Line.Conditions!.Ground(at), at.Y, 1);
    }

    [Fact]
    public void ItNeverCarriesYouUpAMountainside()
    {
        // The director's night: a mountainside on the side they were taken from (1.5: 56°), level land across the line.
        var (n, gap) = Stopped(1.5, 0);
        var (at, lateral, s) = Carried(n, gap);
        Assert.True(lateral < 0, $"taken {lateral:0.0} m to the mountain's side");
        Assert.InRange(-lateral, E.Whistler.NestMinDistance - 1, E.Whistler.NestDistance + 1);
        Assert.InRange(at.Y - gap.Y, -E.Whistler.NestMaxRise, E.Whistler.NestMaxRise);
        // Stood on the ground there, not underground or up on a hill.
        Assert.Equal(n.Train.Line.Conditions!.Ground(at), at.Y, 1);
        Assert.True(s.Alive);
    }

    [Fact]
    public void AGentleBankIsFineAndACuttingBothSidesIsTheFormationsEdge()
    {
        var (n, gap) = Stopped(0.2, 0.2);
        var right = (n.Train.Frames[2].Right with { Y = 0 }).Normalized;
        var (nest, run) = Whistler.NestSite(n.Train, gap, right, 1, E.Whistler);
        Assert.Equal(E.Whistler.NestDistance, run, 1);
        Assert.True(Double3.Dot(nest - gap, right) > 0);
        // A deep cutting: walls either side. It goes no further than the formation's edge.
        var (c, cut) = Stopped(2, 2);
        var (edge, short_) = Whistler.NestSite(c.Train, cut, right, -1, E.Whistler);
        Assert.Equal(Tuning.Train.Recovery.EdgeM, short_, 1);
        Assert.True(Double3.Dot(edge - cut, right) < 0, "on the side they were taken from");
    }
}
