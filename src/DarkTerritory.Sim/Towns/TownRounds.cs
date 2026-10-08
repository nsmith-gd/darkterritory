using Ballast;
using DarkTerritory.Sim.LineGen;

namespace DarkTerritory.Sim.Towns;

/// <summary>
/// A stop on somebody's round, in the town's rail frame: where they stand (<see cref="S"/>, <see cref="D"/>), which way
/// they face there, what they do (<see cref="Act"/>: "idle", "seated", "crouch", "lantern", "read", "warm", "talk",
/// "vigil", "work", "mend"; the art plays it), and the way there from the stop before it (<see cref="Via"/>, round what's solid).
/// </summary>
public sealed record RoundStop(double S, double D, double FaceS, double FaceD, string Act, IReadOnlyList<(double S, double D)> Via);

/// <summary>
/// Somebody's round (note 353): <see cref="Stops"/>, one to a slot of <see cref="Slot"/> seconds, round and round on the
/// night's clock from <see cref="Offset"/> seconds in; the first is their post, where they were put.
/// </summary>
public sealed record TownRound(double Slot, double Offset, IReadOnlyList<RoundStop> Stops)
{
    public double Period => Slot * Stops.Count;
}

/// <summary>Where somebody is now: their feet (world), which way they face, what they're doing ("walk" between stops).</summary>
public readonly record struct TownPose(Double3 Feet, Double3 Facing, string Act, bool Walking);

/// <summary>
/// Plans a town's rounds (the director, 8 Oct 2026: "There needs to be a behaviour loop for all the NPCs, its weird that
/// so many of them are just standing around doing nothing"; ARCHITECTURE §8 note 353). Out of doors, everyone's day is
/// the same few slots: their post, then somewhere about the square, their post, somewhere else: a bench to sit on, a fire
/// barrel, the board, a stall, the centrepiece, or a word with somebody at their post. A place holds as many as it has
/// room for each slot, so two never stand in one, and the way there is clear of what's solid. The gate's people pace their
/// post; a lamp-carrier walks their street; the lampman the line. At home, a household goes round its rooms' places (the
/// range, the table, the parlour's chair, the window, the stair, the door), each in their own turn so no two share one, by
/// the way through the partition. Drawn from nothing but the plan and each person's name: every machine has the same.
/// </summary>
public static class TownRounds
{
    /// <summary>A place to go: where to stand, which way to face, the act, a short step from the approach (a bench's seat), and its room.</summary>
    sealed record Place(string Key, double S, double D, double FaceS, double FaceD, string Act, (double S, double D)? Approach);

    public static TownRound?[] Plan(Town town)
    {
        var plan = town.Plan;
        var t = town.Tuning.Rounds;
        var rounds = new TownRound?[plan.People.Count];
        if (t.Slot <= 0 || t.Slots < 2 || t.Walk <= 0)
            return rounds;
        int side = plan.Square.Side;

        // The square's places, each a slot's room for one.
        var places = new List<Place>();
        foreach (var f in plan.Fixtures.Where(f => f.House < 0))
            switch (f.Kind)
            {
                case "bench":
                    // Two to a bench, sat facing the way it does; stepped onto from in front.
                    foreach (double along in (double[])[-0.45, 0.45])
                        places.Add(new($"bench{f.Id}{along}", f.S + along, f.D, f.FaceS, f.FaceD, "seated", (f.S + along, f.D + f.FaceD * 0.9)));
                    break;
                case "barrel":
                    // Round a fire barrel, warming their hands, three to a fire.
                    for (int i = 0; i < 3; i++)
                    {
                        double a = (i + 0.5) * 2 * Math.PI / 3 + f.Id * 0.7;
                        double ds = DMath.Cos(a), dd = DMath.Sin(a);
                        places.Add(new($"barrel{f.Id}.{i}", f.S + ds * 1.0, f.D + dd * 1.0, -ds, -dd, "warm", null));
                    }
                    break;
                case "board":
                    foreach (double along in (double[])[-0.6, 0.6])
                        places.Add(new($"board{f.Id}{along}", f.S + along, f.D + f.FaceD * 1.1, -f.FaceS, -f.FaceD, "read", null));
                    break;
                case "plaque":
                    places.Add(new($"plaque{f.Id}", f.S, f.D + f.FaceD * 1.1, -f.FaceS, -f.FaceD, "read", null));
                    break;
                case "stall":
                    // At the stall's counter (it faces along the line, its back to the square's end), seeing to it.
                    places.Add(new($"stall{f.Id}", f.S + f.FaceS * (f.SolidD + 0.6), f.D, -f.FaceS, -f.FaceD, "work", null));
                    break;
                case "crate":
                    places.Add(new($"crate{f.Id}", f.S, f.D + f.FaceD * (f.SolidD + 0.7), -f.FaceS, -f.FaceD, "mend", null));
                    break;
                default:
                    // The centrepiece: stood before it a while (the custom's vigil), at the side away from the line.
                    if (f.Id == 0)
                        for (int i = 0; i < 2; i++)
                        {
                            double a = (i == 0 ? 0.35 : -0.35) + (side > 0 ? Math.PI / 2 : -Math.PI / 2);
                            double r = Math.Max(f.SolidS, f.SolidD) + 1.2;
                            double ds = DMath.Cos(a), dd = DMath.Sin(a);
                            places.Add(new($"vigil{i}", f.S + ds * r, f.D + dd * r, -ds, -dd, "vigil", null));
                        }
                    break;
            }

        var outdoors = plan.People.Where(p => p.House < 0).ToList();
        // Who's away in which slots: the people at their doors (the clerk, the keeper, the store, the fitter) go once, in the
        // middle of the day, when everyone else is back at their post; the rest go every other slot. So there's always
        // somebody at their post to go and have a word with.
        bool Out(Townsperson p, int k) => p.Role is "clerk" or "keeper" or "storekeeper" or "fitter" ? k == t.Slots / 2 : k % 2 == 1;
        // Where everyone's standing, slot by slot, so nobody's put where somebody already is.
        var stood = new List<Double3>[t.Slots];
        for (int k = 0; k < t.Slots; k++)
            stood[k] = [.. outdoors.Where(p => !Out(p, k)).Select(p => town.World(p.S, p.D, p.Up))];
        var taken = new HashSet<string>[t.Slots];
        for (int k = 0; k < t.Slots; k++)
            taken[k] = [];
        var planned = outdoors.ToDictionary(p => p.Id, p => new RoundStop[t.Slots]);
        for (int k = 0; k < t.Slots; k++)
            foreach (var p in outdoors)
            {
                var post = new RoundStop(p.S, p.D, p.FaceS, p.FaceD, p.Pose, []);
                if (!Out(p, k))
                {
                    planned[p.Id][k] = post;
                    continue;
                }
                var rng = new Pcg32(Streams.Mix(0, "rounds", p.Name, k), 0);
                var there = Away(town, p, places, taken[k], [.. outdoors.Where(o => !Out(o, k))], stood[k], k, ref rng);
                planned[p.Id][k] = there ?? post;
                if (there is not null)
                    stood[k].Add(town.World(there.S, there.D, p.Up));
            }
        foreach (var p in outdoors)
        {
            // Up off a bench by the way they sat down (stepped out in front of it), then on.
            var r = planned[p.Id];
            for (int k = 0; k < r.Length; k++)
                if (r[k].Act == "seated" && r[k].Via.Count > 0 && r[(k + 1) % r.Length] is var next && next != r[k])
                    r[(k + 1) % r.Length] = next with { Via = [r[k].Via[^1], .. next.Via] };
            rounds[p.Id] = new TownRound(t.Slot, 0, r);
        }

        // At home: round the rooms' places in turn.
        foreach (var group in plan.People.Where(p => p.House >= 0).GroupBy(p => p.House))
        {
            var house = plan.Houses[group.Key];
            if (house.Layout is not { } layout || layout.Spots.Count < 2)
                continue;
            var spots = layout.Spots;
            double offset = Streams.Mix(0, "rounds.home", house.Family, house.Id) % 1000 / 1000.0 * t.HomeSlot;
            // The ways between the places, worked out once a house.
            var ways = new Dictionary<(int, int), IReadOnlyList<(double S, double D)>>();
            foreach (var p in group)
            {
                // Their own place first (where the household was put), then on round the rest in the layout's order.
                int start = Nearest(house, spots, p);
                var stops = new List<RoundStop>();
                for (int k = 0; k < spots.Count; k++)
                {
                    int to = (start + k) % spots.Count, from = (to - 1 + spots.Count) % spots.Count;
                    var spot = spots[to];
                    var (s, d) = house.Rail(spot.U, spot.V);
                    var (fs, fd) = house.Facing(spot.FaceU, spot.FaceV);
                    if (!ways.TryGetValue((from, to), out var way))
                        ways[(from, to)] = way = Indoors(town, house, layout, (spots[from].U, spots[from].V), (spot.U, spot.V));
                    stops.Add(new RoundStop(s, d, fs, fd, spot.Pose, way));
                }
                rounds[p.Id] = new TownRound(t.HomeSlot, offset, stops);
            }
        }
        return rounds;
    }

    /// <summary>Where somebody goes in slot <paramref name="k"/>: their pace, their street, the line, or a place about the square with room.</summary>
    static RoundStop? Away(Town town, Townsperson p, List<Place> places, HashSet<string> taken, List<Townsperson> atPosts, List<Double3> stood, int k,
        ref Pcg32 rng)
    {
        var t = town.Tuning.Rounds;
        var at = town.World(p.S, p.D, p.Up);
        // Nobody stands where somebody already is this slot.
        bool Room(Double3 there) => stood.All(o => (o - there).Length > 0.9);
        RoundStop? Along(double by, string act)
        {
            var there = town.World(p.S + by, p.D, p.Up);
            return Room(there) && town.Free(there) && town.Clear(at, there) ? new RoundStop(p.S + by, p.D, by > 0 ? 1 : -1, 0, act, []) : null;
        }
        switch (p.Role)
        {
            // The gate's people and the porter on the platform keep to their post, pacing it.
            case "gatekeeper" or "guard" or "porter":
                return Along((k / 2 % 2 == 0 ? 1 : -1) * t.Pace, p.Pose) ?? Along((k / 2 % 2 == 0 ? -1 : 1) * t.Pace, p.Pose);
            // The lampman walks the line's lamps.
            case "lampman":
                return Along((k / 2 % 2 == 0 ? 1 : -1) * rng.Range(t.Street[0], t.Street[1]), "lantern");
        }
        // A lamp-carrier out in front of their house walks along their street to a neighbour's and stands there.
        if (p.Pose == "lantern")
        {
            double by = rng.Range(t.Street[0], t.Street[1]) * (rng.Chance(0.5) ? 1 : -1);
            return Along(by, "lantern") ?? Along(-by, "lantern");
        }
        // A word with somebody at their post, one time in three; else somewhere about the square with room this slot and
        // a clear way there; else that word after all.
        if (rng.Chance(1.0 / 3) && Talk(ref rng) is { } word)
            return word;
        foreach (var place in Shuffled(places, ref rng))
        {
            if (taken.Contains(place.Key))
                continue;
            var goal = place.Approach ?? (place.S, place.D);
            var there = town.World(goal.S, goal.D, p.Up);
            if ((there - at).Length > 40 || !Room(town.World(place.S, place.D, p.Up)) || !town.Free(there) || !town.Clear(at, there))
                continue;
            taken.Add(place.Key);
            return new RoundStop(place.S, place.D, place.FaceS, place.FaceD, place.Act, place.Approach is { } step ? [step] : []);
        }
        return Talk(ref rng);

        RoundStop? Talk(ref Pcg32 rng)
        {
            foreach (var other in Shuffled(atPosts, ref rng))
            {
                if (other.Id == p.Id || other.Role is "gatekeeper" or "guard" or "porter" || taken.Contains($"talk{other.Id}"))
                    continue;
                // In front of them, a step off, facing them.
                double s = other.S + other.FaceS * 1.2, d = other.D + other.FaceD * 1.2;
                var there = town.World(s, d, p.Up);
                if ((there - at).Length > 30 || !Room(there) || !town.Free(there) || !town.Clear(at, there))
                    continue;
                taken.Add($"talk{other.Id}");
                return new RoundStop(s, d, -other.FaceS, -other.FaceD, "talk", []);
            }
            return null;
        }
    }

    /// <summary><paramref name="items"/> in an order drawn from <paramref name="rng"/> (Fisher–Yates).</summary>
    static List<T> Shuffled<T>(IReadOnlyList<T> items, ref Pcg32 rng)
    {
        var list = items.ToList();
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = (int)(rng.NextDouble() * (i + 1));
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }

    /// <summary>The layout's place somebody at home was put at (the nearest to their feet).</summary>
    static int Nearest(TownHouse h, IReadOnlyList<HouseSpot> spots, Townsperson p)
    {
        int best = 0;
        double bestD = double.MaxValue;
        for (int i = 0; i < spots.Count; i++)
        {
            var (s, d) = h.Rail(spots[i].U, spots[i].V);
            double dist = (s - p.S) * (s - p.S) + (d - p.D) * (d - p.D);
            if (dist < bestD)
                (best, bestD) = (i, dist);
        }
        return best;
    }

    /// <summary>
    /// The way between two places in an open house (its frame, u along the front, v in from it), round the furniture and
    /// through the partition's doorway: the shortest over a grid of the places a body can stand, pulled straight wherever
    /// the way's clear. Empty when it's clear all the way (or there's no way at all: then straight, as before).
    /// </summary>
    static IReadOnlyList<(double S, double D)> Indoors(Town town, TownHouse h, HouseLayout l, (double U, double V) from, (double U, double V) to)
    {
        Double3 W((double U, double V) p)
        {
            var (s, d) = h.Rail(p.U, p.V);
            return town.World(s, d);
        }
        if (town.Clear(W(from), W(to), indoors: true))
            return [];
        const double step = 0.4;
        var points = new List<(double U, double V)> { from, to, (0, l.PassV) };
        double half = h.Width / 2 - HouseLayout.Wall;
        for (double u = -half + 0.25; u <= half - 0.25; u += step)
            for (double v = HouseLayout.Wall + 0.25; v <= h.Depth - HouseLayout.Wall - 0.25; v += step)
                if (town.Free(W((u, v)), indoors: true))
                    points.Add((u, v));
        int n = points.Count;
        var at = points.Select(W).ToArray();
        double Length(int i, int j) => Math.Sqrt((points[i].U - points[j].U) * (points[i].U - points[j].U) + (points[i].V - points[j].V) * (points[i].V - points[j].V));
        // Dijkstra from the start: a grid point's neighbours (and the ends' and the doorway's, a little further), where the way's clear.
        var dist = Enumerable.Repeat(double.MaxValue, n).ToArray();
        var prev = Enumerable.Repeat(-1, n).ToArray();
        var done = new bool[n];
        dist[0] = 0;
        while (true)
        {
            int u = -1;
            for (int i = 0; i < n; i++)
                if (!done[i] && dist[i] < double.MaxValue && (u < 0 || dist[i] < dist[u]))
                    u = i;
            if (u < 0 || u == 1)
                break;
            done[u] = true;
            for (int v = 0; v < n; v++)
            {
                if (done[v])
                    continue;
                double len = Length(u, v);
                double reach = u < 3 || v < 3 ? step * 2.5 : step * 1.5;
                if (len <= reach && dist[u] + len < dist[v] && town.Clear(at[u], at[v], indoors: true))
                {
                    dist[v] = dist[u] + len;
                    prev[v] = u;
                }
            }
        }
        if (prev[1] < 0)
            return [];
        var path = new List<int>();
        for (int i = 1; i >= 0; i = prev[i])
            path.Insert(0, i);
        // Pulled straight: from each point, on to the furthest one along that's in clear sight.
        var via = new List<(double S, double D)>();
        for (int i = 0; i < path.Count - 1;)
        {
            int j = path.Count - 1;
            while (j > i + 1 && !town.Clear(at[path[i]], at[path[j]], indoors: true))
                j--;
            if (j < path.Count - 1)
                via.Add(h.Rail(points[path[j]].U, points[path[j]].V));
            i = j;
        }
        return via;
    }
}
