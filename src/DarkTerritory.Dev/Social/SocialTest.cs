using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Audit;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Dev.Social;

/// <summary>Mirror of content/tuning/social.json: GDD §20's social test as `dt social` measures it (note 1038). Field docs live there.</summary>
public sealed record SocialTuning
{
    public const string File = "tuning/social.json";

    public IReadOnlyList<string> Creatures { get; init; } = [];
    public string Route { get; init; } = "frontier:7";
    public int Cars { get; init; } = 6;
    public double Seconds { get; init; } = 120;
    public int Seeds { get; init; } = 2;
    public IReadOnlyList<int> Crews { get; init; } = [1, 2];
    public double InsistEvery { get; init; } = 10;
    public double TwoHeldShare { get; init; } = 0.75;
    public double ActMinSeconds { get; init; } = 8;
    public double ActMaxSeconds { get; init; } = 20;
    public double ReachM { get; init; } = 2.5;
    public double BlamedShare { get; init; } = 0.5;
    public double SplitM { get; init; } = 26;

    /// <summary>The creatures measured: the tuning's, else the director's roster and everything that grabs, in roster order.</summary>
    public IReadOnlyList<EnemyKind> Kinds() => Creatures.Count > 0
        ? [.. Creatures.Select(k => Enum.Parse<EnemyKind>(k, ignoreCase: true))]
        : [.. Combinations.Roster.Concat(GrabAudit.Grabbers).Distinct()];
}

/// <summary>
/// One grab seen on a bot night: whose creature, how long it held (s), how long till a friend was within reach of the held
/// (null: nobody came), and how it ended ("broken", "killed", or "held" when the night ended first).
/// </summary>
public sealed record SocialGrab(double Held, double? Reach, string End);

/// <summary>
/// One bot night of a creature: the crew (the bots by name, each driving their player through intent as a person does), what
/// failed and whether the report named someone for it, the grabs, and the crew's own numbers: crew-seconds split up (no
/// living friend within voice's reach), calls made, and crew-seconds dead and watching. <paramref name="Calls"/> are what the
/// bots said on the crew's voice (<see cref="VoiceReport.Calls"/>: a change in what one says, not a repeat), which a bot keeps
/// up far more than a person would; <paramref name="Pings"/> the pings made (GDD §12), a player's own call. <paramref name="Seconds"/>: how long it ran.
/// </summary>
public sealed record SocialNight(string Creature, int Crew, int Seed, IReadOnlyList<string> Bots, int Deaths, int Failures, int Blamed,
    IReadOnlyList<SocialGrab> Grabs, double SplitSeconds, int Calls, int Pings, double DeadSeconds, bool Engaged, double Seconds);

/// <summary>A criterion's verdict for a creature: "pass", "fail" or "unmeasured" (nothing happened that could judge it), and why.</summary>
public sealed record SocialVerdict(string Result, string Why);

/// <summary>
/// One creature's row: the staged grab alone and with a friend (`dt audit grabs`' rig, null for one that doesn't grab), its
/// nights summed by crew size, and the verdict on §20's criteria 2, 3 and 5.
/// </summary>
public sealed record SocialRow(string Creature, GrabCheck? Alone, GrabCheck? Paired, IReadOnlyList<SocialCrewSum> ByCrew,
    SocialVerdict Two, SocialVerdict Act, SocialVerdict Blame)
{
    public int Passed => new[] { Two, Act, Blame }.Count(v => v.Result == "pass");
    public int Failed => new[] { Two, Act, Blame }.Count(v => v.Result == "fail");
}

/// <summary>A creature's nights at one crew size, summed: per night where it says so.</summary>
public sealed record SocialCrewSum(int Crew, int Nights, int Engaged, double DeathsPerNight, double FailuresPerNight, int Failures, int Blamed,
    int Grabs, int Broken, int Killed, double? MeanHeld, double? MeanReach, double SplitPerNight, double CallsPerNight, double DeadPerNight);

/// <summary>The whole test: every creature's row, the crew's numbers over every night, and which creatures fail what.</summary>
public sealed record SocialReport(string Route, int Cars, double Seconds, int Seeds, IReadOnlyList<int> Crews, IReadOnlyList<SocialRow> Rows,
    IReadOnlyList<SocialNight> Nights, object Crew, IReadOnlyDictionary<string, IReadOnlyList<string>> Fails);

/// <summary>
/// GDD §20's social test made into numbers (queue #255, ARCHITECTURE §8 note 1038). For each creature: the staged grab
/// (<see cref="GrabAudit.Check"/>) with the victim alone and with one friend near, and bot nights with only it insisted on
/// at each crew size, watched a tick at a time (<see cref="Watch"/>). Reads the host's world, never changes it: the
/// measuring lives here, not in the Sim (note 514).
/// </summary>
public static class SocialTest
{
    /// <summary>Failures the incident report keeps, for criterion 5: what a person's choice could be blamed for.</summary>
    static readonly HashSet<IncidentKind> FailureKinds =
    [
        IncidentKind.Death, IncidentKind.Punished, IncidentKind.Struck, IncidentKind.Fire, IncidentKind.Nest, IncidentKind.Aboard,
        IncidentKind.Runaway, IncidentKind.CarLost,
    ];

    public static AuditContent Content(string content) => new(
        DataFile.Load<Sim.Train.TrainTuning>(Path.Combine(content, Sim.Train.TrainTuning.File)),
        DataFile.Load<PlayerTuning>(Path.Combine(content, PlayerTuning.File)),
        DataFile.Load<Sim.Train.BoilerTuning>(Path.Combine(content, Sim.Train.BoilerTuning.File)),
        DataFile.Load<Sim.Combat.CombatTuning>(Path.Combine(content, Sim.Combat.CombatTuning.File)),
        DataFile.Load<EnemyTuning>(Path.Combine(content, EnemyTuning.File)),
        DataFile.Load<RunTuning>(Path.Combine(content, RunTuning.File)),
        DataFile.Load<BalanceTuning>(Path.Combine(content, BalanceTuning.File)));

    /// <summary>Runs the test: <paramref name="parallel"/> nights at once (each night is its own, so the result is the same at any count).</summary>
    public static SocialReport Run(string content, SocialTuning t, int parallel = 1, Action<string>? progress = null)
    {
        var c = Content(content);
        var kinds = t.Kinds();
        var route = Sim.LineGen.Routes.Generate(content, t.Route, t.Cars);
        double yard = route.GateOr(RouteTuning.Load(content).YardLength);
        var facilities = DataFile.Load<FacilityTuning>(Path.Combine(content, FacilityTuning.File));
        var sight = DataFile.Load<SightTuning>(Path.Combine(content, SightTuning.File));
        var combos = c.Balance.Combinations;
        var stageLine = route.Build();
        var stages = kinds.ToDictionary(k => k, k => Combinations.Stage(route, stageLine, [k], c.Enemies, yard, combos.LeadM, combos.ApproachM,
            f => facilities.ModulesOf(f).Contains(ModuleKind.Crane), atStops: true));
        var plan = (from k in kinds from crew in t.Crews from seed in Enumerable.Range(1, t.Seeds) select (Kind: k, Crew: crew, Seed: seed)).ToList();
        var nights = new SocialNight[plan.Count];
        int done = 0;
        Parallel.For(0, plan.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, parallel) }, i =>
        {
            var (kind, crew, seed) = plan[i];
            var stage = stages[kind];
            var watch = new Watch(kind, t);
            var report = Harness.Run(route.Build(), c.Train, c.Player, new HarnessOptions
            {
                Bots = crew,
                Cars = t.Cars,
                Seconds = stage.AtStop ? Math.Max(t.Seconds, combos.StopSeconds) : t.Seconds,
                Seed = seed,
                // A clean link and clear voice: the crew's dealings are under test, not the netcode; the voice counts the calls.
                Link = new Ballast.Net.LinkConditions(0, 0, 0),
                Voice = VoiceConditions.Clear,
                StartDistance = stage.StartM,
                Combat = c.Combat,
                Enemies = c.Enemies,
                Route = route,
                Run = c.Run,
                Facilities = facilities,
                Sight = sight,
                YardLength = yard,
                Insist = [kind],
                InsistEvery = t.InsistEvery,
                Look = combos.Look,
                Observe = watch.Step,
            }, c.Boiler);
            nights[i] = watch.Night(crew, seed, report);
            int n = Interlocked.Increment(ref done);
            progress?.Invoke($"{n}/{plan.Count} nights");
        });
        var rows = kinds.Select(k =>
        {
            bool grabs = GrabAudit.Grabbers.Contains(k);
            var alone = grabs ? GrabAudit.Check(c, k, 1) : null;
            var paired = grabs ? GrabAudit.Check(c, k, 2) : null;
            return Judge(k, alone, paired, [.. nights.Where(n => n.Creature == k.ToString())], t);
        }).ToList();
        var fails = new SortedDictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["2"] = [.. rows.Where(r => r.Two.Result == "fail").Select(r => r.Creature)],
            ["3"] = [.. rows.Where(r => r.Act.Result == "fail").Select(r => r.Creature)],
            ["5"] = [.. rows.Where(r => r.Blame.Result == "fail").Select(r => r.Creature)],
        };
        var crewNumbers = t.Crews.Select(crew =>
        {
            var mine = nights.Where(n => n.Crew == crew).ToList();
            return new
            {
                crew,
                nights = mine.Count,
                splitPerNight = Mean(mine.Select(n => n.SplitSeconds)),
                splitShare = Math.Round(mine.Sum(n => n.SplitSeconds) / Math.Max(1e-9, mine.Sum(n => n.Crew * n.Seconds)), 3),
                deadShare = Math.Round(mine.Sum(n => n.DeadSeconds) / Math.Max(1e-9, mine.Sum(n => n.Crew * n.Seconds)), 3),
                callsPerNight = Mean(mine.Select(n => (double)n.Calls)),
                pingsPerNight = Mean(mine.Select(n => (double)n.Pings)),
                deadPerNight = Mean(mine.Select(n => n.DeadSeconds)),
            };
        }).ToList();
        return new SocialReport(t.Route, t.Cars, t.Seconds, t.Seeds, t.Crews, rows, nights, crewNumbers, fails);
    }

    static double Mean(IEnumerable<double> xs) => xs.Any() ? Math.Round(xs.Average(), 2) : 0;

    /// <summary>A creature's verdicts from its staged grabs and its nights.</summary>
    public static SocialRow Judge(EnemyKind kind, GrabCheck? alone, GrabCheck? paired, IReadOnlyList<SocialNight> nights, SocialTuning t)
    {
        var byCrew = nights.GroupBy(n => n.Crew).OrderBy(g => g.Key).Select(g =>
        {
            var all = g.ToList();
            var grabs = all.SelectMany(n => n.Grabs).ToList();
            var reached = grabs.Where(x => x.Reach is not null).Select(x => x.Reach!.Value).ToList();
            return new SocialCrewSum(g.Key, all.Count, all.Count(n => n.Engaged), Mean(all.Select(n => (double)n.Deaths)), Mean(all.Select(n => (double)n.Failures)),
                all.Sum(n => n.Failures), all.Sum(n => n.Blamed), grabs.Count, grabs.Count(x => x.End == "broken"), grabs.Count(x => x.End == "killed"),
                grabs.Count > 0 ? Math.Round(grabs.Average(x => x.Held), 2) : null, reached.Count > 0 ? Math.Round(reached.Average(), 2) : null,
                Mean(all.Select(n => n.SplitSeconds)), Mean(all.Select(n => (double)n.Calls + n.Pings)), Mean(all.Select(n => n.DeadSeconds)));
        }).ToList();
        return new SocialRow(kind.ToString(), alone, paired, byCrew, Two(alone, paired, byCrew, t), Act(alone, paired, nights, t), Blame(nights, t));
    }

    /// <summary>Criterion 2: the staged grab alone against with a friend; with none, the nights' failures at the least and next crew.</summary>
    static SocialVerdict Two(GrabCheck? alone, GrabCheck? paired, IReadOnlyList<SocialCrewSum> byCrew, SocialTuning t)
    {
        if (alone is { Grabbed: true } a && paired is { Grabbed: true } p)
        {
            string said = $"alone: {(a.Freed ? $"freed in {a.HeldSeconds:0.0} s" : a.Detail)}; with a friend: {(p.Freed ? $"freed in {p.HeldSeconds:0.0} s" : p.Detail)}";
            if (p.Freed && !a.Freed)
                return new("pass", "two free a grab one can't: " + said);
            if (p.Freed && p.HeldSeconds <= t.TwoHeldShare * a.HeldSeconds)
                return new("pass", "two are held shorter: " + said);
            return new("fail", (p.Freed ? "two are held no shorter than one: " : "two can't free it: ") + said);
        }
        if (byCrew.Count >= 2 && byCrew[0].Failures + byCrew[1].Failures > 0)
        {
            var (one, two) = (byCrew[0], byCrew[1]);
            string said = $"{one.FailuresPerNight:0.##} failures a night at crew {one.Crew}, {two.FailuresPerNight:0.##} at crew {two.Crew}";
            return two.FailuresPerNight < one.FailuresPerNight ? new("pass", said) : new("fail", said);
        }
        return new("unmeasured", alone is { Grabbed: false } ? $"no staged grab ({alone.Detail}), and nothing failed on its nights" : "no grab staged, and nothing failed on its nights");
    }

    /// <summary>Criterion 3: the grab's window (grab to kill) inside the band, staged; else the nights' grabs that killed.</summary>
    static SocialVerdict Act(GrabCheck? alone, GrabCheck? paired, IReadOnlyList<SocialNight> nights, SocialTuning t)
    {
        var grabs = nights.SelectMany(n => n.Grabs).ToList();
        var reached = grabs.Where(x => x.Reach is not null).Select(x => x.Reach!.Value).ToList();
        string reach = reached.Count > 0 ? $"; on its nights a friend reached the held in {reached.Average():0.0} s ({reached.Count} of {grabs.Count} grabs)"
            : grabs.Count > 0 ? $"; on its nights no friend reached the held ({grabs.Count} grabs)" : "";
        double? window = alone is { Grabbed: true } ? alone.WindowSeconds : paired is { Grabbed: true } ? paired.WindowSeconds : null;
        string source = "the staged grab's window";
        if (window is null && grabs.Where(x => x.End == "killed").Select(x => x.Held).ToList() is { Count: > 0 } kills)
            (window, source) = (Math.Round(kills.Average(), 2), $"the nights' {kills.Count} grabs that killed, on average");
        if (window is not { } w)
            return new("unmeasured", "no grab staged or seen kill" + reach);
        bool ok = w >= t.ActMinSeconds && w <= t.ActMaxSeconds;
        return new(ok ? "pass" : "fail", $"{source}: {w:0.0} s (band {t.ActMinSeconds:0}-{t.ActMaxSeconds:0} s)" + reach);
    }

    /// <summary>Criterion 5: the share of its nights' failures the report names a crewmate's contributing action for.</summary>
    static SocialVerdict Blame(IReadOnlyList<SocialNight> nights, SocialTuning t)
    {
        int failures = nights.Sum(n => n.Failures), blamed = nights.Sum(n => n.Blamed);
        if (failures == 0)
            return new("unmeasured", "nothing failed on its nights");
        double share = (double)blamed / failures;
        return new(share >= t.BlamedShare ? "pass" : "fail", $"{blamed} of {failures} failures name a crewmate ({share:P0}, at least {t.BlamedShare:P0})");
    }

    /// <summary>
    /// Watches one bot night (the harness's <see cref="HarnessOptions.Observe"/>, after every step): the creature's grabs, from
    /// its spine's events (who it holds, and how soon a living friend is within reach of them), the incident log's failures and
    /// whom they name, and the crew's places, deaths and pings. The harness's crew are its bots in order, player ids 1 up (as
    /// `dt telemetry record` reads them).
    /// </summary>
    public sealed class Watch(EnemyKind kind, SocialTuning t)
    {
        readonly SortedDictionary<int, (int Victim, uint From, uint? Reach)> _open = [];
        readonly List<SocialGrab> _grabs = [];
        readonly HashSet<int> _pings = [];
        IReadOnlyList<string> _bots = [];
        int _incidents, _deaths, _failures, _blamed;
        double _split, _dead;
        bool _engaged;
        World? _world;

        public void Step(uint tick, IReadOnlyList<(IBot Bot, PlayerState State)> crew, World world)
        {
            _world = world;
            if (_bots.Count == 0)
                _bots = [.. crew.Select(c => c.Bot.Name)];
            var at = crew.Select(c => c.State.Alive ? PlayerMotor.WorldPosition(c.State, world.Train) : (Double3?)null).ToList();
            foreach (var e in world.EnemyEvents.Where(e => e.Kind == kind))
            {
                if (e.To is SpinePhase.Telegraph or SpinePhase.Commit or SpinePhase.Grab or SpinePhase.Punish)
                    _engaged = true;
                if (e.From == SpinePhase.Grab && _open.Remove(e.EnemyId, out var was))
                    _grabs.Add(new SocialGrab(Math.Round(e.SecondsInFrom, 2), was.Reach is { } r ? Math.Round((r - was.From) * SimConstants.TickSeconds, 2) : (double?)null,
                        e.To == SpinePhase.Punish ? "killed" : "broken"));
                if (e.To == SpinePhase.Grab && world.ActiveEnemies.FirstOrDefault(x => x.Id == e.EnemyId) is { Holding: >= 1 } holder)
                    _open[e.EnemyId] = (holder.Holding, tick, null);
            }
            foreach (var (id, g) in _open.ToList())
                if (g.Reach is null && g.Victim - 1 < at.Count && at[g.Victim - 1] is { } v
                    && at.Where((p, i) => i != g.Victim - 1 && p is not null).Any(p => (p!.Value - v).Length <= t.ReachM))
                    _open[id] = g with { Reach = tick };
            foreach (var p in world.Pings)
                _pings.Add(p.Id);
            var log = world.Attribution.Log;
            for (; _incidents < log.Count; _incidents++)
                if (FailureKinds.Contains(log[_incidents].Kind))
                {
                    _failures++;
                    _deaths += log[_incidents].Kind == IncidentKind.Death ? 1 : 0;
                    _blamed += log[_incidents].Actor >= 0 ? 1 : 0;
                }
            for (int i = 0; i < at.Count; i++)
            {
                if (at[i] is not { } me)
                {
                    _dead += SimConstants.TickSeconds;
                    continue;
                }
                if (at.Count > 1 && !at.Where((p, j) => j != i && p is not null).Any(p => (p!.Value - me).Length <= t.SplitM))
                    _split += SimConstants.TickSeconds;
            }
        }

        /// <summary>The night as watched, with the harness's report for what the voice carried. A grab still on at the end is "held".</summary>
        public SocialNight Night(int crew, int seed, HarnessReport report) =>
            new(kind.ToString(), crew, seed, _bots, _deaths, _failures, _blamed,
                [.. _grabs, .. _open.Values.Select(g => new SocialGrab(Math.Round(((_world?.Tick ?? g.From) - g.From) * SimConstants.TickSeconds, 2),
                    g.Reach is { } r ? Math.Round((r - g.From) * SimConstants.TickSeconds, 2) : (double?)null, "held"))],
                Math.Round(_split, 1), report.Voice?.Calls ?? 0, _pings.Count, Math.Round(_dead, 1), _engaged, Math.Round(report.Seconds, 1));
    }
}
