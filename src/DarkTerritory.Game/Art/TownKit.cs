using System.Numerics;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

/// <summary>
/// Dead settlements beside the line (GDD §30: "dead settlements ... contaminated rural spaces"; the art sheet's
/// "isolated town"): gabled houses of peeling plaster and sagging slate, their windows black, a church with a spire
/// you can see before you can see the church, a windmill with its sails stopped. Read by silhouette in the fog.
/// </summary>
public static class TownKit
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
            var (kx, ky, _, _) = Sim.Run.StopWalls.Kept(b, c.Kind, c.Index);
            var (fx, fy) = Sim.Run.StopWalls.InsideLocal(b, c.Kind, c.Index);
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
        else
            Box(0, fy * (hy - t / 2), door, t / 2, Lintel, H);

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
                k.Use("lamp_lens", Palette.LampAmber * 0.5f, 0, 0, tile: 0.25f);
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

        // What the finds are kept in, each where the sim puts its find (its thin side to the wall it stands against).
        foreach (var c in kept)
        {
            // The find lies out in the room; a cupboard stands behind it against the back wall, a cabinet against the side,
            // as solid as the walls (StopWalls.Furniture; the scene draws a searched one opened).
            var (x, y, faceX, _) = Sim.Run.StopWalls.Kept(b, c.Kind, c.Index);
            // The boards run the way the spot faces into the room (a plain house's toward its door).
            bool endOn = Math.Abs(faceX) > 0.5;
            switch (c.Kind)
            {
                case Sim.Stops.ContainerKind.Cupboard or Sim.Stops.ContainerKind.Cabinet:
                    var piece = Sim.Run.StopWalls.Furniture(b, [c]).Single();
                    bool cupboard = c.Kind == Sim.Stops.ContainerKind.Cupboard;
                    k.Use("wood_grey", cupboard ? Palette.DeepBrown : Palette.RustRed, 0.8f, cupboard ? 0.05f : 0.1f, tile: 1);
                    Box(piece.X, piece.Y, piece.HalfX, piece.HalfY, Floor, cupboard ? 1.9f : 1.0f);
                    break;
                case Sim.Stops.ContainerKind.Cellar:
                    k.Use("wood_grey", Palette.SootBlack, 0.9f, 0, tile: 1);
                    Box(x, y, 0.5, 0.5, Floor, Floor + 0.05f);
                    k.Use("rust_heavy", Palette.RustRed, 0.6f, 0.4f, tile: 1);
                    Box(x, y + 0.3, 0.06, 0.06, Floor + 0.05f, Floor + 0.08f);
                    break;
                case Sim.Stops.ContainerKind.UnderFloor:
                    // A run of boards prised up, standing proud of the floor.
                    k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0, tile: 1);
                    for (int i = 0; i < 3; i++)
                        Box(x + (endOn ? 0 : (i - 1) * 0.18), y + (endOn ? (i - 1) * 0.18 : 0), endOn ? 0.55 : 0.08, endOn ? 0.08 : 0.55, Floor, Floor + 0.03f + i * 0.03f);
                    break;
            }
        }
    }
}
