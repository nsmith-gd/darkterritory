using Ballast;
using DarkTerritory.Sim.LineGen;

namespace DarkTerritory.Sim.Towns;

/// <summary>
/// Where a town is to be made: the fortress's name and what it makes (linegen §10's per-town identity), its outer gate
/// along the main line, the night's seed, the creatures this edition fields (enemies.json director.roster; empty, all of
/// them), and the custom of the last town, which this one won't share (App. F.1: "different from the last").
/// </summary>
public sealed record TownSite(string Name, string Industry, double Gate, ulong Seed, IReadOnlyList<string> Roster, string? Last = null)
{
    /// <summary>The departure fortress of a route: the plan's, or for a hand-laid line one named from the seed.</summary>
    public static TownSite Of(Route.Route route, double gate, IReadOnlyList<string> roster, TownContent content, string? last = null)
    {
        if (route.Plan?.Fortress is { } f)
            return new TownSite(f.Name, f.Identity, gate, route.Seed, roster, last);
        var rng = Streams.Rng(route.Seed, "town", "name");
        string[] industries = [.. content.Writing.Industries.Keys.Order(StringComparer.Ordinal)];
        return new TownSite("Fort " + rng.Pick(content.Surnames), industries.Length > 0 ? rng.Pick(industries) : "", gate, route.Seed, roster, last);
    }
}

/// <summary>
/// Makes a fortress town (GDD §3.1, App. F.1 T133 and the director's notes of 7 Oct 2026; ARCHITECTURE §8 note 281). Each
/// part draws from its own seeded stream (linegen's <see cref="Streams"/>), so changing how people are placed never
/// changes what the town's custom is.
/// </summary>
public static partial class TownGenerator
{
    // Where the people with places stand, in metres. Not design numbers: where a body stands to be in front of the thing
    // it tends (a door, the board, the engine), clear of the thing itself.
    const double DoorStep = 1.4, BoardOut = 6.5, BoardReader = 1.3, BesideTrack = 3.4, Ring = 1.8, GateIn = 9, GateOut = 4.5;
    const double PlatformD = 7.0, PlatformTop = 0.25;

    /// <summary>The roles any townsperson without a place may have ("hand": works at what the town makes).</summary>
    static readonly string[] Folk = ["widow", "driver", "cook", "guard", "hand"];

    /// <summary>
    /// Who hears the custom's lines first: they run out (a culture has eight), so the keeper of its hall and the folk round
    /// its centrepiece have them, then the households (after their own story), the board's reader, the gate, the rest at
    /// work; the street gets the town's other habits and its trade.
    /// </summary>
    static int Precedence(Spot spot) => spot.Street ? 6 : spot.House >= 0 ? 2 : spot.Role switch
    {
        "keeper" => 0,
        "" => 1,
        "gatekeeper" or "guard" => 3,
        _ => 4,
    };

    /// <summary>Somebody's place before they're anybody: their job, where, which way, how, and whose house.</summary>
    record struct Spot(string Role, double S, double D, double FaceS, double FaceD, double Up, bool Street = false, int House = -1,
        string Pose = "idle", string Part = "");

    public static TownPlan Generate(TownContent content, TownSite site)
    {
        var t = content.Tuning;
        var w = content.Writing;
        ulong seed = Streams.Mix(site.Seed, "town", site.Name);
        Pcg32 Rng(string stage) => Streams.Rng(seed, stage);

        // The custom: one of those whose creature this edition has (the Choir is always about).
        var cultures = w.Cultures.Where(c => c.Creature == "choir" || site.Roster.Count == 0 || site.Roster.Contains(c.Creature)).ToList();
        if (cultures.Count > 1 && site.Last is { } last)
            cultures.RemoveAll(c => c.Id == last);
        if (cultures.Count == 0)
            cultures = [.. w.Cultures];
        var culture = Rng("culture").Pick(cultures);
        var quirks = Take(w.Quirks, Rng("quirks").RangeInclusive(t.Quirks[0], t.Quirks[1]), Rng("quirks.pick"));
        var industry = w.Industries.TryGetValue(site.Industry, out var ind) ? ind : null;

        // How many live here now, and how many did (the director, 7 Oct: 20 to 350, then up to 3000; every town has lost people).
        var prng0 = Rng("population");
        // Most towns small, a few big (towns.json populationPower): u to a power, by multiplying (no Math.Pow in the Sim).
        double draw = prng0.NextDouble(), skew = draw;
        for (int i = 1; i < t.PopulationPower; i++)
            skew *= draw;
        int population = t.Population[0] + (int)Math.Round((t.Population[1] - t.Population[0]) * skew);
        int former = (int)Math.Round(population * prng0.Range(t.Former[0], t.Former[1]));

        // The square, beside the engine as the night starts (towns.json "square").
        var sq = t.Square;
        int side = sq.Side;
        double s0 = site.Gate + sq.FromGate[0], s1 = site.Gate + sq.FromGate[1], mid = (s0 + s1) / 2;
        var square = new TownSquare(s0, s1, side, side * sq.WallOut);
        // Back of the buildings a metre in from the far wall; the centrepiece halfway between their fronts and the track.
        double back = sq.WallOut - 1, front = back - sq.BuildingDepth, centreD = (front + BesideTrack + 2) / 2;

        var doors = Rng("doors");
        string Knock(string kind) => w.Doors.TryGetValue(kind, out var lines) && lines.Length > 0 ? doors.Pick(lines) : "";
        var buildings = new List<TownBuilding>
        {
            new("office", "the clerk's office", s0 + 4 + sq.OfficeWidth / 2, side * (back - sq.BuildingDepth / 2), sq.OfficeWidth, sq.BuildingDepth, Knock("office")),
            new("hall", culture.Hall, mid, side * (back - sq.BuildingDepth / 2), sq.HallWidth, sq.BuildingDepth, Knock("hall"), culture.HallStyle),
            new("store", "the stores", s1 - 4 - sq.OfficeWidth / 2, side * (back - sq.BuildingDepth / 2), sq.OfficeWidth, sq.BuildingDepth, Knock("store")),
        };

        // The things in the square to look at: the custom's centrepiece in the middle, the board by the line, the plaque at
        // the way in from the engine, benches, a crate, fire barrels, the market's two stalls shut for the night.
        var fixtures = new List<TownFixture>();
        void Fix(string kind, string name, string text, double s, double d, double faceS, double faceD, int house = -1, double? height = null)
        {
            var (hs, hd, h) = TownFixtures.Size(kind);
            fixtures.Add(new TownFixture(fixtures.Count, kind, name, text, s, d, faceS, faceD, hs, hd, height ?? h, house));
        }
        var cp = culture.Centrepiece;
        Fix(cp.Kind, cp.Name, cp.Text, mid, side * centreD, 0, -side);
        Fix("board", "the notice board", "", mid + 14, side * BoardOut, 0, -side);
        Fix("plaque", site.Name, "", s1 - 3, side * (BesideTrack + 1.2), 0, -side);
        Fix("bench", "a bench", "", mid - 15, side * (centreD + 2), 0, -side);
        Fix("bench", "a bench", "", mid + 7, side * (centreD + 4), 0, -side);
        Fix("crate", "a crate", "", site.Gate - 30, side * (BesideTrack + 0.6), 0, -side);
        Fix("barrel", "a fire barrel", "", mid - 8, side * (centreD - 5), 0, -side);
        Fix("barrel", "a fire barrel", "", mid + 22, side * (centreD + 3), 0, -side);
        var stalls = Rng("stalls");
        string[] wares = industry?.Stalls ?? [];
        Fix("stall", "a market stall", wares.Length > 0 ? stalls.Pick(wares) : "", s0 + 2.2, side * (front - 4), 1, 0);
        Fix("stall", "a market stall", wares.Length > 1 ? wares.First(x => x != fixtures[^1].Text) : "", s1 - 2.2, side * (front - 4), -1, 0);

        // The houses down the yard's street, and the households in the open ones (TownGenerator.Houses).
        var homes = Houses(content, site, square, population, former, Rng);

        // The council's laws posted by the clerk's door, and the town's flag in the square; a walled town's green across the
        // street with its statue, its wall of names, its bandstand, its garden under lamps and its trees; and the day painted
        // on its walls (the director, 8 Oct 2026: "parks, signs of governance, signs of culture, statues, things that tell the
        // story of a people walled in for fear of the outside world"; note 353).
        var civic = Rng("civic");
        TownText? Civic(string kind) => w.Civic.TryGetValue(kind, out var texts) && texts.Length > 0 ? civic.Pick(texts) : null;
        if (w.Laws.Length > 0)
        {
            var laws = Take(w.Laws, Math.Min(4, w.Laws.Length), Rng("laws"));
            string[] numerals = ["I", "II", "III", "IV"];
            string ordained = $"BY ORDER OF THE COUNCIL OF {site.Name.ToUpperInvariant()}. "
                + string.Join(" ", laws.Select((l, i) => $"{numerals[i]}. {l}"));
            Fix("laws", "the ordinances", ordained, buildings[0].S + sq.OfficeWidth / 2 - 1.2, side * (front - 0.3), 0, -side);
        }
        if (Civic("flag") is { } flag)
            Fix("flag", flag.Title, flag.Text, s1 - 8, side * (BesideTrack + 5), 0, -side);
        if (homes.Green is { } green)
        {
            double gs = (green.S0 + green.S1) / 2, gd = side * (green.Near + green.Far) / 2, glen = green.S1 - green.S0;
            if (Civic("statue") is { } statue)
                Fix("statue", statue.Title, statue.Text, gs, gd, 0, -side);
            if (Civic("memorial") is { } wallOfNames)
                Fix("memorial", wallOfNames.Title, wallOfNames.Text, gs, side * (green.Far - 1.6), 0, -side);
            if (Civic("bandstand") is { } band)
                Fix("bandstand", band.Title, band.Text, green.S0 + glen * 0.2, gd, 0, -side);
            var garden = Take(w.Civic.GetValueOrDefault("garden") ?? [], 2, Rng("civic.garden"));
            for (int i = 0; i < garden.Count; i++)
                Fix("garden", garden[i].Title, garden[i].Text, green.S1 - glen * 0.2 + (i - 0.5) * 4.4, gd + side * (i == 0 ? -2.5 : 2.5), 0, -side);
            var trees = Take(w.Civic.GetValueOrDefault("tree") ?? [], 2, Rng("civic.trees"));
            (double S, double D)[] corners = [(green.S0 + 3, green.Near + 3), (green.S1 - 3, green.Far - 3), (green.S1 - 3, green.Near + 3), (green.S0 + 3, green.Far - 3)];
            for (int i = 0; i < corners.Length && trees.Count > 0; i++)
                Fix("tree", trees[i % trees.Count].Title, trees[i % trees.Count].Text, corners[i].S, side * corners[i].D, 0, -side);
            foreach (double along in (double[])[-6, 6])
                Fix("bench", "a bench", "", gs + along, gd - side * 3.5, 0, -side);
        }
        if (homes.Bounds is { } walls)
        {
            // The day, painted on the inside of the back wall at the ends of the first streets, where you see it down them.
            var murals = Take(w.Civic.GetValueOrDefault("mural") ?? [], 2, Rng("civic.murals"));
            for (int i = 0; i < murals.Count && i < walls.Streets.Count; i++)
                Fix("mural", murals[i].Title, murals[i].Text, walls.Rear + Run.Fortresses.WallHalf + 0.05, walls.Streets[i].D, 1, 0);
        }

        // The people. Those with a place: the jobs a town this size has, round the centrepiece, the households at home, out
        // with a lantern in front of the lived-in houses.
        var jobs = new List<Spot>
        {
            new("gatekeeper", site.Gate - GateIn, -side * GateOut, 0, side, 0),
            new("clerk", buildings[0].S, side * (front - DoorStep), 0, -side, 0),
            new("keeper", buildings[1].S + 2.5, side * (front - DoorStep), 0, -side, 0),
            new("fitter", site.Gate - 26, side * BesideTrack, 0, -side, 0),
            new("storekeeper", buildings[2].S, side * (front - DoorStep), 0, -side, 0),
            new("", mid + 14 + 1.4, side * (BoardOut - BoardReader), -0.5, side * 0.85, 0),
            new("lampman", s0 + 6, side * BesideTrack, 0, -side, 0),
            new("guard", site.Gate - GateIn, side * GateOut, 0, -side, 0),
            new("porter", mid + 4, -side * PlatformD, 0, side, PlatformTop),
        };
        // A hamlet of twenty has its gatekeeper, its clerk and its keeper; a town of 300 has everyone at work.
        var spots = jobs.Take(Math.Clamp(3 + population / 45, 3, jobs.Count)).ToList();
        var ring = Rng("ring");
        int around = Math.Clamp(population / 30, t.Ring[0], t.Ring[1]);
        var (cs, cd, _) = TownFixtures.Size(cp.Kind);
        double radius = Math.Max(cs, cd) + Ring;
        double turn = ring.Range(0, Math.PI * 2);
        for (int i = 0; i < around; i++)
        {
            double a = turn + i * Math.PI * 2 / around + ring.Range(-0.3, 0.3);
            double ds = DMath.Cos(a), dd = DMath.Sin(a);
            spots.Add(new("", mid + ds * radius, side * centreD + dd * radius, -ds, -dd, 0));
        }
        spots.AddRange(homes.Residents);
        // Out of doors at night, a lantern in hand, in front of their own houses nearest the square: one in t.Outdoors.
        var lived = homes.Houses.Where(h => h.Kind == HouseKind.Lived).ToList();
        int outdoors = Math.Min(Math.Min(lived.Count, t.OutdoorsMax), (int)Math.Round(population / Math.Max(1, t.Outdoors)));
        for (int i = 0; i < outdoors; i++)
        {
            var h = lived[i];
            // Out on the street in front of it, clear of an enclosed porch (HouseDesign.VestibuleDepth).
            spots.Add(new("", h.S + (i % 3 - 1) * 1.2, h.FrontD - h.Side * 2.6, 0, -h.Side, 0, Street: true, Pose: "lantern"));
        }

        // Who they are: a name each, a household's sharing its surname.
        var names = new HashSet<string>();
        var nrng = Rng("names");
        string NewName(string? family)
        {
            for (int tries = 0; ; tries++)
            {
                string name = $"{nrng.Pick(w.FirstNames)} {family ?? nrng.Pick(content.Surnames)}";
                if (names.Add(name) || tries > 40)
                    return name;
            }
        }
        var rrng = Rng("roles");
        var people = new List<(string Role, string Name, Spot Spot)>();
        foreach (var spot in spots)
        {
            string role = spot.Role.Length > 0 ? spot.Role : spot.House >= 0 ? "home" : rrng.Pick(Folk);
            people.Add((role, NewName(spot.House >= 0 ? homes.Houses[spot.House].Family : null), spot));
        }
        string[] everyone = [.. people.Select(p => p.Name)];
        var places = w.Places.Length > 0 ? w.Places : [.. content.Surnames];
        string founded = w.Founded.Length > 0 ? Rng("founded").Pick(w.Founded) : "";
        var fill = new Fill(new Dictionary<string, string>
        {
            ["{town}"] = site.Name,
            ["{TOWN}"] = site.Name.ToUpperInvariant(),
            ["{hall}"] = culture.Hall,
            ["{law}"] = culture.Law.Replace("{town}", site.Name),
            ["{founded}"] = founded,
            ["{population}"] = population.ToString(),
            ["{former}"] = former.ToString(),
        }, everyone, places, site.Name, Rng("fill"));

        // Each person's lines. A household's say their own story first; the folk round the centrepiece and the keeper talk
        // about the custom; the people at work about the work; then perhaps a scrap of a thread, and what anybody says.
        var cultureLines = new Deck<string>(culture.Lines, Rng("lines.culture"));
        var spare = new Deck<string>([.. quirks.SelectMany(q => q.Lines)], Rng("lines.spare"));
        var trade = new Deck<string>(industry?.Lines ?? [], Rng("lines.trade"));
        var scraps = new Deck<string>([.. w.Threads.SelectMany(th => th.Lines)], Rng("lines.scraps"));
        var anyone = new Deck<string>(w.Roles.TryGetValue("anyone", out var any) ? any.Lines : [], Rng("lines.anyone"));
        // What a walled town's people say of living inside (note 353).
        var inside = new Deck<string>(homes.Bounds is not null ? w.Walled : [], Rng("lines.walled"));
        var roleDecks = new Dictionary<string, Deck<string>>();
        Deck<string> JobDeck(string role)
        {
            if (!roleDecks.TryGetValue(role, out var deck))
                // A hand's job is the town's trade (one deck, so nobody else says the same).
                roleDecks[role] = deck = role == "hand" ? trade : new Deck<string>(w.Roles.TryGetValue(role, out var r) ? r.Lines : [], Rng("lines." + role));
            return deck;
        }
        var lrng = Rng("lines");
        var said = new List<string>[people.Count];
        foreach (int i in Enumerable.Range(0, people.Count).OrderBy(i => Precedence(people[i].Spot)).ThenBy(i => i))
        {
            var (role, name, spot, street) = (people[i].Role, people[i].Name, people[i].Spot, people[i].Spot.Street);
            var lines = new List<string>();
            int want = lrng.RangeInclusive(t.LinesPerPerson[0], t.LinesPerPerson[1]);
            if (spot.House >= 0)
            {
                // Their household's story, in their own part, then the custom.
                foreach (var line in homes.Story(spot.House, spot.Part))
                    lines.Add(line);
                if (cultureLines.Next() is { } town)
                    lines.Add(town);
            }
            else
            {
                string? job = JobDeck(role).Next(), town = (street ? inside.Next() : cultureLines.Next()) ?? spare.Next() ?? trade.Next();
                // The gatekeeper says the town's law first, as you come in (the first thing anyone in a town tells you).
                if (role == "gatekeeper" && w.Welcome.Length > 0)
                    lines.Add(Rng("welcome").Pick(w.Welcome));
                bool talksTown = spot.Role.Length == 0 || role == "keeper";
                foreach (var line in talksTown ? new[] { town, job } : [job, town])
                    if (line is not null)
                        lines.Add(line);
            }
            if (lines.Count < want && lrng.Chance(t.ScrapShare) && scraps.Next() is { } scrap)
                lines.Add(scrap);
            // Short of lines: the town's habits and trade, another of the job's, a scrap of a thread after all, and last what
            // anybody says.
            while (lines.Count < want && (spare.Next() ?? inside.Next() ?? trade.Next() ?? JobDeck(role).Next() ?? scraps.Next() ?? anyone.Next()) is { } extra)
                lines.Add(extra);
            var vars = spot.House >= 0 ? homes.Vars(spot.House) : null;
            said[i] = [.. lines.Take(Math.Max(Math.Max(1, want), spot.House >= 0 ? 2 : 1)).Select(l => fill.In(l, name, vars))];
        }
        var townsfolk = new List<Townsperson>();
        for (int i = 0; i < people.Count; i++)
        {
            var (role, name, spot) = people[i];
            string title = spot.House >= 0 ? homes.Title(spot.House, spot.Part)
                : role == "hand" ? industry?.Hand ?? "townsman" : w.Roles.TryGetValue(role, out var rt) ? rt.Title : role;
            townsfolk.Add(new Townsperson(i, name, fill.In(title, name, spot.House >= 0 ? homes.Vars(spot.House) : null), role, spot.S, spot.D, spot.Up, spot.FaceS, spot.FaceD,
                (int)(Streams.Mix(seed, "look", name) % 8), said[i], spot.House, spot.Pose, TownGear.Pick(homes.Gear, Streams.Mix(seed, "gear", name))));
        }

        // The plaque: the town, when it was walled, how many live here and how many did.
        string plaque = w.Plaque.Length > 0 ? fill.In(Rng("plaque").Pick(w.Plaque), null) : $"{site.Name.ToUpperInvariant()}. {founded}";
        fixtures[2] = fixtures[2] with { Text = plaque };
        // Inside the open houses: the household's own thing, the range, the stair door, a photograph.
        var rooms = Rng("rooms");
        string Room(string kind) => w.Rooms.TryGetValue(kind, out var texts) && texts.Length > 0 ? rooms.Pick(texts) : "";
        foreach (var h in homes.Houses)
        {
            if (h.Layout is not { } layout)
                continue;
            var household = homes.HouseholdOf(h.Id)!;
            var vars = homes.Vars(h.Id);
            var (ou, ov, oh) = layout.Place(household.Object.Kind, h.Width, h.Depth);
            var at = h.Rail(ou, ov);
            Fix(household.Object.Kind, household.Object.Name, fill.In(household.Object.Text, null, vars), at.S, at.D, 0, -h.Side, h.Id, oh);
            foreach (var thing in layout.Things.Where(x => x.Kind is "stove" or "stairs" or "photo"))
            {
                // The stair door is on the box's face toward the partition; the photograph on the partition.
                double u = thing.Kind == "stairs" ? thing.U + layout.Kitchen * (thing.HalfU + 0.05) : thing.U;
                var p = h.Rail(u, thing.V);
                Fix(thing.Kind == "stairs" ? "stairdoor" : thing.Kind, thing.Kind switch { "stairs" => "the stair door", "stove" => "the range", _ => "a photograph" },
                    fill.In(Room(thing.Kind), null, vars), p.S, p.D, 0, -h.Side, h.Id, thing.Kind switch { "stove" => 0.9, "stairs" => 1.2, _ => 1.6 });
            }
        }

        // The board: the custom's order first, then what the town makes, then whatever else is posted; and a note or two
        // left about the square (a letter on a bench, a page by the hall door, a scrap by the engine).
        var papers = new List<TownPaper>();
        var prng = Rng("papers");
        var board = fixtures.First(f => f.Kind == "board");
        var posted = new List<TownText> { culture.Notes[0] };
        var more = new Deck<TownText>([.. industry?.Notes ?? [], .. quirks.SelectMany(q => q.Notes), .. w.Notices, .. w.Threads.SelectMany(th => th.Notes)], prng);
        int notices = prng.RangeInclusive(t.Notices[0], t.Notices[1]);
        while (posted.Count < notices && more.Next() is { } p)
            posted.Add(p);
        foreach (var p in posted)
            papers.Add(new TownPaper(papers.Count, fill.In(p.Title, null), fill.In(p.Text, null), true, board.S, board.D, 1.5));
        var loose = new Deck<TownText>([.. culture.Notes.Skip(1), .. w.Threads.SelectMany(th => th.Notes)], Rng("papers.loose"));
        var benches = fixtures.Where(f => f.Kind == "bench").ToList();
        (double S, double D, double H)[] lying =
        [
            (benches[0].S, benches[0].D, 0.5),
            (buildings[1].S - 2.5, side * (front - 0.15), 1.5),
            (site.Gate - 30, side * (BesideTrack + 0.6), TownFixtures.Size("crate").Height + 0.02),
        ];
        int looseCount = Math.Min(lying.Length, prng.RangeInclusive(t.LooseNotes[0], t.LooseNotes[1]));
        for (int i = 0; i < looseCount && loose.Next() is { } n; i++)
            papers.Add(new TownPaper(papers.Count, fill.In(n.Title, null), fill.In(n.Text, null), false, lying[i].S, lying[i].D, lying[i].H));

        return new TownPlan(site.Name, population, former, culture.Id, culture.Creature, culture.Law.Replace("{town}", site.Name), culture.Hall,
            industry?.Name ?? site.Industry, [.. quirks.Select(q => q.Id)], square, buildings, homes.Houses, townsfolk, papers, fixtures, homes.Character, homes.Bounds,
            homes.Green);
    }

    static List<T> Take<T>(IReadOnlyList<T> from, int count, Pcg32 rng) where T : class
    {
        var deck = new Deck<T>(from, rng);
        var taken = new List<T>();
        while (taken.Count < count && deck.Next() is { } next)
            taken.Add(next);
        return taken;
    }

    /// <summary>A shuffled deck dealt without repeats, so no two people in a town say the same line; null when it's out.</summary>
    sealed class Deck<T> where T : class
    {
        readonly List<T> _cards;
        int _next;

        public Deck(IReadOnlyList<T> cards, Pcg32 rng)
        {
            _cards = [.. cards];
            for (int i = _cards.Count - 1; i > 0; i--)
            {
                int j = (int)(rng.NextDouble() * (i + 1));
                (_cards[i], _cards[j]) = (_cards[j], _cards[i]);
            }
        }

        public T? Next() => _next < _cards.Count ? _cards[_next++] : null;

        /// <summary>Everything left in the deck, in its order.</summary>
        public IEnumerable<T> All()
        {
            while (Next() is { } card)
                yield return card;
        }
    }

    /// <summary>
    /// The placeholders: the town's own ({town}, {TOWN}, {hall}, {law}, {founded}, {population}, {former}), a household's
    /// ({family}, {absent}), and those drawn afresh each time: {other} (another town), {n}, and {name}/{name2} (somebody
    /// else in the town).
    /// </summary>
    sealed class Fill(IReadOnlyDictionary<string, string> fixedVars, string[] everyone, IReadOnlyList<string> places, string town, Pcg32 rng)
    {
        Pcg32 _rng = rng;

        public string In(string text, string? speaker, IReadOnlyDictionary<string, string>? vars = null)
        {
            if (!text.Contains('{'))
                return text;
            foreach (var (key, value) in vars ?? new Dictionary<string, string>())
                text = text.Replace(key, value);
            foreach (var (key, value) in fixedVars)
                text = text.Replace(key, value);
            string Someone(string? not)
            {
                for (int tries = 0; tries < 20 && everyone.Length > 0; tries++)
                {
                    string n = everyone[(int)(_rng.NextDouble() * everyone.Length)];
                    if (n != speaker && n != not)
                        return n;
                }
                return "the Gillis boy";
            }
            string Other()
            {
                for (int tries = 0; tries < 20 && places.Count > 0; tries++)
                {
                    string p = _rng.Pick(places);
                    if (!town.Contains(p, StringComparison.OrdinalIgnoreCase))
                        return p;
                }
                return "the next town";
            }
            string a = Someone(null);
            return text.Replace("{other}", Other()).Replace("{n}", _rng.RangeInclusive(2, 9).ToString())
                .Replace("{name2}", Someone(a)).Replace("{name}", a);
        }
    }
}

/// <summary>
/// The size of each thing in a town: half its footprint along and across the line, and its height (m). The art builds
/// each to these, and the walls (<see cref="Town"/>) stand where they say. A thing painted on the ground, hung on a wall
/// or lying on a table has no footprint.
/// </summary>
public static class TownFixtures
{
    public static (double HalfS, double HalfD, double Height) Size(string kind) => kind switch
    {
        "bell" => (1.3, 0.9, 3.6),
        "post" => (0.35, 0.35, 2.4),
        "tally" => (1.2, 0.25, 2.4),
        "lamps" => (1.6, 0.45, 2.2),
        "horn" => (0.3, 0.3, 5.5),
        "mirror" => (0.8, 0.3, 2.6),
        "shelf" => (2.0, 0.3, 1.6),
        "lectern" => (0.45, 0.4, 1.3),
        "brazier" => (0.8, 0.8, 1.1),
        "line" => (0, 0, 0),
        "pegs" => (2.4, 0.3, 2.0),
        "bench" => (1.0, 0.3, 0.9),
        "carriage" => (3.2, 1.4, 3.6),
        "cannon" => (1.4, 0.8, 1.3),
        "board" => (1.1, 0.15, 2.2),
        "plaque" => (0.2, 0.2, 2.2),
        "crate" => (0.5, 0.4, 0.8),
        "barrel" => (0.35, 0.35, 1.0),
        "stall" => (0.9, 1.5, 2.6),
        // The civic pieces (note 353): a statue on its plinth, the wall of names, the bandstand's raised floor, a bed under
        // its lamps, a tree's trunk, the flagpole, the laws' board; a mural is paint on a wall (its half-width to look from).
        "statue" => (0.8, 0.8, 3.4),
        "memorial" => (3.6, 0.35, 2.2),
        "bandstand" => (2.8, 2.8, 0.8),
        "garden" => (1.6, 0.7, 0.45),
        "tree" => (0.35, 0.35, 4.5),
        "flag" => (0.15, 0.15, 8.0),
        "laws" => (0.9, 0.12, 2.0),
        "mural" => (5.0, 0, 4.0),
        // In the houses (their furniture is the layout's): only the cradle stands on its own feet.
        "cradle" => (0.45, 0.3, 0.7),
        "table" or "letters" or "anklebell" or "timetable" or "boots" or "boards" or "suitcase" or "radio" or "clock"
            or "stove" or "stairdoor" or "photo" => (0, 0, 0.5),
        _ => (0.5, 0.5, 1.5),
    };
}
