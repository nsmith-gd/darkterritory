using Ballast;

namespace DarkTerritory.Sim.Towns;

/// <summary>What stands between a town's houses (GDD §3.1; ARCHITECTURE §8 note 989).</summary>
public enum BetweenKind : byte { FishShed, Traps, Granite, Fence }

/// <summary>
/// A thing between a town's houses (note 989; GDD §3.1's "fish sheds, fences, granite and lobster traps between the houses";
/// the director's references of 7 Oct: Peggy's Cove's houses on the granite above the fish sheds, Blue Rocks' shingled
/// shacks), in the rail frame: its middle (<see cref="S"/>, <see cref="D"/>), half its drawn footprint along the line and
/// across it, its height, and which way its front faces across the line (<see cref="Face"/>, ±1: a shed's door, a stack's
/// open side). A fence runs across the line, along the line between two lots. <see cref="Variant"/> picks its look: a
/// shed's build (0–2), a stack's traps (its columns and layers, a coil of rope beside it or a buoy on it), a rock's shape, a
/// fence's kind (0 picket, 1 post-and-rail, 2 post-and-rail gone over and broken). <see cref="Open"/>: a shed's door ajar;
/// <see cref="Hung"/>: floats and buoys hung on its wall.
/// </summary>
public sealed record TownPiece(BetweenKind Kind, double S, double D, double HalfS, double HalfD, double Height, int Face, int Variant,
    bool Open = false, bool Hung = false)
{
    /// <summary>
    /// What stops you, half along and across and its height: a rock's box a little inside its drawn lump (it's rounded, and
    /// sunk), the rest their footprint. A fence that's gone over leans out past its posts, so it's a little wider.
    /// </summary>
    public (double HalfS, double HalfD, double Height) Solid => Kind switch
    {
        BetweenKind.Granite => (HalfS * 0.8, HalfD * 0.8, Height * 0.85),
        _ => (HalfS, HalfD, Height),
    };
}

/// <summary>
/// The tuning of what's between the houses (towns.json <c>between</c>; note 989): how many pieces a house, at most, and
/// their mix by town type; how many gaps between neighbouring lots are fenced; and how far each keeps clear of what's
/// already there (the line, the streets and lanes, a house's front and door, the yard things, the walls, people).
/// </summary>
public sealed record BetweenTuning
{
    public bool Enabled { get; init; } = true;
    public double PerHouse { get; init; } = 0.5;
    public double PerHectare { get; init; } = 2;
    public int Most { get; init; } = 140;
    public double Near { get; init; } = 30;
    public Dictionary<string, double> Mix { get; init; } = new() { ["fishShed"] = 3, ["traps"] = 3, ["granite"] = 4 };
    /// <summary>A town type's own count and mix ("" the street town), over the defaults.</summary>
    public Dictionary<string, BetweenForm> ByForm { get; init; } = [];
    public double ShedTraps { get; init; } = 0.6;
    public double Rocks { get; init; } = 0.5;
    public double Open { get; init; } = 0.35;
    public double Hung { get; init; } = 0.5;
    public double Fences { get; init; } = 0.5;
    public double FenceGap { get; init; } = 0.4;
    public double[] FenceRun { get; init; } = [4, 12];
    public double Broken { get; init; } = 0.3;
    public double Step { get; init; } = 4.5;
    public double Line { get; init; } = 12;
    public double Street { get; init; } = 1.5;
    public double Front { get; init; } = 6;
    public double Things { get; init; } = 1.0;
    public double Places { get; init; } = 3;
    public double Wall { get; init; } = 4;
    public double People { get; init; } = 2;

    public double Weight(BetweenKind kind, string form)
    {
        var mix = ByForm.TryGetValue(form, out var f) && f.Mix is { } m ? m : Mix;
        return mix.TryGetValue(char.ToLowerInvariant(kind.ToString()[0]) + kind.ToString()[1..], out double w) ? w : 0;
    }

    public double PerHouseFor(string form) => ByForm.TryGetValue(form, out var f) && f.PerHouse is { } p ? p : PerHouse;
}

/// <summary>A town type's own count of pieces a house and their mix (towns.json <c>between.byForm</c>).</summary>
public sealed record BetweenForm(double? PerHouse = null, Dictionary<string, double>? Mix = null);

/// <summary>
/// Lays what stands between a town's houses (note 989), from its plan and its own stream: first a fence down the gap
/// between some neighbouring lots (the lot line, from the street side's front to their backs), then fish sheds, stacks of
/// traps and granite scattered over the ground that's free, a stack of traps often by a shed's door and a rock or two by a
/// rock. Free ground: inside the wall and back from it, off the line, its streets, lanes and ways, out of the square, the
/// green, the works and the rail yard, on level dry ground, clear of every house, its front and its yard's things, the
/// buildings, the fixtures and the places people go to, and of each other. Deterministic: the same plan and stream give the
/// same pieces on every machine.
/// </summary>
public static class TownBetween
{
    /// <summary>The pieces' sizes (half along, half across, height; m): what the art builds. Not design numbers.</summary>
    static readonly (double HalfS, double HalfD, double Height)[] Sheds = [(1.6, 1.3, 3.0), (1.25, 1.1, 2.6), (2.0, 1.5, 3.3)];

    /// <summary>A rectangle in the rail frame.</summary>
    readonly record struct Box(double S0, double S1, double D0, double D1)
    {
        public static Box Of(double s, double d, double hs, double hd) => new(s - hs, s + hs, d - hd, d + hd);
        public Box Pad(double p) => new(S0 - p, S1 + p, D0 - p, D1 + p);
        public bool Hits(Box o) => S0 < o.S1 && o.S0 < S1 && D0 < o.D1 && o.D0 < D1;
    }

    /// <summary>The obstacles, bucketed by 10 m along and across, so a check looks only at its neighbours.</summary>
    sealed class Taken
    {
        const double Cell = 10;
        readonly Dictionary<(int, int), List<Box>> _cells = [];

        public void Add(Box b)
        {
            for (int i = (int)Math.Floor(b.S0 / Cell); i <= (int)Math.Floor(b.S1 / Cell); i++)
                for (int j = (int)Math.Floor(b.D0 / Cell); j <= (int)Math.Floor(b.D1 / Cell); j++)
                {
                    if (!_cells.TryGetValue((i, j), out var list))
                        _cells[(i, j)] = list = [];
                    list.Add(b);
                }
        }

        public bool Hits(Box b)
        {
            for (int i = (int)Math.Floor(b.S0 / Cell); i <= (int)Math.Floor(b.S1 / Cell); i++)
                for (int j = (int)Math.Floor(b.D0 / Cell); j <= (int)Math.Floor(b.D1 / Cell); j++)
                    if (_cells.TryGetValue((i, j), out var list))
                        foreach (var o in list)
                            if (o.Hits(b))
                                return true;
            return false;
        }
    }

    public static IReadOnlyList<TownPiece> Lay(TownPlan plan, BetweenTuning t, Pcg32 rng)
    {
        var pieces = new List<TownPiece>();
        if (!t.Enabled || plan.Bounds is not { } bounds || plan.Houses.Count == 0)
            return pieces;
        var ground = TownGround.Of(plan);
        var taken = Obstacles(plan, t);

        bool Free(Box b, double pad)
        {
            var p = b.Pad(pad);
            return !taken.Hits(p) && Open(plan, bounds, ground, t, b);
        }

        // The fences first: down the lot line between neighbouring lots on a street, from their fronts back.
        var rows = plan.Houses.GroupBy(h => (h.Side, (int)Math.Round(h.FrontD / 3)))
            .OrderBy(g => g.Key.Side).ThenBy(g => g.Key.Item2);
        foreach (var row in rows)
        {
            var houses = row.OrderBy(h => h.S).ToList();
            for (int i = 0; i + 1 < houses.Count; i++)
            {
                var (a, b) = (houses[i], houses[i + 1]);
                double gap0 = a.S + Reach(a, +1), gap1 = b.S + Reach(b, -1);
                bool fence = rng.Chance(t.Fences);
                int variant = rng.Chance(t.Broken) ? 2 : rng.RangeInclusive(0, 1);
                // (To the half metre: the art builds a fence a length.)
                double run = Math.Round(rng.Range(t.FenceRun[0], t.FenceRun[1]) * 2) / 2;
                if (!fence || gap1 - gap0 < t.FenceGap)
                    continue;
                int side = a.Side;
                double s = (gap0 + gap1) / 2, from = Math.Max(Math.Abs(a.FrontD), Math.Abs(b.FrontD)) + 0.5;
                // As long as it can be, from the front back, to the run drawn: shortened till it's clear.
                for (double len = run; len >= t.FenceRun[0]; len -= 0.5)
                {
                    double d = side * (from + len / 2);
                    // (A fence gone over leans out past its posts.)
                    double half = variant == 2 ? 0.2 : 0.1;
                    var box = Box.Of(s, d, half, len / 2);
                    if (!Free(box, 0.05))
                        continue;
                    var piece = new TownPiece(BetweenKind.Fence, Round(s), Round(d), half, Round(len / 2), variant == 0 ? 1.0 : 1.15, -side, variant);
                    pieces.Add(piece);
                    taken.Add(box);
                    break;
                }
            }
        }

        // Then the rest over the free ground, in an order drawn from the stream: first round the houses (a shed or a stack of
        // traps is somebody's), each house's share of the mix; then the granite breaking through the open ground, a share a
        // hectare inside the wall.
        var spots = new List<(double S, double D)>();
        for (double s = bounds.Rear + t.Wall; s < bounds.Gate - t.Wall; s += t.Step)
            for (double d = -bounds.Left + t.Wall; d < bounds.Right - t.Wall; d += t.Step)
                if (Math.Abs(d) >= t.Line)
                    spots.Add((s, d));
        for (int i = spots.Count - 1; i > 0; i--)
        {
            int j = (int)(rng.NextDouble() * (i + 1));
            (spots[i], spots[j]) = (spots[j], spots[i]);
        }
        var homes = new Taken();
        foreach (var h in plan.Houses)
            homes.Add(Box.Of(h.S, h.D, h.Width / 2, h.Depth / 2));
        var kinds = new[] { BetweenKind.FishShed, BetweenKind.Traps, BetweenKind.Granite };
        double hectares = (bounds.Gate - bounds.Rear) * (bounds.Left + bounds.Right) / 10_000;
        int byHouses = (int)Math.Round(plan.Houses.Count * t.PerHouseFor(plan.Form)), inOpen = (int)Math.Round(hectares * t.PerHectare);
        double rockShare = t.Weight(BetweenKind.Granite, plan.Form) / Math.Max(1e-9, kinds.Sum(k => t.Weight(k, plan.Form)));
        Scatter(byHouses, kinds, near: true);
        Scatter((int)Math.Round(inOpen * rockShare), [BetweenKind.Granite], near: false);
        return pieces;

        void Scatter(int want, BetweenKind[] from, bool near)
        {
            // (The fences aren't counted: they're the lots'.)
            want = Math.Min(want, t.Most - pieces.Count(p => p.Kind != BetweenKind.Fence));
            double total = from.Sum(k => t.Weight(k, plan.Form));
            if (total <= 0 || want <= 0)
                return;
            int placed = 0;
            foreach (var (s0, d0) in spots)
            {
                if (placed >= want)
                    break;
                if (near && !homes.Hits(Box.Of(s0, d0, t.Near, t.Near)))
                    continue;
                double s = s0 + rng.Range(-1.5, 1.5), d = d0 + rng.Range(-1.5, 1.5), roll = rng.NextDouble() * total;
                var kind = from[^1];
                foreach (var k in from)
                    if ((roll -= t.Weight(k, plan.Form)) < 0)
                    {
                        kind = k;
                        break;
                    }
                int face = rng.Chance(0.5) ? 1 : -1;
                var piece = Draw(kind, s, d, face, t, ref rng);
                var box = Box.Of(piece.S, piece.D, piece.HalfS, piece.HalfD);
                if (!Free(box, t.Things))
                    continue;
                pieces.Add(piece);
                taken.Add(box);
                placed++;
                // A stack of traps by a shed's door, to one side of it; a rock or two beside a rock.
                if (kind == BetweenKind.FishShed && rng.Chance(t.ShedTraps))
                {
                    int by = rng.Chance(0.5) ? 1 : -1;
                    var traps = Draw(BetweenKind.Traps, 0, 0, face, t, ref rng);
                    double ts = piece.S + by * (piece.HalfS - traps.HalfS), td = piece.D + face * (piece.HalfD + 0.35 + traps.HalfD);
                    var tb = Box.Of(ts, td, traps.HalfS, traps.HalfD);
                    if (Free(tb, 0.2))
                    {
                        pieces.Add(traps with { S = Round(ts), D = Round(td) });
                        taken.Add(tb);
                    }
                }
                if (kind == BetweenKind.Granite && rng.Chance(t.Rocks))
                {
                    var rock = Draw(BetweenKind.Granite, 0, 0, face, t, ref rng, small: true);
                    double rs = piece.S + (rng.Chance(0.5) ? 1 : -1) * (piece.HalfS + rock.HalfS * 0.7), rd = piece.D + rng.Range(-0.5, 0.5) * piece.HalfD;
                    var rb = Box.Of(rs, rd, rock.HalfS, rock.HalfD);
                    // (It may touch its neighbour: rocks lie in heaps.)
                    if (!taken.Hits(Box.Of(rs + Math.Sign(rs - piece.S) * rock.HalfS * 0.3, rd, rock.HalfS * 0.7, rock.HalfD).Pad(0.2)) && Open(plan, bounds, ground, t, rb))
                    {
                        pieces.Add(rock with { S = Round(rs), D = Round(rd) });
                        taken.Add(rb);
                    }
                }
            }
        }
    }

    /// <summary>A piece of <paramref name="kind"/> at (s, d), drawn from the stream: its size and look.</summary>
    static TownPiece Draw(BetweenKind kind, double s, double d, int face, BetweenTuning t, ref Pcg32 rng, bool small = false)
    {
        switch (kind)
        {
            case BetweenKind.FishShed:
                {
                    int v = rng.RangeInclusive(0, Sheds.Length - 1);
                    var (hs, hd, h) = Sheds[v];
                    bool open = rng.Chance(t.Open), hung = rng.Chance(t.Hung);
                    return new(kind, Round(s), Round(d), hs, hd, h, face, v, open, hung);
                }
            case BetweenKind.Traps:
                {
                    // Columns of traps a metre long (along the line), stacked half a metre a layer; a coil of rope beside them
                    // (odd variants), which takes its room at one end.
                    int columns = rng.RangeInclusive(1, 2), layers = rng.RangeInclusive(1, 3), coil = rng.RangeInclusive(0, 1);
                    double hs = columns * 0.5 + coil * 0.35;
                    return new(kind, Round(s), Round(d), hs, 0.33, layers * 0.5, face, (columns - 1) * 6 + (layers - 1) * 2 + coil);
                }
            case BetweenKind.Granite:
                {
                    // A boulder, or (one in three) a low broad ledge of the bedrock breaking through.
                    bool ledge = !small && rng.Chance(1 / 3.0);
                    double hs = small ? rng.Range(0.45, 0.9) : ledge ? rng.Range(1.6, 3.0) : rng.Range(0.8, 1.7);
                    double hd = hs * rng.Range(0.6, 1.0), h = ledge ? rng.Range(0.5, 0.8) : hs * rng.Range(0.7, 1.0);
                    return new(kind, Round(s), Round(d), Round(hs), Round(hd), Round(h), face, rng.RangeInclusive(0, 5));
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    /// <summary>How far a house and its yard reach along the line its way (<paramref name="dir"/> ±1) from its middle.</summary>
    static double Reach(TownHouse h, int dir)
    {
        var (u0, u1, _, _) = h.Extent();
        double far = dir > 0 ? u1 : -u0;
        foreach (var y in h.Yard)
            far = Math.Max(far, dir > 0 ? Math.Max(y.U0, y.U1) : -Math.Min(y.U0, y.U1));
        return dir * far;
    }

    /// <summary>Everything already standing, and the ground kept clear round it.</summary>
    static Taken Obstacles(TownPlan plan, BetweenTuning t)
    {
        var taken = new Taken();
        Box Rail(TownHouse h, double u0, double u1, double v0, double v1)
        {
            var (sa, da) = h.Rail(u0, v0);
            var (sb, db) = h.Rail(u1, v1);
            return new(Math.Min(sa, sb), Math.Max(sa, sb), Math.Min(da, db), Math.Max(da, db));
        }
        foreach (var h in plan.Houses)
        {
            var (u0, u1, v0, v1) = h.Extent();
            taken.Add(Rail(h, u0, u1, v0, v1));
            // Its front, out to the street: its step, its path and its gate, and the door's approach.
            taken.Add(Rail(h, u0 - 0.5, u1 + 0.5, -t.Front, 0));
            foreach (var y in h.Yard)
                taken.Add(Rail(h, y.U0, y.U1, y.V0, y.V1));
        }
        foreach (var b in plan.Buildings)
            taken.Add(Box.Of(b.S, b.D, b.Length / 2, b.Depth / 2).Pad(t.Places));
        foreach (var f in plan.Fixtures.Where(f => f.House < 0))
        {
            bool turned = Math.Abs(f.FaceS) > Math.Abs(f.FaceD);
            double hs = Math.Max(0.5, turned ? f.SolidD : f.SolidS), hd = Math.Max(0.5, turned ? f.SolidS : f.SolidD);
            taken.Add(Box.Of(f.S, f.D, hs, hd).Pad(t.Places));
        }
        foreach (var p in plan.People.Where(p => p.House < 0))
            taken.Add(Box.Of(p.S, p.D, t.People, t.People));
        foreach (var (s0, d0, s1, _) in plan.Wards)
            taken.Add(new Box(Math.Min(s0, s1), Math.Max(s0, s1), d0 - Run.Fortresses.WardHalf, d0 + Run.Fortresses.WardHalf).Pad(1));
        return taken;
    }

    /// <summary>
    /// Whether a footprint is on open ground: inside the wall by its pad, off the line, its streets, lanes and ways, out of
    /// the square, the green, the works and the rail yard, out of the town type's decks, plots and stages, dry and level.
    /// </summary>
    static bool Open(TownPlan plan, TownBounds bounds, TownGround ground, BetweenTuning t, Box b)
    {
        if (b.D0 < t.Line && b.D1 > -t.Line)
            return false;
        var w = b.Pad(t.Wall);
        foreach (var (s, d) in new[] { (w.S0, w.D0), (w.S1, w.D0), (w.S0, w.D1), (w.S1, w.D1), ((w.S0 + w.S1) / 2, w.D0), ((w.S0 + w.S1) / 2, w.D1),
                     (w.S0, (w.D0 + w.D1) / 2), (w.S1, (w.D0 + w.D1) / 2) })
            if (!bounds.Holds(s, d))
                return false;
        var sq = plan.Square;
        if (Region(b, sq.S0, sq.S1, 0, Math.Abs(sq.WallD), sq.Side, t.Places))
            return false;
        if (plan.Green is { } g && Region(b, g.S0, g.S1, g.Near, g.Far, g.Side, t.Places)
            || plan.Works is { } wk && Region(b, wk.S0, wk.S1, wk.Near, wk.Far, wk.Side, t.Places)
            || plan.Yard is { } yd && Region(b, yd.S0, yd.S1, 0, yd.Far, yd.Side, t.Places))
            return false;
        var p = b.Pad(t.Street);
        foreach (var st in bounds.Streets)
        {
            if (p.S1 < st.S0 || p.S0 > st.S1)
                continue;
            foreach (double s in new[] { p.S0, (p.S0 + p.S1) / 2, p.S1 })
            {
                double c = st.At(s), half = st.Width / 2;
                if (p.D0 < c + half && p.D1 > c - half)
                    return false;
            }
        }
        foreach (var lane in bounds.Lanes)
        {
            double lo = Math.Min(lane.D0, lane.D1), hi = Math.Max(lane.D0, lane.D1);
            if (p.D1 < lo || p.D0 > hi)
                continue;
            var (ls, hs) = lane.Span(Math.Max(lo, p.D0), Math.Min(hi, p.D1));
            if (p.S0 < hs && p.S1 > ls)
                return false;
        }
        foreach (var way in plan.Ways)
            for (int i = 0; i + 1 < way.Points.Count; i++)
                if (Near(p, way.Points[i], way.Points[i + 1], way.Width / 2))
                    return false;
        // Out of a town type's growing plots and its working yards (a slip, the logs); its water and decks are the ground's.
        foreach (var district in plan.Districts)
            if (district.Kind is DistrictKind.Plots or DistrictKind.Yard && !district.Outside && Corners(b).Any(c => district.Holds(c.S, c.D)))
                return false;
        // Dry, off a deck, and level under it all (not over a terrace's edge or down a bank).
        double level = ground.Level((b.S0 + b.S1) / 2, (b.D0 + b.D1) / 2);
        foreach (var (s, d) in Corners(b).Append(((b.S0 + b.S1) / 2, (b.D0 + b.D1) / 2)))
            if (ground.Water(s, d) is not null || ground.Deck(s, d) || Math.Abs(ground.Level(s, d) - level) > 0.05)
                return false;
        return true;
    }

    static (double S, double D)[] Corners(Box b) => [(b.S0, b.D0), (b.S1, b.D0), (b.S0, b.D1), (b.S1, b.D1)];

    /// <summary>Whether a footprint reaches into a region along the line from s0 to s1, out from near to far on a side.</summary>
    static bool Region(Box b, double s0, double s1, double near, double far, int side, double pad)
    {
        double d0 = side > 0 ? near : -far, d1 = side > 0 ? far : -near;
        return b.Hits(new Box(s0, s1, d0, d1).Pad(pad));
    }

    /// <summary>Whether a footprint comes within <paramref name="half"/> of a way's stretch from a to b (by its corners and middle).</summary>
    static bool Near(Box p, (double S, double D) a, (double S, double D) b, double half)
    {
        double es = b.S - a.S, ed = b.D - a.D, len2 = es * es + ed * ed;
        foreach (var (s, d) in Corners(p).Append(((p.S0 + p.S1) / 2, (p.D0 + p.D1) / 2)))
        {
            double k = len2 < 1e-9 ? 0 : Math.Clamp(((s - a.S) * es + (d - a.D) * ed) / len2, 0, 1);
            double x = a.S + es * k - s, y = a.D + ed * k - d;
            if (x * x + y * y < half * half)
                return true;
        }
        // The stretch's own points inside the footprint (a narrow way through a broad one).
        return a.S > p.S0 && a.S < p.S1 && a.D > p.D0 && a.D < p.D1 || b.S > p.S0 && b.S < p.S1 && b.D > p.D0 && b.D < p.D1;
    }

    static double Round(double x) => Math.Round(x, 3);
}
