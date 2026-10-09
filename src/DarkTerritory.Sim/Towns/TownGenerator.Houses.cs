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
        /// <summary>What the town breathes through (its character's <see cref="HouseCharacter.Gear"/>).</summary>
        public IReadOnlyDictionary<string, double> Gear = new Dictionary<string, double> { ["respirator"] = 1 };
        /// <summary>A walled town's extent and streets (queue #74), or null where its houses are the line's street's alone.</summary>
        public TownBounds? Bounds;
        /// <summary>A walled town's green, across the first street from the square (note 353).</summary>
        public TownGreen? Green;
        /// <summary>A walled town's works, across the line from the green (note 353).</summary>
        public TownWorks? Works;
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
        /// <summary>Nicki's party's words, while there's one (note 527).</summary>
        public FolkParty? Party;

        public string Title(int house, string part) => part switch
        {
            "host" => Party?.Title ?? "",
            "guest" => Party?.GuestTitle ?? "",
            "elder" => "the {family}s' eldest",
            "child" => "the {family}s' youngest",
            "lodger" => "lodging with the {family}s",
            _ => "of the {family} house",
        };
    }

    /// <summary>How near a lane comes to a lot for its yard to be fenced along it (m; note 353).</summary>
    const double LaneFence = 6;

    /// <summary>The least room between a house's front and its picket fence (m): a step and a path.</summary>
    const double FenceOffStep = 2.6;

    /// <summary>How far in from the square's ends the green across the street from it starts (m).</summary>
    const double GreenIn = 4;

    static Homes Houses(TownContent content, TownSite site, TownSquare square, int population, int former, Func<string, Pcg32> rngFor,
        TownFolk? folk = null)
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
        // Each lot's row: 0 the line's own street, 2k - 1 and 2k the line side and the far side of the k-th street out.
        var lots = new List<(double S, double D, int Side, double Frontage, int Row)>();
        for (double s = Math.Floor((site.Gate - st.FromGate) / st.Every) * st.Every; s > 25; s -= st.Every)
            foreach (int sd in new[] { side, -side })
            {
                if (sd == side && s > square.S0 - 6 && s < square.S1 + 6)
                    continue;
                if (sd == -side && s > 50 && s < site.Gate - 20)
                    continue;
                lots.Add((s, sd * st.Out, sd, st.Every, 0));
            }
        var wt = t.Walled;
        var lrng = rngFor("houses.lots");
        double s0 = wt.From, s1 = site.Gate - wt.ToGate, depthMax = st.Depth[1];
        var streets = new List<TownStreet>();
        var lanes = new List<TownLane>();
        double off = wt.Width / 2 + wt.Setback + depthMax / 2;
        // Where the k-th street out (1 up) runs straight, and the back line between its far row and the next street's
        // near one (0: between the line's own row and the first street's): distances out from the line.
        double Street(int k) => wt.First + (k - 1) * wt.Every;
        double Back(int k) => k == 0 ? (st.Out + Street(1) - off) / 2 : Street(k) + wt.Every / 2;
        // The streets bend (note 353; the director, 8 Oct: "Towns dont feel like they have a natural layout to them"): each
        // side's on one wave, more for each street out, so the rows between them keep their room; straight by the square
        // and its green. And a house stands back from its street by a little more or less than the next.
        var brng = rngFor("houses.bends");
        var waves = new Dictionary<int, TownWave>();
        foreach (int sd in new[] { side, -side })
            waves[sd] = new TownWave(brng.Range(0, 400), brng.Range(wt.BendWavelength[0], wt.BendWavelength[1]),
                square.S0 - wt.BendClear, square.S1 + wt.BendClear, 40);
        double Amp(int k) => k <= 0 ? 0 : Math.Min(wt.BendMax, wt.BendBase + wt.BendStep * (k - 1));
        // How far out from straight the k-th street (and its rows) is at s, on side sd (0 for the line's own row).
        double Swing(int sd, int k, double s) => k <= 0 ? 0 : Amp(k) * waves[sd].At(s);
        // The works' stretch along the line (note 353), across it from the square and its green.
        double worksFrom = Math.Max(s0, square.S0 - wt.WorksBefore), worksTo = Math.Min(s1, square.S1 + wt.WorksPast);
        if (lots.Count < needed)
        {
            // Lanes across, the ways from the line out to the streets: never through the square (its buildings back onto
            // where its far wall was) or the works across the line from it, so one that would be is moved to just past
            // them. The line's own row leaves them clear.
            double sqFrom = Math.Min(square.S0 - 6, worksFrom) - wt.LaneWidth / 2, sqTo = Math.Max(square.S1 + 6, worksTo) + wt.LaneWidth / 2;
            var laneAt = new List<double>();
            for (double s = s0 + lrng.Range(wt.LaneEvery[0], wt.LaneEvery[1]); s < s1 - wt.LaneEvery[0] / 2; s += lrng.Range(wt.LaneEvery[0], wt.LaneEvery[1]))
            {
                if (s > sqFrom && s < sqTo)
                    s = sqTo;
                if (s < s1 - wt.LaneEvery[0] / 2)
                    laneAt.Add(Math.Round(s, 2));
            }
            // And they run crooked (note 353; the director, 8 Oct: "Towns dont feel like they have a natural layout"): where
            // a lane meets each street it turns a few metres one way or the other, so no two crossings line up and a lane is
            // never a sight-line from the line to the wall. It wanders no more than two turns from where it crosses the line
            // (the next lane is further than that), never into the square, nor across the green beyond it, nor through the
            // works across the line from them (note 353).
            var jrng0 = rngFor("houses.lanes");
            double wander = 2 * wt.LaneJog[1];
            foreach (double at in laneAt)
            {
                var kinks = new List<(double D, double S)> { (0, at) };
                foreach (int sd in new[] { -1, 1 })
                {
                    double s = at;
                    for (int k = 1; k <= wt.MaxStreets; k++)
                    {
                        double jog = jrng0.Range(wt.LaneJog[0], wt.LaneJog[1]) * (jrng0.Chance(0.5) ? 1 : -1);
                        s = Math.Clamp(s + jog, Math.Max(at - wander, s0 + wt.LaneWidth), Math.Min(at + wander, s1 - wt.LaneWidth));
                        if (k <= 2 && s > sqFrom && s < sqTo)
                            s = at <= sqFrom ? sqFrom : sqTo;
                        kinks.Add((sd * Street(k), Math.Round(s, 2)));
                    }
                }
                lanes.Add(new TownLane(at, 0, 0, wt.LaneWidth, [.. kinks.OrderBy(x => x.D)]));
            }
            lots.RemoveAll(l => InLane(l.S, l.Frontage, l.Side, 0));
        }
        // A lot's ground out from the line on side sd, at s along it: from its street to its back line, halfway to the row
        // behind (as RowYards has them), where the bend has them there. (The outermost row's yards run on to the wall, but
        // a lane runs straight on from its last street: it's no wider there.)
        (double Near, double Far) Ground(int row, int sd, double s)
        {
            if (row == 0)
                return (0, Back(0) + Swing(sd, 1, s) / 2);
            int k = (row + 1) / 2;
            double c = Street(k), sw = Swing(sd, k, s);
            return row % 2 == 1
                ? (k == 1 ? (st.Out + c - off + sw) / 2 : Back(k - 1) + (Swing(sd, k - 1, s) + sw) / 2, c + sw)
                : (c + sw, Back(k) + (sw + Swing(sd, k + 1, s)) / 2);
        }
        // How far along the line the lanes come on either side of a lot on row, side sd, from s - frontage/2 to
        // s + frontage/2 (give or take how its street's bend moves over its frontage): the nearest lane's edge below it and
        // above it (in it if they cross).
        (double Below, double Above) Lanes(int row, int sd, double s, double frontage)
        {
            var (near, far) = Ground(row, sd, s);
            double below = double.MinValue, above = double.MaxValue;
            foreach (var lane in lanes)
            {
                var (lo, hi) = lane.Span(sd * (near - 1.5), sd * (far + 1.5));
                if (hi <= s)
                    below = Math.Max(below, hi);
                if (lo > s)
                    above = Math.Min(above, lo);
                if (lo <= s && hi > s)
                    (below, above) = (Math.Max(below, hi), Math.Min(above, lo));
            }
            return (below, above);
        }
        bool InLane(double s, double frontage, int sd, int row) =>
            Lanes(row, sd, s, frontage) is var (below, above) && (below > s - frontage / 2 || above < s + frontage / 2);
        var jrng = rngFor("houses.setbacks");
        int count = 0;
        while (lots.Count < needed && count < wt.MaxStreets)
        {
            count++;
            double c = Street(count);
            foreach (int sd in new[] { side, -side })
            {
                streets.Add(new TownStreet(sd * c, s0, s1, wt.Width, Amp(count), waves[sd]));
                // Its line-side row faces out from the line, its far row faces back toward it: both front the street.
                foreach (var (d, facing, row) in new[] { (sd * (c - off), -sd, 2 * count - 1), (sd * (c + off), sd, 2 * count) })
                    for (double s = s0; ;)
                    {
                        double lot = lrng.Range(wt.Lot[0], wt.Lot[1]);
                        if (s + lot > s1)
                            break;
                        double mid = s + lot / 2;
                        bool lane = InLane(mid, lot, sd, row);
                        bool inSquare = sd == side && mid > square.S0 - 6 && mid < square.S1 + 6 && Math.Abs(d) - depthMax / 2 < Math.Abs(square.WallD) + 3;
                        // The green (note 353): the first street's far row and the next street's near one, across from the square.
                        bool onGreen = sd == side && row is 2 or 3 && mid + lot / 2 > square.S0 + GreenIn && mid - lot / 2 < square.S1 - GreenIn;
                        // The works (note 353): the same rows on the line's other side, a little past the square's ends.
                        onGreen |= sd == -side && row is 2 or 3 && mid + lot / 2 > worksFrom && mid - lot / 2 < worksTo;
                        double back = jrng.Range(wt.SetbackJitter[0], wt.SetbackJitter[1]);
                        if (!lane && !inSquare && !onGreen)
                            lots.Add((Math.Round(mid, 3), d + sd * Swing(sd, count, mid) + facing * back, facing, lot, row));
                        s += lot;
                    }
            }
        }
        // Nearest the square first: the town grows out from its heart, its lost at its edges, nothing past them.
        double heartS = (square.S0 + square.S1) / 2, heartD = side * 15;
        lots = [.. lots.OrderBy(l => (l.S - heartS) * (l.S - heartS) + (l.D - heartD) * (l.D - heartD)).ThenBy(l => l.Side == side ? 0 : 1)];
        double reach = wt.First + (count - 1) * wt.Every + wt.Width / 2 + wt.Setback + depthMax + wt.Margin + wt.SetbackJitter[1] + Amp(count);
        if (count > 0)
        {
            homes.Green = new TownGreen(square.S0 + GreenIn, square.S1 - GreenIn, wt.First + wt.Width / 2,
                count > 1 ? wt.First + wt.Every - wt.Width / 2 : reach - wt.Margin, side);
            homes.Works = new TownWorks(worksFrom, worksTo, wt.First + wt.Width / 2,
                count > 1 ? wt.First + wt.Every - wt.Width / 2 : reach - wt.Margin, -side, site.Industry);
        }
        // A lane turns only where there's a street (straight on from the last to the wall).
        if (count > 0)
            homes.Bounds = new TownBounds(wt.Rear, site.Gate, reach, reach, streets,
                [.. lanes.Select(l => l with { D0 = -reach, D1 = reach, Kinks = [.. l.Kinks!.Where(k => Math.Abs(k.D) <= Street(count) + 1e-6)] })]);
        // A row's yards (note 335): the street's edge in front of it (none on the line's own street), and the back line its
        // yards run to: halfway to the row behind, or short of the wall. The line-side rows fence the back line between them
        // and the row behind (one fence, not two). Distances out from the line.
        double offRow = wt.Width / 2 + wt.Setback + depthMax / 2;
        // Where the streets bend, the edges and back lines go with them (a back line halfway between its two rows' swings),
        // each taken the cautious way over the lot's whole frontage (from s - half to s + half): a yard never reaches past
        // the nearest its back line comes, nor a fence into its street, so the rows either side can't meet (note 353).
        (double? Edge, double Back, bool Fenced) RowYards(int row, int sd, double s, double half)
        {
            (double Lo, double Hi) Over(Func<double, double> f)
            {
                double lo = double.MaxValue, hi = double.MinValue;
                for (int i = 0; i <= 4; i++)
                {
                    double v = f(s - half + half * i / 2);
                    (lo, hi) = (Math.Min(lo, v), Math.Max(hi, v));
                }
                return (lo, hi);
            }
            if (row == 0)
                return (null, count > 0 ? Over(x => (st.Out + wt.First - offRow) / 2 + Swing(sd, 1, x) / 2).Lo : Run.Fortresses.WallOut - Run.Fortresses.WallHalf - 0.3, false);
            int k = (row + 1) / 2;
            double c = wt.First + (k - 1) * wt.Every;
            // The line-side row: its street's edge out from it, its yard back toward the line. The far row: the other way.
            return row % 2 == 1
                ? (Over(x => c - wt.Width / 2 + Swing(sd, k, x)).Lo,
                    Over(x => k == 1 ? (st.Out + c - offRow + Swing(sd, k, x)) / 2 : c - wt.Every / 2 + (Swing(sd, k - 1, x) + Swing(sd, k, x)) / 2).Hi, true)
                : (Over(x => c + wt.Width / 2 + Swing(sd, k, x)).Hi,
                    k < count ? Over(x => c + wt.Every / 2 + (Swing(sd, k, x) + Swing(sd, k + 1, x)) / 2).Lo : reach - 1.5, false);
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
        // The yards on a stream of their own, so the houses are as they were (note 335).
        var yrng = rngFor("houses.yards");
        // The town's character (houses.json), and the stream its houses' designs come from: a fishing cove's shingled
        // gable-fronts, an old town's painted bumps, a company town's one house over and over in its own paints.
        var drng = rngFor("houses.design");
        var character = HouseDesigner.Character(content.Looks, site.Industry, ref drng);
        homes.Character = character.Id;
        homes.Gear = character.Gear;
        HouseDesign? model = null;
        var knocks = new Deck<string>(w.HouseKnocks, rngFor("houses.knocks"));
        var stories = new Deck<TownHousehold>(w.Households, rngFor("houses.stories"));
        // The families by the town's mix of peoples (note 474), or any of the province's.
        // Nicki's party (note 527): some towns, one of the open houses, on its own stream so the rest are as they were.
        var prng = rngFor("houses.party");
        int partyAt = folk?.Writing.Party is { } party && prng.Chance(t.Nicki) && open > 0 ? prng.RangeInclusive(0, open - 1) : -1;
        if (partyAt >= 0)
            homes.Party = folk!.Writing.Party;
        var families = folk is not null ? new Deck<string>(folk.Families(rngFor("houses.families")), null) : new Deck<string>([.. content.Surnames], rngFor("houses.families"));
        var srng = rngFor("houses.spots");
        string Empty(string kind) => w.EmptyHouses.TryGetValue(kind, out var texts) && texts.Length > 0 ? looks.Pick(texts) : "";
        for (int i = 0; i < sizes.Count + lostHouses; i++)
        {
            var (s, d0, sd, frontage, row) = lots[i];
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
            var house = new TownHouse(i, s, d0, sd, width, depth, kind, design, family, text, layout) { Party = i == partyAt && layout is not null };
            var (edge, back, fenced) = RowYards(row, Math.Sign(d0), s, frontage / 2);
            // A lot beside a lane is fenced along it (note 353): a lane runs between board fences, not across open yards.
            var (below, above) = lanes.Count > 0 ? Lanes(row, Math.Sign(d0), s, frontage) : (double.MinValue, double.MaxValue);
            int laneSides = (s - frontage / 2 - below < LaneFence ? 1 : 0) | (above - (s + frontage / 2) < LaneFence ? 2 : 0);
            var yard = Yard(house, character.Yard, frontage, edge * Math.Sign(d0), back * Math.Sign(d0), fenced, ref yrng, laneSides);
            // A lot beside the square can reach past its end (the lots keep out of it by their middles): nothing of its yard
            // stands in the square.
            yard.RemoveAll(y => InSquare(house, y, square));
            house = house with { Yard = yard };
            homes.Houses.Add(house);
            if (house.Party && layout is not null)
            {
                PartyAt(i, house, layout);
                continue;
            }
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

        // Nicki at the door with a glass for whoever comes in, waving them in; her guests about the rooms, dancing (one sat
        // at the table with the wine). Its "household" is the party's words: what she says first, the rest, the guests'.
        void PartyAt(int i, TownHouse house, HouseLayout layout)
        {
            var party = homes.Party!;
            var household = new TownHousehold
            {
                Id = "party",
                Lines = new Dictionary<string, string[]> { ["offer"] = party.Offer, ["host"] = party.Host, ["guest"] = party.Guests },
                Object = party.Wine,
            };
            homes.Open(i, household, new Dictionary<string, string> { ["{family}"] = house.Family }, srng);
            int guests = Math.Min(layout.Spots.Count - 1, prng.RangeInclusive(t.NickiGuests[0], t.NickiGuests[1]));
            var door = layout.Spots.FirstOrDefault(x => x.Name == "door") ?? layout.Spots[0];
            var order = new List<HouseSpot> { door };
            order.AddRange(layout.Spots.Where(x => x != door).Take(guests));
            for (int k = 0; k < order.Count; k++)
            {
                var spot = order[k];
                var (ps, pd) = house.Rail(spot.U, spot.V);
                // Nicki faces her door (the street beyond it), waving you in; her guests as their spots have them.
                var (fs, fd) = k == 0 ? house.Facing(layout.DoorU - spot.U, -1.5 - spot.V) : house.Facing(spot.FaceU, spot.FaceV);
                // One sat at the table with the wine; everyone else up dancing.
                bool sat = spot.Pose == "seated" && order.Take(k).All(x => x.Pose != "seated");
                string pose = k == 0 ? "wave" : sat ? "seated" : "dance";
                homes.Residents.Add(new Spot("", ps, pd, fs, fd, 0, House: i, Pose: pose, Part: k == 0 ? "host" : "guest"));
            }
        }
    }

    /// <summary>Whether a yard thing stands in the square (along it, on its side, short of its far wall).</summary>
    static bool InSquare(TownHouse h, YardThing y, TownSquare sq)
    {
        var (sa, da) = h.Rail(y.U0, y.V0);
        var (sb, db) = h.Rail(y.U1, y.V1);
        return Math.Sign(da) == sq.Side && Math.Max(sa, sb) > sq.S0 - 0.5 && Math.Min(sa, sb) < sq.S1 + 0.5
            && Math.Min(Math.Abs(da), Math.Abs(db)) < Math.Abs(sq.WallD) + 0.5;
    }

    /// <summary>
    /// A house's yard (the director's references, 7 Oct 2026: the picket fences of Lunenburg, the woodpiles, sheds and traps
    /// behind Peggy's Cove's houses; note 335), in its own frame. On a street, a picket fence along the street's edge with
    /// its gate in front of the door; on a line-side row, the board fence along the back line it shares with the row
    /// behind; and along the back, what a yard keeps (the character's <see cref="YardOdds"/>), as much as there's room
    /// for, clear of the house and a step behind it. The houses nobody lives in keep less, their fences more often down.
    /// Beside a lane (<paramref name="laneSides"/>: 1 the lot's end toward the rear wall, 2 its end toward the gate), a board
    /// fence down that side from the street's edge (the house's front, on the line's own row) to the back line.
    /// </summary>
    public static List<YardThing> Yard(TownHouse h, YardOdds odds, double frontage, double? edgeD, double backD, bool fenced, ref Pcg32 rng,
        int laneSides = 0)
    {
        var things = new List<YardThing>();
        bool lived = h.Kind is HouseKind.Lived or HouseKind.Open;
        double half = frontage / 2;
        // (By the house's number, not the yard's stream: the yards that aren't by a lane come out as they did. Most stand;
        // one in ten is down, or one in three where nobody lives.)
        if (laneSides != 0 && (uint)(h.Id * 2654435761u) % 30 >= (lived ? 3u : 10u))
        {
            double vFront = edgeD is { } e ? h.Side * (e - h.FrontD) + 0.25 : 0, vEnd = h.Side * (backD - h.FrontD);
            if ((laneSides & 1) != 0)
                things.Add(new(YardKind.Boards, -half, -half + 0.06, vFront, vEnd, 1.7));
            if ((laneSides & 2) != 0)
                things.Add(new(YardKind.Boards, half - 0.06, half, vFront, vEnd, 1.7));
        }
        // (Where a bending street comes close, there's no room for one off the step: note 353.)
        if (edgeD is { } edge && rng.Chance(lived ? odds.Picket : odds.Picket / 2) && h.Side * (edge - h.FrontD) + 0.25 < -FenceOffStep)
        {
            double v = h.Side * (edge - h.FrontD) + 0.25, gate = h.Design.DoorU;
            int worn = lived ? 0 : 1;
            if (gate - 0.75 > -half + 0.6)
                things.Add(new(YardKind.Picket, -half + 0.15, gate - 0.75, v, v + 0.06, 1.0, worn));
            if (gate + 0.75 < half - 0.6)
                things.Add(new(YardKind.Picket, gate + 0.75, half - 0.15, v, v + 0.06, 1.0, worn));
        }
        double vBack = h.Side * (backD - h.FrontD);
        if (fenced && rng.Chance(odds.Boards))
            things.Add(new(YardKind.Boards, -half, half, vBack - 0.03, vBack + 0.03, 1.7));
        // Behind the house (its wing too) and a step clear of it, to just short of the back line.
        double v0 = h.Parts().Select(p => p.V1).Append(h.Depth).Max() + 0.5, v1 = vBack - 0.15, room = v1 - v0;
        if (room < 0.7 || h.Kind == HouseKind.Burnt)
            return things;
        int n = rng.RangeInclusive(odds.Count[0], odds.Count[1]) - (lived ? 0 : 1);
        var kinds = new[] { YardKind.Woodpile, YardKind.Shed, YardKind.Privy, YardKind.Traps, YardKind.Dory, YardKind.Clothesline, YardKind.Barrel };
        var picked = new List<(YardKind Kind, double L, double D, double H, int Variant)>();
        for (int i = 0; i < n; i++)
        {
            // Its size (along the back, deep, high): what fits the room behind the house, each of the one-off things once.
            (double L, double D, double H) Size(YardKind k) => k switch
            {
                YardKind.Woodpile => (2.6, 0.8, 1.4),
                YardKind.Shed => (2.4, 2.0, 2.7),
                YardKind.Privy => (1.3, 1.3, 2.3),
                YardKind.Traps => (1.0, 0.65, 1.0),
                YardKind.Dory => (4.3, 1.4, 0.8),
                YardKind.Barrel => (0.65, 0.65, 0.95),
                _ => (0, 1.2, 2.1),
            };
            var fits = kinds.Where(k => Size(k).D <= room && odds.Weight(k) > 0
                && (k is YardKind.Woodpile or YardKind.Traps or YardKind.Barrel || picked.All(p => p.Kind != k))).ToList();
            if (fits.Count == 0)
                break;
            double total = fits.Sum(odds.Weight), roll = rng.NextDouble() * total;
            var kind = fits.Last();
            foreach (var k in fits)
                if ((roll -= odds.Weight(k)) < 0)
                {
                    kind = k;
                    break;
                }
            var (l, d, ht) = Size(kind);
            int variant = rng.RangeInclusive(0, 2);
            if (kind == YardKind.Woodpile)
                l = rng.Range(2.2, 3.4);
            if (kind == YardKind.Traps)
                (l, ht) = (variant % 2 + 1.0, 0.5 * (variant + 1));
            picked.Add((kind, l, d, ht, variant));
        }
        // Along the back line from one end, a few steps between them.
        double uLo = -half + 0.35, uHi = half - 0.35, deepest = 0;
        bool fromLeft = rng.Chance(0.5);
        double cursor = fromLeft ? uLo + rng.Range(0, 0.8) : uHi - rng.Range(0, 0.8);
        foreach (var (kind, l, d, ht, variant) in picked.Where(p => p.Kind != YardKind.Clothesline))
        {
            double a = fromLeft ? cursor : cursor - l, b = a + l;
            if (a < uLo || b > uHi)
                continue;
            things.Add(new(kind, Math.Round(a, 3), Math.Round(b, 3), Math.Round(v1 - d, 3), Math.Round(v1, 3), ht, variant));
            deepest = Math.Max(deepest, d);
            cursor = fromLeft ? b + rng.Range(0.6, 1.4) : a - rng.Range(0.6, 1.4);
        }
        // The washing across the yard between the house and what's at the back, where there's room to pass under it.
        if (picked.Any(p => p.Kind == YardKind.Clothesline) && room - deepest >= 1.2 && uHi - uLo > 4)
        {
            double v = Math.Round(v0 + (room - deepest) / 2, 3);
            things.Add(new(YardKind.Clothesline, uLo + 0.4, uHi - 0.4, v - 0.04, v + 0.04, 2.1, rng.RangeInclusive(0, 2)));
        }
        return things;
    }
}
