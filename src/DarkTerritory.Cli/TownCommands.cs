using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Towns;

/// <summary>
/// `dt town`: a night's departure fortress town (GDD §3.1; ARCHITECTURE §8 note 281) as it's made, without a window: its
/// custom, people and their lines, papers and fixtures, where each stands. `dt town sweep`: many towns' customs and their
/// words, checked against what the HUD's cards can show, and how many are walled (note 335).
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
            plan.Population,
            plan.Former,
            plan.Character,
            houses = plan.Houses.GroupBy(h => h.Kind).ToDictionary(g => g.Key.ToString(), g => g.Count()),
            // A walled town (queue #74, note 335): its wall's reach either side, its streets' middles and lanes' places.
            walled = plan.Bounds is { } b ? new
            {
                left = Math.Round(b.Left, 1),
                right = Math.Round(b.Right, 1),
                rear = Math.Round(b.Rear, 1),
                streets = b.Streets.Select(x => Math.Round(x.D, 1)).Order(),
                lanes = b.Lanes.Select(x => Math.Round(x.S - gate, 1)),
            } : null,
            // Its works (note 353): its trade, where they stand, and each piece along the line from their start.
            works = plan.Works is { } wk ? new
            {
                wk.Trade,
                along = new[] { Math.Round(wk.S0 - gate, 1), Math.Round(wk.S1 - gate, 1) },
                across = new[] { Math.Round(wk.Side * wk.Near, 1), Math.Round(wk.Side * wk.Far, 1) },
                pieces = plan.Fixtures.Where(f => wk.Holds(f.S, f.D)).Select(f => $"{f.Kind} at {f.S - wk.S0:0.0} ({f.Name})"),
            } : null,
            open = plan.Houses.Where(h => h.Kind == HouseKind.Open).Select(h => new
            {
                h.Family,
                at = new[] { Math.Round(h.S - gate, 1), Math.Round(h.D, 1) },
                household = plan.People.Where(p => p.House == h.Id).Select(p => $"{p.Name} ({p.Title}, {p.Pose})"),
                things = plan.Fixtures.Where(f => f.House == h.Id).Select(f => $"{f.Name}: {f.Text}"),
            }),
            plan.Culture,
            plan.Creature,
            plan.Law,
            plan.Hall,
            plan.Industry,
            plan.Quirks,
            square = new { plan.Square.S0, plan.Square.S1, plan.Square.Side, plan.Square.WallD, gate },
            // Who they are (note 474): the town's peoples, how many of each temperament, and each person's traits.
            peoples = plan.People.Where(p => p.Personality is not null).GroupBy(p => p.Personality!.Heritage).OrderByDescending(g => g.Count())
                .ToDictionary(g => g.Key, g => g.Count()),
            temperaments = Temperaments(plan.People),
            people = plan.People.Select(p => new
            {
                p.Id,
                p.Name,
                p.Title,
                at = new[] { Math.Round(p.S - gate, 1), Math.Round(p.D, 1) },
                p.House,
                p.Pose,
                mind = p.Personality is { } m ? new
                {
                    m.Temperament,
                    strength = Math.Round(m.Strength, 2),
                    m.Heritage,
                    m.Generation,
                    traits = TownTraits.Axes.Select((a, i) => (a, v: Math.Round(m.Traits[i], 2))).ToDictionary(x => x.a, x => x.v),
                } : null,
                p.Lines,
            }),
            papers = plan.Papers.Select(p => new { p.Title, p.Text, where = p.OnBoard ? "board" : $"{Math.Round(p.S - gate, 1)}, {Math.Round(p.D, 1)}" }),
            fixtures = plan.Fixtures.Where(f => f.Text.Length > 0).Select(f => new { f.Kind, f.Name, f.Text }),
            doors = plan.Buildings.Select(b => new { b.Name, b.Knock }),
        };
    }

    static SortedDictionary<string, int> Temperaments(IEnumerable<Townsperson> people) =>
        new(people.Where(p => p.Personality is not null).GroupBy(p => p.Personality!.Temperament).ToDictionary(g => g.Key, g => g.Count()), StringComparer.Ordinal);

    /// <summary>Many towns from made-up sites: how often each custom comes up, and the longest line and paper against the
    /// cards' limits (TownTests holds the same).</summary>
    static object Sweep(TownContent towns, string[] roster, int seeds)
    {
        var cultures = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int repeats = 0, longestLine = 0, longestPaper = 0, minPeople = int.MaxValue, maxPeople = 0, minPop = int.MaxValue, maxPop = 0, maxHouses = 0;
        // Walled towns (note 335): how many, the smallest walled and the biggest that isn't, and the most streets a side.
        int walled = 0, smallestWalled = int.MaxValue, biggestYard = 0, mostStreets = 0;
        string? last = null;
        // The personality matrix (note 474): each custom's people's mean traits and temperaments, and the bynamed share.
        var minds = new SortedDictionary<string, List<TownPersonality>>(StringComparer.Ordinal);
        string[] industries = [.. towns.Writing.Industries.Keys.Order(StringComparer.Ordinal)];
        for (int i = 1; i <= seeds; i++)
        {
            var site = new TownSite($"Fort {towns.Surnames[i % towns.Surnames.Count]}", industries[i % industries.Length], 1100, (ulong)i, roster, last);
            var plan = TownGenerator.Generate(towns, site);
            cultures[plan.Culture] = cultures.GetValueOrDefault(plan.Culture) + 1;
            repeats += plan.Culture == last ? 1 : 0;
            if (!minds.TryGetValue(plan.Culture, out var those))
                minds[plan.Culture] = those = [];
            those.AddRange(plan.People.Select(p => p.Personality).OfType<TownPersonality>());
            last = plan.Culture;
            longestLine = Math.Max(longestLine, plan.People.SelectMany(p => p.Lines).Max(l => l.Length));
            longestPaper = Math.Max(longestPaper, plan.Papers.Max(p => p.Text.Length));
            minPeople = Math.Min(minPeople, plan.People.Count);
            maxPeople = Math.Max(maxPeople, plan.People.Count);
            (minPop, maxPop) = (Math.Min(minPop, plan.Population), Math.Max(maxPop, plan.Population));
            maxHouses = Math.Max(maxHouses, plan.Houses.Count);
            if (plan.Bounds is { } b)
                (walled, smallestWalled, mostStreets) = (walled + 1, Math.Min(smallestWalled, plan.Population), Math.Max(mostStreets, b.Streets.Count / 2));
            else
                biggestYard = Math.Max(biggestYard, plan.Population);
            longestLine = Math.Max(longestLine, plan.Fixtures.Select(f => f.Text.Length).DefaultIfEmpty(0).Max() > 420 ? 9999 : longestLine);
        }
        return new
        {
            seeds,
            roster = roster.Length == 0 ? "all" : string.Join(",", roster),
            cultures,
            repeatsOfTheLast = repeats,
            longestLine,
            longestPaper,
            people = new[] { minPeople, maxPeople },
            population = new[] { minPop, maxPop },
            maxHouses,
            personalities = minds.ToDictionary(m => m.Key, m => new
            {
                people = m.Value.Count,
                traits = TownTraits.Axes.Select((a, i) => (a, v: Math.Round(m.Value.Average(p => p.Traits[i]), 2))).ToDictionary(x => x.a, x => x.v),
                temperaments = new SortedDictionary<string, string>(m.Value.GroupBy(p => p.Temperament)
                    .ToDictionary(g => g.Key, g => $"{100.0 * g.Count() / m.Value.Count:0}%"), StringComparer.Ordinal),
                bynamed = $"{100.0 * m.Value.Count(p => p.Byname.Length > 0) / m.Value.Count:0}%",
            }),
            walled = new { towns = walled, smallest = walled > 0 ? smallestWalled : 0, biggestUnwalled = biggestYard, mostStreetsASide = mostStreets }
        };
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
