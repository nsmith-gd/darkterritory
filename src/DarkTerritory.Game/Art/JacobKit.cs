using System.Numerics;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

/// <summary>
/// Jacob, the fisherman (the director, 8 Oct 2026; GDD §3.2; ARCHITECTURE §8 note 572), dressed after the director's
/// photographs of him (9 Oct: "Model his character after this look (with a shorter beard)"): a young man, broad, his short
/// auburn beard trimmed close; a navy ball cap on backwards, its strap at his brow; wraparound sunglasses with blue mirror
/// lenses; a red-and-black plaid flannel open over a slate-blue tee, its sleeves down; a silver chain with a compass on it;
/// dark grey joggers and olive clogs. The survivors' figure (<see cref="Redress"/>: no coat, no scarf, no lamp) in his own
/// things, no mask, at the water's edge with his rod out over it and its line down into the water, a pail at his feet and a
/// lantern on a stake beside him (its light the scene's). In his frame: he faces −Z, out over the water, his feet at the origin.
/// </summary>
public sealed class JacobKit(Look? look)
{
    MeshAsset? _gear;
    readonly Dictionary<string, MeshAsset> _pieces = [];

    /// <summary>The figure he's drawn as (<see cref="Clothes"/> on the survivors', CreatureArt's dressed figures), and the clip he stands in, the rod held out.</summary>
    public const string Figure = "jacob";
    public const string Clip = "lantern";

    /// <summary>His lantern's flame on its stake, in his frame.</summary>
    public static readonly Vector3 Flame = new(-0.7f, 1.35f, -0.1f);

    static readonly Vector3 Red = new(0.5f, 0.07f, 0.08f), Black = new(0.05f, 0.045f, 0.05f), Tee = new(0.3f, 0.34f, 0.4f);

    /// <summary>
    /// The flannel's red over the whole of him above the waist, sleeves down to the wrists (its plaid and the tee down its
    /// open front are <see cref="Dress"/>'s); dark grey joggers, olive clogs; his face and hands the figure's own, a little ruddy.
    /// </summary>
    public static readonly IReadOnlyDictionary<Cloth, Dye> Clothes = new Dictionary<Cloth, Dye>
    {
        [Cloth.Face] = new(null, new Vector3(1.15f, 1.04f, 1.0f), 0.12f),
        [Cloth.Hands] = new(null, new Vector3(1.3f, 1.08f, 0.96f), 0.1f),
        [Cloth.Forearms] = new("cream", Red, 0.08f),
        [Cloth.Sleeves] = new("cream", Red, 0.08f),
        [Cloth.Shirt] = new("cream", Red, 0.08f),
        [Cloth.Trousers] = new("cream", new Vector3(0.2f, 0.2f, 0.215f), 0.06f),
        [Cloth.Feet] = new("cream", new Vector3(0.3f, 0.36f, 0.2f), 0.2f),
    };

    /// <summary>
    /// What grows on him, from the head's own surface (<see cref="Growth"/>): his short auburn beard round the jaw, under the chin
    /// and over the lip, the mouth left clear; and his short brown hair round the back and sides, under the cap.
    /// </summary>
    public static readonly IReadOnlyList<Growth> Growths =
    [
        new("beard", Beard, 0.0065f, new Dye("cream", new Vector3(0.2f, 0.08f, 0.032f), 0.02f)),
        new("hair", p => p.Y > 1.655f && p.Z > -0.05f && p.Y < 1.76f, 0.005f, new Dye("cream", new Vector3(0.3f, 0.18f, 0.1f), 0.04f)),
    ];

    /// <summary>
    /// Where his beard grows, in the head's bind pose (measured off its profile: the chin's underside 1.54 m, the lower lip
    /// 1.58, the upper lip 1.60, the nose 1.62-1.65, the ears ±0.097 at z ≈ 0): the jaw and chin, down the throat a little,
    /// up the cheeks to the sideburns, and the moustache over the lip (a full short beard: at a face this size, a gap for the
    /// mouth reads as a hole in it).
    /// </summary>
    static bool Beard(Vector3 p)
    {
        if (p.Y < 1.505f || p.Z > -0.015f)
            return false;
        float off = MathF.Abs(MathF.Atan2(p.X, -(p.Z - Mid)));
        if (off < 0.95f)
            return p.Y <= 1.614f;
        return p.Y <= 1.65f && off < 1.75f;
    }

    /// <summary>Puts on him, as drawn last at <paramref name="model"/>: his cap and sunglasses, the flannel and the chain.</summary>
    public void Dress(CreatureArt creatures, MeshBuilder mesh, in Matrix4x4 model)
    {
        foreach (var piece in (string[])["cap", "shades"])
            creatures.Wear(mesh, Get(piece), "head", model, Figure);
        creatures.Wear(mesh, Get("flannel"), "spine_02", model, Figure);
        creatures.Wear(mesh, Get("chain"), "spine_03", model, Figure);
    }

    MeshAsset Get(string key)
    {
        if (_pieces.TryGetValue(key, out var piece))
            return piece;
        var k = new Kit(look, 5400 + key.Length);
        switch (key)
        {
            case "cap": Cap(k); break;
            case "shades": Shades(k); break;
            case "flannel": Flannel(k); break;
            default: Chain(k); break;
        }
        return _pieces[key] = k.Build("jacob." + key);
    }

    static void Colour(Kit k, Vector3 colour, float wear = 0.5f, float shine = 0.05f)
    {
        k.Use("cream", colour, wear, shine, tile: 1);
        k.Tint = colour;
    }

    // The survivors' head (measured off the figure: front −0.12, back +0.095, ±0.097 at the ears, its crown 1.79 m).
    const float Mid = -0.015f, HeadZ = -0.026f, Crown = 1.794f, EyeY = 1.655f, EyeZ = -0.128f;

    /// <summary>A ring round the head, <paramref name="from"/>..<paramref name="to"/> radians of it (0 the back, π the face), lower
    /// at the back than the front by the two heights, its front and back reach.</summary>
    static Vector3[] Arc(float yBack, float yFront, float half, float front, float back, float from = -MathF.PI, float to = MathF.PI, int n = 20)
    {
        var ring = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            float a = from + (to - from) * i / (n - 1), c = MathF.Cos(a);
            float z = c < 0 ? Mid + c * (Mid - front) : Mid + c * (back - Mid);
            ring[i] = new Vector3(MathF.Sin(a) * half, (yBack + yFront) / 2 - c * (yFront - yBack) / 2, z);
        }
        return ring;
    }

    /// <summary>
    /// His fitted cap, on backwards: a structured six-panel crown in navy, high at its front panel (at the back of his head,
    /// worn so) and round at the top with its button, the panels' seams, and the flat brim out over the back of his neck,
    /// curved a little side to side. No strap: it's a fitted one.
    /// </summary>
    static void Cap(Kit k)
    {
        float r = 0.122f, foot = 1.69f;
        var navy = new Vector3(0.1f, 0.12f, 0.22f);
        Colour(k, navy, 0.6f, 0.08f);
        var c = new Vector3(0, foot - 0.01f, HeadZ + 0.004f);
        k.Lathe(c, [new(r + 0.004f, 0), new(r + 0.006f, 0.055f), new(r - 0.006f, 0.095f), new(0.085f, 0.125f), new(0.04f, 0.14f), new(0, 0.143f)], 18);
        // The seams, darker, up from the band to the button; the button on top.
        Colour(k, navy * 0.55f, 0.5f);
        for (int i = 0; i < 6; i++)
        {
            float a = i * MathF.Tau / 6;
            Vector3 At(float rr, float h) => c + new Vector3(MathF.Sin(a) * rr, h, MathF.Cos(a) * rr);
            k.Cylinder(At(r + 0.007f, 0.004f), At(r - 0.004f, 0.098f), 0.0018f, 3, caps: false);
            k.Cylinder(At(r - 0.004f, 0.098f), At(0.004f, 0.144f), 0.0018f, 3, caps: false);
        }
        k.Cylinder(c + new Vector3(0, 0.142f, 0), c + new Vector3(0, 0.152f, 0), 0.008f, 6);
        // The brim: flat, out the back, a little curve side to side, and its underside a shade darker.
        Colour(k, navy * 0.9f, 0.55f, 0.08f);
        Vector3 Brim(float u, float out_) => new(MathF.Sin(u) * (r + 0.004f + out_), foot + 0.006f - out_ * 0.04f - (1 - MathF.Cos(u * 1.6f)) * 0.006f, c.Z + MathF.Cos(u) * (r + 0.004f + out_ * 1.05f));
        for (int i = 0; i < 10; i++)
        {
            float a0 = -1.0f + i * 0.2f, a1 = a0 + 0.2f;
            k.Quad(Brim(a0, 0), Brim(a1, 0), Brim(a1, 0.078f), Brim(a0, 0.078f), twoSided: true);
        }
    }

    /// <summary>Wraparound sunglasses: a single band of blue mirror across both eyes, curving back round to his temples.</summary>
    static void Shades(Kit k)
    {
        var lens = Arc(EyeY - 0.022f, EyeY - 0.022f, 0.104f, EyeZ - 0.012f, 0, 2.15f, 4.13f, 14);
        var brow = Arc(EyeY + 0.02f, EyeY + 0.02f, 0.104f, EyeZ - 0.01f, 0, 2.15f, 4.13f, 14);
        Colour(k, new Vector3(0.1f, 0.35f, 0.95f), 0.05f, 0.95f);
        k.Loft([lens, brow], twoSided: true);
        // The frame: dark grey along its top, and the arms back over the ears.
        Colour(k, new Vector3(0.12f, 0.12f, 0.13f), 0.3f, 0.5f);
        var rim = Arc(EyeY + 0.026f, EyeY + 0.026f, 0.106f, EyeZ - 0.012f, 0, 2.15f, 4.13f, 14);
        k.Loft([brow, rim], twoSided: true);
        foreach (float side in (float[])[-1, 1])
            k.Cylinder(new Vector3(side * 0.1f, EyeY + 0.015f, -0.08f), new Vector3(side * 0.102f, EyeY + 0.005f, HeadZ + 0.03f), 0.003f, 4);
    }

    /// <summary>
    /// The flannel's plaid and its open front, laid on the red the figure's shirt is dyed (just proud of the figure's torso):
    /// black bands across, black stripes down, the slate tee in a strip down the open front with the buttons beside it, and
    /// its collar turned round the neck.
    /// </summary>
    static void Flannel(Kit k)
    {
        // The figure's torso, measured (height, half-width, front, back), and how far proud of it the plaid lies.
        (float Y, float Half, float Front, float Back)[] torso = [(0.9f, 0.19f, 0.17f, 0.18f), (1.0f, 0.19f, 0.17f, 0.18f), (1.1f, 0.2f, 0.18f, 0.14f),
            (1.2f, 0.21f, 0.18f, 0.14f), (1.3f, 0.235f, 0.17f, 0.155f), (1.42f, 0.22f, 0.15f, 0.15f)];
        const float proud = 0.008f, tee = 0.0f;
        Vector3 On(float a, float y)
        {
            int i = 0;
            while (i < torso.Length - 2 && torso[i + 1].Y < y)
                i++;
            var (lo, hi) = (torso[i], torso[i + 1]);
            float f = Math.Clamp((y - lo.Y) / (hi.Y - lo.Y), 0, 1);
            float half = lo.Half + (hi.Half - lo.Half) * f + proud, front = lo.Front + (hi.Front - lo.Front) * f + proud, back = lo.Back + (hi.Back - lo.Back) * f + proud;
            float c = MathF.Cos(a);
            return new Vector3(MathF.Sin(a) * half, y, c < 0 ? c * front : c * back);
        }
        // Where the tee's strip is, as an angle either side of the front at that height.
        float Open(float y) => MathF.Asin(Math.Min(1, tee / On(MathF.PI / 2, y).X));
        void Band(float y0, float y1, float from, float to)
        {
            const int n = 14;
            for (int i = 0; i < n; i++)
            {
                float a0 = from + (to - from) * i / n, a1 = from + (to - from) * (i + 1) / n;
                k.Quad(On(a0, y0), On(a1, y0), On(a1, y1), On(a0, y1), twoSided: true);
            }
        }
        Colour(k, Vector3.Lerp(Red, Black, 0.6f), 0.6f);
        // A tartan's grid: a broad dark band and a fine line between, across and down.
        for (float y = 0.93f; y < 1.4f; y += 0.075f)
        {
            float open = Open(y);
            Band(y, y + 0.012f, -MathF.PI + open, MathF.PI - open);
            Band(y + 0.042f, y + 0.046f, -MathF.PI + open, MathF.PI - open);
        }
        for (float a = -2.7f; a <= 2.71f; a += 0.36f)
            for (float y = 0.9f; y < 1.4f; y += 0.04f)
            {
                var d = (On(a + 0.035f, y) - On(a, y)) * 0.5f;
                var u = On(a, y + 0.04f) - On(a, y);
                var p0 = On(a, y);
                k.Quad(p0 - d, p0 + d, p0 + d + u, p0 - d + u, twoSided: true);
            }
        // Buttoned up but the top one: the placket down the front with its buttons, and the tee in the V at the collar.
        Colour(k, Vector3.Lerp(Red, Black, 0.35f), 0.6f);
        for (float y = 0.9f; y < 1.34f; y += 0.04f)
        {
            float y1 = Math.Min(y + 0.04f, 1.34f);
            Vector3 P(float x, float yy) => On(MathF.PI, yy) with { X = x } - new Vector3(0, 0, 0.002f);
            k.Quad(P(-0.012f, y), P(0.012f, y), P(0.012f, y1), P(-0.012f, y1), twoSided: true);
        }
        Colour(k, Black, 0.3f, 0.4f);
        for (float y = 0.96f; y < 1.33f; y += 0.075f)
            k.Disc(On(MathF.PI, y) - new Vector3(0, 0, 0.004f), -Vector3.UnitZ, 0.006f, 6);
        Colour(k, Tee, 0.6f);
        var v0 = On(MathF.PI, 1.33f) - new Vector3(0, 0, 0.003f);
        var vl = On(MathF.PI, 1.44f) with { X = -0.06f } - new Vector3(0, 0, 0.003f);
        var vr = On(MathF.PI, 1.44f) with { X = 0.06f } - new Vector3(0, 0, 0.003f);
        k.Tri(v0, vr, vl);
        k.Tri(v0, vl, vr);
        // Its collar, turned down round the neck.
        Colour(k, Red, 0.6f);
        static Vector3[] Ring(float y, float half, float front, float back, float open)
        {
            const int n = 16;
            var ring = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float a = -MathF.PI + open + (MathF.Tau - 2 * open) * i / (n - 1), c = MathF.Cos(a);
                ring[i] = new Vector3(MathF.Sin(a) * half, y, c < 0 ? c * front : c * back);
            }
            return ring;
        }
        k.Loft([Ring(1.44f, 0.15f, 0.13f, 0.14f, 0.35f), Ring(1.5f, 0.095f, 0.09f, 0.09f, 0.35f)], twoSided: true);
    }

    /// <summary>A silver chain round his neck, a compass on it on his chest.</summary>
    static void Chain(Kit k)
    {
        Colour(k, new Vector3(0.85f, 0.85f, 0.88f), 0.2f, 0.85f);
        Vector3[] half = [new(0.065f, 1.53f, 0.0f), new(0.075f, 1.49f, -0.08f), new(0.05f, 1.41f, -0.16f), new(0, 1.36f, -0.185f)];
        foreach (float side in (float[])[-1, 1])
            for (int i = 0; i + 1 < half.Length; i++)
                k.Cylinder(half[i] with { X = half[i].X * side }, half[i + 1] with { X = half[i + 1].X * side }, 0.0025f, 4, caps: false);
        k.Cylinder(new Vector3(-0.065f, 1.53f, 0), new Vector3(0.065f, 1.53f, 0.04f), 0.0025f, 4, caps: false);
        k.Cylinder(new Vector3(0, 1.345f, -0.188f), new Vector3(0, 1.345f, -0.196f), 0.016f, 10);
    }

    /// <summary>His rod out over the water and its line down into it, his pail, and the lantern on its stake (his frame).</summary>
    public MeshAsset Gear => _gear ??= Build("gear", k =>
    {
        // The rod, from his hands out and up over the water, bending at its tip; the line hanging down from it.
        k.Use("wood_grey", Palette.DeepBrown, 0.5f, 0.2f, tile: 1);
        k.Tint = new Vector3(0.8f, 0.6f, 0.4f);
        var butt = new Vector3(0.18f, 1.0f, -0.25f);
        var mid = new Vector3(0.3f, 1.9f, -2.2f);
        var tip = new Vector3(0.38f, 2.2f, -4.1f);
        k.Cylinder(butt, mid, 0.016f, 6, radiusB: 0.011f);
        k.Cylinder(mid, tip, 0.011f, 5, radiusB: 0.005f);
        k.Use("cream", Palette.BoardEnamel, 0.2f, 0.4f, tile: 1);
        k.Tint = new Vector3(0.9f);
        k.Cylinder(tip, new Vector3(0.38f, -0.3f, -4.3f), 0.0025f, 3);
        // A red float on the water.
        k.Use("cream", Palette.SignalRed, 0.3f, 0.3f, tile: 1);
        k.Tint = new Vector3(0.9f, 0.15f, 0.1f);
        k.BoxAt(new Vector3(0.38f, -0.25f, -4.3f), new Vector3(0.025f, 0.03f, 0.025f));
        // The pail at his feet, its catch a glint of silver.
        k.Use("iron_plate", Palette.IronGrey, 0.6f, 0.4f, tile: 1);
        k.Tint = Vector3.One;
        k.Lathe(new Vector3(0.45f, 0, 0.2f), [new(0.11f, 0), new(0.14f, 0.26f)], 10, capTop: false);
        k.Use("cream", Palette.BoardEnamel, 0.1f, 0.9f, tile: 1);
        k.Tint = new Vector3(0.75f, 0.8f, 0.85f);
        k.Disc(new Vector3(0.45f, 0.2f, 0.2f), Vector3.UnitY, 0.12f, 10);
        // The lantern's stake and the lantern.
        k.Use("wood_grey", Palette.DeepBrown, 0.6f, 0, tile: 1);
        k.Tint = new Vector3(0.7f, 0.55f, 0.4f);
        k.Cylinder(new Vector3(Flame.X, 0, Flame.Z), Flame + new Vector3(0, 0.25f, 0), 0.02f, 5);
        k.Use("brass", Palette.TarnishedBrass, 0.4f, 0.5f, tile: 1);
        k.Tint = Vector3.One;
        k.BoxAt(Flame + new Vector3(0, 0.11f, 0), new Vector3(0.055f, 0.012f, 0.055f));
        k.BoxAt(Flame + new Vector3(0, -0.1f, 0), new Vector3(0.06f, 0.014f, 0.06f));
        k.Use("glass_dirty", Palette.LampAmber, 0.1f, 0.6f, tile: 1);
        k.Tint = new Vector3(1.6f, 1.2f, 0.7f);
        k.Emissive = 0.9f;
        k.BoxAt(Flame, new Vector3(0.045f, 0.09f, 0.045f));
        k.Emissive = 0;
        k.Tint = Vector3.One;
    });

    MeshAsset Build(string name, Action<Kit> build)
    {
        var k = new Kit(look, 5300 + name.Length);
        build(k);
        return k.Build("jacob." + name);
    }
}
