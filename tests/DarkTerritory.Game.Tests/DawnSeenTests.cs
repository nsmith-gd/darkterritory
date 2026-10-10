using System.Numerics;
using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// Dawn seen and felt coming (note 599; docs/design/creatures/wakers.md §2-§4; the director: "the tension before it is really,
/// really important"): the cold line behind the train over the stir and where it lies, the lanterns knocked by its thuds,
/// a Waker heaving up out of the ground, and the town's guns that bear on one stopped at the walls.
/// </summary>
public class DawnSeenTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TrainTuning T = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
    static readonly PlayerTuning P = DataFile.Load<PlayerTuning>(Path.Combine(Content, PlayerTuning.File));
    static readonly CombatTuning C = DataFile.Load<CombatTuning>(Path.Combine(Content, CombatTuning.File));
    static readonly EnemyTuning E = DataFile.Load<EnemyTuning>(Path.Combine(Content, EnemyTuning.File));
    static readonly WakersTuning W = E.Wakers;

    /// <summary>A standing train out on a Frontier night that dawns <paramref name="dawnIn"/> seconds from now, quiet but for the Wakers.</summary>
    static (World World, PlayerState Player) Night(double dawnIn)
    {
        var routeTuning = RouteTuning.Load(Content);
        var route = RouteGenerator.Generate(routeTuning, RouteTier.Frontier, 7) with { DawnSeconds = dawnIn };
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 6, 1)), route.Build(), 9_000);
        var world = new World(train, C);
        world.EnableRun(DataFile.Load<RunTuning>(Path.Combine(Content, RunTuning.File)), route, 600, authority: true);
        world.EnableEnemies(E with { Director = E.Director with { GraceMinSeconds = 1e9, GraceMaxSeconds = 1e9 } }, null, 1, crew: 1, authority: true);
        return (world, PlayerMotor.SpawnInCab(train, P));
    }

    static void Step(World world, ref PlayerState player, double seconds)
    {
        for (int i = 0; i < seconds * SimConstants.TickRate && world.Run?.Over != true; i++)
        {
            world.BeginTick();
            world.CrewAct(ref player, default, 1);
            world.Step(new TrainControls { Reverser = 1, Brake = 1 });
            world.StepRun([player]);
        }
    }

    [Fact]
    public void TheColdLineComesUpBehindTheTrainOverTheStirThenTowardTheWakers()
    {
        Assert.True(W.Enabled && W.StirSeconds > 0);
        var (world, player) = Night(W.StirSeconds + 30);
        var rear = world.Train.Frames[world.Train.Dynamics.Consist.Vehicles[^1].Id];
        // Before the stir: nothing; no thuds, the lanterns still.
        Assert.Equal(0, DawnStir.Sky(world));
        Assert.Equal(0, DawnStir.LampSway(world, 3.7));
        Step(world, ref player, 30 + W.StirSeconds / 2);
        // Halfway through it: half up, behind the train, and the lanterns knocked about by the thuds.
        Assert.InRange(DawnStir.Sky(world), 0.4, 0.6);
        Assert.True(Double3.Dot(DawnStir.SkyFrom(world, world.Train.Frames).Normalized, rear.Back) > 0.95, "the line isn't behind the train");
        Assert.Contains(Enumerable.Range(0, 70).Select(k => Math.Abs(DawnStir.LampSway(world, k * 0.1))), a => a > 0.01);
        Assert.All(Enumerable.Range(0, 70).Select(k => Math.Abs(DawnStir.LampSway(world, k * 0.1))), a => Assert.True(a < 0.3, "swung half over"));
        // At dawn they're up: the light is where the lead one is (behind, off the line it rose on).
        Step(world, ref player, W.StirSeconds / 2 + W.RiseSeconds / 2);
        Assert.Equal(1, DawnStir.Sky(world));
        var lead = world.ActiveEnemies.OfType<Waker>().Single(w => w.Lead);
        var toward = (lead.WorldPosition(world.Train) - rear.Origin) with { Y = 0 };
        Assert.True(Double3.Dot(DawnStir.SkyFrom(world, world.Train.Frames), toward.Normalized) > 0.999);
    }

    [Fact]
    public void AWakerHeavesUpOutOfTheGroundOverItsRise()
    {
        Assert.Equal(WakerRise.Depth, WakerRise.Sink(SpinePhase.Dormant, 0, W.RiseSeconds));
        Assert.Equal(WakerRise.Depth, WakerRise.Sink(SpinePhase.Telegraph, 0, W.RiseSeconds), 6);
        Assert.Equal(0, WakerRise.Sink(SpinePhase.Telegraph, W.RiseSeconds, W.RiseSeconds), 6);
        Assert.Equal(0, WakerRise.Sink(SpinePhase.Commit, 0, W.RiseSeconds));
        // Out of the ground quick and slowing as it stands: over half up by a third of the way through.
        Assert.True(WakerRise.Up(SpinePhase.Telegraph, W.RiseSeconds / 3, W.RiseSeconds) > 0.5);
        // The seconds since it began to rise run on through the chase, for the earth it threw up to come down.
        Assert.Equal(W.RiseSeconds + 2, WakerRise.Since(SpinePhase.Commit, 2, W.RiseSeconds));
        Assert.Equal(-1, WakerRise.Since(SpinePhase.Dormant, 0, W.RiseSeconds));
    }

    [Fact]
    public void TheGunsThatBearOnTheWallsStandOnTheGatehouseAndTheTowersInside()
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(3_000)]));
        double gate = 2_500;
        var guns = GreyboxScene.WallGuns(line, gate, Double3.Zero);
        Assert.Equal(6, guns.Count);
        foreach (var g in guns)
        {
            // The line runs off from the origin along −Z (heading 0).
            double along = -g.Z;
            Assert.True(along >= gate - 1, $"a gun outside the gate, at {along:0} m");
            Assert.True(g.Y > Sim.Run.Fortresses.TowerHeight - 0.5, "a gun below the towers' tops");
        }
    }
}
