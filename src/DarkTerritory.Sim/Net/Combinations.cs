using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Route;

namespace DarkTerritory.Sim.Net;

/// <summary>Mirror of content/tuning/balance.json <c>combinations</c>: GDD §34's combination fairness sweep. Field docs live there.</summary>
public sealed record CombinationTuning
{
    /// <summary>The default grid's routes (tier:seed): each combination is run on every one.</summary>
    public IReadOnlyList<string> Routes { get; init; } = ["frontier:7"];
    /// <summary>The default grid's crew sizes: each combination is run with every one, on every route.</summary>
    public IReadOnlyList<int> Crews { get; init; } = [4];
    public int Cars { get; init; } = 6;
    public double Seconds { get; init; } = 90;
    /// <summary>Seeds per (route, crew) cell by default.</summary>
    public int Seeds { get; init; } = 1;
    /// <summary>The nightly's grid (<c>--wide</c>): more routes, crew sizes and seeds.</summary>
    public CombinationGrid Wide { get; init; } = new(["frontier:7"], [2, 8], 2);
    /// <summary>The default grid, as a <see cref="CombinationGrid"/>.</summary>
    public CombinationGrid Grid => new(Routes, Crews, Seeds);
    public int TripleSample { get; init; } = 136;
    public double InsistEvery { get; init; } = 10;
    public double LeadM { get; init; } = 20;
    /// <summary>A conflict pair with a grade (<c>--conflicts</c>, note 1032): the steep stretch lies within this far of the start (m).</summary>
    public double GradeWithinM { get; init; } = 1200;
    public double StopSeconds { get; init; } = 150;
    /// <summary>A combination with a kind that comes to a fast train (<see cref="Combinations.Fast"/>, note 1036): the express driver's speed (m/s).</summary>
    public double ExpressSpeed { get; init; } = 21;
    /// <summary>... the night's length (s), and how far ahead of its start the line has no tunnel (m).</summary>
    public double FastSeconds { get; init; } = 150;
    public double FastClearM { get; init; } = 3000;
    /// <summary>At a coaling tower (note 1036), the tender's coal at the start, a share of its capacity.</summary>
    public double CoalTenderShare { get; init; } = 0.5;
    /// <summary>A pair whose condition is the stop or its loading, or with a creature of the loading (note 1036): the night's length (s).</summary>
    public double LoadingSeconds { get; init; } = 360;
    /// <summary>Routes (tier:seed) a conflict pair is run on instead when its stop is on none of the grid's (note 1036).</summary>
    public IReadOnlyList<string> SiteRoutes { get; init; } = [];
    public double ApproachM { get; init; } = 350;
    public double UnwinnableLost { get; init; } = 0.5;
    public double TrivialCargoLoss { get; init; } = 0.02;
    public IReadOnlyList<HazardSet> HazardSets { get; init; } = [HazardSet.Clear];
    /// <summary>The look-out's errand on the sweep's nights (note 212).</summary>
    public Bots.LookTuning Look { get; init; } = new();
}

/// <summary>Which routes, crew sizes and seeds a sweep runs each combination over (note 204). A (route, crew) pair is a cell.</summary>
public sealed record CombinationGrid(IReadOnlyList<string> Routes, IReadOnlyList<int> Crews, int Seeds);

/// <summary>One night of the sweep: the kinds insisted on, what the line takes away, the seed, and where the train starts,
/// on which route (tier:seed).</summary>
public sealed record CombinationNight(IReadOnlyList<EnemyKind> Kinds, HazardSet Hazards, int Seed, double StartM, IReadOnlyList<string> Unstaged,
    string Route = "");

/// <summary>Where a combination's nights start: out on the line, or short of a facility the crew stop and work at.</summary>
/// <param name="Coal">At a coaling tower: the tender's staged short enough of coal for the crew to stop and fill it
/// (<see cref="CombinationTuning.CoalTenderShare"/>; the bots coal only with a quarter of it to fill).</param>
/// <param name="Long">A kind in it stands the train (<see cref="Combinations.Stands"/>): the night runs <see cref="CombinationTuning.StopSeconds"/>.</param>
/// <param name="Fast">Run with the express driver (<see cref="CombinationTuning.ExpressSpeed"/>): a kind in it comes only to a fast train.</param>
public sealed record CombinationStage(double StartM, bool AtStop, IReadOnlyList<string> Unstaged, bool Fast = false, bool Coal = false)
{
    public bool Long { get; init; }
    /// <summary>At a stop for its loading (<see cref="CombinationTuning.LoadingSeconds"/>): the pair's condition is the stop, or a creature in it comes at the loading.</summary>
    public bool Loading { get; init; }
}

/// <summary>
/// One pair of the director's conflict table (App. B.1 contradiction seeding) as the sweep runs it (note 1032): the
/// creatures in it insisted on, and its conditions staged: <paramref name="AtStop"/> for a facility stop or its loading,
/// <paramref name="Grade"/> for a climb or fall ahead. The Choir is a creature here (insisted on, it gathers).
/// </summary>
/// <param name="Unknown">Sides that are neither a creature nor a condition the director knows: a table typo.</param>
public sealed record ConflictPair(string Name, IReadOnlyList<EnemyKind> Kinds, bool AtStop, bool Grade, IReadOnlyList<string> Unknown);

/// <summary>How one night of a combination went.</summary>
/// <param name="Lost">Counted lost: derailed, stranded, the crew wiped out, or <see cref="CombinationTuning.UnwinnableLost"/> of them dead.</param>
/// <param name="Engaged">The kinds that came on (telegraphed or further) during the night.</param>
/// <param name="Placed">The kinds sent at all (their spawn found somewhere to put them).</param>
public sealed record CombinationRun(int Seed, double StartM, string End, int Deaths, bool Derailed, bool Lost, int Grabs, int Punishes, int Rescues,
    double CargoLoss, IReadOnlyList<string> Engaged)
{
    public IReadOnlyList<string> Placed { get; init; } = [];
    /// <summary>Structures brought down (Tower Jaw's coaling tower or gantry, note 1036): a landing, though its spine records none.</summary>
    public int TowersDown { get; init; }
    /// <summary>The route (tier:seed) and crew size of the night's cell (note 204).</summary>
    public string Route { get; init; } = "";
    public int Crew { get; init; }
}

/// <summary>A combination's nights in one (route, crew) cell under one hazard set (note 204).</summary>
/// <param name="Unwinnable">Lost every night in the cell.</param>
public sealed record CombinationCell(string Route, int Crew, int Nights, int Lost, bool Unwinnable, int Grabs, int Punishes, int Deaths,
    IReadOnlyList<string> Engaged);

/// <summary>One (route, crew) cell over the whole sweep: its nights, how many were lost, and the combinations unwinnable there.</summary>
public sealed record CombinationGridCell(string Route, int Crew, int Nights, int Lost, IReadOnlyList<string> Unwinnable);

/// <summary>A combination against one hazard set, over its routes, crews and seeds, and the verdict.</summary>
/// <param name="Verdict">"unwinnable" (lost every night in some (route, crew) cell), "trivial" (everything came on and
/// nothing landed, every night in every cell),
/// "unplaced" (something's spawn never found anywhere to put it: no crane, no marsh ahead), "dormant" (something was put
/// there and never came on: lay in wait all night, the crew never giving it its chance), or "fair". Unplaced and dormant
/// aren't judged: the two never met.</param>
/// <param name="Cells">The nights by (route, crew), in the order they were run.</param>
public sealed record CombinationRow(IReadOnlyList<string> Kinds, string Hazards, string Verdict, string Detail, IReadOnlyList<string> Unexercised,
    IReadOnlyList<string> Unstaged, IReadOnlyList<CombinationCell> Cells, IReadOnlyList<CombinationRun> Runs);

/// <param name="ByCell">Each (route, crew) cell over every combination: nights, lost, and what was unwinnable there.</param>
public sealed record CombinationReport(int Size, int Combinations, int Nights, IReadOnlyDictionary<string, int> Verdicts,
    IReadOnlyList<string> Unwinnable, IReadOnlyList<string> Trivial, IReadOnlyDictionary<string, int> NeverExercised,
    IReadOnlyList<CombinationGridCell> ByCell, IReadOnlyList<CombinationRow> Rows, bool Pass);

/// <summary>
/// GDD §34 "Combination fairness: every pair and triple in the roster against every hazard set. Flag unwinnable or
/// trivially solved" (note 186). Each combination is insisted on (<see cref="World.Insist"/>: only those kinds, sent
/// whenever their spawn can place them) for a short night with bots on a generated line, under each hazard set, and judged.
/// It's a sieve, not a balance pass: bots aren't people, and a short night isn't a run. What it finds are the meetings bots
/// can't survive at all, and the ones where nothing ever lands.
/// </summary>
public static class Combinations
{
    /// <summary>GDD §21's roster: every creature the director or a condition can send (not the hazards, Sleepers and the Drift).</summary>
    public static IReadOnlyList<EnemyKind> Roster { get; } =
    [
        EnemyKind.TrackDoll, EnemyKind.CinderHound, EnemyKind.CarHugger, EnemyKind.Climber, EnemyKind.Dragger, EnemyKind.Whistler,
        EnemyKind.Stoker, EnemyKind.TippyToesie, EnemyKind.FireFlies, EnemyKind.Ribbit, EnemyKind.Gaunt, EnemyKind.Follower,
        EnemyKind.SootChildren, EnemyKind.Choir, EnemyKind.Passenger, EnemyKind.Grumbler, EnemyKind.Moose,
        EnemyKind.Gannet, EnemyKind.FreightBeetle,
    ];

    /// <summary>The conflict table's conditions (<see cref="Director"/>'s <c>Present</c>): a stop, its loading, a grade ahead.</summary>
    public static IReadOnlySet<string> Conditions { get; } = new HashSet<string> { "facilityStop", "facilityLoading", "grade" };

    /// <summary>
    /// App. B.10 "Contradiction quality: every seeded pair is solvable, hard, not impossible" (note 1032): the director's
    /// conflict table (enemies.json <c>director.conflicts</c>) as combinations, in table order, a pair once.
    /// </summary>
    public static IReadOnlyList<ConflictPair> Conflicts(IEnumerable<string[]> table)
    {
        var kinds = Enum.GetValues<EnemyKind>().ToDictionary(Director.Key, k => k);
        return [.. table.Where(p => p.Length == 2).DistinctBy(p => $"{p[0]}+{p[1]}").Select(p => new ConflictPair($"{p[0]}+{p[1]}",
            [.. p.Where(kinds.ContainsKey).Select(x => kinds[x])],
            p.Any(x => x is "facilityStop" or "facilityLoading"), p.Contains("grade"),
            [.. p.Where(x => !kinds.ContainsKey(x) && !Conditions.Contains(x))]))];
    }

    /// <summary>
    /// Every combination of <paramref name="size"/> from <paramref name="roster"/>, in roster order; with
    /// <paramref name="sample"/> &gt; 0, that many of them picked by <paramref name="seed"/> (the same pick every time).
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<EnemyKind>> Of(IReadOnlyList<EnemyKind> roster, int size, int sample = 0, ulong seed = 1)
    {
        var all = new List<IReadOnlyList<EnemyKind>>();
        void Pick(int from, List<EnemyKind> chosen)
        {
            if (chosen.Count == size)
            {
                all.Add([.. chosen]);
                return;
            }
            for (int i = from; i < roster.Count; i++)
            {
                chosen.Add(roster[i]);
                Pick(i + 1, chosen);
                chosen.RemoveAt(chosen.Count - 1);
            }
        }
        Pick(0, []);
        if (sample <= 0 || sample >= all.Count)
            return all;
        var rng = new Ballast.Pcg32(seed, 0xC0_4B1_7E5UL);
        var keyed = all.Select(c => (Key: rng.NextUInt(), c)).OrderBy(x => x.Key).Take(sample).Select(x => x.c);
        // Back in roster order, so a sample reads like the full table.
        return [.. keyed.OrderBy(c => all.IndexOf(c))];
    }

    /// <summary>
    /// The kinds that come at a stop (App. B.4, B.6, B.8): at a facility, or onto the crew on foot. A combination with one of
    /// these starts short of a facility the crew can work, so they're down on the ground for it.
    /// </summary>
    public static IReadOnlySet<EnemyKind> AtStops { get; } = new HashSet<EnemyKind>
    {
        EnemyKind.Whistler, EnemyKind.Ribbit, EnemyKind.Gaunt, EnemyKind.Follower, EnemyKind.SootChildren, EnemyKind.Passenger, EnemyKind.Grumbler,
        EnemyKind.Moose, EnemyKind.FreightBeetle,
    };

    /// <summary>
    /// The kinds that come only to a train run fast (note 1036): the Gannet (App. B.4: enemies.json gannet.arriveAbove 18 m/s
    /// for arriveSeconds 30, in open country). The sweep's driver cruises at 14 m/s, so on its nights the Gannet was never
    /// sent at all; a combination with one is run with the express driver (note 376: hot, no stops), from a stretch with no
    /// tunnel for <see cref="CombinationTuning.FastClearM"/> (in one, it peels off).
    /// </summary>
    /// <summary>
    /// The kinds that come only at some stops (note 1036; <see cref="Stage"/>): the Grumbler a crane's, Tower Jaw a coaling
    /// tower's or a crane's, the Freight Beetle one with loose freight, the Track Doll's loading a coaling tower's on the main.
    /// Unstaged on a route for want of one, the conflicts sweep runs them on a site route instead.
    /// </summary>
    public static IReadOnlySet<EnemyKind> AtSites { get; } = new HashSet<EnemyKind>
    {
        EnemyKind.Grumbler, EnemyKind.TowerJaw, EnemyKind.FreightBeetle, EnemyKind.TrackDoll,
    };

    /// <summary>
    /// The kinds the sweep insists on only when their own rule would send them (<see cref="World.InsistByRule"/>, note 1036):
    /// the Knotter boards a train above knotter.boardAbove, the Gannet comes over one fast. Their rule is the meeting.
    /// </summary>
    public static IReadOnlySet<EnemyKind> ByRule { get; } = new HashSet<EnemyKind> { EnemyKind.Knotter, EnemyKind.Gannet };

    /// <summary>
    /// The kinds that stand the train (note 1036): the Knotter's killed only at a stand, so the crew stop for each. A
    /// combination with one runs <see cref="CombinationTuning.StopSeconds"/>: on 90 s, two Knotters' stands left the train 9 m
    /// short of the Car Hugger's bridge at the end.
    /// </summary>
    public static IReadOnlySet<EnemyKind> Stands { get; } = new HashSet<EnemyKind> { EnemyKind.Knotter };

    /// <summary>The kinds met once they've picked someone (gone Alert), not only once they telegraph (note 1036): the Gannet's hang.</summary>
    public static IReadOnlySet<EnemyKind> MetAtAlert { get; } = new HashSet<EnemyKind> { EnemyKind.Gannet };

    /// <summary>The kinds that come at a stop's loading (note 1036): Tower Jaw at its structure, the Freight Beetle among the freight.</summary>
    public static IReadOnlySet<EnemyKind> AtLoading { get; } = new HashSet<EnemyKind> { EnemyKind.TowerJaw, EnemyKind.FreightBeetle };

    public static IReadOnlySet<EnemyKind> Fast { get; } = new HashSet<EnemyKind> { EnemyKind.Gannet };

    /// <summary>
    /// Where a combination's nights start (on <paramref name="route"/>, built as <paramref name="line"/>). With a kind that
    /// comes at a stop, and <paramref name="atStops"/>: <paramref name="approach"/> short of the first facility with a spur to work (one with a crane for the
    /// Grumbler, <paramref name="crane"/>). Otherwise the first place clear of the yard, stops and tunnels from which every kind
    /// that needs the line to place it can be placed (the Car Hugger's marsh or bridge ahead), <paramref name="lead"/> short. The kinds no place suits are returned as <c>Unstaged</c>.
    /// </summary>
    public static CombinationStage Stage(Route.Route route, Rail.RailLine line, IReadOnlyList<EnemyKind> kinds, EnemyTuning e, double yard, double lead,
        double approach = 350, Func<RouteFeature, IReadOnlyCollection<DarkTerritory.Sim.Run.ModuleKind>>? modules = null, bool atStops = true, bool stop = false,
        double gradeWithin = 0, double fastClear = 0)
    {
        // A conflict pair with a stop in it (note 1032) is staged at one whatever its creature.
        if (atStops && (stop || kinds.Any(AtStops.Contains)))
        {
            bool Spur(RouteFeature f) => line.Branches.Any(b => b.Kind == Rail.BranchKind.Spur && b.Toe >= f.Start - 50 && b.Toe <= f.End + 50);
            bool Coaling(RouteFeature f) => f.Facility == FacilityKind.CoalingTower;
            bool Has(RouteFeature f, DarkTerritory.Sim.Run.ModuleKind m) => modules?.Invoke(f).Contains(m) == true;
            bool Crane(RouteFeature f) => Has(f, DarkTerritory.Sim.Run.ModuleKind.Crane);
            bool Structure(RouteFeature f) => Coaling(f) || Crane(f);
            bool Crates(RouteFeature f) => Has(f, DarkTerritory.Sim.Run.ModuleKind.Crates);
            var stops = route.Of(FeatureKind.Facility).Where(f => Spur(f) || Coaling(f)).Where(f => f.Start - approach >= yard + YardClear).ToList();
            // What the stop must have for each kind that's there only at some (note 1036): the Grumbler a crane; Tower Jaw a
            // structure to gnaw, a coaling tower's leg or a crane's gantry; the Freight Beetle loose freight, a stop with crates;
            // the Track Doll a stop worked on the main line, where it can stand ahead: a coaling tower (a spur's loading is off
            // the main, and the doll comes only on it). The rest go to any stop with a spur.
            var needs = new List<(EnemyKind Kind, Func<RouteFeature, bool> Ok)>();
            if (kinds.Contains(EnemyKind.Grumbler) && modules is not null)
                needs.Add((EnemyKind.Grumbler, Crane));
            if (kinds.Contains(EnemyKind.TowerJaw))
                needs.Add((EnemyKind.TowerJaw, Structure));
            if (kinds.Contains(EnemyKind.FreightBeetle) && modules is not null)
                needs.Add((EnemyKind.FreightBeetle, Crates));
            if (kinds.Contains(EnemyKind.TrackDoll))
                needs.Add((EnemyKind.TrackDoll, Coaling));
            // A coaling tower only for a kind that asks for one; the rest want a yard to work.
            // With none that has all of it, the stop with a spur that has the most (deadLines:2's Switchyard for the Grumbler and
            // the Freight Beetle: crates, if no crane).
            var at = stops.FirstOrDefault(f => needs.All(n => n.Ok(f)) && (Spur(f) || needs.Count > 0))
                ?? stops.Where(Spur).OrderByDescending(f => needs.Count(n => n.Ok(f))).FirstOrDefault();
            if (at is not null)
            {
                var unstaged = needs.Where(n => !n.Ok(at)).Select(n => n.Kind.ToString()).ToList();
                unstaged.AddRange(kinds.Where(k => k is EnemyKind.CarHugger).Select(k => k.ToString()));
                // At the stop for its loading (note 1036): a crew of two was still down the Foundry's spur when 150 s ran out,
                // the loading 134-184 s in.
                bool loading = stop || kinds.Any(AtLoading.Contains);
                if (Coaling(at))
                    return new CombinationStage(Math.Round(at.Start - approach), true, unstaged, Coal: true) { Loading = loading };
                double toe = line.Branches.Where(b => b.Kind == Rail.BranchKind.Spur && b.Toe >= at.Start - 50 && b.Toe <= at.End + 50).Min(b => b.Toe);
                return new CombinationStage(Math.Round(toe - approach), true, unstaged) { Loading = loading };
            }
        }
        // Out on the line, what comes at a stop is sent at the crew as they board from the ballast at the start (the halt's
        // stop): unstaged, since it's no facility, and it may well lie in wait unmet.
        bool fast = kinds.Any(Fast.Contains);
        var (start, missing) = OnTheLine(route, line, kinds, e, yard, lead, gradeWithin, fast ? fastClear : 0);
        return new CombinationStage(start, false, [.. missing, .. kinds.Where(AtStops.Contains).Select(k => k.ToString())], fast) { Long = kinds.Any(Stands.Contains) };
    }

    /// <summary>The nearest a night starts past the yard's gate (m): a stop's approach, or the Car Hugger's lurk.</summary>
    const double YardClear = 100;

    static (double StartM, IReadOnlyList<string> Unstaged) OnTheLine(Route.Route route, Rail.RailLine line, IReadOnlyList<EnemyKind> kinds,
        EnemyTuning e, double yard, double lead, double gradeWithin = 0, double fastClear = 0)
    {
        bool Hugger(double s) => route.Features.Any(f => f.Kind is FeatureKind.Marsh or FeatureKind.Bridge
            && (f.Start + f.End) * 0.5 is var at && at >= s + e.CarHugger.LurkAheadMin + lead && at <= s + e.CarHugger.LurkAheadMax
            && Math.Abs(line.Sample(Rail.RailLine.MainPath, at).GradePercent) <= e.CarHugger.MaxGrade);
        bool Clear(double s) => !route.Features.Any(f => f.Kind is FeatureKind.Facility or FeatureKind.Tunnel or FeatureKind.Junction
            && s >= f.Start - 300 && s <= f.End + 100);
        var needs = new List<(EnemyKind Kind, Func<double, bool> Ok)>();
        if (kinds.Contains(EnemyKind.CarHugger))
            needs.Add((EnemyKind.CarHugger, Hugger));
        // A fast train's kind (note 1036): open country ahead for the express's run, no tunnel in it.
        if (fastClear > 0)
            foreach (var k in kinds.Where(Fast.Contains))
                needs.Add((k, s => !route.Features.Any(f => f.Kind == FeatureKind.Tunnel && f.End >= s && f.Start <= s + fastClear)));
        // A conflict pair's grade (note 1032): a stretch as steep as the director's "grade" reads within gradeWithin ahead.
        bool Steep(double s)
        {
            for (double at = s; at <= s + gradeWithin; at += 50)
                if (Math.Abs(line.Sample(Rail.RailLine.MainPath, at).GradePercent) >= e.Director.GradePercent)
                    return true;
            return false;
        }
        // The places to try: just far enough short of each thing a kind needs (so the train's soon on it from a stand), then
        // every 50 m of the line.
        // Note 1036: short of a marsh or bridge, as near the yard as YardClear: frontier:7's and deadLines:2's only bridges are
        // 560-600 m past the gate, and from yard + 400 the Car Hugger had nowhere to lurk on either (never placed).
        var tries = new List<double>();
        if (kinds.Contains(EnemyKind.CarHugger))
            tries.AddRange(route.Features.Where(f => f.Kind is FeatureKind.Marsh or FeatureKind.Bridge).Select(f => (f.Start + f.End) * 0.5 - e.CarHugger.LurkAheadMin - lead)
                .Where(s => s >= yard + YardClear));
        for (double s = yard + 400; s < route.RunLength - 4000; s += 50)
            tries.Add(s);
        int wanted = needs.Count + (gradeWithin > 0 ? 1 : 0);
        int Met(double s) => needs.Count(n => n.Ok(s)) + (gradeWithin > 0 && Steep(s) ? 1 : 0);
        double best = -1;
        int bestMet = -1;
        foreach (double s in tries.Where(s => s < route.RunLength - 4000))
        {
            if (!Clear(s))
                continue;
            int met = Met(s);
            if (met > bestMet)
            {
                (best, bestMet) = (s, met);
                if (met == wanted)
                    break;
            }
        }
        if (best < 0)
            best = yard + 400;
        return (best, [.. needs.Where(n => !n.Ok(best)).Select(n => n.Kind.ToString()), .. gradeWithin > 0 && !Steep(best) ? ["grade"] : Array.Empty<string>()]);
    }

    /// <summary>One night's run, from its harness report.</summary>
    public static CombinationRun Run(CombinationNight n, HarnessReport r, CombinationTuning t, int crew)
    {
        var threats = r.Threats;
        string end = r.Run?.End.ToString() ?? "none";
        bool derailed = threats?.Derailed == true;
        bool lost = derailed || end is "Stranded" or "CrewLost" or "Derailed" || r.Deaths >= Math.Max(1, Math.Ceiling(t.UnwinnableLost * crew));
        var engaged = threats?.Engaged.Keys.Where(k => n.Kinds.Any(kind => kind.ToString() == k)).ToList() ?? [];
        // The Gannet's met when it hangs over a walker (note 1036): a crew that stands still under it (Heed.Gannet, note 454)
        // is never folded on, and that's its answer working, not a meeting that never came.
        // And Tower Jaw that brought its structure down came on, telegraph or none (note 1036: its fall is entered from Alert).
        if (threats?.TowersDown > 0 && n.Kinds.Contains(EnemyKind.TowerJaw) && !engaged.Contains(nameof(EnemyKind.TowerJaw)))
            engaged.Add(nameof(EnemyKind.TowerJaw));
        engaged.AddRange(threats?.Alerted.Keys.Where(k => MetAtAlert.Any(kind => kind.ToString() == k) && n.Kinds.Any(kind => kind.ToString() == k) && !engaged.Contains(k)) ?? []);
        // The Choir isn't spawned by a rule: it's there once its ghosts are.
        var placed = n.Kinds.Select(k => k.ToString()).Where(k => threats?.Spawned.ContainsKey(k) == true || engaged.Contains(k)).ToList();
        return new CombinationRun(n.Seed, Math.Round(n.StartM), end, r.Deaths, derailed, lost,
            threats?.Grabs.Values.Sum() ?? 0, threats?.Punishes.Values.Sum() ?? 0, threats?.Rescues.Values.Sum() ?? 0,
            Math.Round(1 - (threats?.MeanCargoIntegrity ?? 1), 3), engaged)
        { Placed = placed, Route = n.Route, Crew = crew, TowersDown = threats?.TowersDown ?? 0 };
    }

    /// <summary>
    /// Every night of a sweep, in a fixed order: combination by combination, then route, crew and seed (note 204). Each
    /// combination meets every hazard set (<paramref name="everyHazard"/>), or one, the sets taken in turn down the list.
    /// </summary>
    public static IReadOnlyList<(IReadOnlyList<EnemyKind> Kinds, HazardSet Hazards, string Route, int Crew, int Seed)> Nights(
        IReadOnlyList<IReadOnlyList<EnemyKind>> combos, IReadOnlyList<HazardSet> hazards, bool everyHazard, CombinationGrid grid) =>
        [.. from i in Enumerable.Range(0, combos.Count)
            from hz in everyHazard ? hazards : [hazards[i % hazards.Count]]
            from route in grid.Routes
            from crew in grid.Crews
            from s in Enumerable.Range(1, grid.Seeds)
            select (combos[i], hz, route, crew, s)];

    /// <summary>A combination's nights by (route, crew), in the order the cells first come up.</summary>
    public static IReadOnlyList<CombinationCell> Cells(IReadOnlyList<CombinationRun> runs) =>
        [.. runs.GroupBy(r => (r.Route, r.Crew)).Select(g => new CombinationCell(g.Key.Route, g.Key.Crew, g.Count(), g.Count(r => r.Lost),
            g.All(r => r.Lost), g.Sum(r => r.Grabs), g.Sum(r => r.Punishes), g.Sum(r => r.Deaths), [.. g.SelectMany(r => r.Engaged).Distinct()]))];

    static string CellName(string route, int crew) => route.Length == 0 ? $"crew {crew}" : $"{route} crew {crew}";

    /// <summary>
    /// A combination under one hazard set, over its routes, crews and seeds (note 204): unwinnable (lost every night in some
    /// (route, crew) cell), unexercised (a kind that came on in no night anywhere), trivial (everything came on and nothing
    /// landed in any night anywhere), or fair.
    /// </summary>
    public static CombinationRow Judge(IReadOnlyList<EnemyKind> kinds, HazardSet hazards, IReadOnlyList<CombinationRun> runs,
        IReadOnlyList<string> unstaged, CombinationTuning t)
    {
        var names = kinds.Select(k => k.ToString()).ToList();
        var unexercised = names.Where(k => runs.All(r => !r.Engaged.Contains(k))).ToList();
        var cells = Cells(runs);
        string verdict, detail;
        if (cells.Where(c => c.Unwinnable).ToList() is { Count: > 0 } lost)
        {
            verdict = "unwinnable";
            detail = string.Join("; ", lost.Select(c => $"{CellName(c.Route, c.Crew)}: " + string.Join(", ", runs.Where(r => r.Route == c.Route && r.Crew == c.Crew)
                .Select(r => $"seed {r.Seed} {r.End}, {r.Deaths} dead{(r.Derailed ? ", derailed" : "")}"))));
            // Lost in some cells and not others: say how many it got through.
            if (lost.Count < cells.Count)
                detail += $" (won a night in {cells.Count - lost.Count} of {cells.Count} cells)";
        }
        else if (unexercised.Where(k => runs.All(r => !r.Placed.Contains(k))).ToList() is { Count: > 0 } unplaced)
        {
            verdict = "unplaced";
            detail = $"never put anywhere: {string.Join(", ", unplaced)}";
        }
        else if (unexercised.Count > 0)
        {
            verdict = "dormant";
            detail = $"put there, never came on: {string.Join(", ", unexercised)}";
        }
        else if (runs.All(r => r.Grabs == 0 && r.Punishes == 0 && r.Deaths == 0 && !r.Derailed && r.CargoLoss <= t.TrivialCargoLoss && r.TowersDown == 0))
        {
            verdict = "trivial";
            detail = "everything came on and nothing landed: no grab, no punish, no death, the cargo untouched";
        }
        else
        {
            verdict = "fair";
            detail = $"{runs.Sum(r => r.Grabs)} grabs ({runs.Sum(r => r.Rescues)} broken), {runs.Sum(r => r.Punishes)} punishes, {runs.Sum(r => r.Deaths)} dead over {runs.Count} nights"
                + (runs.Sum(r => r.TowersDown) is > 0 and var down ? $", {down} structures down" : "");
        }
        return new CombinationRow(names, hazards.Name, verdict, detail, unexercised, unstaged, cells, runs);
    }

    public static CombinationReport Report(int size, IReadOnlyList<CombinationRow> rows)
    {
        static string Name(CombinationRow r) => $"{string.Join("+", r.Kinds)} / {r.Hazards}";
        var never = Roster.Select(k => k.ToString())
            .Select(k => (k, Rows: rows.Where(r => r.Kinds.Contains(k)).ToList()))
            .Where(x => x.Rows.Count > 0 && x.Rows.All(r => r.Unexercised.Contains(x.k)))
            .ToDictionary(x => x.k, x => x.Rows.Count);
        var verdicts = new SortedDictionary<string, int>(rows.GroupBy(r => r.Verdict).ToDictionary(g => g.Key, g => g.Count()), StringComparer.Ordinal);
        var unwinnable = rows.Where(r => r.Verdict == "unwinnable").Select(Name).ToList();
        var trivial = rows.Where(r => r.Verdict == "trivial").Select(Name).ToList();
        // Each cell over the whole sweep, in the order the cells first come up.
        var byCell = rows.SelectMany(r => r.Cells.Select(c => (Row: r, Cell: c))).GroupBy(x => (x.Cell.Route, x.Cell.Crew))
            .Select(g => new CombinationGridCell(g.Key.Route, g.Key.Crew, g.Sum(x => x.Cell.Nights), g.Sum(x => x.Cell.Lost),
                [.. g.Where(x => x.Cell.Unwinnable).Select(x => Name(x.Row))]))
            .ToList();
        return new CombinationReport(size, rows.Select(r => string.Join("+", r.Kinds)).Distinct().Count(), rows.Sum(r => r.Runs.Count), verdicts,
            unwinnable, trivial, never, byCell, rows, unwinnable.Count == 0 && trivial.Count == 0);
    }
}
