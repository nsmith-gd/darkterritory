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
    public double StopSeconds { get; init; } = 150;
    public double ApproachM { get; init; } = 350;
    public double UnwinnableLost { get; init; } = 0.5;
    public double TrivialCargoLoss { get; init; } = 0.02;
    public IReadOnlyList<HazardSet> HazardSets { get; init; } = [HazardSet.Clear];
}

/// <summary>Which routes, crew sizes and seeds a sweep runs each combination over (note 204). A (route, crew) pair is a cell.</summary>
public sealed record CombinationGrid(IReadOnlyList<string> Routes, IReadOnlyList<int> Crews, int Seeds);

/// <summary>One night of the sweep: the kinds insisted on, what the line takes away, the seed, and where the train starts,
/// on which route (tier:seed).</summary>
public sealed record CombinationNight(IReadOnlyList<EnemyKind> Kinds, HazardSet Hazards, int Seed, double StartM, IReadOnlyList<string> Unstaged,
    string Route = "");

/// <summary>Where a combination's nights start: out on the line, or short of a facility the crew stop and work at.</summary>
public sealed record CombinationStage(double StartM, bool AtStop, IReadOnlyList<string> Unstaged);

/// <summary>How one night of a combination went.</summary>
/// <param name="Lost">Counted lost: derailed, stranded, the crew wiped out, or <see cref="CombinationTuning.UnwinnableLost"/> of them dead.</param>
/// <param name="Engaged">The kinds that came on (telegraphed or further) during the night.</param>
/// <param name="Placed">The kinds sent at all (their spawn found somewhere to put them).</param>
public sealed record CombinationRun(int Seed, double StartM, string End, int Deaths, bool Derailed, bool Lost, int Grabs, int Punishes, int Rescues,
    double CargoLoss, IReadOnlyList<string> Engaged)
{
    public IReadOnlyList<string> Placed { get; init; } = [];
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
        EnemyKind.SootChildren, EnemyKind.Choir, EnemyKind.Passenger, EnemyKind.Switchman, EnemyKind.Grumbler,
    ];

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
    };

    /// <summary>
    /// Where a combination's nights start (on <paramref name="route"/>, built as <paramref name="line"/>). With a kind that
    /// comes at a stop, and <paramref name="atStops"/>: <paramref name="approach"/> short of the first facility with a spur to work (one with a crane for the
    /// Grumbler, <paramref name="crane"/>). Otherwise the first place clear of the yard, stops and tunnels from which every kind
    /// that needs the line to place it can be placed (the Car Hugger's marsh or bridge ahead, the Switchman's dead line
    /// ahead), <paramref name="lead"/> short. The kinds no place suits are returned as <c>Unstaged</c>.
    /// </summary>
    public static CombinationStage Stage(Route.Route route, Rail.RailLine line, IReadOnlyList<EnemyKind> kinds, EnemyTuning e, double yard, double lead,
        double approach = 350, Func<RouteFeature, bool>? crane = null, bool atStops = true)
    {
        if (atStops && kinds.Any(AtStops.Contains))
        {
            var spurs = route.Of(FeatureKind.Facility).Where(f => line.Branches.Any(b => b.Kind == Rail.BranchKind.Spur && b.Toe >= f.Start - 50 && b.Toe <= f.End + 50))
                .Where(f => f.Start - approach >= yard + 100).ToList();
            bool wantCrane = kinds.Contains(EnemyKind.Grumbler) && crane is not null;
            var at = (wantCrane ? spurs.FirstOrDefault(crane!) : null) ?? spurs.FirstOrDefault();
            if (at is not null)
            {
                double toe = line.Branches.Where(b => b.Kind == Rail.BranchKind.Spur && b.Toe >= at.Start - 50 && b.Toe <= at.End + 50).Min(b => b.Toe);
                var unstaged = new List<string>();
                if (wantCrane && !crane!(at))
                    unstaged.Add(EnemyKind.Grumbler.ToString());
                unstaged.AddRange(kinds.Where(k => k is EnemyKind.CarHugger or EnemyKind.Switchman).Select(k => k.ToString()));
                return new CombinationStage(Math.Round(toe - approach), true, unstaged);
            }
        }
        // Out on the line, what comes at a stop is sent at the crew as they board from the ballast at the start (the halt's
        // stop): unstaged, since it's no facility, and it may well lie in wait unmet.
        var (start, missing) = OnTheLine(route, line, kinds, e, yard, lead);
        return new CombinationStage(start, false, [.. missing, .. kinds.Where(AtStops.Contains).Select(k => k.ToString())]);
    }

    static (double StartM, IReadOnlyList<string> Unstaged) OnTheLine(Route.Route route, Rail.RailLine line, IReadOnlyList<EnemyKind> kinds,
        EnemyTuning e, double yard, double lead)
    {
        bool Hugger(double s) => route.Features.Any(f => f.Kind is FeatureKind.Marsh or FeatureKind.Bridge
            && (f.Start + f.End) * 0.5 is var at && at >= s + e.CarHugger.LurkAheadMin + lead && at <= s + e.CarHugger.LurkAheadMax
            && Math.Abs(line.Sample(Rail.RailLine.MainPath, at).GradePercent) <= e.CarHugger.MaxGrade);
        bool Switchman(double s) => line.Branches.Any(b => b.Kind == Rail.BranchKind.DeadLine && b.Toe - s >= e.Switchman.Ahead[0] + lead && b.Toe - s <= e.Switchman.Ahead[1]);
        bool Clear(double s) => !route.Features.Any(f => f.Kind is FeatureKind.Facility or FeatureKind.Tunnel or FeatureKind.Junction
            && s >= f.Start - 300 && s <= f.End + 100);
        var needs = new List<(EnemyKind Kind, Func<double, bool> Ok)>();
        if (kinds.Contains(EnemyKind.CarHugger))
            needs.Add((EnemyKind.CarHugger, Hugger));
        if (kinds.Contains(EnemyKind.Switchman))
            needs.Add((EnemyKind.Switchman, Switchman));
        // The places to try: just far enough short of each thing a kind needs (so the train's soon on it from a stand), then
        // every 50 m of the line.
        var tries = new List<double>();
        if (kinds.Contains(EnemyKind.CarHugger))
            tries.AddRange(route.Features.Where(f => f.Kind is FeatureKind.Marsh or FeatureKind.Bridge).Select(f => (f.Start + f.End) * 0.5 - e.CarHugger.LurkAheadMin - lead));
        if (kinds.Contains(EnemyKind.Switchman))
            tries.AddRange(line.Branches.Where(b => b.Kind == Rail.BranchKind.DeadLine).Select(b => b.Toe - e.Switchman.Ahead[0] - lead));
        for (double s = yard + 400; s < route.Length - 4000; s += 50)
            tries.Add(s);
        double best = -1;
        int bestMet = -1;
        foreach (double s in tries.Where(s => s >= yard + 400 && s < route.Length - 4000))
        {
            if (!Clear(s))
                continue;
            int met = needs.Count(n => n.Ok(s));
            if (met > bestMet)
            {
                (best, bestMet) = (s, met);
                if (met == needs.Count)
                    break;
            }
        }
        if (best < 0)
            best = yard + 400;
        return (best, [.. needs.Where(n => !n.Ok(best)).Select(n => n.Kind.ToString())]);
    }

    /// <summary>One night's run, from its harness report.</summary>
    public static CombinationRun Run(CombinationNight n, HarnessReport r, CombinationTuning t, int crew)
    {
        var threats = r.Threats;
        string end = r.Run?.End.ToString() ?? "none";
        bool derailed = threats?.Derailed == true;
        bool lost = derailed || end is "Stranded" or "CrewLost" or "Derailed" || r.Deaths >= Math.Max(1, Math.Ceiling(t.UnwinnableLost * crew));
        var engaged = threats?.Engaged.Keys.Where(k => n.Kinds.Any(kind => kind.ToString() == k)).ToList() ?? [];
        // The Choir isn't spawned by a rule: it's there once its ghosts are.
        var placed = n.Kinds.Select(k => k.ToString()).Where(k => threats?.Spawned.ContainsKey(k) == true || engaged.Contains(k)).ToList();
        return new CombinationRun(n.Seed, Math.Round(n.StartM), end, r.Deaths, derailed, lost,
            threats?.Grabs.Values.Sum() ?? 0, threats?.Punishes.Values.Sum() ?? 0, threats?.Rescues.Values.Sum() ?? 0,
            Math.Round(1 - (threats?.MeanCargoIntegrity ?? 1), 3), engaged)
        { Placed = placed, Route = n.Route, Crew = crew };
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
        else if (runs.All(r => r.Grabs == 0 && r.Punishes == 0 && r.Deaths == 0 && !r.Derailed && r.CargoLoss <= t.TrivialCargoLoss))
        {
            verdict = "trivial";
            detail = "everything came on and nothing landed: no grab, no punish, no death, the cargo untouched";
        }
        else
        {
            verdict = "fair";
            detail = $"{runs.Sum(r => r.Grabs)} grabs ({runs.Sum(r => r.Rescues)} broken), {runs.Sum(r => r.Punishes)} punishes, {runs.Sum(r => r.Deaths)} dead over {runs.Count} nights";
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
