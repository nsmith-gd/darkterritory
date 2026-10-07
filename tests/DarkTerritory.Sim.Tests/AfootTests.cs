using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Stops;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 327 (GDD App. F.3, the director, 7 Oct 2026: "when they leave, there is this presence of threat at all times"): the
/// crew afoot off the train are watched. Signs (eyes at the lamp's edge, and a sound) come from what lives at the stop, and
/// the night's pressure builds faster while they're out.
/// </summary>
public class AfootTests
{
    static readonly AfootTuning A = Tuning.Enemies.Director.Afoot;

    /// <summary>A night stopped at the first generated stop with a site of <paramref name="kind"/>, the driver in the cab.</summary>
    static (Night Night, RouteFeature Stop, StopLair Lair) At(LairKind kind)
    {
        foreach (var tier in new[] { RouteTier.Frontier, RouteTier.DeadLines, RouteTier.DeepTerritory })
            for (ulong seed = 1; seed < 60; seed++)
            {
                var route = RouteGenerator.Generate(Tuning.Route, tier, seed);
                if (route.Features.FirstOrDefault(f => f.Stop?.Lairs.Any(l => l.Kind == kind) == true) is not { } stop)
                    continue;
                var n = new Night(4, speed: 0, route);
                var state = n.Train.Capture();
                n.Train.Restore(state with { Rakes = [state.Rakes[0] with { Distance = stop.Start + stop.Stop!.StopPoint.S, Velocity = 0 }] });
                n.Train.RefreshFrames();
                n.Crew[9] = PlayerMotor.SpawnInCab(n.Train, Tuning.Player);
                return (n, stop, stop.Stop!.Lairs.First(l => l.Kind == kind));
            }
        throw new InvalidOperationException($"no route has a {kind}");
    }

    /// <summary>Crewmate <paramref name="id"/> on the ground at a point in the stop's rail frame.</summary>
    static Double3 Stand(Night n, RouteFeature stop, int id, Pt p)
    {
        double hint = 0;
        var at = Run.Run.StopWorld(n.Train.Line, stop, p);
        at = at with { Y = PlayerMotor.GroundAt(at, n.Train.Line, ref hint) };
        n.Crew[id] = new PlayerState { Parent = PlayerState.World, Position = at, Health = Tuning.Player.Health };
        return at;
    }

    static Double3 Flat(Double3 v) => v with { Y = 0 };

    [Fact]
    public void OffTheTrainSomethingThatLivesHereIsSeenWatchingAndHeardFromWhereItIs()
    {
        var (n, stop, warren) = At(LairKind.Warren);
        // Out by the warren, 30 m off it toward the line: well off the train.
        var me = Stand(n, stop, 1, warren.At + new Pt(0, -Math.Sign(warren.At.D) * 30));
        Assert.True(Director.FromTrain(n.Train, me) > A.FromTrainM);
        var d = n.World.Director!;
        var shown = new List<Watcher>();
        for (int s = 0; s < 90; s++)
        {
            n.Run(1);
            // Keep them there: this is about what they see, not what comes for them.
            n.Crew[1] = n.Crew[1] with { Position = me, Health = Tuning.Player.Health };
            if (n.World.Watcher.Showing && (shown.Count == 0 || shown[^1].Seconds < n.World.Watcher.Seconds))
                shown.Add(n.World.Watcher);
        }
        Assert.InRange(d.Signs.Count, 90 / A.SignEvery[1], 90 / A.SignEvery[0] + 1);
        Assert.InRange((d.Signs[0].Tick) * SimConstants.TickSeconds, A.FirstSign - 1, A.FirstSign + 2);
        Assert.True(d.AfootSeconds >= 80, $"{d.AfootSeconds} s afoot");
        // Honest: every sign is of something whose site is at this stop, toward it, at the lamp's edge, at its eye height.
        var site = Run.Run.StopWorld(n.Train.Line, stop, warren.At);
        var lives = stop.Stop!.Lairs.Select(l => Director.Lives(l.Kind)).ToHashSet();
        Assert.All(d.Signs, s => Assert.True(s.FromSite && lives.Contains(s.Kind) && s.Player == 1, $"{s.Kind} from a site: {s.FromSite}"));
        Assert.NotEmpty(shown);
        foreach (var w in shown.Where(w => w.Kind == EnemyKind.Ribbit))
        {
            var to = Flat(w.At - me);
            Assert.InRange(to.Length, A.SignOut[0] * 0.5 - 0.1, A.SignOut[1] + 0.1);
            double angle = Math.Acos(Math.Clamp(Double3.Dot(to.Normalized, Flat(site - me).Normalized), -1, 1)) * 180 / Math.PI;
            Assert.True(angle <= A.SignSpread + 1, $"{angle:0}° off the warren's bearing");
            double hint = 0;
            Assert.Equal(A.SignHeight["ribbits"], w.At.Y - PlayerMotor.GroundAt(w.At, n.Train.Line, ref hint), 2);
        }
        // A sign is never a spawn: no Ribbit was put down for it (the director's own spawns are logged apart).
        Assert.True(n.World.ActiveEnemies.OfType<Ribbit>().Select(r => r.Pack).Distinct().Count() <= d.Log.Count(l => l.Kind == EnemyKind.Ribbit) + d.Hunts.Count);
    }

    [Fact]
    public void AboardOrBesideTheTrainNothingWatchesAndThePressureBuildsAsBefore()
    {
        var (aboard, _, _) = At(LairKind.Warren);
        var (afoot, stop, warren) = At(LairKind.Warren);
        aboard.Crew[1] = PlayerMotor.SpawnInCab(aboard.Train, Tuning.Player);
        var me = Stand(afoot, stop, 1, warren.At + new Pt(0, -Math.Sign(warren.At.D) * 30));
        double grace = afoot.World.Director!.Grace;
        aboard.Run(grace + 2);
        afoot.Run(grace + 2);
        Assert.Empty(aboard.World.Director!.Signs);
        Assert.Equal(0, aboard.World.Director.AfootShare);
        Assert.Equal(0.5, afoot.World.Director!.AfootShare, 6);
        // The same night, the same second: the crew afoot draw it on by perSecond × their share (in the model's other terms).
        var (a, b) = (aboard.World.Director.Terms, afoot.World.Director.Terms);
        Assert.True(b.Rate > a.Rate, $"afoot {b.Rate:0.000}/s vs aboard {a.Rate:0.000}/s");
        Assert.Equal(0, a.Afoot);
        Assert.Equal(A.PerSecond * 0.5, b.Afoot, 9);
        _ = me;

        // Off: as it was.
        var off = Tuning.Enemies with { Director = Tuning.Enemies.Director with { Afoot = A with { On = false } } };
        var route = afoot.World.Route!;
        var n = new Night(4, 0, route, enemies: off);
        var state = n.Train.Capture();
        n.Train.Restore(state with { Rakes = [state.Rakes[0] with { Distance = stop.Start + stop.Stop!.StopPoint.S, Velocity = 0 }] });
        n.Train.RefreshFrames();
        n.Crew[9] = PlayerMotor.SpawnInCab(n.Train, Tuning.Player);
        Stand(n, stop, 1, warren.At + new Pt(0, -Math.Sign(warren.At.D) * 30));
        n.Run(grace + 2);
        Assert.Empty(n.World.Director!.Signs);
        Assert.Equal(a.Rate, n.World.Director.Terms.Rate, 6);
    }

    [Fact]
    public void TheSignIsOnTheWorldRecordForEveryMachine()
    {
        var (n, _, _) = At(LairKind.Warren);
        n.World.Watcher = new Watcher(2.5, EnemyKind.Gaunt, new Double3(12.25, 3.5, -40.75), 1);
        var controls = default(TrainControls);
        var records = WorldRecords.Capture(n.World, controls, []);
        var client = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 1)), n.Train.Line, 0));
        WorldRecords.Apply(records, client, ref controls, []);
        Assert.True(client.Watcher.Showing);
        Assert.Equal(EnemyKind.Gaunt, client.Watcher.Kind);
        Assert.Equal(1, client.Watcher.Player);
        Assert.Equal(2.5, client.Watcher.Seconds, 3);
        Assert.True((client.Watcher.At - n.World.Watcher.At).Length < 0.01);
    }
}
