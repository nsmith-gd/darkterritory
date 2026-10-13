using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Stops;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The outside creatures start where the stops' layouts say they live (level-design H.2; GDD B.6; ARCHITECTURE §8 note 309):
/// Ribbits out of their warren, the Gaunt asleep in its roost, a child's call from beyond the built edge, Followers only on
/// their grounds. The director still says when.
/// </summary>
public class SpawnSiteTests
{
    static readonly EnemyTuning E = Tuning.Enemies;

    /// <summary>A night standing at the first generated stop that has a site of <paramref name="kind"/>, and that site.</summary>
    static (Night Night, RouteFeature Stop, StopLair Lair) At(LairKind kind)
    {
        foreach (var tier in new[] { RouteTier.DeadLines, RouteTier.DeepTerritory, RouteTier.Frontier })
            for (ulong seed = 1; seed < 60; seed++)
            {
                var route = RouteGenerator.Generate(Tuning.Route, tier, seed);
                if (route.Features.FirstOrDefault(f => f.Stop?.Lairs.Any(l => l.Kind == kind) == true) is not { } stop)
                    continue;
                var n = new Night(4, speed: 0, route);
                var state = n.Train.Capture();
                n.Train.Restore(state with { Rakes = [state.Rakes[0] with { Distance = stop.Start + stop.Stop!.StopPoint.S, Velocity = 0 }] });
                n.Train.RefreshFrames();
                return (n, stop, stop.Stop!.Lairs.First(l => l.Kind == kind));
            }
        throw new InvalidOperationException($"no route has a {kind}");
    }

    /// <summary>Stands crewmate <paramref name="id"/> on the ground at a point in the stop's rail frame, and runs a tick so the world sees them.</summary>
    static Double3 Stand(Night n, RouteFeature stop, int id, Pt p)
    {
        double hint = 0;
        var at = Run.Run.StopWorld(n.Train.Line, stop, p);
        at = at with { Y = PlayerMotor.GroundAt(at, n.Train.Line, ref hint) };
        n.Crew[id] = new PlayerState { Parent = PlayerState.World, Position = at, Health = Tuning.Player.Health };
        n.Run(SimConstants.TickSeconds);
        return at;
    }

    static double Flat(Double3 a, Double3 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));

    static Double3 SiteOf(Night n, RouteFeature stop, StopLair lair) => Run.Run.StopWorld(n.Train.Line, stop, lair.At);

    [Fact]
    public void RibbitsComeOutOfTheWarrenNearestTheCrewAndNotFromAStopWhoseWarrensAreFar()
    {
        var (n, stop, warren) = At(LairKind.Warren);
        // 30 m off the warren, toward the line.
        Stand(n, stop, 1, warren.At + new Pt(0, -Math.Sign(warren.At.D) * 30));
        Stand(n, stop, 2, warren.At + new Pt(2, -Math.Sign(warren.At.D) * 30));
        var rule = Spawns.For(EnemyKind.Ribbit)!;
        int before = n.World.ActiveEnemies.Count;
        Assert.True(rule.Spawn(new SpawnContext(n.World, E, n.World.Director!)));
        var pack = n.World.ActiveEnemies.Skip(before).OfType<Ribbit>().ToList();
        Assert.NotEmpty(pack);
        var centre = (n.Crew[1].Position + n.Crew[2].Position) * 0.5;
        var nearest = stop.Stop!.Lairs.Where(l => l.Kind == LairKind.Warren).Select(l => SiteOf(n, stop, l)).OrderBy(a => Flat(a, centre)).First();
        Assert.All(pack, r => Assert.True(Flat(r.Local, nearest) < 4, $"a Ribbit {Flat(r.Local, nearest):0.0} m from its warren"));

        // A crew standing nowhere near any warren: the stop has none to send.
        var far = At(LairKind.Warren);
        var away = far.Stop.Stop!.Lairs.Where(l => l.Kind == LairKind.Warren).ToList();
        // A spot in the stop's zone, out on the ground, further than any warren's reach.
        var spot = Enumerable.Range(0, 40).SelectMany(i => new[] { 25.0, -25, 50, -50, 80, -80 }.Select(d => new Pt(far.Stop.Stop.ZoneLength * i / 40, d)))
            .First(p => away.All(l => Pt.Distance(l.At, p) > E.Sites.WarrenReach + 5));
        Stand(far.Night, far.Stop, 1, spot);
        Stand(far.Night, far.Stop, 2, spot + new Pt(2, 0));
        Assert.False(rule.Spawn(new SpawnContext(far.Night.World, E, far.Night.World.Director!)));
    }

    [Fact]
    public void TheGauntSleepsInItsRoostAndAChildCallsFromItsCall()
    {
        int seen = 0;
        foreach (var (kind, enemy) in new[] { (LairKind.GauntRoost, EnemyKind.Gaunt), (LairKind.SootCall, EnemyKind.SootChildren) })
        {
            var (n, stop, lair) = At(kind);
            var site = SiteOf(n, stop, lair);
            // 30 m short of it, toward the line: the crew out at the edge of things.
            var toward = lair.At + new Pt(0, -Math.Sign(lair.At.D) * 30);
            Stand(n, stop, 1, toward);
            Stand(n, stop, 2, toward + new Pt(3, 0));
            int before = n.World.ActiveEnemies.Count;
            Assert.True(Spawns.For(enemy)!.Spawn(new SpawnContext(n.World, E, n.World.Director!)));
            var e = n.World.ActiveEnemies.Skip(before).Single();
            Assert.True(Flat(e.Local, site) < 0.5, $"{enemy} {Flat(e.Local, site):0.0} m from its {kind}");
            seen++;
        }
        Assert.Equal(2, seen);
    }

    [Fact]
    public void AFollowerTakesOnlyACrewmateStandingOnItsGround()
    {
        var (n, stop, ground) = At(LairKind.FollowerGround);
        var rule = Spawns.For(EnemyKind.Follower)!;
        // Off every ground: nobody to take.
        var grounds = stop.Stop!.Lairs.Where(l => l.Kind == LairKind.FollowerGround).ToList();
        var off = Enumerable.Range(0, 40).SelectMany(i => new[] { 25.0, -25, 50, -50 }.Select(d => new Pt(stop.Stop.ZoneLength * i / 40, d)))
            .First(p => grounds.All(g => Pt.Distance(g.At, p) > g.Radius + E.Sites.GroundMargin + 2));
        Stand(n, stop, 1, off);
        Assert.False(rule.Spawn(new SpawnContext(n.World, E, n.World.Director!)));
        // In the middle of one: they're taken.
        Stand(n, stop, 1, ground.At);
        int before = n.World.ActiveEnemies.Count;
        Assert.True(rule.Spawn(new SpawnContext(n.World, E, n.World.Director!)));
        Assert.IsType<Follower>(n.World.ActiveEnemies.Skip(before).Single());
    }

    [Fact]
    public void TheWhistlerCarriesItsVictimToTheStopsNestAndThroughNoWall()
    {
        // Every generated stop with a nest, the train stood at it, a crewmate taken at the gap behind car one (note 314).
        int atSite = 0, stops = 0;
        foreach (var tier in new[] { RouteTier.Frontier, RouteTier.DeadLines, RouteTier.DeepTerritory })
            for (ulong seed = 1; seed <= 12; seed++)
            {
                var route = RouteGenerator.Generate(Tuning.Route, tier, seed);
                foreach (var stop in route.Features.Where(f => f.Stop?.Lairs.Any(l => l.Kind == LairKind.WhistlerNest) == true))
                {
                    var n = new Night(4, speed: 0, route);
                    var state = n.Train.Capture();
                    n.Train.Restore(state with { Rakes = [state.Rakes[0] with { Distance = stop.Start + stop.Stop!.StopPoint.S, Velocity = 0 }] });
                    n.Train.RefreshFrames();
                    // The stops' houses stand as walls, as a run's start has them (World.EnableRun).
                    n.Train.Walls = Run.StopWalls.Of(route, n.Train.Line);
                    var gap = n.Train.Frames[1].ToWorld(CrewSense.GapLocal(n.Train, 1));
                    var right = (n.Train.Frames[1].Right with { Y = 0 }).Normalized;
                    var sites = CreatureSites.Of(n.World, LairKind.WhistlerNest, E.Sites.Around).Select(x => x.At).ToList();
                    var (nest, run) = Whistler.NestSite(n.Train, gap, right, 1, E.Whistler, sites);
                    stops++;
                    var site = sites.OrderBy(x => Flat(x, gap)).First();
                    if (Flat(nest, site) < 0.5)
                    {
                        atSite++;
                        Assert.InRange(run, E.Whistler.NestMinDistance, E.Whistler.NestSiteReach);
                    }
                    else
                        Assert.True(run <= E.Whistler.NestDistance + 1e-6, $"{tier}:{seed} at {stop.Start:0}: {run:0} m out and not to its nest");
                    // Whichever it was, the run there goes round every house, not through one.
                    for (double x = 0; x <= run; x += 1)
                    {
                        var at = gap + ((nest - gap) with { Y = 0 }).Normalized * x;
                        Assert.DoesNotContain(n.Train.Walls!.Near(at), w =>
                            Math.Abs(w.ToLocal(at).X) <= w.HalfLength && Math.Abs(w.ToLocal(at).Z) <= w.HalfWidth);
                    }
                }
            }
        Assert.True(stops > 0 && atSite * 10 >= stops * 9, $"to the stop's own nest at {atSite} of {stops} stops");
    }

    [Fact]
    public void AHandLaidRoutesStopsStillSendThemFromOutInTheDark()
    {
        // No layouts, no sites: a Ribbit pack comes out round the ground crew, as before (note 309's fallback).
        var n = new Night(4, speed: 0);
        var beside = n.Train.Frames[2].ToWorld(new Double3(4, 0, 0));
        n.Crew[1] = new PlayerState { Parent = PlayerState.World, Position = beside, Health = Tuning.Player.Health };
        n.Crew[2] = n.Crew[1] with { Position = beside + new Double3(0, 0, 2) };
        n.Run(SimConstants.TickSeconds);
        var ctx = new SpawnContext(n.World, E, n.World.Director!);
        Assert.Empty(ctx.Sites(LairKind.Warren));
        Assert.True(Spawns.For(EnemyKind.Ribbit)!.Spawn(ctx));
    }
}
