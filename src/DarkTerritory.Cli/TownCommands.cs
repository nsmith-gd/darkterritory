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
        if (args.Length > 1 && args[1] == "standing")
            return Standing(content, towns, roster, args);
        string spec = Str(args, "--route", "frontier:7");
        var route = Routes.Generate(content, spec, (int)Opt(args, "--cars", 6));
        double gate = route.GateOr(RouteTuning.Load(content).YardLength);
        var site = TownSite.Of(route, gate, roster, towns, Str(args, "--last", "") is { Length: > 0 } last ? last : null);
        // --form: the town made as that type (note 692; "street" the street town of note 335), whatever its trade draws.
        if (Str(args, "--form", "") is { Length: > 0 } formArg)
            site = site with { Form = formArg == "street" ? "" : formArg };
        var plan = TownGenerator.Generate(towns, site);
        // `dt town map`: the town's plan top-down as a PNG (note 692).
        if (args.Length > 1 && args[1] == "map")
        {
            string png = Str(args, "--out", $"out/towns/{spec.Replace(':', '-')}{(plan.Form.Length > 0 ? "-" + plan.Form : "")}.png");
            DarkTerritory.Game.TownMap.Save(plan, png, (int)Opt(args, "--width", 1800));
            // Its climbs (note 692): each stair or ramp's ends along its frame and their levels.
            var climbs = plan.Ways.Where(w => w.Levels is not null).Select(w => new
            {
                from = new[] { Math.Round(w.Points[0].S, 1), Math.Round(w.Points[0].D, 1), Math.Round(w.Levels![0], 1) },
                to = new[] { Math.Round(w.Points[^1].S, 1), Math.Round(w.Points[^1].D, 1), Math.Round(w.Levels[^1], 1) },
            });
            var ground = TownGround.Of(plan);
            return new
            {
                png,
                plan.Name,
                plan.Form,
                plan.Population,
                houses = plan.Houses.Count,
                plan.Laid,
                plan.Frame,
                plan.Margin,
                climbs,
                highest = ground.Flat ? 0 : Math.Round(plan.Houses.Max(h => h.Level), 1),
                lowest = ground.Flat ? 0 : Math.Round(plan.Houses.Min(h => h.Level), 1),
                districts = plan.Districts.Where(d => d.Name.Length > 0).Select(d => $"{d.Kind} {d.Id} {d.Name}")
            };
        }
        return new
        {
            route = spec,
            plan.Name,
            plan.Population,
            plan.Former,
            plan.Character,
            houses = plan.Houses.GroupBy(h => h.Kind).ToDictionary(g => g.Key.ToString(), g => g.Count()),
            // Its houses with floors (note 1022): which go up, which have a cellar, a burnt one's, and the boarded left open.
            floors = new
            {
                upstairs = plan.Houses.Where(h => h.Upstairs).Select(h => h.Id).ToList(),
                cellars = plan.Houses.Where(h => h.Cellar && h.Kind != HouseKind.Burnt).Select(h => h.Id).ToList(),
                burnt = plan.Houses.Where(h => h.Cellar && h.Kind == HouseKind.Burnt).Select(h => h.Id).ToList(),
                left = plan.Houses.Where(h => h.Left is not null).Select(h => h.Id).ToList(),
            },
            // What stands between the houses (note 989), by kind.
            between = plan.Between.GroupBy(p => p.Kind).OrderBy(g => g.Key).ToDictionary(g => g.Key.ToString(), g => g.Count()),
            // Nothing ruled straight (note 490): how many stand off true and how far, and the row across the green's far
            // street, turned to it (along the line from the green's middle, degrees).
            turned = new
            {
                houses = plan.Houses.Count(h => h.Turn != 0),
                meanDegrees = plan.Houses.Where(h => h.Turn != 0).Select(h => Math.Abs(h.Turn) * 180 / Math.PI).DefaultIfEmpty().Average() is var m ? Math.Round(m, 1) : 0,
                crescent = plan.Green is { } g && plan.Bounds is not null
                    ? plan.Houses.Where(h => Math.Sign(h.D) == g.Side && Math.Abs(h.D) > g.Far + 6 && Math.Abs(h.D) < g.Far + 20 && h.S > g.S0 - 8 && h.S < g.S1 + 8)
                        .OrderBy(h => h.S).Select(h => new { along = Math.Round(h.S - (g.S0 + g.S1) / 2), degrees = Math.Round(h.Turn * 180 / Math.PI, 1) }).ToList()
                    : null,
            },
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
            // Its rail yard (note 502): where it runs behind the platform, its siding, and each piece from its start, how far out.
            yard = plan.Yard is { } ry ? new
            {
                along = new[] { Math.Round(ry.S0 - gate, 1), Math.Round(ry.S1 - gate, 1) },
                across = new[] { Math.Round(ry.Side * ry.Near, 1), Math.Round(ry.Side * ry.Far, 1) },
                siding = new[] { Math.Round(ry.Siding0 - ry.S0, 1), Math.Round(ry.Siding1 - ry.S0, 1), Math.Round(ry.Side * ry.SidingD, 1) },
                pieces = plan.Fixtures.Where(f => ry.Holds(f.S, f.D) && f.Kind != "yardlamp")
                    .Select(f => $"{f.Kind} at {f.S - ry.S0:0.0}, {f.D:0.0} out ({f.Name})"),
                lamps = plan.Fixtures.Count(f => ry.Holds(f.S, f.D) && f.Kind == "yardlamp"),
                porters = plan.People.Count(p => p.Role == "porter" && ry.Holds(p.S, p.D)),
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
    /// <summary>
    /// `dt town standing [--route frontier:7] [--brought 1] [--nights 6] [--mode welcome|guards|off]` (note 1024; GDD §3.1 "A town
    /// remembers"): a campaign's crew brings <c>--brought</c> creatures into the route's terminus town, then rides
    /// <c>--nights</c> more nights elsewhere, settled as the app settles them. Each night: how cold the town is to the crew, what
    /// its blacksmith asks (and another town's for the same thing), what its gatekeeper says first, how many of its people are
    /// wary, its doors shut and its waves unreturned, and with fewer guards its yard's fight and burns.
    /// </summary>
    static object Standing(string content, TownContent towns, string[] roster, string[] args)
    {
        var standing = towns.Tuning.Standing;
        if (Str(args, "--mode", "") is { Length: > 0 } mode)
            standing = standing with { Mode = Enum.Parse<StandingMode>(mode, ignoreCase: true) };
        var content2 = towns with { Tuning = towns.Tuning with { Standing = standing } };
        var t = DataFile.Load<DarkTerritory.Sim.Campaign.CampaignTuning>(Path.Combine(content, DarkTerritory.Sim.Campaign.CampaignTuning.File));
        var run = DataFile.Load<DarkTerritory.Sim.Run.RunTuning>(Path.Combine(content, DarkTerritory.Sim.Run.RunTuning.File));
        string spec = Str(args, "--route", "frontier:7");
        var route = Routes.Generate(content, spec, (int)Opt(args, "--cars", 6));
        double gate = route.GateOr(RouteTuning.Load(content).YardLength);
        var trades = LineGenContent.Cached(content).Config.Tiers.Fortress.Identities;
        var town = TownAt.Terminus(route, trades) ?? throw new InvalidOperationException($"{spec} ends at a silent settlement: no town to bring anything into");
        var other = new TownAt("Elsewhere", town.Industry, town.Seed + 1);
        int brought = (int)Opt(args, "--brought", 1), nights = (int)Opt(args, "--nights", 6);
        var upgrade = t.Upgrades.First(u => u.Effect.Count > 0);
        var s = DarkTerritory.Sim.Campaign.Campaign.New(t, 1, "standing", 1);
        DarkTerritory.Sim.Run.RunReport Night(DarkTerritory.Sim.Run.RunEnd end, int n) =>
            new(end, 1000, 7, 6, 0, 6, 1000, 50, 0, 0, 950, 4, 0) { BroughtIn = n, TownBurnt = n * run.BroughtIn.Burns };
        // Tonight: delivered into the town with the creatures on the cars.
        s = DarkTerritory.Sim.Campaign.Campaign.Arrived(DarkTerritory.Sim.Campaign.Campaign.Settle(s, Night(DarkTerritory.Sim.Run.RunEnd.Delivered, brought), t, standing),
            Night(DarkTerritory.Sim.Run.RunEnd.Delivered, brought), town, standing);
        var rows = new List<object>();
        for (int night = 0; night <= nights; night++)
        {
            int cold = s.Standing.GetValueOrDefault(town.Key);
            var here = s with { Town = town with { Burnt = s.Burnt.GetValueOrDefault(town.Key), Cold = standing.On ? cold : 0 } };
            var plan = TownGenerator.Generate(content2, new TownSite(town.Name, town.Industry, gate, town.Seed, roster, null, here.Town!.Burnt) { Cold = here.Town.Cold });
            var gatekeeper = plan.People.FirstOrDefault(p => p.Role == "gatekeeper");
            var smith = plan.People.FirstOrDefault(p => p.Role == "blacksmith");
            var said = towns.Writing.BroughtIn;
            rows.Add(new
            {
                nightsAfter = night,
                cold,
                price = DarkTerritory.Sim.Campaign.Campaign.UpgradeCost(t, here, upgrade, standing),
                elsewhere = DarkTerritory.Sim.Campaign.Campaign.UpgradeCost(t, s with { Town = other }, upgrade, standing),
                gatekeeper = gatekeeper?.Lines.Take(2),
                smith = smith?.Lines.FirstOrDefault(),
                wary = plan.People.Count(p => said is not null && p.Lines.Any(l => said.Wary.Any(w => l == w.Replace("{town}", plan.Name)))),
                people = plan.People.Count,
                doorsShut = plan.Houses.Count(h => said is not null && h.Kind == HouseKind.Lived && said.Door.Any(d => h.Text == d.Replace("{town}", plan.Name).Replace("{family}", h.Family))),
                knockable = plan.Houses.Count(h => h.Kind == HouseKind.Lived && h.Layout is null),
                waveBack = Math.Round(1 - standing.Waves * standing.Of(plan.Cold), 2),
                guardsTurnOut = plan.People.Count(p => Town.TurnsOut(p, plan, standing)),
                fightSeconds = run.BroughtIn.FightSeconds * standing.Fight(plan.Cold),
                burnsEach = run.BroughtIn.Burns + standing.Burns(plan.Cold),
            });
            // A night elsewhere, settled: it eases.
            s = DarkTerritory.Sim.Campaign.Campaign.Settle(s, Night(DarkTerritory.Sim.Run.RunEnd.Delivered, 0), t, standing);
        }
        return new { town = town.Name, mode = standing.Mode.ToString(), brought, upgrade = upgrade.Id, listPrice = DarkTerritory.Sim.Campaign.Campaign.UpgradeCost(t, s with { Town = null }, upgrade), nights = rows };
    }

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
