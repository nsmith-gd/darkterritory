using System.Numerics;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

/// <summary>
/// A walled town's works (queue #90, note 353; GDD §3: "Fortified towns survive behind stone and steel walls ... furnaces,
/// rail yards and warehouses"): its trade at work inside its wall. Where a facility's piece is modelled (tools/models
/// facility_pieces: the mine head's headframe, winding house and tip, the grain elevator, the foundry's shed and stack, the
/// water tower) the works stand it as it is, scaled to the town (<see cref="Prop"/>), its footprint on its fixture's
/// (<see cref="Sim.Towns.TownFixtures.Size"/>, held by TownWorksArtTests); the rest are this kit's: the glasshouses lit from
/// within, the root cellar, heaps of coal and slag, pig iron, the warehouse. Each in its fixture's frame, its front toward −Z.
/// </summary>
public static class WorksKit
{
    /// <summary>The works' kinds this kit draws.</summary>
    public static bool Draws(string kind) => kind is "headframe" or "winding" or "tip" or "elevator" or "glasshouse" or "cellar"
        or "casting" or "slag" or "coal" or "pigs" or "warehouse" or "watertower";

    /// <summary>
    /// A kind that's a facility's modelled piece: its prop, how big it's stood, which way its front (−Z) faces in the rail
    /// frame (along the line: +S; else toward the line), and whether it's centred on its fixture by its mesh's footprint
    /// (the headframe isn't: its legs are on its origin, its ropes run on to the winding house).
    /// </summary>
    public static (string Name, float Scale, bool Along, bool Centred)? Prop(string kind) => kind switch
    {
        "headframe" => ("headframe", 1.5f, true, false),
        "winding" => ("winding_house", 1f, true, true),
        "tip" => ("spoil_heap", 0.55f, false, true),
        "elevator" => ("grain_elevator", 0.8f, false, true),
        "casting" => ("foundry_shed", 0.6f, false, true),
        "watertower" => ("water_tower", 1f, false, true),
        _ => null,
    };

    /// <summary>A mesh's footprint and height in its own space: the middle of its least and most X and Z, and its extent.</summary>
    public static (Vector3 Middle, Vector3 Size) Footprint(MeshAsset mesh)
    {
        var lo = new Vector3(float.MaxValue);
        var hi = new Vector3(float.MinValue);
        foreach (var v in mesh.Vertices)
        {
            lo = Vector3.Min(lo, v.Position);
            hi = Vector3.Max(hi, v.Position);
        }
        return (new Vector3((lo.X + hi.X) / 2, 0, (lo.Z + hi.Z) / 2), hi - lo);
    }

    /// <summary>
    /// The top of a modelled piece's tallest stack, in its own space: its highest vertex (a chimney or a stack is the
    /// tallest thing on a works).
    /// </summary>
    public static Vector3 StackTop(MeshAsset mesh)
    {
        var top = new Vector3(0, float.MinValue, 0);
        foreach (var v in mesh.Vertices)
            if (v.Position.Y > top.Y)
                top = v.Position;
        return top;
    }

    /// <summary>Where a piece's lights burn, in its own frame, and how: the glasshouses' lamps, the furnace's mouth, the slag's glow.</summary>
    public static IEnumerable<(Vector3 At, Vector3 Colour, float Range)> Lights(string kind)
    {
        var (hs, hd, _) = Sim.Towns.TownFixtures.Size(kind);
        float x = (float)hs, z = (float)hd;
        var grow = new Vector3(1.0f, 0.62f, 0.42f);
        switch (kind)
        {
            case "glasshouse":
                yield return (new Vector3(0, 2.4f, -z * 0.5f), grow * 1.6f, 9);
                yield return (new Vector3(0, 2.4f, z * 0.5f), grow * 1.6f, 9);
                break;
            case "casting":
                // The furnace's light through the shed's great doorways, both ends of its front.
                yield return (new Vector3(-x * 0.7f, 2.5f, -z - 1.5f), Palette.FurnaceOrange * 2.2f, 14);
                yield return (new Vector3(x * 0.7f, 2.5f, -z - 1.5f), Palette.FurnaceOrange * 1.6f, 12);
                break;
            case "slag":
                // Over the pool at the toe of its front, where the runs come down (Heap's glow).
                yield return (new Vector3(0, 0.7f, -z * 1.05f), Palette.FurnaceOrange * 1.1f, 7);
                break;
            case "winding":
                yield return (new Vector3(0, 3.5f, -z - 1.2f), Palette.LampAmber * 1.2f, 8);
                break;
            case "warehouse":
                yield return (new Vector3(1.6f, 3.2f, -z - 0.6f), Palette.LampAmber * 1.3f, 8);
                break;
        }
    }

    /// <summary>
    /// The kit's own piece for a kind, the size its fixture is: those it makes, and a plain brick or iron stand-in for a
    /// modelled one where it isn't built (an edition without the props).
    /// </summary>
    public static MeshAsset Piece(Look? look, string kind)
    {
        var k = new Kit(look, 5200 + kind.Sum(c => c) % 97);
        var (hs, hd, ht) = Sim.Towns.TownFixtures.Size(kind);
        float x = (float)hs, z = (float)hd, h = (float)ht;
        switch (kind)
        {
            case "glasshouse":
                Glasshouse(k, x, z, h);
                break;
            case "cellar":
                Cellar(k, x, z, h);
                break;
            case "slag":
                // Tipped from the ladles, its runs still glowing where the last one went over (note 870: it was a smooth
                // dome with a flat glowing panel at its foot).
                Heap(k, x, z, h, "slag", Palette.Charcoal, 0.15f, 11, glow: true);
                break;
            case "coal":
                Heap(k, x, z, h, "coal", Palette.SootBlack, 0.35f, 5, glow: false);
                // The shovel left standing in it.
                k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0, tile: 1);
                k.Rod(new Vector3(-x * 0.35f, h * 0.55f, -z * 0.45f), new Vector3(-x * 0.42f, h * 0.55f + 1.15f, -z * 0.62f), 0.025f);
                k.Use("rust_heavy", Palette.IronGrey, 0.8f, 0.4f, tile: 1);
                k.Quad(new Vector3(-x * 0.35f - 0.14f, h * 0.55f + 0.05f, -z * 0.45f), new Vector3(-x * 0.35f + 0.14f, h * 0.55f + 0.05f, -z * 0.45f),
                    new Vector3(-x * 0.35f + 0.12f, h * 0.55f - 0.3f, -z * 0.4f), new Vector3(-x * 0.35f - 0.12f, h * 0.55f - 0.3f, -z * 0.4f), twoSided: true);
                break;
            case "pigs":
                // Pig iron in stacks like loaves, crossed course on course.
                k.Use("rust_heavy", Palette.RustRed, 0.8f, 0.3f, tile: 0.6f);
                for (int stack = 0; stack < 2; stack++)
                {
                    float sx = -x + 0.1f + stack * (x + 0.1f), w = x - 0.2f;
                    for (int course = 0; course < 5; course++)
                    {
                        float y = course * 0.2f;
                        if (course % 2 == 0)
                            for (float pz = -z + 0.1f; pz < z - 0.2f; pz += 0.32f)
                                k.Box(new Vector3(sx, y, pz), new Vector3(sx + w, y + 0.18f, pz + 0.26f), Kit.Faces.All & ~Kit.Faces.NegY);
                        else
                            for (float px = sx; px < sx + w - 0.2f; px += 0.32f)
                                k.Box(new Vector3(px, y, -z + 0.1f), new Vector3(px + 0.26f, y + 0.18f, z - 0.1f), Kit.Faces.All & ~Kit.Faces.NegY);
                    }
                }
                break;
            case "warehouse":
                Warehouse(k, x, z, h);
                break;
            default:
                // A modelled piece's stand-in: a brick block its size, an iron one for the frames.
                if (kind is "headframe" or "watertower")
                    k.Use("rust_heavy", Palette.IronGrey, 0.8f, 0.4f, tile: 1);
                else
                    k.Use("brick_soot", Palette.RustRed, 0.9f, 0.1f, tile: 1.2f);
                k.Box(new Vector3(-x, -0.3f, -z), new Vector3(x, h, z), Kit.Faces.All & ~Kit.Faces.NegY);
                break;
        }
        return k.Build($"works-{kind}");
    }

    /// <summary>
    /// A glasshouse grown under lamps (the director's "a people ... trying to find ways of making the world feel tolerable"):
    /// a brick dwarf wall, whitewashed glass on an iron frame to a ridge along it, rows of lamps hung inside over the beds,
    /// its door in the gable toward the line.
    /// </summary>
    static void Glasshouse(Kit k, float x, float z, float h)
    {
        const float wall = 0.7f;
        float eave = h - 1.5f;
        k.Use("brick_soot", Palette.RustRed, 0.9f, 0.1f, tile: 1.2f);
        k.Box(new Vector3(-x, -0.2f, -z), new Vector3(x, wall, z), Kit.Faces.All & ~Kit.Faces.NegY);
        // The beds inside, under the glass: dark soil in rows.
        k.Use("ground_mud", Palette.DeepBrown, 0.9f, 0, tile: 2);
        // The glass, whitewashed (so the light doesn't get out, and does): the lamps' warmth through it.
        k.Use("glass_dirty", Palette.LampAmber, 0.2f, 0.6f, tile: 2);
        k.Tint = new Vector3(1.25f, 0.95f, 0.75f);
        k.Emissive = 0.75f;
        foreach (float sx in new[] { -1f, 1f })
            k.Quad(new Vector3(sx * x, wall, -z), new Vector3(sx * x, wall, z), new Vector3(sx * x, eave, z), new Vector3(sx * x, eave, -z), twoSided: true);
        foreach (float sx in new[] { -1f, 1f })
            k.Quad(new Vector3(sx * x, eave, -z), new Vector3(sx * x, eave, z), new Vector3(0, h, z), new Vector3(0, h, -z), twoSided: true);
        foreach (float sz in new[] { -1f, 1f })
        {
            k.Quad(new Vector3(-x, wall, sz * z), new Vector3(x, wall, sz * z), new Vector3(x, eave, sz * z), new Vector3(-x, eave, sz * z), twoSided: true);
            k.Tri(new Vector3(-x, eave, sz * z), new Vector3(x, eave, sz * z), new Vector3(0, h, sz * z));
            k.Tri(new Vector3(x, eave, sz * z), new Vector3(-x, eave, sz * z), new Vector3(0, h, sz * z));
        }
        k.Emissive = 0;
        k.Tint = Vector3.One;
        // Its iron frame: the glazing bars, the ridge.
        k.Use("paint_black", Palette.Charcoal, 0.6f, 0.4f, tile: 1);
        for (float bz = -z; bz <= z + 0.01f; bz += 1.0f)
            foreach (float sx in new[] { -1f, 1f })
            {
                k.Rod(new Vector3(sx * (x + 0.02f), wall, bz), new Vector3(sx * (x + 0.02f), eave, bz), 0.04f);
                k.Rod(new Vector3(sx * (x + 0.02f), eave, bz), new Vector3(0, h + 0.02f, bz), 0.04f);
            }
        foreach (float sx in new[] { -1f, 1f })
        {
            k.Rod(new Vector3(sx * (x + 0.03f), eave, -z), new Vector3(sx * (x + 0.03f), eave, z), 0.07f);
            k.Rod(new Vector3(sx * (x + 0.03f), (wall + eave) / 2, -z), new Vector3(sx * (x + 0.03f), (wall + eave) / 2, z), 0.03f);
        }
        // The gables' bars, the ridge.
        foreach (float sz in new[] { -1f, 1f })
            for (float bx = -x; bx <= x + 0.01f; bx += x / 2)
                k.Rod(new Vector3(bx, wall, sz * (z + 0.02f)), new Vector3(bx, eave + (h - eave) * (1 - Math.Abs(bx) / x), sz * (z + 0.02f)), 0.04f);
        k.Rod(new Vector3(0, h, -z), new Vector3(0, h, z), 0.07f);
        // The door in the gable toward the line, dark.
        k.Use("wood_grey", Palette.BlueGrey, 0.9f, 0.05f, tile: 1);
        k.Box(new Vector3(-0.55f, 0, -z - 0.05f), new Vector3(0.55f, 2.1f, -z + 0.02f));
        // Rows of lamps hung inside over the beds, lit: the glow that's the warmest thing in the town.
        k.Use("lamp_lens", Palette.LampAmber, 0.2f, 0.3f, tile: 1);
        k.Tint = new Vector3(1.0f, 0.75f, 0.55f);
        k.Emissive = 1.2f;
        for (float lz = -z + 1.5f; lz < z - 1f; lz += 2.5f)
            foreach (float lx in new[] { -x * 0.45f, x * 0.45f })
                k.BoxAt(new Vector3(lx, eave - 0.3f, lz), new Vector3(0.18f, 0.08f, 0.18f));
        k.Emissive = 0;
        k.Tint = Vector3.One;
    }

    /// <summary>A root cellar: a turf mound over a stone vault, its stone face and padlocked door toward the line.</summary>
    static void Cellar(Kit k, float x, float z, float h)
    {
        var arch = new List<Vector2>();
        for (int i = 0; i <= 12; i++)
        {
            float a = MathF.PI * i / 12;
            arch.Add(new Vector2(MathF.Cos(a) * x, MathF.Sin(a) * h));
        }
        k.Use("ground_grass", Palette.MuddyOlive, 0.9f, 0, tile: 2);
        k.Prism(arch, -z + 0.5f, z, caps: true, smooth: true);
        k.Use("stone_block", Palette.IronGrey, 0.8f, 0.05f, tile: 1);
        k.Box(new Vector3(-x * 0.75f, -0.2f, -z), new Vector3(x * 0.75f, h * 0.85f, -z + 0.5f), Kit.Faces.All & ~Kit.Faces.NegY);
        k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0.05f, tile: 1);
        k.Box(new Vector3(-0.5f, 0, -z - 0.04f), new Vector3(0.5f, 1.75f, -z + 0.01f));
        k.Use("rust_heavy", Palette.IronGrey, 0.8f, 0.4f, tile: 1);
        k.Box(new Vector3(0.28f, 0.85f, -z - 0.09f), new Vector3(0.38f, 0.98f, -z - 0.04f));
    }

    /// <summary>
    /// A heap (coal, slag), tipped from one side: a long uneven mound higher toward its back, gullied down its slopes where
    /// the rain runs, its toe spread out ragged, and lumps of what it's made of rolled down round its foot and lying on its
    /// flanks (note 870: the director's playtest of 19 Oct, line 1, "a bit odd and 2D flat": a smooth dome, shaded as one).
    /// <paramref name="glow"/>: the slag's runs from the last ladle down its front, and the crust at its toe, still hot.
    /// </summary>
    static void Heap(Kit k, float x, float z, float h, string texture, Vector3 colour, float shine, int seed, bool glow)
    {
        var rng = new Random(seed * 31 + 870);
        k.Use(texture, colour, 0.9f, shine, tile: 1.2f);
        // Its outline and its surface, as functions of how far out (t, 0 the crest, 1 the toe) and which way (a).
        float Reach(float a) => 1 + 0.12f * MathF.Sin(a * 2 + seed) + 0.08f * MathF.Sin(a * 5 + seed * 1.7f) + 0.05f * MathF.Sin(a * 11 + seed * 0.3f);
        Vector3 At(float t, float a)
        {
            // A ridge along it at the crest (a heap tipped from a line of barrows, not a cone), widening to its toe.
            float r = MathF.Min(1, t) * Reach(a);
            float px = MathF.Cos(a) * x * (0.42f * (1 - r) + r), pz = MathF.Sin(a) * z * (0.06f * (1 - r) + r);
            // The crest set back (+Z): the tipping face toward the line longer than the back.
            pz += z * 0.18f * (1 - MathF.Min(1, t));
            // Its slopes straight, at what it'll stand at, rounding over only at the crest.
            float fall = 1 - MathF.Pow(MathF.Min(1, t), 1.2f);
            // Gullies down the slopes, the crest's own lumps.
            float gully = 0.07f * MathF.Sin(a * 13 + seed) * MathF.Sin(MathF.PI * MathF.Min(1, t)) + 0.05f * MathF.Sin(px * 1.3f + pz * 2.1f + seed);
            float y = h * (fall + gully * (0.4f + fall)) - 0.2f * MathF.Max(0, t - 0.9f) * 10;
            return new Vector3(px, MathF.Max(-0.2f, y), pz);
        }
        // The skin: rings from the crest out to the toe, smooth-shaded, its texture laid from above (a loft's runs round each
        // ring, sheared to a streak toward the crest), so the slag's cracks and the coal's lumps lie the same size all over.
        const int rings = 12, sides = 30;
        var grid = new Vector3[rings + 1, sides];
        for (int r = 0; r <= rings; r++)
            for (int i = 0; i < sides; i++)
                grid[r, i] = At(r == rings ? 1.04f : (float)r / rings, MathF.Tau * i / sides);
        var normals = new Vector3[rings + 1, sides];
        for (int r = 0; r < rings; r++)
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                var n = Vector3.Cross(grid[r, j] - grid[r, i], grid[r + 1, i] - grid[r, i]);
                if (n.Y < 0)
                    n = -n;
                normals[r, i] += n;
                normals[r, j] += n;
                normals[r + 1, i] += n;
                normals[r + 1, j] += n;
            }
        void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc)
        {
            var n = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(n, na + nb + nc) < 0)
                (b, c, nb, nc, n) = (c, b, nc, nb, -n);
            // Each face's texture from the way it most faces: from above on the gentle slopes, from the side on the steep.
            n = Vector3.Normalize(n);
            Func<Vector3, Vector2> uv = MathF.Abs(n.Y) > 0.55f ? p => new(p.X, p.Z) : MathF.Abs(n.X) > MathF.Abs(n.Z) ? p => new(p.Z, p.Y) : p => new(p.X, p.Y);
            k.Tri(a, b, c, Vector3.Normalize(na), Vector3.Normalize(nb), Vector3.Normalize(nc), uv(a), uv(b), uv(c));
        }
        for (int r = 0; r < rings; r++)
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                Face(grid[r, i], grid[r, j], grid[r + 1, j], normals[r, i], normals[r, j], normals[r + 1, j]);
                Face(grid[r, i], grid[r + 1, j], grid[r + 1, i], normals[r, i], normals[r + 1, j], normals[r + 1, i]);
            }
        // The crest's ridge closed over.
        var crest = new Vector3[sides];
        for (int i = 0; i < sides; i++)
            crest[i] = grid[0, i];
        for (int i = 1; i < sides - 1; i++)
            Face(crest[0], crest[i], crest[i + 1], Vector3.UnitY, Vector3.UnitY, Vector3.UnitY);
        // The lumps: rolled to the foot and out past it mostly, a few lodged up the flanks, each its own size and lie.
        var tint = k.Tint;
        for (int i = 0; i < 34; i++)
        {
            float a = (float)rng.NextDouble() * MathF.Tau;
            float t = i < 24 ? 0.9f + (float)rng.NextDouble() * 0.22f : 0.35f + (float)rng.NextDouble() * 0.5f;
            var p = At(MathF.Min(t, 1), a);
            if (t > 1)
                p = new Vector3(p.X * t, -0.05f, p.Z * t);
            float size = (i < 24 ? 0.18f : 0.12f) + (float)rng.NextDouble() * 0.28f;
            k.Tint = tint * (0.85f + (float)rng.NextDouble() * 0.3f);
            Lump(k, p, size, rng);
        }
        k.Tint = tint;
        if (!glow)
            return;
        // Three runs down the front from near the crest, each a narrow skin laid on the slope, brightest at the toe.
        k.Use("ember_crack", Palette.FurnaceOrange, 0.9f, 0, tile: 1.5f);
        foreach (float a0 in new[] { -1.85f, -1.45f, -1.2f })
        {
            float a = a0 + ((float)rng.NextDouble() - 0.5f) * 0.1f, w = 0.05f + (float)rng.NextDouble() * 0.04f;
            var strip = new List<Vector3[]>();
            for (int s = 0; s <= 8; s++)
            {
                float t = 0.3f + 0.68f * s / 8, wob = 0.04f * MathF.Sin(s * 1.9f + a * 7);
                var l = At(t, a + wob - w) + new Vector3(0, 0.04f, 0);
                var r = At(t, a + wob + w) + new Vector3(0, 0.04f, 0);
                strip.Add([l, r]);
            }
            for (int s = 0; s < strip.Count - 1; s++)
            {
                k.Emissive = 0.3f + 0.7f * s / (strip.Count - 1);
                k.Loft([strip[s], strip[s + 1]], facing: Vector3.UnitY);
            }
        }
        // And where they've pooled at the toe: a crust broken over the glow, dark plates on a lit bed.
        k.Emissive = 0.9f;
        var pool = new Vector3[10];
        var foot = At(1, -1.5f);
        for (int i = 0; i < pool.Length; i++)
        {
            float a = MathF.Tau * i / pool.Length, r = 0.9f + 0.3f * MathF.Sin(i * 2.3f);
            pool[i] = foot + new Vector3(MathF.Cos(a) * r * 1.6f, 0.06f, MathF.Sin(a) * r * 0.7f - 0.4f);
        }
        k.Cap(pool, Vector3.UnitY);
        k.Emissive = 0;
        k.Use(texture, colour, 0.9f, shine, tile: 1.2f);
        for (int i = 0; i < 6; i++)
        {
            var c = foot + new Vector3(((float)rng.NextDouble() - 0.5f) * 2.4f, 0.08f, ((float)rng.NextDouble() - 0.5f) * 1.0f - 0.4f);
            float s = 0.2f + (float)rng.NextDouble() * 0.25f;
            k.Box(c - new Vector3(s, 0.02f, s * 0.7f), c + new Vector3(s, 0.06f, s * 0.7f), Kit.Faces.All & ~Kit.Faces.NegY);
        }
    }

    /// <summary>A lump of coal or slag: a few faces, flat-shaded, lying however it came to rest.</summary>
    static void Lump(Kit k, Vector3 at, float size, Random rng)
    {
        const int n = 5;
        float turn = (float)rng.NextDouble() * MathF.Tau, squash = 0.55f + (float)rng.NextDouble() * 0.35f;
        var rings = new List<Vector3[]>();
        foreach (var (lift, scale) in new[] { (-0.25f, 0.75f), (0.2f, 1f), (0.8f, 0.45f) })
        {
            var ring = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float a = turn + MathF.Tau * i / n, r = size * scale * (0.75f + (float)rng.NextDouble() * 0.5f);
                ring[i] = at + new Vector3(MathF.Cos(a) * r, lift * size * squash, MathF.Sin(a) * r * (0.7f + (float)rng.NextDouble() * 0.4f));
            }
            rings.Add(ring);
        }
        k.Loft(rings, closed: true, smooth: false, facing: Vector3.Zero);
        k.Cap(rings[^1], Vector3.UnitY);
    }

    /// <summary>
    /// The warehouse: a timber goods shed gable-on to the line, its roof corrugated iron, its sliding door in that gable with
    /// a lamp over it and what came last chalked on it.
    /// </summary>
    static void Warehouse(Kit k, float x, float z, float h)
    {
        float eave = h - 2.2f;
        k.Use("wood_siding", Palette.BlueGrey, 0.85f, 0.05f, tile: 1);
        k.Box(new Vector3(-x, -0.3f, -z), new Vector3(x, eave, z), Kit.Faces.All & ~Kit.Faces.NegY);
        foreach (float sz in new[] { -1f, 1f })
        {
            k.Tri(new Vector3(-x, eave, sz * z), new Vector3(x, eave, sz * z), new Vector3(0, h, sz * z));
            k.Tri(new Vector3(x, eave, sz * z), new Vector3(-x, eave, sz * z), new Vector3(0, h, sz * z));
        }
        k.Use("corrugated_iron", Palette.IronGrey, 0.8f, 0.35f, tile: 1.5f);
        foreach (float sx in new[] { -1f, 1f })
            k.Quad(new Vector3(sx * (x + 0.35f), eave - 0.25f, -z - 0.3f), new Vector3(sx * (x + 0.35f), eave - 0.25f, z + 0.3f),
                new Vector3(0, h + 0.1f, z + 0.3f), new Vector3(0, h + 0.1f, -z - 0.3f), twoSided: true);
        // The sliding door on its rail, and the chalk on it.
        k.Use("wood_grey", Palette.DeepBrown, 0.9f, 0.05f, tile: 1);
        k.Box(new Vector3(-2.2f, 0, -z - 0.12f), new Vector3(1.8f, 3.6f, -z - 0.02f));
        k.Use("rust_heavy", Palette.IronGrey, 0.8f, 0.4f, tile: 1);
        k.Box(new Vector3(-2.6f, 3.65f, -z - 0.14f), new Vector3(2.6f, 3.75f, -z - 0.02f));
        k.Use("paper_form", Palette.BoardEnamel, 0.7f, 0, tile: 1);
        k.Tint = new Vector3(0.85f);
        for (int line = 0; line < 4; line++)
            k.Box(new Vector3(-1.8f, 2.6f - line * 0.28f, -z - 0.125f), new Vector3(-1.8f + 0.9f + (line * 37 % 5) * 0.2f, 2.66f - line * 0.28f, -z - 0.121f));
        k.Tint = Vector3.One;
        // The lamp over the door.
        k.Use("lamp_lens", Palette.LampAmber, 0.2f, 0.3f, tile: 1);
        k.Emissive = 1.4f;
        k.BoxAt(new Vector3(1.6f, 3.2f, -z - 0.25f), new Vector3(0.12f, 0.15f, 0.1f));
        k.Emissive = 0;
    }
}
