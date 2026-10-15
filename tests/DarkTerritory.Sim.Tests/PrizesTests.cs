using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Stops;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The night's prizes (queue #452; ARCHITECTURE §8 note 718; the director, 14 Oct 2026: "I want players to eventually
/// understand that certain objects are more valuable than others ... as they play the game more often, they learn what those
/// objects are"; comet cargo "needs to be at actual stops and needs to be extra valuable loot"; team-killable creatures "can
/// sometimes leave trophies"). A comet fragment lies off the line in the lamp's sight on most nights (note 755), one at most; one aboard draws as the comet's
/// freight does and pays at the gate; every find of a kind is worth the same; the clerk's tally names the best finds with
/// their worth and the crewmate who brought the most aboard; a team kill leaves a trophy at its chance, and it pays.
/// </summary>
public class PrizesTests
{
    static readonly LootTuning L = DataFile.Load<LootTuning>(Path.Combine(DataFile.FindContentRoot(), LootTuning.File));
    static readonly PlayerTuning P = Tuning.Player;

    static double PerCar(RouteTier tier) => Tuning.Run.Economy.PerCar[StopLoot.TierKey(tier)];

    // ---- comet fragments at the stops.

    /// <summary>Note 718's fragments, each stop's chance (loot.json <c>comet.perNight</c> 0, the off setting of note 755's).</summary>
    static readonly LootTuning PerStop = L with { Comet = L.Comet! with { PerNight = 0 } };

    [Fact]
    public void WithPerNightOffCometFragmentsLieAtSomeStopsAtTheirTiersChance()
    {
        // Seeded per stop, the same every time; each stop's chance by tier (deeper likelier), so some nights have one and
        // some don't; one or a few, each beside something whose things lie out (never a crane's bay or a hiding spot).
        var L = PerStop;
        double lastShare = -1;
        foreach (var tier in new[] { RouteTier.Local, RouteTier.Frontier, RouteTier.DeadLines, RouteTier.DeepTerritory })
        {
            int stops = 0, holding = 0, nightsWith = 0;
            const int nights = 60;
            for (ulong seed = 1; seed <= nights; seed++)
            {
                var route = RouteGenerator.Generate(Tuning.Route, tier, seed);
                bool any = false;
                for (int i = 0; i < route.Features.Count; i++)
                {
                    if (route.Features[i].Stop is not { } stop)
                        continue;
                    stops++;
                    var at = StopLoot.Comets(L, stop, route.Seed, i, tier, _ => true);
                    Assert.Equal(at, StopLoot.Comets(L, stop, route.Seed, i, tier, _ => true));
                    Assert.True(at.Count <= L.Comet!.Count[^1]);
                    Assert.Equal(at.Count, at.Distinct().Count());
                    Assert.All(at, c => Assert.NotEqual(ContainerKind.CraneBay, stop.Containers[c].Kind));
                    // Nothing lying out, nowhere to put one.
                    Assert.Empty(StopLoot.Comets(L, stop, route.Seed, i, tier, _ => false));
                    if (at.Count > 0)
                        holding++;
                    any |= at.Count > 0;
                }
                nightsWith += any ? 1 : 0;
            }
            double chance = L.Comet!.Chance[StopLoot.TierKey(tier)], share = holding / (double)stops;
            // Binomial over a few hundred stops: well inside four standard deviations.
            double sd = Math.Sqrt(chance * (1 - chance) / stops);
            Assert.InRange(share, chance - 4 * sd - 0.01, chance + 4 * sd + 0.01);
            Assert.True(nightsWith > 0 && nightsWith < nights, $"{tier}: {nightsWith} of {nights} nights with a fragment");
            Assert.True(chance > lastShare, $"{tier} isn't likelier than the tier before");
            lastShare = chance;
        }
    }

    [Fact]
    public void TonightHoldsAFragmentAtTheTiersNightlyChance()
    {
        // Note 755 (the audit's R6): the night's roll, from its seed alone, the same every time; most frontier nights, fewer
        // local ones, more deep ones.
        double last = -1;
        foreach (var tier in new[] { RouteTier.Local, RouteTier.Frontier, RouteTier.DeadLines, RouteTier.DeepTerritory })
        {
            const int nights = 800;
            int with = 0;
            for (ulong seed = 1; seed <= nights; seed++)
            {
                bool has = StopLoot.CometNight(L, seed, tier) is not null;
                Assert.Equal(has, StopLoot.CometNight(L, seed, tier) is not null);
                with += has ? 1 : 0;
            }
            double chance = L.Comet!.NightChance[StopLoot.TierKey(tier)], share = with / (double)nights;
            double sd = Math.Sqrt(chance * (1 - chance) / nights);
            Assert.InRange(share, chance - 4 * sd, chance + 4 * sd);
            Assert.True(chance > last, $"{tier} isn't likelier than the tier before");
            last = chance;
        }
        Assert.True(L.Comet!.NightChance[StopLoot.TierKey(RouteTier.Frontier)] >= 0.5, "most frontier nights");
        // Off (perNight 0), the night rolls nothing: each stop rolls its own.
        Assert.Null(StopLoot.CometNight(PerStop, 1, RouteTier.Frontier));
    }

    [Fact]
    public void TheNightsOneFragmentLiesOffTheLineInTheLampsSight()
    {
        // Note 755: at most one a night (perNight 1), on every night the roll says, at one stop; offLine metres out from the
        // main line, on the ground, clear of the buildings; and (but for a stop with nowhere sighted) where the headlamp coming
        // in has it in its cone with nothing between: no building, and on a generated line no land.
        double near = L.Comet!.OffLine[0], far = L.Comet.OffLine[^1];
        int nights = 0, sighted = 0;
        string content = DataFile.FindContentRoot();
        for (int seed = 1; seed <= 8; seed++)
        {
            var route = LineGen.Routes.Generate(content, $"frontier:{seed}", 3);
            Assert.NotNull(route.Plan);
            var world = Night(route, 0, L);
            var run = world.Run!;
            var holding = Enumerable.Range(0, run.Stops.Count).Where(k => run.CometsAt(k).Count > 0).ToList();
            Assert.True(holding.Count <= 1);
            Assert.Equal(StopLoot.CometNight(L, route.Seed, route.Tier) is not null && run.Stops.Count > 0, holding.Count == 1);
            if (holding.Count == 0)
                continue;
            nights++;
            int k = holding[0];
            var p = Assert.Single(run.CometsAt(k));
            Assert.InRange(Math.Abs(p.D), near, far);
            Assert.DoesNotContain(run.Stops[k].Stop!.Buildings, b => Within(p, b, 1));
            sighted += run.CometInSight(k, 0) ? 1 : 0;
            // Stocked, it lies there: that far from the line, on the land.
            run.Stock(world.Bodies, k);
            var b = Assert.Single(world.Bodies.All, Run.Run.IsComet);
            var at = b.Pbd.Particles[0].Position;
            double hint = run.Stops[k].Start + p.S;
            var (_, along) = world.Train.Line.Nearest(at, ref hint);
            var t = world.Train.Line.Sample(along);
            double off = ((at - t.Position) with { Y = 0 }).Length;
            Assert.InRange(off, near - 1, far + 1);
            Assert.Equal(PlayerMotor.GroundAt(at, world.Train.Line, ref hint) + L.Radius, at.Y, 3);
        }
        Assert.True(nights >= 3, $"{nights} of 8 frontier nights with a fragment");
        Assert.True(sighted >= nights - 1, $"{sighted} of {nights} in the lamp's sight");
    }

    /// <summary>Whether a point in a stop's rail frame is inside a building's footprint, <paramref name="pad"/> wider.</summary>
    static bool Within(Pt p, StopBuilding b, double pad)
    {
        double ds = p.S - b.Centre.S, dd = p.D - b.Centre.D, c = Math.Cos(b.Yaw), s = Math.Sin(b.Yaw);
        return Math.Abs(ds * c + dd * s) <= b.Length / 2 + pad && Math.Abs(-ds * s + dd * c) <= b.Width / 2 + pad;
    }

    [Fact]
    public void AFragmentPaysOnlyIfTheNightIsDelivered()
    {
        // Brought aboard and stowed, then the night lost: nothing paid, no prize named.
        var (world, k) = AtAStopWithAComet();
        var run = world.Run!;
        run.Stock(world.Bodies, k);
        var fragment = world.Bodies.All.First(Run.Run.IsComet);
        Carry(world, fragment, 1);
        PutDown(world, fragment);
        Assert.True(fragment.Banked);
        run.Mirror(RunPhase.Arrived, RunEnd.Derailed, run.Seconds, -1, false, [], scavenged: run.Scavenged);
        var report = run.Tally(world, [PlayerMotor.SpawnInCab(world.Train, P)]);
        Assert.Equal(0, report.Scavenged);
        Assert.Empty(report.Prizes);
    }

    [Fact]
    public void AFragmentLiesOutAtItsStopAndIsWorthMoreThanAnyFindOrACarLoad()
    {
        var (world, k) = AtAStopWithAComet();
        var run = world.Run!;
        run.Stock(world.Bodies, k);
        var fragments = world.Bodies.All.Where(Run.Run.IsComet).ToList();
        Assert.Equal(run.CometsAt(k).Count, fragments.Count);
        Assert.NotEmpty(fragments);
        double perCar = PerCar(RouteTier.Frontier);
        foreach (var b in fragments)
        {
            var f = run.FindOf(b)!.Value;
            Assert.Equal(L.Comet!.Item, f.Item);
            Assert.Equal(L.WorthOf(L.Comet.Item, perCar), f.Value);
            Assert.Equal("Comet fragment", run.FindName(b));
            // Out on the ground at the stop, near what it lies beside (a crew sees it from the line).
            Assert.Equal(PlayerState.World, b.Parent);
        }
        double best = L.Worth.Where(w => w.Key != L.Comet!.Item).Max(w => w.Value);
        Assert.True(L.Worth[L.Comet!.Item] >= 2, "a fragment is worth at least two car-loads (note 755)");
        Assert.True(L.Worth[L.Comet.Item] > 5 * best, "a fragment is worth far more than any other find");
        Assert.True(L.Worth[L.Comet.Item] > Tuning.Run.Economy.Rate(CargoKind.Comet) * 0.5);
    }

    [Fact]
    public void AFragmentAboardTurnsTheCometsDirectorWeightsOnAndPaysAtTheGate()
    {
        var (world, k) = AtAStopWithAComet();
        var run = world.Run!;
        run.Stock(world.Bodies, k);
        var fragment = world.Bodies.All.First(Run.Run.IsComet);
        Assert.DoesNotContain(CargoKind.Comet, Director.Aboard(world));
        Assert.False(Run.Run.CometAboard(world));
        // Carried aboard by crewmate 1 and put down in a car.
        Carry(world, fragment, 1);
        Assert.True(Run.Run.CometAboard(world), "in the hands of someone aboard");
        PutDown(world, fragment);
        Assert.True(fragment.Banked);
        Assert.Contains(CargoKind.Comet, Director.Aboard(world));
        double worth = run.FindOf(fragment)!.Value.Value;
        Assert.Equal(worth, run.Scavenged, 6);
        var report = Delivered(world);
        Assert.Equal(Math.Round(worth), report.Scavenged);
        Assert.Contains(report.Prizes, p => p.Item == L.Comet!.Item && p.Worth == worth);
        // Thrown off the train again, it's not aboard.
        fragment.Parent = PlayerState.World;
        Assert.DoesNotContain(CargoKind.Comet, Director.Aboard(world));
    }

    // ---- value you learn.

    [Fact]
    public void EveryFindOfAKindIsWorthTheSameAndNoneIsWorthNothing()
    {
        foreach (var tier in new[] { RouteTier.Local, RouteTier.DeepTerritory })
        {
            var worth = new Dictionary<string, double>();
            for (ulong seed = 1; seed <= 12; seed++)
            {
                var route = RouteGenerator.Generate(Tuning.Route, tier, seed);
                for (int i = 0; i < route.Features.Count; i++)
                    if (route.Features[i].Stop is { } stop)
                        foreach (var f in StopLoot.Village(L, stop, route.Seed, i, PerCar(tier)))
                        {
                            if (worth.TryGetValue(f.Item, out double was))
                                Assert.Equal(was, f.Value);
                            worth[f.Item] = f.Value;
                            Assert.True(f.Value >= LootTuning.LeastWorth);
                        }
            }
            // Rarer is worth more: a pocket watch is worth several boxes of candles.
            Assert.True(worth["pocketWatch"] >= 4 * worth["candles"]);
        }
        // Every find a container can hold, and every trophy, has a worth and a name of its own.
        foreach (var item in L.Kinds.Values.SelectMany(k => k.Items).Distinct())
        {
            Assert.True(L.Worth.ContainsKey(item), $"{item} has no worth");
            Assert.True(L.Items.ContainsKey(item), $"{item} has no name");
        }
        foreach (var trophy in L.Trophies.Values)
            Assert.True(L.Items.ContainsKey(trophy.Item), $"{trophy.Item} has no name");
    }

    [Fact]
    public void TheTallyNamesTheBestFindsWithTheirWorthAndWhoBroughtTheMostAboard()
    {
        var (world, k) = AtAHalt();
        var run = world.Run!;
        run.Stock(world.Bodies, k, searched: true);
        world.Names[1] = "Dave";
        world.Names[2] = "Priya";
        // Dave brings in the two worthiest, Priya the rest.
        var finds = world.Bodies.All.Where(b => b.Kind == BodyKind.Loot && !Run.Run.IsComet(b)).OrderByDescending(b => run.FindOf(b)!.Value.Value).ToList();
        Assert.True(finds.Count >= 3);
        for (int i = 0; i < finds.Count; i++)
        {
            Carry(world, finds[i], i < 2 ? 1 : 2);
            PutDown(world, finds[i], z: -3 + i * 0.4);
        }
        Assert.Equal(finds.Count, run.Stowed.Count);
        Assert.Equal(run.Stowed.Count, run.Finders.Count);
        var report = Delivered(world);
        double dave = finds.Take(2).Sum(b => run.FindOf(b)!.Value.Value), priya = finds.Skip(2).Sum(b => run.FindOf(b)!.Value.Value);
        // The best kinds, the worthiest first, each at what one paid.
        var kinds = finds.Select(b => run.FindOf(b)!.Value).GroupBy(f => f.Item).Select(g => (g.Key, g.First().Value)).ToList();
        Assert.Equal(Math.Min(L.Tally.Best, kinds.Count), report.Prizes.Count);
        Assert.Equal(kinds.Max(x => x.Value), report.Prizes[0].Worth);
        for (int i = 1; i < report.Prizes.Count; i++)
            Assert.True(report.Prizes[i - 1].Worth >= report.Prizes[i].Worth);
        Assert.All(report.Prizes, p => Assert.Equal(L.Name(p.Item), p.Name));
        Assert.Equal(dave >= priya ? "Dave" : "Priya", report.Finder);
        Assert.Equal(Math.Max(dave, priya), report.FinderWorth);
        // The clerk reads them, flat, after the salvage, and keeps every line he had.
        var lines = Radio.Tally(report);
        var top = report.Prizes[0];
        Assert.Contains(lines, l => l == (top.Count > 1 ? $"Finds: {char.ToLowerInvariant(top.Name[0])}{top.Name[1..]}, {top.Count} at {top.Worth:0}."
            : $"Find: {char.ToLowerInvariant(top.Name[0])}{top.Name[1..]}, {top.Worth:0}."));
        Assert.Contains($"Light fingers: {report.Finder}, {report.FinderWorth:0}.", lines);
        int salvage = lines.FindIndex(l => l.StartsWith("Salvage:", StringComparison.Ordinal));
        Assert.True(salvage >= 0 && salvage < lines.FindIndex(l => l.StartsWith("Find", StringComparison.Ordinal)));
        Assert.StartsWith("Net:", lines[^1]);
        // A night with nothing brought home says nothing of finds.
        Assert.DoesNotContain(Radio.Tally(report with { Prizes = [], Finder = "", FinderWorth = 0 }), l => l.StartsWith("Find", StringComparison.Ordinal) || l.StartsWith("Light", StringComparison.Ordinal));
    }

    // ---- trophies, sometimes.

    [Fact]
    public void ATeamKillLeavesATrophyAtItsChanceAndItPays()
    {
        // Rolled from the run's seed and the creature's id: the same every time, at about the tuned chance.
        var (world, _) = AtAHalt();
        var run = world.Run!;
        var gaunt = L.Trophies["gaunt"];
        Assert.InRange(gaunt.Chance, 0.05, 0.95);
        int left = 0;
        const int kills = 600;
        for (int id = 1; id <= kills; id++)
        {
            bool once = run.TrophyFor(EnemyKind.Gaunt, id) is not null;
            Assert.Equal(once, run.TrophyFor(EnemyKind.Gaunt, id) is not null);
            left += once ? 1 : 0;
        }
        double sd = Math.Sqrt(gaunt.Chance * (1 - gaunt.Chance) / kills);
        Assert.InRange(left / (double)kills, gaunt.Chance - 4 * sd, gaunt.Chance + 4 * sd);
        // The Gannet's head, every time (note 340); the Choir's ghosts leave nothing.
        Assert.All(Enumerable.Range(1, 20), id => Assert.NotNull(run.TrophyFor(EnemyKind.Gannet, id)));
        Assert.All(Enumerable.Range(1, 20), id => Assert.Null(run.TrophyFor(EnemyKind.Choir, id)));

        // Two crewmates beat a Grumbler to death: its jaw is left where it fell, when its roll says so, and it pays.
        foreach (bool sure in new[] { true, false })
        {
            var loot = L with { Trophies = new(L.Trophies) { ["grumbler"] = L.Trophies["grumbler"] with { Chance = sure ? 1 : 0 } } };
            var (w, _) = AtAHalt(loot);
            var ctx = new EnemyContext { Tuning = Tuning.Enemies, World = w };
            var at = w.Train.Frames[0].ToWorld(new Double3(4, 0, 0));
            foreach (var (id, x) in new[] { (1, 1.2), (2, -1.2) })
                ctx.Crew.Add((new PlayerSnapshot((byte)id, new PlayerState { Parent = PlayerState.World, Position = at + new Double3(x, 0, 0), Surface = Surface.Ground, Health = P.Health }), new PlayerIntent()));
            var g = w.AddEnemy(id => Grumbler.OnCrates(id, at, -1, Tuning.Enemies.Grumbler));
            for (int n = 0; n < 60 && !g.Gone; n++)
            {
                g.Struck(ctx, 1 + n % 2, Tuning.Enemies.Melee.Crowbar);
                for (int i = 0; i < 4 && !g.Gone; i++)
                {
                    w.Tick++;
                    g.Step(ctx);
                }
            }
            Assert.Contains(EnemyKind.Grumbler, w.Slain);
            var jaw = w.Bodies.All.Where(b => b.Owner == Run.Run.TrophyOwner(EnemyKind.Grumbler)).ToList();
            if (!sure)
            {
                Assert.Empty(jaw);
                continue;
            }
            var body = Assert.Single(jaw);
            Assert.Equal(BodyKind.Loot, body.Kind);
            Assert.True((body.Centre - at).Length < 1.5, "left where it fell");
            var find = w.Run!.FindOf(body)!.Value;
            Assert.Equal("grumblerJaw", find.Item);
            Assert.Equal(L.Trophies["grumbler"].PerCar * PerCar(RouteTier.Local), find.Value, 6);
            Carry(w, body, 1);
            PutDown(w, body);
            Assert.Equal(find.Value, w.Run.Scavenged, 6);
        }
    }

    // ---- the rig.

    /// <summary>A host night standing at a village halt with finds, the stops' loot tuning <paramref name="loot"/>.</summary>
    static (World World, int Stop) AtAHalt(LootTuning? loot = null)
    {
        for (ulong seed = 1; seed < 100; seed++)
        {
            var route = RouteGenerator.Generate(Tuning.Route, RouteTier.Local, seed);
            if (route.Of(FeatureKind.Village).FirstOrDefault(v => v.Stop!.Containers.Count(c => c.Zone == StopZone.Village) > 3) is not { } halt)
                continue;
            var world = Night(route, (halt.Start + halt.End) / 2, loot ?? L);
            return (world, world.Run!.Stops.ToList().IndexOf(halt));
        }
        throw new InvalidOperationException("no local route has a village halt with finds");
    }

    /// <summary>A host night on a frontier line standing at a stop with a comet fragment.</summary>
    static (World World, int Stop) AtAStopWithAComet()
    {
        for (ulong seed = 1; seed < 200; seed++)
        {
            var route = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, seed);
            var world = Night(route, 0, L);
            var run = world.Run!;
            for (int k = 0; k < run.Stops.Count; k++)
                if (run.CometsAt(k).Count > 0)
                    return (Night(route, (run.Stops[k].Start + run.Stops[k].End) / 2, L), k);
        }
        throw new InvalidOperationException("no frontier night has a comet fragment");
    }

    static World Night(Route.Route route, double at, LootTuning loot)
    {
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 3, 0)), route.Build(), Math.Max(at, 700), Tuning.Boiler);
        var world = new World(train, Tuning.Combat);
        world.EnableBodies();
        world.EnableRun(Tuning.Run, route, 600, authority: true, FacilityTests.F, loot);
        return world;
    }

    /// <summary>In crewmate <paramref name="who"/>'s hands, aboard the first cargo car, for a tick of the run.</summary>
    static void Carry(World world, Body b, int who)
    {
        var car = world.Train.Vehicles.First(v => v.Kind == VehicleKind.Cargo);
        var room = world.Train.Frames[car.Id].Shape.Interior!.Value;
        b.Parent = car.Id;
        b.Pbd.Particles[0].Position = b.Pbd.Particles[0].Previous = new Double3(0, room.Min.Y + 1.1, 0);
        b.Carrier = who;
        b.Claimed = true;
        world.StepRun([]);
    }

    /// <summary>Let go of on the first cargo car's floor and left there until it's stowed.</summary>
    static void PutDown(World world, Body b, double z = 0)
    {
        var car = world.Train.Vehicles.First(v => v.Kind == VehicleKind.Cargo);
        var room = world.Train.Frames[car.Id].Shape.Interior!.Value;
        b.Carrier = -1;
        b.Parent = car.Id;
        b.Pbd.Particles[0].Position = b.Pbd.Particles[0].Previous = new Double3(0, room.Min.Y + 0.3, z);
        for (int t = 0; t < (L.SettleSeconds + 0.5) * SimConstants.TickRate; t++)
        {
            world.Train.Dynamics.Velocity = 0;
            world.BeginTick();
            world.Step(new TrainControls { Reverser = 1, Brake = 1 });
            world.StepBodies([]);
            world.StepRun([]);
        }
    }

    /// <summary>The night delivered, and the clerk's report of it.</summary>
    static RunReport Delivered(World world)
    {
        var run = world.Run!;
        run.Mirror(RunPhase.Arrived, RunEnd.Delivered, run.Seconds, -1, false, [], scavenged: run.Scavenged);
        return run.Tally(world, [PlayerMotor.SpawnInCab(world.Train, P)]);
    }
}
