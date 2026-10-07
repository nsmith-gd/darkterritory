using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Towns;

/// <summary>
/// `dt town`: a night's departure fortress town (GDD §3.1; ARCHITECTURE §8 note 304) as it's made, without a window: its
/// custom, people and their lines, papers and fixtures, where each stands. `dt town sweep`: many towns' customs and their
/// words, checked against what the HUD's cards can show.
/// </summary>
static class TownCommands
{
    public static object Run(string content, string[] args)
    {
        var towns = TownContent.Load(content) ?? throw new InvalidOperationException("this content has no towns (tuning/towns.json, world/towns.json)");
        var roster = DataFile.Load<EnemyTuning>(Path.Combine(content, EnemyTuning.File)).Director.Roster;
        if (args.Length > 1 && args[1] == "sweep")
            return Sweep(towns, roster, (int)Opt(args, "--seeds", 200));
        string spec = Str(args, "--route", "frontier:7");
        var route = Routes.Generate(content, spec, (int)Opt(args, "--cars", 6));
        double gate = route.GateOr(RouteTuning.Load(content).YardLength);
        var plan = TownGenerator.Generate(towns, TownSite.Of(route, gate, roster, towns, Str(args, "--last", "") is { Length: > 0 } last ? last : null));
        return new
        {
            route = spec,
            plan.Name,
            plan.Culture,
            plan.Creature,
            plan.Law,
            plan.Hall,
            plan.Industry,
            plan.Quirks,
            square = new { plan.Square.S0, plan.Square.S1, plan.Square.Side, plan.Square.WallD, gate },
            people = plan.People.Select(p => new { p.Id, p.Name, p.Title, at = new[] { Math.Round(p.S - gate, 1), Math.Round(p.D, 1) }, p.Lines }),
            papers = plan.Papers.Select(p => new { p.Title, p.Text, where = p.OnBoard ? "board" : $"{Math.Round(p.S - gate, 1)}, {Math.Round(p.D, 1)}" }),
            fixtures = plan.Fixtures.Where(f => f.Text.Length > 0).Select(f => new { f.Kind, f.Name, f.Text }),
            doors = plan.Buildings.Select(b => new { b.Name, b.Knock }),
        };
    }

    /// <summary>Many towns from made-up sites: how often each custom comes up, and the longest line and paper against the
    /// cards' limits (TownTests holds the same).</summary>
    static object Sweep(TownContent towns, string[] roster, int seeds)
    {
        var cultures = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int repeats = 0, longestLine = 0, longestPaper = 0, minPeople = int.MaxValue, maxPeople = 0;
        string? last = null;
        string[] industries = [.. towns.Writing.Industries.Keys.Order(StringComparer.Ordinal)];
        for (int i = 1; i <= seeds; i++)
        {
            var site = new TownSite($"Fort {towns.Surnames[i % towns.Surnames.Count]}", industries[i % industries.Length], 1100, (ulong)i, roster, last);
            var plan = TownGenerator.Generate(towns, site);
            cultures[plan.Culture] = cultures.GetValueOrDefault(plan.Culture) + 1;
            repeats += plan.Culture == last ? 1 : 0;
            last = plan.Culture;
            longestLine = Math.Max(longestLine, plan.People.SelectMany(p => p.Lines).Max(l => l.Length));
            longestPaper = Math.Max(longestPaper, plan.Papers.Max(p => p.Text.Length));
            minPeople = Math.Min(minPeople, plan.People.Count);
            maxPeople = Math.Max(maxPeople, plan.People.Count);
        }
        return new { seeds, roster = roster.Length == 0 ? "all" : string.Join(",", roster), cultures, repeatsOfTheLast = repeats, longestLine, longestPaper, people = new[] { minPeople, maxPeople } };
    }

    static double Opt(string[] args, string name, double fallback)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length && double.TryParse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }

    static string Str(string[] args, string name, string fallback)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
    }
}
