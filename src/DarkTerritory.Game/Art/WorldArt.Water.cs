using System.Numerics;
using System.Runtime.CompilerServices;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Rail;

namespace DarkTerritory.Game.Art;

/// <summary>
/// A generated line's water (maritime-rules.md §2-5, linegen plan §12.4; ARCHITECTURE §8 note 424): the rivers under their
/// spans and alongside the line, the marshes' standing water, the lakes, the Atlantic, Fundy's red tide and the tar ponds'
/// pitch. The water is its own surface (<see cref="WaterSheet"/>): it stays put under the camera, runs with its current,
/// ripples and swells in the night's wind, and laps at its waterline, all in the shader (scene.frag's water).
/// </summary>
public sealed partial class WorldArt
{
    /// <summary>
    /// The water's surface as the shader reads it (scene.frag's <c>waterNormal</c>; note 424): each vertex's texture
    /// coordinates are where it lies in the world, in metres (wrapped as the grime's are, so it stays put under the camera,
    /// and seamless where the wrap moves on); its surface coordinates its current (x, z, m/s) and how open it lies to the
    /// wind; its blend how near the land (1 at the waterline: the lap). Face up, one-sided: it's never seen from under.
    /// </summary>
    sealed class WaterSheet(MeshBuilder mesh, Double3 eye, int layer)
    {
        readonly Vector2 _origin = new(W(eye.X), W(eye.Z));
        public Vector3 Tint { get; set; } = Vector3.One;
        public Vector2 Current { get; set; }
        public float Open { get; set; }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float la, float lb, float lc, float ld)
        {
            Tri(a, b, c, la, lb, lc);
            Tri(a, c, d, la, lc, ld);
        }

        public void Tri(Vector3 a, Vector3 b, Vector3 c, float la, float lb, float lc)
        {
            float up = Vector3.Cross(b - a, c - a).Y;
            if (MathF.Abs(up) < 1e-6f)
                return;
            if (up < 0)
                (b, c, lb, lc) = (c, b, lc, lb);
            V(a, la);
            V(b, lb);
            V(c, lc);
        }

        void V(Vector3 p, float lap) => mesh.Add(new Vertex(p, Vector3.UnitY, Tint)
        {
            Surface = new Vector3(Current.X, Current.Y, Open),
            Shine = 0.9f,
            Uv = new Vector2(_origin.X + p.X, _origin.Y + p.Z),
            Layer = layer,
            Blend = lap,
        });
    }

    /// <summary>How far past a lake's shore its water runs on under the land (m), so the shore's ground always hides its edge.</summary>
    const double LakeSkirtM = 2;

    /// <summary>A lake's water drawn to its own shoreline: rings round it, in the world's x and z.</summary>
    sealed record LakeRings(Vector2[] Deep, Vector2[] Waterline, Vector2[] Skirt);

    readonly ConditionalWeakTable<PlanLake, LakeRings> _lakeRings = new();

    /// <summary>
    /// Where a lake's water lies (TerrainField.Waterside, LakeMetric): its waterline, where the basin's bed comes up through
    /// the water a few metres inside its shore; a ring <paramref name="lap"/> further in, where the lap's foam has died
    /// away; and a ring <see cref="LakeSkirtM"/> out past the shore, under its rim. Each is the first point out along its
    /// ray at its metric, so a wobbled shore is followed and the rings never cross.
    /// </summary>
    static LakeRings Rings(PlanLake lake, double lap)
    {
        const int n = 64;
        double r = lake.RadiusM;
        // The bed (Waterside) climbs from the depth to 0.35 m over the water across the last 14 m in to the shore, by a
        // smoothstep: it meets the water where that's 0.35 / (depth + 0.35) of the way up.
        double f = 0.35 / (lake.DepthM + 0.35);
        double inside = 14 * (0.5 - Math.Sin(Math.Asin(1 - 2 * f) / 3));
        double waterline = 1 - inside / r, deep = Math.Max(0.2, 1 - (inside + lap) / r), skirt = 1 + LakeSkirtM / r;
        var rings = new LakeRings(new Vector2[n], new Vector2[n], new Vector2[n]);
        for (int i = 0; i < n; i++)
        {
            double a = i * Math.Tau / n, cx = Math.Cos(a), cz = Math.Sin(a);
            Vector2 At(double m)
            {
                double d = Reach(lake, cx, cz, m);
                return new Vector2((float)(lake.X + cx * d), (float)(lake.Z + cz * d));
            }
            rings.Deep[i] = At(deep);
            rings.Waterline[i] = At(waterline);
            rings.Skirt[i] = At(skirt);
        }
        return rings;
    }

    /// <summary>How far out from a lake's centre along (cx, cz) its metric first reaches <paramref name="m"/>: marched out, then halved onto it.</summary>
    static double Reach(PlanLake lake, double cx, double cz, double m)
    {
        double max = lake.RadiusM * lake.Stretch * (1 + lake.Wobble) * 1.5 + 50, step = Math.Max(0.5, lake.RadiusM / 40);
        for (double d = step; d < max; d += step)
        {
            if (TerrainField.LakeMetric(lake, lake.X + cx * d, lake.Z + cz * d) < m)
                continue;
            double lo = d - step, hi = d;
            for (int k = 0; k < 14; k++)
            {
                double mid = (lo + hi) / 2;
                if (TerrainField.LakeMetric(lake, lake.X + cx * mid, lake.Z + cz * mid) < m)
                    lo = mid;
                else
                    hi = mid;
            }
            return (lo + hi) / 2;
        }
        return max;
    }

    /// <summary>A stable coin for a piece of water by its id: which way a river runs, which way Fundy's tide is setting.</summary>
    static int Way(string id)
    {
        uint h = 2166136261;
        foreach (char c in id)
            h = (h ^ c) * 16777619;
        return (h & 1) == 0 ? 1 : -1;
    }

    /// <summary>
    /// §12.4's water: the rivers under their spans and their standing water off the bed, the lakes to their shorelines,
    /// the shores' sea out into the fog, the rivers alongside between their banks. Each moves as its kind does (look.json
    /// <c>water</c>): a river runs down its valley, Fundy's tide sets along its shore, the sea and the lakes ripple and swell
    /// in the wind, a marsh lies still. The tar ponds' pitch stands still, glossy black under its oil film.
    /// </summary>
    void Water(MeshBuilder mesh, PlanScene p, Double3 eye, float drawDistance)
    {
        var origin = new Vector3(W(eye.X), W(eye.Y), W(eye.Z));
        var k = new Kit(_look, mesh) { SurfaceOrigin = origin, Baked = 0 };
        var tuning = _look?.Tuning.Water ?? new WaterTuning();
        float lapM = tuning.LapM;
        int layer = _look?.Layer("water_dark") ?? -1;
        // Without the look's water (a greybox build), a flat dark sheet, as it was.
        var sheet = layer >= 0 ? new WaterSheet(mesh, eye, layer) : null;
        if (sheet is null)
            k.Use("tar", new Vector3(0.05f, 0.06f, 0.07f), 0.05f, 0.9f, tile: 29);
        // The water's colour (maritime-rules.md §2-5): the upland lakes and rivers tea-dark with tannin, the sea slate,
        // Fundy's tidal water red-brown with the mud it carries; the marshes' standing water a little green.
        var peat = new Vector3(1.2f, 0.95f, 0.7f);
        var slate = new Vector3(0.85f, 1.0f, 1.1f);
        var mud = new Vector3(2.2f, 1.3f, 0.9f);
        var still = new Vector3(0.9f, 0.95f, 0.9f);
        // Pitch for the stretch being drawn (the tar ponds, biomes.json contaminatedMarsh, GDD §30): not water. Black,
        // glossy as a mirror, an oil film's colours on it and its slow bubbles: a marsh you'd not wade.
        bool pitch = false;
        void Pitch(bool tar)
        {
            if (tar == pitch)
                return;
            pitch = tar;
            if (tar)
                k.Use(_look?.Layer("tar") >= 0 ? "tar" : "water_dark", new Vector3(0.02f, 0.02f, 0.02f), PitchWear, 0.55f, tile: 9).Shade(0.3f);
            else if (sheet is null)
                k.Use("tar", new Vector3(0.05f, 0.06f, 0.07f), 0.05f, 0.9f, tile: 29);
        }
        Vector2 U(Vector3 q) => new(origin.X + q.X, origin.Z + q.Z);
        void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float la, float lb, float lc, float ld)
        {
            if (sheet is not null && !pitch)
                sheet.Quad(a, b, c, d, la, lb, lc, ld);
            else
                k.Quad(a, b, c, d, U(a), U(b), U(c), U(d), twoSided: true);
        }
        void Paint(Vector3 tint, string kind, Vector2 current)
        {
            if (pitch)
                return;
            if (sheet is not null)
            {
                // Deep water's own colour is all but lost under the sky it gives back (scene.frag keeps a third of it); a
                // silty one's mud is lit like the shallows'.
                sheet.Tint = tint * (1 + 1.9f * tuning.Of(kind).Murk);
                sheet.Open = tuning.Of(kind).Open;
                sheet.Current = current * tuning.Of(kind).Current;
            }
            else
                k.Tint = new Vector3(0.05f, 0.06f, 0.07f) * tint;
        }
        static Vector2 Flat(Double3 v) => new((float)v.X, (float)v.Z);
        // A strip of water beside a line from one sample to the next: its laterals out along r and its level at each end,
        // with the lap at each lateral.
        void Strip(TrackSample a, Double3 ra, double[] la, double ya, TrackSample b, Double3 rb, double[] lb, double yb, float[] lap)
        {
            static Vector3 P(TrackSample t, Double3 r, double l, double y, Double3 eye) => (new Double3(t.Position.X, y, t.Position.Z) + r * l).RelativeTo(eye);
            for (int i = 0; i + 1 < la.Length; i++)
                Face(P(a, ra, la[i], ya, eye), P(a, ra, la[i + 1], ya, eye), P(b, rb, lb[i + 1], yb, eye), P(b, rb, lb[i], yb, eye), lap[i], lap[i + 1], lap[i + 1], lap[i]);
        }

        foreach (var w in p.Plan.Water)
        {
            var line = p.EdgeLine(w.Edge);
            var mid = line.Sample(Math.Clamp((w.S0 + w.S1) / 2, 0, line.Length));
            if ((mid.Position - eye).Length > drawDistance + 150)
                continue;
            bool tar = w.Type == "contaminatedMarsh" || p.BiomeAt((w.S0 + w.S1) / 2) == "contaminatedMarsh";
            Pitch(tar);
            bool river = w.Type is "river" or "tidal";
            Paint(w.Type == "tidal" ? mud : river ? peat : still, river ? w.Type : "marsh", default);
            // A river runs across under the span and on down its valley either way into the far land (note 138), one way or
            // the other by its own id, its banks at either end of its crossing; a marsh lies beside the bed, lapping at it.
            int way = Way(w.Id);
            double step = river ? Math.Min(10, lapM) : 10;
            for (double s = w.S0; s < w.S1 - 1e-6; s += step)
            {
                double s1 = Math.Min(s + step, w.S1);
                var a = line.Sample(s);
                var b = line.Sample(s1);
                var ra = Double3.Cross(a.Tangent, Double3.Up).Normalized;
                var rb = Double3.Cross(b.Tangent, Double3.Up).Normalized;
                if (river)
                {
                    if (sheet is not null)
                        sheet.Current = Flat(ra) * (way * tuning.Of(w.Type).Current);
                    float Bank(double at) => (float)Math.Clamp(1 - Math.Min(at - w.S0, w.S1 - at) / lapM, 0, 1);
                    Vector3 P(TrackSample t, Double3 r, double l) => (new Double3(t.Position.X, w.LevelM, t.Position.Z) + r * l).RelativeTo(eye);
                    Face(P(a, ra, -1200), P(a, ra, 1200), P(b, rb, 1200), P(b, rb, -1200), Bank(s), Bank(s), Bank(s1), Bank(s1));
                }
                else
                    foreach (int side in new[] { -1, 1 })
                        Strip(a, ra * side, [5, 5 + lapM, 90], w.LevelM, b, rb * side, [5, 5 + lapM, 90], w.LevelM, [1, 0, 0]);
                if (tar)
                    foreach (int side in river ? new[] { 1 } : new[] { -1, 1 })
                        TarFilm(k, a, ra, side, w.LevelM, s, eye);
            }
        }
        Pitch(false);

        // Lakes: to their own shorelines (Rings), on under the land a little way so its rim hides the edge.
        foreach (var lake in p.Plan.Lakes)
        {
            var c = new Double3(lake.X, lake.LevelM, lake.Z);
            double reach = lake.RadiusM * lake.Stretch * (1 + lake.Wobble);
            if ((c - eye).Length > drawDistance + reach)
                continue;
            // A lake in the tar ponds' country is one of them: pitch, not peat water.
            var near = p.Terrain.Nearby(lake.X, lake.Z, 700).Where(q => q.Edge == 0).OrderBy(q => Math.Abs(q.Lateral)).FirstOrDefault();
            bool tar = p.BiomeAt(near.S) == "contaminatedMarsh";
            Pitch(tar);
            Paint(peat, "lake", default);
            var rings = _lakeRings.GetValue(lake, l => Rings(l, lapM));
            Vector3 R(Vector2 q) => new Double3(q.X, lake.LevelM, q.Y).RelativeTo(eye);
            var centre = c.RelativeTo(eye);
            int n = rings.Deep.Length;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                if (sheet is not null && !pitch)
                    sheet.Tri(centre, R(rings.Deep[i]), R(rings.Deep[j]), 0, 0, 0);
                else
                    Face(centre, R(rings.Deep[i]), R(rings.Deep[j]), centre, 0, 0, 0, 0);
                Face(R(rings.Deep[i]), R(rings.Waterline[i]), R(rings.Waterline[j]), R(rings.Deep[j]), 0, 1, 1, 0);
                Face(R(rings.Waterline[i]), R(rings.Skirt[i]), R(rings.Skirt[j]), R(rings.Waterline[j]), 1, 1, 1, 1);
            }
            if (tar)
            {
                var sheen = k.Tint;
                var rng = new Random(lake.Id.GetHashCode(StringComparison.Ordinal) & 0xffff);
                for (int i = 0; i < 6; i++)
                {
                    double a = rng.NextDouble() * Math.Tau, d = Math.Sqrt(rng.NextDouble()) * lake.RadiusM * 0.8;
                    TarSpot(k, new Double3(lake.X + Math.Cos(a) * d, lake.LevelM, lake.Z + Math.Sin(a) * d), rng, eye);
                }
                k.Tint = sheen;
            }
        }
        Pitch(false);

        // Shores: the sea from under the beach out into the fog, along the shore and past its tapered ends, its lap along the
        // waterline (the beach's foot; Fundy's and the dykes' out on the mud at low water); a river between its banks.
        foreach (var sh in p.Plan.Shores)
        {
            var line = p.EdgeLine(sh.Edge);
            double taper = p.Plan.Rules.Terrain.Shore.TaperM;
            var (tint, kind) = sh.Kind switch
            {
                ShoreKind.Sea => (slate, "sea"),
                ShoreKind.River => (peat, "river"),
                ShoreKind.Dyke => (mud, "dyke"),
                _ => (mud, "fundy"),
            };
            Paint(tint, kind, default);
            // A river runs down the way its rail falls; the sea and the tide set along the shore, one way by its id.
            int way = sh.Kind == ShoreKind.River
                ? line.Sample(Math.Clamp(sh.S1, 0, line.Length)).Position.Y < line.Sample(Math.Clamp(sh.S0, 0, line.Length)).Position.Y ? 1 : -1
                : Way(sh.Id);
            bool river = sh.Kind == ShoreKind.River;
            double from = sh.S0 - (river ? taper * 0.5 : taper), to = sh.S1 + (river ? taper * 0.5 : taper), step = river ? 10 : 20;
            for (double s = from; s < to; s += step)
            {
                double s1 = Math.Min(s + step, to);
                var a = line.Sample(Math.Clamp(s, 0, line.Length));
                var b = line.Sample(Math.Clamp(s1, 0, line.Length));
                if ((a.Position - eye).Length > drawDistance + (river ? 200 : 1500))
                    continue;
                bool tar = p.BiomeAt(Math.Clamp(s, 0, line.Length)) == "contaminatedMarsh";
                Pitch(tar);
                if (!tar)
                    Paint(tint, kind, Flat(a.Tangent) * way);
                var ra = Double3.Cross(a.Tangent, Double3.Up).Normalized * sh.Side;
                var rb = Double3.Cross(b.Tangent, Double3.Up).Normalized * sh.Side;
                double da = p.Terrain.ShoreEdge(sh, s), db = p.Terrain.ShoreEdge(sh, s1);
                if (river)
                {
                    // A ribbon between its banks, falling with the rail, along its meander: lapping at both.
                    double half = Math.Min(lapM, sh.FlatM / 2);
                    double[] Across(double d) => [d - 4, d, d + half, d + sh.FlatM - half, d + sh.FlatM, d + sh.FlatM + 4];
                    float inner = (float)(1 - half / lapM);
                    Strip(a, ra, Across(da), RiverLevel(a, sh), b, rb, Across(db), RiverLevel(b, sh), [1, 1, inner, inner, 1, 1]);
                    continue;
                }
                // The waterline: the beach's foot on the Atlantic (TerrainField.Shore), and out on the mud at low water on
                // Fundy's and the dykes' (the flat's surface dips under the water a little past halfway across it).
                double wa = sh.Kind == ShoreKind.Sea ? da : da + sh.FlatM * 0.53, wb = sh.Kind == ShoreKind.Sea ? db : db + sh.FlatM * 0.53;
                double inner2 = Math.Max(sh.NearM * 0.5, 12);
                if (tar && (a.Position - eye).Length < drawDistance)
                    for (int i = 0; i < 2; i++)
                        TarFilm(k, a, ra, 1, sh.LevelM, s + i * 10, eye);
                Strip(a, ra, [Math.Min(inner2, wa), wa, wa + lapM, 300, 1500], sh.LevelM, b, rb, [Math.Min(inner2, wb), wb, wb + lapM, 300, 1500], sh.LevelM, [1, 1, 0, 0, 0]);
            }
            Pitch(false);
        }
    }

    /// <summary>A river ribbon's laterals are measured from the rail's own height (TerrainField.Shore: its water follows the rail).</summary>
    static double RiverLevel(TrackSample t, PlanShore sh) => t.Position.Y - sh.LevelM;
}
