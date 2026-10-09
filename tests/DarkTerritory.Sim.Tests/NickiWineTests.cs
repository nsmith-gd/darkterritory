using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Towns;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Nicki's wine (the director, 9 Oct 2026: "when Nicki offers wine to the players they should get extra health for the next run
/// if they take it"; ARCHITECTURE §8 note 488). Use held by her, empty-handed, is a glass: tuning/towns.json <c>wine.health</c>
/// over full for the night that's about to set out, once a night; the host's to pour.
/// </summary>
public class NickiWineTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TownContent Towns = TownContent.Load(Content)!;
    static readonly PlayerTuning P = Tuning.Player;

    /// <summary>A departure town with Nicki's party in it (every town has one here), its world and her.</summary>
    static (World World, Townsperson Nicki) AtTheParty()
    {
        var content = Towns with { Tuning = Towns.Tuning with { Nicki = 1 } };
        for (int seed = 1; seed < 40; seed++)
        {
            var route = Routes.Generate(Content, $"frontier:{seed}", 6);
            double gate = route.GateOr(RouteTuning.Load(Content).YardLength);
            var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), route.Build(), gate - 8), Tuning.Combat);
            world.EnableRun(Tuning.Run, route, gate, authority: true);
            world.EnableTown(content, route, gate, []);
            if (world.Nicki is { } nicki)
                return (world, nicki);
        }
        throw new InvalidOperationException("no town of 40 had an open house for her party");
    }

    static PlayerState By(World world, Double3 at) => PlayerMotor.SpawnOnGround(at, world.Train.Line, world.Train.Dynamics.Distance, P);

    /// <summary>Use held <paramref name="seconds"/> by crewmate <paramref name="id"/>.</summary>
    static void Hold(World world, ref PlayerState s, int id, double seconds)
    {
        var use = new PlayerIntent { Buttons = PlayerButtons.Use };
        for (double t = 0; t < seconds; t += SimConstants.TickSeconds)
            world.CrewAct(ref s, use, id);
        world.CrewAct(ref s, default, id);
    }

    [Fact]
    public void AGlassIsHealthOverFullOnceANightAndOnlyByHer()
    {
        var (world, nicki) = AtTheParty();
        var wine = Towns.Tuning.Wine;
        var feet = world.Town!.Feet(nicki);
        var near = By(world, feet + new Double3(0.8, 0, 0));
        Assert.Equal(P.Health, near.Health);
        Assert.True(world.WineInReach(near, 1));
        // A tap's a word with her, not a glass.
        Hold(world, ref near, 1, wine.HoldSeconds / 2);
        Assert.Equal(P.Health, near.Health);
        // Held: a glass, over full.
        Hold(world, ref near, 1, wine.HoldSeconds + 0.1);
        Assert.Equal(P.Health + wine.Health, near.Health);
        Assert.Contains(1, world.Toasted);
        Assert.False(world.WineInReach(near, 1));
        // Once a night: knocked back down, another hold pours nothing.
        near.Health = 60;
        Hold(world, ref near, 1, wine.HoldSeconds + 0.1);
        Assert.Equal(60, near.Health);
        // Too far from her: nothing.
        var far = By(world, feet + new Double3(wine.Reach + 3, 0, 0));
        Hold(world, ref far, 2, wine.HoldSeconds + 0.1);
        Assert.Equal(P.Health, far.Health);
        Assert.DoesNotContain(2, world.Toasted);
        Assert.True(wine.Health > 0 && wine.HoldSeconds > 0.3, "a glass is a deliberate hold, and worth having");
    }
}
