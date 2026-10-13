using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The Knotter (GDD §21, App. A.3, B.3; the director's brief of 8 Oct 2026; ARCHITECTURE §8 note 365). Rule: don't walk
/// the rope; stop, kill it, couple up. It forces a coupling apart to a gap no jump carries, the train still one; whoever
/// walks its back at speed slips into its coil (a grab a friend breaks); blows land only at a stand; killed, the cars stand
/// uncoupled for the crew to couple up.
/// </summary>
public class KnotterTests
{
    static readonly KnotterTuning K = Tuning.Enemies.Knotter;
    static readonly PlayerTuning P = Tuning.Player;

    static Knotter Into(Night n, int car = 2) => n.World.AddEnemy(id => Knotter.Into(id, n.Train, car, K));

    static double Gap(Night n, int car)
    {
        var a = n.Train.Cars[car];
        var b = n.Train.Cars[car + 1];
        return a.FrontDistance - a.Length - b.FrontDistance;
    }

    [Fact]
    public void ItForcesTheCouplingApartAndTheTrainStillPullsAsOne()
    {
        var n = new Night(5, speed: 12);
        var k = Into(n);
        double before = Gap(n, 2);
        n.Run(K.CreepSeconds + K.ForceSeconds + 0.5);
        Assert.Equal(KnotterMode.Taut, k.Mode);
        Assert.InRange(Gap(n, 2), K.Gap - 0.05, K.Gap + 0.05);
        Assert.True(Gap(n, 2) > before + 3);
        // Still one rake: the cars behind it are the engine's.
        Assert.Equal(6, n.Train.Dynamics.Consist.Vehicles.Count);
        Assert.Single(n.Train.Rakes);
        // Wider than any roof jump carries.
        Assert.True(Gap(n, 2) > P.JumpGap + 1);
    }

    [Fact]
    public void CutTheCouplingWhileItCreepsAndItDropsOff()
    {
        var n = new Night(5, speed: 12);
        var k = Into(n);
        n.Run(1);
        n.Train.Uncouple(2);
        n.Run(0.2);
        Assert.True(k.Gone);
        Assert.Equal(0, n.Train.Vehicles[2].Knot);
    }

    [Fact]
    public void WalkingItsBackAtSpeedSlipsIntoItsCoilAndAFriendPullsThemUp()
    {
        var n = new Night(5, speed: 12);
        var k = Into(n);
        n.Run(K.CreepSeconds + K.ForceSeconds + 2);
        // On its back, mid-gap.
        var shape = n.Train.Frames[2].Shape;
        var g = Tuning.Train.Geometry;
        double l = shape.HalfLength;
        n.Crew[1] = new PlayerState
        {
            Parent = 2,
            Position = new Double3(g.PlateX, g.CouplerHeight, l + 1.0),
            Surface = Surface.Coupler,
            Health = P.Health,
            Kit = P.StartingKit,
            LineHint = n.Train.Cars[2].FrontDistance,
        };
        for (int i = 0; i < 20 * SimConstants.TickRate && k.Phase != SpinePhase.Grab; i++)
        {
            var s = n.Crew[1];
            n.Crew[1] = s with { Parent = 2, Position = new Double3(g.PlateX, g.CouplerHeight, l + 1.0), Surface = Surface.Coupler };
            n.Run(SimConstants.TickSeconds);
        }
        Assert.Equal(SpinePhase.Grab, k.Phase);
        Assert.Equal(1, k.Holding);
        // A friend at the gap's end, Use held at them: pulled up.
        n.Crew[2] = n.Crew[1] with { Position = new Double3(g.PlateX, g.CouplerHeight, l + 0.2), Flags = 0 };
        n.Run(0.4, id => id == 2 ? new PlayerIntent { Buttons = PlayerButtons.Use } : default);
        Assert.NotEqual(SpinePhase.Grab, k.Phase);
        Assert.True(n.Crew[1].Alive);
        n.AssertFair();
    }

    [Fact]
    public void LeftInItsCoilTheyrePulledUnder()
    {
        var n = new Night(5, speed: 12);
        var k = Into(n);
        n.Run(K.CreepSeconds + K.ForceSeconds + 2);
        var g = Tuning.Train.Geometry;
        double l = n.Train.Frames[2].Shape.HalfLength;
        n.Crew[1] = new PlayerState
        {
            Parent = 2,
            Position = new Double3(g.PlateX, g.CouplerHeight, l + 1.0),
            Surface = Surface.Coupler,
            Health = P.Health,
            Kit = P.StartingKit,
            LineHint = n.Train.Cars[2].FrontDistance,
        };
        for (int i = 0; i < 30 * SimConstants.TickRate && n.Crew[1].Alive; i++)
        {
            var s = n.Crew[1];
            if (s.Alive && !s.Has(PlayerFlags.Held))
                n.Crew[1] = s with { Parent = 2, Position = new Double3(g.PlateX, g.CouplerHeight, l + 1.0), Surface = Surface.Coupler };
            n.Run(SimConstants.TickSeconds);
        }
        Assert.False(n.Crew[1].Alive);
        Assert.Equal(DeathCause.PulledUnder, n.Crew[1].Death);
        n.AssertFair();
    }

    [Fact]
    public void BlowsLandOnlyAtAStandAndKilledTheCarsStandUncoupled()
    {
        var n = new Night(5, speed: 12);
        var k = Into(n);
        n.Run(K.CreepSeconds + K.ForceSeconds + 1);
        double l = n.Train.Frames[2].Shape.HalfLength;
        // From the roof's end, facing it.
        // On its back by the car's end (safe at a stand), the swing down at it.
        PlayerState Swinger()
        {
            var g = Tuning.Train.Geometry;
            var s = new PlayerState
            {
                Parent = 2,
                Position = new Double3(g.PlateX, g.CouplerHeight, l + 0.8),
                Surface = Surface.Coupler,
                Health = P.Health,
                Kit = P.StartingKit,
                LineHint = n.Train.Cars[2].FrontDistance,
            };
            var me = PlayerMotor.WorldPosition(s, n.Train);
            var d = n.Train.Frames[2].ToWorld(k.Local) - me;
            return s with { Yaw = DMath.Atan2(-d.X, -d.Z), Pitch = -0.5 };
        }
        n.Crew[1] = Swinger();
        double health = k.Health;
        n.Run(Tuning.Enemies.Melee.SwingSeconds + 0.1, id => new PlayerIntent { Actions = PlayerActions.Swing });
        Assert.Equal(health, k.Health);
        // Stopped: slack, and it can be killed.
        n.Train.Dynamics.Velocity = 0;
        n.Run(0.5);
        Assert.Equal(KnotterMode.Slack, k.Mode);
        for (int i = 0; i < 14 && !k.Gone; i++)
        {
            n.Crew[1] = Swinger();
            n.Run(Tuning.Enemies.Melee.SwingSeconds + 0.05, id => new PlayerIntent { Actions = PlayerActions.Swing });
        }
        Assert.True(k.Gone);
        Assert.Equal(2, n.Train.Rakes.Count);
        Assert.Equal(0, n.Train.Vehicles[2].Knot);
    }
}
