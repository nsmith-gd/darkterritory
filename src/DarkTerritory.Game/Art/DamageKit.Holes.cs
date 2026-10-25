using System.Numerics;
using Ballast.Render;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Art;

/// <summary>
/// A failing car's holes, cut through it (note 1014; the director's Look Review verdict of 23 Oct on `cap-derail`: "This
/// feels like just textures, I think failing cabs should have holes and bits of board missing and progressively getting
/// worse to show the failure"). Not drawn over the body as the scars and <see cref="Breach"/> are: the body's own walls and
/// roof are built without them (<see cref="TrainKit.Car"/>, <see cref="TrainKit.Engine"/>), so it's open there, the
/// broken ends of its boards pale at the edges and the car's dark inside, its load and its far wall, showing through.
/// </summary>
public static partial class DamageKit
{
    /// <summary>
    /// A board's length gone from a side wall (<paramref name="Side"/> −1 left, +1 right): along the car (z) and up (y); which
    /// of a car's hole sites it's of, and its row there (0 the site's middle, the first to go).
    /// </summary>
    public readonly record struct Gap(int Side, float Z0, float Z1, float Y0, float Y1, int Site = 0, int Row = 0)
    {
        public float Area => (Z1 - Z0) * (Y1 - Y0);
        public bool Holds(float z, float y) => z > Z0 && z < Z1 && y > Y0 && y < Y1;
    }

    /// <summary>A roof sheet gone off one side's eave (<paramref name="Side"/>), from the eave in to |x| = <paramref name="Inner"/>, between Z0 and Z1.</summary>
    public readonly record struct RoofGap(int Side, float Z0, float Z1, float Inner);

    /// <summary>
    /// What's gone from a car at a <see cref="HoleLevel"/>: its walls' missing boards, its roof's missing sheets, and the hole
    /// the peeled sheet came off (<see cref="Failing"/>'s, breaking and on).
    /// </summary>
    public sealed record Holes(IReadOnlyList<Gap> Walls, IReadOnlyList<RoofGap> Roof, Gap? Peel)
    {
        public static readonly Holes None = new([], [], null);
        public bool Any => Walls.Count > 0 || Roof.Count > 0;
    }

    /// <summary>How far gone a car's holes are: none holding, then one a stage (failing 1, breaking 2, come apart 3).</summary>
    public static int HoleLevel(FailStage stage) => stage switch
    {
        FailStage.Failing => 1,
        FailStage.Breaking => 2,
        FailStage.Broken => 3,
        _ => 0,
    };

    /// <summary>The worst there is: a car come apart on the line, or a wreck's heap.</summary>
    public const int MostHoles = 3;

    /// <summary>A board's height, the step a hole grows in up and down the wall (m).</summary>
    public const float Board = 0.22f;

    /// <summary>When each wall site opens (the level), and each roof site: four a stage on the walls, two a side; two a stage on the roof from breaking, one a side.</summary>
    static readonly int[] WallOpens = [1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3], RoofOpens = [2, 2, 3, 3];

    /// <summary>How far a site that opened at <paramref name="opens"/> has grown by <paramref name="level"/> (0 not yet, to 1).</summary>
    static float Grown(int level, int opens) => level < opens ? 0 : MathF.Min(1, 0.55f + 0.25f * (level - opens));

    /// <summary>
    /// A walk-in car's holes at <paramref name="level"/> (<see cref="HoleLevel"/>), <paramref name="seed"/> (the car) choosing
    /// where, the same every time. Every site is chosen whatever the level, so a hole never moves as the car gets worse, and
    /// each level's holes hold the last's: a site's boards go from its middle row out, each row's gap longer at either end.
    /// Four sites open failing (two a side), four more breaking, four more come apart; some a ragged hole a few boards high,
    /// some a board or two gone along a panel; roof sheets go breaking and on. They keep off
    /// the side doors (whose leaves slide over the wall), the ends (the gangways), the hatch and the roof walk.
    /// </summary>
    public static Holes CarHoles(CarShape shape, int level, int seed)
    {
        if (shape.Interior is not { } room || shape.Cab is not null)
            return Holes.None;
        float w = (float)shape.HalfWidth, l = (float)shape.HalfLength;
        float floor = (float)room.Min.Y + 0.1f, ceiling = (float)room.Max.Y;
        var doors = SideDoors(shape);
        var rng = new Random(7300 + seed * 29);
        var walls = new List<Gap>();
        Gap? peel = null;
        // Each stage's sites as many a side, so it's seen from either side of the line.
        int first = rng.Next(2) * 2 - 1;
        for (int i = 0; i < WallOpens.Length; i++)
        {
            // Chosen whatever the level: the same draws every time, so the sites don't move.
            int side = i % 2 == 0 ? first : -first;
            float zc = 0;
            for (int tries = 0; tries < 24; tries++)
            {
                zc = Lerp(-l + 1.6f, l - 1.6f, (float)rng.NextDouble());
                if (!doors.Any(d => d.Side == side && zc > d.Z0 - 0.9f && zc < d.Z1 + 0.9f))
                    break;
            }
            float yc = Lerp(floor + 0.6f, ceiling - 0.5f, (float)rng.NextDouble());
            // A ragged hole a few boards high, or a board or two gone along much of a panel.
            bool planks = rng.NextDouble() < 0.4;
            int rows = planks ? 1 + rng.Next(2) : 4 + rng.Next(3);
            int lead = rng.Next(2) * 2 - 1;
            var ends = new (float Fore, float Aft)[rows];
            for (int j = 0; j < rows; j++)
                ends[j] = planks ? (0.7f + 0.5f * (float)rng.NextDouble(), 0.7f + 0.5f * (float)rng.NextDouble())
                    : (0.35f + 0.65f * (float)rng.NextDouble(), 0.35f + 0.65f * (float)rng.NextDouble());
            float g = Grown(level, WallOpens[i]);
            if (g <= 0)
                continue;
            // Its rows from the middle out (0, +1, −1, +2, …, the first step the way the site leads), as many as it's grown to.
            int shown = Math.Max(1, (int)MathF.Round(g * rows));
            float reach = 0.35f + 0.65f * g;
            float lo = float.MaxValue, hi = float.MinValue, bottom = float.MaxValue, top = float.MinValue;
            int start = walls.Count;
            for (int j = 0; j < shown; j++)
            {
                int step = (j + 1) / 2 * (j % 2 == 1 ? lead : -lead);
                float y0 = MathF.Max(floor + 0.12f, yc + (step - 0.5f) * Board), y1 = MathF.Min(ceiling - 0.12f, yc + (step + 0.5f) * Board);
                if (y1 - y0 < 0.05f)
                    continue;
                float z0 = zc - ends[j].Aft * reach * 1.6f, z1 = zc + ends[j].Fore * reach * 1.6f;
                // Off the ends and the doors: cut short where it'd run onto one.
                z0 = MathF.Max(z0, -l + 0.8f);
                z1 = MathF.Min(z1, l - 0.8f);
                foreach (var d in doors.Where(d => d.Side == side))
                {
                    if (zc <= d.Z0 && z1 > d.Z0 - 0.3f)
                        z1 = d.Z0 - 0.3f;
                    if (zc >= d.Z1 && z0 < d.Z1 + 0.3f)
                        z0 = d.Z1 + 0.3f;
                }
                if (z1 - z0 < 0.1f)
                    continue;
                walls.Add(new Gap(side, z0, z1, y0, y1, i, j));
                (lo, hi, bottom, top) = (MathF.Min(lo, z0), MathF.Max(hi, z1), MathF.Min(bottom, y0), MathF.Max(top, y1));
            }
            // The first of breaking's sites is where the great sheet peeled off (Failing's Peeled), its middle row's span.
            if (WallOpens[i] == 2 && peel is null && walls.Count > start)
                peel = new Gap(side, lo, hi, bottom, top, i);
        }

        var roof = new List<RoofGap>();
        float hz0 = shape.Hatch is { } hb ? (float)hb.Min.Z - 0.3f : l + 1, hz1 = shape.Hatch is { } hb1 ? (float)hb1.Max.Z + 0.3f : l + 1;
        int roofFirst = rng.Next(2) * 2 - 1;
        for (int i = 0; i < RoofOpens.Length; i++)
        {
            int side = i % 2 == 0 ? roofFirst : -roofFirst;
            const float Sheet = 0.55f;
            int sheets = 3;
            float z = 0;
            for (int tries = 0; tries < 24; tries++)
            {
                z = Lerp(-l + 1.2f, l - 1.2f - sheets * Sheet, (float)rng.NextDouble());
                if (z + sheets * Sheet < hz0 || z > hz1)
                    break;
            }
            var inner = new float[sheets];
            for (int j = 0; j < sheets; j++)
                inner[j] = 0.6f + 0.4f * (float)rng.NextDouble();
            float g = Grown(level, RoofOpens[i]);
            if (g <= 0 || (z + sheets * Sheet >= hz0 && z <= hz1))
                continue;
            // From the middle sheet out, each going further in toward the walk as it grows; never onto the walk (0.45 m).
            int shown = Math.Max(1, (int)MathF.Round(g * sheets));
            foreach (int j in (ReadOnlySpan<int>)[1, 0, 2])
            {
                if (shown-- <= 0)
                    break;
                float into = w - (w - 0.45f) * inner[j] * (0.6f + 0.4f * g);
                roof.Add(new RoofGap(side, z + j * Sheet, z + (j + 1) * Sheet, MathF.Max(0.45f, into)));
            }
        }
        return new Holes(walls, roof, peel);
    }

    /// <summary>
    /// The boiler's burst through the armoured hood (note 1014: the rupture's tear was a black disc on the plate): the hood's
    /// plate is gone round <see cref="TrainKit.RuptureSeam"/>, an oval of rows (<paramref name="size"/> the tear's,
    /// <see cref="TrainKit.RuptureSize"/>), so the corridor's dark and the boiler show through it under the tear's petals.
    /// None on an engine without a hood (the tear's on its boiler, and stays drawn black).
    /// </summary>
    public static IReadOnlyList<Gap> HoodHole(CarShape shape)
    {
        if (shape.Cab is null || !shape.Solids.Any(s => s.Part == PartKind.Boiler))
            return [];
        var seam = TrainKit.RuptureSeam(shape);
        if (TrainKit.HoodFace(shape, -1, seam.Z) is null)
            return [];
        float size = TrainKit.RuptureSize(shape);
        float halfZ = 0.42f * size, halfY = 0.24f * size;
        var gaps = new List<Gap>();
        const int Rows = 5;
        for (int j = 0; j < Rows; j++)
        {
            float y0 = seam.Y - halfY + j * 2 * halfY / Rows, y1 = y0 + 2 * halfY / Rows;
            float my = ((y0 + y1) / 2 - seam.Y) / halfY;
            // An oval, each row ragged at either end (the tear's own petals cover the rim).
            float across = halfZ * MathF.Sqrt(MathF.Max(0.15f, 1 - my * my));
            float ragA = 0.85f + 0.2f * Frac(MathF.Sin(j * 17.3f) * 431.1f), ragB = 0.85f + 0.2f * Frac(MathF.Sin(j * 29.9f) * 217.3f);
            gaps.Add(new Gap(-1, seam.Z - across * ragA, seam.Z + across * ragB, y0, y1));
        }
        return gaps;
    }

    static float Frac(float x) => x - MathF.Floor(x);

    /// <summary>
    /// A wall box (<paramref name="min"/>, <paramref name="max"/>, its outer face on <paramref name="side"/>) less the gaps on
    /// that side, cut right through: the pieces of it that are left, in bands up the wall, each band's runs between its gaps.
    /// </summary>
    public static List<(Vector3 Min, Vector3 Max)> Pieces(Vector3 min, Vector3 max, IReadOnlyList<Gap> gaps, int side)
    {
        var rects = Subtract((min.Z, max.Z, min.Y, max.Y), gaps.Where(g => g.Side == side).Select(g => (g.Z0, g.Z1, g.Y0, g.Y1)));
        return rects.Select(r => (new Vector3(min.X, r.B0, r.A0), new Vector3(max.X, r.B1, r.A1))).ToList();
    }

    /// <summary>
    /// A rectangle (a along, b across) less <paramref name="holes"/>: what's left, as rectangles. Banded on b at every hole's
    /// edge, and in each band the runs of a between the holes that cross it.
    /// </summary>
    public static List<(float A0, float A1, float B0, float B1)> Subtract((float A0, float A1, float B0, float B1) whole,
        IEnumerable<(float A0, float A1, float B0, float B1)> holes)
    {
        var (a0, a1, b0, b1) = whole;
        var hs = holes.Where(h => h.A1 > a0 && h.A0 < a1 && h.B1 > b0 && h.B0 < b1).ToList();
        if (hs.Count == 0)
            return [whole];
        var cuts = hs.SelectMany(h => new[] { h.B0, h.B1 }).Where(b => b > b0 && b < b1).Append(b0).Append(b1).Distinct().Order().ToList();
        var left = new List<(float, float, float, float)>();
        for (int i = 0; i + 1 < cuts.Count; i++)
        {
            float lo = cuts[i], hi = cuts[i + 1];
            if (hi - lo < 1e-4f)
                continue;
            float mid = (lo + hi) / 2;
            float at = a0;
            foreach (var h in hs.Where(h => h.B0 < mid && h.B1 > mid).OrderBy(h => h.A0))
            {
                if (h.A0 > at + 1e-4f)
                    left.Add((at, MathF.Min(h.A0, a1), lo, hi));
                at = MathF.Max(at, h.A1);
            }
            if (a1 > at + 1e-4f)
                left.Add((at, a1, lo, hi));
        }
        return left;
    }

    /// <summary>The faces of a piece that were cut (inside the whole it's from): where a hole's edge is.</summary>
    public static Kit.Faces CutFaces(Vector3 pmin, Vector3 pmax, Vector3 min, Vector3 max)
    {
        Kit.Faces cut = 0;
        if (pmin.Y > min.Y + 1e-4f) cut |= Kit.Faces.NegY;
        if (pmax.Y < max.Y - 1e-4f) cut |= Kit.Faces.PosY;
        if (pmin.Z > min.Z + 1e-4f) cut |= Kit.Faces.NegZ;
        if (pmax.Z < max.Z - 1e-4f) cut |= Kit.Faces.PosZ;
        return cut;
    }

    /// <summary>
    /// Where a board's broken off: its fresh end pale, split wood (on plate, scraped bright), what catches a lamp at the edge
    /// of a hole. For what <see cref="CutFaces"/> found.
    /// </summary>
    public static void Raw(Kit k, bool iron)
    {
        if (iron)
            k.Use("iron_plate", Palette.IronGrey, 0.3f, 0.5f).Shade(1.5f);
        else
            k.Use("wood_grey", Palette.BoardEnamel, 0.8f, 0, 1f).Shade(1.6f);
    }

    /// <summary>
    /// The broken ends of the boards round a wall's gaps, what makes a hole read as boards gone and not a dark patch: at each
    /// gap's ends where the wall goes on, the board's end snapped off ragged, a long splinter and teeth sticking out into the
    /// hole; and along its top and bottom, where the next board's still there, that board's split edge, pale and uneven.
    /// In the wall's outer plane (<paramref name="w"/>), two-sided.
    /// </summary>
    public static void Splinters(Kit k, IReadOnlyList<Gap> gaps, float w, float thick, bool iron)
    {
        Raw(k, iron);
        bool Open(int side, float z, float y) => gaps.Any(o => o.Side == side && o.Holds(z, y));
        foreach (var g in gaps)
        {
            float x = g.Side * (w - thick * 0.3f), face = g.Side * (w + 0.004f);
            float ym = (g.Y0 + g.Y1) / 2;
            foreach (int end in (ReadOnlySpan<int>)[-1, 1])
            {
                float z = end < 0 ? g.Z0 : g.Z1;
                // Only where the board goes on past it: not into another gap.
                if (Open(g.Side, z + end * 0.04f, ym))
                    continue;
                float h = Hash(z, g.Y0), h2 = Hash(g.Y1, z);
                float len = 0.2f + 0.3f * h, tipY = Lerp(g.Y0 + 0.04f, g.Y1 - 0.04f, h2);
                var a = new Vector3(x, g.Y0, z - end * 0.002f);
                var b = new Vector3(x, g.Y1, z - end * 0.002f);
                var tip = new Vector3(x, tipY, z - end * len);
                // A tooth either side of the long splinter, shorter.
                var low = new Vector3(x, Lerp(g.Y0, tipY, 0.35f), z - end * len * 0.5f);
                var high = new Vector3(x, Lerp(tipY, g.Y1, 0.65f), z - end * len * 0.35f);
                Both(k, a, low, tip);
                Both(k, tip, high, b);
                Both(k, a, tip, b);
            }
            // The split edges above and below, where the boards there are whole: a ragged pale strip just proud of the face.
            foreach (int edge in (ReadOnlySpan<int>)[-1, 1])
            {
                float y = edge < 0 ? g.Y0 : g.Y1;
                const int Bits = 4;
                for (int i = 0; i < Bits; i++)
                {
                    float z0 = Lerp(g.Z0, g.Z1, i / (float)Bits), z1 = Lerp(g.Z0, g.Z1, (i + 1) / (float)Bits);
                    if (Open(g.Side, (z0 + z1) / 2, y + edge * 0.04f))
                        continue;
                    float d0 = 0.02f + 0.04f * Hash(z0, y), d1 = 0.02f + 0.04f * Hash(z1, y);
                    var p0 = new Vector3(face, y, z0);
                    var p1 = new Vector3(face, y, z1);
                    Both(k, p0, p1, p1 + new Vector3(0, edge * d1, 0));
                    Both(k, p0, p1 + new Vector3(0, edge * d1, 0), p0 + new Vector3(0, edge * d0, 0));
                }
            }
        }
    }

    /// <summary>
    /// Round a roof's gaps, the sheets' torn edges scraped bright where they were wrenched off their nails, lying on the
    /// arched roof (<paramref name="h"/> its crown) along each gap's inner edge and across its ends where the roof goes on,
    /// ragged, and a tab of each edge bent up: what shows the roof's open from above at night, the car's dark under it.
    /// </summary>
    public static void RoofEdges(Kit k, IReadOnlyList<RoofGap> gaps, float w, float h)
    {
        Raw(k, iron: true);
        float Arch(float x) { float s = x / (w + 0.02f); return h - 0.035f * s * s - 0.005f + 0.012f; }
        bool Open(int side, float x, float z) => gaps.Any(o => o.Side == side && z > o.Z0 && z < o.Z1 && MathF.Abs(x) > o.Inner);
        foreach (var g in gaps)
        {
            // Along the inner edge, the bright strip on the sheet that's left, wider in places.
            const int Bits = 3;
            for (int i = 0; i < Bits; i++)
            {
                float z0 = Lerp(g.Z0, g.Z1, i / (float)Bits), z1 = Lerp(g.Z0, g.Z1, (i + 1) / (float)Bits);
                float x = g.Side * g.Inner, d0 = 0.04f + 0.06f * Hash(z0, x), d1 = 0.04f + 0.06f * Hash(z1, x);
                if (Open(g.Side, x - g.Side * 0.05f, (z0 + z1) / 2))
                    continue;
                var a = new Vector3(x, Arch(x), z0);
                var b = new Vector3(x, Arch(x), z1);
                Both(k, a, b, new Vector3(x - g.Side * d1, Arch(x - g.Side * d1), z1));
                Both(k, a, new Vector3(x - g.Side * d1, Arch(x - g.Side * d1), z1), new Vector3(x - g.Side * d0, Arch(x - g.Side * d0), z0));
            }
            // Across each end, out to the eave, where the next sheet's still on.
            foreach (int end in (ReadOnlySpan<int>)[-1, 1])
            {
                float z = end < 0 ? g.Z0 : g.Z1;
                float mid = g.Side * (g.Inner + w) / 2;
                if (Open(g.Side, mid, z + end * 0.05f))
                    continue;
                float x0 = g.Side * g.Inner, x1 = g.Side * (w + 0.06f);
                float d = 0.05f + 0.05f * Hash(z, g.Inner);
                Both(k, new Vector3(x0, Arch(x0), z), new Vector3(x1, Arch(x1), z), new Vector3(x1, Arch(x1), z + end * d));
                Both(k, new Vector3(x0, Arch(x0), z), new Vector3(x1, Arch(x1), z + end * d), new Vector3(x0, Arch(x0), z + end * d * 0.6f));
                // A tab of the torn edge bent up off the roof, catching the light.
                float tx = Lerp(x0, x1, 0.3f + 0.4f * Hash(g.Inner, z)), tw = 0.18f;
                var foot = new Vector3(tx, Arch(tx), z);
                Both(k, foot, foot + new Vector3(g.Side * tw, 0.01f, 0), foot + new Vector3(g.Side * tw * 0.5f, 0.14f, -end * 0.06f));
            }
        }
    }

    /// <summary>A splinter's triangle, seen from either side.</summary>
    static void Both(Kit k, Vector3 a, Vector3 b, Vector3 c)
    {
        k.Tri(a, b, c, new(0, 0), new(0.2f, 0), new(0.1f, 0.2f));
        k.Tri(c, b, a, new(0.1f, 0.2f), new(0.2f, 0), new(0, 0));
    }

    /// <summary>A number in 0..1 from two (where a splinter's tip goes): the same for the same place every time.</summary>
    static float Hash(float a, float b) => Frac(MathF.Sin(a * 12.9898f + b * 78.233f) * 43758.55f);

    /// <summary>
    /// The roof's arched section (TrainKit.Car's) between |x| limits: <paramref name="xl"/> to <paramref name="xr"/>, the eave
    /// kept on a side that reaches it (null limit), cut off square at a limit where its sheets are gone, counter-clockwise.
    /// </summary>
    public static List<Vector2> RoofProfile(float w, float h, float ceiling, float? xl, float? xr)
    {
        const int N = 6;
        float Arch(float x) { float s = -x / (w + 0.02f); return h - 0.035f * s * s - 0.005f; }
        var profile = new List<Vector2>();
        if (xr is { } r)
            profile.AddRange([new(r, ceiling - 0.02f), new(r, Arch(r))]);
        else
            profile.AddRange([new(w + 0.07f, ceiling - 0.02f), new(w + 0.07f, ceiling + 0.04f)]);
        for (int i = 0; i <= N; i++)
        {
            float s = (float)i / N * 2 - 1, x = -s * (w + 0.02f);
            if ((xr is null || x < xr - 0.01f) && (xl is null || x > xl + 0.01f))
                profile.Add(new Vector2(x, h - 0.035f * s * s - 0.005f));
        }
        if (xl is { } lft)
            profile.AddRange([new(lft, Arch(lft)), new(lft, ceiling - 0.02f)]);
        else
            profile.AddRange([new(-w - 0.07f, ceiling + 0.04f), new(-w - 0.07f, ceiling - 0.02f)]);
        return profile;
    }

    /// <summary>
    /// The roof along a span, split where sheets are gone: each stretch's z range and its section's limits (null: the
    /// eave), from <see cref="RoofProfile"/>.
    /// </summary>
    public static IEnumerable<(float Z0, float Z1, float? Xl, float? Xr)> RoofSpans(float z0, float z1, IReadOnlyList<RoofGap> gaps)
    {
        var cuts = gaps.SelectMany(g => new[] { g.Z0, g.Z1 }).Where(z => z > z0 && z < z1).Append(z0).Append(z1).Distinct().Order().ToList();
        for (int i = 0; i + 1 < cuts.Count; i++)
        {
            float mid = (cuts[i] + cuts[i + 1]) / 2;
            float? xl = null, xr = null;
            foreach (var g in gaps.Where(g => g.Z0 < mid && g.Z1 > mid))
                if (g.Side > 0)
                    xr = MathF.Min(xr ?? float.MaxValue, g.Inner);
                else
                    xl = MathF.Max(xl ?? float.MinValue, -g.Inner);
            yield return (cuts[i], cuts[i + 1], xl, xr);
        }
    }

    /// <summary>
    /// How much of a car's sides and roof is open at a level (m²): its gaps' union, sampled on a 5 cm grid (what the tests
    /// hold to grow with every stage).
    /// </summary>
    public static float OpenArea(CarShape shape, Holes holes)
    {
        const float Step = 0.05f;
        float l = (float)shape.HalfLength, h = (float)shape.RoofHeight, w = (float)shape.HalfWidth;
        int open = 0;
        foreach (int side in (ReadOnlySpan<int>)[-1, 1])
            for (float z = -l; z < l; z += Step)
                for (float y = 0; y < h; y += Step)
                    if (holes.Walls.Any(g => g.Side == side && g.Holds(z + Step / 2, y + Step / 2)))
                        open++;
        foreach (int side in (ReadOnlySpan<int>)[-1, 1])
            for (float z = -l; z < l; z += Step)
                for (float x = 0; x < w; x += Step)
                    if (holes.Roof.Any(g => g.Side == side && z + Step / 2 > g.Z0 && z + Step / 2 < g.Z1 && x + Step / 2 > g.Inner))
                        open++;
        return open * Step * Step;
    }
}
