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
    public static void OpenHouse(Kit k, Sim.Stops.StopBuilding b, IEnumerable<Sim.Stops.StopContainer> kept)
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

        k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0, tile: 1);
        // The frame stands 0.15 m under the ground (WorldArt.Building), so the boards are just over it.
        Box(0, 0, hx - t, hy - t, -0.4f, Floor);
        k.Use("plaster_ruin", Palette.BlueGrey, 0.9f, 0.05f, tile: 2);
        foreach (var (x, y, wx, wy) in Sim.Run.StopWalls.OpenWalls(b))
            Box(x, y, wx, wy, -0.4f, H);
        // Over the doorway.
        double door = Sim.Run.StopWalls.DoorWidth / 2;
        if (fx != 0)
            Box(fx * (hx - t / 2), 0, t / 2, door, Lintel, H);
        else
            Box(0, fy * (hy - t / 2), door, t / 2, Lintel, H);

        // Gables at the ends (the building's x), the ridge along it; both faces, so they read from inside too.
        float w = (float)b.Width, d = (float)b.Length, ridge = H + w * 0.42f;
        foreach (float z in new[] { -d / 2, d / 2 })
        {
            var a = new Vector3(-w / 2, H, z);
            var c = new Vector3(w / 2, H, z);
            var top = new Vector3(0, ridge, z);
            k.Tri(a, top, c, new(a.X, -a.Y), new(top.X, -top.Y), new(c.X, -c.Y));
            k.Tri(c, top, a, new(-c.X, -c.Y), new(-top.X, -top.Y), new(-a.X, -a.Y));
        }
        k.Use("roof_slate", Palette.Charcoal, 0.9f, 0.15f, tile: 1.5f);
        {
            float over = 0.35f, z0 = -d / 2 - over, z1 = d / 2 + over;
            var el = new Vector3(-w / 2 - over, H - over * 0.9f, 0);
            var er = new Vector3(w / 2 + over, H - over * 0.9f, 0);
            var top = new Vector3(0, ridge + 0.05f, 0);
            k.Quad(top with { Z = z1 }, top with { Z = z0 }, el with { Z = z0 }, el with { Z = z1 }, twoSided: true);
            k.Quad(top with { Z = z0 }, top with { Z = z1 }, er with { Z = z1 }, er with { Z = z0 }, twoSided: true);
        }

        // What the finds are kept in, each where the sim puts its find (its thin side to the wall it stands against).
        foreach (var c in kept)
        {
            var (x, y) = Sim.Run.StopWalls.InsideLocal(b, c.Kind, c.Index);
            bool endOn = fx != 0;
            // The find lies out in the room; a cupboard stands behind it against the back wall, a cabinet against the side.
            double sign = c.Index % 2 == 0 ? 1 : -1, back = Sim.Run.StopWalls.FindOut - 0.25, side = Sim.Run.StopWalls.FindOut - 0.22;
            switch (c.Kind)
            {
                case Sim.Stops.ContainerKind.Cupboard:
                    k.Use("wood_grey", Palette.DeepBrown, 0.8f, 0.05f, tile: 1);
                    Box(x - fx * back, y - fy * back, endOn ? 0.25 : 0.5, endOn ? 0.5 : 0.25, Floor, 1.9f);
                    break;
                case Sim.Stops.ContainerKind.Cabinet:
                    k.Use("wood_grey", Palette.RustRed, 0.8f, 0.1f, tile: 1);
                    Box(x - fy * sign * side, y + fx * sign * side, endOn ? 0.42 : 0.22, endOn ? 0.22 : 0.42, Floor, 1.0f);
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
