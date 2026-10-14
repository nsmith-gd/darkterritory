using System.Numerics;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

/// <summary>
/// Nicki (the director, 8 Oct 2026: "an NPC you can find some times in one of the houses. Her name is Nicki and she's the only
/// house late at night that is partying"; GDD §3.2; ARCHITECTURE §8 note 571), dressed after the director's photographs of
/// her: shoulder-length strawberry-blonde hair, wavy, with a fringe; a tan; dangling silver earrings; a tank top (a lime green
/// one, or a blush-pink camisole) and black trousers. The survivors' figure (<see cref="Redress"/>: no coat, no scarf, no
/// lamp) a little narrower at the shoulders and the waist, in her own things, and no mask: it's her party.
/// </summary>
public sealed class NickiKit(Look? look)
{
    readonly Dictionary<string, MeshAsset> _pieces = [];

    /// <summary>The figure she's drawn as (<see cref="Clothes"/> on the survivors', CreatureArt's dressed figures).</summary>
    public const string Figure = "nicki";

    static readonly Vector3 Tan = new(1.45f, 1.08f, 0.88f);
    static readonly Vector3 Skin = new(0.6f, 0.38f, 0.27f);

    /// <summary>
    /// Her clothes: a tank top over bare shoulders and arms (lime green, as the night has it; <see cref="Top"/> wears the
    /// camisole over it on the other nights), black trousers, black shoes; her face and hands the figure's own, tanned.
    /// </summary>
    public static readonly IReadOnlyDictionary<Cloth, Dye> Clothes = new Dictionary<Cloth, Dye>
    {
        [Cloth.Face] = new(null, Tan, 0.15f),
        [Cloth.Hands] = new(null, Tan, 0.12f),
        [Cloth.Forearms] = new("cream", Skin, 0.12f),
        [Cloth.Sleeves] = new("cream", Skin, 0.12f),
        [Cloth.Shirt] = new("cream", new Vector3(0.62f, 0.85f, 0.2f), 0.08f),
        [Cloth.Trousers] = new("cream", new Vector3(0.06f, 0.06f, 0.065f), 0.1f),
        [Cloth.Feet] = new("cream", new Vector3(0.05f, 0.045f, 0.045f), 0.3f),
    };

    /// <summary>
    /// The figure taken in a little (the crew's is a broad man's): the shoulders and the chest narrower, the waist more so,
    /// the arms brought in with them so they still meet the shoulder.
    /// </summary>
    public static Vector3 Shape(Vector3 p, string bone)
    {
        if (bone is "spine_03" or "spine_02" or "neck")
            return p with { X = p.X * 0.9f, Z = p.Z * 0.96f };
        if (bone is "spine_01")
            return p with { X = p.X * 0.86f, Z = p.Z * 0.92f };
        if (bone.StartsWith("clavicle", StringComparison.Ordinal))
            return p with { X = p.X * 0.92f };
        if (bone.StartsWith("upperarm", StringComparison.Ordinal) || bone.StartsWith("lowerarm", StringComparison.Ordinal)
            || bone.StartsWith("hand", StringComparison.Ordinal) || bone.StartsWith("finger", StringComparison.Ordinal) || bone.StartsWith("thumb", StringComparison.Ordinal))
            return p with { X = p.X - MathF.Sign(p.X) * 0.02f, Y = p.Y, Z = p.Z * 0.92f };
        return p;
    }

    /// <summary>Her hair, her earrings, and tonight's top (0 the lime tank, 1 the blush camisole over it).</summary>
    public MeshAsset Hair => Get("hair", HairOf);
    public MeshAsset Earrings => Get("earrings", EarringsOf);
    public MeshAsset Top(int kind) => Get("top." + kind % 2, k => TopOf(k, kind % 2));

    /// <summary>Which top she's in tonight, from the town's seed: 0 the lime tank top, 1 the blush camisole.</summary>
    public static int TopOf(ulong seed) => (int)(seed % 2);

    /// <summary>Puts on her, as drawn last at <paramref name="model"/>: her hair and earrings, and tonight's top.</summary>
    public void Dress(CreatureArt creatures, MeshBuilder mesh, in Matrix4x4 model, int top)
    {
        creatures.Wear(mesh, Hair, "head", model, Figure);
        creatures.Wear(mesh, Earrings, "head", model, Figure);
        creatures.Wear(mesh, Top(top), "spine_02", model, Figure);
    }

    MeshAsset Get(string key, Action<Kit> build)
    {
        if (_pieces.TryGetValue(key, out var piece))
            return piece;
        var k = new Kit(look, 5300 + key.Length);
        build(k);
        return _pieces[key] = k.Build("nicki." + key);
    }

    static void Colour(Kit k, Vector3 colour, float wear = 0.5f, float shine = 0.05f)
    {
        k.Use("cream", colour, wear, shine, tile: 1);
        k.Tint = colour;
    }

    // The survivors' head (measured off the figure: front −0.12, back +0.095, ±0.097 at the ears, its crown 1.79 m).
    const float Mid = -0.015f, Crown = 1.79f;

    /// <summary>A ring round the head: <paramref name="from"/>..<paramref name="to"/> radians of it (0 the back, π the face),
    /// lower at the back than the front by the two heights, its front and back reach, and a wave in it.</summary>
    static Vector3[] Arc(float yBack, float yFront, float half, float front, float back, float from = -MathF.PI, float to = MathF.PI,
        float wave = 0, float phase = 0, int n = 22)
    {
        var ring = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            float a = from + (to - from) * i / (n - 1), c = MathF.Cos(a);
            float w = 1 + wave * MathF.Sin(a * 7 + phase);
            float z = c < 0 ? Mid + c * (Mid - front) : Mid + c * (back - Mid);
            ring[i] = new Vector3(MathF.Sin(a) * half * w, (yBack + yFront) / 2 - c * (yFront - yBack) / 2, (z - Mid) * w + Mid);
        }
        return ring;
    }

    /// <summary>
    /// Her hair: a full crown of it, a fringe across the forehead to her brows, and the rest falling in waves past her ears to
    /// her shoulders, open at the face; strawberry blonde, lighter on top where the light finds it.
    /// </summary>
    static void HairOf(Kit k)
    {
        var blonde = new Vector3(0.66f, 0.43f, 0.2f);
        Colour(k, blonde * 1.12f, 0.35f, 0.15f);
        // The crown, fuller than the skull all round: it's been done for the party.
        k.Loft([Arc(1.6f, 1.765f, 0.122f, -0.132f, 0.125f), Arc(1.68f, 1.795f, 0.128f, -0.122f, 0.135f), Arc(1.76f, 1.818f, 0.11f, -0.095f, 0.12f),
            Arc(1.81f, 1.835f, 0.07f, -0.055f, 0.07f), Arc(1.84f, 1.84f, 0.004f, -0.018f, -0.012f)], closed: true, twoSided: true);
        // The fringe: across the forehead from temple to temple, down to the brows, swept a little to one side.
        Colour(k, blonde, 0.4f, 0.12f);
        k.Loft([Arc(1.79f, 1.79f, 0.108f, -0.126f, 0, 2.2f, 4.08f, 0.02f), Arc(1.735f, 1.735f, 0.106f, -0.134f, 0, 2.25f, 4.03f, 0.04f, 1.3f),
            Arc(1.685f, 1.695f, 0.104f, -0.136f, 0, 2.35f, 3.95f, 0.07f, 2.1f)], twoSided: true);
        // The fall: round the sides and back from above the ears to the shoulders, flaring as it falls, in big soft waves; open
        // at the face (the arcs' ends at the cheeks).
        Colour(k, blonde * 0.95f, 0.4f, 0.12f);
        k.Loft([Arc(1.7f, 1.74f, 0.126f, -0.124f, 0.13f, -2.3f, 2.3f), Arc(1.62f, 1.66f, 0.14f, -0.118f, 0.142f, -2.25f, 2.25f, 0.06f, 0.5f),
            Arc(1.54f, 1.58f, 0.158f, -0.11f, 0.15f, -2.2f, 2.2f, 0.09f, 1.6f), Arc(1.47f, 1.5f, 0.175f, -0.1f, 0.15f, -2.1f, 2.1f, 0.12f, 2.9f),
            Arc(1.42f, 1.44f, 0.185f, -0.09f, 0.14f, -2.0f, 2.0f, 0.14f, 4.1f)], twoSided: true);
        // Its ends: locks curling out and up at the shoulders, a lighter colour where they catch the light.
        Colour(k, blonde * 1.2f, 0.35f, 0.15f);
        var hem = Arc(1.42f, 1.44f, 0.185f, -0.09f, 0.14f, -2.0f, 2.0f, 0.14f, 4.1f, 15);
        for (int i = 0; i < hem.Length; i++)
        {
            var root = hem[i];
            var outward = Vector3.Normalize(new Vector3(root.X, 0, root.Z - Mid));
            var tip = root + outward * 0.04f + new Vector3(0, i % 2 == 0 ? -0.05f : -0.03f, 0);
            var side = Vector3.Normalize(Vector3.Cross(outward, Vector3.UnitY)) * 0.02f;
            k.Quad(root - side, root + side, tip + side * 0.3f, tip - side * 0.3f, twoSided: true);
        }
        k.Tint = Vector3.One;
    }

    /// <summary>Her earrings: a silver drop from each lobe, a twist of wire to a flat diamond that catches the light.</summary>
    static void EarringsOf(Kit k)
    {
        Colour(k, new Vector3(0.9f, 0.9f, 0.93f), 0.15f, 0.9f);
        foreach (float side in (float[])[-1, 1])
        {
            var lobe = new Vector3(side * 0.099f, 1.6f, -0.005f);
            var drop = lobe + new Vector3(side * 0.004f, -0.035f, 0);
            k.Cylinder(lobe, drop, 0.0018f, 4);
            var c = drop + new Vector3(0, -0.018f, 0);
            k.Quad(drop, c + new Vector3(0, 0, -0.011f), c + new Vector3(0, -0.02f, 0), c + new Vector3(0, 0, 0.011f), twoSided: true);
        }
    }

    /// <summary>
    /// Her top, fitted over the figure (where the coat was, the frame has its gaps): 0 the lime tank top, broad straps; 1 the
    /// blush camisole, thin straps and a lace edge at its neckline.
    /// </summary>
    static void TopOf(Kit k, int kind)
    {
        Colour(k, kind == 0 ? Clothes[Cloth.Shirt].Colour : new Vector3(0.95f, 0.76f, 0.74f), 0.4f, 0.12f);
        // Over the narrowed torso (its front −0.17, its back +0.165 at the chest; the hips as they were, 0.19 and +0.18).
        (float Y, float Half, float Front, float Back)[] body = [(0.92f, 0.205f, 0.188f, 0.195f), (1.1f, 0.19f, 0.184f, 0.176f), (1.25f, 0.196f, 0.187f, 0.168f), (1.37f, 0.205f, 0.183f, 0.162f)];
        static Vector3[] Ring((float Y, float Half, float Front, float Back) t)
        {
            const int n = 20;
            var ring = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float a = i * MathF.Tau / n, c = MathF.Cos(a);
                ring[i] = new Vector3(MathF.Sin(a) * t.Half, t.Y - (c < 0 ? 0.03f * c * c : 0), c < 0 ? c * t.Front : c * t.Back);
            }
            return ring;
        }
        k.Loft([.. body.Select(Ring)], closed: true, twoSided: true);
        // The straps over the shoulders.
        foreach (float side in (float[])[-1, 1])
        {
            float strap = kind == 0 ? 0.016f : 0.006f;
            k.Cylinder(new Vector3(side * 0.09f, 1.34f, -0.15f), new Vector3(side * 0.1f, 1.5f, -0.02f), strap, 4);
            k.Cylinder(new Vector3(side * 0.1f, 1.5f, -0.02f), new Vector3(side * 0.09f, 1.35f, 0.13f), strap, 4);
        }
        if (kind == 0)
        {
            k.Tint = Vector3.One;
            return;
        }
        // The lace at the neckline: a paler edge.
        Colour(k, new Vector3(1.05f, 0.92f, 0.9f), 0.3f, 0.1f);
        var top = body[^1];
        var edge = Ring(top);
        var below = Ring(top with { Y = top.Y - 0.025f });
        for (int i = 0; i < edge.Length; i++)
        {
            int j = (i + 1) % edge.Length;
            if (edge[i].Z > -0.05f || edge[j].Z > -0.05f)
                continue;
            float o = 0.003f;
            k.Quad(below[i] - Vector3.UnitZ * o, below[j] - Vector3.UnitZ * o, edge[j] - Vector3.UnitZ * o, edge[i] - Vector3.UnitZ * o, twoSided: true);
        }
        k.Tint = Vector3.One;
    }
}
