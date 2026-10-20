using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;

/// <summary>
/// `dt eligibility` (ARCHITECTURE §8 note 875; the spawn director overhaul's brief §2 issue 5): every kind of the director's spend
/// table, its gates read a second at a time through ordinary nights (<see cref="Eligibility"/>): the nights as `dt trends`
/// plays them (`dt harness --as-app --enemies --upkeep`), over routes × crews × seeds. For each kind: the nights its chance let
/// it about, the share of the night's open seconds (past the grace, short of the final stretch, outside the forts and the line's
/// bans) its rule was met and it could have been sent, the share the night's curve could have sent it free, each gate's share,
/// what blocked it most, and how many came. And the hound run's sustained speed (director.run), measured as the director counts
/// it. `--markdown` prints the tables for the note.
/// </summary>
static class EligibilityCommands
{
    /// <summary>A night's sampling, summed.</summary>
    sealed class Night
    {
        public string Route = "", Config = "";
        public int Bots, Cars, Seed;
        public int Seconds, Open;
        public double Km;
        public Dictionary<EnemyKind, Kind> Kinds = [];
        public Dictionary<string, int> Held = [];
        public Dictionary<string, int> SpeedAtLeast = [];
        public double RunMetresMax;
        public int HoundRuns;
        public int Stopped, AtStop, OnGround;
    }

    sealed class Kind
    {
        public bool About;
        public int Night, Rule, Sendable, Free, SendableFree, Either, Situational;
        public Dictionary<string, int> Gates = [];
        public Dictionary<string, int> Blocked = [];
        public int Sent, SentFree, SentStop;
    }

    static readonly double[] Speeds = [3, 5, 8, 10, 14, 18, 19];

    public static object Run(string[] args, Func<string[], Action<uint, IReadOnlyList<(IBot Bot, PlayerState State)>, World>, object> harness)
    {
        var routes = Str(args, "--routes", "frontier:7,deadLines:3").Split(',', StringSplitOptions.RemoveEmptyEntries);
        // A crew and its train: 1:3 is one crewmate on three cars (the brief §7's sessions).
        var crews = Str(args, "--crews", "1:3,2:5,4:8").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(c => c.Split(':') is [var b, var k] ? (Bots: int.Parse(b, CultureInfo.InvariantCulture), Cars: int.Parse(k, CultureInfo.InvariantCulture))
                : (Bots: int.Parse(c, CultureInfo.InvariantCulture), Cars: 8)).ToList();
        var seeds = Seeds(Str(args, "--seeds", "1-3"));
        double seconds = Opt(args, "--seconds", 2700);
        var grid = (from r in routes from c in crews from s in seeds select (Route: r, c.Bots, c.Cars, Seed: s)).ToList();
        var nights = new ConcurrentBag<Night>();
        Parallel.ForEach(grid, new ParallelOptions { MaxDegreeOfParallelism = (int)Opt(args, "--parallel", Environment.ProcessorCount) }, g =>
        {
            var night = new Night { Route = g.Route, Bots = g.Bots, Cars = g.Cars, Seed = g.Seed, Config = $"{g.Route} {g.Bots}p/{g.Cars}c" };
            var run = (World?)null;
            double runMetres = 0;
            harness(["harness", "--as-app", "--route", g.Route, "--bots", I(g.Bots), "--cars", I(g.Cars), "--enemies", "--upkeep",
                "--seconds", seconds.ToString(CultureInfo.InvariantCulture), "--seed", I(g.Seed)], (tick, _, world) =>
                {
                    run = world;
                    if (tick % SimConstants.TickRate != 0 || world.Director is not { } d || world.Enemies is not { } et)
                        return;
                    night.Seconds++;
                    string held = d.HeldBecause ?? "sent or spending";
                    if (held.StartsWith("banned", StringComparison.Ordinal))
                        held = "banned";
                    night.Held[held] = night.Held.GetValueOrDefault(held) + 1;
                    // Open: the director's own rules could send now (not the grace, the final stretch, a fort, the line's bans, a
                    // derailed or safe-yard night).
                    if (world.Derailed || world.SafeYard || world.CallOnly || held is "grace" or "final stretch" or "in a fort" or "banned")
                        return;
                    night.Open++;
                    double v = world.Train.Dynamics.Speed;
                    foreach (double at in Speeds)
                        if (v >= at)
                            night.SpeedAtLeast[$"≥{at:0}"] = night.SpeedAtLeast.GetValueOrDefault($"≥{at:0}") + 1;
                    // The hound run's count (Director.Runs): metres at fromSpeed and up, started again below stopSpeed.
                    var hr = d.Tuning.Run;
                    runMetres = v < hr.StopSpeed ? 0 : runMetres + (v >= hr.FromSpeed ? v : 0);
                    night.RunMetresMax = Math.Max(night.RunMetresMax, runMetres);
                    var ctx = new SpawnContext(world, et, d);
                    if (ctx.Stopped)
                        night.Stopped++;
                    if (ctx.AtStop || world.InSettlement)
                        night.AtStop++;
                    if (ctx.OnGround.Any())
                        night.OnGround++;
                    foreach (var k in Eligibility.Sample(world))
                    {
                        if (!night.Kinds.TryGetValue(k.Kind, out var s))
                            night.Kinds[k.Kind] = s = new Kind { About = !d.OutTonight(k.Kind) && d.Allows(k.Kind) };
                        if (k.Night)
                            s.Night++;
                        if (k.RuleMet)
                            s.Rule++;
                        if (k.Sendable)
                            s.Sendable++;
                        if (k.FreeMet)
                            s.Free++;
                        if (k.SendableFree)
                            s.SendableFree++;
                        if (k.Sendable || k.SendableFree)
                            s.Either++;
                        if (k.Situational)
                            s.Situational++;
                        foreach (var gate in k.Gates)
                            if (gate.Holds)
                                s.Gates[gate.Gate] = s.Gates.GetValueOrDefault(gate.Gate) + 1;
                            else
                                s.Gates.TryAdd(gate.Gate, 0);
                        if (k.SituationBlocked is { } b)
                            s.Blocked[b] = s.Blocked.GetValueOrDefault(b) + 1;
                    }
                });
            if (run?.Director is { } dd)
            {
                foreach (var l in dd.Log)
                    if (night.Kinds.TryGetValue(l.Kind, out var s))
                    {
                        s.Sent++;
                        s.SentFree += l.Free ? 1 : 0;
                        s.SentStop += l.Stop ? 1 : 0;
                    }
                night.HoundRuns = dd.HoundRuns.Count;
                night.Km = run.Train.Dynamics.Distance / 1000;
            }
            nights.Add(night);
            Console.Error.WriteLine($"  {night.Config} seed {night.Seed}: {night.Open}/{night.Seconds} s open, {night.Km:0.0} km");
        });
        var all = nights.OrderBy(n => n.Route, StringComparer.Ordinal).ThenBy(n => n.Bots).ThenBy(n => n.Seed).ToList();
        var report = Report(all, seconds);
        if (Str(args, "--markdown", "") is { Length: > 0 } md)
            File.WriteAllText(md, Markdown(all));
        return report;
    }

    static object Report(List<Night> nights, double seconds) => new
    {
        seconds,
        nights = nights.Count,
        configs = nights.GroupBy(n => n.Config).Select(g => new
        {
            config = g.Key,
            seeds = g.Select(n => n.Seed).ToList(),
            openSeconds = g.Sum(n => n.Open),
            km = R(g.Average(n => n.Km)),
            stopped = Share(g.Sum(n => n.Stopped), g.Sum(n => n.Open)),
            atStop = Share(g.Sum(n => n.AtStop), g.Sum(n => n.Open)),
            onGround = Share(g.Sum(n => n.OnGround), g.Sum(n => n.Open)),
            speed = Speeds.ToDictionary(s => $"≥{s:0}", s => Share(g.Sum(n => n.SpeedAtLeast.GetValueOrDefault($"≥{s:0}")), g.Sum(n => n.Open))),
            houndRun = new { runs = g.Sum(n => n.HoundRuns), bestMetres = g.Select(n => Math.Round(n.RunMetresMax)).ToList() },
            held = g.SelectMany(n => n.Held).GroupBy(h => h.Key).OrderByDescending(h => h.Sum(x => x.Value))
                .ToDictionary(h => h.Key, h => Share(h.Sum(x => x.Value), g.Sum(n => n.Seconds))),
        }).ToList(),
        kinds = Kinds(nights).Select(k => new
        {
            kind = Director.Key(k.Kind),
            about = $"{k.About}/{k.Nights}",
            situational = k.Situational,
            rule = k.Rule,
            free = k.Free,
            sendable = k.Sendable,
            sendableAbout = k.SendableAbout,
            sent = k.Sent,
            sentFree = k.SentFree,
            sentStop = k.SentStop,
            byConfig = k.ByConfig,
            gates = k.Gates,
            blocked = k.Blocked,
        }).ToList(),
    };

    sealed record KindSummary(EnemyKind Kind, int About, int Nights, double Situational, double Rule, double Free, double Sendable, double SendableAbout,
        int Sent, int SentFree, int SentStop, Dictionary<string, double> ByConfig, Dictionary<string, double> Gates, Dictionary<string, double> Blocked);

    /// <summary>
    /// Each kind over the nights. What the moment allows is measured over every night's open seconds, its night's roll aside:
    /// <c>situational</c> (its rule or its free gate met), <c>rule</c>, <c>free</c>, each gate's share and the first gate that
    /// failed. What the director could actually have sent (<c>sendable</c>: that, and the night allowing it, and room) over every
    /// night's open seconds, and over the nights it was about (<c>sendableAbout</c>). <c>byConfig</c>: situational by crew and train.
    /// </summary>
    static List<KindSummary> Kinds(List<Night> nights)
    {
        var kinds = nights.SelectMany(n => n.Kinds.Keys).Distinct().OrderBy(k => Director.Key(k), StringComparer.Ordinal).ToList();
        return [.. kinds.Select(kind =>
        {
            var with = nights.Where(n => n.Kinds.ContainsKey(kind)).ToList();
            var about = with.Where(n => n.Kinds[kind].About).ToList();
            int open = with.Sum(n => n.Open), openAbout = about.Sum(n => n.Open);
            var ks = with.Select(n => n.Kinds[kind]).ToList();
            return new KindSummary(kind, about.Count, nights.Count,
                Share(ks.Sum(s => s.Situational), open), Share(ks.Sum(s => s.Rule), open), Share(ks.Sum(s => s.Free), open),
                Share(ks.Sum(s => s.Either), open), Share(about.Sum(n => n.Kinds[kind].Either), openAbout),
                ks.Sum(s => s.Sent), ks.Sum(s => s.SentFree), ks.Sum(s => s.SentStop),
                with.GroupBy(n => n.Config).ToDictionary(g => g.Key, g => Share(g.Sum(n => n.Kinds[kind].Situational), g.Sum(n => n.Open))),
                ks.SelectMany(s => s.Gates.Keys).Distinct().ToDictionary(gate => gate, gate => Share(ks.Sum(s => s.Gates.GetValueOrDefault(gate)), open)),
                ks.SelectMany(s => s.Blocked).GroupBy(b => b.Key).OrderByDescending(b => b.Sum(x => x.Value)).Take(3)
                    .ToDictionary(b => b.Key, b => Share(b.Sum(x => x.Value), open)));
        })];
    }

    static string Markdown(List<Night> nights)
    {
        var sb = new StringBuilder();
        var configs = nights.Select(n => n.Config).Distinct().ToList();
        sb.AppendLine($"| Kind | About | Situational | Rule met | Free gate | Sendable (nights about) | {string.Join(" | ", configs)} | Sent (free, stop) | First gate failing |");
        sb.AppendLine($"|---|---|---|---|---|---|{string.Concat(configs.Select(_ => "---|"))}---|---|");
        foreach (var k in Kinds(nights))
            sb.AppendLine($"| {Director.Key(k.Kind)} | {k.About}/{k.Nights} | {P(k.Situational)} | {P(k.Rule)} | {P(k.Free)} | {P(k.SendableAbout)} | "
                + string.Join(" | ", configs.Select(c => k.ByConfig.TryGetValue(c, out var x) ? P(x) : "")) + $" | {k.Sent} ({k.SentFree}, {k.SentStop}) | "
                + string.Join("; ", k.Blocked.Select(b => $"{b.Key} {P(b.Value)}")) + " |");
        sb.AppendLine();
        sb.AppendLine("| Night | Open s | km | Stopped | At a stop | Someone afoot | ≥8 m/s | ≥10 | ≥14 | ≥18 | ≥19 | Hound runs | Best run count (m) |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var n in nights)
            sb.AppendLine($"| {n.Config} s{n.Seed} | {n.Open} | {n.Km:0.0} | {P(Share(n.Stopped, n.Open))} | {P(Share(n.AtStop, n.Open))} | {P(Share(n.OnGround, n.Open))} | "
                + string.Join(" | ", new[] { 8, 10, 14, 18, 19 }.Select(s => P(Share(n.SpeedAtLeast.GetValueOrDefault($"≥{s}"), n.Open)))) + $" | {n.HoundRuns} | {n.RunMetresMax:0} |");
        sb.AppendLine();
        sb.AppendLine("| Kind | Gate: share of open seconds it held |");
        sb.AppendLine("|---|---|");
        foreach (var k in Kinds(nights))
            sb.AppendLine($"| {Director.Key(k.Kind)} | {string.Join("; ", k.Gates.Select(g => $"{g.Key} {P(g.Value)}"))} |");
        return sb.ToString();
    }

    static double Share(int n, int of) => of == 0 ? 0 : Math.Round((double)n / of, 4);
    static double R(double x) => Math.Round(x, 2);
    static string P(double x) => x <= 0 ? "0" : x < 0.001 ? "<0.1%" : (x * 100).ToString(x < 0.1 ? "0.0" : "0", CultureInfo.InvariantCulture) + "%";
    static string I(int x) => x.ToString(CultureInfo.InvariantCulture);

    static List<int> Seeds(string spec)
    {
        var seeds = new List<int>();
        foreach (var part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (part.Split('-') is [var a, var b])
                seeds.AddRange(Enumerable.Range(int.Parse(a, CultureInfo.InvariantCulture), int.Parse(b, CultureInfo.InvariantCulture) - int.Parse(a, CultureInfo.InvariantCulture) + 1));
            else
                seeds.Add(int.Parse(part, CultureInfo.InvariantCulture));
        return seeds;
    }

    static string Str(string[] args, string name, string fallback)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
    }

    static double Opt(string[] args, string name, double fallback) =>
        Str(args, name, "") is { Length: > 0 } s ? double.Parse(s, CultureInfo.InvariantCulture) : fallback;
}
