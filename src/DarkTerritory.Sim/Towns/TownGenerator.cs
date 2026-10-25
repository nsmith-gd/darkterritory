using Ballast;
using DarkTerritory.Sim.LineGen;

namespace DarkTerritory.Sim.Towns;

/// <summary>
/// Where a town is to be made: the fortress's name and what it makes (linegen §10's per-town identity), its outer gate
/// along the main line, the night's seed, the creatures this edition fields (enemies.json director.roster; empty, all of
/// them), and the custom of the last town, which this one won't share (App. F.1: "different from the last").
/// </summary>
/// <para><see cref="Burnt"/>: how many of its houses are burnt out (note 608), the nearest where a train come in stands (<see cref="TownGenerator.Harmed"/>, note 966).</para>
public sealed record TownSite(string Name, string Industry, double Gate, ulong Seed, IReadOnlyList<string> Roster, string? Last = null, int Burnt = 0)
{
    /// <summary>How cold the town is to the crew, in percent (note 1024).</summary>
    public int Cold { get; init; }

    /// <summary>The town type to make it (note 692; "" the street town), or null to draw it from its trade and seed.</summary>
    public string? Form { get; init; }

    /// <summary>The departure fortress of a route: the plan's, or for a hand-laid line one named from the seed.</summary>
    public static TownSite Of(Route.Route route, double gate, IReadOnlyList<string> roster, TownContent content, string? last = null)
    {
        // The town arrived at last night (note 591) is its own seed's; any other, the route's.
        if (route.Plan?.Fortress is { } f)
            return new TownSite(f.Name, f.Identity, gate, f.TownSeed ?? route.Seed, roster, last, f.TownBurnt) { Cold = f.TownCold };
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
    /// <summary>
    /// The board of lost crews (note 721): along the square from its middle (the notice board's at +14, the bench at +7), and
    /// what it says to anyone with no crews posted (each machine's own profile fills it).
    /// </summary>
    const double LostCrewsAlong = 10.6;
    public const string LostCrewsText = "The company's board. A card goes up for every crew whose train was taken: the crew's name, how many nights it ran, and how its last one ended. Nothing's posted for any crew you rode with.";
    const double DoorStep = 1.4, BoardOut = 6.5, BoardReader = 1.3, BesideTrack = 3.4, Ring = 1.8, GateIn = 9, GateOut = 4.5;
    const double PlatformD = 7.0, PlatformTop = 0.25;
    /// <summary>The quiet house: how far inside the gate, and how far out from the line on the far side (m).</summary>
    const double QuietIn = 15, QuietOut = 21;
    /// <summary>How far past the way in's gatehouse a town type's murals are painted on its back wall (m; note 692).</summary>
    /// <summary>
    /// Note 971: a household's quarrel, at most one a house, by its two at most odds on a trait <c>quarrels.lines</c> names,
    /// <c>quarrels.gap</c> apart or more; the first line said by the one higher on it. Not at the party.
    /// </summary>
    static List<TownQuarrel> Quarrels(QuarrelTuning t, IReadOnlyList<Townsperson> people, Pcg32 rng)
    {
        var quarrels = new List<TownQuarrel>();
        if (t.Lines.Count == 0)
            return quarrels;
        string[] axes = [.. TownTraits.Axes.Where(t.Lines.ContainsKey)];
        foreach (var house in people.Where(p => p.House >= 0 && p.Personality is not null && !p.Hosting).GroupBy(p => p.House).OrderBy(g => g.Key))
        {
            var members = house.OrderBy(p => p.Id).ToList();
            (Townsperson A, Townsperson B, string Over, double Gap)? worst = null;
            foreach (var axis in axes)
                for (int x = 0; x < members.Count; x++)
                    for (int y = x + 1; y < members.Count; y++)
                    {
                        int at = Array.IndexOf(TownTraits.Axes, axis);
                        double a = members[x].Personality!.Traits[at], b = members[y].Personality!.Traits[at];
                        if (Math.Abs(a - b) >= t.Gap && (worst is null || Math.Abs(a - b) > worst.Value.Gap))
                            worst = a >= b ? (members[x], members[y], axis, Math.Abs(a - b)) : (members[y], members[x], axis, Math.Abs(a - b));
                    }
            if (worst is not { } w || t.Lines[w.Over] is not { Length: > 0 } decks)
                continue;
            var said = decks[rng.RangeInclusive(0, decks.Length - 1)];
            string Given(Townsperson p) => p.Personality?.Given is { Length: > 0 } g ? g : p.Name;
            quarrels.Add(new TownQuarrel(house.Key, w.A.Id, w.B.Id, w.Over,
                [.. said.Select((line, k) => k % 2 == 0 ? (w.A.Id, line.Replace("{other}", Given(w.B))) : (w.B.Id, line.Replace("{other}", Given(w.A))))]));
        }
        return quarrels;
    }

    const double MuralByGate = 6;

    /// <summary>The roles any townsperson without a place may have ("hand": works at what the town makes).</summary>
    static readonly string[] Folk = ["widow", "driver", "cook", "guard", "hand"];

    /// <summary>
    /// Who hears the custom's lines first: they run out (a culture has eight), so the keeper of its hall and the folk round
    /// its centrepiece have them, then the households (after their own story), the board's reader, the gate, the rest at
    /// work; the street gets the town's other habits and its trade.
    /// </summary>
    static int Precedence(Spot spot) => spot.Street ? 6 : spot.House >= 0 ? 2 : spot.Role switch
    {
        // The keeper, and the hands at the works (note 353): the trade's lines are theirs first.
        "keeper" or "hand" => 0,
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
        // Who the people are and what they're called (note 474), when the content has the townsfolk's matrix.
        var folk = content.Folk is { } fc ? new TownFolk(fc, w, site, culture.Id, seed) : null;
        // What kind of town it is (note 692; the director, 13 Oct: "multiple different town types"), from its trade and its
        // own stream, and its ground plan: a harbour, a dykeland, a quarry, a tidewater or a glasshouse town, or the
        // street town of note 335.
        string formId = site.Form ?? TownForms.Pick(t.Forms, site.Industry, Rng("form"));
        var form = TownForms.Make(t, w, formId, site.Gate, square, Rng);
        var homes = Houses(content, site, square, population, former, Rng, folk, form);
        // What its people believe, by its houses' character (note 822, the Flooders'): heard with its custom, posted after it.
        var creed = homes.Creed is { } creedId ? w.Creeds.FirstOrDefault(c => c.Id == creedId) : null;

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
        // A town type's landmarks (note 692), standing where its form put them.
        // (Its own words for one; one of many, its works' words of that kind in turn: a glasshouse town's ranges.)
        var many = new Dictionary<string, int>();
        foreach (var (kind, _, ls, ld, lfs, lfd) in form?.Landmarks ?? [])
        {
            var words = w.Forms.GetValueOrDefault(form!.Form)?.Landmarks.GetValueOrDefault(kind);
            if (words is null && (w.Works.GetValueOrDefault(form.Form) ?? []).Where(x => x.Kind == kind).ToList() is { Count: > 0 } kinds)
            {
                int n = many[kind] = many.GetValueOrDefault(kind) + 1;
                words = new TownText(kinds[(n - 1) % kinds.Count].Title, kinds[(n - 1) % kinds.Count].Text);
            }
            Fix(kind, words?.Title.Replace("{town}", site.Name) ?? kind, words?.Text.Replace("{town}", site.Name) ?? "", ls, ld, lfs, lfd);
        }
        if (homes.Works is { } works)
        {
            // A walled town's works (note 353; GDD §3: "Fortified towns survive behind stone and steel walls ... furnaces, rail
            // yards and warehouses"): its trade's pieces, then what every town's works have, in order along the line, each
            // that fits between the streets; packed (the winding house its ropes' length from its headframe) and centred.
            var wt = t.Walled;
            TownWork[] pieces = [.. w.Works.GetValueOrDefault(works.Trade) ?? [], .. w.Works.GetValueOrDefault("any") ?? []];
            double near = works.Near + wt.WorksMargin, far = works.Far - wt.WorksMargin, room = works.S1 - works.S0 - 2 * wt.WorksMargin;
            // A piece and those that stand by it (After) go together or not at all: a headframe has its winding house.
            var groups = new List<List<TownWork>>();
            foreach (var piece in pieces)
                if (piece.After > 0 && groups.Count > 0)
                    groups[^1].Add(piece);
                else
                    groups.Add([piece]);
            var laid = new List<(TownWork Piece, double At)>();
            double end = 0;
            foreach (var group in groups)
            {
                // Where each stands from the group's first, and how far the group reaches either side of that.
                var offsets = new List<double> { 0 };
                for (int k = 1; k < group.Count; k++)
                    offsets.Add(offsets[^1] + group[k].After);
                double lo = 0, hi = 0;
                bool fits = true;
                for (int k = 0; k < group.Count; k++)
                {
                    var (hs, hd, _) = TownFixtures.Size(group[k].Kind);
                    fits &= Math.Abs(group[k].Across) + hd <= (far - near) / 2;
                    (lo, hi) = (Math.Min(lo, offsets[k] - hs), Math.Max(hi, offsets[k] + hs));
                }
                double first = (laid.Count == 0 ? 0 : end + wt.WorksGap) - lo;
                if (!fits || first + hi > room)
                    continue;
                for (int k = 0; k < group.Count; k++)
                    laid.Add((group[k], first + offsets[k]));
                end = first + hi;
            }
            double from = works.S0 + wt.WorksMargin + (room - end) / 2, middle = (near + far) / 2;
            foreach (var (piece, at) in laid)
                Fix(piece.Kind, piece.Title.Replace("{town}", site.Name), piece.Text.Replace("{town}", site.Name), from + at,
                    works.Side * (middle + piece.Across), 0, -works.Side);
        }
        if (homes.Yard is { } ry)
            RailYard(ry, t.Walled, w, site.Name, Rng("yard"), Fix);
        if (homes.Bounds is not null)
        {
            // A walled town's clerk keeps the council's house (note 353): its cupola and clock over the square.
            buildings[0] = buildings[0] with { Name = "the council house", Style = "council", Knock = Knock("council") is { Length: > 0 } council ? council : buildings[0].Knock };
            // The quiet house by the gate, where whoever comes in from outside sleeps the first night (the laws say so),
            // on the line's other side from the square, short of the first streets' ends.
            if (Knock("quiet") is { Length: > 0 } quiet)
                buildings.Add(new TownBuilding("quiet", "the quiet house", site.Gate - QuietIn, -side * QuietOut, 6, 5, quiet));
        }
        else
        {
            // A small town keeps its dead and its day on the square's far wall, in the gaps between its buildings.
            double far = Math.Abs(square.WallD) - Run.Fortresses.WallHalf;
            double gap0 = buildings[0].S + buildings[0].Length / 2, gap1 = buildings[1].S - buildings[1].Length / 2;
            double gap2 = buildings[1].S + buildings[1].Length / 2, gap3 = buildings[2].S - buildings[2].Length / 2;
            var mural = Take(w.Civic.GetValueOrDefault("mural") ?? [], 1, Rng("civic.murals"));
            if (mural.Count > 0 && gap1 - gap0 >= 2 * TownFixtures.Size("mural").HalfS + 1)
                Fix("mural", mural[0].Title, mural[0].Text, (gap0 + gap1) / 2, side * (far - 0.05), 0, -side);
            var (_, nd, _) = TownFixtures.Size("memorial");
            if (Civic("memorial") is { } dead && gap3 - gap2 >= 2 * TownFixtures.Size("memorial").HalfS + 1)
                Fix("memorial", dead.Title, dead.Text, (gap2 + gap3) / 2, side * (far - nd - 0.05), 0, -side);
            // And a green of its own (note 490; the board's "a green or a garden for a small town"): grass in the square's
            // rear end, a tree in its far corner, the lamp garden by the way in, a bench between them to sit and look at it.
            var small = new TownGreen(s0 + sq.GreenAlong[0], s0 + sq.GreenAlong[1], sq.GreenOut[0], sq.GreenOut[1], side);
            homes.Green = small;
            double glen = small.S1 - small.S0, gmid = (small.Near + small.Far) / 2;
            var trees = Take(w.Civic.GetValueOrDefault("tree") ?? [], 1, Rng("civic.trees"));
            if (trees.Count > 0)
                Fix("tree", trees[0].Title, trees[0].Text, small.S0 + 2.5, side * (small.Far - 2.5), 0, -side);
            var garden = Take(w.Civic.GetValueOrDefault("garden") ?? [], 1, Rng("civic.garden"));
            if (garden.Count > 0)
                Fix("garden", garden[0].Title, garden[0].Text, small.S1 - glen * 0.3, side * (gmid - 1), 0, -side);
            Fix("bench", "a bench", "", small.S0 + glen * 0.4, side * (small.Near + 1.2), 0, side);
        }
        if (homes.Bounds is { } walls)
        {
            // The day, painted on the inside of the back wall at the ends of the first streets, where you see it down them.
            // Dave's (signed "D.)"; note 570) only on some towns' walls, one at most (towns.json daveMural).
            var all = w.Civic.GetValueOrDefault("mural") ?? [];
            bool daves(TownText m) => m.Title.Contains("D.)", StringComparison.Ordinal);
            var murals = Take([.. all.Where(m => !daves(m))], 2, Rng("civic.murals"));
            var drng = Rng("civic.dave");
            if (murals.Count > 0 && drng.Chance(t.DaveMural) && all.Where(daves).ToList() is { Count: > 0 } his)
                murals[drng.Chance(0.5) ? 0 : murals.Count - 1] = drng.Pick(his);
            // (A town type's warren has none of the street town's streets, note 692: the back wall either side of the way in's
            // gatehouse, where everybody coming in from the last night sees it.)
            var at = walls.Streets.Count > 0 ? walls.Streets.Select(x => x.At(walls.Rear)).ToList()
                : [side * (Run.Fortresses.GateOuter + MuralByGate), -side * (Run.Fortresses.GateOuter + MuralByGate)];
            for (int i = 0; i < murals.Count && i < at.Count; i++)
                Fix("mural", murals[i].Title, murals[i].Text, walls.Rear + Run.Fortresses.WallHalf + 0.05, at[i], 1, 0);
        }

        // The night market (queue #552, note 825): its stalls lit and open in a row between the buildings' fronts and the
        // centrepiece, each where it's clear of what's in the square and of the doors. Its stalls are listed after every other
        // fixture but the forge, and its stallholders after every other townsperson but the blacksmith and the Flame's keeper,
        // so every other keeps its index and its name.
        var market = Market(sq, s0, s1, side * (front - sq.MarketIn), population, fixtures, buildings, homes.Green, w, industry, Rng("market"));

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
            // Out on the street in front of it, clear of an enclosed porch (HouseDesign.VestibuleDepth); in front of it as it
            // stands turned (a town type's ring of houses turns them far round, note 692).
            var (ls, ld) = h.Body((i % 3 - 1) * 1.2, -2.6);
            var (lfs, lfd) = h.BodyFacing(0, -1);
            spots.Add(new("", ls, ld, lfs, lfd, 0, Street: true, Pose: "lantern"));
        }
        // Its people out at their own work among its houses (note 841; the director, 16 Oct, in a Flooder town: "There's
        // also nobody walking around. There's no kind of like culture going on"): one for every Out[0] of its houses, spread
        // evenly through the town, at a house's front corner facing it (a hull caulked, the water's mark read on a stilt), or
        // out with a lamp, walking its street.
        if (homes.Out is [> 0, > 0] && homes.OutActs.Length > 0)
        {
            // Any house standing shut, lived in or left (a neighbour's hull caulked as their own): the lived ones keep to
            // the square's end of a town that's emptied.
            var lit = lived.Take(outdoors).ToHashSet();
            var further = homes.Houses.Where(h => h.Layout is null && h.Kind != HouseKind.Burnt && !lit.Contains(h)).ToList();
            int count = Math.Min(Math.Min(homes.Out[1], homes.Houses.Count / homes.Out[0]), further.Count);
            for (int i = 0; i < count; i++)
            {
                var h = further[i * further.Count / count];
                string act = homes.OutActs[i % homes.OutActs.Length];
                double u = (i % 2 == 0 ? -1 : 1) * Math.Max(0, h.Width / 2 - OutAtCorner);
                var (os, od) = h.Body(u, -OutInFront - (h.Design.Hull ? HouseDesign.HullBow : 0));
                var (ofs, ofd) = act == "lantern" ? h.BodyFacing(0, -1) : h.BodyFacing(0, 1);
                spots.Add(new("", os, od, ofs, ofd, 0, Street: act == "lantern", Pose: act));
            }
        }
        // The works' hands (note 353), at work all night: in front of a piece with a door or a mouth to it, one a kind.
        if (homes.Works is { } atWork)
            foreach (var f in fixtures.Where(f => atWork.Holds(f.S, f.D) && f.Kind is "headframe" or "winding" or "casting" or "glasshouse"
                or "elevator" or "warehouse").DistinctBy(f => f.Kind).Take(t.Walled.WorksHands))
                spots.Add(new("hand", f.S, f.D + f.FaceD * (f.SolidD + 1.2), -f.FaceS, -f.FaceD, 0));
        // The rail yard's porters (note 502), at the office's door and the weighbridge's window: off the siding.
        if (homes.Yard is { } inYard)
            foreach (var f in fixtures.Where(f => inYard.Holds(f.S, f.D) && f.Kind is "yardoffice" or "weighbridge").Take(t.Walled.YardHands))
                spots.Add(new("porter", f.S, f.D + f.FaceD * (f.SolidD + 1.2), -f.FaceS, -f.FaceD, 0));

        // The night market's stallholders (note 825), each behind their counter, facing the square: after everyone else with
        // a place, before the blacksmith, who stays last.
        foreach (var (ms, md, face, _) in market)
            spots.Add(new("stallholder", ms, md - face * (TownFixtures.Size("marketstall").HalfD + StallholderBack), 0, face, 0));

        // The town's blacksmith, who does the trains (note 697; the director, 13 Oct 2026: "a vendor who is clearly marked as
        // the vendor blacksmith of the town who does train upgrades"): every town has one, at the forge beside the engine as it
        // stands in the yard. Last of everyone, so who everybody else is and what they're called is as it was.
        if (t.Smithy is { } smithy)
            spots.Add(new("blacksmith", site.Gate + smithy.FromGate, side * (smithy.Out - smithy.Stand), 0, -side, 0));
        // A harbour town's keeper of the Flame (queue #547, note 819), at the Hope Tower's door all night, looking out over
        // the town between the hours' tubs of coal.
        foreach (var f in fixtures.Where(f => f.Kind == "lighthouse" && f.House < 0).Take(1))
            spots.Add(new("flamekeeper", f.S + f.FaceS * (f.SolidS + 1.2), f.D + f.FaceD * (f.SolidD + 1.2), f.FaceS, f.FaceD, 0));

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
        var people = new List<(string Role, string Name, Spot Spot, TownPersonality? Mind)>();
        foreach (var spot in spots)
        {
            string role = spot.Role.Length > 0 ? spot.Role : spot.House >= 0 ? "home" : rrng.Pick(Folk);
            // Nicki's guests have come from their own houses (note 571); Nicki is just Nicki.
            bool partying = spot.Part is "host" or "guest";
            string? family = spot.House >= 0 && !partying ? homes.Houses[spot.House].Family : null;
            if (folk is null)
            {
                people.Add((role, NewName(family), spot, null));
                continue;
            }
            // Who they are, then what they're called by it (note 474). A lamp-carrier on the street is "street" to the
            // matrix (their role's a passer-by's).
            string? absent = spot.House >= 0 && homes.Vars(spot.House) is { } hv && hv.TryGetValue("{absent}", out var lost) ? lost : null;
            var mind = folk.Person(people.Count, spot.Street ? "street" : role, spot.Part, spot.House, family, absent, names);
            if (spot.Part == "host" && homes.Party is { } host)
            {
                mind = mind with { Given = host.Name, Surname = "", Byname = "", Heritage = "" };
                names.Add(mind.Name);
            }
            people.Add((role, mind.Name, spot, mind));
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
        // The creed (note 822): heard once the custom's lines are spoken for, before the town's other habits.
        var creedLines = new Deck<string>(creed?.Lines ?? [], Rng("lines.creed"));
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
        var temperDecks = new Dictionary<string, Deck<string>?>();
        Deck<string>? TemperDeck(string temperament)
        {
            if (!temperDecks.TryGetValue(temperament, out var deck))
                temperDecks[temperament] = deck = folk is not null && folk.Writing.Temperaments.TryGetValue(temperament, out var tw)
                    ? new Deck<string>(tw.Lines, Rng("lines.temper." + temperament)) : null;
            return deck;
        }
        var lrng = Rng("lines");
        var said = new List<string>[people.Count];
        var custom = new int[people.Count][];
        foreach (int i in Enumerable.Range(0, people.Count).OrderBy(i => Precedence(people[i].Spot)).ThenBy(i => i))
        {
            var (role, name, spot, street) = (people[i].Role, people[i].Name, people[i].Spot, people[i].Spot.Street);
            var lines = new List<string>();
            int first = 1;
            int want = lrng.RangeInclusive(t.LinesPerPerson[0], t.LinesPerPerson[1]);
            // How much they'll tell (note 474): somebody close keeps the custom to themselves (its keeper never does) and
            // says as little as anyone; somebody open says as much as anyone.
            var mind = people[i].Mind;
            bool close = mind is not null && mind.Traits.Telling < folk!.Tuning.Telling.Close && role != "keeper";
            if (mind is not null)
                want = close ? t.LinesPerPerson[0] - 1 : mind.Traits.Telling > folk!.Tuning.Telling.Open ? t.LinesPerPerson[1] : want;
            if (spot.House >= 0)
            {
                // Their household's story, in their own part, then the custom.
                // Nicki: the wine first, then two of hers, and nothing else (note 571).
                if (spot.Part == "host")
                {
                    lines.AddRange(homes.Story(spot.House, "offer").Take(1));
                    lines.AddRange(homes.Story(spot.House, "host"));
                    want = lines.Count;
                }
                else
                    foreach (var line in homes.Story(spot.House, spot.Part))
                        lines.Add(line);
                first = lines.Count;
                // At the party, nobody's talking about the custom tonight.
                if (!close && spot.Part != "guest" && spot.Part != "host" && (cultureLines.Next() ?? creedLines.Next()) is { } town)
                    lines.Add(town);
            }
            else
            {
                string? job = JobDeck(role).Next(), town = (street ? inside.Next() : close ? null : cultureLines.Next() ?? creedLines.Next()) ?? spare.Next()
                    ?? trade.Next();
                // The gatekeeper says the town's law first, as you come in (the first thing anyone in a town tells you).
                if (role == "gatekeeper" && w.Welcome.Length > 0)
                    lines.Add(Rng("welcome").Pick(w.Welcome));
                bool talksTown = spot.Role.Length == 0 || role == "keeper";
                foreach (var line in talksTown ? new[] { town, job } : [job, town])
                    if (line is not null)
                        lines.Add(line);
            }
            // How they carry it (their temperament's line, note 474): the second thing they say, after a household's story
            // or the gate's law.
            if (mind is not null && spot.Part != "host" && TemperDeck(mind.Temperament)?.Next() is { } temper)
                lines.Insert(Math.Min(lines.Count, first), temper);
            if (lines.Count < want && lrng.Chance(t.ScrapShare) && scraps.Next() is { } scrap)
                lines.Add(scrap);
            // Short of lines: the town's habits and trade, another of the job's, a scrap of a thread after all, and last what
            // anybody says.
            while (lines.Count < want && (spare.Next() ?? inside.Next() ?? trade.Next() ?? JobDeck(role).Next() ?? scraps.Next() ?? anyone.Next()) is { } extra)
                lines.Add(extra);
            var vars = spot.House >= 0 ? homes.Vars(spot.House) : null;
            var kept = lines.Take(Math.Max(Math.Max(1, want), spot.House >= 0 ? 2 : 1)).ToList();
            said[i] = [.. kept.Select(l => fill.In(l, name, vars))];
            // Which are the custom's (note 971): once it's been told in the town, they say something else in its place.
            custom[i] = [.. kept.Select((l, k) => (l, k)).Where(x => culture.Lines.Contains(x.l) || creed?.Lines.Contains(x.l) == true).Select(x => x.k)];
        }
        var townsfolk = new List<Townsperson>();
        for (int i = 0; i < people.Count; i++)
        {
            var (role, name, spot, mind) = people[i];
            string title = spot.House >= 0 ? homes.Title(spot.House, spot.Part)
                : role == "hand" ? industry?.Hand ?? "townsman" : w.Roles.TryGetValue(role, out var rt) ? rt.Title : role;
            townsfolk.Add(new Townsperson(i, name, fill.In(title, name, spot.House >= 0 ? homes.Vars(spot.House) : null), role, spot.S, spot.D, spot.Up, spot.FaceS, spot.FaceD,
                (int)(Streams.Mix(seed, "look", name) % 8), said[i], spot.House, spot.Pose, TownGear.Pick(homes.Gear, Streams.Mix(seed, "gear", name)), mind)
            { Hosting = spot.Part == "host", Custom = custom[i] });
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

        // The company's board of lost crews (GDD §3.1, §11; the director, 14 Oct 2026; note 721), beside the notices: each
        // ended campaign the player rode with is a card on it, from their own profile (the app's, not the plan's). Last of the
        // fixtures, so every other keeps its index.
        Fix("lostcrews", "the board of lost crews", LostCrewsText, mid + LostCrewsAlong, side * BoardOut, 0, -side);
        // The night market's stalls (note 825), after it.
        foreach (var (ms, md, face, on) in market)
            Fix("marketstall", on.Title, on.Text, ms, md, 0, face);

        // The board: the custom's order first, then what the town makes, then whatever else is posted; and a note or two
        // left about the square (a letter on a bench, a page by the hall door, a scrap by the engine).
        var papers = new List<TownPaper>();
        var prng = Rng("papers");
        var board = fixtures.First(f => f.Kind == "board");
        var posted = new List<TownText> { culture.Notes[0] };
        // Then the town's own notices (note 669; the director, 13 Oct: "actual like custom writing"), two of them, on a
        // stream of their own so the rest of the board is dealt as it was.
        var own = new Deck<TownText>(culture.Board, Rng("papers.board"));
        for (int i = 0; i < t.BoardOwn && own.Next() is { } o; i++)
            posted.Add(o);
        // And the creed's first (note 822: the Flooders' Water Book), after the town's own.
        if (creed is { Notes.Length: > 0 })
            posted.Add(creed.Notes[0]);
        var more = new Deck<TownText>([.. industry?.Notes ?? [], .. quirks.SelectMany(q => q.Notes), .. creed?.Notes.Skip(1) ?? [], .. w.Notices,
            .. w.Threads.SelectMany(th => th.Notes)], prng);
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

        // The forge, last of the fixtures (so every other one's number is as it was): its board lettered to read from the cab.
        if (t.Smithy is { } forge)
            Fix("forge", w.Forge?.Title ?? "the forge", fill.In(w.Forge?.Text ?? "", null), site.Gate + forge.FromGate, side * forge.Out, 0, -side);

        var plan = new TownPlan(site.Name, population, former, culture.Id, culture.Creature, culture.Law.Replace("{town}", site.Name), culture.Hall,
            industry?.Name ?? site.Industry, [.. quirks.Select(q => q.Id)], square, buildings, homes.Houses, townsfolk, papers, fixtures, homes.Character, homes.Bounds,
            homes.Green, homes.Works, homes.Yard)
        {
            Gate = site.Gate,
            Frame = FrameOf(t, site.Gate, homes.Bounds),
            Form = form?.Form ?? "",
            Districts = form?.Districts ?? [],
            Laid = form is null ? "" : $"stairs {form.Stairs}, cut off {form.CutOff}, on ways {form.OnWays}, on landmarks {form.OnLandmarks}, off their ground {form.OffGround}",
            Margin = form?.Margin ?? 0,
            Ways = form?.Ways ?? [],
            Wards = form?.Wards ?? [],
            Moles = form?.Moles ?? [],
            Quarrels = Quarrels(t.Quarrels, townsfolk, Rng("quarrels")),
        };
        plan = Remember(plan, site.Burnt, w, Rng("remember"), quiet: t.Standing.Of(site.Cold) > 0);
        // Its standing with the crew (note 1024), on a stream of its own: a town nobody's brought anything into draws nothing.
        plan = Cold(plan, site.Cold, t.Standing, w, Rng("standing"));
        // What stands between the houses (note 989), last, on a stream of its own: nothing else in the town moves for it.
        return plan with { Between = TownBetween.Lay(plan, t.Between, Rng("between")) };
    }

    /// <summary>
    /// Where the town's two gates and its yard are (note 692): in at the rear wall from the last night, out at the gate to
    /// the next, the longest consist (towns.json <c>forms.yardM</c>) standing back from the way out.
    /// </summary>
    static TownFrame FrameOf(TownTuning t, double gate, TownBounds? bounds)
    {
        double exit = gate, entry = bounds?.Rear ?? 0;
        double front = exit - t.Forms.ShortOfGate;
        return new TownFrame(entry, exit, Math.Max(entry, front - t.Forms.YardM), front);
    }

    /// <summary>
    /// The <paramref name="count"/> houses a creature brought in through the gate burns (note 608): lived-in houses, the
    /// nearest where the train come in stands first (<see cref="FromWayIn"/>), the same ones every time for the same plan. A plan with
    /// some burnt already gives the next ones, so tonight's fires and the town the crew comes back to agree.
    /// </summary>
    public static IReadOnlyList<TownHouse> Harmed(TownPlan plan, int count) =>
        count <= 0 ? [] : [.. plan.Houses.Where(h => h.Kind == HouseKind.Lived).OrderBy(h => FromWayIn(plan, h)).ThenBy(h => h.Id).Take(count)];

    /// <summary>
    /// How far a house stands from where a train come in through the town's way in stands (m; note 966): along the line from
    /// its rear gate (<see cref="TownFrame.Entry"/>; the engine's home is run.json homeInsideGateM inside it, its rake behind),
    /// and out from the line, as the crew on the ground there sees it. The gate's houses (the way out) stood about 430 m up
    /// the town on frontier:7 (note 958), the fire neither seen nor heard from the train.
    /// </summary>
    public static double FromWayIn(TownPlan plan, TownHouse h)
    {
        double along = h.S - plan.Frame.Entry;
        return Math.Sqrt(along * along + h.D * h.D);
    }

    /// <summary>
    /// The town remembering what's been brought into it (note 608): its <paramref name="burnt"/> houses (<see cref="Harmed"/>)
    /// burnt out and shut, nobody home, and its gatekeeper and guards saying so first.
    /// </summary>
    /// <param name="quiet">The gatekeeper says why the town's cold instead (<see cref="Cold"/>), so not this as well.</param>
    static TownPlan Remember(TownPlan plan, int burnt, TownWriting w, Pcg32 rng, bool quiet = false)
    {
        var harmed = Harmed(plan, burnt);
        if (harmed.Count == 0 || w.BroughtIn is not { } said)
            return plan;
        var gone = harmed.Select(h => h.Id).ToHashSet();
        string Fill(string text, string? family = null) => text.Replace("{town}", plan.Name).Replace("{family}", family ?? "");
        var houses = plan.Houses.Select(h => gone.Contains(h.Id)
            ? h with { Kind = HouseKind.Burnt, Layout = null, Text = said.Houses.Length > 0 ? Fill(rng.Pick(said.Houses), h.Family) : h.Text }
            : h).ToList();
        var people = plan.People.Select(p =>
        {
            var lines = p.Role switch { "gatekeeper" => said.Gate, "guard" => said.Guard, _ => [] };
            if (lines.Length == 0)
                return p;
            var line = Fill(rng.Pick(lines));
            if (quiet && p.Role == "gatekeeper")
                return p;
            var all = p.Lines.ToList();
            // The gate's law first, as ever; then this.
            all.Insert(p.Role == "gatekeeper" && all.Count > 0 ? 1 : 0, line);
            return p with { Lines = all };
        }).ToList();
        return plan with { Houses = houses, People = people, Burnt = harmed.Count };
    }

    /// <summary>
    /// The town cold to the crew (note 1024; GDD §3.1 "A town remembers"): <paramref name="cold"/> percent, from the creatures
    /// they brought in through its gate lately. The gatekeeper says why, on the way in; the blacksmith says what it costs now;
    /// of everyone else, about <c>wary</c> × how cold it is open on a wary line, and as many of the lived-in doors stay shut
    /// to them (towns.json <c>standing</c>). Nothing at all with it off or the town warm.
    /// </summary>
    static TownPlan Cold(TownPlan plan, int cold, StandingTuning t, TownWriting w, Pcg32 rng)
    {
        double c = t.Of(cold);
        if (c <= 0 || w.BroughtIn is not { } said)
            return plan with { Cold = t.On ? cold : 0 };
        string Fill(string text, string? family = null) => text.Replace("{town}", plan.Name).Replace("{family}", family ?? "");
        var people = plan.People.Select(p =>
        {
            (string[] lines, double chance) = p.Role switch
            {
                "gatekeeper" => (said.Standing, 1.0),
                "blacksmith" => (t.Dearer > 0 ? said.Smith : [], 1.0),
                "guard" => ([], 0.0),
                _ => (said.Wary, t.Wary * c),
            };
            // Drawn for everyone alike, so who's wary doesn't move with who has lines to say it in.
            bool says = rng.Chance(chance);
            string? line = lines.Length > 0 ? Fill(rng.Pick(lines)) : null;
            if (!says || line is null)
                return p;
            var all = p.Lines.ToList();
            // The gate's law first, as ever; then why.
            all.Insert(p.Role == "gatekeeper" && all.Count > 0 ? 1 : 0, line);
            return p with { Lines = all };
        }).ToList();
        var houses = plan.Houses.Select(h =>
        {
            // Only a door the crew knocks on (an open house's door stands open: it's walked into, note 281).
            bool shut = h.Kind == HouseKind.Lived && h.Layout is null && rng.Chance(t.Doors * c);
            string? line = said.Door.Length > 0 ? Fill(rng.Pick(said.Door), h.Family) : null;
            return shut && line is not null ? h with { Text = line } : h;
        }).ToList();
        return plan with { People = people, Houses = houses, Cold = cold };
    }

    /// <summary>How far a piece of stock stands out either side of its siding's middle (m; TownFixtures "van", "flatdory").</summary>
    const double StockHalf = 1.5;

    /// <summary>
    /// A walled town's rail yard (queue #239, note 502; GDD §3: "Fortified towns survive behind stone and steel walls ... rail
    /// yards and warehouses"): a buffer stop at each end of its siding; its pieces (world/towns.json "yard") fronting the
    /// siding from the gate end back, each that fits short of the yard's rear end (<see cref="TownWork.After"/>: room left
    /// beside it toward that end, the weighbridge's plate); the lamps down the siding's near side; and the stock left
    /// standing in the siding, one to a stretch of it, clear of each other and the buffer stops.
    /// </summary>
    static void RailYard(TownYard yard, WalledTuning wt, TownWriting w, string town, Pcg32 rng,
        Action<string, string, string, double, double, double, double, int, double?> fix)
    {
        void Fix(string kind, string name, string text, double s, double d, double faceS, double faceD) =>
            fix(kind, name.Replace("{town}", town), text.Replace("{town}", town), s, d, faceS, faceD, -1, null);
        int side = yard.Side;
        var buffers = w.Yard.Where(p => p.Kind == "buffers").ToList();
        double bd = TownFixtures.Size("buffers").HalfD;
        Fix("buffers", buffers.ElementAtOrDefault(0)?.Title ?? "a buffer stop", buffers.ElementAtOrDefault(0)?.Text ?? "",
            yard.Siding1 + bd, side * yard.SidingD, -1, 0);
        Fix("buffers", buffers.ElementAtOrDefault(1)?.Title ?? "a buffer stop", buffers.ElementAtOrDefault(1)?.Text ?? "",
            yard.Siding0 - bd, side * yard.SidingD, 1, 0);
        // The pieces front the siding's stock (a goods shed's dock at a van's side), the far ones back toward the street.
        double front = yard.SidingD + StockHalf + 0.1, back = yard.Far - wt.YardMargin, at = yard.S1 - wt.YardMargin;
        foreach (var piece in w.Yard)
        {
            if (piece.Kind == "buffers")
                continue;
            var (hs, hd, _) = TownFixtures.Size(piece.Kind);
            double d = front + piece.Across + hd;
            if (d + hd > back || at - 2 * hs - piece.After < yard.S0 + wt.YardMargin)
                continue;
            Fix(piece.Kind, piece.Title, piece.Text, at - hs, side * d, 0, -side);
            at -= 2 * hs + piece.After + wt.YardGap;
        }
        // Its lamps between the platform's back and the siding, lighting the stock.
        double lampD = (yard.Near + yard.SidingD - StockHalf) / 2;
        for (double s = yard.S1 - wt.YardLamps / 2; s > yard.S0 + 2; s -= wt.YardLamps)
            Fix("yardlamp", "", "", s, side * lampD, 0, side);
        // The stock: the siding cut into a stretch apiece, each standing somewhere in its own, facing up or down the line.
        int count = Math.Min(w.Stock.Length, rng.RangeInclusive(wt.YardStock[0], wt.YardStock[1]));
        var stock = Take(w.Stock, count, rng.Fork(1));
        double from = yard.Siding0 + 0.5, stretch = (yard.Siding1 - 0.5 - from) / Math.Max(1, stock.Count);
        for (int i = 0; i < stock.Count; i++)
        {
            double half = TownFixtures.Size(stock[i].Kind).HalfD;
            if (2 * half > stretch)
                continue;
            double s = from + i * stretch + half + rng.NextDouble() * (stretch - 2 * half);
            Fix(stock[i].Kind, stock[i].Title, stock[i].Text, s, side * yard.SidingD, rng.Chance(0.5) ? 1 : -1, 0);
        }
    }

    /// <summary>
    /// Where a town's people out at their work stand (note 841): in from a house's corner along its front, and out in front
    /// of it (past a houseboat's hull), a body's room from what's solid (m).
    /// </summary>
    const double OutAtCorner = 0.6, OutInFront = 1.2;

    /// <summary>How far behind a market stall's counter its stallholder stands (m), and how wide a door's way is kept clear.</summary>
    const double StallholderBack = 0.7, DoorClear = 1.6;

    /// <summary>
    /// The night market (queue #552, note 825; the director's playtest of 14 Oct, line 3: "The town's looking better ...
    /// Still a bit basic"; of 19 Oct, line 4, note 870: "Too many cut and paste stalls in a row ... doesn't look like a
    /// market"): two rows of stalls facing each other across an aisle (<see cref="SquareTuning.MarketAisle"/>), the first
    /// <paramref name="d"/> out with its counters to the line, the second nearer the line with its counters to the
    /// buildings; along each, every <see cref="SquareTuning.MarketEvery"/> from ten metres in from the square's ends (the
    /// second row's places half a step on from the first's), each stall set off its place by up to
    /// <see cref="SquareTuning.MarketJitter"/> each way, where it stands clear of every fixture so far, of the
    /// buildings' doors and of a small town's green; as many of those as the town's size has stalls
    /// (<see cref="SquareTuning.Market"/>), drawn at random and laid in order along the square. Each stall's wares are
    /// dealt from the town's trade's stalls and the market's own, none twice and none a shut stall's already. Face: the way
    /// its counter looks, across the line (±1).
    /// </summary>
    static List<(double S, double D, int Face, TownText Wares)> Market(SquareTuning sq, double s0, double s1, double d, int population,
        List<TownFixture> fixtures, List<TownBuilding> buildings, TownGreen? green, TownWriting w, TownIndustry? industry, Pcg32 rng)
    {
        int want = Math.Clamp(sq.Market[0] + (int)(population / Math.Max(1, sq.MarketPer)), sq.Market[0], sq.Market[1]);
        var (hs, hd, _) = TownFixtures.Size("marketstall");
        int side = Math.Sign(d);
        // The rows: the first toward the buildings, counters to the line; the second across the aisle from it.
        var rows = new[] { (D: d, Face: -side, From: 0.0), (D: d - side * (2 * hd + sq.MarketAisle), Face: side, From: sq.MarketEvery / 2) };
        var clear = new List<(double S, double D, int Face)>();
        var jitter = rng.Fork(2);
        foreach (var row in rows)
            for (double s = s0 + 10 + row.From; s <= s1 - 10; s += sq.MarketEvery)
            {
                // Each off its place a little, along and across, so the rows are a market's, not a ruled line.
                double ps = s + (jitter.NextDouble() * 2 - 1) * sq.MarketJitter, pd = row.D + (jitter.NextDouble() * 2 - 1) * sq.MarketJitter;
                if (fixtures.All(f => Math.Abs(f.S - ps) > f.SolidS + hs + 0.8 || Math.Abs(f.D - pd) > f.SolidD + hd + 0.8)
                    && buildings.All(b => Math.Abs(b.S - ps) > hs + DoorClear) && green?.Holds(ps, pd, Math.Max(hs, hd)) != true)
                    clear.Add((ps, pd, row.Face));
            }
        // Which of them: a shuffle of the clear places, the first as many as are wanted, in order along the square.
        for (int i = clear.Count - 1; i > 0; i--)
        {
            int j = rng.RangeInclusive(0, i);
            (clear[i], clear[j]) = (clear[j], clear[i]);
        }
        var wares = new Deck<TownText>([.. (industry?.Stalls ?? []).Where(x => fixtures.All(f => f.Text != x)).Select(x => new TownText("a market stall", x)),
            .. w.Market], rng.Fork(1));
        var stalls = new List<(double, double, int, TownText)>();
        foreach (var (s, sd, face) in clear.Take(want).OrderBy(x => x.S).ThenBy(x => x.Face))
            if (wares.Next() is { } on)
                stalls.Add((s, sd, face, on));
        return stalls;
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

        /// <summary>The cards shuffled, or dealt in the order given with no <paramref name="rng"/> (already drawn in order).</summary>
        public Deck(IReadOnlyList<T> cards, Pcg32? rng)
        {
            _cards = [.. cards];
            if (rng is not { } r)
                return;
            for (int i = _cards.Count - 1; i > 0; i--)
            {
                int j = (int)(r.NextDouble() * (i + 1));
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
        // Note 687: the blacksmith's forge, its hood and its board (the chimney's above).
        "forge" => (1.7, 1.1, 3.4),
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
        "board" or "lostcrews" => (1.1, 0.15, 2.2),
        "plaque" => (0.2, 0.2, 2.2),
        "crate" => (0.5, 0.4, 0.8),
        "barrel" => (0.35, 0.35, 1.0),
        "stall" => (0.9, 1.5, 2.6),
        // The night market's stalls (note 825): open, a counter of its wares, an awning and a lantern, end on along the row.
        "marketstall" => (1.4, 1.2, 2.7),
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
        // A town's works (note 353), its front to the line: the pit's headframe and its winding house, the tip; the elevator,
        // a glasshouse, a root cellar; the casting shed and its stack, the slag; coal and pig iron in heaps; the warehouse,
        // the water tower.
        // (Those that are the facilities' modelled pieces are their meshes' footprints as the works draw them, which
        // TownWorksArtTests holds them to: the headframe's legs, half as big again; the winding house with its boiler house
        // and chimney behind; the tip at 0.55; the elevator at 0.8; the foundry's shed and stack at 0.6.)
        "headframe" => (2.8, 3.4, 29),
        "winding" => (13.0, 8.0, 14.5),
        "tip" => (12.7, 9.8, 8.0),
        "elevator" => (14.4, 7.0, 34),
        "glasshouse" => (3.6, 8.0, 3.9),
        "cellar" => (2.6, 3.2, 2.2),
        "casting" => (24.2, 6.9, 9),
        "slag" => (6.0, 4.5, 4.0),
        "coal" => (4.2, 3.0, 2.6),
        "pigs" => (2.2, 1.2, 1.1),
        "warehouse" => (5.5, 7.5, 7.5),
        "watertower" => (2.2, 3.2, 10.2),
        // The town types' landmarks (note 692): the harbour's lighthouse on its head and the beacon at the breakwater's end,
        // its boat shed on the slip; a chapel on a hummock or a hill; a watch tower; a quarry's lift and crane; the seed house.
        // The harbour's light is its Hope Tower (queue #547, note 819; HopeKit): its plinth and the coal bay under its hoist,
        // and up to its fire basket's brim.
        "lighthouse" => (8.4, 8.4, 84),
        "beacon" => (1.2, 1.2, 6),
        "boatshed" => (9.0, 6.0, 7.5),
        "chapel" => (5.5, 9.0, 12),
        "watchtower" => (2.7, 2.7, 16),
        "lift" => (3.0, 3.0, 30),
        "crane" => (2.0, 2.0, 14),
        // A dory tied at a jetty's foot (note 692): its beam across, its length along its bow's way.
        "dory" => (0.8, 2.6, 0.9),
        // A stack of salt hay on its staddle out in a dykeland's marsh (note 1031): its ring of posts, its stack and its pole.
        "haystack" => (2.1, 2.1, 5.2),
        "seedhouse" => (6.0, 4.0, 6.0),
        // A glasshouse town's great glasshouses (note 692): the works' glasshouse at 2.6 times its size, end on to the line.
        "greathouse" => (9.4, 20.8, 10.1),
        // The second five town types' landmarks (note 692): a mill stack (SettingKit.Chimney); a foundry's and a sawmill's
        // sheds (the foundry shed's model, at the works' 0.6 and at 0.5); a stack of logs; a watchtower of logs on legs; a
        // windpump; a processing shed (the fish shed at 1.6); a keep (a wall's tower at 1.8); a barge (the dory at 4.5).
        "stack" => (2.2, 2.2, 26),
        "foundry" => (24.2, 6.9, 9),
        "sawmill" => (20.2, 5.8, 7.5),
        "logs" => (5.2, 2.0, 2.4),
        "timbertower" => (2.7, 2.7, 14.4),
        "windpump" => (2.4, 2.4, 16),
        "shed" => (9.0, 6.0, 7.5),
        "keep" => (4.6, 4.6, 24),
        "barge" => (3.3, 11.7, 3.5),
        // A town's rail yard (note 502), behind the home platform: the goods shed (the facility's modelled piece, its walls
        // and dock as TownYardArtTests holds them: the office at one end, the steps at the other; its canopy and its crane's
        // jib reach out over the siding), the yard office, the weighbridge's house (its plate lies beside it, flush), freight
        // stacked waiting, a handcart, old sleepers stacked to dry; in the siding a van and a flat wagon with a dory or rails
        // on it (half their width, half their length: they face along it),
        // the buffer stops at its ends (half their width across it, their depth); the lamps down it.
        "goodsshed" => (17.9, 6.4, 9.1),
        "yardoffice" => (2.4, 1.8, 3.8),
        "weighbridge" => (1.3, 1.3, 3.0),
        "freight" => (2.0, 1.0, 1.8),
        "cart" => (1.0, 1.6, 1.5),
        "sleepers" => (1.4, 1.4, 1.35),
        "van" => (1.5, 4.2, 3.7),
        "flatdory" or "flatrails" => (1.4, 4.6, 2.2),
        "buffers" => (1.4, 0.45, 1.2),
        "yardlamp" => (0.15, 0.15, 4.6),
        // In the houses (their furniture is the layout's): only the cradle stands on its own feet.
        "cradle" => (0.45, 0.3, 0.7),
        "table" or "letters" or "anklebell" or "timetable" or "boots" or "boards" or "suitcase" or "radio" or "clock"
            or "stove" or "stairdoor" or "photo" => (0, 0, 0.5),
        _ => (0.5, 0.5, 1.5),
    };

    /// <summary>
    /// The works' heaps, slag and coal (WorksKit.Heap, note 870's mound tipped from a line: a ridge along X at its crest, set
    /// back toward +Z, its slopes straight, gullied): a mound, not a block.
    /// </summary>
    public static bool Heap(string kind) => kind is "slag" or "coal";

    /// <summary>
    /// A heap's walls (note 869; the director's playtest of 19 Oct line 2, "Collision boxes are off": a slag heap stood a 4 m
    /// box from the ground over its whole footprint, so its foot stopped you in mid-air): a stack of layers no deeper than
    /// <paramref name="step"/> (player.json stepUp: walked up, as a heap is), each the cross of two boxes inside where the
    /// mound stands higher than the layer's foot. In the heap's own frame (WorksKit's: X along its front, +Z back from it),
    /// its footprint <paramref name="halfX"/> by <paramref name="halfZ"/>: each layer's half sizes, how far back its middle
    /// stands, its foot and top. From the art's shape at every height (its outline's least reach, <see cref="HeapReachLeast"/>;
    /// its gullies' deepest, <see cref="HeapGully"/> of its height), so a layer is never out past the mound drawn.
    /// </summary>
    public static IEnumerable<(double HalfX, double HalfZ, double Back, double Bottom, double Top)> HeapLayers(double halfX, double halfZ, double height,
        double step)
    {
        // Up to its crest, less what a gully takes off it.
        double peak = height * (1 - HeapGully);
        int layers = Math.Max(1, (int)Math.Ceiling(peak / Math.Max(0.05, step * 0.9)));
        for (int i = 0; i < layers; i++)
        {
            double below = peak * i / layers, top = peak * (i + 1) / layers;
            // How far out (t) its slope still stands over the layer's foot, its gullies' worst taken off: y = h (1 - t^1.2).
            double fall = Math.Min(1, below / height + HeapGully);
            double t = SixthRootOfFifth(1 - fall), r = t * HeapReachLeast;
            double x = halfX * (0.42 * (1 - r) + r), z = halfZ * (0.06 * (1 - r) + r), back = halfZ * 0.18 * (1 - t);
            // The cross inscribed in that ellipse: (0.88, 0.44) and (0.44, 0.88) of it, its corners just inside.
            yield return (x * 0.88, z * 0.44, back, below, top);
            yield return (x * 0.44, z * 0.88, back, below, top);
        }
    }

    /// <summary>
    /// x^(5/6), the art's t^1.2 undone (1 / 1.2 = 5 / 6), for x in 0..1: the y with y^6 = x^5, found by halving, so it's the
    /// same bits on every machine (the C library's Pow isn't; DeterministicMathTests).
    /// </summary>
    static double SixthRootOfFifth(double x)
    {
        double target = DMath.Pow(Math.Clamp(x, 0, 1), 5), lo = 0, hi = 1;
        for (int i = 0; i < 52; i++)
        {
            double mid = (lo + hi) / 2;
            (lo, hi) = DMath.Pow(mid, 6) < target ? (mid, hi) : (lo, mid);
        }
        return lo;
    }

    /// <summary>A heap's outline's least reach (of its footprint's half sizes) and its gullies' deepest (of its height): WorksKit.Heap's. Not design numbers.</summary>
    public const double HeapReachLeast = 0.72, HeapGully = 0.17;
}
