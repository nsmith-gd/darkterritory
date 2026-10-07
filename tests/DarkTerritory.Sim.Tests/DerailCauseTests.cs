using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// T121 playtest: "we appear to have derailed at a very low speed. Something doesn't seem to be functioning correctly with
/// derailment", and "if derailment happens people should know they took the corner too hard and by how much". A derailing
/// Switchman throws the points under a train at any speed it's moving, which at a crawl splits them instead (the engine's
/// wrenched and brought up short); over that it's off the rails, and the cause says at what speed.
/// </summary>
public class DerailCauseTests
{
    static (World World, TrainOnLine Train, int Branch) AtBlackwellJunction(double speed)
    {
        var line = LineGen.Routes.Generate(DataFile.FindContentRoot(), "frontier:7", 4).Build();
        int branch = Enumerable.Range(0, line.Branches.Count).OrderBy(i => Math.Abs(line.Branches[i].Toe - 6850)).First();
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 1)), line, line.Branches[branch].Toe - 600);
        var world = new World(train, Tuning.Combat);
        var quiet = Tuning.Enemies with { Director = Tuning.Enemies.Director with { GraceMinSeconds = 1e9, GraceMaxSeconds = 1e9 } };
        world.EnableEnemies(quiet, route: null, 1, crew: 2, authority: true);
        world.AddEnemy(i => Switchman.At(i, line.Branches[branch], quiet.Switchman, 2.6, derail: true));
        train.Dynamics.Velocity = speed;
        return (world, train, branch);
    }

    static void RunPast(World world, TrainOnLine train, int branch, double speed, PlayerState? gunner = null)
    {
        double toe = train.Line.Branches[branch].Toe;
        var g = gunner ?? default;
        for (int i = 0; i < 200 * SimConstants.TickRate && !world.Derailed && train.Dynamics.RearDistance < toe + 5; i++)
        {
            if (train.Dynamics.Velocity > 0.01)
                train.Dynamics.Velocity = speed;
            world.BeginTick();
            if (gunner is not null)
                world.CrewAct(ref g, default, 1);
            world.Step(new TrainControls { Reverser = 1 });
            if (train.Dynamics.Velocity < 0.01 && train.Dynamics.Distance > toe)
                break;
        }
    }

    [Fact]
    public void PointsThrownUnderACrawlingTrainSplitAndDoNotDerailIt()
    {
        double crawl = Tuning.Enemies.Switchman.DerailAbove - 1.5;
        var (world, train, branch) = AtBlackwellJunction(crawl);
        double before = train.Vehicles[0].Integrity;
        RunPast(world, train, branch, crawl);
        Assert.False(world.Derailed, world.DerailCause);
        Assert.True(train.Vehicles[0].Integrity < before, "the engine takes the split points");
    }

    [Fact]
    public void PointsThrownUnderAFastTrainDerailItAndSayHowFast()
    {
        double fast = Tuning.Enemies.Switchman.DerailAbove + 4;
        var (world, train, branch) = AtBlackwellJunction(fast);
        RunPast(world, train, branch, fast);
        Assert.True(world.Derailed);
        Assert.Matches(@"^the Switchman threw the points under it at \d+ km/h \(over \d+ km/h they throw a train off\)$", world.DerailCause);
    }

    /// <summary>Stood at the engine's forward cannon, facing the way it faces (T112).</summary>
    static PlayerState AtTheForwardGun(TrainOnLine train)
    {
        var mount = train.Frames[0].Shape.Gun!.Value;
        var s = PlayerMotor.SpawnOnRoof(train, 0, mount.Position.Z - mount.Facing.Z * 0.7, Tuning.Player);
        s.Yaw = mount.Facing.Z < 0 ? 0 : Math.PI;
        return s;
    }

    [Fact]
    public void ASwitchmanDerailmentNamesTheForwardGunnerNotTheThrottle()
    {
        // GDD v1.4 App. C.9's Switchman row (note 190): whether the forward cannon was crewed, and by whom.
        double fast = Tuning.Enemies.Switchman.DerailAbove + 4;
        var (world, train, branch) = AtBlackwellJunction(fast);
        world.Attribution.Drove(2);
        var gunner = AtTheForwardGun(train);
        Assert.Equal(0, Combat.Guns.MannedGun(gunner, train, Tuning.Combat.Guns));
        RunPast(world, train, branch, fast, gunner);
        Assert.True(world.Derailed);
        Assert.Equal(1, world.DerailActor);
        Assert.Equal("Forward cannon crewed by {actor}.", world.DerailAction);
        Assert.Contains("they throw a train off). Forward cannon crewed by Crew 1. Recovery not scheduled.", IncidentLog.CauseCard(world));
    }

    [Fact]
    public void ASwitchmanDerailmentWithNobodyOnTheForwardGunSaysSo()
    {
        double fast = Tuning.Enemies.Switchman.DerailAbove + 4;
        var (world, train, branch) = AtBlackwellJunction(fast);
        world.Attribution.Drove(2);
        RunPast(world, train, branch, fast);
        Assert.True(world.Derailed);
        Assert.Equal(-1, world.DerailActor);
        Assert.Equal("Forward cannon not crewed.", world.DerailAction);
        Assert.DoesNotContain("Throttle", IncidentLog.CauseCard(world));
        // The derailment's record is the run's; the Switchman's PUNISH writes no second one.
        Assert.Empty(world.Attribution.Of(IncidentKind.Points));
    }

    [Fact]
    public void PointsSplitUnderACrawlingTrainAreRecordedWithTheForwardGun()
    {
        double crawl = Tuning.Enemies.Switchman.DerailAbove - 1.5;
        var (world, train, branch) = AtBlackwellJunction(crawl);
        RunPast(world, train, branch, crawl);
        var points = Assert.Single(world.Attribution.Of(IncidentKind.Points));
        Assert.Equal("Split the Switchman's points under the engine", points.What);
        Assert.Equal("Forward cannon not crewed.", points.Action);
    }
}
