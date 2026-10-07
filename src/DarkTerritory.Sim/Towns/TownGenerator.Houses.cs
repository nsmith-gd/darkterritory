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
        /// <summary>The town's character (houses.json), its houses' designs drawn by it.</summary>
        public string Character = "";
        /// <summary>A walled town's extent and streets (queue #74), or null where its houses are the line's street's alone.</summary>
        public TownBounds? Bounds;
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

        // The households: two to six apiece (towns.json "household").
        var hrng = rngFor("households");
        var sizes = new List<int>();
        for (int left = population; left > 0;)
        {
            int n = Math.Min(left, hrng.RangeInclusive(t.Household[0], t.Household[1]));
            sizes.Add(n);
            left -= n;
        }
        double average = (t.Household[0] + t.Household[1]) / 2.0;
        // The houses of the people the town lost, no more than its share of the households (towns.json lostShare): a big
        // town that lost most of itself leaves the rest as nothing at all.
        int lostWanted = Math.Clamp((int)Math.Round((former - population) / average), 0, (int)Math.Min(int.MaxValue / 2, sizes.Count * t.LostShare));
        int needed = sizes.Count + lostWanted;

        // Where a house can stand. First the line's own street (note 107's: every st.Every m, st.Out out, from st.FromGate
        // inside the gate, out of the square's way and off the home platform). Then, while that isn't lots enough, a walled
        // town's streets beside the line (queue #74; towns.json "walled"), a row of houses either side of each, lanes across.
        int side = square.Side;
        var lots = new List<(double S, double D, int Side, double Frontage)>();
        for (double s = Math.Floor((site.Gate - st.FromGate) / st.Every) * st.Every; s > 25; s -= st.Every)
            foreach (int sd in new[] { side, -side })
            {
                if (sd == side && s > square.S0 - 6 && s < square.S1 + 6)
                    continue;
                if (sd == -side && s > 50 && s < site.Gate - 20)
                    continue;
                lots.Add((s, sd * st.Out, sd, st.Every));
            }
        var wt = t.Walled;
        var lrng = rngFor("houses.lots");
        double s0 = wt.From, s1 = site.Gate - wt.ToGate, depthMax = st.Depth[1];
        var streets = new List<TownStreet>();
        var laneAt = new List<double>();
        if (lots.Count < needed)
        {
            // Lanes across, the ways from the line out to the streets: never through the square (its buildings back onto
            // where its far wall was), so one that would be is moved to just past it. The line's own row leaves them clear.
            double sqFrom = square.S0 - 6 - wt.LaneWidth / 2, sqTo = square.S1 + 6 + wt.LaneWidth / 2;
            for (double s = s0 + lrng.Range(wt.LaneEvery[0], wt.LaneEvery[1]); s < s1 - wt.LaneEvery[0] / 2; s += lrng.Range(wt.LaneEvery[0], wt.LaneEvery[1]))
            {
                if (s > sqFrom && s < sqTo)
                    s = sqTo;
                if (s < s1 - wt.LaneEvery[0] / 2)
                    laneAt.Add(Math.Round(s, 2));
            }
            lots.RemoveAll(l => laneAt.Any(a => Math.Abs(a - l.S) < l.Frontage / 2 + wt.LaneWidth / 2));
        }
        int count = 0;
        while (lots.Count < needed && count < wt.MaxStreets)
        {
            count++;
            double c = wt.First + (count - 1) * wt.Every, off = wt.Width / 2 + wt.Setback + depthMax / 2;
            foreach (int sd in new[] { side, -side })
            {
                streets.Add(new TownStreet(sd * c, s0, s1, wt.Width));
                // Its line-side row faces out from the line, its far row faces back toward it: both front the street.
                foreach (var (d, facing) in new[] { (sd * (c - off), -sd), (sd * (c + off), sd) })
                    for (double s = s0; ;)
                    {
                        double lot = lrng.Range(wt.Lot[0], wt.Lot[1]);
                        if (s + lot > s1)
                            break;
                        double mid = s + lot / 2;
                        bool lane = laneAt.Any(l => Math.Abs(l - mid) < lot / 2 + wt.LaneWidth / 2);
                        bool inSquare = sd == side && mid > square.S0 - 6 && mid < square.S1 + 6 && Math.Abs(d) - depthMax / 2 < Math.Abs(square.WallD) + 3;
                        if (!lane && !inSquare)
                            lots.Add((Math.Round(mid, 3), d, facing, lot));
                        s += lot;
                    }
            }
        }
        // Nearest the square first: the town grows out from its heart, its lost at its edges, nothing past them.
        double heartS = (square.S0 + square.S1) / 2, heartD = side * 15;
        lots = [.. lots.OrderBy(l => (l.S - heartS) * (l.S - heartS) + (l.D - heartD) * (l.D - heartD)).ThenBy(l => l.Side == side ? 0 : 1)];
        if (count > 0)
        {
            double reach = wt.First + (count - 1) * wt.Every + wt.Width / 2 + wt.Setback + depthMax + wt.Margin;
            homes.Bounds = new TownBounds(wt.Rear, site.Gate, reach, reach, streets,
                [.. laneAt.Select(l => new TownLane(l, -reach, reach, wt.LaneWidth))]);
        }

        // More to a house where there aren't houses enough.
        while (sizes.Count > lots.Count && sizes.Count > 1)
        {
            sizes[^2] += sizes[^1];
            sizes.RemoveAt(sizes.Count - 1);
        }
        int lostHouses = Math.Clamp(lostWanted, 0, lots.Count - sizes.Count);
        int open = Math.Min(sizes.Count, Math.Clamp((int)Math.Round(population / Math.Max(1, t.Explorable.Per)), t.Explorable.Min, t.Explorable.Max));

        var looks = rngFor("houses");
        // The town's character (houses.json), and the stream its houses' designs come from: a fishing cove's shingled
        // gable-fronts, an old town's painted bumps, a company town's one house over and over in its own paints.
        var drng = rngFor("houses.design");
        var character = HouseDesigner.Character(content.Looks, site.Industry, ref drng);
        homes.Character = character.Id;
        HouseDesign? model = null;
        var knocks = new Deck<string>(w.HouseKnocks, rngFor("houses.knocks"));
        var stories = new Deck<TownHousehold>(w.Households, rngFor("houses.stories"));
        var families = new Deck<string>([.. content.Surnames], rngFor("houses.families"));
        var srng = rngFor("houses.spots");
        string Empty(string kind) => w.EmptyHouses.TryGetValue(kind, out var texts) && texts.Length > 0 ? looks.Pick(texts) : "";
        for (int i = 0; i < sizes.Count + lostHouses; i++)
        {
            var (s, d0, sd, frontage) = lots[i];
            bool gable = model?.GableFront ?? drng.Chance(character.GableFront);
            double width = model is null ? looks.Range(gable ? st.GableWidth[0] : st.Width[0], gable ? st.GableWidth[1] : st.Width[1]) : homes.Houses[0].Width;
            double depth = model is null ? looks.Range(st.Depth[0], st.Depth[1]) : homes.Houses[0].Depth;
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
            // No wider than its lot (a street's lots run 13 to 17 m), with room either side for a wing before the next lot's
            // house (whose wing may come this way).
            width = Math.Min(width, frontage - 2);
            double room = (frontage - width) / 2 - 0.6;
            var layout = kind == HouseKind.Open ? HouseLayout.For(width, depth, looks.Chance(0.5) ? 1 : -1) : null;
            var design = model is not null && character.Uniform
                ? HouseDesigner.Copy(content.Looks, character, model, layout?.DoorU, ref drng)
                : HouseDesigner.Draw(content.Looks, character, gable, width, depth, room, layout is not null, layout?.DoorU, ref drng);
            if (character.Uniform)
                model ??= design;
            var house = new TownHouse(i, s, d0, sd, width, depth, kind, design, family, text, layout);
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
