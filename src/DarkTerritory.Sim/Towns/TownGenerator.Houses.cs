using Ballast;

namespace DarkTerritory.Sim.Towns;

/// <summary>
/// The town's houses and the households in them (the director, 7 Oct 2026: towns of 20 to 350, "fully interior modeled
/// and explorable for some of them with residents"; note 281). Down the yard's street from the square: the lived-in
/// houses nearest it, a few of those standing open with their household at home, then the houses of the people the town
/// has lost (boarded, burnt, left open), then nothing.
/// </summary>
public static partial class TownGenerator
{
    /// <summary>A town's houses as made, and what's needed to give its households their words.</summary>
    sealed class Homes
    {
        public readonly List<TownHouse> Houses = [];
        public readonly List<Spot> Residents = [];
        readonly Dictionary<int, (TownHousehold Household, Dictionary<string, string> Vars, Dictionary<string, Queue<string>> Lines)> _open = [];

        public void Open(int house, TownHousehold household, Dictionary<string, string> vars, Pcg32 rng)
        {
            // Each part's lines shuffled once, dealt to whoever has that part, so two parents don't say the same.
            var lines = household.Lines.ToDictionary(kv => kv.Key, kv => new Queue<string>(new Deck<string>(kv.Value, rng.Fork((ulong)kv.Key.Length)).All()));
            _open[house] = (household, vars, lines);
        }

        public TownHousehold? HouseholdOf(int house) => _open.TryGetValue(house, out var o) ? o.Household : null;

        public IReadOnlyDictionary<string, string>? Vars(int house) => _open.TryGetValue(house, out var o) ? o.Vars : null;

        /// <summary>A household member's own lines, their part's: two at most.</summary>
        public IEnumerable<string> Story(int house, string part)
        {
            if (!_open.TryGetValue(house, out var o) || !o.Lines.TryGetValue(part, out var queue))
                yield break;
            for (int i = 0; i < 2 && queue.Count > 0; i++)
                yield return queue.Dequeue();
        }

        /// <summary>What a household member is called on their card: their part in the house.</summary>
        public string Title(int house, string part) => part switch
        {
            "elder" => "the {family}s' eldest",
            "child" => "the {family}s' youngest",
            "lodger" => "lodging with the {family}s",
            _ => "of the {family} house",
        };
    }

    static Homes Houses(TownContent content, TownSite site, TownSquare square, int population, int former, Func<string, Pcg32> rngFor)
    {
        var t = content.Tuning;
        var st = t.Houses;
        var w = content.Writing;
        var homes = new Homes();

        // Where a house can stand: every st.Every m down the street on both sides, from st.FromGate inside the gate, out of
        // the square's way and off the home platform (right of the line, note 107), nearest the square first.
        int side = square.Side;
        var lots = new List<(double S, int Side)>();
        for (double s = Math.Floor((site.Gate - st.FromGate) / st.Every) * st.Every; s > 25; s -= st.Every)
            foreach (int sd in new[] { side, -side })
            {
                if (sd == side && s > square.S0 - 6 && s < square.S1 + 6)
                    continue;
                if (sd == -side && s > 50 && s < site.Gate - 20)
                    continue;
                lots.Add((s, sd));
            }
        lots = [.. lots.OrderBy(l => Math.Abs(l.S - (square.S0 + square.S1) / 2)).ThenBy(l => l.Side == side ? 0 : 1)];

        // The households: two to six apiece (towns.json "household"), more to a house where there aren't houses enough.
        var hrng = rngFor("households");
        var sizes = new List<int>();
        for (int left = population; left > 0;)
        {
            int n = Math.Min(left, hrng.RangeInclusive(t.Household[0], t.Household[1]));
            sizes.Add(n);
            left -= n;
        }
        while (sizes.Count > lots.Count && sizes.Count > 1)
        {
            sizes[^2] += sizes[^1];
            sizes.RemoveAt(sizes.Count - 1);
        }
        double average = (t.Household[0] + t.Household[1]) / 2.0;
        int lostHouses = Math.Clamp((int)Math.Round((former - population) / average), 0, lots.Count - sizes.Count);
        int open = Math.Min(sizes.Count, Math.Clamp((int)Math.Round(population / Math.Max(1, t.Explorable.Per)), t.Explorable.Min, t.Explorable.Max));

        var looks = rngFor("houses");
        var knocks = new Deck<string>(w.HouseKnocks, rngFor("houses.knocks"));
        var stories = new Deck<TownHousehold>(w.Households, rngFor("houses.stories"));
        var families = new Deck<string>([.. content.Surnames], rngFor("houses.families"));
        var srng = rngFor("houses.spots");
        string Empty(string kind) => w.EmptyHouses.TryGetValue(kind, out var texts) && texts.Length > 0 ? looks.Pick(texts) : "";
        for (int i = 0; i < sizes.Count + lostHouses; i++)
        {
            var (s, sd) = lots[i];
            double width = looks.Range(st.Width[0], st.Width[1]), depth = looks.Range(st.Depth[0], st.Depth[1]);
            int style = looks.RangeInclusive(0, 3), paint = looks.RangeInclusive(0, 6);
            string family = families.Next() ?? looks.Pick(content.Surnames);
            HouseKind kind;
            string text;
            if (i < sizes.Count)
            {
                kind = i < open ? HouseKind.Open : HouseKind.Lived;
                text = kind == HouseKind.Lived ? knocks.Next() ?? "Nobody answers." : "";
            }
            else
            {
                double roll = looks.NextDouble();
                kind = roll < st.Burnt ? HouseKind.Burnt : roll < st.Burnt + st.Open ? HouseKind.Empty : HouseKind.Boarded;
                text = Empty(kind switch { HouseKind.Burnt => "burnt", HouseKind.Empty => "open", _ => "boarded" });
            }
            var layout = kind == HouseKind.Open ? HouseLayout.For(width, depth, looks.Chance(0.5) ? 1 : -1) : null;
            var house = new TownHouse(i, s, sd * st.Out, sd, width, depth, kind, style, paint, family, text, layout);
            homes.Houses.Add(house);
            if (layout is null || stories.Next() is not { } household)
                continue;

            // The household at home: always a parent, then whoever else its story has a part for, as many as live here.
            // A household's {other} is one town (the lodgers came from it, the letters go to it), not a new one a line.
            var vars = new Dictionary<string, string> { ["{family}"] = family };
            if (w.Places.Length > 0)
                vars["{other}"] = srng.Pick(w.Places.Where(p => !site.Name.Contains(p, StringComparison.OrdinalIgnoreCase)).ToList());
            if (household.Absent)
                vars["{absent}"] = srng.Pick(w.FirstNames);
            homes.Open(i, household, vars, srng);
            var parts = new List<string> { "parent" };
            foreach (string part in new[] { "elder", "child", "lodger", "parent" })
                if (parts.Count < Math.Min(sizes[i], 4) && household.Lines.ContainsKey(part) && (part != "parent" || sizes[i] >= 4))
                    parts.Add(part);
            var taken = new HashSet<string>();
            foreach (string part in parts)
            {
                // Where each part is likeliest found: the old at the window or in the chair, a parent at the range or the
                // table, a child at the table or the stair door, a lodger by the stairs or the door.
                string[] wants = part switch
                {
                    "elder" => household.Id == "window" ? ["window", "chair"] : ["chair", "window"],
                    "child" => ["table", "stairs", "chair"],
                    "lodger" => ["stairs", "door"],
                    _ => ["stove", "table", "door"],
                };
                var spot = wants.Select(name => layout.Spots.FirstOrDefault(x => x.Name == name)).FirstOrDefault(x => x is not null && !taken.Contains(x.Name))
                    ?? layout.Spots.First(x => !taken.Contains(x.Name));
                taken.Add(spot.Name);
                var (ps, pd) = house.Rail(spot.U, spot.V);
                var (fs, fd) = house.Facing(spot.FaceU, spot.FaceV);
                homes.Residents.Add(new Spot("", ps, pd, fs, fd, 0, House: i, Pose: spot.Pose, Part: part));
            }
        }
        return homes;
    }
}
