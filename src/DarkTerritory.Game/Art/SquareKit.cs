using System.Numerics;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

/// <summary>
/// A fortress town's square (GDD §3.1; ARCHITECTURE §8 note 281): the three buildings backed onto its wall, the custom's
/// centrepiece in the middle of it, the notice board, the plaque, benches, lamp posts. Each piece fronts −Z, centred on
/// its origin, its footprint the one <see cref="Sim.Towns.TownFixtures.Size"/> gives the walls (X along the line, Z across).
/// First-pass art (L1): kit boxes and lathes, readable at night in the fog, telling one town's square from the next by
/// its centrepiece's silhouette.
/// </summary>
public static class SquareKit
{
    /// <summary>
    /// One of the square's buildings, <paramref name="w"/> along its front by <paramref name="d"/> deep: the custom's hall in
    /// its <paramref name="style"/> (the custom's towns.json <c>hallStyle</c>: a white clapboard "church" with a steeple over
    /// its door, a one-room "school" with its belfry, a board-and-batten car "shed", or a painted clapboard "hall"), the
    /// clerk's office or the stores (clapboard). Maritime forms (the director, 7 Oct 2026; note 281). A board over the door,
    /// a lamp beside it, lamplight in the windows. Everything stands inside its footprint, the box the town's walls give it.
    /// </summary>
    public static MeshAsset Building(Look? look, string kind, float w, float d, string style = "")
    {
        var k = new Kit(look, 2100 + kind.Length + style.Length * 7);
        bool hall = kind == "hall";
        string form = hall ? (style is "church" or "school" or "shed" ? style : "hall") : style == "council" ? "council" : kind;
        if (form == "quiet")
            return Quiet(look, w, d);
        float h = form switch { "church" => 5.4f, "hall" => 6.2f, "shed" => 5.2f, "school" => 4.2f, "council" => 6.0f, _ => 4.0f };
        // The walls: clapboard in the form's paint, or a shed's upright boards.
        var paint = form switch
        {
            "church" => new Vector3(0.78f, 0.76f, 0.70f),
            "school" => new Vector3(0.46f, 0.16f, 0.12f),
            "shed" => new Vector3(0.36f, 0.30f, 0.26f),
            "hall" => new Vector3(0.36f, 0.42f, 0.46f),
            "council" => new Vector3(0.72f, 0.70f, 0.64f),
            _ => new Vector3(0.62f, 0.50f, 0.26f),
        };
        bool clap = form != "shed";
        Walls(k, paint, clap);
        k.Box(new Vector3(-w / 2, -0.4f, -d / 2), new Vector3(w / 2, h, d / 2), Kit.Faces.Sides);
        // Gable ends, and a slate roof over them.
        float ridge = h + d * (form == "church" ? 0.55f : 0.4f);
        foreach (float x in new[] { -w / 2, w / 2 })
        {
            var a = new Vector3(x, h, -d / 2);
            var b = new Vector3(x, h, d / 2);
            var c = new Vector3(x, ridge, 0);
            Vector2 Uv(Vector3 p, float flip) => new(flip * p.Z, -p.Y);
            if (x < 0)
                k.Tri(a, c, b, Uv(a, 1), Uv(c, 1), Uv(b, 1));
            else
                k.Tri(b, c, a, Uv(b, -1), Uv(c, -1), Uv(a, -1));
        }
        // Corner boards on the clapboard.
        if (clap)
        {
            k.Use("paint_black", new Vector3(0.80f, 0.78f, 0.72f), 0.6f, 0.1f);
            k.Tint = new Vector3(0.80f, 0.78f, 0.72f);
            foreach (float x in new[] { -w / 2, w / 2 })
                foreach (float z in new[] { -d / 2, d / 2 })
                    k.Box(new Vector3(x - 0.07f, -0.3f, z - 0.07f), new Vector3(x + 0.07f, h, z + 0.07f));
        }
        k.Use("roof_slate", Palette.Charcoal, 0.9f, 0.15f, tile: 1.5f);
        const float over = 0.4f;
        var top = new Vector3(0, ridge + 0.05f, 0);
        var front = new Vector3(0, h - over * 0.8f, -d / 2 - over);
        var rear = new Vector3(0, h - over * 0.8f, d / 2 + over);
        k.Quad(top with { X = -w / 2 - over }, top with { X = w / 2 + over }, front with { X = w / 2 + over }, front with { X = -w / 2 - over }, twoSided: true);
        k.Quad(top with { X = w / 2 + over }, top with { X = -w / 2 - over }, rear with { X = -w / 2 - over }, rear with { X = w / 2 + over }, twoSided: true);
        // The church's tower over its door, flush with the front, its spire; the school's belfry on the ridge.
        if (form == "church")
            Steeple(k, d, h, ridge, paint);
        if (form == "school")
            Belfry(k, ridge, paint);
        // The council house: a cupola on the ridge with the town's clock in it, a portico over the door (note 353).
        if (form == "council")
        {
            Belfry(k, ridge, paint);
            k.Use("paper_form", Palette.BoardEnamel, 0.5f, 0.2f, tile: 1);
            k.Emissive = 0.3f;
            k.Disc(new Vector3(0, ridge + 0.05f, -0.62f), -Vector3.UnitZ, 0.42f, 16);
            k.Emissive = 0;
            k.Use("paint_black", Palette.SootBlack, 0.6f, 0.1f);
            k.Box(new Vector3(-0.02f, ridge + 0.05f, -0.64f), new Vector3(0.02f, ridge + 0.36f, -0.63f));
            k.Box(new Vector3(-0.02f, ridge + 0.03f, -0.64f), new Vector3(0.24f, ridge + 0.07f, -0.63f));
            Walls(k, paint, true);
            foreach (float x in new[] { -1.3f, 1.3f })
                k.Cylinder(new Vector3(x, -0.1f, -d / 2 - 1.6f), new Vector3(x, 3.3f, -d / 2 - 1.6f), 0.16f, 10);
            k.Box(new Vector3(-1.7f, 3.3f, -d / 2 - 1.9f), new Vector3(1.7f, 3.6f, -d / 2));
            k.Tri(new Vector3(1.7f, 3.6f, -d / 2 - 1.9f), new Vector3(-1.7f, 3.6f, -d / 2 - 1.9f), new Vector3(0, 4.4f, -d / 2 - 1.9f), new(1, 0), new(0, 0), new(0.5f, -0.4f));
            k.Use("stone_block", Palette.Ballast, 0.7f, 0.1f, tile: 1);
            k.Box(new Vector3(-1.8f, -0.4f, -d / 2 - 2.1f), new Vector3(1.8f, 0.15f, -d / 2));
        }
        // The door, and windows either side of it, a lamp burning behind most: a church's tall and pointed, a shed's
        // small and high either side of its big doors.
        bool bay = form is "hall" or "shed";
        k.Use("glass_dirty", Palette.SootBlack, 0.4f, 0.4f, tile: 1);
        k.Shade(0.3f);
        k.Doorway(new Vector3(0, -0.1f, -d / 2 - (form == "church" ? 0.05f : 0.01f)), -Vector3.UnitZ, form switch { "shed" => 3.2f, "hall" => 1.8f, _ => 1.0f }, bay: bay);
        k.Use("window_lit", Palette.LampAmber, 0.1f, 0.3f, tile: 1);
        k.Emissive = form == "church" ? 0.6f : 1;
        float clear = form switch { "church" => 2.2f, "shed" => 2.4f, "hall" => 1.6f, _ => 1.0f };
        int storeys = form is "hall" or "council" ? 2 : 1;
        for (int s = 0; s < storeys; s++)
            for (float x = -w / 2 + 1.4f; x < w / 2 - 1.0f; x += 2.2f)
            {
                if (MathF.Abs(x) <= clear && s == 0 || form == "church" && MathF.Abs(x) <= clear)
                    continue;
                var at = new Vector3(x, 1.5f + s * 2.8f, -d / 2 - 0.02f);
                if (form == "church")
                    Lancet(k, at with { Y = 2.1f });
                else if (form == "shed")
                    k.Panel(at with { Y = 3.4f }, -Vector3.UnitZ, Vector3.UnitY, 0.7f, 0.5f, Vector2.Zero, Vector2.One);
                else
                    k.Panel(at, -Vector3.UnitZ, Vector3.UnitY, 0.8f, 1.1f, Vector2.Zero, Vector2.One);
            }
        k.Emissive = 0;
        // The name board over the door (the text is the plan's, read with Use; the board says there's something to read).
        float doorTop = k.DoorHeight(bay);
        k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0, tile: 1);
        k.Box(new Vector3(-1.5f, doorTop + 0.25f, -d / 2 - 0.12f), new Vector3(1.5f, doorTop + 0.85f, -d / 2 - 0.02f));
        k.Use("paint_black", Palette.SootBlack, 0.6f, 0.1f);
        k.Box(new Vector3(-1.3f, doorTop + 0.38f, -d / 2 - 0.14f), new Vector3(1.3f, doorTop + 0.72f, -d / 2 - 0.12f), Kit.Faces.NegZ);
        // The lamp beside the door, on a bracket.
        k.Use("rust_heavy", Palette.IronGrey, 0.8f, 0.3f);
        var lamp = Lamp(d, hall);
        k.Rod(lamp + new Vector3(0, 0.25f, 0.35f), lamp + new Vector3(0, 0.25f, 0), 0.03f);
        k.Use("lamp_lens", Palette.LampAmber, 0, 0, tile: 0.25f);
        k.Emissive = 1;
        k.BoxAt(lamp, new Vector3(0.11f, 0.16f, 0.11f));
        k.Emissive = 0;
        return k.Build($"square-{kind}-{form}-{w:0}x{d:0}");
    }

    /// <summary>
    /// A building's walls in its paint: painted clapboard (its boards across, tools/art's "clapboard"), or a shed's upright
    /// boards (the boxcar siding's).
    /// </summary>
    static void Walls(Kit k, Vector3 paint, bool clap)
    {
        k.Use(clap ? "clapboard" : "wood_siding", paint, 0.85f, 0.05f, tile: clap ? 1.0f : 2.0f);
        if (k.Tint == Vector3.One)
            k.Tint = clap ? paint * (0.75f / 0.26f) : paint * 2.1f;
    }

    /// <summary>A white church's tower over its door, flush with the front wall and inside the footprint, its belfry's louvres, the needle spire.</summary>
    static void Steeple(Kit k, float d, float h, float ridge, Vector3 paint)
    {
        const float half = 1.3f;
        float z0 = -d / 2, z1 = z0 + 2 * half, top = ridge + 1.6f, spire = top + 6.5f;
        Walls(k, paint, true);
        k.Box(new Vector3(-half, -0.4f, z0 - 0.04f), new Vector3(half, top, z1), Kit.Faces.Sides);
        // The belfry: dark louvres on each face below the spire.
        k.Use("paint_black", Palette.SootBlack, 0.7f, 0.1f);
        foreach (var (n, c) in new[] { (-Vector3.UnitZ, new Vector3(0, top - 1.1f, z0 - 0.06f)), (Vector3.UnitX, new Vector3(half + 0.02f, top - 1.1f, (z0 + z1) / 2)), (-Vector3.UnitX, new Vector3(-half - 0.02f, top - 1.1f, (z0 + z1) / 2)) })
            k.Panel(c, n, Vector3.UnitY, 1.2f, 1.3f, Vector2.Zero, Vector2.One);
        // The spire: four faces to a needle point, slate.
        k.Use("roof_slate", Palette.Charcoal, 0.9f, 0.15f, tile: 1.5f);
        var apex = new Vector3(0, spire, (z0 + z1) / 2);
        var c0 = new Vector3(-half - 0.1f, top, z0 - 0.14f);
        var c1 = new Vector3(half + 0.1f, top, z0 - 0.14f);
        var c2 = new Vector3(half + 0.1f, top, z1 + 0.1f);
        var c3 = new Vector3(-half - 0.1f, top, z1 + 0.1f);
        foreach (var (a, b) in new[] { (c1, c0), (c2, c1), (c3, c2), (c0, c3) })
            k.Tri(a, b, apex, new(a.X + a.Z, -a.Y), new(b.X + b.Z, -b.Y), new(0, -apex.Y));
        k.Use("iron_smokebox", Palette.SootBlack, 0.6f, 0.3f, tile: 1);
        k.Rod(apex, apex + Vector3.UnitY * 0.9f, 0.025f);
    }

    /// <summary>A schoolhouse's belfry on its ridge: four posts, the bell between them, a little pyramid roof.</summary>
    static void Belfry(Kit k, float ridge, Vector3 paint)
    {
        const float half = 0.6f;
        float y0 = ridge - 0.2f, y1 = ridge + 1.3f;
        Walls(k, paint, true);
        k.Box(new Vector3(-half, y0 - 0.4f, -half), new Vector3(half, y0 + 0.3f, half), Kit.Faces.Sides);
        k.Use("paint_black", new Vector3(0.80f, 0.78f, 0.72f), 0.6f, 0.1f);
        k.Tint = new Vector3(0.80f, 0.78f, 0.72f);
        foreach (float x in new[] { -half, half - 0.1f })
            foreach (float z in new[] { -half, half - 0.1f })
                k.Box(new Vector3(x, y0 + 0.3f, z), new Vector3(x + 0.1f, y1, z + 0.1f));
        k.Use("brass", Palette.TarnishedBrass, 0.3f, 0.6f);
        k.Cylinder(new Vector3(0, y1 - 0.15f, 0), new Vector3(0, y0 + 0.55f, 0), 0.12f, 10, radiusB: 0.26f);
        k.Use("roof_slate", Palette.Charcoal, 0.9f, 0.15f, tile: 1.5f);
        var apex = new Vector3(0, y1 + 0.9f, 0);
        var p = new[] { new Vector3(-half - 0.2f, y1, -half - 0.2f), new Vector3(half + 0.2f, y1, -half - 0.2f), new Vector3(half + 0.2f, y1, half + 0.2f), new Vector3(-half - 0.2f, y1, half + 0.2f) };
        for (int i = 0; i < 4; i++)
        {
            var a = p[(i + 1) % 4];
            var b = p[i];
            k.Tri(a, b, apex, new(a.X + a.Z, -a.Y), new(b.X + b.Z, -b.Y), new(0, -apex.Y));
        }
    }

    /// <summary>A Gothic Revival lancet: a tall window, its head pointed, in white trim.</summary>
    static void Lancet(Kit k, Vector3 at)
    {
        const float hw = 0.36f, hh = 0.85f;
        k.Panel(at, -Vector3.UnitZ, Vector3.UnitY, hw * 2, hh * 2, Vector2.Zero, Vector2.One);
        var l = at + new Vector3(-hw, hh, 0);
        var r = at + new Vector3(hw, hh, 0);
        var point = at + new Vector3(0, hh + 0.55f, 0);
        k.Tri(r, l, point, new(1, 0), new(0, 0), new(0.5f, -0.3f));
    }

    /// <summary>
    /// The quiet house (note 353): small, windowless, whitewashed, its door barred on the outside, a slate by it that says
    /// QUIET, a bowl and a folded blanket on the step. No lamp: nobody wants to see in.
    /// </summary>
    static MeshAsset Quiet(Look? look, float w, float d)
    {
        var k = new Kit(look, 2177);
        const float h = 3.2f;
        Walls(k, new Vector3(0.70f, 0.70f, 0.68f), true);
        k.Box(new Vector3(-w / 2, -0.4f, -d / 2), new Vector3(w / 2, h, d / 2), Kit.Faces.Sides);
        foreach (float x in new[] { -w / 2, w / 2 })
        {
            var a = new Vector3(x, h, -d / 2);
            var b = new Vector3(x, h, d / 2);
            var c = new Vector3(x, h + d * 0.35f, 0);
            if (x < 0)
                k.Tri(a, c, b, new(a.Z, -a.Y), new(c.Z, -c.Y), new(b.Z, -b.Y));
            else
                k.Tri(b, c, a, new(-b.Z, -b.Y), new(-c.Z, -c.Y), new(-a.Z, -a.Y));
        }
        k.Use("roof_slate", Palette.Charcoal, 0.9f, 0.15f, tile: 1.5f);
        var top = new Vector3(0, h + d * 0.35f + 0.05f, 0);
        foreach (float z in new[] { -d / 2 - 0.3f, d / 2 + 0.3f })
        {
            var eave = new Vector3(0, h - 0.25f, z);
            if (z < 0)
                k.Quad(top with { X = -w / 2 - 0.3f }, top with { X = w / 2 + 0.3f }, eave with { X = w / 2 + 0.3f }, eave with { X = -w / 2 - 0.3f }, twoSided: true);
            else
                k.Quad(top with { X = w / 2 + 0.3f }, top with { X = -w / 2 - 0.3f }, eave with { X = -w / 2 - 0.3f }, eave with { X = w / 2 + 0.3f }, twoSided: true);
        }
        // The door, shut, an iron bar across it in its brackets, outside.
        k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0, tile: 1);
        k.Box(new Vector3(-0.5f, -0.1f, -d / 2 - 0.06f), new Vector3(0.5f, 2.0f, -d / 2 - 0.01f));
        k.Use("rust_heavy", Palette.IronGrey, 0.8f, 0.4f);
        k.Box(new Vector3(-0.75f, 1.05f, -d / 2 - 0.16f), new Vector3(0.75f, 1.15f, -d / 2 - 0.08f));
        foreach (float x in new[] { -0.68f, 0.68f })
            k.Box(new Vector3(x - 0.05f, 0.98f, -d / 2 - 0.18f), new Vector3(x + 0.05f, 1.22f, -d / 2 - 0.01f));
        // The slate on the wall beside it: QUIET, chalked.
        k.Use("roof_slate", Palette.Charcoal, 0.6f, 0.2f, tile: 1);
        k.Box(new Vector3(0.75f, 1.3f, -d / 2 - 0.04f), new Vector3(1.35f, 1.7f, -d / 2 - 0.01f));
        k.Use("paper_form", Palette.BoardEnamel, 0.7f, 0, tile: 1);
        k.Box(new Vector3(0.82f, 1.47f, -d / 2 - 0.05f), new Vector3(1.28f, 1.53f, -d / 2 - 0.045f));
        // The step, the bowl on it, a blanket folded beside.
        k.Use("stone_block", Palette.Ballast, 0.7f, 0.1f, tile: 1);
        k.Box(new Vector3(-0.8f, -0.4f, -d / 2 - 0.6f), new Vector3(0.8f, 0.12f, -d / 2));
        k.Use("iron_plate", Palette.IronGrey, 0.6f, 0.4f, tile: 1);
        k.Lathe(new Vector3(-0.45f, 0.12f, -d / 2 - 0.3f), [new(0.06f, 0), new(0.11f, 0.06f), new(0.12f, 0.07f)], 10, capTop: false);
        k.Use("wool", Palette.MuddyOlive, 0.8f, 0, tile: 1);
        k.Box(new Vector3(0.1f, 0.12f, -d / 2 - 0.5f), new Vector3(0.6f, 0.24f, -d / 2 - 0.12f));
        return k.Build($"square-quiet-{w:0}x{d:0}");
    }

    /// <summary>Where a building's door lamp hangs (local), for its light.</summary>
    public static Vector3 Lamp(float depth, bool hall) => new(hall ? 1.6f : 1.0f, 2.5f, -depth / 2 - 0.45f);

    /// <summary>
    /// A centrepiece, a board, a plaque, a bench or a crate by <paramref name="kind"/> (the plan's fixture kinds). For the
    /// board, <paramref name="count"/> is how many notices are pinned to it; for the line, how long it runs (m).
    /// </summary>
    public static MeshAsset Fixture(Look? look, string kind, int count = 0)
    {
        // Seeded by the kind's letters, not GetHashCode (seeded afresh each process): screenshots stay the same run to run.
        var k = new Kit(look, 2200 + kind.Sum(c => c) % 97);
        var (hs, hd, ht) = Sim.Towns.TownFixtures.Size(kind);
        float x = (float)hs, z = (float)hd, h = (float)ht;
        switch (kind)
        {
            case "bell":
                // A timber frame, the bell hung in it, its clapper bound in felt (a pale lump).
                Timber(k);
                k.BoxAt(new Vector3(-x + 0.15f, h / 2, 0), new Vector3(0.14f, h / 2, 0.14f));
                k.BoxAt(new Vector3(x - 0.15f, h / 2, 0), new Vector3(0.14f, h / 2, 0.14f));
                k.BoxAt(new Vector3(0, h - 0.15f, 0), new Vector3(x, 0.15f, 0.16f));
                Iron(k);
                k.Lathe(new Vector3(0, h - 1.5f, 0), [new(0.62f, 0), new(0.58f, 0.15f), new(0.42f, 0.6f), new(0.3f, 1.05f), new(0.08f, 1.2f)], 12);
                k.Use("wool", Palette.BoardEnamel, 0.9f, 0);
                k.BoxAt(new Vector3(0, h - 1.55f, 0), new Vector3(0.14f, 0.18f, 0.14f));
                break;
            case "post":
                Timber(k);
                k.BoxAt(new Vector3(0, h / 2, 0), new Vector3(0.12f, h / 2, 0.12f));
                Iron(k);
                for (int i = 0; i < 4; i++)
                {
                    float y = 0.9f + i * 0.35f, a = i * MathF.PI / 2;
                    var c = new Vector3(MathF.Sin(a) * 0.16f, y, MathF.Cos(a) * 0.16f);
                    k.BoxAt(c, new Vector3(0.07f, 0.07f, 0.02f));
                    k.Use("leather", Palette.DeepBrown, 0.9f, 0);
                    k.Rod(c, c + new Vector3(MathF.Sin(a) * 0.12f, -0.45f, MathF.Cos(a) * 0.12f), 0.015f);
                    k.Rod(c, c + new Vector3(MathF.Sin(a + 0.4f) * 0.12f, -0.5f, MathF.Cos(a + 0.4f) * 0.12f), 0.015f);
                    Iron(k);
                }
                break;
            case "tally":
                Timber(k);
                Legs(k, x, h - 0.4f, 0.08f);
                k.Use("paint_black", Palette.SootBlack, 0.7f, 0.1f);
                k.Box(new Vector3(-x, 0.9f, -0.06f), new Vector3(x, h, 0.06f));
                // Chalk: a column of names, and rows of marks against them.
                k.Use("paper_form", Palette.BoardEnamel, 0.9f, 0);
                k.Emissive = 0.15f;
                for (int r = 0; r < 9; r++)
                {
                    float y = h - 0.2f - r * 0.15f;
                    k.Box(new Vector3(-x + 0.1f, y, -0.075f), new Vector3(-x + 0.5f, y + 0.03f, -0.065f), Kit.Faces.NegZ);
                    for (int m = 0; m < 2 + (r * 7 % 5); m++)
                        k.Box(new Vector3(-x + 0.65f + m * 0.09f, y - 0.03f, -0.075f), new Vector3(-x + 0.67f + m * 0.09f, y + 0.07f, -0.065f), Kit.Faces.NegZ);
                }
                k.Emissive = 0;
                break;
            case "lamps":
                // Racks of lanterns, a tin shutter shut over each one's glass.
                Timber(k);
                Legs(k, x, h, 0.07f);
                foreach (float y in new[] { 0.7f, 1.5f })
                {
                    k.Box(new Vector3(-x, y - 0.05f, -z), new Vector3(x, y, z));
                    for (float lx = -x + 0.3f; lx < x - 0.2f; lx += 0.45f)
                    {
                        Iron(k);
                        k.BoxAt(new Vector3(lx, y + 0.2f, 0), new Vector3(0.1f, 0.2f, 0.1f));
                        k.Use("lamp_lens", Palette.LampAmber, 0, 0, tile: 0.25f);
                        k.Emissive = 0.4f;
                        k.Panel(new Vector3(lx, y + 0.2f, 0.11f), Vector3.UnitZ, Vector3.UnitY, 0.12f, 0.2f);
                        k.Emissive = 0;
                        k.Use("rust_heavy", Palette.IronGrey, 0.8f, 0.3f);
                        k.Box(new Vector3(lx - 0.11f, y + 0.05f, -0.13f), new Vector3(lx + 0.11f, y + 0.36f, -0.11f));
                    }
                    Timber(k);
                }
                break;
            case "horn":
                Iron(k);
                k.Cylinder(Vector3.Zero, new Vector3(0, h, 0), 0.07f, 6);
                k.Cylinder(new Vector3(0, h - 0.3f, 0), new Vector3(0, h - 0.4f, -0.9f), 0.12f, 10, radiusB: 0.55f, capB: false);
                k.Use("paint_black", Palette.SootBlack, 0.6f, 0.1f);
                k.BoxAt(new Vector3(0, 1.4f, 0.1f), new Vector3(0.2f, 0.25f, 0.12f));
                break;
            case "mirror":
                Iron(k);
                k.Box(new Vector3(-x, 0, -0.08f), new Vector3(-x + 0.1f, h, 0.08f));
                k.Box(new Vector3(x - 0.1f, 0, -0.08f), new Vector3(x, h, 0.08f));
                k.Box(new Vector3(-x, h - 0.1f, -0.08f), new Vector3(x, h, 0.08f));
                k.Box(new Vector3(-x, 0.3f, -0.08f), new Vector3(x, 0.4f, 0.08f));
                k.Use("glass_dirty", Palette.BlueGrey, 0.05f, 0.9f, tile: 1);
                k.Shade(1.6f);
                k.Panel(new Vector3(0, (h + 0.4f) / 2, -0.03f), -Vector3.UnitZ, Vector3.UnitY, 2 * x - 0.2f, h - 0.5f, Vector2.Zero, Vector2.One);
                // The line, five paces out in front of it.
                Paint(k);
                k.Box(new Vector3(-1.2f, 0.01f, -3.9f), new Vector3(1.2f, 0.03f, -3.7f), Kit.Faces.PosY);
                break;
            case "shelf":
                Timber(k);
                Legs(k, x, h, 0.07f);
                k.Box(new Vector3(-x, h - 0.5f, -z), new Vector3(x, h - 0.45f, z));
                k.Box(new Vector3(-x, h - 0.08f, -z), new Vector3(x, h, z));
                k.Box(new Vector3(-x, 0.4f, z - 0.04f), new Vector3(x, h, z));
                break;
            case "lectern":
                Timber(k);
                k.BoxAt(new Vector3(0, 0.55f, 0), new Vector3(0.12f, 0.55f, 0.12f));
                k.BoxAt(new Vector3(0, 0.05f, 0), new Vector3(0.35f, 0.05f, 0.3f));
                k.Quad(new Vector3(-0.4f, 1.15f, 0.25f), new Vector3(0.4f, 1.15f, 0.25f), new Vector3(0.4f, 1.0f, -0.3f), new Vector3(-0.4f, 1.0f, -0.3f), twoSided: true);
                // The book, open, thick as a brick; its chain down to the post.
                k.Use("paper_form", Palette.BoardEnamel, 0.9f, 0);
                k.Box(new Vector3(-0.3f, 1.08f, -0.2f), new Vector3(0.3f, 1.2f, 0.18f));
                Iron(k);
                k.Rod(new Vector3(0.3f, 1.1f, 0), new Vector3(0.13f, 0.7f, 0), 0.012f);
                break;
            case "brazier":
                Iron(k);
                for (int i = 0; i < 3; i++)
                {
                    float a = i * MathF.Tau / 3;
                    k.Rod(new Vector3(MathF.Sin(a) * 0.55f, 0, MathF.Cos(a) * 0.55f), new Vector3(MathF.Sin(a) * 0.45f, 0.7f, MathF.Cos(a) * 0.45f), 0.04f);
                }
                k.Lathe(new Vector3(0, 0.6f, 0), [new(0.2f, 0), new(0.65f, 0.3f), new(0.78f, 0.5f)], 12, capTop: false);
                k.Use("ember_crack", Palette.FurnaceOrange, 0.5f, 0);
                k.Emissive = 0.7f;
                k.Disc(new Vector3(0, 1.0f, 0), Vector3.UnitY, 0.68f, 12);
                k.Emissive = 0;
                // The tongs, and the poker bent nearly double.
                Iron(k);
                k.Rod(new Vector3(-0.6f, 1.08f, 0.1f), new Vector3(0.1f, 1.05f, 0.2f), 0.02f);
                k.Rod(new Vector3(-0.2f, 1.06f, -0.3f), new Vector3(0.5f, 1.08f, -0.25f), 0.02f);
                k.Rod(new Vector3(0.5f, 1.08f, -0.25f), new Vector3(0.25f, 1.1f, -0.05f), 0.02f);
                break;
            case "line":
                // Down the middle of the square, worn: a white strip, broken where the paint's gone.
                Paint(k);
                for (float lx = -count / 2f; lx < count / 2f; lx += 2.2f)
                    k.Box(new Vector3(lx, 0.01f, -0.15f), new Vector3(lx + 1.9f, 0.025f, 0.15f), Kit.Faces.PosY);
                break;
            case "pegs":
                Timber(k);
                Legs(k, x, h, 0.07f);
                k.Box(new Vector3(-x, h - 0.12f, -0.06f), new Vector3(x, h, 0.06f));
                // Coats hung back to front: the backs, brushed, to the square.
                k.Use("coat_oilskin", Palette.MuddyOlive, 0.8f, 0.2f, tile: 1);
                for (float lx = -x + 0.35f; lx < x - 0.2f; lx += 0.55f)
                    k.Box(new Vector3(lx - 0.22f, h - 1.15f, -0.18f), new Vector3(lx + 0.22f, h - 0.15f, 0.04f));
                break;
            case "bench":
                Timber(k);
                Legs(k, x - 0.1f, 0.45f, 0.05f);
                k.Box(new Vector3(-x, 0.42f, -z), new Vector3(x, 0.48f, z - 0.05f));
                k.Box(new Vector3(-x, 0.48f, z - 0.08f), new Vector3(x, 0.9f, z));
                break;
            case "carriage":
                // The front half of a goods van on blocks, bitten off: a ragged curve, painted white.
                k.Use("stone_block", Palette.Charcoal, 0.8f, 0.1f, tile: 1);
                k.BoxAt(new Vector3(-x + 0.5f, 0.3f, 0), new Vector3(0.4f, 0.3f, z - 0.2f));
                k.BoxAt(new Vector3(0.3f, 0.3f, 0), new Vector3(0.4f, 0.3f, z - 0.2f));
                k.Use("paint_oxide", Palette.RustRed, 0.8f, 0.1f, tile: 2);
                k.Box(new Vector3(-x, 0.6f, -z), new Vector3(0.6f, h - 0.5f, z), Kit.Faces.Sides | Kit.Faces.PosY);
                k.Use("roof_slate", Palette.Charcoal, 0.8f, 0.2f, tile: 1.5f);
                k.Box(new Vector3(-x - 0.1f, h - 0.5f, -z - 0.1f), new Vector3(0.6f, h - 0.35f, z + 0.1f));
                Paint(k);
                for (int i = 0; i < 7; i++)
                {
                    float y = 0.6f + i * (h - 1.1f) / 6, bite = 0.6f + MathF.Sin(i * 1.7f) * 0.35f + (i % 2) * 0.25f;
                    k.Box(new Vector3(0.6f, y, -z - 0.02f), new Vector3(0.6f + bite, y + 0.35f, z + 0.02f));
                }
                break;
            case "cannon":
                Timber(k);
                k.Box(new Vector3(-x + 0.2f, 0.3f, -0.45f), new Vector3(x - 0.3f, 0.75f, 0.45f));
                Iron(k);
                foreach (float wz in new[] { -0.55f, 0.55f })
                    k.Cylinder(new Vector3(-0.3f, 0.45f, wz), new Vector3(-0.3f, 0.45f, wz + (wz < 0 ? -0.1f : 0.1f)), 0.45f, 10);
                k.Cylinder(new Vector3(x - 0.2f, 0.95f, 0), new Vector3(-x, 1.05f, 0), 0.24f, 10, radiusB: 0.18f);
                k.Use("brass", Palette.TarnishedBrass, 0.3f, 0.7f);
                k.Cylinder(new Vector3(-x + 0.25f, 1.04f, 0), new Vector3(-x + 0.1f, 1.05f, 0), 0.22f, 10);
                break;
            case "board":
                // The notice board: two posts, a little roof, the papers pinned up (one panel a notice).
                Timber(k);
                Legs(k, x, h, 0.07f);
                k.Box(new Vector3(-x, 1.0f, -0.05f), new Vector3(x, h - 0.2f, 0.05f));
                k.Use("roof_slate", Palette.Charcoal, 0.8f, 0.2f, tile: 1);
                k.Quad(new Vector3(-x - 0.15f, h, 0.1f), new Vector3(x + 0.15f, h, 0.1f), new Vector3(x + 0.15f, h - 0.2f, -0.35f), new Vector3(-x - 0.15f, h - 0.2f, -0.35f), twoSided: true);
                k.Use("paper_form", Palette.BoardEnamel, 0.9f, 0);
                k.Emissive = 0.2f;
                for (int i = 0; i < count; i++)
                {
                    float px = -x + 0.35f + i * (2 * x - 0.5f) / Math.Max(1, count - 1) * (count > 1 ? 1 : 0.5f);
                    float py = 1.55f + (i % 2) * 0.12f;
                    k.Panel(new Vector3(Math.Min(px, x - 0.3f), py, -0.06f), -Vector3.UnitZ, Vector3.UnitY, 0.32f, 0.44f, Vector2.Zero, Vector2.One);
                }
                k.Emissive = 0;
                break;
            case "plaque":
                Iron(k);
                k.BoxAt(new Vector3(0, h / 2, 0), new Vector3(0.06f, h / 2, 0.06f));
                k.Use("brass", Palette.TarnishedBrass, 0.4f, 0.6f);
                k.Box(new Vector3(-0.45f, h - 0.75f, -0.1f), new Vector3(0.45f, h - 0.1f, -0.06f));
                break;
            case "barrel":
                // An oil drum with a fire in it, for the folk to stand at.
                Iron(k);
                k.Cylinder(Vector3.Zero, new Vector3(0, h - 0.05f, 0), x - 0.03f, 10, capB: false);
                k.Use("ember_crack", Palette.FurnaceOrange, 0.5f, 0);
                k.Emissive = 0.9f;
                k.Disc(new Vector3(0, h - 0.12f, 0), Vector3.UnitY, x - 0.06f, 10);
                k.Emissive = 0;
                break;
            case "stall":
                // A market stall shut for the night: a counter, posts, a canvas awning, sacks under a cloth.
                Timber(k);
                k.Box(new Vector3(-x, 0, -z), new Vector3(x, 0.95f, z));
                Legs(k, x, h - 0.2f, 0.06f);
                k.Box(new Vector3(-x, 0, z - 0.12f), new Vector3(x, h - 0.2f, z));
                k.Use("sac", Palette.MuddyOlive, 0.9f, 0, tile: 1);
                k.Quad(new Vector3(-x - 0.2f, h, z), new Vector3(x + 0.2f, h, z), new Vector3(x + 0.2f, h - 0.5f, -z - 0.5f), new Vector3(-x - 0.2f, h - 0.5f, -z - 0.5f), twoSided: true);
                k.BoxAt(new Vector3(-0.2f, 1.15f, 0), new Vector3(0.45f, 0.2f, 0.5f));
                break;
            case "crate":
                k.Use("wood_crate", Palette.DeepBrown, 0.8f, 0.05f, tile: 1);
                k.Box(new Vector3(-x, 0, -z), new Vector3(x, h, z));
                break;
            default:
                Timber(k);
                k.Box(new Vector3(-x, 0, -z), new Vector3(x, h, z));
                break;
        }
        return k.Build($"square-{kind}-{count}");
    }

    /// <summary>A lamp post at the square's corners: iron, a lantern at the top. <see cref="LampTop"/> is its flame.</summary>
    public static MeshAsset LampPost(Look? look)
    {
        var k = new Kit(look, 2300);
        Iron(k);
        k.Cylinder(Vector3.Zero, new Vector3(0, 3.2f, 0), 0.07f, 6);
        k.Rod(new Vector3(0, 3.1f, 0), new Vector3(0, 3.1f, -0.4f), 0.03f);
        k.Use("lamp_lens", Palette.LampAmber, 0, 0, tile: 0.25f);
        k.Emissive = 1;
        k.BoxAt(LampTop, new Vector3(0.12f, 0.18f, 0.12f));
        k.Emissive = 0;
        return k.Build("square-lamppost");
    }

    public static readonly Vector3 LampTop = new(0, 2.85f, -0.4f);

    /// <summary>A paper lying about the square: a page, flat (<paramref name="standing"/> false) or pinned up.</summary>
    public static MeshAsset Paper(Look? look, bool standing)
    {
        var k = new Kit(look, 2310);
        k.Use("paper_form", Palette.BoardEnamel, 0.9f, 0);
        k.Emissive = 0.25f;
        if (standing)
            k.Panel(new Vector3(0, 0, -0.01f), -Vector3.UnitZ, Vector3.UnitY, 0.24f, 0.32f, Vector2.Zero, Vector2.One);
        else
            k.Panel(new Vector3(0, 0.005f, 0), Vector3.UnitY, -Vector3.UnitZ, 0.24f, 0.32f, Vector2.Zero, Vector2.One);
        k.Emissive = 0;
        return k.Build(standing ? "square-paper-up" : "square-paper-flat");
    }

    static void Timber(Kit k) => k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0, tile: 1);
    static void Iron(Kit k) => k.Use("rust_heavy", Palette.IronGrey, 0.8f, 0.3f);
    static void Paint(Kit k)
    {
        k.Use("paper_form", Palette.BoardEnamel, 0.9f, 0);
        k.Shade(1.2f);
    }

    /// <summary>Two posts at either end of a thing <paramref name="x"/> each way from its middle, <paramref name="h"/> high.</summary>
    static void Legs(Kit k, float x, float h, float half)
    {
        k.Box(new Vector3(-x, 0, -half), new Vector3(-x + 2 * half, h, half));
        k.Box(new Vector3(x - 2 * half, 0, -half), new Vector3(x, h, half));
    }
}
