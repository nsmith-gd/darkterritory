using System.Numerics;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

/// <summary>
/// Dead settlements beside the line (GDD §30: "dead settlements ... contaminated rural spaces"; the art sheet's
/// "isolated town"): gabled houses of peeling plaster and sagging slate, their windows black, a church with a spire
/// you can see before you can see the church, a windmill with its sails stopped. Read by silhouette in the fog.
/// </summary>
public static partial class TownKit
{
    /// <summary>
    /// A house, <paramref name="variant"/> choosing its size, storeys and state: whole, roof fallen in at one end, or a
    /// shell. Front faces −Z (toward the line when placed).
    /// </summary>
    public static MeshAsset House(Look? look, int variant)
    {
        // The modelled frontier houses (tools/models/recipes/town_houses.py, note 139): weatherboard and trim that read
        // through the fog, front to −Z as this kit's. Without the look's props, the kit's boxes.
        if (look is not null && PropArt.Of(look).Get($"house_{((variant % Houses) + Houses) % Houses}") is { } modelled)
            return modelled;
        var k = new Kit(look, 1200 + variant);
        var rng = new Random(9001 + variant);
        float w = 5 + (float)rng.NextDouble() * 3, d = 6 + (float)rng.NextDouble() * 3;
        House(k, rng, w, d, variant);
        return k.Build($"house-{variant}");
    }

    /// <summary>
    /// A lived-in house inside a fortress's walls (T100 playtest: "fort villages should look like protected villages"):
    /// whole, its size by <paramref name="variant"/>, no deeper than <see cref="LivedDepth"/> so it stands between the
    /// line and the wall, and a lamp burning behind one or two of its front windows. Front faces −Z.
    /// </summary>
    public static MeshAsset LivedHouse(Look? look, int variant)
    {
        var k = new Kit(look, 1500 + variant);
        // Its size is the sim's (Fortresses.LivedSize, T124: the walls a crewmate bumps into are this house's).
        var rng = new Random(Sim.Run.Fortresses.HouseSeed + variant);
        var (fw, fd) = Sim.Run.Fortresses.LivedSize(rng);
        float w = (float)fw, d = (float)fd;
        House(k, rng, w, d, 0);
        // Lamplight behind the ground-floor glass: the windows House cut, one or two of them.
        k.Use("window_lit", Palette.LampAmber, 0.1f, 0.3f, tile: 1);
        k.Emissive = 1;
        int lit = 0;
        for (float x = -w / 2 + 1.2f; x < w / 2 - 0.8f && lit < 1 + variant % 2; x += 1.8f)
            if (MathF.Abs(x) >= 0.7f)
            {
                k.Panel(new Vector3(x, 1.3f, -d / 2 - 0.02f), -Vector3.UnitZ, Vector3.UnitY, 0.7f, 1.0f, Vector2.Zero, Vector2.One);
                lit++;
            }
        k.Emissive = 0;
        return k.Build($"lived-house-{variant}");
    }

    /// <summary>How many modelled houses there are (house_0 ..), and each one's walls' width and depth (m).</summary>
    const int Houses = 4;
    static readonly (float W, float D)[] HouseSizes = [(6.6f, 7.2f), (5.4f, 6.2f), (6.2f, 7.0f), (5.6f, 6.6f)];

    /// <summary>
    /// A stop's village house (level-design P9) as the modelled one (note 139), fitted to a footprint part <paramref
    /// name="w"/> across by <paramref name="d"/> deep: scaled to it, its height kept in proportion. False without the
    /// look's props (the kit's <see cref="House(Kit, Random, float, float, int)"/> then).
    /// </summary>
    public static bool HouseProp(Kit k, Look? look, float w, float d, int variant)
    {
        int i = ((variant % Houses) + Houses) % Houses;
        if (look is null || PropArt.Of(look).Get($"house_{i}") is not { } modelled)
            return false;
        var (pw, pd) = HouseSizes[i];
        float sx = w / pw, sz = d / pd, sy = Math.Clamp((sx + sz) / 2, 0.85f, 1.2f);
        k.Append(modelled, Matrix4x4.CreateScale(sx, sy, sz));
        return true;
    }

    /// <summary>The deepest a lived-in house is (front to back), so it fits inside a fortress's walls.</summary>
    public const float LivedDepth = (float)Sim.Run.Fortresses.LivedDepth;

    /// <summary>
    /// A house <paramref name="w"/> across (X) by <paramref name="d"/> deep (Z), centred on the kit's origin, front to −Z:
    /// a stop's village house, one per part of its footprint (level-design P9). <paramref name="rng"/> picks the rest.
    /// </summary>
    public static void House(Kit k, Random rng, float w, float d, int variant)
    {
        float h = rng.Next(2) == 0 ? 3.2f : 5.8f;
        int state = variant % 3; // 0 whole, 1 fallen in, 2 shell
        k.Use(rng.Next(2) == 0 ? "plaster_ruin" : "brick_soot", Palette.BlueGrey, 0.9f, 0.05f, tile: 2);
        k.Box(new Vector3(-w / 2, -0.4f, -d / 2), new Vector3(w / 2, h, d / 2), Kit.Faces.Sides);
        // Gable ends.
        float ridge = h + w * 0.45f;
        foreach (float z in new[] { -d / 2, d / 2 })
        {
            var a = new Vector3(-w / 2, h, z);
            var b = new Vector3(w / 2, h, z);
            var c = new Vector3(0, ridge, z);
            if (z < 0)
                k.Tri(a, c, b, new(a.X, -a.Y), new(c.X, -c.Y), new(b.X, -b.Y));
            else
                k.Tri(b, c, a, new(-b.X, -b.Y), new(-c.X, -c.Y), new(-a.X, -a.Y));
        }
        // Windows and a door: black holes, one or two with a shutter hanging.
        k.Use("glass_dirty", Palette.SootBlack, 0.4f, 0.4f, tile: 1);
        k.Shade(0.25f);
        int storeys = h > 4 ? 2 : 1;
        for (int s = 0; s < storeys; s++)
            for (float x = -w / 2 + 1.2f; x < w / 2 - 0.8f; x += 1.8f)
            {
                if (s == 0 && MathF.Abs(x) < 0.7f)
                    continue;
                k.Panel(new Vector3(x, 1.3f + s * 2.6f, -d / 2 - 0.01f), -Vector3.UnitZ, Vector3.UnitY, 0.8f, 1.1f, Vector2.Zero, Vector2.One);
                k.Panel(new Vector3(-x, 1.3f + s * 2.6f, d / 2 + 0.01f), Vector3.UnitZ, Vector3.UnitY, 0.8f, 1.1f, Vector2.Zero, Vector2.One);
            }
        k.Shade(0.4f);
        k.Doorway(new Vector3(0.2f, -0.1f, -d / 2 - 0.01f), -Vector3.UnitZ, 1.0f);
        // The roof: two slopes of slate, overhanging, broken open by the state.
        k.Use("roof_slate", Palette.Charcoal, 0.9f, 0.15f, tile: 1.5f);
        float over = 0.4f, z0 = -d / 2 - over, z1 = state == 1 ? 0.5f : d / 2 + over;
        if (state < 2)
        {
            var el = new Vector3(-w / 2 - over, h - over * 0.9f, 0);
            var er = new Vector3(w / 2 + over, h - over * 0.9f, 0);
            var top = new Vector3(0, ridge + 0.05f, 0);
            k.Quad(top with { Z = z1 }, top with { Z = z0 }, el with { Z = z0 }, el with { Z = z1 }, twoSided: true);
            k.Quad(top with { Z = z0 }, top with { Z = z1 }, er with { Z = z1 }, er with { Z = z0 }, twoSided: true);
            if (state == 1)
            {
                // What's left of the fallen half: rafters against the sky.
                k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0, tile: 1);
                for (float z = 1.2f; z < d / 2; z += 0.9f)
                {
                    k.Rod(new Vector3(-w / 2, h, z), new Vector3(0, ridge - (z - 0.5f) * 0.4f, z), 0.07f);
                    if (z < d / 2 - 1.5f)
                        k.Rod(new Vector3(w / 2, h, z), new Vector3(0, ridge - (z - 0.5f) * 0.4f, z), 0.07f);
                }
            }
        }
        // A chimney stack at one gable.
        k.Use("brick_soot", Palette.RustRed, 0.9f, 0.05f, tile: 1.2f);
        float cx = state == 2 ? w / 2 - 0.6f : -w / 4;
        k.Box(new Vector3(cx - 0.35f, h, d / 2 - 1.2f), new Vector3(cx + 0.35f, ridge + 1.1f, d / 2 - 0.5f), Kit.Faces.All & ~Kit.Faces.NegY);
    }

    /// <summary>The church: a long nave, a square tower and a slate spire, a round window in the gable gone black.</summary>
    public static MeshAsset Church(Look? look)
    {
        var k = new Kit(look, 1300);
        k.Use("stone_block", Palette.Charcoal, 0.9f, 0.1f, tile: 2.5f);
        k.Box(new Vector3(-4, -0.5f, -6), new Vector3(4, 7, 12), Kit.Faces.Sides);
        foreach (float z in new[] { -6f, 12f })
        {
            var a = new Vector3(-4, 7, z);
            var b = new Vector3(4, 7, z);
            var c = new Vector3(0, 11, z);
            if (z < 0)
                k.Tri(a, c, b, new(a.X, -a.Y), new(c.X, -c.Y), new(b.X, -b.Y));
            else
                k.Tri(b, c, a, new(-b.X, -b.Y), new(-c.X, -c.Y), new(-a.X, -a.Y));
        }
        k.Use("roof_slate", Palette.Charcoal, 0.9f, 0.15f, tile: 1.5f);
        k.Quad(new Vector3(0, 11.05f, 12.3f), new Vector3(0, 11.05f, -6.3f), new Vector3(-4.4f, 6.8f, -6.3f), new Vector3(-4.4f, 6.8f, 12.3f), twoSided: true);
        k.Quad(new Vector3(0, 11.05f, -6.3f), new Vector3(0, 11.05f, 12.3f), new Vector3(4.4f, 6.8f, 12.3f), new Vector3(4.4f, 6.8f, -6.3f), twoSided: true);
        k.Use("stone_block", Palette.Charcoal, 0.9f, 0.1f, tile: 2.5f);
        k.Box(new Vector3(-2.6f, -0.5f, -11.2f), new Vector3(2.6f, 17, -6), Kit.Faces.All & ~Kit.Faces.NegY);
        k.Use("roof_slate", Palette.Charcoal, 0.9f, 0.15f, tile: 1.5f);
        k.Lathe(new Vector3(0, 17, -8.6f), [new(3.6f, 0), new(0.05f, 13)], 4, smooth: false, capTop: false);
        k.Use("rust_heavy", Palette.IronGrey, 0.9f, 0.3f);
        k.Rod(new Vector3(0, 29.8f, -8.6f), new Vector3(0, 31.6f, -8.6f), 0.05f);
        k.Rod(new Vector3(-0.5f, 31.0f, -8.6f), new Vector3(0.5f, 31.0f, -8.6f), 0.04f);
        k.Use("glass_dirty", Palette.SootBlack, 0.4f, 0.4f, tile: 1);
        k.Shade(0.2f);
        k.Disc(new Vector3(0, 8.6f, 12.01f), Vector3.UnitZ, 1.1f, 10);
        for (float z = -4; z < 11; z += 3)
            foreach (int side in new[] { -1, 1 })
                k.Panel(new Vector3(side * 4.01f, 3.6f, z), new Vector3(side, 0, 0), Vector3.UnitY, 1.0f, 3.2f, Vector2.Zero, Vector2.One);
        k.Doorway(new Vector3(0, 0.5f, -11.21f), -Vector3.UnitZ, 1.6f, bay: true);
        return k.Build("church");
    }

    /// <summary>A windmill: a tapering tower, a cap, and four sails stopped at an angle (the art sheet's).</summary>
    public static MeshAsset Windmill(Look? look)
    {
        var k = new Kit(look, 1400);
        k.Use("plaster_ruin", Palette.BlueGrey, 0.9f, 0.05f, tile: 2);
        k.Cylinder(new Vector3(0, -0.5f, 0), new Vector3(0, 12, 0), 3.4f, 8, caps: false, smooth: false, radiusB: 2.3f);
        k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0, tile: 1.5f);
        k.Lathe(new Vector3(0, 12, 0), [new(2.6f, 0), new(2.4f, 1.2f), new(0.6f, 2.8f), new(0, 3)], 8, smooth: false);
        var hub = new Vector3(0, 13.4f, -2.8f);
        k.Rod(hub + new Vector3(0, 0, 0.9f), hub, 0.25f);
        for (int i = 0; i < 4; i++)
        {
            float a = 0.35f + i * MathF.PI / 2;
            var dir = new Vector3(MathF.Cos(a), MathF.Sin(a), 0);
            var side = new Vector3(-dir.Y, dir.X, 0);
            k.Rod(hub, hub + dir * 9, 0.12f);
            // The sail's lattice: a frame, most of its cloth gone.
            for (float r = 2; r < 9; r += 1.4f)
                k.Rod(hub + dir * r, hub + dir * r + side * 1.6f, 0.05f);
            k.Rod(hub + dir * 2 + side * 1.6f, hub + dir * 9 + side * 1.6f, 0.05f);
            if (i % 2 == 0)
            {
                k.Use("coat_oilskin", Palette.DeepBrown, 0.9f, 0, tile: 2);
                k.Quad(hub + dir * 2 + new Vector3(0, 0, -0.05f), hub + dir * 6 + new Vector3(0, 0, -0.05f), hub + dir * 6 + side * 1.6f + new Vector3(0, 0, -0.05f), hub + dir * 2 + side * 1.6f + new Vector3(0, 0, -0.05f), twoSided: true);
                k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0, tile: 1.5f);
            }
        }
        k.Use("glass_dirty", Palette.SootBlack, 0.4f, 0.4f, tile: 1);
        k.Shade(0.2f);
        k.Doorway(new Vector3(0, 0, -3.36f), -Vector3.UnitZ, 1.1f);
        return k.Build("windmill");
    }
    /// <summary>
    /// A village house you walk into and search (GDD App. F.3; ARCHITECTURE §8 note 326): its walls as the sim has them
    /// (<see cref="Sim.Run.StopWalls.OpenWalls"/>), the doorway in the face toward the line with a lintel over it, a
    /// boarded floor, gables and a slate roof over, and what its finds are kept in where the sim puts them: a cupboard
    /// against the back wall, a cabinet against a side, the cellar's hatch, a loose run of floorboards. In the building's
    /// frame: its axis (x) to −Z, across it (y) to +X.
    /// </summary>
    /// <summary>
    /// The one dim light in an open house (the director, 8 Oct: "some lighting inside, dim to keep it scary"), in its own
    /// frame: a candle stub left guttering on the floor in a corner, clear of what's kept there, or (one house in three) an
    /// oil lamp turned right down, hung from the middle of the ceiling. Its flame's height over the floor. The scene lights
    /// it; the art stands it there.
    /// </summary>
    /// <param name="clutter">Its ransacked furniture (StopWalls.ClutterOf): a candle stands clear of the heavy pieces.</param>
    /// <param name="nest">Where the Gaunt nests in it, if it does: then there's no light at all (null), and the dark is the tell.</param>
    public static (double X, double Y, float Height, bool Lamp)? HouseLight(Sim.Stops.StopBuilding b, IEnumerable<Sim.Stops.StopContainer> kept,
        IReadOnlyList<Sim.Run.Clutter>? clutter = null, (double X, double Y)? nest = null)
    {
        if (nest is not null)
            return null;
        var o = Sim.Run.StopWalls.Outline(b);
        if (b.Variant % 3 == 0)
        {
            var (x0, y0, x1, y1) = o.Cells.MaxBy(c => (c.X1 - c.X0) * (c.Y1 - c.Y0));
            return ((x0 + x1) / 2, (y0 + y1) / 2, 2.25f, true);
        }
        // Every inside corner by a wall with no door, in the outline's order turned by the house's variant; the first clear of
        // the furniture and the finds.
        double t = Sim.Run.StopWalls.WallThickness, inset = t + 0.2;
        var taken = kept.SelectMany(c =>
        {
            var (kx, ky, _, _) = Sim.Run.StopWalls.Kept(b, c);
            var (fx, fy) = Sim.Run.StopWalls.InsideLocal(b, c);
            return new[] { (kx, ky), (fx, fy) };
        }).ToList();
        var corners = new List<(double, double)>();
        for (int k = 0; k < o.Runs.Count; k++)
        {
            // (Not by the inner wall's faces: their ends are its doorway. The outside's runs end at it, so its corners are had.)
            if (o.Doors.Contains(k) || o.Runs[k].Inner)
                continue;
            var r = o.Runs[k];
            var (nx, ny) = (-r.Normal.X, -r.Normal.Y);
            foreach (var (end, dir) in new[] { (0.0, 1.0), (1.0, -1.0) })
            {
                var (px, py) = r.Point(end);
                var (ax, ay) = r.AlongX ? (dir, 0.0) : (0.0, dir);
                double x = px + ax * inset + nx * inset, y = py + ay * inset + ny * inset;
                if (Sim.Run.StopWalls.InParts(b, x, y))
                    corners.Add((x, y));
            }
        }
        for (int i = 0; i < corners.Count; i++)
        {
            var (x, y) = corners[(i + b.Variant) % corners.Count];
            if (taken.All(p => Math.Abs(p.Item1 - x) + Math.Abs(p.Item2 - y) > 1.4)
                && (clutter ?? []).All(c => !c.Solid || Math.Abs(c.X - x) > c.Box.HalfX + 0.15 || Math.Abs(c.Y - y) > c.Box.HalfY + 0.15))
                return (x, y, 0.16f, false);
        }
        var c0 = o.Cells[0];
        return ((c0.X0 + c0.X1) / 2, (c0.Y0 + c0.Y1) / 2, 2.25f, true);
    }

    /// <summary>
    /// A gabled roof on walls <paramref name="eaves"/> high, in the kit's frame: centred at (<paramref name="cx"/>,
    /// <paramref name="cz"/>), <paramref name="span"/> across the ridge and <paramref name="length"/> along it, the ridge along
    /// Z (or X); the gable ends in plaster, both faces, so they read from inside too, then the slate.
    /// </summary>
    static void Gabled(Kit k, float cx, float cz, float span, float length, bool ridgeAlongZ, float eaves)
    {
        // (across, up, along) the ridge, out to the kit's frame.
        Vector3 M(float u, float h, float v) => ridgeAlongZ ? new(cx + u, h, cz + v) : new(cx + v, h, cz + u);
        float w = span, d = length, ridge = eaves + w * 0.42f;
        k.Use("plaster_ruin", Palette.BlueGrey, 0.9f, 0.05f, tile: 2);
        foreach (float z in new[] { -d / 2, d / 2 })
        {
            var a = M(-w / 2, eaves, z);
            var c = M(w / 2, eaves, z);
            var top = M(0, ridge, z);
            // Texture across the gable (u) and up it, as the plain house's had it.
            Vector2 Uv(float u, float h, float sign) => new(sign * u, -h);
            k.Tri(a, top, c, Uv(-w / 2, eaves, 1), Uv(0, ridge, 1), Uv(w / 2, eaves, 1));
            k.Tri(c, top, a, Uv(w / 2, eaves, -1), Uv(0, ridge, -1), Uv(-w / 2, eaves, -1));
        }
        k.Use("roof_slate", Palette.Charcoal, 0.9f, 0.15f, tile: 1.5f);
        float over = 0.35f, z0 = -d / 2 - over, z1 = d / 2 + over;
        k.Quad(M(0, ridge + 0.05f, z1), M(0, ridge + 0.05f, z0), M(-w / 2 - over, eaves - over * 0.9f, z0), M(-w / 2 - over, eaves - over * 0.9f, z1), twoSided: true);
        k.Quad(M(0, ridge + 0.05f, z0), M(0, ridge + 0.05f, z1), M(w / 2 + over, eaves - over * 0.9f, z1), M(w / 2 + over, eaves - over * 0.9f, z0), twoSided: true);
    }

    /// <summary>
    /// An open house's windows, boarded over (GDD §30's dead settlements; the Wiki's "white trim still shows round windows
    /// boarded over with raw planks"): along each run of its outside wall with no door, one or two, as fit a window's width and a
    /// pier either side, each a white trim frame with a black pane behind three raw planks nailed across it, ragged, and the
    /// planks' backs on the inside face. Art only: the wall's as solid behind them as anywhere (StopWalls).
    /// </summary>
    /// <param name="box">A box in the house's frame: middle, half sizes, bottom and top over the floor.</param>
    static void BoardedWindows(Kit k, Sim.Stops.StopBuilding b, Action<double, double, double, double, float, float> box)
    {
        const float Sill = 0.95f, High = 1.1f, Half = 0.44f, TrimW = 0.07f;
        double t = Sim.Run.StopWalls.WallThickness;
        var o = Sim.Run.StopWalls.Outline(b);
        int n = 0;
        for (int i = 0; i < o.Runs.Count; i++)
        {
            var r = o.Runs[i];
            if (r.Inner || o.Doors.Contains(i))
                continue;
            // One or two, as fit, a pier of about 1 m either side and between, spread evenly along the run.
            int count = Math.Min(2, (int)((r.Length - 1.0) / (2 * Half + 1.6)));
            for (int w = 0; w < count; w++)
            {
                double along = r.A + r.Length * (w + 1) / (count + 1);
                int seed = b.Variant * 31 + n++ * 7;
                // Outside: the frame proud of the wall's face, the pane black behind, the planks across it. Inside: the planks' backs.
                foreach (int face in new[] { 1, -1 })
                {
                    double at = face > 0 ? r.At + r.Out * 0.02 : r.At - r.Out * (t + 0.02);
                    void Across(double a0, double a1, float y0, float y1, double depth) =>
                        box(r.AlongX ? (a0 + a1) / 2 : at + r.Out * face * depth, r.AlongX ? at + r.Out * face * depth : (a0 + a1) / 2,
                            r.AlongX ? (a1 - a0) / 2 : 0.02, r.AlongX ? 0.02 : (a1 - a0) / 2, y0, y1);
                    if (face > 0)
                    {
                        k.Use("glass_dirty", Palette.SootBlack, 0.4f, 0.4f, tile: 1);
                        Across(along - Half, along + Half, Sill, Sill + High, -0.01);
                        k.Use("clapboard", new Vector3(0.62f, 0.6f, 0.55f), 0.9f, 0.05f, tile: 3);
                        Across(along - Half - TrimW, along - Half, Sill - TrimW, Sill + High + TrimW, 0.01);
                        Across(along + Half, along + Half + TrimW, Sill - TrimW, Sill + High + TrimW, 0.01);
                        Across(along - Half - TrimW, along + Half + TrimW, Sill + High, Sill + High + TrimW, 0.01);
                        Across(along - Half - TrimW - 0.04, along + Half + TrimW + 0.04, Sill - TrimW - 0.03f, Sill, 0.02);
                    }
                    // Three planks, each a little longer one side than the other and off level, as they were nailed up in a hurry.
                    k.Use("wood_grey", new Vector3(0.42f, 0.34f, 0.24f) * (face > 0 ? 1 : 0.7f), 0.85f, 0.03f, tile: 1);
                    for (int p = 0; p < 3; p++)
                    {
                        int h = (seed + p * 13) % 7;
                        float y = Sill + 0.18f + p * 0.36f + (h - 3) * 0.012f;
                        double left = along - Half - 0.06 - (h % 3) * 0.04, right = along + Half + 0.06 + ((h + 1) % 3) * 0.04;
                        Across(left, right, y - 0.08f, y + 0.08f, 0.04);
                    }
                }
            }
        }
    }

    /// <summary>
    /// The tints over the boards' own colour a house's dresser and sideboard wear (note 760): their doors' worn milk paint, a
    /// muddy olive gone grey; the carcass stained dark; the dark of inside one; its shelves.
    /// </summary>
    public static readonly Vector3 Painted = new(0.72f, 0.78f, 0.6f), Stained = new(0.7f, 0.55f, 0.42f), Inside = new(0.06f, 0.05f, 0.04f),
        Shelf = new(0.65f, 0.52f, 0.4f);

    // The dresser's and the sideboard's fronts (note 760), shut in the cooked house and swung open by the scene once searched
    // (GreyboxScene.SearchedSpots, FrontPiece): their doors' heights off the boards, the frames round their panels, how far
    // in from the sides and apart in the middle they are; the sideboard's drawer; the panels a shade darker than the frames;
    // the grain, a board to a door. Not design numbers: a kitchen dresser and a sideboard.
    public static readonly (float From, float To)[] DresserDoors = [(0.12f, 0.86f), (0.99f, 1.76f)];
    public const float DoorFrame = 0.065f, DoorInset = 0.03f, DoorGap = 0.006f, DrawerFrom = 0.64f, DrawerTo = 0.8f, DrawerFrame = 0.035f,
        DrawerInset = 0.04f, PanelShade = 0.8f, FrameShade = 1.0f, PanelTile = 1.2f, FrontTile = 2.5f, CarcassTile = 2f;

    /// <summary>The jackpot house's door's paint (note 719): oxblood, gone dull.</summary>
    public static readonly Vector3 Oxblood = new(0.42f, 0.09f, 0.07f);

    /// <summary>
    /// An open house's door (notes 401, 719, 941), in its own frame: x across from its hinge at 0 to its free edge at
    /// <paramref name="width"/> (with <paramref name="mirror"/>, the hinge at <paramref name="width"/> and the free edge at
    /// 0), y up from 0 to <paramref name="height"/>, z through it, its outside face out along +z and its leaf
    /// 2·<see cref="DoorThick"/> thick about z = 0. The director, 20 Oct: "there's no texture for the abandoned village
    /// doors, so it looks like I'm picking nothing". A village's is boards of the houses' grey deal, ledged and braced on the
    /// room's side, strap hinges and a ring latch of rusted iron on the outside. The jackpot house's (<paramref name="better"/>)
    /// is panelled and painted oxblood with a brass knob and knocker, and its lock is what the wrench picks: a brass plate
    /// under the knob with its keyhole black in it, and while it's <paramref name="locked"/> a barrel bolt shot into the
    /// frame with a padlock hung through it.
    /// </summary>
    public static MeshAsset HouseDoor(Look? look, float width, float height, bool better, bool locked, bool mirror)
    {
        var k = new Kit(look, better ? 64 : 63);
        const float T = DoorThick;
        float X(float a) => mirror ? width - a : a;
        void At(float a, float y, float z, float hx, float hy, float hz) => k.BoxAt(new Vector3(X(a), y, z), new Vector3(hx, hy, hz));
        // An upright piece, its grain up it: built lying along x (the texture's grain) and stood up.
        void Upright(float a, float y, float z, float hx, float hy, float hz) =>
            k.With(Matrix4x4.CreateRotationZ(MathF.PI / 2) * Kit.At(X(a), y, z), () => k.BoxAt(Vector3.Zero, new Vector3(hy, hx, hz)));
        if (better)
        {
            // Stiles and rails round four sunk panels, the panels a shade darker, the whole painted and worn.
            k.Use("wood_floor", Oxblood, 0.8f, 0.08f, tile: PanelTile).Tint = Oxblood * PanelShade;
            At(width / 2, height / 2, 0, width / 2 - 0.004f, height / 2 - 0.004f, T * 0.6f);
            k.Use("wood_grey", Oxblood, 0.75f, 0.08f, tile: FrontTile).Tint = Oxblood;
            float stile = 0.11f, rail = 0.13f, mid = height * 0.52f;
            foreach (float a in new[] { stile / 2, width / 2, width - stile / 2 })
                Upright(a, height / 2, 0, a == width / 2 ? 0.04f : stile / 2, height / 2, T);
            foreach (float y in new[] { rail / 2, mid, height - rail / 2 })
                At(width / 2, y, 0, width / 2, y == mid ? 0.08f : rail / 2, T);
            // Raised fields in the panels, outside and in.
            k.Use("wood_floor", Oxblood, 0.8f, 0.08f, tile: PanelTile).Tint = Oxblood * 0.9f;
            float pw = (width - 2 * stile - 0.08f) / 2;
            foreach (float a in new[] { stile + pw / 2, width - stile - pw / 2 })
                foreach (var (y0, y1) in new[] { (rail, mid - 0.08f), (mid + 0.08f, height - rail) })
                    foreach (float z in new[] { 1f, -1f })
                        At(a, (y0 + y1) / 2, z * T * 0.75f, pw / 2 - 0.035f, (y1 - y0) / 2 - 0.035f, T * 0.3f);
            k.Use("brass", Palette.TarnishedBrass, 0.55f, 0.6f, tile: 0.3f);
            // The knob and its rose, the knocker on the top rail, and the lock: its plate, keyhole and all.
            float knob = width - 0.09f;
            k.Cylinder(new Vector3(X(knob), 1.0f, T), new Vector3(X(knob), 1.0f, T + 0.05f), 0.012f, 8, radiusB: 0.03f);
            k.Cylinder(new Vector3(X(knob), 1.0f, T), new Vector3(X(knob), 1.0f, T + 0.008f), 0.035f, 10);
            At(width / 2, height - 0.42f, T + 0.012f, 0.05f, 0.07f, 0.012f);
            k.Cylinder(new Vector3(X(width / 2) - 0.05f, height - 0.5f, T + 0.03f), new Vector3(X(width / 2) + 0.05f, height - 0.5f, T + 0.03f), 0.012f, 6);
            At(knob, 0.86f, T + 0.006f, 0.035f, 0.075f, 0.006f);
            k.Use("rust_heavy", Palette.SootBlack, 0.95f, 0, tile: 1).Tint = Palette.SootBlack * 0.3f;
            k.Cylinder(new Vector3(X(knob), 0.885f, T + 0.012f), new Vector3(X(knob), 0.885f, T + 0.0125f), 0.011f, 8);
            At(knob, 0.855f, T + 0.0122f, 0.0045f, 0.022f, 0.0005f);
            if (locked)
            {
                // A barrel bolt shot into the frame (the doorway's jamb hides where it goes), padlocked through its staple.
                k.Use("rust_heavy", Palette.IronGrey, 0.9f, 0.3f, tile: 1);
                float bolt = 1.24f, staple = width - 0.1f;
                At(width - 0.17f, bolt, T + 0.006f, 0.17f, 0.035f, 0.006f);
                k.Cylinder(new Vector3(X(width - 0.32f), bolt, T + 0.022f), new Vector3(X(width + 0.02f), bolt, T + 0.022f), 0.011f, 8);
                At(staple, bolt, T + 0.03f, 0.012f, 0.045f, 0.018f);
                // The padlock hung from the staple: its shackle and its brass body, the keyhole black in it.
                foreach (float sx in new[] { -0.025f, 0.025f })
                    k.Rod(new Vector3(X(staple + sx), bolt - 0.075f, T + 0.04f), new Vector3(X(staple + sx), bolt - 0.005f, T + 0.04f), 0.007f);
                k.Rod(new Vector3(X(staple - 0.025f), bolt - 0.005f, T + 0.04f), new Vector3(X(staple + 0.025f), bolt - 0.005f, T + 0.04f), 0.007f);
                k.Use("brass", Palette.TarnishedBrass, 0.7f, 0.45f, tile: 0.3f);
                At(staple, bolt - 0.12f, T + 0.04f, 0.042f, 0.05f, 0.017f);
                k.Use("rust_heavy", Palette.SootBlack, 0.95f, 0, tile: 1).Tint = Palette.SootBlack * 0.3f;
                At(staple, bolt - 0.13f, T + 0.058f, 0.005f, 0.016f, 0.001f);
            }
            return k.Build(locked ? "house-door-locked" : "house-door-better");
        }
        // Boards side by side, each its own width, a hair apart.
        k.Use("wood_grey", new Vector3(0.42f, 0.34f, 0.24f), 0.9f, 0.03f, tile: 1);
        int boards = 5;
        for (int i = 0; i < boards; i++)
        {
            float a0 = width * i / boards, a1 = width * (i + 1) / boards;
            k.Shade(i % 2 == 0 ? 1f : 0.92f);
            Upright((a0 + a1) / 2, height / 2 - (i % 3) * 0.004f, 0, (a1 - a0) / 2 - 0.003f, height / 2 - (i % 3) * 0.004f, T);
            k.Shade(i % 2 == 0 ? 1f : 1 / 0.92f);
        }
        // On the room's side: the ledges top and bottom and the brace between them, rising from the hinge's foot.
        k.Shade(0.85f);
        foreach (float y in new[] { 0.3f, height - 0.3f })
            At(width / 2, y, -T - 0.012f, width / 2 - 0.04f, 0.06f, 0.012f);
        var foot = new Vector3(X(0.08f), 0.36f, -T - 0.012f);
        var head = new Vector3(X(width - 0.08f), height - 0.36f, -T - 0.012f);
        k.Cylinder(foot, head, 0.05f, 4, smooth: false);
        // Strap hinges and the ring latch, rusted iron, on the outside.
        k.Use("rust_heavy", Palette.IronGrey, 0.9f, 0.3f, tile: 1);
        foreach (float y in new[] { 0.3f, height - 0.3f })
        {
            At(width * 0.32f, y, T + 0.004f, width * 0.32f, 0.022f, 0.004f);
            k.Cylinder(new Vector3(X(0.012f), y - 0.06f, T + 0.006f), new Vector3(X(0.012f), y + 0.06f, T + 0.006f), 0.012f, 6);
        }
        float latch = width - 0.1f;
        At(latch, 1.0f, T + 0.004f, 0.025f, 0.06f, 0.004f);
        for (int i = 0; i < 8; i++)
        {
            float a0 = i * MathF.Tau / 8, a1 = (i + 1) * MathF.Tau / 8;
            k.Rod(new Vector3(X(latch) + 0.04f * MathF.Sin(a0), 0.93f - 0.04f * MathF.Cos(a0), T + 0.014f),
                new Vector3(X(latch) + 0.04f * MathF.Sin(a1), 0.93f - 0.04f * MathF.Cos(a1), T + 0.014f), 0.005f);
        }
        return k.Build("house-door");
    }

    /// <summary>Half an open house's door's thickness (m): a plank door's boards, an inch and a bit.</summary>
    public const float DoorThick = 0.025f;

    /// <summary>
    /// A door or a drawer front on its own (note 760; the one a searched dresser's swung open on), in its own frame: x across
    /// from 0 to <paramref name="width"/>, y up from 0 to <paramref name="height"/>, its back on z = 0 and its face out along
    /// +z. As TownKit's shut ones: a frame of stiles and rails round its panel, set back, in the worn milk paint; its knob at
    /// (<paramref name="knobX"/>, <paramref name="knobY"/>); with <paramref name="drawer"/>, the drawer's box behind it.
    /// </summary>
    public static MeshAsset FrontPiece(Look? look, float width, float height, float frame, float knobX, float knobY, float drawer = 0)
    {
        var k = new Kit(look, 61);
        k.Use("wood_floor", Painted, 0.85f, 0.05f, tile: PanelTile).Tint = Painted * PanelShade;
        k.Box(new Vector3(0.004f, 0.004f, 0), new Vector3(width - 0.004f, height - 0.004f, 0.008f));
        k.Use("wood_grey", Painted, 0.8f, 0.05f, tile: FrontTile).Tint = Painted * FrameShade;
        k.Box(new Vector3(0, 0, 0.008f), new Vector3(width, frame, 0.018f));
        k.Box(new Vector3(0, height - frame, 0.008f), new Vector3(width, height, 0.018f));
        k.Box(new Vector3(0, frame, 0.008f), new Vector3(frame, height - frame, 0.018f));
        k.Box(new Vector3(width - frame, frame, 0.008f), new Vector3(width, height - frame, 0.018f));
        if (drawer > 0)
        {
            // Its sides and bottom, raw deal, and the dark in it.
            k.Use("wood_grey", Shelf, 0.85f, 0.03f, tile: FrontTile).Tint = Shelf * 1.3f;
            k.Box(new Vector3(0.01f, 0.01f, -drawer), new Vector3(0.022f, height - 0.02f, 0));
            k.Box(new Vector3(width - 0.022f, 0.01f, -drawer), new Vector3(width - 0.01f, height - 0.02f, 0));
            k.Box(new Vector3(0.01f, 0.01f, -drawer), new Vector3(width - 0.01f, 0.022f, 0));
            k.Box(new Vector3(0.01f, 0.01f, -drawer), new Vector3(width - 0.01f, height - 0.02f, -drawer + 0.012f));
        }
        k.Use("rust_heavy", Palette.IronGrey, 0.7f, 0.35f, tile: 1);
        k.Cylinder(new Vector3(knobX, knobY, 0.018f), new Vector3(knobX, knobY, 0.036f), 0.012f, 6, radiusB: 0.016f);
        return k.Build("furniture-front");
    }

    /// <summary>
    /// What a searched dresser shows where its doors were (note 760), in its own frame: x across its face (its middle at 0), y up
    /// off the boards, z out of it: the dark inside, out past its shut doors' knobs (the cooked house still has them), its two
    /// halves' shelves and the worktop's ledge between them, catching a lamp.
    /// </summary>
    public static MeshAsset DresserInside(Look? look)
    {
        var k = new Kit(look, 62);
        float half = (float)Sim.Run.StopWalls.CupboardWidth - DoorInset;
        k.Use("wood_grey", Inside, 0.95f, 0, tile: FrontTile).Tint = Inside;
        foreach (var (h0, h1) in DresserDoors)
            k.Box(new Vector3(-half, h0, 0), new Vector3(half, h1, 0.004f));
        k.Use("wood_grey", Shelf, 0.85f, 0.03f, tile: FrontTile).Tint = Shelf;
        foreach (var (h0, h1) in DresserDoors)
        {
            float shelf = h0 + (h1 - h0) * 0.48f;
            k.Box(new Vector3(-half, shelf - 0.012f, 0.004f), new Vector3(half, shelf + 0.012f, 0.03f));
        }
        return k.Build("dresser-inside");
    }

    // The cellar's hatch and the loose boards (note 991), each in its own frame: x across, y up off the boards' top, z out into
    // the room the way the spot faces (its back, toward the wall it's nearest, at −z). The hatch's hinges are on its back
    // edge, so a searched one stands up against the wall side. Not design numbers: a farmhouse's trapdoor and floorboards.
    public const float HatchHalf = 0.47f, HatchThick = 0.035f, HatchFrame = 0.05f, BoardWidth = 0.12f, BoardGap = 0.008f, BoardsHalf = 0.55f;
    static readonly Vector3 Oak = new(0.55f, 0.42f, 0.3f), Deal = new(0.62f, 0.5f, 0.38f);

    /// <summary>
    /// Note 991: the cellar's trapdoor on its own, shut (flat, its top up): five planks, their grain along it, a little apart;
    /// two battens under them; a pair of strap hinges from its back edge; an iron ring pull lying in its staple at the front.
    /// The scene stands it up on its hinges once the cellar's searched (GreyboxScene.SearchedSpots).
    /// </summary>
    public static MeshAsset CellarLid(Look? look) => CellarLid(new Kit(look, 63)).Build("cellar-lid");

    static Kit CellarLid(Kit k)
    {
        const int planks = 5;
        float w = 2 * HatchHalf / planks;
        for (int i = 0; i < planks; i++)
        {
            k.Use("wood_floor", Oak, 0.9f, 0.03f, tile: 1.4f).Tint = Oak * (i % 2 == 0 ? 0.92f : 1.05f) * (i == 3 ? 0.85f : 1);
            float x0 = -HatchHalf + i * w + 0.004f, x1 = x0 + w - 0.008f;
            k.Box(new Vector3(x0, 0, -HatchHalf), new Vector3(x1, HatchThick, HatchHalf));
        }
        k.Use("wood_grey", Oak, 0.9f, 0.02f, tile: 2).Tint = Oak * 0.75f;
        foreach (float z in new[] { -0.28f, 0.28f })
            k.Box(new Vector3(-HatchHalf + 0.06f, -0.03f, z - 0.05f), new Vector3(HatchHalf - 0.06f, 0, z + 0.05f));
        // The strap hinges, rusted: a strap from the knuckle at the back edge in across the planks, a nail at each end.
        k.Use("rust_heavy", Palette.RustRed, 0.8f, 0.3f, tile: 1).Tint = new Vector3(0.6f, 0.45f, 0.4f);
        foreach (float x in new[] { -0.28f, 0.28f })
        {
            k.Box(new Vector3(x - 0.025f, HatchThick, -HatchHalf - 0.01f), new Vector3(x + 0.025f, HatchThick + 0.006f, -0.08f));
            k.Cylinder(new Vector3(x - 0.04f, HatchThick * 0.5f, -HatchHalf - 0.012f), new Vector3(x + 0.04f, HatchThick * 0.5f, -HatchHalf - 0.012f), 0.016f, 6);
        }
        // The ring pull, lying flat in its staple, toward the front.
        k.Use("iron_smokebox", Palette.IronGrey, 0.7f, 0.5f).Tint = new Vector3(1.6f, 1.55f, 1.5f);
        var ring = new Vector3(0, HatchThick + 0.008f, HatchHalf - 0.13f);
        for (int i = 0; i < 8; i++)
        {
            float a0 = i * MathF.Tau / 8, a1 = (i + 1) * MathF.Tau / 8;
            k.Rod(ring + new Vector3(MathF.Cos(a0), 0, MathF.Sin(a0)) * 0.05f, ring + new Vector3(MathF.Cos(a1), 0, MathF.Sin(a1)) * 0.05f, 0.006f, 4);
        }
        k.Box(new Vector3(-0.012f, HatchThick, ring.Z - 0.07f), new Vector3(0.012f, HatchThick + 0.016f, ring.Z - 0.045f));
        return k;
    }

    /// <summary>Note 991: the cellar shut, as the cooked house has it: its trapdoor in an oak frame let into the floor.</summary>
    public static MeshAsset CellarHatch(Look? look)
    {
        var k = new Kit(look, 64);
        k.Use("wood_grey", Oak, 0.85f, 0.04f, tile: 2).Tint = Oak * 0.7f;
        float o = HatchHalf + HatchFrame;
        k.Box(new Vector3(-o, 0, -o), new Vector3(o, 0.012f, -HatchHalf));
        k.Box(new Vector3(-o, 0, HatchHalf), new Vector3(o, 0.012f, o));
        k.Box(new Vector3(-o, 0, -HatchHalf), new Vector3(-HatchHalf, 0.012f, HatchHalf));
        k.Box(new Vector3(HatchHalf, 0, -HatchHalf), new Vector3(o, 0.012f, HatchHalf));
        CellarLid(k);
        return k.Build("cellar-hatch");
    }

    /// <summary>
    /// Note 991: the cellar opened (drawn over the shut hatch once it's searched): the dark of the hole down, its frame's lip
    /// round it, and the top of the ladder down into it, its stiles out over the floor.
    /// </summary>
    public static MeshAsset CellarOpen(Look? look)
    {
        var k = new Kit(look, 65);
        k.Use("paint_black", Inside, 1, 0).Tint = Vector3.One * 0.2f;
        k.Box(new Vector3(-HatchHalf, -0.4f, -HatchHalf), new Vector3(HatchHalf, HatchThick + 0.02f, HatchHalf), top: true, bottom: false);
        // The ladder, its foot down on the cellar's floor at the back, leant out to the hole's front edge.
        k.Use("wood_grey", Deal, 0.9f, 0.02f, tile: 2).Tint = Deal * 0.8f;
        foreach (float x in new[] { -0.2f, 0.2f })
            k.Rod(new Vector3(x, -0.4f, -0.05f), new Vector3(x, 0.55f, HatchHalf - 0.08f), 0.024f, 4);
        foreach (float y in new[] { 0.15f, 0.42f })
            k.Rod(new Vector3(-0.2f, y, -0.05f + (y + 0.4f) / 0.95f * (HatchHalf - 0.03f)), new Vector3(0.2f, y, -0.05f + (y + 0.4f) / 0.95f * (HatchHalf - 0.03f)), 0.014f, 4);
        return k.Build("cellar-open");
    }

    /// <summary>
    /// Note 991: loose floorboards over something kept under them, as the cooked house has them: five short lengths of deal
    /// let into the floor across the grain of its boards, darker and a little proud, their nails drawn, the fourth sprung up
    /// at its front end.
    /// </summary>
    public static MeshAsset LooseBoards(Look? look)
    {
        var k = new Kit(look, 66);
        for (int i = 0; i < 5; i++)
        {
            float x0 = (i - 2.5f) * (BoardWidth + BoardGap) + BoardGap / 2, x1 = x0 + BoardWidth;
            k.Use("wood_floor", Deal, 0.9f, 0.03f, tile: 1.6f).Tint = Deal * (0.62f + 0.06f * (i % 3));
            if (i == 3)
            {
                // Sprung: its back end on the joist, its front end up 4 cm.
                k.Quad(new Vector3(x0, 0.012f, -BoardsHalf), new Vector3(x1, 0.012f, -BoardsHalf), new Vector3(x1, 0.052f, BoardsHalf), new Vector3(x0, 0.052f, BoardsHalf));
                k.Quad(new Vector3(x0, 0, BoardsHalf), new Vector3(x1, 0, BoardsHalf), new Vector3(x1, 0.052f, BoardsHalf), new Vector3(x0, 0.052f, BoardsHalf), twoSided: true);
                k.Quad(new Vector3(x0, 0, -BoardsHalf), new Vector3(x0, 0, BoardsHalf), new Vector3(x0, 0.052f, BoardsHalf), new Vector3(x0, 0.012f, -BoardsHalf), twoSided: true);
                k.Quad(new Vector3(x1, 0, -BoardsHalf), new Vector3(x1, 0, BoardsHalf), new Vector3(x1, 0.052f, BoardsHalf), new Vector3(x1, 0.012f, -BoardsHalf), twoSided: true);
                continue;
            }
            k.Box(new Vector3(x0, 0, -BoardsHalf), new Vector3(x1, 0.012f, BoardsHalf));
        }
        // The gaps between them dark, and a nail head left at either end of the shut ones.
        k.Use("paint_black", Inside, 1, 0).Tint = Vector3.One * 0.3f;
        k.Box(new Vector3(-2.5f * (BoardWidth + BoardGap), 0, -BoardsHalf), new Vector3(2.5f * (BoardWidth + BoardGap), 0.004f, BoardsHalf));
        k.Use("iron_smokebox", Palette.IronGrey, 0.7f, 0.5f).Tint = new Vector3(1.4f, 1.35f, 1.3f);
        for (int i = 0; i < 5; i++)
            if (i != 3)
                foreach (float z in new[] { -BoardsHalf + 0.05f, BoardsHalf - 0.05f })
                {
                    float x = (i - 2) * (BoardWidth + BoardGap);
                    k.Box(new Vector3(x - 0.008f, 0.012f, z - 0.008f), new Vector3(x + 0.008f, 0.016f, z + 0.008f));
                }
        return k.Build("loose-boards");
    }

    /// <summary>
    /// Note 991: the loose boards lifted (drawn over them once searched): the dark under the floor where the middle three were,
    /// the joists across it, and the three laid beside the gap on the floor, one leant on the next.
    /// </summary>
    public static MeshAsset UnderFloorOpen(Look? look)
    {
        var k = new Kit(look, 67);
        float half = 1.5f * (BoardWidth + BoardGap);
        k.Use("paint_black", Inside, 1, 0).Tint = Vector3.One * 0.2f;
        k.Box(new Vector3(-half, -0.25f, -BoardsHalf), new Vector3(half, 0.056f, BoardsHalf), top: true, bottom: false);
        // The joists under it, seen across the gap, and the dust between them a shade off the dark.
        k.Use("wood_grey", Deal, 0.95f, 0, tile: 2).Tint = Deal * 0.2f;
        foreach (float z in new[] { -0.32f, 0.3f })
            k.Box(new Vector3(-half, 0.056f, z - 0.05f), new Vector3(half, 0.06f, z + 0.05f));
        // The boards lifted out, beside the gap.
        for (int i = 0; i < 3; i++)
        {
            k.Use("wood_floor", Deal, 0.9f, 0.03f, tile: 1.6f).Tint = Deal * (0.62f + 0.06f * i);
            float x0 = half + 0.12f + i * (BoardWidth + 0.03f);
            float lift = i == 2 ? 0.06f : 0;
            k.Quad(new Vector3(x0, 0.024f, -BoardsHalf + 0.05f * i), new Vector3(x0 + BoardWidth, 0.024f + lift, -BoardsHalf + 0.05f * i),
                new Vector3(x0 + BoardWidth, 0.024f + lift, BoardsHalf + 0.05f * i), new Vector3(x0, 0.024f, BoardsHalf + 0.05f * i));
            k.Box(new Vector3(x0, 0, -BoardsHalf + 0.05f * i), new Vector3(x0 + BoardWidth, 0.024f, BoardsHalf + 0.05f * i), top: false);
        }
        return k.Build("underfloor-open");
    }

    /// <param name="clutter">What a ransack left about it (StopWalls.ClutterOf).</param>
    /// <param name="nest">Where the Gaunt nests in it, if it does (StopWalls.Nest).</param>
    public static void OpenHouse(Kit k, Sim.Stops.StopBuilding b, IEnumerable<Sim.Stops.StopContainer> kept,
        IReadOnlyList<Sim.Run.Clutter>? clutter = null, (double X, double Y)? nest = null)
    {
        static Vector3 K(double x, double y, float h) => new((float)y, h, (float)-x);
        // A box in the building's frame: (x, y) its middle, (hx, hy) half its size, from y0 to y1 up.
        void Box(double x, double y, double hx, double hy, float y0, float y1)
        {
            var a = K(x - hx, y - hy, y0);
            var c = K(x + hx, y + hy, y1);
            k.Box(Vector3.Min(a, c), Vector3.Max(a, c));
        }
        const float Floor = 0.17f, H = 3.0f, Lintel = 2.15f;
        double hx = b.Length / 2, hy = b.Width / 2, t = Sim.Run.StopWalls.WallThickness;
        var (fx, fy) = Sim.Run.StopWalls.Front(b);

        bool composite = Sim.Run.StopWalls.Composite(b);
        var outline = composite ? Sim.Run.StopWalls.Outline(b) : null;
        k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0, tile: 1);
        // The frame stands 0.15 m under the ground (WorldArt.Building), so the boards are just over it. An L's or a cross's
        // floor is its outline's cells, which don't overlap.
        if (outline is null)
            Box(0, 0, hx - t, hy - t, -0.4f, Floor);
        else
            foreach (var (x0, y0, x1, y1) in outline.Cells)
                Box((x0 + x1) / 2, (y0 + y1) / 2, (x1 - x0) / 2, (y1 - y0) / 2, -0.4f, Floor);
        k.Use("plaster_ruin", Palette.BlueGrey, 0.9f, 0.05f, tile: 2);
        foreach (var (x, y, wx, wy) in Sim.Run.StopWalls.OpenWalls(b))
            Box(x, y, wx, wy, -0.4f, H);
        // Over the doorway (each unit's).
        double door = Sim.Run.StopWalls.DoorWidth / 2;
        if (outline is not null)
            foreach (var r in outline.Doors.Select(i => outline.Runs[i]))
            {
                double mid = (r.A + r.B) / 2, across = r.At - r.Out * t / 2;
                if (r.AlongX)
                    Box(mid, across, door, t / 2, Lintel, H);
                else
                    Box(across, mid, t / 2, door, Lintel, H);
            }
        else if (fx != 0)
            Box(fx * (hx - t / 2), 0, t / 2, door, Lintel, H);
        // And over the way through to the back room.
        if (outline?.Partition is { } inner)
        {
            var (dx, dy) = inner.Doorway;
            if (inner.AlongX)
                Box(dx, dy, door, t / 2, Lintel, H);
            else
                Box(dx, dy, t / 2, door, Lintel, H);
            // Across a plain house it goes up into the gable under the ridge, so the back room's its own in the dark too.
            if (b.Parts.Count <= 1 && !inner.AlongX)
            {
                float px = (float)inner.At, w = (float)b.Width, ridge = H + w * 0.42f;
                var (l, top, r) = (K(px, -w / 2, H), K(px, 0, ridge), K(px, w / 2, H));
                k.Tri(l, top, r, new(-w / 2, -H), new(0, -ridge), new(w / 2, -H));
                k.Tri(r, top, l, new(w / 2, -H), new(0, -ridge), new(-w / 2, -H));
            }
        }
        else if (outline is null && fx == 0)
            // (A plain house's door in its side: its lintel. Not one in its end, which has its own above: drawn here as well, it
            // hung in the middle of the room.)
            Box(0, fy * (hy - t / 2), door, t / 2, Lintel, H);

        BoardedWindows(k, b, Box);

        // A gabled roof over each part, its ridge along the part's longer side (a plain house's along its axis).
        if (outline is null)
            Gabled(k, 0, 0, (float)b.Width, (float)b.Length, true, H);
        else
            foreach (var part in b.Parts)
                Gabled(k, (float)part.Y, (float)-part.X, (float)Math.Min(part.Length, part.Width), (float)Math.Max(part.Length, part.Width), part.Length >= part.Width, H);

        // What a ransack left (the director, 8 Oct): each piece where the sim has it, turned its way (its x along the house's
        // yaw from x toward y; the kit's frame has the house's x on −Z and its y on +X).
        foreach (var c in clutter ?? [])
        {
            float yaw = (float)c.Yaw;
            var along = new Vector3(MathF.Sin(yaw), 0, -MathF.Cos(yaw));
            var front = new Vector3(MathF.Cos(yaw), 0, MathF.Sin(yaw));
            var at = K(c.X, c.Y, Floor);
            var m = new Matrix4x4(along.X, along.Y, along.Z, 0, 0, 1, 0, 0, front.X, front.Y, front.Z, 0, at.X, at.Y, at.Z, 1);
            k.With(m, () => RansackKit.Piece(k, c));
        }
        // The Gaunt's nest, if it roosts here: in the dark, nothing lit.
        if (nest is { } n)
            k.With(Matrix4x4.CreateTranslation(K(n.X, n.Y, Floor)), () => RansackKit.Nest(k, b.Variant));

        // Its light (HouseLight): a candle stub on a saucer, the wax run down it, its flame; or a tin lamp on a chain, its
        // glass barely lit.
        if (HouseLight(b, kept, clutter, nest) is var (lx, ly, height, lamp))
        {
            var at = K(lx, ly, Floor);
            if (lamp)
            {
                k.Use("rust_heavy", Palette.SootBlack, 0.7f, 0.4f);
                k.Rod(at + new Vector3(0, H, 0), at + new Vector3(0, height + 0.16f, 0), 0.006f);
                k.Use("rust_heavy", Palette.IronGrey, 0.7f, 0.4f);
                k.Lathe(at + new Vector3(0, height - 0.12f, 0), [new(0.07f, 0), new(0.08f, 0.03f), new(0.06f, 0.06f)], 8, smooth: false);
                k.Lathe(at + new Vector3(0, height + 0.06f, 0), [new(0.06f, 0), new(0.04f, 0.05f), new(0.015f, 0.1f)], 8, smooth: false);
                k.Use("lantern_pane", Palette.LampAmber * 0.5f, 0, 0, tile: 0.25f);
                k.Emissive = 1;
                k.Cylinder(at + new Vector3(0, height - 0.06f, 0), at + new Vector3(0, height + 0.06f, 0), 0.045f, 8, caps: false);
                k.Emissive = 0;
            }
            else
            {
                k.Use("rust_heavy", Palette.IronGrey, 0.7f, 0.4f);
                k.Cylinder(at, at + new Vector3(0, 0.012f, 0), 0.06f, 8);
                k.Use("plaster_ruin", new Vector3(0.62f, 0.58f, 0.48f), 0.6f, 0, tile: 0.2f);
                k.Cylinder(at + new Vector3(0, 0.012f, 0), at + new Vector3(0, height - 0.05f, 0), 0.022f, 7);
                k.Cylinder(at + new Vector3(0.012f, 0.012f, 0.008f), at + new Vector3(0.012f, 0.05f, 0.008f), 0.016f, 5);
                k.Use("lamp_lens", Palette.LampAmber, 0, 0, tile: 0.25f);
                k.Emissive = 1;
                k.Cylinder(at + new Vector3(0, height - 0.05f, 0), at + new Vector3(0, height, 0), 0.008f, 5, radiusB: 0.001f);
                k.Emissive = 0;
            }
        }

        // A piece of furniture in its own frame (note 760): u out from the wall it stands against into the room (its face at
        // +depth), w across its face, h up off the boards; drawn in the building's frame by which way it faces.
        void Piece(double cx, double cy, double fx, double fy, double u, double w, double hu, double hw, float h0, float h1) =>
            Box(cx + fx * u - fy * w, cy + fy * u + fx * w, Math.Abs(fx) * hu + Math.Abs(fy) * hw, Math.Abs(fy) * hu + Math.Abs(fx) * hw, Floor + h0, Floor + h1);
        Vector3 PieceAt(double cx, double cy, double fx, double fy, double u, double w, float h) => K(cx + fx * u - fy * w, cy + fy * u + fx * w, Floor + h);
        // A door or a drawer front on a face at depth d (as FrontPiece draws one on its own: a searched one swung open): a
        // frame of stiles and rails round its panel, set back, from h0 to h1 and w0 to w1 across.
        void Front(double cx, double cy, double fx, double fy, double d, double w0, double w1, float h0, float h1, float frame)
        {
            double wm = (w0 + w1) / 2, hw = (w1 - w0) / 2;
            k.Use("wood_floor", Painted, 0.85f, 0.05f, tile: PanelTile).Tint = Painted * PanelShade;
            Piece(cx, cy, fx, fy, d + 0.004, wm, 0.004, hw - 0.004, h0 + 0.004f, h1 - 0.004f);
            k.Use("wood_grey", Painted, 0.8f, 0.05f, tile: FrontTile).Tint = Painted * FrameShade;
            Piece(cx, cy, fx, fy, d + 0.013, wm, 0.005, hw, h0, h0 + frame);
            Piece(cx, cy, fx, fy, d + 0.013, wm, 0.005, hw, h1 - frame, h1);
            Piece(cx, cy, fx, fy, d + 0.013, w0 + frame / 2, 0.005, frame / 2, h0 + frame, h1 - frame);
            Piece(cx, cy, fx, fy, d + 0.013, w1 - frame / 2, 0.005, frame / 2, h0 + frame, h1 - frame);
        }
        void Knob(double cx, double cy, double fx, double fy, double d, double w, float h)
        {
            k.Use("rust_heavy", Palette.IronGrey, 0.7f, 0.35f, tile: 1);
            k.Cylinder(PieceAt(cx, cy, fx, fy, d + 0.018, w, h), PieceAt(cx, cy, fx, fy, d + 0.036, w, h), 0.012f, 6, radiusB: 0.016f);
        }
        // The kitchen dresser a find's kept in (the director, 14 Oct, `docs/playtest/2026-10-14.md` line 21: "Cupboards that
        // definitely need a redesign"): on a plinth, two panelled doors below a worktop, two over it, a cornice; iron knobs
        // and hinges; the carcass dark, its doors a worn milk paint. Its face stays inside the sim's 1.0 x 0.5 x 1.9 box but
        // for the worktop's and the cornice's lip and the knobs (the scene's searched one covers them, GreyboxScene).
        void Dresser(double cx, double cy, double fx, double fy)
        {
            const double D = Sim.Run.StopWalls.CupboardDepth, W = Sim.Run.StopWalls.CupboardWidth;
            k.Use("wood_grey", Stained, 0.9f, 0.02f, tile: CarcassTile).Tint = Stained * 0.65f;
            Piece(cx, cy, fx, fy, -0.01, 0, D - 0.04, W - 0.03, 0, 0.08f);
            k.Use("wood_grey", Stained, 0.85f, 0.05f, tile: CarcassTile).Tint = Stained;
            Piece(cx, cy, fx, fy, -0.005, 0, D - 0.005, W, 0.08f, 1.8f);
            // The worktop's edge between the two halves, and the cornice: proud of the face.
            k.Use("wood_grey", Stained, 0.8f, 0.08f, tile: CarcassTile).Tint = Stained * 1.15f;
            Piece(cx, cy, fx, fy, 0.01, 0, D + 0.01, W + 0.015, 0.9f, 0.95f);
            Piece(cx, cy, fx, fy, 0.012, 0, D + 0.012, W + 0.02, 1.8f, 1.86f);
            Piece(cx, cy, fx, fy, 0.02, 0, D + 0.02, W + 0.03, 1.86f, 1.9f);
            foreach (double s in new[] { -1.0, 1.0 })
            {
                double w0 = s < 0 ? -W + DoorInset : DoorGap, w1 = s < 0 ? -DoorGap : W - DoorInset;
                foreach (var (h0, h1) in DresserDoors)
                    Front(cx, cy, fx, fy, D - 0.005, w0, w1, h0, h1, DoorFrame);
                Knob(cx, cy, fx, fy, D - 0.005, s * 0.06, 0.6f);
                Knob(cx, cy, fx, fy, D - 0.005, s * 0.06, 1.18f);
                // The hinges at the outer edges, two a door.
                k.Use("rust_heavy", Palette.RustRed, 0.8f, 0.3f, tile: 1).Tint = new Vector3(0.7f, 0.55f, 0.5f);
                foreach (float h in new[] { 0.2f, 0.74f, 1.08f, 1.64f })
                    Piece(cx, cy, fx, fy, D + 0.012, s * (W - 0.02), 0.004, 0.012, h, h + 0.05f);
            }
        }
        // The low cabinet a find's kept in: a sideboard on a plinth, a wide drawer under its top (where the scene's searched
        // one pulls it out), two panelled doors under that, the top lipped.
        void Sideboard(double cx, double cy, double fx, double fy)
        {
            const double D = Sim.Run.StopWalls.CabinetDepth, W = Sim.Run.StopWalls.CabinetWidth;
            k.Use("wood_grey", Stained, 0.9f, 0.02f, tile: CarcassTile).Tint = Stained * 0.65f;
            Piece(cx, cy, fx, fy, -0.01, 0, D - 0.035, W - 0.025, 0, 0.07f);
            k.Use("wood_grey", Stained, 0.85f, 0.05f, tile: CarcassTile).Tint = Stained;
            Piece(cx, cy, fx, fy, -0.005, 0, D - 0.005, W, 0.07f, 0.95f);
            k.Use("wood_grey", Stained, 0.8f, 0.08f, tile: CarcassTile).Tint = Stained * 1.15f;
            Piece(cx, cy, fx, fy, 0.012, 0, D + 0.012, W + 0.02, 0.95f, 1.0f);
            Front(cx, cy, fx, fy, D - 0.005, -W + DrawerInset, W - DrawerInset, DrawerFrom, DrawerTo, DrawerFrame);
            Knob(cx, cy, fx, fy, D - 0.005, -0.18, 0.72f);
            Knob(cx, cy, fx, fy, D - 0.005, 0.18, 0.72f);
            foreach (double s in new[] { -1.0, 1.0 })
            {
                Front(cx, cy, fx, fy, D - 0.005, s < 0 ? -W + DoorInset : DoorGap, s < 0 ? -DoorGap : W - DoorInset, 0.1f, 0.6f, 0.055f);
                Knob(cx, cy, fx, fy, D - 0.005, s * 0.05, 0.42f);
            }
        }

        // A spot's own piece laid on the boards at (x, y), its z out the way it faces (TownKit.CellarHatch's frame).
        Matrix4x4 Laid(double x, double y, double faceX, double faceY)
        {
            var into = Vector3.Normalize(K(faceX, faceY, 0));
            var side = Vector3.Cross(Vector3.UnitY, into);
            var at = K(x, y, Floor);
            return new Matrix4x4(side.X, side.Y, side.Z, 0, 0, 1, 0, 0, into.X, into.Y, into.Z, 0, at.X, at.Y, at.Z, 1);
        }

        // What the finds are kept in, each where the sim puts its find (its thin side to the wall it stands against).
        foreach (var c in kept)
        {
            // The find lies out in the room; a cupboard stands behind it against the back wall, a cabinet against the side,
            // as solid as the walls (StopWalls.Furniture; the scene draws a searched one opened).
            var (x, y, faceX, faceY) = Sim.Run.StopWalls.Kept(b, c);
            // The boards run the way the spot faces into the room (a plain house's toward its door).
            bool endOn = Math.Abs(faceX) > 0.5;
            switch (c.Kind)
            {
                case Sim.Stops.ContainerKind.Cupboard or Sim.Stops.ContainerKind.Cabinet:
                    var piece = Sim.Run.StopWalls.Furniture(b, [c]).Single();
                    if (c.Kind == Sim.Stops.ContainerKind.Cupboard)
                        Dresser(piece.X, piece.Y, faceX, faceY);
                    else
                        Sideboard(piece.X, piece.Y, faceX, faceY);
                    break;
                case Sim.Stops.ContainerKind.Cellar:
                    // Its trapdoor let into the floor, hinged on the wall's side (note 991).
                    k.Append(CellarHatch(k.Look), Laid(x, y, faceX, faceY));
                    break;
                case Sim.Stops.ContainerKind.UnderFloor:
                    // A short run of loose boards, one sprung (note 991).
                    k.Append(LooseBoards(k.Look), Laid(x, y, faceX, faceY));
                    break;
            }
        }
    }
}
