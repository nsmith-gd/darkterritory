using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The night hears the band (ARCHITECTURE §8 note 711; the director, 14 Oct 2026: "let's get the band calming the gaunt and
/// stopping the moose"): a crewmate playing near the Gaunt is talking to it; near the Moose, it stops to listen.
/// </summary>
public class BandListenerTests
{
    static readonly EnemyTuning E = Tuning.Enemies;
    static readonly PlayerTuning P = Tuning.Player;

    sealed class Rig
    {
        public readonly World World;
        public readonly EnemyContext Ctx;

        public Rig()
        {
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 5, 1)), new RailLine(new LineDefinition("t", [new TrackSegment(10_000)])), 2_000);
            World = new World(train, Tuning.Combat);
            Ctx = new EnemyContext { Tuning = E, World = World };
        }

        /// <summary>The crew, each on the ground at x (m off the line's origin, z along it), playing or not.</summary>
        public void Crew(params (int Id, Double3 At, bool Playing)[] crew)
        {
            Ctx.Crew.Clear();
            foreach (var (id, at, playing) in crew)
            {
                var s = new PlayerState { Parent = PlayerState.World, Position = at, Surface = Surface.Ground, Health = P.Health };
                if (playing)
                    s.Flags |= PlayerFlags.Playing;
                Ctx.Crew.Add((new PlayerSnapshot((byte)id, s), new PlayerIntent()));
            }
        }

        /// <summary>On the ground beside car 2, <paramref name="lateral"/> m off the line.</summary>
        public Double3 Beside(double lateral) => World.Train.Frames[2].ToWorld(new Double3(lateral, 0, 0)) with { Y = 0 };

        public void Run(Enemy e, double seconds)
        {
            for (int i = 0; i < seconds * SimConstants.TickRate && !e.Gone; i++)
            {
                World.Tick++;
                e.Step(Ctx);
            }
        }
    }

    [Fact]
    public void PlayingNearTheGauntIsTalkingToItAndItCalms()
    {
        foreach (bool playing in new[] { true, false })
        {
            var r = new Rig();
            // Its waker silent beside it, a crewmate playing within its hearing.
            r.Crew((1, new Double3(0, 0, 0), false), (2, new Double3(E.Gaunt.ListenRadius - 1, 0, 0), playing));
            var g = r.World.AddEnemy(id => Gaunt.Asleep(id, new Double3(0, 0, 1.2), E.Gaunt));
            g.Restore(SpinePhase.Commit, 5, E.Gaunt.Health, Enemy.Loose, new Double3(0, 0, 1.2), 0, 0, 0, extra: 1, extra2: E.Gaunt.AttackAt);
            r.Run(g, E.Gaunt.SilenceSeconds + 0.1);
            if (playing)
            {
                // Calmed a step, and it stops attacking.
                Assert.Equal(E.Gaunt.AttackAt - 1, g.Anger);
                Assert.Equal(SpinePhase.Telegraph, g.Phase);
            }
            else
                Assert.True(g.Anger >= E.Gaunt.AttackAt, "silence calmed it");
        }
    }

    [Fact]
    public void ThePlayingMustBeInItsHearing()
    {
        var r = new Rig();
        r.Crew((1, new Double3(0, 0, 0), false), (2, new Double3(E.Gaunt.ListenRadius + 3, 0, 0), true));
        var g = r.World.AddEnemy(id => Gaunt.Asleep(id, new Double3(0, 0, 1.2), E.Gaunt));
        g.Restore(SpinePhase.Telegraph, 5, E.Gaunt.Health, Enemy.Loose, new Double3(0, 0, 1.2), 0, 0, 0, extra: 1, extra2: E.Gaunt.AttackAt - 1);
        r.Run(g, E.Gaunt.SilenceSeconds + 0.1);
        Assert.Equal(E.Gaunt.AttackAt, g.Anger);
    }

    [Fact]
    public void TheMooseStopsToListenEvenMidChargeAndSettles()
    {
        var r = new Rig();
        var side = r.Beside(30);
        var moose = r.World.AddEnemy(id => Moose.Grazing(id, side, r.World.Train.Dynamics.Distance, 0));
        // Crowded close by one crewmate: it's riled, after them, squaring up or charging.
        r.Crew((1, side + new Double3(0, 0, 8), false));
        r.Run(moose, 6);
        Assert.Equal(1, moose.Target);
        Assert.True(moose.Phase is SpinePhase.Alert or SpinePhase.Telegraph or SpinePhase.Commit, $"{moose.Phase}");
        // Someone strikes up within its hearing: it forgets them, stands listening, and its temper falls.
        var there = moose.WorldPosition(r.World.Train);
        r.Crew((1, side + new Double3(0, 0, 8), false), (2, there with { X = there.X + E.Moose.BandHears - 2, Y = 0 }, true));
        Assert.NotEqual(SpinePhase.Grab, moose.Phase);
        r.Run(moose, 1);
        Assert.Null(moose.Target);
        Assert.Equal(SpinePhase.Alert, moose.Phase);
        r.Run(moose, 100 / E.Moose.BandCalmPerSecond + 1);
        Assert.Equal(0, moose.Aggro);
        Assert.Null(moose.Target);
        // The music stops, and with nobody crowding it, it settles to grazing.
        r.Crew((1, side + new Double3(0, 0, 40), false));
        r.Run(moose, 10);
        Assert.Equal(SpinePhase.Dormant, moose.Phase);
        Assert.Null(moose.Target);
    }

    [Fact]
    public void OutOfItsHearingTheBandDoesntStopIt()
    {
        var r = new Rig();
        var side = r.Beside(30);
        var moose = r.World.AddEnemy(id => Moose.Grazing(id, side, r.World.Train.Dynamics.Distance, 0));
        r.Crew((1, side + new Double3(0, 0, 8), false), (2, side + new Double3(0, 0, -(E.Moose.BandHears + 5)), true));
        r.Run(moose, 6);
        Assert.Equal(1, moose.Target);
    }
}
