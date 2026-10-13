using System.Numerics;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

/// <summary>
/// A town character from the director's lookbook (note 694): who they are, which of a town's roles they're given to
/// (towns.json <c>roles</c>; <c>anyone</c> for the town's other people), and what they're dressed in on the survivors'
/// figure (<see cref="Redress"/>: their coat and scarf kept and dyed when they wear one).
/// </summary>
public sealed record TownCharacter(string Id, string Name, string[] Roles, IReadOnlyDictionary<Cloth, Dye> Clothes,
    Func<Vector3, string, Vector3>? Shape = null, IReadOnlyList<Growth>? Growths = null)
{
    /// <summary>Their dressed figure's name (CreatureArt's dressed figures).</summary>
    public string Figure => "town." + Id;
}

/// <summary>
/// The townsfolk lookbook (the director, 13 Oct 2026, with a sheet of ten town characters: "We want to create more NPC
/// models. We want to be able to populate the town with interesting characters that look cool"; note 694). Built as Dave,
/// Nicki and Jacob are (notes 570-572; no Blender in this container): the survivors' figure in each one's own colours, and
/// what makes them who they are made here and worn on its bones or held in its fists:
/// <list type="bullet">
/// <item>the trapper: a yellow oilskin coat, a bucket hat, a gas mask's snout and can, a gaff and a lobster pot;</item>
/// <item>the watchman: a padded coat, a fur ushanka, binoculars on his chest, a red-and-white flag;</item>
/// <item>the market woman: a red headscarf, a plaid shawl, an apron over a red skirt, keys at her belt, a crate of greens;</item>
/// <item>the lamplighter: a hooded brown coat, a lamp burning before his mask, its bottles on his back, a lamp on a pole;</item>
/// <item>the linesman: a flat cap with goggles on it, a wrap over his face, a tool belt, a coil of wire at his hip;</item>
/// <item>the preacher: a black cassock, a wide hat, a veil over his face, a stole and a cross;</item>
/// <item>the butcher: a knitted cap, a gas mask, a stained apron, his sleeves rolled, a knife;</item>
/// <item>the harbour master: a navy greatcoat, double-breasted in brass, a peaked cap, a white beard, an eyeglass, keys, a brass glass;</item>
/// <item>the drifter: a long brown coat, a flat cap, a red scarf over his face, a flask;</item>
/// <item>the schoolteacher: a green tweed coat over a long skirt, her hair up, spectacles, books under her arm.</item>
/// </list>
/// The pieces are made in the figure's bind pose (the survivors': a T-pose, the hands at x ±0.72, 1.45 m; −Z the front)
/// and worn on a bone (<see cref="CreatureArt.Wear"/>), or held (<see cref="CreatureArt.Hold"/>): standing upright from
/// the fist (a pole, a flag), or in it on the hand's socket (a knife, a flask).
/// </summary>
public sealed class LookbookKit(Look? look)
{
    readonly Dictionary<string, MeshAsset> _pieces = [];

    /// <summary>
    /// A flat cloth dye. The values below are written as the lookbook's colours read; the renderer shows a flat dye about
    /// twice as light as its value (the lit, toned colour), so they're taken down by <see cref="Muted"/> to sit with the
    /// lookbook's dark, worn palette.
    /// </summary>
    static Dye Flat(float r, float g, float b, float shine = 0.06f) => new("cream", new Vector3(r, g, b) * Muted, shine);

    /// <summary>How far the lookbook's colours are taken down (see <see cref="Flat"/>).</summary>
    const float Muted = 0.45f;

    // The survivors' head and body in the bind pose (measured off survivor_prisoner.glb: the head's front −0.12, back +0.095,
    // ±0.097 at the ears, its crown 1.794 m; the mouth 1.60, the eyes 1.655 at z −0.128; the chest's front −0.17 at spine_03;
    // the waist 0.94; the coat from its collar at 1.62 to its hem at 0.475).
    const float Mid = -0.015f, HeadZ = -0.026f, HeadHalfX = 0.1f, HeadHalfZ = 0.125f, Crown = 1.794f, EyeY = 1.655f, EyeZ = -0.128f;
    static readonly Vector3 Mouth = new(0, 1.60f, -0.136f);

    static readonly Dye Skin = new(null, new Vector3(1.1f, 1.0f, 0.95f), 0.1f), Weathered = new(null, new Vector3(1.0f, 0.88f, 0.8f), 0.1f);
    static readonly Dye Gloves = Flat(0.08f, 0.075f, 0.07f, 0.15f), Boots = Flat(0.1f, 0.085f, 0.07f, 0.2f);

    /// <summary>The ten, in the lookbook's order (its top row, then its bottom).</summary>
    public static readonly IReadOnlyList<TownCharacter> Characters =
    [
        new("trapper", "the trapper", ["porter", "anyone"], new Dictionary<Cloth, Dye>
        {
            [Cloth.Collar] = Flat(0.7f, 0.54f, 0.18f, 0.3f), [Cloth.Face] = Weathered, [Cloth.Hands] = Gloves,
            [Cloth.Coat] = Flat(0.78f, 0.6f, 0.2f, 0.3f),
            [Cloth.Sleeves] = Flat(0.72f, 0.55f, 0.18f, 0.3f),
            [Cloth.Forearms] = Flat(0.72f, 0.55f, 0.18f, 0.3f),
            [Cloth.Shirt] = Flat(0.72f, 0.55f, 0.18f, 0.3f), [Cloth.Trousers] = Flat(0.17f, 0.15f, 0.12f), [Cloth.Feet] = Boots,
        }),
        new("watchman", "the watchman", ["guard", "anyone"], new Dictionary<Cloth, Dye>
        {
            [Cloth.Collar] = Flat(0.16f, 0.16f, 0.15f), [Cloth.Face] = Weathered, [Cloth.Hands] = Gloves,
            [Cloth.Coat] = Flat(0.26f, 0.27f, 0.24f, 0.04f),
            [Cloth.Sleeves] = Flat(0.24f, 0.25f, 0.22f), [Cloth.Forearms] = Flat(0.24f, 0.25f, 0.22f),
            [Cloth.Shirt] = Flat(0.2f, 0.2f, 0.18f), [Cloth.Trousers] = Flat(0.16f, 0.15f, 0.13f), [Cloth.Feet] = Boots,
        }),
        new("market", "the market woman", ["storekeeper", "anyone"], new Dictionary<Cloth, Dye>
        {
            [Cloth.Collar] = Flat(0.3f, 0.27f, 0.23f), [Cloth.Face] = Weathered, [Cloth.Hands] = Gloves,
            [Cloth.Shirt] = Flat(0.3f, 0.27f, 0.23f), [Cloth.Sleeves] = Flat(0.3f, 0.27f, 0.23f), [Cloth.Forearms] = Flat(0.3f, 0.27f, 0.23f),
            [Cloth.Trousers] = Flat(0.12f, 0.1f, 0.09f), [Cloth.Feet] = Boots,
        }, Shape: Sturdy),
        new("lamplighter", "the lamplighter", ["lampman"], new Dictionary<Cloth, Dye>
        {
            [Cloth.Collar] = Flat(0.2f, 0.16f, 0.12f), [Cloth.Face] = Weathered, [Cloth.Hands] = Gloves,
            [Cloth.Coat] = Flat(0.33f, 0.26f, 0.18f, 0.04f),
            [Cloth.Sleeves] = Flat(0.3f, 0.23f, 0.16f), [Cloth.Forearms] = Flat(0.3f, 0.23f, 0.16f),
            [Cloth.Shirt] = Flat(0.26f, 0.2f, 0.14f), [Cloth.Trousers] = Flat(0.16f, 0.14f, 0.11f), [Cloth.Feet] = Boots,
        }),
        new("linesman", "the linesman", ["fitter", "anyone"], new Dictionary<Cloth, Dye>
        {
            [Cloth.Collar] = Flat(0.3f, 0.3f, 0.24f), [Cloth.Face] = Weathered, [Cloth.Hands] = Flat(0.28f, 0.2f, 0.13f, 0.15f),
            [Cloth.Scarf] = Flat(0.45f, 0.4f, 0.3f),
            [Cloth.Shirt] = Flat(0.3f, 0.3f, 0.24f), [Cloth.Sleeves] = Flat(0.3f, 0.3f, 0.24f), [Cloth.Forearms] = Flat(0.3f, 0.3f, 0.24f),
            [Cloth.Trousers] = Flat(0.2f, 0.19f, 0.16f), [Cloth.Feet] = Flat(0.36f, 0.3f, 0.22f, 0.15f),
        }, Shape: Lean),
        new("preacher", "the preacher", ["keeper"], new Dictionary<Cloth, Dye>
        {
            [Cloth.Collar] = Flat(0.08f, 0.075f, 0.07f), [Cloth.Face] = Skin, [Cloth.Hands] = Gloves,
            [Cloth.Coat] = Flat(0.09f, 0.085f, 0.08f, 0.04f),
            [Cloth.Sleeves] = Flat(0.08f, 0.075f, 0.07f), [Cloth.Forearms] = Flat(0.08f, 0.075f, 0.07f),
            [Cloth.Shirt] = Flat(0.07f, 0.065f, 0.06f), [Cloth.Trousers] = Flat(0.07f, 0.065f, 0.06f), [Cloth.Feet] = Boots,
        }),
        new("butcher", "the butcher", ["cook", "anyone"], new Dictionary<Cloth, Dye>
        {
            [Cloth.Collar] = Flat(0.42f, 0.4f, 0.36f), [Cloth.Face] = Weathered, [Cloth.Hands] = Gloves,
            [Cloth.Shirt] = Flat(0.42f, 0.4f, 0.36f), [Cloth.Sleeves] = Flat(0.42f, 0.4f, 0.36f),
            [Cloth.Forearms] = Flat(0.6f, 0.44f, 0.36f, 0.1f),
            [Cloth.Trousers] = Flat(0.16f, 0.15f, 0.13f), [Cloth.Feet] = Boots,
        }, Shape: Broad),
        new("harbourmaster", "the harbour master", ["gatekeeper"], new Dictionary<Cloth, Dye>
        {
            [Cloth.Collar] = Flat(0.09f, 0.1f, 0.15f), [Cloth.Face] = Weathered, [Cloth.Hands] = Gloves,
            [Cloth.Coat] = Flat(0.09f, 0.1f, 0.15f, 0.05f),
            [Cloth.Sleeves] = Flat(0.09f, 0.1f, 0.15f), [Cloth.Forearms] = Flat(0.09f, 0.1f, 0.15f),
            [Cloth.Shirt] = Flat(0.09f, 0.1f, 0.15f), [Cloth.Trousers] = Flat(0.08f, 0.08f, 0.1f), [Cloth.Feet] = Boots,
        }, Shape: Broad, Growths: [new("beard", Beard, 0.008f, Flat(0.62f, 0.6f, 0.56f, 0.02f)),
            new("hair", p => p.Y > 1.6f && p.Y < 1.72f && p.Z > -0.02f, 0.005f, Flat(0.6f, 0.58f, 0.55f, 0.02f))]),
        new("drifter", "the drifter", ["driver", "anyone"], new Dictionary<Cloth, Dye>
        {
            [Cloth.Collar] = Flat(0.4f, 0.1f, 0.08f), [Cloth.Face] = Weathered, [Cloth.Hands] = new(null, new Vector3(0.95f, 0.85f, 0.78f), 0.1f),
            [Cloth.Coat] = Flat(0.24f, 0.18f, 0.13f, 0.04f),
            [Cloth.Scarf] = Flat(0.4f, 0.1f, 0.08f),
            [Cloth.Sleeves] = Flat(0.22f, 0.16f, 0.12f), [Cloth.Forearms] = Flat(0.22f, 0.16f, 0.12f),
            [Cloth.Shirt] = Flat(0.16f, 0.12f, 0.09f), [Cloth.Trousers] = Flat(0.26f, 0.21f, 0.15f), [Cloth.Feet] = Boots,
        }, Shape: Lean),
        new("teacher", "the schoolteacher", ["clerk", "anyone"], new Dictionary<Cloth, Dye>
        {
            [Cloth.Collar] = Flat(0.55f, 0.5f, 0.42f), [Cloth.Face] = Skin, [Cloth.Hands] = Skin,
            [Cloth.Coat] = Flat(0.24f, 0.28f, 0.2f, 0.04f),
            [Cloth.Scarf] = Flat(0.55f, 0.5f, 0.42f),
            [Cloth.Sleeves] = Flat(0.24f, 0.28f, 0.2f), [Cloth.Forearms] = Flat(0.24f, 0.28f, 0.2f),
            [Cloth.Shirt] = Flat(0.5f, 0.46f, 0.4f), [Cloth.Trousers] = Flat(0.16f, 0.14f, 0.12f), [Cloth.Feet] = Boots,
        }, Shape: NickiKit.Shape, Growths: [new("hair", p => p.Y > 1.6f && p.Z > -0.06f || p.Y > 1.7f, 0.009f, Flat(0.2f, 0.12f, 0.08f, 0.04f))]),
    ];

    /// <summary>The character called <paramref name="id"/>, or null.</summary>
    public static TownCharacter? Of(string id) => Characters.FirstOrDefault(c => c.Id == id);

    /// <summary>
    /// Who a townsperson in <paramref name="role"/> is drawn as (note 694): the character given that role, if one is; and
    /// for the town's other people (<c>anyone</c>) one of those who go about the town, for one person in two, by who they
    /// are (the same every night). Null: the town's drab figure, as before.
    /// </summary>
    public static TownCharacter? For(string role, int who)
    {
        if (Characters.FirstOrDefault(c => c.Roles[0] == role) is { } own)
            return own;
        if (role != "anyone" || (who * 2654435761u >> 7) % 2 == 0)
            return null;
        var common = Characters.Where(c => c.Roles.Contains("anyone")).ToList();
        return common[(int)((who * 40503u >> 3) % (uint)common.Count)];
    }

    /// <summary>What each wears, on which bone.</summary>
    static (string Piece, string Bone)[] Worn(string id) => id switch
    {
        "trapper" => [("bucket", "head"), ("snout", "head")],
        "watchman" => [("ushanka", "head"), ("binoculars", "spine_03")],
        "market" => [("redscarf", "head"), ("shawl", "spine_03"), ("skirt", "pelvis"), ("apron", "pelvis"), ("keys", "pelvis")],
        "lamplighter" => [("hood", "head"), ("lampmask", "head"), ("bottles", "spine_03")],
        "linesman" => [("capgoggles", "head"), ("wrap", "head"), ("toolbelt", "pelvis"), ("coil", "pelvis")],
        "preacher" => [("widehat", "head"), ("veil", "head"), ("stole", "spine_03"), ("cross", "spine_03")],
        "butcher" => [("beanie", "head"), ("snout", "head"), ("bibapron", "spine_02")],
        "harbourmaster" => [("captcap", "head"), ("eyeglass", "head"), ("buttons", "spine_02"), ("keys", "pelvis")],
        "drifter" => [("flatcap", "head"), ("redwrap", "head")],
        "teacher" => [("bun", "head"), ("spectacles", "head"), ("longskirt", "pelvis")],
        _ => [],
    };

    /// <summary>What each carries: in the left fist or the right, standing upright from it (a pole) or on the hand's socket.</summary>
    static (string Piece, bool Left, bool Upright)[] Held(string id) => id switch
    {
        "trapper" => [("gaff", false, true), ("pot", true, true)],
        "watchman" => [("flag", false, true)],
        "market" => [("crate", true, true)],
        "lamplighter" => [("lamppole", false, true)],
        "preacher" => [("handcross", false, false)],
        "butcher" => [("knife", false, false)],
        "harbourmaster" => [("spyglass", false, false)],
        "drifter" => [("flask", false, false)],
        "teacher" => [("books", true, true)],
        _ => [],
    };

    /// <summary>Whether a character carries something in their right fist (a townsperson's hand lamp would be in it).</summary>
    public static bool RightHandFull(string id) => Held(id).Any(h => !h.Left);

    /// <summary>The lamplighter's lamps (his mask's on his head, his pole's over his fist), where their flames are.</summary>
    public static readonly Vector3 MaskFlame = new(0, 1.64f, -0.245f), PoleFlame = new(0, 0.92f, -0.16f);

    /// <summary>
    /// Puts on <paramref name="c"/>, as drawn last at <paramref name="model"/>, what they wear; and in their fists what they
    /// carry, unless <paramref name="hands"/> is false (sat at a table, at the range, at work: their hands are busy).
    /// <paramref name="far"/>: drawn as instances (a far figure's, note 479). Lights its lamps (the lamplighter's).
    /// </summary>
    public void Dress(CreatureArt creatures, MeshBuilder mesh, in Matrix4x4 model, TownCharacter c, bool hands = true, bool far = false)
    {
        foreach (var (piece, bone) in Worn(c.Id))
            creatures.Wear(mesh, Get(piece), bone, model, c.Figure, instanced: far);
        if (hands)
            foreach (var (piece, left, upright) in Held(c.Id))
                creatures.Hold(mesh, Get(piece), model, c.Figure, left, upright);
        if (c.Id != "lamplighter")
            return;
        var mask = creatures.Posed("head", MaskFlame, model, c.Figure);
        mesh.PointLights.Add(new PointLight(mask, Palette.LampAmber * 0.9f, 5));
        mesh.Billboard(mask, 0.3f, 0, new Vector4(Palette.LampAmber * 0.6f, 1), -1, FxBlend.Additive);
        if (hands && creatures.HoldAt(model, c.Figure, false) is { } fist)
        {
            var up = Vector3.Normalize(new Vector3(model.M21, model.M22, model.M23));
            var fwd = -Vector3.Normalize(new Vector3(model.M31, model.M32, model.M33));
            var flame = fist + up * PoleFlame.Y + fwd * -PoleFlame.Z;
            mesh.PointLights.Add(new PointLight(flame, Palette.LampAmber * 1.4f, 9));
            mesh.Billboard(flame, 0.45f, 0, new Vector4(Palette.LampAmber * 0.7f, 1), -1, FxBlend.Additive);
        }
    }

    MeshAsset Get(string key)
    {
        if (_pieces.TryGetValue(key, out var piece))
            return piece;
        var k = new Kit(look, 6100 + key.Sum(ch => ch) % 97);
        switch (key)
        {
            case "bucket": Bucket(k); break;
            case "snout": Snout(k); break;
            case "gaff": Gaff(k); break;
            case "pot": Pot(k); break;
            case "ushanka": Ushanka(k); break;
            case "binoculars": Binoculars(k); break;
            case "flag": Flag(k); break;
            case "redscarf": Headscarf(k, new Vector3(0.42f, 0.11f, 0.09f)); break;
            case "shawl": Shawl(k); break;
            case "skirt": Skirt(k, new Vector3(0.4f, 0.09f, 0.07f), 0.98f, 0.3f, 0.33f); break;
            case "longskirt": Skirt(k, new Vector3(0.17f, 0.15f, 0.13f), 0.7f, 0.12f, 0.27f); break;
            case "apron": Apron(k); break;
            case "keys": Keys(k); break;
            case "crate": Crate(k); break;
            case "hood": Hood(k); break;
            case "lampmask": LampMask(k); break;
            case "bottles": Bottles(k); break;
            case "lamppole": LampPole(k); break;
            case "capgoggles": FlatCap(k, new Vector3(0.24f, 0.25f, 0.21f), goggles: true); break;
            case "flatcap": FlatCap(k, new Vector3(0.26f, 0.2f, 0.14f), goggles: false); break;
            case "wrap": Wrap(k, new Vector3(0.38f, 0.34f, 0.26f)); break;
            case "redwrap": Wrap(k, new Vector3(0.42f, 0.1f, 0.08f)); break;
            case "toolbelt": ToolBelt(k); break;
            case "coil": Coil(k); break;
            case "widehat": WideHat(k); break;
            case "veil": Veil(k); break;
            case "stole": Stole(k); break;
            case "cross": Cross(k); break;
            case "handcross": HandCross(k); break;
            case "beanie": Beanie(k); break;
            case "bibapron": BibApron(k); break;
            case "knife": Knife(k); break;
            case "captcap": CaptainsCap(k); break;
            case "eyeglass": Eyeglass(k); break;
            case "buttons": Buttons(k); break;
            case "spyglass": Spyglass(k); break;
            case "flask": Flask(k); break;
            case "bun": Bun(k); break;
            case "spectacles": Spectacles(k); break;
            case "books": Books(k); break;
        }
        return _pieces[key] = k.Build("lookbook." + key);
    }

    /// <summary>Every piece the lookbook makes, built (for the tests' triangle counts).</summary>
    public IEnumerable<(string Key, MeshAsset Piece)> All() =>
        Characters.SelectMany(c => Worn(c.Id).Select(w => w.Piece).Concat(Held(c.Id).Select(h => h.Piece))).Distinct().Select(key => (key, Get(key)));

    // ------------------------------------------------------------------ shapes

    /// <summary>A broad, heavy frame: wider at the belly and the shoulders (the butcher, the harbour master).</summary>
    static Vector3 Broad(Vector3 p, string bone) => bone switch
    {
        "spine_01" or "spine_02" or "pelvis" => new Vector3(p.X * 1.1f, p.Y, p.Z * 1.12f + (p.Z < 0 ? -0.01f : 0)),
        "spine_03" => new Vector3(p.X * 1.07f, p.Y, p.Z * 1.06f),
        _ => p,
    };

    /// <summary>A sturdy woman's frame: the market woman's.</summary>
    static Vector3 Sturdy(Vector3 p, string bone) => bone switch
    {
        "spine_01" or "pelvis" => new Vector3(p.X * 1.08f, p.Y, p.Z * 1.08f),
        "spine_02" or "spine_03" => new Vector3(p.X * 0.97f, p.Y, p.Z * 1.04f),
        _ => p,
    };

    /// <summary>A lean, rangy frame: the linesman, the drifter.</summary>
    static Vector3 Lean(Vector3 p, string bone) => bone switch
    {
        "spine_01" or "spine_02" or "pelvis" => new Vector3(p.X * 0.93f, p.Y, p.Z * 0.92f),
        _ => p,
    };

    /// <summary>The harbour master's beard: full and white, from the sideburns round the jaw and over the lip (Jacob's region, grown longer).</summary>
    static bool Beard(Vector3 p)
    {
        if (p.Y < 1.49f || p.Z > -0.005f)
            return false;
        float off = MathF.Abs(MathF.Atan2(p.X, -(p.Z - Mid)));
        return off < 0.95f ? p.Y <= 1.616f : p.Y <= 1.66f && off < 1.8f;
    }

    // ------------------------------------------------------------------ helpers

    static void Colour(Kit k, Vector3 colour, float wear = 0.5f, float shine = 0.05f)
    {
        k.Use("cream", colour * Muted, wear, shine, tile: 1);
        k.Tint = colour * Muted;
    }

    static void Brass(Kit k) => k.Use("brass", Palette.TarnishedBrass, 0.4f, 0.6f);
    static void Iron(Kit k) => k.Use("iron_plate", Palette.IronGrey, 0.6f, 0.3f, tile: 1);
    static void Wood(Kit k) => k.Use("wood_grey", Palette.DeepBrown, 0.8f, 0.05f, tile: 1);
    static void Rubber(Kit k) => Colour(k, new Vector3(0.07f, 0.065f, 0.06f), 0.5f, 0.25f);
    static void Glow(Kit k, Vector3 colour, float emissive)
    {
        Colour(k, colour, 0.1f, 0.5f);
        k.Emissive = emissive;
    }

    /// <summary>A ring round the head at <paramref name="y"/>, an ellipse grown by <paramref name="grow"/>, round its middle.</summary>
    static Vector3[] HeadRing(float y, float grow, int n = 18, float from = -MathF.PI, float to = MathF.PI, bool closed = true)
    {
        var ring = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            float a = from + (to - from) * i / (closed ? n : n - 1);
            ring[i] = new Vector3(MathF.Sin(a) * (HeadHalfX + grow), y, HeadZ + MathF.Cos(a) * (HeadHalfZ + grow));
        }
        return ring;
    }

    /// <summary>A brim: from round the crown at <paramref name="y"/> out by <paramref name="wide"/> (more behind by
    /// <paramref name="back"/>), its edge dropped by <paramref name="droop"/>.</summary>
    static void Brim(Kit k, float y, float grow, float wide, float droop, float back = 0, float front = 0)
    {
        const int n = 20;
        var inner = new Vector3[n];
        var outer = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            float a = i * MathF.Tau / n, c = MathF.Cos(a);
            float w = wide + back * MathF.Max(0, c) + front * MathF.Max(0, -c);
            inner[i] = new Vector3(MathF.Sin(a) * (HeadHalfX + grow), y, HeadZ + c * (HeadHalfZ + grow));
            outer[i] = new Vector3(MathF.Sin(a) * (HeadHalfX + grow + w), y - droop, HeadZ + c * (HeadHalfZ + grow + w));
        }
        k.Loft([inner, outer], closed: true, twoSided: true);
    }

    /// <summary>A crown over the head from <paramref name="y0"/> up to <paramref name="top"/>, rounded off.</summary>
    static void Dome(Kit k, float y0, float grow, float top, float taper = 0.7f)
    {
        var rings = new List<Vector3[]>();
        for (int i = 0; i <= 5; i++)
        {
            float t = i / 5f, y = y0 + (top - y0) * t;
            float g = grow - (HeadHalfX + grow) * (1 - taper) * t * t;
            rings.Add(HeadRing(y, g));
        }
        rings.Add(HeadRing(top + 0.008f, -HeadHalfX * 0.6f));
        k.Loft(rings, closed: true, twoSided: true);
        k.Cap(rings[^1], Vector3.UnitY);
    }

    // ------------------------------------------------------------------ the trapper

    /// <summary>A bucket hat in oilskin: a soft crown, its brim turned down all round.</summary>
    static void Bucket(Kit k)
    {
        Colour(k, new Vector3(0.7f, 0.52f, 0.18f), 0.6f, 0.25f);
        Dome(k, 1.69f, 0.03f, 1.84f, 0.8f);
        Brim(k, 1.695f, 0.03f, 0.075f, 0.06f, back: 0.02f);
    }

    /// <summary>
    /// A gas mask's snout (the trapper's, the butcher's): a black rubber facepiece over the nose and mouth up to the eyes,
    /// two round glass eyepieces, one round filter can screwed into its front, and its straps round the head.
    /// </summary>
    static void Snout(Kit k)
    {
        Rubber(k);
        var rings = new List<Vector3[]>();
        // The facepiece: the face's front from the brow to under the chin (π is the face), pushed out toward the can.
        foreach (var (y, grow, reach) in new[] { (1.70f, 0.012f, 0f), (1.655f, 0.02f, 0.012f), (1.6f, 0.026f, 0.04f), (1.55f, 0.018f, 0.03f), (1.52f, 0.0f, 0.0f) })
            rings.Add([.. HeadRing(y, grow, 13, MathF.PI * 0.55f, MathF.PI * 1.45f, closed: false)
                .Select(v => v + new Vector3(0, 0, -reach * MathF.Max(0, -(v.Z - HeadZ) / HeadHalfZ)))]);
        k.Loft(rings, twoSided: true);
        // The eyepieces: brass-rimmed rounds of dark glass.
        foreach (float side in (float[])[-1, 1])
        {
            var eye = new Vector3(side * 0.042f, EyeY, EyeZ - 0.03f);
            Brass(k);
            k.Cylinder(eye + new Vector3(0, 0, 0.012f), eye - new Vector3(0, 0, 0.006f), 0.028f, 12);
            Glow(k, new Vector3(0.08f, 0.1f, 0.1f), 0.05f);
            k.Disc(eye - new Vector3(0, 0, 0.007f), -Vector3.UnitZ, 0.022f, 12);
            k.Emissive = 0;
        }
        // The can: olive tin, screwed in under the nose, its grille dark.
        k.Use("paint_olive", new Vector3(0.24f, 0.26f, 0.18f), 0.6f, 0.3f, tile: 1);
        k.Tint = new Vector3(0.36f, 0.38f, 0.26f);
        var root = Mouth + new Vector3(0, -0.012f, -0.05f);
        var tip = root + new Vector3(0, -0.035f, -0.075f);
        k.Cylinder(root, tip, 0.045f, 14);
        Rubber(k);
        k.Disc(tip + Vector3.Normalize(tip - root) * 0.001f, Vector3.Normalize(tip - root), 0.034f, 14);
        // The straps round the back of the head.
        Colour(k, new Vector3(0.12f, 0.1f, 0.08f), 0.6f, 0.1f);
        foreach (float y in (float[])[1.6f, 1.7f])
            k.Loft([HeadRing(y - 0.012f, 0.007f, 12, -MathF.PI * 0.75f, MathF.PI * 0.75f, false), HeadRing(y + 0.012f, 0.007f, 12, -MathF.PI * 0.75f, MathF.PI * 0.75f, false)], twoSided: true);
    }

    /// <summary>A gaff: a long ash pole with an iron hook at its head, held upright, the grip at the origin.</summary>
    static void Gaff(Kit k)
    {
        Wood(k);
        k.Cylinder(new Vector3(0, -0.85f, 0), new Vector3(0, 0.95f, 0), 0.02f, 7);
        Iron(k);
        k.Shade(0.6f);
        k.Cylinder(new Vector3(0, 0.9f, 0), new Vector3(0, 1.08f, 0), 0.012f, 6);
        k.Cylinder(new Vector3(0, 1.08f, 0), new Vector3(0, 1.12f, -0.06f), 0.01f, 6);
        k.Cylinder(new Vector3(0, 1.12f, -0.06f), new Vector3(0, 1.08f, -0.11f), 0.009f, 6);
        k.Cylinder(new Vector3(0, 1.08f, -0.11f), new Vector3(0, 1.02f, -0.1f), 0.007f, 6);
        // A net bundled on it, under the hook: a few hanging lengths of twine.
        Colour(k, new Vector3(0.35f, 0.32f, 0.24f), 0.7f, 0);
        for (int i = 0; i < 5; i++)
        {
            float a = i * 1.26f;
            k.Rod(new Vector3(0, 0.82f, 0), new Vector3(MathF.Sin(a) * 0.09f, 0.4f + 0.05f * (i % 2), MathF.Cos(a) * 0.09f), 0.006f, 3);
        }
    }

    /// <summary>A lobster pot carried by its rope at the left fist: a half-round cage of laths on a board floor, its funnel's mouth in the end.</summary>
    static void Pot(Kit k)
    {
        const float L = 0.5f, W = 0.32f, H = 0.26f, Y0 = -0.55f, X = -0.12f;
        Wood(k);
        k.Tint = new Vector3(0.6f, 0.5f, 0.38f);
        k.Box(new Vector3(X - W / 2, Y0, -L / 2), new Vector3(X + W / 2, Y0 + 0.02f, L / 2));
        // Three bows over, laths along them.
        foreach (float z in (float[])[-L / 2, 0, L / 2])
            for (int i = 0; i < 6; i++)
            {
                float a0 = MathF.PI * i / 6, a1 = MathF.PI * (i + 1) / 6;
                k.Rod(new Vector3(X - MathF.Cos(a0) * W / 2, Y0 + MathF.Sin(a0) * H, z), new Vector3(X - MathF.Cos(a1) * W / 2, Y0 + MathF.Sin(a1) * H, z), 0.008f, 3);
            }
        for (int i = 0; i <= 6; i++)
        {
            float a = MathF.PI * i / 6;
            var p = new Vector3(X - MathF.Cos(a) * W / 2, Y0 + MathF.Sin(a) * H, 0);
            k.Rod(p with { Z = -L / 2 }, p with { Z = L / 2 }, 0.006f, 3);
        }
        // The rope up to the fist.
        Colour(k, new Vector3(0.45f, 0.4f, 0.3f), 0.7f, 0);
        k.Rod(new Vector3(X, Y0 + H, -0.15f), new Vector3(0, 0, 0), 0.008f, 3);
        k.Rod(new Vector3(X, Y0 + H, 0.15f), new Vector3(0, 0, 0), 0.008f, 3);
    }

    // ------------------------------------------------------------------ the watchman

    /// <summary>A fur ushanka: the crown in grey cloth, the fur round it, its flaps down over the ears, the front turned up.</summary>
    static void Ushanka(Kit k)
    {
        Colour(k, new Vector3(0.34f, 0.26f, 0.18f), 0.7f, 0.0f);
        // The fur band round the head.
        k.Loft([HeadRing(1.66f, 0.03f), HeadRing(1.7f, 0.045f), HeadRing(1.74f, 0.04f)], closed: true, twoSided: true);
        // The turned-up front, higher than the band.
        k.Loft([HeadRing(1.7f, 0.05f, 12, MathF.PI * 0.6f, MathF.PI * 1.4f, false), HeadRing(1.79f, 0.058f, 12, MathF.PI * 0.6f, MathF.PI * 1.4f, false)], twoSided: true);
        // The flaps down over the ears to the jaw.
        foreach (float side in (float[])[-1, 1])
            k.BevelBox(new Vector3(side > 0 ? HeadHalfX - 0.005f : -HeadHalfX - 0.05f, 1.5f, HeadZ - 0.07f), new Vector3(side > 0 ? HeadHalfX + 0.05f : -HeadHalfX + 0.005f, 1.7f, HeadZ + 0.08f), 0.02f);
        // The crown: grey wool.
        Colour(k, new Vector3(0.22f, 0.22f, 0.21f), 0.6f, 0.02f);
        Dome(k, 1.73f, 0.035f, 1.835f, 0.75f);
    }

    /// <summary>Binoculars hung on the chest by their strap.</summary>
    static void Binoculars(Kit k)
    {
        Colour(k, new Vector3(0.08f, 0.08f, 0.07f), 0.5f, 0.3f);
        foreach (float side in (float[])[-1, 1])
        {
            k.Cylinder(new Vector3(side * 0.04f, 1.21f, -0.205f), new Vector3(side * 0.04f, 1.36f, -0.215f), 0.032f, 10);
            Brass(k);
            k.Cylinder(new Vector3(side * 0.04f, 1.2f, -0.205f), new Vector3(side * 0.04f, 1.22f, -0.206f), 0.035f, 10);
            Colour(k, new Vector3(0.08f, 0.08f, 0.07f), 0.5f, 0.3f);
        }
        k.BoxAt(new Vector3(0, 1.3f, -0.21f), new Vector3(0.025f, 0.03f, 0.015f));
        Colour(k, new Vector3(0.2f, 0.14f, 0.09f), 0.6f, 0.15f);
        foreach (float side in (float[])[-1, 1])
        {
            k.Rod(new Vector3(side * 0.07f, 1.35f, -0.21f), new Vector3(side * 0.1f, 1.48f, -0.12f), 0.008f, 3);
            k.Rod(new Vector3(side * 0.1f, 1.48f, -0.12f), new Vector3(side * 0.09f, 1.5f, 0.05f), 0.008f, 3);
        }
    }

    /// <summary>A signal flag on its staff, held upright: red over white, a little blown.</summary>
    static void Flag(Kit k)
    {
        Wood(k);
        k.Cylinder(new Vector3(0, -0.5f, 0), new Vector3(0, 0.95f, 0), 0.015f, 6);
        float x0 = 0.015f;
        Vector3 P(float u, float v) => new(x0 + u * 0.4f, 0.92f - v * 0.28f, MathF.Sin(u * 3.2f) * 0.04f * u);
        foreach (var (v0, v1, colour) in new[] { (0f, 0.5f, new Vector3(0.55f, 0.08f, 0.06f)), (0.5f, 1f, new Vector3(0.75f, 0.72f, 0.66f)) })
        {
            Colour(k, colour, 0.6f, 0.02f);
            for (int i = 0; i < 4; i++)
            {
                float u0 = i / 4f, u1 = (i + 1) / 4f;
                k.Quad(P(u0, v0), P(u1, v0), P(u1, v1), P(u0, v1), twoSided: true);
            }
        }
    }

    // ------------------------------------------------------------------ the market woman

    /// <summary>A headscarf over the hair, knotted under the chin, in <paramref name="colour"/>.</summary>
    static void Headscarf(Kit k, Vector3 colour)
    {
        Colour(k, colour, 0.6f, 0.02f);
        Dome(k, 1.6f, 0.018f, 1.81f, 0.72f);
        // Down the cheeks to the knot, and its tails.
        foreach (float side in (float[])[-1, 1])
            k.Rod(new Vector3(side * (HeadHalfX + 0.012f), 1.62f, -0.06f), new Vector3(side * 0.03f, 1.535f, -0.105f), 0.02f, 4);
        k.BoxAt(new Vector3(0, 1.53f, -0.11f), new Vector3(0.026f, 0.02f, 0.018f));
        // The scarf's triangle down the back of the head.
        k.Quad(new Vector3(-0.08f, 1.62f, HeadZ + HeadHalfZ + 0.012f), new Vector3(0.08f, 1.62f, HeadZ + HeadHalfZ + 0.012f),
            new Vector3(0.0f, 1.48f, HeadZ + HeadHalfZ + 0.03f), new Vector3(-0.0f, 1.48f, HeadZ + HeadHalfZ + 0.03f), twoSided: true);
    }

    /// <summary>A plaid shawl over the shoulders, its ends crossed on the chest.</summary>
    static void Shawl(Kit k)
    {
        var red = new Vector3(0.42f, 0.13f, 0.1f);
        Colour(k, red, 0.6f, 0.02f);
        Vector3[] Ring(float y, float rx, float rz, float zFront)
        {
            var ring = new Vector3[16];
            for (int i = 0; i < 16; i++)
            {
                // Round the back and over the shoulders, open down the front.
                float a = -MathF.PI * 0.82f + MathF.PI * 1.64f * i / 15, c = MathF.Cos(a);
                ring[i] = new Vector3(MathF.Sin(a) * rx, y, c > 0 ? c * rz : c * -zFront);
            }
            return ring;
        }
        k.Loft([Ring(1.52f, 0.13f, 0.12f, 0.12f), Ring(1.45f, 0.22f, 0.17f, 0.2f), Ring(1.3f, 0.25f, 0.18f, 0.22f), Ring(1.12f, 0.24f, 0.18f, 0.23f)], twoSided: true);
        // The plaid: dark bands across and down it.
        Colour(k, new Vector3(0.12f, 0.1f, 0.1f), 0.6f, 0.02f);
        foreach (float y in (float[])[1.42f, 1.26f])
            k.Loft([Ring(y - 0.012f, 0.237f, 0.178f, 0.218f), Ring(y + 0.012f, 0.232f, 0.176f, 0.212f)], twoSided: true);
        // Its ends crossed over the chest, a fringe.
        Colour(k, red, 0.6f, 0.02f);
        // (Each end from its shoulder down across the chest to the other side's waist, where they're tucked in.)
        foreach (float side in (float[])[-1, 1])
            k.Quad(new Vector3(side * 0.19f, 1.46f, -0.15f), new Vector3(side * 0.09f, 1.47f, -0.19f), new Vector3(-side * 0.04f, 1.0f, -0.235f - side * 0.004f),
                new Vector3(-side * 0.14f, 1.0f, -0.225f - side * 0.004f), twoSided: true);
    }

    /// <summary>A skirt from the waist (<paramref name="top"/> m) down to <paramref name="hem"/>, flaring to <paramref name="flare"/>.</summary>
    static void Skirt(Kit k, Vector3 colour, float top, float hem, float flare)
    {
        Colour(k, colour, 0.6f, 0.03f);
        var rings = new List<Vector3[]>();
        for (int i = 0; i <= 4; i++)
        {
            float t = i / 4f, y = top + (hem - top) * t, r = 0.2f + (flare - 0.2f) * MathF.Sqrt(t);
            var ring = new Vector3[18];
            for (int j = 0; j < 18; j++)
            {
                float a = j * MathF.Tau / 18;
                // Folds in it, more toward the hem.
                float fold = 1 + 0.04f * t * MathF.Sin(a * 6);
                ring[j] = new Vector3(MathF.Sin(a) * r * 1.05f * fold, y, MathF.Cos(a) * r * 0.95f * fold);
            }
            rings.Add(ring);
        }
        k.Loft(rings, closed: true, twoSided: true);
    }

    /// <summary>A long apron over the skirt, tied at the waist, its strings at the back.</summary>
    static void Apron(Kit k)
    {
        Colour(k, new Vector3(0.55f, 0.5f, 0.42f), 0.6f, 0.02f);
        Vector3 P(float x, float y) => new(x, y, -0.235f - (0.98f - y) * 0.17f + x * x * 0.6f);
        var rows = new List<Vector3[]>();
        foreach (float y in (float[])[0.98f, 0.8f, 0.6f, 0.4f])
            rows.Add([.. Enumerable.Range(0, 6).Select(i => P(-0.2f - (0.98f - y) * 0.08f + i * (0.4f + (0.98f - y) * 0.16f) / 5, y))]);
        k.Loft(rows, twoSided: true);
        // The band round the waist.
        Colour(k, new Vector3(0.4f, 0.36f, 0.3f), 0.6f, 0.02f);
        k.Loft([Ring(0.96f, 0.215f, 0.2f), Ring(1.0f, 0.212f, 0.198f)], closed: true, twoSided: true);
    }

    static Vector3[] Ring(float y, float rx, float rz, int n = 16)
    {
        var ring = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            float a = i * MathF.Tau / n;
            ring[i] = new Vector3(MathF.Sin(a) * rx, y, MathF.Cos(a) * rz);
        }
        return ring;
    }

    /// <summary>A ring of keys at the right hip.</summary>
    static void Keys(Kit k)
    {
        Iron(k);
        k.Shade(0.8f);
        var ring = new Vector3(0.2f, 0.9f, -0.12f);
        k.Lathe(ring + new Vector3(0, -0.005f, 0), [new(0.035f, 0), new(0.035f, 0.01f)], 10, capTop: false);
        Brass(k);
        for (int i = 0; i < 5; i++)
        {
            float a = -0.6f + i * 0.3f;
            var top = ring + new Vector3(MathF.Sin(a) * 0.03f, -0.03f, MathF.Cos(a) * 0.01f);
            var tip = top + new Vector3(MathF.Sin(a) * 0.03f, -0.1f, 0);
            k.Rod(top, tip, 0.005f, 3);
            k.BoxAt(tip + new Vector3(0, 0.012f, 0), new Vector3(0.012f, 0.004f, 0.003f));
        }
    }

    /// <summary>A crate of greens and tomatoes, held by its slatted side at the left fist.</summary>
    static void Crate(Kit k)
    {
        const float X = -0.1f, Y0 = -0.32f, H = 0.2f, W = 0.3f, L = 0.42f;
        k.Use("wood_crate", Palette.DeepBrown, 0.7f, 0.05f, tile: 1);
        k.Box(new Vector3(X - W / 2, Y0, -L / 2), new Vector3(X + W / 2, Y0 + 0.02f, L / 2));
        foreach (float side in (float[])[-1, 1])
        {
            k.Box(new Vector3(X - W / 2, Y0, side * L / 2 - 0.01f), new Vector3(X + W / 2, Y0 + H, side * L / 2 + 0.01f));
            for (int s = 0; s < 2; s++)
                k.Box(new Vector3(X + side * W / 2 - 0.01f, Y0 + 0.02f + s * 0.1f, -L / 2), new Vector3(X + side * W / 2 + 0.01f, Y0 + 0.08f + s * 0.1f, L / 2));
        }
        // What's in it: cabbages and greens, a few tomatoes.
        var rng = new Random(61);
        for (int i = 0; i < 9; i++)
        {
            bool tomato = i % 3 == 2;
            Colour(k, tomato ? new Vector3(0.55f, 0.12f, 0.06f) : new Vector3(0.22f, 0.36f, 0.12f), 0.4f, tomato ? 0.4f : 0.1f);
            float r = tomato ? 0.035f : 0.06f;
            var c = new Vector3(X + (float)(rng.NextDouble() - 0.5) * (W - 0.1f), Y0 + H - 0.02f, (float)(rng.NextDouble() - 0.5) * (L - 0.1f));
            k.Lathe(c - new Vector3(0, r, 0), [new(0.0f, 0), new(r * 0.8f, r * 0.3f), new(r, r), new(r * 0.8f, r * 1.7f), new(0, r * 2)], 8);
        }
    }

    // ------------------------------------------------------------------ the lamplighter

    /// <summary>A deep hood over the head, its edge round the face.</summary>
    static void Hood(Kit k)
    {
        Colour(k, new Vector3(0.38f, 0.3f, 0.21f), 0.6f, 0.03f);
        var rings = new List<Vector3[]>();
        foreach (var (y, grow) in new[] { (1.48f, 0.065f), (1.56f, 0.05f), (1.66f, 0.048f), (1.76f, 0.045f), (1.84f, 0.02f) })
            rings.Add(HeadRing(y, grow, 16, -MathF.PI * 0.72f, MathF.PI * 0.72f, closed: false));
        k.Loft(rings, twoSided: true);
        // The peak over the brow, folded forward.
        k.Loft([HeadRing(1.84f, 0.02f, 8, MathF.PI * 0.72f, MathF.PI * 1.28f, false), HeadRing(1.8f, 0.07f, 8, MathF.PI * 0.72f, MathF.PI * 1.28f, false)], twoSided: true);
        // Down onto the shoulders behind.
        k.Loft([HeadRing(1.48f, 0.065f, 12, -MathF.PI * 0.5f, MathF.PI * 0.5f, false), HeadRing(1.38f, 0.12f, 12, -MathF.PI * 0.5f, MathF.PI * 0.5f, false)], twoSided: true);
    }

    /// <summary>
    /// The lamplighter's mask: a black facepiece with a little brass lamp before it, its glass burning (lit by
    /// <see cref="Dress"/>), and a hose from it over his shoulder to the bottles on his back.
    /// </summary>
    static void LampMask(Kit k)
    {
        Rubber(k);
        k.Loft([HeadRing(1.7f, 0.012f, 12, MathF.PI * 0.55f, MathF.PI * 1.45f, false), HeadRing(1.6f, 0.03f, 12, MathF.PI * 0.55f, MathF.PI * 1.45f, false),
            HeadRing(1.52f, 0.02f, 12, MathF.PI * 0.55f, MathF.PI * 1.45f, false)], twoSided: true);
        // The lamp: a brass cage, its glass glowing, a little chimney.
        var c = MaskFlame;
        Brass(k);
        foreach (float x in (float[])[-1, 1])
            foreach (float z in (float[])[-1, 1])
                k.Cylinder(c + new Vector3(x * 0.045f, -0.06f, z * 0.04f), c + new Vector3(x * 0.045f, 0.06f, z * 0.04f), 0.006f, 5);
        k.BoxAt(c + new Vector3(0, 0.065f, 0), new Vector3(0.055f, 0.008f, 0.05f));
        k.BoxAt(c + new Vector3(0, -0.065f, 0), new Vector3(0.055f, 0.008f, 0.05f));
        k.Cylinder(c + new Vector3(0, 0.07f, 0), c + new Vector3(0, 0.11f, 0), 0.015f, 6);
        // Its arm back to the mask.
        k.Rod(c + new Vector3(0, 0, 0.04f), new Vector3(0, 1.64f, EyeZ - 0.02f), 0.01f, 4);
        Glow(k, new Vector3(1.0f, 0.75f, 0.35f), 1.6f);
        k.BoxAt(c, new Vector3(0.04f, 0.055f, 0.035f));
        k.Emissive = 0;
        // The hose from under the mask over the right shoulder.
        Rubber(k);
        k.Cylinder(Mouth + new Vector3(0.03f, -0.05f, -0.02f), new Vector3(0.14f, 1.5f, -0.05f), 0.014f, 6);
        k.Cylinder(new Vector3(0.14f, 1.5f, -0.05f), new Vector3(0.16f, 1.5f, 0.12f), 0.014f, 6);
    }

    /// <summary>Two bottles on his back in a harness, their valves, the hose's other end.</summary>
    static void Bottles(Kit k)
    {
        Iron(k);
        k.Tint = new Vector3(0.55f, 0.5f, 0.4f);
        foreach (float x in (float[])[-0.065f, 0.065f])
        {
            var b = new Vector3(x, 1.0f, 0.23f);
            k.Lathe(b, [new(0, 0), new(0.06f, 0.02f), new(0.06f, 0.4f), new(0.02f, 0.46f)], 10);
            Brass(k);
            k.Cylinder(b + new Vector3(0, 0.46f, 0), b + new Vector3(0, 0.52f, 0), 0.02f, 6);
            Iron(k);
            k.Tint = new Vector3(0.55f, 0.5f, 0.4f);
        }
        Colour(k, new Vector3(0.18f, 0.12f, 0.08f), 0.6f, 0.15f);
        foreach (float side in (float[])[-1, 1])
        {
            k.Rod(new Vector3(side * 0.12f, 1.38f, 0.18f), new Vector3(side * 0.14f, 1.5f, 0.0f), 0.015f, 3);
            k.Rod(new Vector3(side * 0.14f, 1.5f, 0.0f), new Vector3(side * 0.14f, 1.25f, -0.19f), 0.015f, 3);
        }
        k.Rod(new Vector3(-0.17f, 1.1f, 0.17f), new Vector3(0.17f, 1.1f, 0.17f), 0.018f, 3);
        Rubber(k);
        k.Cylinder(new Vector3(0.065f, 1.52f, 0.23f), new Vector3(0.16f, 1.5f, 0.12f), 0.014f, 6);
    }

    /// <summary>A lamplighter's pole, held upright: the lamp on its head, under a little roof, out on an arm.</summary>
    static void LampPole(Kit k)
    {
        Wood(k);
        k.Cylinder(new Vector3(0, -0.85f, 0), new Vector3(0, 1.05f, 0), 0.018f, 6);
        Iron(k);
        k.Shade(0.5f);
        k.Rod(new Vector3(0, 1.02f, 0), new Vector3(0, 1.02f, PoleFlame.Z), 0.008f, 4);
        var c = PoleFlame;
        foreach (float x in (float[])[-1, 1])
            foreach (float z in (float[])[-1, 1])
                k.Cylinder(c + new Vector3(x * 0.05f, -0.07f, z * 0.05f), c + new Vector3(x * 0.05f, 0.08f, z * 0.05f), 0.006f, 5);
        k.Lathe(c + new Vector3(0, 0.08f, 0), [new(0.08f, 0), new(0.0f, 0.06f)], 8);
        k.BoxAt(c + new Vector3(0, -0.075f, 0), new Vector3(0.06f, 0.008f, 0.06f));
        Glow(k, new Vector3(1.0f, 0.75f, 0.35f), 1.8f);
        k.BoxAt(c, new Vector3(0.045f, 0.065f, 0.045f));
        k.Emissive = 0;
    }

    // ------------------------------------------------------------------ the linesman

    /// <summary>A flat cap (with a pair of goggles on its brow when <paramref name="goggles"/>).</summary>
    static void FlatCap(Kit k, Vector3 colour, bool goggles)
    {
        Colour(k, colour, 0.6f, 0.03f);
        var c = new Vector3(0, 1.72f, HeadZ - 0.012f);
        k.Lathe(c, [new(HeadHalfX + 0.022f, 0), new(HeadHalfX + 0.034f, 0.04f), new(HeadHalfX + 0.014f, 0.07f), new(0, 0.082f)], 16);
        var peak = new Vector3[] { c + new Vector3(-0.095f, 0.006f, -0.085f), c + new Vector3(0, 0.012f, -0.12f), c + new Vector3(0.095f, 0.006f, -0.085f) };
        var lip = new Vector3[] { c + new Vector3(-0.085f, -0.012f, -0.125f), c + new Vector3(0, -0.01f, -0.185f), c + new Vector3(0.085f, -0.012f, -0.125f) };
        k.Loft([peak, lip], twoSided: true);
        if (!goggles)
            return;
        // The goggles pushed up on the cap's front, their strap round it.
        foreach (float side in (float[])[-1, 1])
        {
            var lens = c + new Vector3(side * 0.045f, 0.03f, -0.115f);
            Brass(k);
            k.Cylinder(lens + new Vector3(0, -0.01f, 0.015f), lens + new Vector3(0, 0.005f, -0.01f), 0.03f, 10);
            Glow(k, new Vector3(0.25f, 0.3f, 0.3f), 0.1f);
            k.Disc(lens + new Vector3(0, 0.006f, -0.012f), Vector3.Normalize(new Vector3(0, 0.4f, -1)), 0.024f, 10);
            k.Emissive = 0;
        }
        Colour(k, new Vector3(0.12f, 0.1f, 0.08f), 0.6f, 0.1f);
        k.Loft([HeadRing(c.Y + 0.02f, 0.035f, 14, -MathF.PI * 0.8f, MathF.PI * 0.8f, false), HeadRing(c.Y + 0.04f, 0.035f, 14, -MathF.PI * 0.8f, MathF.PI * 0.8f, false)], twoSided: true);
    }

    /// <summary>A wool wrap over the nose and mouth, knotted behind, its tail down the back.</summary>
    static void Wrap(Kit k, Vector3 colour)
    {
        Colour(k, colour, 0.65f, 0.02f);
        k.Loft([HeadRing(1.53f, 0.018f), HeadRing(1.59f, 0.03f), HeadRing(1.645f, 0.02f)], closed: true, twoSided: true);
        k.Quad(new Vector3(0.0f, 1.58f, HeadZ + HeadHalfZ + 0.03f), new Vector3(0.07f, 1.58f, HeadZ + HeadHalfZ + 0.03f),
            new Vector3(0.09f, 1.38f, HeadZ + HeadHalfZ + 0.08f), new Vector3(0.03f, 1.38f, HeadZ + HeadHalfZ + 0.08f), twoSided: true);
    }

    /// <summary>A lineman's belt: leather, its pouches, a pair of pliers and a hammer hung from it.</summary>
    static void ToolBelt(Kit k)
    {
        Colour(k, new Vector3(0.22f, 0.15f, 0.09f), 0.6f, 0.2f);
        k.Loft([Ring(0.94f, 0.205f, 0.19f), Ring(0.99f, 0.205f, 0.19f)], closed: true, twoSided: true);
        foreach (float a in (float[])[-2.1f, 2.4f, 1.6f])
            k.BevelBox(new Vector3(MathF.Sin(a) * 0.21f - 0.04f, 0.86f, MathF.Cos(a) * 0.19f - 0.025f), new Vector3(MathF.Sin(a) * 0.21f + 0.04f, 0.97f, MathF.Cos(a) * 0.19f + 0.025f), 0.01f);
        Iron(k);
        k.Shade(0.6f);
        k.Rod(new Vector3(-0.2f, 0.95f, -0.06f), new Vector3(-0.21f, 0.78f, -0.07f), 0.008f, 4);
        k.Rod(new Vector3(-0.2f, 0.95f, -0.06f), new Vector3(-0.19f, 0.78f, -0.05f), 0.008f, 4);
        Wood(k);
        k.Rod(new Vector3(0.21f, 0.95f, 0.03f), new Vector3(0.22f, 0.72f, 0.03f), 0.012f, 4);
    }

    /// <summary>A coil of wire hung at his right hip on a strap over the left shoulder.</summary>
    static void Coil(Kit k)
    {
        Colour(k, new Vector3(0.35f, 0.22f, 0.12f), 0.4f, 0.5f);
        var c = new Vector3(0.29f, 0.98f, 0.0f);
        const int n = 14, m = 6;
        var rings = new List<Vector3[]>();
        for (int i = 0; i <= n; i++)
        {
            float a = i * MathF.Tau / n;
            var centre = c + new Vector3(0, MathF.Sin(a) * 0.17f, MathF.Cos(a) * 0.17f);
            var outward = new Vector3(0, MathF.Sin(a), MathF.Cos(a));
            rings.Add([.. Enumerable.Range(0, m).Select(j =>
            {
                float b = j * MathF.Tau / m;
                return centre + outward * (MathF.Cos(b) * 0.035f) + Vector3.UnitX * (MathF.Sin(b) * 0.04f);
            })]);
        }
        k.Loft(rings, closed: true);
        Colour(k, new Vector3(0.18f, 0.13f, 0.08f), 0.6f, 0.15f);
        k.Rod(c + new Vector3(-0.03f, 0.17f, 0), new Vector3(0.12f, 1.48f, -0.1f), 0.012f, 3);
        k.Rod(new Vector3(0.12f, 1.48f, -0.1f), new Vector3(-0.14f, 1.5f, 0.08f), 0.012f, 3);
    }

    // ------------------------------------------------------------------ the preacher

    /// <summary>A wide-brimmed black hat, its crown low and flat.</summary>
    static void WideHat(Kit k)
    {
        Colour(k, new Vector3(0.06f, 0.055f, 0.05f), 0.5f, 0.08f);
        Dome(k, 1.72f, 0.03f, 1.86f, 0.92f);
        Brim(k, 1.725f, 0.03f, 0.16f, 0.02f);
        Colour(k, new Vector3(0.12f, 0.1f, 0.08f), 0.5f, 0.08f);
        k.Loft([HeadRing(1.73f, 0.034f), HeadRing(1.76f, 0.033f)], closed: true, twoSided: true);
    }

    /// <summary>A veil from the hat's brim down over the face to the chest: pale, thin linen.</summary>
    static void Veil(Kit k)
    {
        Colour(k, new Vector3(0.42f, 0.4f, 0.35f), 0.6f, 0.02f);
        var rings = new List<Vector3[]>();
        foreach (var (y, grow) in new[] { (1.72f, 0.035f), (1.6f, 0.045f), (1.5f, 0.06f) })
            rings.Add(HeadRing(y, grow, 14, MathF.PI * 0.45f, MathF.PI * 1.55f, false));
        k.Loft(rings, twoSided: true);
    }

    /// <summary>A stole down his front over the cassock, gold worked on it, a cross near each end.</summary>
    static void Stole(Kit k)
    {
        var gold = new Vector3(0.48f, 0.38f, 0.16f);
        foreach (float side in (float[])[-1, 1])
        {
            Colour(k, gold, 0.5f, 0.25f);
            float x0 = side * 0.075f;
            k.Quad(new Vector3(x0 - 0.04f, 1.48f, -0.16f), new Vector3(x0 + 0.04f, 1.48f, -0.16f), new Vector3(x0 + 0.045f, 0.62f, -0.205f), new Vector3(x0 - 0.045f, 0.62f, -0.205f), twoSided: true);
            Colour(k, new Vector3(0.12f, 0.08f, 0.04f), 0.5f, 0.2f);
            var cross = new Vector3(x0, 0.75f, -0.205f);
            k.BoxAt(cross, new Vector3(0.008f, 0.04f, 0.004f));
            k.BoxAt(cross + new Vector3(0, 0.012f, 0), new Vector3(0.025f, 0.008f, 0.004f));
        }
    }

    /// <summary>A pectoral cross on a chain round his neck.</summary>
    static void Cross(Kit k)
    {
        Brass(k);
        var c = new Vector3(0, 1.22f, -0.215f);
        k.BoxAt(c, new Vector3(0.012f, 0.05f, 0.006f));
        k.BoxAt(c + new Vector3(0, 0.02f, 0), new Vector3(0.035f, 0.01f, 0.006f));
        k.Rod(c + new Vector3(0, 0.05f, 0), new Vector3(-0.09f, 1.45f, -0.14f), 0.003f, 3);
        k.Rod(c + new Vector3(0, 0.05f, 0), new Vector3(0.09f, 1.45f, -0.14f), 0.003f, 3);
    }

    /// <summary>A brass cross in his hand on the socket (its +Y out of the fist's thumb side).</summary>
    static void HandCross(Kit k)
    {
        Brass(k);
        k.BoxAt(new Vector3(0, 0.08f, 0), new Vector3(0.012f, 0.15f, 0.012f));
        k.BoxAt(new Vector3(0, 0.15f, 0), new Vector3(0.012f, 0.012f, 0.06f));
    }

    // ------------------------------------------------------------------ the butcher

    /// <summary>A knitted cap, close to the head, its cuff turned up.</summary>
    static void Beanie(Kit k)
    {
        Colour(k, new Vector3(0.36f, 0.36f, 0.34f), 0.7f, 0f);
        Dome(k, 1.67f, 0.022f, 1.83f, 0.7f);
        k.Loft([HeadRing(1.665f, 0.032f), HeadRing(1.715f, 0.034f)], closed: true, twoSided: true);
    }

    /// <summary>A butcher's bib apron from the chest to the knees, stained.</summary>
    static void BibApron(Kit k)
    {
        Colour(k, new Vector3(0.58f, 0.5f, 0.3f), 0.6f, 0.04f);
        Vector3 P(float x, float y) => new(x, y, -0.2f - MathF.Max(0, 1.0f - y) * 0.06f - x * x * 0.8f + (y > 1.05f ? (y - 1.05f) * 0.15f : 0));
        var rows = new List<Vector3[]>();
        foreach (var (y, half) in new[] { (1.42f, 0.13f), (1.15f, 0.17f), (0.95f, 0.22f), (0.72f, 0.24f), (0.52f, 0.25f) })
            rows.Add([.. Enumerable.Range(0, 6).Select(i => P(-half + i * 2 * half / 5, y))]);
        k.Loft(rows, twoSided: true);
        // The blood on it: dark red smears, more low down where he wipes his hands.
        Colour(k, new Vector3(0.32f, 0.04f, 0.03f), 0.4f, 0.2f);
        var rng = new Random(77);
        for (int i = 0; i < 14; i++)
        {
            float y = 0.6f + (float)rng.NextDouble() * 0.75f, x = ((float)rng.NextDouble() - 0.5f) * 0.3f, r = 0.02f + (float)rng.NextDouble() * 0.035f;
            var at = P(x, y) + new Vector3(0, 0, -0.003f);
            k.Disc(at, -Vector3.UnitZ, r, 7);
        }
        // Its neck strap and ties.
        Colour(k, new Vector3(0.3f, 0.25f, 0.15f), 0.6f, 0.04f);
        foreach (float side in (float[])[-1, 1])
        {
            k.Rod(new Vector3(side * 0.12f, 1.42f, -0.18f), new Vector3(side * 0.07f, 1.5f, 0.02f), 0.008f, 3);
            k.Rod(new Vector3(side * 0.22f, 0.98f, -0.17f), new Vector3(side * 0.12f, 0.98f, 0.18f), 0.008f, 3);
        }
    }

    /// <summary>A butcher's knife on the hand's socket: the handle in the fist, the blade out of the thumb side.</summary>
    static void Knife(Kit k)
    {
        Wood(k);
        k.BoxAt(new Vector3(0, 0, 0), new Vector3(0.012f, 0.06f, 0.016f));
        Iron(k);
        k.Shade(1.4f);
        k.Quad(new Vector3(0, 0.06f, 0.018f), new Vector3(0, 0.06f, -0.02f), new Vector3(0, 0.27f, -0.002f), new Vector3(0, 0.27f, 0.015f), twoSided: true);
        Colour(k, new Vector3(0.3f, 0.04f, 0.03f), 0.3f, 0.3f);
        k.Quad(new Vector3(0.001f, 0.18f, 0.012f), new Vector3(0.001f, 0.18f, -0.008f), new Vector3(0.001f, 0.26f, 0.0f), new Vector3(0.001f, 0.26f, 0.012f), twoSided: true);
    }

    // ------------------------------------------------------------------ the harbour master

    /// <summary>A peaked cap: navy, its white top, a black peak and band, a brass badge.</summary>
    static void CaptainsCap(Kit k)
    {
        Colour(k, new Vector3(0.07f, 0.08f, 0.12f), 0.5f, 0.06f);
        k.Loft([HeadRing(1.7f, 0.022f), HeadRing(1.76f, 0.026f)], closed: true, twoSided: true);
        // The crown, wider than the band, a flat top.
        k.Loft([HeadRing(1.76f, 0.026f), HeadRing(1.8f, 0.05f), HeadRing(1.81f, 0.045f)], closed: true, twoSided: true);
        k.Cap(HeadRing(1.81f, 0.045f), Vector3.UnitY);
        // The peak: black, shining.
        Colour(k, new Vector3(0.03f, 0.03f, 0.03f), 0.3f, 0.5f);
        var c = new Vector3(0, 1.705f, HeadZ);
        var inner = new Vector3[] { c + new Vector3(-0.09f, 0, -0.1f), c + new Vector3(0, 0, -0.15f), c + new Vector3(0.09f, 0, -0.1f) };
        var outer = new Vector3[] { c + new Vector3(-0.08f, -0.03f, -0.15f), c + new Vector3(0, -0.035f, -0.21f), c + new Vector3(0.08f, -0.03f, -0.15f) };
        k.Loft([inner, outer], twoSided: true);
        k.Loft([HeadRing(1.7f, 0.025f), HeadRing(1.725f, 0.025f)], closed: true, twoSided: true);
        Brass(k);
        k.BoxAt(new Vector3(0, 1.745f, HeadZ - HeadHalfZ - 0.03f), new Vector3(0.03f, 0.016f, 0.004f));
    }

    /// <summary>A brass eyeglass in his right eye, its cord.</summary>
    static void Eyeglass(Kit k)
    {
        var eye = new Vector3(0.042f, EyeY, EyeZ - 0.015f);
        Brass(k);
        k.Cylinder(eye + new Vector3(0, 0, 0.008f), eye - new Vector3(0, 0, 0.03f), 0.022f, 12);
        Glow(k, new Vector3(0.4f, 0.45f, 0.45f), 0.15f);
        k.Disc(eye - new Vector3(0, 0, 0.031f), -Vector3.UnitZ, 0.018f, 12);
        k.Emissive = 0;
        Colour(k, new Vector3(0.1f, 0.08f, 0.06f), 0.5f, 0.1f);
        k.Rod(eye - new Vector3(0, 0.02f, 0.01f), new Vector3(0.07f, 1.42f, -0.17f), 0.002f, 3);
    }

    /// <summary>His greatcoat's brass buttons, two rows down its double-breasted front.</summary>
    static void Buttons(Kit k)
    {
        Brass(k);
        foreach (float x in (float[])[-0.075f, 0.075f])
            for (int i = 0; i < 4; i++)
            {
                float y = 1.36f - i * 0.12f;
                k.Lathe(new Vector3(x, y, -0.205f - (1.36f - y) * 0.02f) - new Vector3(0, 0, 0.005f), [new(0.014f, 0), new(0.012f, 0.008f), new(0, 0.01f)], 8);
            }
    }

    /// <summary>A brass spyglass, closed, on the hand's socket.</summary>
    static void Spyglass(Kit k)
    {
        Brass(k);
        k.Cylinder(new Vector3(0, -0.06f, 0), new Vector3(0, 0.12f, 0), 0.028f, 10);
        k.Cylinder(new Vector3(0, 0.12f, 0), new Vector3(0, 0.22f, 0), 0.024f, 10);
        Colour(k, new Vector3(0.15f, 0.1f, 0.06f), 0.5f, 0.2f);
        k.Cylinder(new Vector3(0, -0.04f, 0), new Vector3(0, 0.06f, 0), 0.031f, 10);
    }

    // ------------------------------------------------------------------ the drifter

    /// <summary>A hip flask on the hand's socket: pewter, flat, its cap.</summary>
    static void Flask(Kit k)
    {
        Iron(k);
        k.Shade(1.5f);
        k.BevelBox(new Vector3(-0.012f, -0.05f, -0.035f), new Vector3(0.012f, 0.07f, 0.035f), 0.008f);
        k.Cylinder(new Vector3(0, 0.07f, 0), new Vector3(0, 0.09f, 0), 0.01f, 6);
    }

    // ------------------------------------------------------------------ the schoolteacher

    /// <summary>Her hair up in a bun at the back of her head.</summary>
    static void Bun(Kit k)
    {
        Colour(k, new Vector3(0.2f, 0.12f, 0.08f), 0.5f, 0.06f);
        var c = new Vector3(0, 1.66f, HeadZ + HeadHalfZ + 0.03f);
        k.Lathe(c - new Vector3(0, 0.045f, 0), [new(0, 0), new(0.04f, 0.015f), new(0.05f, 0.045f), new(0.04f, 0.075f), new(0, 0.09f)], 10);
    }

    /// <summary>Round wire spectacles.</summary>
    static void Spectacles(Kit k)
    {
        Colour(k, new Vector3(0.12f, 0.11f, 0.1f), 0.4f, 0.4f);
        foreach (float side in (float[])[-1, 1])
        {
            var eye = new Vector3(side * 0.042f, EyeY - 0.003f, EyeZ - 0.022f);
            const int n = 12;
            var ring = new Vector3[n];
            var ringB = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float a = i * MathF.Tau / n;
                ring[i] = eye + new Vector3(MathF.Cos(a) * 0.024f, MathF.Sin(a) * 0.022f, 0);
                ringB[i] = eye + new Vector3(MathF.Cos(a) * 0.028f, MathF.Sin(a) * 0.026f, 0);
            }
            k.Loft([ring, ringB], closed: true, twoSided: true);
            k.Rod(eye + new Vector3(side * 0.028f, 0, 0), new Vector3(side * (HeadHalfX + 0.006f), EyeY, -0.02f), 0.002f, 3);
        }
        k.Rod(new Vector3(-0.016f, EyeY, EyeZ - 0.024f), new Vector3(0.016f, EyeY, EyeZ - 0.024f), 0.002f, 3);
    }

    /// <summary>Three books held at the left fist by their spines, hanging at her side.</summary>
    static void Books(Kit k)
    {
        var colours = new[] { new Vector3(0.32f, 0.12f, 0.08f), new Vector3(0.12f, 0.18f, 0.12f), new Vector3(0.3f, 0.24f, 0.12f) };
        for (int i = 0; i < 3; i++)
        {
            Colour(k, colours[i], 0.5f, 0.1f);
            float x = -0.04f - i * 0.035f;
            k.BevelBox(new Vector3(x - 0.015f, -0.25f + i * 0.01f, -0.1f), new Vector3(x + 0.015f, 0.0f - i * 0.01f, 0.1f), 0.004f);
        }
    }
}
