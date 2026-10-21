using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Stops;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Dread at the stops (queue #646, note 944; the director's playtest of 20 Oct evening, line 15: "You start safely collecting
/// loot and then suddenly it doesn't feel so safe. You've been in there just long enough"): the longer an outing afoot, the
/// nearer and more often the signs, then a scare, then a real threat brought due on the encounter clock, once an outing.
/// </summary>
public class DreadTests
{
    static readonly DirectorTuning D = Tuning.Enemies.Director;
    static readonly DreadTuning T = D.Afoot.Dread with { On = true };

    static EnemyTuning With(bool on) => Tuning.Enemies with
    {
        Director = D with { Afoot = D.Afoot with { Dread = D.Afoot.Dread with { On = on } } },
    };

    [Fact]
    public void ItShipsOff() => Assert.False(D.Afoot.Dread.On);

    [Fact]
    public void TheStagesComeInOrderAndTheSignsCloseIn()
    {
        Assert.True(T.ScareAt < T.ThreatAt);
        Assert.Equal(Dread.Stage.Signs, Dread.At(0, T));
        Assert.Equal(Dread.Stage.Scare, Dread.At(T.ScareAt, T));
        Assert.Equal(Dread.Stage.Threat, Dread.At(T.ThreatAt, T));
        Assert.Equal(1, Dread.SignEvery(0, T), 9);
        Assert.Equal(1, Dread.SignOut(0, T), 9);
        double lastEvery = 2, lastOut = 2;
        for (double s = 0; s <= T.ThreatAt + 60; s += 5)
        {
            double every = Dread.SignEvery(s, T), out_ = Dread.SignOut(s, T);
            Assert.True(every <= lastEvery + 1e-12 && out_ <= lastOut + 1e-12, $"at {s} s");
            (lastEvery, lastOut) = (every, out_);
        }
        Assert.Equal(T.ScareOut, Dread.SignOut(T.ScareAt, T), 9);
        Assert.True(T.ScareOut < T.SignOutAtScare, "the scare is nearer than the build's nearest");
    }

    /// <summary>A night stood at a stop with a warren, one crewmate kept on the ground 30 m off it toward the line, for <paramref name="seconds"/>.</summary>
    static (Director Director, List<Watcher> Shown) Outing(bool on, int seconds)
    {
        foreach (var tier in new[] { RouteTier.Frontier, RouteTier.DeadLines })
            for (ulong seed = 1; seed < 60; seed++)
            {
                var route = RouteGenerator.Generate(Tuning.Route, tier, seed);
                if (route.Features.FirstOrDefault(f => f.Stop?.Lairs.Any(l => l.Kind == LairKind.Warren) == true) is not { } stop)
                    continue;
                var n = new Night(4, speed: 0, route, enemies: With(on));
                var state = n.Train.Capture();
                n.Train.Restore(state with { Rakes = [state.Rakes[0] with { Distance = stop.Start + stop.Stop!.StopPoint.S, Velocity = 0 }] });
                n.Train.RefreshFrames();
                n.Crew[9] = PlayerMotor.SpawnInCab(n.Train, Tuning.Player);
                var warren = stop.Stop!.Lairs.First(l => l.Kind == LairKind.Warren);
                double hint = 0;
                var me = Run.Run.StopWorld(n.Train.Line, stop, warren.At + new Pt(0, -Math.Sign(warren.At.D) * 30));
                me = me with { Y = PlayerMotor.GroundAt(me, n.Train.Line, ref hint) };
                var shown = new List<Watcher>();
                for (int s = 0; s < seconds; s++)
                {
                    n.Run(1);
                    n.Crew[1] = new PlayerState { Parent = PlayerState.World, Position = me, Health = Tuning.Player.Health };
                    if (n.World.Watcher.Showing && (shown.Count == 0 || shown[^1].Seconds < n.World.Watcher.Seconds))
                        shown.Add(n.World.Watcher);
                }
                return (n.World.Director!, shown);
            }
        throw new InvalidOperationException("no route has a warren");
    }

    /// <summary>On, a long outing sees more signs than off, the late ones nearer, and one real threat brought due.</summary>
    [Fact]
    public void ALongOutingIsWatchedMoreClosely()
    {
        int seconds = (int)T.ThreatAt + 30;
        var (off, _) = Outing(on: false, seconds);
        var (on, _) = Outing(on: true, seconds);
        Assert.True(on.Signs.Count > off.Signs.Count, $"on {on.Signs.Count} signs, off {off.Signs.Count}");
        Assert.Equal(0, off.DreadThreats);
        Assert.Equal(1, on.DreadThreats);
        Assert.True(on.OutingSeconds >= T.ThreatAt);
        // The late signs are the near ones: the scare's.
        double early = on.Signs.Where(s => s.Tick * SimConstants.TickSeconds < T.ScareAt / 2).Select(s => s.Out).DefaultIfEmpty(0).Average();
        double late = on.Signs.Where(s => s.Tick * SimConstants.TickSeconds >= T.ScareAt).Select(s => s.Out).DefaultIfEmpty(0).Average();
        Assert.True(late > 0 && late < early, $"early {early:0.0} m, late {late:0.0} m");
    }

    /// <summary>Off, nothing about the signs changes: the same signs, at the same ticks, the same distances.</summary>
    [Fact]
    public void OffItIsNote327AsItWas()
    {
        var (a, _) = Outing(on: false, 120);
        var (b, _) = Outing(on: false, 120);
        Assert.Equal(a.Signs, b.Signs);
        Assert.Equal(0, a.DreadThreats);
    }

    [Fact]
    public void ItIsDeterministic()
    {
        var (a, _) = Outing(on: true, 200);
        var (b, _) = Outing(on: true, 200);
        Assert.Equal(a.Signs, b.Signs);
        Assert.Equal(a.DreadThreats, b.DreadThreats);
        Assert.Equal(a.Pacing, b.Pacing);
    }
}
