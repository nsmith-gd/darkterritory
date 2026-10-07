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
/// Makes a fortress town (GDD §3.1, App. F.1 T133; ARCHITECTURE §8 note 281). Each part draws from its own seeded stream
/// (linegen's <see cref="Streams"/>), so changing how people are placed never changes what the town's custom is.
/// </summary>
public static class TownGenerator
{
    // Where the people with places stand, in metres. Not design numbers: where a body stands to be in front of the thing
    // it tends (a door, the board, the engine), clear of the thing itself.
    const double DoorStep = 1.4, BoardOut = 6.5, BoardReader = 1.3, BesideTrack = 3.4, Ring = 1.8, GateIn = 9, GateOut = 4.5;
    const double PlatformD = 7.0, PlatformTop = 0.25, HouseOut = 10.9, HouseDepth = 6, HouseEvery = 15;

    /// <summary>The roles any townsperson without a place may have ("hand": works at what the town makes).</summary>
    static readonly string[] Folk = ["widow", "driver", "cook", "guard", "hand"];

    /// <summary>
    /// Who hears the custom's lines first: they run out (a culture has six), so the keeper of its hall and the folk round
    /// its centrepiece have them, then the board's reader, then the gate, then the rest at work; the street gets the
    /// town's other habits and its trade.
    /// </summary>
    static int Precedence(string role, bool placed) => !placed ? 5 : role switch
    {
        "keeper" => 0,
        "" => 2,
        "gatekeeper" or "guard" => 3,
        _ => 4,
    };

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
        var crng = Rng("culture");
        var culture = crng.Pick(cultures);
        var quirks = Take(w.Quirks, Rng("quirks").RangeInclusive(t.Quirks[0], t.Quirks[1]), Rng("quirks.pick"));
        var industry = w.Industries.TryGetValue(site.Industry, out var ind) ? ind : null;

        // The square, beside the engine as the night starts (towns.json "square").
        var sq = t.Square;
        int side = sq.Side;
        double s0 = site.Gate + sq.FromGate[0], s1 = site.Gate + sq.FromGate[1], mid = (s0 + s1) / 2;
        var square = new TownSquare(s0, s1, side, side * sq.WallOut);
        // Back of the buildings a metre in from the far wall; the centrepiece halfway between their fronts and the track.
        double back = sq.WallOut - 1, front = back - sq.BuildingDepth, centreD = (front + BesideTrack + 2) / 2;

        var others = new OtherTowns(content.Surnames, site.Name, Rng("others"));
        var doors = Rng("doors");
        string Knock(string kind) => w.Doors.TryGetValue(kind, out var lines) && lines.Length > 0 ? doors.Pick(lines) : "";
        var buildings = new List<TownBuilding>
        {
            new("office", "the clerk's office", s0 + 4 + sq.OfficeWidth / 2, side * (back - sq.BuildingDepth / 2), sq.OfficeWidth, sq.BuildingDepth, Knock("office")),
            new("hall", culture.Hall, mid, side * (back - sq.BuildingDepth / 2), sq.HallWidth, sq.BuildingDepth, Knock("hall")),
            new("store", "the stores", s1 - 4 - sq.OfficeWidth / 2, side * (back - sq.BuildingDepth / 2), sq.OfficeWidth, sq.BuildingDepth, Knock("store")),
        };

        // The things in the square to look at: the custom's centrepiece in the middle, the board by the line, the plaque at
        // the way in from the engine, two benches.
        var fixtures = new List<TownFixture>();
        void Fix(string kind, string name, string text, double s, double d, double faceS, double faceD)
        {
            var (hs, hd, h) = TownFixtures.Size(kind);
            fixtures.Add(new TownFixture(fixtures.Count, kind, name, text, s, d, faceS, faceD, hs, hd, h));
        }
        var cp = culture.Centrepiece;
        Fix(cp.Kind, cp.Name, cp.Text, mid, side * centreD, 0, -side);
        Fix("board", "the notice board", "", mid + 14, side * BoardOut, 0, -side);
        string founded = w.Founded.Length > 0 ? Rng("founded").Pick(w.Founded) : "";
        Fix("plaque", site.Name, $"{site.Name.ToUpperInvariant()}. {founded} {culture.Law}".Trim(), s1 - 3, side * (BesideTrack + 1.2), 0, -side);
        Fix("bench", "a bench", "", mid - 15, side * (centreD + 2), 0, -side);
        Fix("bench", "a bench", "", mid + 7, side * (centreD + 4), 0, -side);
        Fix("crate", "a crate", "", site.Gate - 30, side * (BesideTrack + 0.6), 0, -side);
        // Fire barrels for the folk to stand at, and the market's two stalls against the square's ends, shut for the night.
        Fix("barrel", "a fire barrel", "", mid - 8, side * (centreD - 5), 0, -side);
        Fix("barrel", "a fire barrel", "", mid + 22, side * (centreD + 3), 0, -side);
        var stalls = Rng("stalls");
        string[] wares = industry?.Stalls ?? [];
        Fix("stall", "a market stall", wares.Length > 0 ? stalls.Pick(wares) : "", s0 + 2.2, side * (front - 4), 1, 0);
        Fix("stall", "a market stall", wares.Length > 1 ? wares.First(x => x != fixtures[^1].Text) : "", s1 - 2.2, side * (front - 4), -1, 0);

        // The people. Those with a place first: the gate, the three doors, the board, the engine, the lamps, the platform.
        var spots = new List<(string Role, double S, double D, double FaceS, double FaceD, double Up)>
        {
            ("gatekeeper", site.Gate - GateIn, -side * GateOut, 0, side, 0),
            ("guard", site.Gate - GateIn, side * GateOut, 0, -side, 0),
            ("clerk", buildings[0].S, side * (front - DoorStep), 0, -side, 0),
            ("keeper", buildings[1].S + 2.5, side * (front - DoorStep), 0, -side, 0),
            ("storekeeper", buildings[2].S, side * (front - DoorStep), 0, -side, 0),
            ("", mid + 14 + 1.4, side * (BoardOut - BoardReader), -0.5, side * 0.85, 0),
            ("fitter", site.Gate - 26, side * BesideTrack, 0, -side, 0),
            ("lampman", s0 + 6, side * BesideTrack, 0, -side, 0),
            ("porter", mid + 4, -side * PlatformD, 0, side, PlatformTop),
        };
        // Round the centrepiece, facing it.
        var ring = Rng("ring");
        int around = ring.RangeInclusive(t.Ring[0], t.Ring[1]);
        var (cs, cd, _) = TownFixtures.Size(cp.Kind);
        double radius = Math.Max(cs, cd) + Ring;
        double turn = ring.Range(0, Math.PI * 2);
        for (int i = 0; i < around; i++)
        {
            double a = turn + i * Math.PI * 2 / around + ring.Range(-0.3, 0.3);
            double ds = DMath.Cos(a), dd = DMath.Sin(a);
            spots.Add(("", mid + ds * radius, side * centreD + dd * radius, -ds, -dd, 0));
        }
        int placedAndRing = spots.Count;
        // Out in front of the houses down the yard's street (note 107's folk), one house in t.Street, from the yard's far end
        // to the square, on the side with no platform (the right is platform from the gate most of the yard's length).
        for (double s = Math.Ceiling((site.Gate - 400) / HouseEvery) * HouseEvery; s < site.Gate - 30; s += HouseEvery)
        {
            int n = (int)Math.Round(s / HouseEvery);
            if (s > square.S0 - 6 && s < square.S1 + 6 || Math.Abs(n * 37) % Math.Max(1, t.Street * 3) > 2)
                continue;
            spots.Add(("", s + n % 3 - 1, side * (HouseOut - HouseDepth / 2 - 1.3), 0, -side, 0));
        }

        // Who they are, and what they say.
        var names = new HashSet<string>();
        var nrng = Rng("names");
        string NewName()
        {
            for (int tries = 0; ; tries++)
            {
                string name = $"{nrng.Pick(w.FirstNames)} {nrng.Pick(content.Surnames)}";
                if (names.Add(name) || tries > 40)
                    return name;
            }
        }
        var people = new List<(string Role, string Name, (string Role, double S, double D, double FaceS, double FaceD, double Up) Spot, bool Street)>();
        var rrng = Rng("roles");
        for (int i = 0; i < spots.Count; i++)
        {
            var spot = spots[i];
            string role = spot.Role.Length > 0 ? spot.Role : rrng.Pick(Folk);
            people.Add((role, NewName(), spot, i >= placedAndRing));
        }
        string[] everyone = [.. people.Select(p => p.Name)];
        var fill = new Fill(site.Name, culture.Hall, culture.Law, everyone, others, Rng("fill"));

        // Each person's lines: their job's and their town's, which first by their place (the folk round the centrepiece
        // talk about the town, the people at work about the work), then perhaps a scrap of a thread.
        var cultureLines = new Deck<string>(culture.Lines, Rng("lines.culture"));
        var spare = new Deck<string>([.. quirks.SelectMany(q => q.Lines)], Rng("lines.spare"));
        var trade = new Deck<string>(industry?.Lines ?? [], Rng("lines.trade"));
        var scraps = new Deck<string>([.. w.Threads.SelectMany(th => th.Lines)], Rng("lines.scraps"));
        var roleDecks = new Dictionary<string, Deck<string>>();
        // What anyone might say (towns.json roles "anyone"), when everything else has been said.
        var anyone = new Deck<string>(w.Roles.TryGetValue("anyone", out var any) ? any.Lines : [], Rng("lines.anyone"));
        Deck<string> JobDeck(string role)
        {
            if (!roleDecks.TryGetValue(role, out var deck))
            {
                // A hand's job is the town's trade (one deck, so nobody else says the same).
                roleDecks[role] = deck = role == "hand" ? trade : new Deck<string>(w.Roles.TryGetValue(role, out var r) ? r.Lines : [], Rng("lines." + role));
            }
            return deck;
        }
        var lrng = Rng("lines");
        var said = new List<string>[people.Count];
        foreach (int i in Enumerable.Range(0, people.Count).OrderBy(i => Precedence(people[i].Spot.Role, !people[i].Street)).ThenBy(i => i))
        {
            var (role, name, spot, street) = people[i];
            string? job = JobDeck(role).Next(), town = (street ? null : cultureLines.Next()) ?? spare.Next() ?? trade.Next();
            bool talksTown = spot.Role.Length == 0 || role == "keeper";
            var lines = new List<string>();
            // The gatekeeper says the town's law first, as you come in (the first thing anyone in a town tells you).
            if (role == "gatekeeper" && w.Welcome.Length > 0)
                lines.Add(Rng("welcome").Pick(w.Welcome));
            foreach (var line in talksTown ? new[] { town, job } : [job, town])
                if (line is not null)
                    lines.Add(line);
            int want = lrng.RangeInclusive(t.LinesPerPerson[0], t.LinesPerPerson[1]);
            if (lines.Count < want && lrng.Chance(t.ScrapShare) && scraps.Next() is { } scrap)
                lines.Add(scrap);
            // Short of lines: the town's habits and trade, another of the job's, a scrap of a thread after all, and last what
            // anybody says.
            while (lines.Count < want && (spare.Next() ?? trade.Next() ?? JobDeck(role).Next() ?? scraps.Next() ?? anyone.Next()) is { } extra)
                lines.Add(extra);
            said[i] = [.. lines.Take(Math.Max(1, want)).Select(l => fill.In(l, name))];
        }
        var townsfolk = new List<Townsperson>();
        for (int i = 0; i < people.Count; i++)
        {
            var (role, name, spot, _) = people[i];
            string title = role == "hand" ? industry?.Hand ?? "townsman" : w.Roles.TryGetValue(role, out var rt) ? rt.Title : role;
            townsfolk.Add(new Townsperson(i, name, fill.In(title, name), role, spot.S, spot.D, spot.Up, spot.FaceS, spot.FaceD,
                (int)(Streams.Mix(seed, "look", name) % 8), said[i]));
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

        return new TownPlan(site.Name, culture.Id, culture.Creature, culture.Law, culture.Hall, industry?.Name ?? site.Industry,
            [.. quirks.Select(q => q.Id)], square, buildings, townsfolk, papers, fixtures);
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
    }

    /// <summary>Other towns down the line, for {other}: surnames that aren't this town's.</summary>
    sealed class OtherTowns(IReadOnlyList<string> surnames, string town, Pcg32 rng)
    {
        Pcg32 _rng = rng;

        public string Next()
        {
            for (int tries = 0; tries < 20; tries++)
            {
                string name = _rng.Pick(surnames);
                if (!town.Contains(name, StringComparison.OrdinalIgnoreCase))
                    return name;
            }
            return "the next town";
        }
    }

    /// <summary>The placeholders: {town}, {other}, {hall}, {law}, {n}, and {name}/{name2} for somebody else in the town.</summary>
    sealed class Fill(string town, string hall, string law, string[] everyone, OtherTowns others, Pcg32 rng)
    {
        Pcg32 _rng = rng;

        public string In(string text, string? speaker)
        {
            if (!text.Contains('{'))
                return text;
            string Someone(string? not)
            {
                for (int tries = 0; tries < 20 && everyone.Length > 0; tries++)
                {
                    string n = everyone[(int)(_rng.NextDouble() * everyone.Length)];
                    if (n != speaker && n != not)
                        return n;
                }
                return "the Grieve boy";
            }
            string a = Someone(null);
            return text.Replace("{town}", town).Replace("{hall}", hall).Replace("{law}", law).Replace("{other}", others.Next())
                .Replace("{n}", _rng.RangeInclusive(2, 9).ToString()).Replace("{name2}", Someone(a)).Replace("{name}", a);
        }
    }
}

/// <summary>
/// The size of each thing in a square: half its footprint along and across the line, and its height (m). The art builds
/// each to these, and the walls (<see cref="Town"/>) stand where they say. A thing painted on the ground has no footprint.
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
        _ => (0.5, 0.5, 1.5),
    };
}
