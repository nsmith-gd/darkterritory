using System.Numerics;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

/// <summary>
/// What a town's people breathe through, and what they wear on their heads (the director, 8 Oct 2026: "We need townsfolk
/// models who wear some sort of respirator mask or oxygen mask or other breathing apparatuses to indicate the air is
/// foul"; queue #90, note 353). Never the crew's mask: the townsfolk are the survivors' bare-headed figure (the crew's
/// rig and clips, tools/models/recipes/survivor_*) with these pieces worn on it, each made in that figure's bind pose and
/// carried by a bone (<see cref="CreatureArt.Wear"/>): the face's on the head, a bottle or a bag on the chest's spine.
/// A hose from one to the other is drawn each time between its two ends where the pose has put them (<see cref="Hoses"/>).
/// <list type="bullet">
/// <item>respirator: a black rubber half-mask over the nose and mouth, a filter can either side, the straps round the head;</item>
/// <item>oxygen: an amber rubber cup on a hose over the shoulder to a green bottle slung on the back;</item>
/// <item>rebreather: a mine-rescue set (Cape Breton's pits had them): the mouthpiece and nose clip, two hoses down to the
/// breathing bag on the chest and its scrubber can, the harness;</item>
/// <item>wrap: a wool wrap over the nose and mouth with a tin filter can sewn into its front: made at home.</item>
/// </list>
/// At home a household has its mask down (<see cref="Face"/>'s <c>down</c>): hung on the chest, ready.
/// </summary>
public sealed class TownsfolkKit(Look? look)
{
    readonly Dictionary<string, MeshAsset> _pieces = [];

    // The survivors' head in their bind pose (measured off survivor_prisoner.glb's head-weighted skin): the mouth, the nose's
    // tip, the head's middle front to back and its half-width and depth at the ears, its crown.
    static readonly Vector3 Mouth = new(0, 1.60f, -0.136f);
    const float Nose = 1.64f, HeadZ = -0.026f, HeadHalfX = 0.1f, HeadHalfZ = 0.125f, Crown = 1.794f;

    /// <summary>Where a mask hangs at home: under the chin, on the chest (spine_03's bind).</summary>
    static readonly Vector3 Hung = new(0, 1.47f, -0.2f);

    /// <summary>The bottle on the back and the bag on the chest (spine_03's bind; the coat's back at z ≈ 0.15, its front −0.17).</summary>
    static readonly Vector3 Bottle = new(0.06f, 1.02f, 0.235f), Bag = new(0, 1.27f, -0.23f);
    const float BottleRadius = 0.075f, BottleHeight = 0.5f;

    /// <summary>The face piece for <paramref name="gear"/> (on the head bone; <paramref name="down"/>: hung on the chest, on spine_03).</summary>
    public MeshAsset Face(string gear, bool down) => Get($"{gear}.{(down ? "down" : "on")}", k =>
    {
        // Hung on the chest, its straps are round the neck under the collar, out of sight.
        if (down)
            k.Push(FaceDown);
        switch (gear)
        {
            case "oxygen":
                OxygenCup(k, straps: !down);
                break;
            case "rebreather":
                Mouthpiece(k);
                break;
            case "wrap":
                Wrap(k);
                break;
            default:
                HalfMask(k, straps: !down);
                break;
        }
    });

    /// <summary>What <paramref name="gear"/> carries on the body (spine_03): the bottle and its sling, the bag and its harness; null for none.</summary>
    public MeshAsset? Body(string gear) => gear switch
    {
        "oxygen" => Get("oxygen.body", BottleOnTheBack),
        "rebreather" => Get("rebreather.body", BagOnTheChest),
        _ => null,
    };

    /// <summary>A hat (1–4: a sou'wester, a knitted toque, a flat cap, a headscarf), or null bare-headed (0).</summary>
    public MeshAsset? Hat(int kind) => kind switch
    {
        1 => Get("hat.souwester", Souwester),
        2 => Get("hat.toque", Toque),
        3 => Get("hat.cap", Cap),
        4 => Get("hat.scarf", Headscarf),
        _ => null,
    };

    /// <summary>How many hats there are to draw from, bare-headed included.</summary>
    public const int Hats = 5;

    /// <summary>
    /// The hoses <paramref name="gear"/> runs, each from a point on the face piece (in the head's bind; on the chest's when
    /// <paramref name="down"/>) to a point on the body (spine_03's bind): drawn between where the pose has put them.
    /// </summary>
    public static IReadOnlyList<(Vector3 Face, Vector3 Body)> Hoses(string gear, bool down)
    {
        var face = down ? FaceDown : Matrix4x4.Identity;
        return gear switch
        {
            "oxygen" => [(Vector3.Transform(Mouth + new Vector3(0.0f, -0.045f, -0.055f), face), new Vector3(0.14f, 1.5f, -0.06f))],
            "rebreather" => [(Vector3.Transform(Mouth + new Vector3(0.035f, -0.01f, -0.05f), face), Bag + new Vector3(0.07f, 0.1f, -0.02f)),
                (Vector3.Transform(Mouth + new Vector3(-0.035f, -0.01f, -0.05f), face), Bag + new Vector3(-0.07f, 0.1f, -0.02f))],
            _ => [],
        };
    }

    static readonly Matrix4x4 FaceDown = Matrix4x4.CreateTranslation(-Mouth) * Matrix4x4.CreateRotationX(-1.25f) * Matrix4x4.CreateTranslation(Hung);

    /// <summary>A hose's radius (m): the oxygen's rubber line, the rebreather's corrugated ones a little thicker.</summary>
    public static float HoseRadius(string gear) => gear == "rebreather" ? 0.017f : 0.009f;

    /// <summary>Draws a hose from <paramref name="a"/> to <paramref name="b"/> (the draw's space), bowed out toward
    /// <paramref name="forward"/> (the way its wearer faces) the way a hose hangs off a chest.</summary>
    public void Hose(MeshBuilder mesh, Vector3 a, Vector3 b, Vector3 forward, string gear)
    {
        var k = new Kit(look, mesh);
        k.Use("boot", Palette.SootBlack, 0.5f, 0.25f, tile: 1);
        float r = HoseRadius(gear), bow = MathF.Min(0.06f, (a - b).Length() * 0.25f);
        var prev = a;
        for (int i = 1; i <= 4; i++)
        {
            float t = i / 4f;
            var p = Vector3.Lerp(a, b, t) + forward * (bow * 4 * t * (1 - t));
            k.Cylinder(prev, p, r, 6);
            prev = p;
        }
    }

    /// <summary>
    /// The crew clip a townsperson's act plays (<see cref="Sim.Towns.RoundStop.Act"/>; note 353): sat at a table or on a
    /// bench, crouched at the range, kneeling to mend at a crate, hands out to a fire barrel, hands on a stall's counter, a
    /// lamp held up, walking (with the lamp when they carry one), and in a word with somebody now and then pointing the way.
    /// </summary>
    public static string Clip(string act, bool lamp, int who) => act switch
    {
        "seated" => "gunner",
        "crouch" => "crouch_idle",
        "mend" => "mend",
        "warm" => "carry",
        "work" => "push",
        "lantern" => "lantern",
        "walk" => lamp ? "lantern_walk" : "walk",
        "talk" when who % 3 == 0 => "point",
        // Nicki's party (note 488): her guests dancing, Nicki waving you in.
        "dance" => "dance",
        "wave" => "wave",
        _ => "idle",
    };

    /// <summary>The figure a townsperson is drawn as: the survivors' bare-headed one, the wildlander's patched coat on one in three.</summary>
    public static string Figure(int variant, int who) => (variant + who) % 3 == 0 ? "survivor_wildlander" : "survivor_prisoner";

    /// <summary>
    /// A townsperson (note 353), their feet at <paramref name="feet"/> (the draw's space) facing <paramref name="facing"/>,
    /// playing <paramref name="clip"/> on their own beat, in their drab, with what they breathe through and a hat. Set down
    /// on their feet unless <paramref name="seated"/> (crouch_idle's are drawn half a metre up). At home
    /// (<paramref name="home"/>) half have the mask down on the chest and the hats are off but the headscarves. Returns the
    /// figure and where it's placed (to hang a lamp from its fist), or null when the figure isn't built.
    /// </summary>
    public (string Figure, Matrix4x4 At)? Person(CreatureArt creatures, MeshBuilder mesh, Vector3 feet, Vector3 facing, string clip, bool seated,
        string gear, bool home, int variant, int who, double time, float drab)
    {
        string figure = Figure(variant, who);
        if (creatures.Get(figure) is null)
            return null;
        var back = -Vector3.Normalize(facing with { Y = 0 });
        var right = Vector3.Cross(Vector3.UnitY, back);
        float lift = seated ? 0 : creatures.FeetOver(figure, clip);
        var m = CreatureArt.Basis(feet - Vector3.UnitY * lift, right, Vector3.UnitY, back);
        // The survivors' chest lamp is the crew's way of finding each other: a town's people don't wear one lit.
        if (!creatures.Draw(mesh, figure, clip, time, true, m, variant, seed: variant * 13,
            adjust: (mat, l) => l with { Colour = l.Colour * new Vector3(drab, drab * 0.95f, drab * 0.9f) * (mat.Emissive > 0 ? 0.15f : 1), Emissive = 0 }))
            return null;
        // (At Nicki's party, note 488, everyone's mask is down.)
        bool down = home && ((variant + who) % 2 == 0 || clip is "dance" or "wave") && gear != "rebreather";
        creatures.Wear(mesh, Face(gear, down), down ? "spine_03" : "head", m, figure);
        if (Body(gear) is { } body)
            creatures.Wear(mesh, body, "spine_03", m, figure);
        foreach (var (face, onBody) in Hoses(gear, down))
            Hose(mesh, creatures.Posed(down ? "spine_03" : "head", face, m, figure), creatures.Posed("spine_03", onBody, m, figure), -back, gear);
        int hat = (variant * 7 + who * 3) % Hats;
        if (Hat(hat) is { } piece && (!home || hat == 4))
            creatures.Wear(mesh, piece, "head", m, figure);
        return (figure, m);
    }

    MeshAsset Get(string key, Action<Kit> build)
    {
        if (_pieces.TryGetValue(key, out var piece))
            return piece;
        var k = new Kit(look);
        build(k);
        return _pieces[key] = k.Build("townsfolk." + key);
    }

    /// <summary>Places what's drawn next as a cup round an axis out of the face (its local +Y along −Z), from <paramref name="at"/>.</summary>
    static Matrix4x4 OutOfTheFace(Vector3 at) => Matrix4x4.CreateRotationX(-MathF.PI / 2) * Matrix4x4.CreateTranslation(at);

    /// <summary>The respirator: a black rubber half-mask over the nose and mouth, a can either side, its straps.</summary>
    static void HalfMask(Kit k, bool straps)
    {
        k.Use("boot", Palette.Charcoal, 0.55f, 0.3f, tile: 1);
        k.Tint = new Vector3(1.5f);
        var at = new Vector3(0, 1.612f, -0.105f);
        k.With(OutOfTheFace(at), () => k.Lathe(Vector3.Zero, [new(0.066f, 0), new(0.064f, 0.04f), new(0.05f, 0.075f), new(0.028f, 0.092f)], 12));
        // The cans, either cheek, angled out and down: grey painted tin with a darker face.
        k.Use("iron_plate", Palette.IronGrey, 0.6f, 0.3f, tile: 1);
        foreach (float side in (float[])[-1, 1])
        {
            var root = new Vector3(side * 0.045f, 1.59f, -0.165f);
            var tip = root + Vector3.Normalize(new Vector3(side * 0.7f, -0.35f, -0.65f)) * 0.055f;
            k.Cylinder(root, tip, 0.032f, 10);
            k.Use("boot", Palette.SootBlack, 0.6f, 0.2f, tile: 1);
            k.Disc(tip + Vector3.Normalize(tip - root) * 0.001f, Vector3.Normalize(tip - root), 0.024f, 10);
            k.Use("iron_plate", Palette.IronGrey, 0.6f, 0.3f, tile: 1);
        }
        // The exhale valve, a nub under the chin.
        k.Use("boot", Palette.SootBlack, 0.55f, 0.3f, tile: 1);
        k.Cylinder(new Vector3(0, 1.575f, -0.17f), new Vector3(0, 1.565f, -0.192f), 0.016f, 8);
        if (!straps)
            return;
        Straps(k, 1.625f, 0.012f, 0.95f);
        Straps(k, 1.70f, 0.012f, 1.1f);
    }

    /// <summary>The oxygen mask: an amber rubber cup, its elastic, and the hose's spigot under it.</summary>
    static void OxygenCup(Kit k, bool straps)
    {
        k.Use("lamp_lens", new Vector3(0.72f, 0.55f, 0.3f), 0.4f, 0.5f, tile: 0.25f);
        k.Tint = new Vector3(0.55f, 0.42f, 0.25f);
        var at = new Vector3(0, 1.615f, -0.11f);
        k.With(OutOfTheFace(at), () => k.Lathe(Vector3.Zero, [new(0.058f, 0), new(0.052f, 0.035f), new(0.034f, 0.068f), new(0.016f, 0.08f)], 12));
        k.Use("boot", Palette.SootBlack, 0.5f, 0.2f, tile: 1);
        var spigot = Mouth + new Vector3(0, -0.02f, -0.06f);
        k.Cylinder(spigot, Mouth + new Vector3(0.0f, -0.045f, -0.055f), 0.012f, 8);
        if (straps)
            Straps(k, 1.64f, 0.006f, 0.95f);
    }

    /// <summary>The rebreather's mouthpiece: the T-piece in the teeth, the nose clip, the head harness.</summary>
    static void Mouthpiece(Kit k)
    {
        k.Use("boot", Palette.SootBlack, 0.55f, 0.3f, tile: 1);
        k.BoxAt(Mouth + new Vector3(0, -0.005f, -0.03f), new Vector3(0.045f, 0.022f, 0.022f));
        k.Use("brass", Palette.TarnishedBrass, 0.4f, 0.5f);
        foreach (float side in (float[])[-1, 1])
            k.Cylinder(Mouth + new Vector3(side * 0.03f, -0.005f, -0.03f), Mouth + new Vector3(side * 0.035f, -0.01f, -0.05f), 0.014f, 8);
        // The nose clip, a brass spring over the nose.
        k.BoxAt(new Vector3(0, Nose - 0.01f, -0.152f), new Vector3(0.022f, 0.012f, 0.008f));
        // The head harness: a band round the back of the head, one over the crown.
        k.Use("leather", Palette.DeepBrown, 0.6f, 0.2f, tile: 1);
        Straps(k, 1.6f, 0.01f, 0.95f);
        var crown = new Vector3[9];
        var crownB = new Vector3[9];
        for (int i = 0; i < crown.Length; i++)
        {
            float a = -MathF.PI / 2 + MathF.PI * i / (crown.Length - 1);
            crown[i] = new Vector3(MathF.Sin(a) * (HeadHalfX + 0.006f), 1.62f + MathF.Cos(a) * (Crown - 1.62f + 0.006f), HeadZ + 0.03f);
            crownB[i] = crown[i] + new Vector3(0, 0, 0.02f);
        }
        k.Loft([crown, crownB], twoSided: true);
    }

    /// <summary>The wrap: wool wound over the nose and mouth, a tin can sewn into its front.</summary>
    static void Wrap(Kit k)
    {
        k.Use("scarf", Palette.MuddyOlive, 0.7f, 0, tile: 1);
        Band(k, 1.555f, 1.645f, HeadHalfX + 0.014f, HeadHalfZ + 0.022f, -MathF.PI, MathF.PI, closed: true, fuller: 0.01f);
        // A tail hanging at the back where it's knotted.
        k.BoxAt(new Vector3(0.03f, 1.53f, HeadZ + HeadHalfZ + 0.035f), new Vector3(0.03f, 0.07f, 0.008f));
        k.Use("iron_plate", Palette.IronGrey, 0.7f, 0.35f, tile: 1);
        k.Cylinder(new Vector3(0, 1.605f, -0.165f), new Vector3(0, 1.605f, -0.2f), 0.036f, 12);
        k.Use("boot", Palette.SootBlack, 0.6f, 0.2f, tile: 1);
        k.Disc(new Vector3(0, 1.605f, -0.201f), -Vector3.UnitZ, 0.026f, 12);
    }

    /// <summary>The oxygen's bottle slung on the back (its valve at the top, a sling over the right shoulder), and the hose's run over the shoulder.</summary>
    static void BottleOnTheBack(Kit k)
    {
        k.Use("paint_olive", new Vector3(0.22f, 0.36f, 0.24f), 0.55f, 0.35f, tile: 1);
        k.Tint = new Vector3(0.55f, 0.85f, 0.6f);
        k.Lathe(Bottle, [new(0.0f, 0), new(BottleRadius, 0.02f), new(BottleRadius, BottleHeight - 0.06f), new(0.03f, BottleHeight)], 12);
        k.Use("brass", Palette.TarnishedBrass, 0.4f, 0.5f);
        var top = Bottle + new Vector3(0, BottleHeight, 0);
        k.Cylinder(top, top + new Vector3(0, 0.05f, 0), 0.022f, 8);
        k.Cylinder(top + new Vector3(0, 0.05f, 0), top + new Vector3(0, 0.07f, 0), 0.035f, 8);
        // The sling: across the back from the bottle, over the right shoulder, down the chest to the left hip.
        k.Use("harness", Palette.DeepBrown, 0.6f, 0.15f, tile: 1);
        k.Rod(Bottle + new Vector3(0.02f, BottleHeight - 0.1f, 0.05f), new Vector3(0.15f, 1.52f, 0.08f), 0.02f);
        k.Rod(new Vector3(0.15f, 1.52f, 0.08f), new Vector3(0.16f, 1.53f, -0.08f), 0.02f);
        k.Rod(new Vector3(0.16f, 1.53f, -0.08f), new Vector3(-0.12f, 1.08f, -0.17f), 0.02f);
        k.Rod(Bottle + new Vector3(-0.02f, 0.1f, 0.05f), new Vector3(-0.15f, 1.05f, 0.12f), 0.02f);
        // The hose from the valve over the shoulder (the rest's drawn to the mask where it is).
        k.Use("boot", Palette.SootBlack, 0.5f, 0.25f, tile: 1);
        k.Cylinder(top + new Vector3(0, 0.06f, 0), new Vector3(0.13f, 1.56f, 0.1f), 0.009f, 6);
        k.Cylinder(new Vector3(0.13f, 1.56f, 0.1f), new Vector3(0.14f, 1.5f, -0.06f), 0.009f, 6);
    }

    /// <summary>The rebreather's bag on the chest with its scrubber can under it, and the harness over the shoulders.</summary>
    static void BagOnTheChest(Kit k)
    {
        k.Use("leather", new Vector3(0.3f, 0.26f, 0.2f), 0.6f, 0.2f, tile: 1);
        k.Tint = new Vector3(0.5f, 0.45f, 0.38f);
        k.BevelBox(Bag - new Vector3(0.12f, 0.1f, 0.04f), Bag + new Vector3(0.12f, 0.1f, 0.04f), 0.025f);
        k.Use("iron_plate", Palette.IronGrey, 0.6f, 0.3f, tile: 1);
        k.Cylinder(Bag + new Vector3(-0.11f, -0.135f, -0.005f), Bag + new Vector3(0.11f, -0.135f, -0.005f), 0.038f, 10);
        k.Use("brass", Palette.TarnishedBrass, 0.4f, 0.5f);
        foreach (float side in (float[])[-1, 1])
            k.Cylinder(Bag + new Vector3(side * 0.07f, 0.085f, -0.03f), Bag + new Vector3(side * 0.07f, 0.11f, -0.025f), 0.02f, 8);
        k.Use("harness", Palette.DeepBrown, 0.6f, 0.15f, tile: 1);
        foreach (float side in (float[])[-1, 1])
        {
            k.Rod(Bag + new Vector3(side * 0.1f, 0.09f, 0.0f), new Vector3(side * 0.13f, 1.52f, -0.1f), 0.018f);
            k.Rod(new Vector3(side * 0.13f, 1.52f, -0.1f), new Vector3(side * 0.13f, 1.5f, 0.12f), 0.018f);
            k.Rod(new Vector3(side * 0.13f, 1.5f, 0.12f), new Vector3(side * 0.12f, 1.05f, 0.15f), 0.018f);
        }
        k.Rod(new Vector3(-0.17f, 1.12f, -0.16f), new Vector3(0.17f, 1.12f, -0.16f), 0.02f);
    }

    /// <summary>A sou'wester: oilskin, the brim longer behind to shed the rain off the neck.</summary>
    static void Souwester(Kit k)
    {
        k.Use("coat_oilskin", Palette.HazardYellow, 0.55f, 0.35f, tile: 1);
        k.Tint = new Vector3(0.95f, 0.78f, 0.35f);
        var c = new Vector3(0, 1.69f, HeadZ);
        k.Lathe(c, [new(HeadHalfX + 0.025f, 0), new(HeadHalfX + 0.02f, 0.06f), new(HeadHalfX - 0.01f, Crown - c.Y + 0.02f), new(0.03f, Crown - c.Y + 0.045f)], 14);
        // The brim: a ring round the crown, sloped down, wider at the back.
        var outer = new Vector3[16];
        var inner = new Vector3[16];
        for (int i = 0; i < 16; i++)
        {
            float a = i * MathF.Tau / 16, back = MathF.Max(0, MathF.Cos(a));
            float r = HeadHalfX + 0.02f;
            float wide = 0.06f + 0.06f * back;
            inner[i] = c + new Vector3(MathF.Sin(a) * r, 0.0f, MathF.Cos(a) * (r + 0.02f));
            outer[i] = c + new Vector3(MathF.Sin(a) * (r + wide), -0.035f - 0.03f * back, MathF.Cos(a) * (r + 0.02f + wide));
        }
        k.Loft([inner, outer], closed: true, twoSided: true);
    }

    /// <summary>A knitted toque: a close dome to the ears, a turned-up cuff.</summary>
    static void Toque(Kit k)
    {
        k.Use("wool", Palette.RustRed, 0.7f, 0, tile: 1);
        k.Tint = new Vector3(0.6f, 0.3f, 0.25f);
        var c = new Vector3(0, 1.665f, HeadZ + 0.01f);
        k.Lathe(c, [new(HeadHalfX + 0.018f, 0), new(HeadHalfX + 0.02f, 0.05f), new(HeadHalfX + 0.012f, 0.1f), new(0.07f, Crown - c.Y + 0.02f), new(0.0f, Crown - c.Y + 0.035f)], 14);
        k.Shade(0.8f);
        k.Lathe(c, [new(HeadHalfX + 0.026f, 0), new(HeadHalfX + 0.028f, 0.045f)], 14, capTop: false);
    }

    /// <summary>A flat cap: the crown flat over the head, the peak out over the eyes.</summary>
    static void Cap(Kit k)
    {
        k.Use("cap", Palette.Charcoal, 0.6f, 0.05f, tile: 1);
        var c = new Vector3(0, 1.73f, HeadZ - 0.01f);
        k.Lathe(c, [new(HeadHalfX + 0.018f, 0), new(HeadHalfX + 0.03f, 0.04f), new(HeadHalfX + 0.01f, 0.07f), new(0, 0.08f)], 14);
        var peak = new Vector3[] { c + new Vector3(-0.09f, 0.005f, -0.08f), c + new Vector3(0, 0.01f, -0.115f), c + new Vector3(0.09f, 0.005f, -0.08f) };
        var lip = new Vector3[] { c + new Vector3(-0.08f, -0.012f, -0.12f), c + new Vector3(0, -0.01f, -0.175f), c + new Vector3(0.08f, -0.012f, -0.12f) };
        k.Loft([peak, lip], twoSided: true);
    }

    /// <summary>A headscarf: over the hair, tied under the chin.</summary>
    static void Headscarf(Kit k)
    {
        k.Use("scarf", Palette.BlueGrey, 0.6f, 0, tile: 1);
        k.Tint = new Vector3(0.36f, 0.3f, 0.34f);
        var c = new Vector3(0, 1.62f, HeadZ + 0.012f);
        k.Lathe(c, [new(HeadHalfX + 0.015f, 0.02f), new(HeadHalfX + 0.02f, 0.08f), new(HeadHalfX + 0.012f, 0.13f), new(0.06f, Crown - c.Y + 0.018f), new(0, Crown - c.Y + 0.026f)], 14);
        // The sides down past the ears to the knot under the chin.
        foreach (float side in (float[])[-1, 1])
            k.Rod(new Vector3(side * (HeadHalfX + 0.01f), 1.63f, -0.04f), new Vector3(side * 0.03f, 1.545f, -0.1f), 0.018f);
        k.BoxAt(new Vector3(0, 1.54f, -0.105f), new Vector3(0.025f, 0.02f, 0.018f));
    }

    /// <summary>A strap round the back of the head at <paramref name="y"/>, from one side of the face to the other.</summary>
    static void Straps(Kit k, float y, float half, float spread) =>
        Band(k, y - half, y + half, HeadHalfX + 0.006f * spread, HeadHalfZ + 0.006f * spread, -MathF.PI * 0.7f, MathF.PI * 0.7f, closed: false, fuller: 0);

    /// <summary>
    /// A band round the head between <paramref name="y0"/> and <paramref name="y1"/>, an ellipse <paramref name="rx"/> by
    /// <paramref name="rz"/> round its middle, from angle <paramref name="from"/> to <paramref name="to"/> (0 at the back,
    /// ±π at the face), <paramref name="fuller"/> bulging its middle out.
    /// </summary>
    static void Band(Kit k, float y0, float y1, float rx, float rz, float from, float to, bool closed, float fuller)
    {
        int n = closed ? 18 : 12;
        var low = new Vector3[n];
        var mid = new Vector3[n];
        var high = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            float a = from + (to - from) * i / (closed ? n : n - 1);
            Vector3 P(float y, float grow) => new(MathF.Sin(a) * (rx + grow), y, HeadZ + MathF.Cos(a) * (rz + grow));
            low[i] = P(y0, 0);
            mid[i] = P((y0 + y1) / 2, fuller);
            high[i] = P(y1, 0);
        }
        k.Loft([low, mid, high], closed: closed, twoSided: true);
    }
}
