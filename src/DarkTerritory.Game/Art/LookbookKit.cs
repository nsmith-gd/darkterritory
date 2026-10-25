using System.Numerics;
using Ballast.Assets;
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
    /// <summary>Their modelled figure's (tools/models/recipes/townsfolk_&lt;id&gt;.py, its clothes baked on), once it's built.</summary>
    public string Modelled => "townsfolk_" + Id;
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

    // The survivors' head in the bind pose (survivor_prisoner.glb): its middle's Z, the eyes' height. The rest of the
    // figure's shape the pieces are made to is measured off it (<see cref="FigureSurface"/>).
    const float Mid = -0.015f, EyeY = 1.655f;

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
            [Cloth.Shirt] = Flat(0.24f, 0.28f, 0.2f), [Cloth.Trousers] = Flat(0.16f, 0.14f, 0.12f), [Cloth.Feet] = Boots,
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

    /// <summary>
    /// The role of one of the characters who go about a town (<c>anyone</c>), by <paramref name="who"/> (the same every night):
    /// for folk with no job of their own to be dressed for, the fort's (note 1002; the director's 23 Oct playtest, line 23:
    /// "Some of these people's faces are terrifying"), so <see cref="For"/> gives them that character.
    /// </summary>
    public static string AnyoneRole(int who)
    {
        var common = Characters.Where(c => c.Roles.Contains("anyone")).ToList();
        return common[(int)((uint)who * 40503u >> 3) % common.Count].Roles[0];
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

    /// <summary>What each one's modelled figure has baked on of what they wear (the rest is still worn: a gauze veil, a lit lamp).</summary>
    static string[] Baked(string id) => id switch
    {
        "trapper" => ["bucket", "snout"],
        "watchman" => ["ushanka", "binoculars"],
        "market" => ["redscarf", "shawl", "skirt", "apron"],
        // (His mask's facepiece is baked; its lamp, lit, and the hose to his bottles are still worn: "masklamp".)
        "lamplighter" => ["hood", "bottles"],
        "linesman" => ["capgoggles", "wrap", "toolbelt", "coil"],
        "preacher" => ["widehat", "stole", "cross"],
        "butcher" => ["beanie", "snout", "bibapron"],
        "harbourmaster" => ["captcap", "buttons"],
        "drifter" => ["flatcap", "redwrap"],
        "teacher" => ["bun", "longskirt"],
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
    /// <paramref name="far"/>: drawn as instances (a far figure's, note 479). <paramref name="lamp"/>: their right fist has a
    /// hand lamp in it (the street's lamp-carriers), so what they'd carry there is left. Lights its lamps (the lamplighter's).
    /// </summary>
    public void Dress(CreatureArt creatures, MeshBuilder mesh, in Matrix4x4 model, TownCharacter c, bool hands = true, bool far = false, bool lamp = false)
    {
        // A modelled figure has what it wears baked on: only what it carries (and what the bake can't make) is added.
        string figure = creatures.FigureFor(c);
        bool modelled = figure == c.Modelled;
        foreach (var (piece, bone) in Worn(c.Id))
            if (!modelled || !Baked(c.Id).Contains(piece))
                creatures.Wear(mesh, Get(modelled && piece == "lampmask" ? "masklamp" : piece, c), bone, model, figure);
        if (hands)
            foreach (var (piece, left, upright) in Held(c.Id))
                if (left || !lamp)
                    creatures.Hold(mesh, Get(piece, c), model, figure, left, upright);
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

    // ------------------------------------------------------------------ the figure they're made to

    /// <summary>
    /// The surfaces a character's pieces are made to: the survivors' head, and their body as the character's is shaped
    /// (over their coat, for those who wear one). Measured off the figure (<see cref="FigureSurface"/>), so what's worn
    /// sits on it: a hat on the skull, a mask on the face, a skirt hung from the hips.
    /// </summary>
    sealed record Fit(FigureSurface Head, FigureSurface Body);

    static Model? _figure;
    static readonly Lock FigureGate = new();
    readonly Dictionary<string, Fit> _fits = [];

    Model Figure()
    {
        lock (FigureGate)
        {
            if (_figure is not null)
                return _figure;
            string content = look?.TextureRoot is { } t ? Path.GetDirectoryName(Path.GetDirectoryName(t))! : Ballast.DataFile.FindContentRoot();
            return _figure = ModelLoader.Load(Path.Combine(content, CreatureArt.Folder, "survivor_prisoner.glb"));
        }
    }

    Fit FitOf(TownCharacter c)
    {
        if (_fits.TryGetValue(c.Id, out var fit))
            return fit;
        var figure = Figure();
        var head = _fits.Values.FirstOrDefault()?.Head ?? FigureSurface.Head(figure);
        return _fits[c.Id] = new Fit(head, FigureSurface.Body(figure, c.Clothes.ContainsKey(Cloth.Coat), c.Shape));
    }

    MeshAsset Get(string key, TownCharacter c)
    {
        string name = c.Id + "." + key;
        if (_pieces.TryGetValue(name, out var piece))
            return piece;
        var k = new Kit(look, 6100 + key.Sum(ch => ch) % 97);
        var f = FitOf(c);
        switch (key)
        {
            case "bucket": Bucket(k, f); break;
            case "snout": Snout(k, f); break;
            case "gaff": Gaff(k); break;
            case "pot": Pot(k); break;
            case "ushanka": Ushanka(k, f); break;
            case "binoculars": Binoculars(k, f); break;
            case "flag": Flag(k); break;
            case "redscarf": Headscarf(k, f, new Vector3(0.5f, 0.12f, 0.09f)); break;
            case "shawl": Shawl(k, f); break;
            case "skirt": Skirt(k, f, new Vector3(0.48f, 0.1f, 0.07f), 0.98f, 0.3f, 0.11f); break;
            case "longskirt": Skirt(k, f, new Vector3(0.17f, 0.15f, 0.14f), 0.96f, 0.1f, 0.035f); break;
            case "apron": Apron(k, f); break;
            case "keys": Keys(k, f); break;
            case "crate": Crate(k); break;
            case "hood": Hood(k, f); break;
            case "lampmask": LampMask(k, f); break;
            case "masklamp": LampMask(k, f, facepiece: false); break;
            case "bottles": Bottles(k, f); break;
            case "lamppole": LampPole(k); break;
            case "capgoggles": FlatCap(k, f, new Vector3(0.3f, 0.3f, 0.26f), goggles: true); break;
            case "flatcap": FlatCap(k, f, new Vector3(0.3f, 0.24f, 0.17f), goggles: false); break;
            case "wrap": Wrap(k, f, new Vector3(0.42f, 0.38f, 0.3f)); break;
            case "redwrap": Wrap(k, f, new Vector3(0.5f, 0.11f, 0.08f)); break;
            case "toolbelt": ToolBelt(k, f); break;
            case "coil": Coil(k, f); break;
            case "widehat": WideHat(k, f); break;
            case "veil": Veil(k, f); break;
            case "stole": Stole(k, f); break;
            case "cross": Cross(k, f); break;
            case "handcross": HandCross(k); break;
            case "beanie": Beanie(k, f); break;
            case "bibapron": BibApron(k, f); break;
            case "knife": Knife(k); break;
            case "captcap": CaptainsCap(k, f); break;
            case "eyeglass": Eyeglass(k, f); break;
            case "buttons": Buttons(k, f); break;
            case "spyglass": Spyglass(k); break;
            case "flask": Flask(k); break;
            case "bun": Bun(k, f); break;
            case "spectacles": Spectacles(k, f); break;
            case "books": Books(k); break;
        }
        return _pieces[name] = k.Build("lookbook." + name);
    }

    /// <summary>Every piece the lookbook makes, built for whoever wears or carries it (for the tests' triangle counts).</summary>
    public IEnumerable<(string Key, MeshAsset Piece)> All() =>
        Characters.SelectMany(c => Worn(c.Id).Select(w => w.Piece).Concat(Held(c.Id).Select(h => h.Piece)).Concat(c.Id == "lamplighter" ? ["masklamp"] : []).Distinct()
            .Select(key => (c.Id + "." + key, Get(key, c))));

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

    // ------------------------------------------------------------------ materials

    /// <summary>A flat colour (rubber, paint, glass): the lookbook's colour, taken down as <see cref="Flat"/>'s are.</summary>
    static void Colour(Kit k, Vector3 colour, float wear = 0.5f, float shine = 0.05f)
    {
        k.Use("cream", colour * Muted, wear, shine, tile: 1);
        k.Tint = colour * Muted;
    }

    /// <summary>
    /// A cloth in one of the game's woven or leathern textures (its weave and its normals: what the flat colours lacked),
    /// tinted to <paramref name="colour"/> (the lookbook's, as <see cref="Colour"/> takes it): the texture's own average
    /// (<see cref="Albedo"/>) divided out so the colour's what reads, the weave's light and dark kept round it.
    /// </summary>
    void Weave(Kit k, string texture, Vector3 colour, float wear = 0.6f, float shine = 0.04f, float tile = 0.35f)
    {
        k.Use(texture, colour * Muted, wear, shine, tile);
        if (look is not null && look.Layer(texture) >= 0 && Albedo.TryGetValue(texture, out var own))
            k.Tint = colour * Muted / own;
    }

    /// <summary>The textures' average colours, linear (measured off content/art/textures: their sRGB means, decoded).</summary>
    static readonly Dictionary<string, Vector3> Albedo = new()
    {
        ["wool"] = Linear(67, 67, 72),
        ["fleece"] = Linear(131, 123, 109),
        ["leather"] = Linear(93, 73, 61),
        ["coat_oilskin"] = Linear(79, 74, 65),
        ["sac"] = Linear(87, 78, 70),
    };

    static Vector3 Linear(int r, int g, int b)
    {
        static float L(int c) => MathF.Pow((c / 255f + 0.055f) / 1.055f, 2.4f);
        return new Vector3(L(r), L(g), L(b));
    }

    static void Brass(Kit k) => k.Use("brass", Palette.TarnishedBrass, 0.4f, 0.6f);
    static void Iron(Kit k) => k.Use("iron_plate", Palette.IronGrey, 0.6f, 0.3f, tile: 1);
    static void Wood(Kit k) => k.Use("wood_grey", Palette.DeepBrown, 0.8f, 0.05f, tile: 1);
    static void Rubber(Kit k) => Colour(k, new Vector3(0.075f, 0.07f, 0.065f), 0.5f, 0.3f);
    static void Glow(Kit k, Vector3 colour, float emissive)
    {
        Colour(k, colour, 0.1f, 0.5f);
        k.Emissive = emissive;
    }

    // ------------------------------------------------------------------ cloth

    /// <summary>
    /// A cloth with a thickness: <paramref name="rows"/> (each the same count) as its outside, the same moved in by
    /// <paramref name="thick"/> along each point's normal as its inside, and the strips between them round its edges, so a
    /// brim, a hem, a hood's edge has the depth of felt or wool, not of paper. Its outside faces away from
    /// <paramref name="from"/> (each row's own middle when null: what's wrapped round a head or a body).
    /// </summary>
    /// <param name="face">Its outside drawn here too (false: <see cref="Checked"/> draws it square by square).</param>
    static void Thick(Kit k, IReadOnlyList<Vector3[]> rows, float thick, bool closed = false, Vector3? from = null, Vector3? outward = null, bool face = true)
    {
        int m = rows.Count, n = rows[0].Length;
        var inner = new List<Vector3[]>(m);
        Vector3 Middle(int i)
        {
            var s = Vector3.Zero;
            foreach (var p in rows[i])
                s += p;
            return s / n;
        }
        Vector3 Out(int i, int j)
        {
            var p = rows[i][j];
            var du = closed ? rows[i][(j + 1) % n] - rows[i][(j + n - 1) % n] : rows[i][Math.Min(j + 1, n - 1)] - rows[i][Math.Max(j - 1, 0)];
            var dv = rows[Math.Min(i + 1, m - 1)][j] - rows[Math.Max(i - 1, 0)][j];
            var nn = Vector3.Cross(du, dv);
            if (nn.LengthSquared() < 1e-14f)
                nn = p - (from ?? Middle(i));
            nn = Vector3.Normalize(nn);
            var want = outward ?? p - (from ?? Middle(i));
            return Vector3.Dot(nn, want) < 0 ? -nn : nn;
        }
        for (int i = 0; i < m; i++)
            inner.Add([.. Enumerable.Range(0, n).Select(j => rows[i][j] - Out(i, j) * thick)]);
        int mi = (m - 1) / 2, mj = (closed ? n : n - 1) / 2;
        var facing = Out(mi, mj);
        if (face)
            k.Loft(rows, closed, facing: facing);
        k.Loft(inner, closed, facing: -facing);
        k.Loft([rows[0], inner[0]], closed, twoSided: true);
        k.Loft([rows[^1], inner[^1]], closed, twoSided: true);
        if (!closed)
        {
            k.Loft([[.. rows.Select(r => r[0])], [.. inner.Select(r => r[0])]], twoSided: true);
            k.Loft([[.. rows.Select(r => r[^1])], [.. inner.Select(r => r[^1])]], twoSided: true);
        }
    }

    /// <summary>
    /// A strap or a band of cloth along <paramref name="path"/> (points on or over the body), <paramref name="width"/>
    /// across, lying flat on the surface (its across is the path's way round the body's axis), with a thickness.
    /// </summary>
    static void Strap(Kit k, IReadOnlyList<Vector3> path, float width, float thick, float axisZ = -0.015f)
    {
        var a = new Vector3[path.Count];
        var b = new Vector3[path.Count];
        for (int i = 0; i < path.Count; i++)
        {
            var along = path[Math.Min(i + 1, path.Count - 1)] - path[Math.Max(i - 1, 0)];
            var outward = new Vector3(path[i].X, 0, path[i].Z - axisZ);
            outward = outward.LengthSquared() < 1e-8f ? Vector3.UnitZ : Vector3.Normalize(outward);
            var across = Vector3.Cross(along, outward);
            across = across.LengthSquared() < 1e-12f ? Vector3.UnitX : Vector3.Normalize(across);
            a[i] = path[i] - across * (width / 2);
            b[i] = path[i] + across * (width / 2);
        }
        Thick(k, [.. Enumerable.Range(0, path.Count).Select(i => new[] { a[i], b[i] })], thick,
            outward: null, from: new Vector3(0, path.Average(p => p.Y), axisZ));
    }

    /// <summary>The point on <paramref name="s"/> at height <paramref name="y"/> that's <paramref name="x"/> across its front, <paramref name="out"/> m off it.</summary>
    static Vector3 Front(FigureSurface s, float y, float x, float @out = 0) => On(s, y, x, @out, front: true);

    /// <summary>As <see cref="Front"/>, round the back.</summary>
    static Vector3 Back(FigureSurface s, float y, float x, float @out = 0) => On(s, y, x, @out, front: false);

    /// <summary>
    /// A path over the shoulder <paramref name="x"/> across (its sign the side), <paramref name="out"/> off the body: up the
    /// back from <paramref name="back"/> m, over the shoulder's top (1.53 m, measured off the frame) and down the front to
    /// <paramref name="front"/> m. A strap, a stole, a harness goes this way, not out over the shoulder's edge.
    /// </summary>
    static List<Vector3> OverShoulder(Fit f, float x, float @out, float back, float front, int n = 6)
    {
        var path = new List<Vector3>();
        for (int i = 0; i < n; i++)
            path.Add(Back(f.Body, back + (1.46f - back) * i / n, x, @out));
        path.Add(new Vector3(x, 1.5f + @out * 0.7f, 0.08f + @out * 0.5f));
        path.Add(new Vector3(x, 1.53f + @out, 0.0f));
        path.Add(new Vector3(x, 1.5f + @out * 0.7f, -0.09f - @out * 0.5f));
        for (int i = 0; i <= n; i++)
            path.Add(Front(f.Body, 1.46f + (front - 1.46f) * i / n, x, @out));
        return path;
    }

    static Vector3 On(FigureSurface s, float y, float x, float @out, bool front)
    {
        float bearing = front ? MathF.PI : 0;
        for (int i = 0; i < 4; i++)
        {
            float r = MathF.Max(s.Radius(y, bearing) + @out, MathF.Abs(x) + 0.001f);
            float off = MathF.Asin(Math.Clamp(x / r, -1, 1));
            bearing = front ? MathF.PI - off : off;
        }
        return s.At(y, bearing, @out);
    }

    /// <summary>How far out cloth hung from <paramref name="top"/> is at <paramref name="y"/> and <paramref name="bearing"/>: the
    /// furthest the body comes out anywhere between (a skirt falls past the hips, it doesn't go in between the legs).</summary>
    static float Hang(FigureSurface s, float top, float y, float bearing)
    {
        float r = 0;
        for (float h = y; h <= top + 1e-4f; h += 0.02f)
            r = MathF.Max(r, s.Radius(h, bearing));
        return MathF.Max(r, s.Radius(top, bearing));
    }

    /// <summary>A gentle wobble round and down cloth (its folds), the same each build.</summary>
    static float Folds(float bearing, float y, float amount, float round = 7, float down = 9) =>
        amount * (0.6f * MathF.Sin(bearing * round + y * down) + 0.4f * MathF.Sin(bearing * (round * 1.7f) - y * down * 1.3f + 1.3f));

    /// <summary><paramref name="n"/> bearings evenly from <paramref name="from"/> to <paramref name="to"/>, both ends in.</summary>
    static float[] Bearings(float from, float to, int n) => [.. Enumerable.Range(0, n).Select(i => from + (to - from) * i / (n - 1))];

    /// <summary>A crown over the skull, from a band at <paramref name="band"/> (each bearing's own height: a tilted band) up
    /// over the top, <paramref name="ease"/> off it at the band and <paramref name="lift"/> higher than it at the top; its
    /// sides as the skull's, or stood straighter by <paramref name="stand"/> (0: hugging, 1: a straight drum).</summary>
    static List<Vector3[]> Crown(Fit f, Func<float, float> band, float ease, float top, float stand, int n = 28, int levels = 7,
        Func<float, float, float>? extra = null)
    {
        var rows = new List<Vector3[]>();
        var bearings = Enumerable.Range(0, n).Select(i => i * MathF.Tau / n).ToArray();
        for (int l = 0; l <= levels; l++)
        {
            float t = l / (float)levels;
            var ring = new Vector3[n];
            for (int j = 0; j < n; j++)
            {
                float a = bearings[j], y0 = band(a);
                // Up the side, then rounding over: the last fifth turns in to the top's middle.
                float y = y0 + (top - y0) * MathF.Sin(t * MathF.PI / 2);
                float atBand = f.Head.Radius(y0, a) + ease;
                float hug = f.Head.Radius(MathF.Min(y, 1.79f), a) + ease;
                float r = hug + (atBand - hug) * stand;
                r *= MathF.Cos(MathF.Pow(t, 2.2f) * MathF.PI / 2);
                r += extra?.Invoke(a, t) ?? 0;
                ring[j] = new Vector3(MathF.Sin(a) * r, y, f.Head.AxisZ + MathF.Cos(a) * r);
            }
            rows.Add(ring);
        }
        return rows;
    }

    /// <summary>A brim round a band (<see cref="Crown"/>'s), <paramref name="wide"/> out from it (more behind and before by
    /// <paramref name="back"/> and <paramref name="front"/>), its edge dropped by <paramref name="droop"/> (negative: turned up).</summary>
    static void Brim(Kit k, Fit f, Func<float, float> band, float ease, float wide, float droop, float thick, float back = 0, float front = 0,
        float roll = 0, int n = 32)
    {
        var rows = new List<Vector3[]>();
        foreach (float t in (float[])[0, 0.35f, 0.7f, 1])
        {
            var ring = new Vector3[n];
            for (int j = 0; j < n; j++)
            {
                float a = j * MathF.Tau / n, c = MathF.Cos(a), y0 = band(a);
                float w = wide + back * MathF.Max(0, c) + front * MathF.Max(0, -c);
                float r = f.Head.Radius(y0, a) + ease + w * t;
                // The edge rolled up (a felt hat's) over its last third.
                float y = y0 - droop * t * t + roll * MathF.Max(0, t - 0.7f) / 0.3f;
                ring[j] = new Vector3(MathF.Sin(a) * r, y, f.Head.AxisZ + c * r);
            }
            rows.Add(ring);
        }
        Thick(k, rows, thick, closed: true, outward: Vector3.UnitY);
    }

    /// <summary>A ring of points moved out from the head's axis by <paramref name="by"/> (a band laid over a crown).</summary>
    static Vector3[] Out(Vector3[] ring, Fit f, float by) =>
        [.. ring.Select(p => p + Vector3.Normalize(new Vector3(p.X, 0, p.Z - f.Head.AxisZ)) * by)];

    // ------------------------------------------------------------------ the trapper

    /// <summary>A bucket hat in oilskin: a soft crown, a little dented on top, its brim turned down all round.</summary>
    void Bucket(Kit k, Fit f)
    {
        Weave(k, "coat_oilskin", new Vector3(0.72f, 0.54f, 0.18f), 0.5f, 0.3f);
        static float Band(float a) => 1.7f;
        var crown = Crown(f, Band, 0.016f, 1.845f, 0.85f, extra: (a, t) => 0.006f * MathF.Sin(t * MathF.PI));
        // The dent: the middle of the top pressed in.
        crown[^1] = [.. crown[^1].Select(p => p with { Y = p.Y - 0.012f })];
        k.Loft(crown, closed: true, facing: Vector3.Zero);
        k.Cap(crown[^1], Vector3.UnitY);
        Brim(k, f, Band, 0.016f, 0.07f, 0.05f, 0.006f, back: 0.012f);
        // The band of stitching round the crown's foot.
        Weave(k, "coat_oilskin", new Vector3(0.55f, 0.4f, 0.12f), 0.5f, 0.2f);
        Thick(k, [Out(crown[0], f, 0.002f), Out([.. crown[0].Select((p, j) => Vector3.Lerp(p, crown[1][j], 0.3f))], f, 0.002f)], 0.003f, closed: true);
    }

    /// <summary>
    /// A gas mask (the trapper's, the butcher's): a black rubber facepiece moulded over the face from the brow to under
    /// the chin, two round glass eyepieces in brass, a filter can screwed in under the nose, and its straps round the head.
    /// </summary>
    void Snout(Kit k, Fit f)
    {
        Rubber(k);
        Facepiece(k, f, 1.705f, 1.505f, 1.05f, 0.012f, snout: 0.03f);
        // The eyepieces: brass-rimmed rounds of dark glass, sat out on the facepiece.
        foreach (float side in (float[])[-1, 1])
        {
            var eye = Front(f.Head, EyeY + 0.004f, side * 0.04f, 0.02f);
            var normal = Vector3.Normalize(new Vector3(side * 0.25f, 0.05f, -1));
            Brass(k);
            k.Cylinder(eye - normal * 0.006f, eye + normal * 0.012f, 0.027f, 14);
            Glow(k, new Vector3(0.06f, 0.08f, 0.08f), 0.04f);
            k.Disc(eye + normal * 0.0125f, normal, 0.021f, 14);
            k.Emissive = 0;
        }
        // The can: olive tin on a rubber neck, ribbed, its end dark with a grille.
        var root = Front(f.Head, 1.585f, 0, 0.03f);
        var dir = Vector3.Normalize(new Vector3(0, -0.5f, -1));
        Rubber(k);
        k.Cylinder(root - dir * 0.01f, root + dir * 0.03f, 0.03f, 14, radiusB: 0.034f);
        k.Use("paint_olive", new Vector3(0.24f, 0.26f, 0.18f), 0.6f, 0.3f, tile: 0.3f);
        k.Tint = new Vector3(0.5f, 0.52f, 0.38f);
        var can = root + dir * 0.03f;
        k.Cylinder(can, can + dir * 0.075f, 0.043f, 16);
        foreach (float at in (float[])[0.015f, 0.04f, 0.065f])
            k.Cylinder(can + dir * (at - 0.003f), can + dir * (at + 0.003f), 0.046f, 16);
        Rubber(k);
        k.Disc(can + dir * 0.0755f, dir, 0.04f, 16);
        Colour(k, new Vector3(0.04f, 0.04f, 0.04f), 0.5f, 0.1f);
        k.Disc(can + dir * 0.0765f, dir, 0.022f, 12);
        // The straps round the back of the head, to the facepiece's edge.
        Weave(k, "leather", new Vector3(0.12f, 0.1f, 0.08f), 0.6f, 0.15f);
        foreach (float y in (float[])[1.6f, 1.705f])
            Thick(k, [f.Head.Ring(y - 0.011f, 0.006f, 14, -MathF.PI * 0.7f, MathF.PI * 0.7f, false),
                f.Head.Ring(y + 0.011f, 0.006f, 14, -MathF.PI * 0.7f, MathF.PI * 0.7f, false)], 0.003f);
    }

    /// <summary>A rubber facepiece moulded over the face from <paramref name="brow"/> down to <paramref name="chin"/>, either side
    /// of the front by <paramref name="half"/> (rad), off the skin by <paramref name="ease"/>, pushed out at the mouth by
    /// <paramref name="snout"/> for the can.</summary>
    static void Facepiece(Kit k, Fit f, float brow, float chin, float half, float ease, float snout)
    {
        var rows = new List<Vector3[]>();
        const int levels = 9, n = 17;
        for (int l = 0; l <= levels; l++)
        {
            float y = brow + (chin - brow) * l / levels;
            // Narrower at the brow and the chin: an oval round the face.
            float w = half * (0.72f + 0.28f * MathF.Sin(MathF.PI * l / levels));
            rows.Add([.. Bearings(MathF.PI - w, MathF.PI + w, n).Select(a =>
            {
                float front = MathF.Max(0, -MathF.Cos(a));
                float push = snout * front * front * MathF.Exp(-MathF.Pow((y - 1.585f) / 0.045f, 2));
                // Under the chin it closes in toward the throat.
                float under = y < 1.55f ? (1.55f - y) * 0.8f : 0;
                return f.Head.At(MathF.Max(y, 1.53f), a, ease + push - under) with { Y = y };
            })]);
        }
        Thick(k, rows, 0.006f, from: new Vector3(0, 1.62f, f.Head.AxisZ));
    }

    /// <summary>A gaff: a long ash pole with an iron hook at its head, held upright, the grip at the origin.</summary>
    static void Gaff(Kit k)
    {
        Wood(k);
        k.Cylinder(new Vector3(0, -0.85f, 0), new Vector3(0, 0.95f, 0), 0.02f, 8);
        Iron(k);
        k.Shade(0.6f);
        k.Cylinder(new Vector3(0, 0.88f, 0), new Vector3(0, 0.96f, 0), 0.023f, 8);
        var hook = new List<Vector3>();
        for (int i = 0; i <= 10; i++)
        {
            float a = MathF.PI * 1.25f * i / 10;
            hook.Add(i == 0 ? new Vector3(0, 0.95f, 0) : new Vector3(0, 1.1f + MathF.Sin(a) * 0.05f, -0.05f + MathF.Cos(a) * 0.05f - 0.0f));
        }
        for (int i = 0; i + 1 < hook.Count; i++)
            k.Cylinder(hook[i], hook[i + 1], 0.011f - 0.0006f * i, 6);
        // A net bundled on it, under the hook: a few hanging lengths of twine.
        Colour(k, new Vector3(0.35f, 0.32f, 0.24f), 0.7f, 0);
        for (int i = 0; i < 5; i++)
        {
            float a = i * 1.26f;
            k.Rod(new Vector3(0, 0.82f, 0), new Vector3(MathF.Sin(a) * 0.09f, 0.4f + 0.05f * (i % 2), MathF.Cos(a) * 0.09f), 0.005f, 3);
        }
    }

    /// <summary>A lobster pot carried by its rope at the left fist: a half-round cage of laths on a board floor, netted.</summary>
    static void Pot(Kit k)
    {
        const float L = 0.5f, W = 0.32f, H = 0.26f, Y0 = -0.55f, X = -0.12f;
        Wood(k);
        k.Tint = new Vector3(0.6f, 0.5f, 0.38f);
        k.BevelBox(new Vector3(X - W / 2, Y0, -L / 2), new Vector3(X + W / 2, Y0 + 0.02f, L / 2), 0.005f);
        // Three bows over, laths along them.
        foreach (float z in (float[])[-L / 2, 0, L / 2])
            for (int i = 0; i < 8; i++)
            {
                float a0 = MathF.PI * i / 8, a1 = MathF.PI * (i + 1) / 8;
                k.Cylinder(new Vector3(X - MathF.Cos(a0) * W / 2, Y0 + MathF.Sin(a0) * H, z), new Vector3(X - MathF.Cos(a1) * W / 2, Y0 + MathF.Sin(a1) * H, z), 0.009f, 5);
            }
        for (int i = 0; i <= 6; i++)
        {
            float a = MathF.PI * i / 6;
            var p = new Vector3(X - MathF.Cos(a) * W / 2, Y0 + MathF.Sin(a) * H, 0);
            k.Rod(p with { Z = -L / 2 }, p with { Z = L / 2 }, 0.007f, 3);
        }
        // The net over the ends, a mesh of twine.
        Colour(k, new Vector3(0.4f, 0.36f, 0.27f), 0.7f, 0);
        foreach (float z in (float[])[-L / 2, L / 2])
            for (int i = 1; i < 6; i++)
            {
                float a = MathF.PI * i / 6;
                k.Rod(new Vector3(X - MathF.Cos(a) * W / 2, Y0 + MathF.Sin(a) * H, z), new Vector3(X, Y0 + H * 0.35f, z), 0.0025f, 3);
            }
        // The rope up to the fist.
        Colour(k, new Vector3(0.45f, 0.4f, 0.3f), 0.7f, 0);
        k.Cylinder(new Vector3(X, Y0 + H, -0.15f), new Vector3(0, 0, 0), 0.007f, 4);
        k.Cylinder(new Vector3(X, Y0 + H, 0.15f), new Vector3(0, 0, 0), 0.007f, 4);
    }

    // ------------------------------------------------------------------ the watchman

    /// <summary>
    /// A fur ushanka: deep fur all round, its crown in the same pile, the flaps down over the ears to the jaw, the front
    /// flap turned up over the brow, its ties hanging.
    /// </summary>
    void Ushanka(Kit k, Fit f)
    {
        var fur = new Vector3(0.26f, 0.19f, 0.13f);
        Weave(k, "fleece", fur, 0.7f, 0.0f, 0.2f);
        static float Band(float a) => 1.69f;
        // Fur's lumpy: the pile not quite even round and over.
        var crown = Crown(f, Band, 0.03f, 1.87f, 0.6f, extra: (a, t) => 0.006f * MathF.Sin(a * 5 + t * 4) * MathF.Sin(t * MathF.PI));
        k.Loft(crown, closed: true, facing: Vector3.Zero);
        k.Cap(crown[^1], Vector3.UnitY);
        // The flaps over the ears: thick pads from the band down to the jaw, rounded at their foot.
        foreach (float side in (float[])[-1, 1])
        {
            var rows = new List<Vector3[]>();
            for (int l = 0; l <= 5; l++)
            {
                float t = l / 5f, y = 1.71f - 0.2f * t, w = 0.62f * MathF.Sqrt(MathF.Max(0.15f, 1 - MathF.Pow(t, 4)));
                rows.Add([.. Bearings(side * MathF.PI / 2 - w, side * MathF.PI / 2 + w, 9).Select(a =>
                    f.Head.At(MathF.Max(y, 1.56f), a, 0.03f + 0.012f * t) with { Y = y })]);
            }
            Thick(k, rows, 0.028f, from: new Vector3(0, 1.62f, f.Head.AxisZ));
            // The tie hanging from its foot.
            Weave(k, "leather", new Vector3(0.14f, 0.1f, 0.07f), 0.6f, 0.1f);
            var foot = f.Head.At(1.56f, side * MathF.PI / 2, 0.045f) with { Y = 1.515f };
            k.Rod(foot, foot + new Vector3(side * 0.01f, -0.09f, -0.01f), 0.004f, 3);
            Weave(k, "fleece", fur, 0.7f, 0.0f, 0.2f);
        }
        // The front flap turned up: a thick fur board stood up off the brow, leaning out.
        var front = new List<Vector3[]>();
        foreach (var (y, lean) in new[] { (1.69f, 0.03f), (1.75f, 0.045f), (1.8f, 0.058f) })
            front.Add([.. Bearings(MathF.PI - 1.15f, MathF.PI + 1.15f, 13).Select(a => f.Head.At(1.69f, a, lean) with { Y = y })]);
        Thick(k, front, 0.024f, from: new Vector3(0, 1.75f, f.Head.AxisZ));
        // The back flap, turned up behind.
        var back = new List<Vector3[]>();
        foreach (var (y, lean) in new[] { (1.69f, 0.03f), (1.74f, 0.04f) })
            back.Add([.. Bearings(-1.0f, 1.0f, 11).Select(a => f.Head.At(1.69f, a, lean) with { Y = y })]);
        Thick(k, back, 0.022f, from: new Vector3(0, 1.72f, f.Head.AxisZ));
    }

    /// <summary>Binoculars hung on the chest by their strap round the neck.</summary>
    void Binoculars(Kit k, Fit f)
    {
        var body = new Vector3(0.08f, 0.08f, 0.07f);
        foreach (float side in (float[])[-1, 1])
        {
            var top = Front(f.Body, 1.36f, side * 0.042f, 0.04f);
            var foot = Front(f.Body, 1.2f, side * 0.042f, 0.045f);
            Colour(k, body, 0.5f, 0.3f);
            k.Cylinder(foot, top, 0.033f, 12);
            k.Cylinder(top, top + new Vector3(0, 0.03f, 0.004f), 0.026f, 12);
            Brass(k);
            k.Cylinder(foot - new Vector3(0, 0.008f, 0), foot + new Vector3(0, 0.014f, 0), 0.036f, 12);
            Glow(k, new Vector3(0.1f, 0.12f, 0.12f), 0.05f);
            k.Disc(foot - new Vector3(0, 0.0085f, 0), -Vector3.UnitY, 0.028f, 12);
            k.Emissive = 0;
        }
        Colour(k, body, 0.5f, 0.3f);
        k.BevelBox(Front(f.Body, 1.32f, 0, 0.032f) - new Vector3(0.018f, 0.04f, 0.012f), Front(f.Body, 1.32f, 0, 0.032f) + new Vector3(0.018f, 0.04f, 0.012f), 0.005f);
        Brass(k);
        k.Cylinder(Front(f.Body, 1.375f, 0, 0.04f), Front(f.Body, 1.375f, 0, 0.04f) + new Vector3(0, 0.012f, 0), 0.012f, 8);
        // The strap from each barrel's top up round the neck.
        Weave(k, "leather", new Vector3(0.2f, 0.14f, 0.09f), 0.6f, 0.15f);
        var strap = OverShoulder(f, -0.085f, 0.012f, 1.44f, 1.4f);
        strap.Reverse();
        strap.AddRange(OverShoulder(f, 0.085f, 0.012f, 1.44f, 1.4f));
        Strap(k, [Front(f.Body, 1.38f, -0.06f, 0.035f), .. strap, Front(f.Body, 1.38f, 0.06f, 0.035f)], 0.016f, 0.004f, f.Body.AxisZ);
    }

    /// <summary>A signal flag on its staff, held upright: red over white, a little blown, its cloth with a hem.</summary>
    static void Flag(Kit k)
    {
        Wood(k);
        k.Cylinder(new Vector3(0, -0.5f, 0), new Vector3(0, 0.95f, 0), 0.015f, 8);
        Brass(k);
        k.Lathe(new Vector3(0, 0.95f, 0), [new(0.018f, 0), new(0.02f, 0.012f), new(0, 0.03f)], 8);
        float x0 = 0.015f;
        Vector3 P(float u, float v) => new(x0 + u * 0.34f, 0.92f - v * 0.24f - u * u * 0.02f, MathF.Sin(u * 3.4f + v * 0.6f) * 0.035f * u);
        foreach (var (v0, v1, colour) in new[] { (0f, 0.5f, new Vector3(0.6f, 0.08f, 0.06f)), (0.5f, 1f, new Vector3(0.8f, 0.77f, 0.7f)) })
        {
            Colour(k, colour, 0.6f, 0.02f);
            var rows = new List<Vector3[]>();
            for (int i = 0; i <= 6; i++)
                rows.Add([P(i / 6f, v0), P(i / 6f, (v0 + v1) / 2), P(i / 6f, v1)]);
            Thick(k, rows, 0.003f, outward: Vector3.UnitZ);
        }
    }

    // ------------------------------------------------------------------ the market woman

    /// <summary>
    /// A headscarf over the hair and the ears, close to the head, from the brow round the face and down the nape, knotted
    /// under the chin, its tails hanging, in <paramref name="colour"/>.
    /// </summary>
    void Headscarf(Kit k, Fit f, Vector3 colour)
    {
        Weave(k, "wool", colour, 0.65f, 0.02f, 0.25f);
        Hooded(k, f, 1.5f, 1.72f, 0.95f, 0.012f, 0.01f, 0.005f);
        // Round the face and under the chin to the knot, its tails.
        var knot = Front(f.Head, 1.515f, 0, 0.03f);
        foreach (float side in (float[])[-1, 1])
        {
            var path = new List<Vector3>();
            for (int i = 0; i <= 5; i++)
            {
                float t = i / 5f;
                path.Add(Vector3.Lerp(f.Head.At(1.6f, MathF.PI + side * 0.95f, 0.014f), knot, t) + new Vector3(0, -0.012f * MathF.Sin(t * MathF.PI), -0.01f * MathF.Sin(t * MathF.PI)));
            }
            Strap(k, path, 0.028f, 0.006f, f.Head.AxisZ);
            var tail = new List<Vector3> { knot, knot + new Vector3(side * 0.02f, -0.04f, -0.01f), knot + new Vector3(side * 0.035f, -0.09f, -0.002f) };
            Strap(k, tail, 0.03f, 0.004f, f.Head.AxisZ);
        }
        k.Lathe(knot - new Vector3(0, 0.016f, 0), [new(0, 0), new(0.018f, 0.006f), new(0.02f, 0.016f), new(0.014f, 0.028f), new(0, 0.032f)], 8);
        // Its point down the back, over the nape.
        var point = new List<Vector3[]>();
        foreach (var (y, w) in new[] { (1.52f, 0.7f), (1.46f, 0.4f), (1.42f, 0.08f) })
            point.Add([.. Bearings(-w, w, 7).Select(a => f.Head.At(1.52f, a, 0.016f + (1.52f - y) * 0.6f) with { Y = y })]);
        Thick(k, point, 0.004f, from: new Vector3(0, 1.5f, f.Head.AxisZ));
    }

    /// <summary>
    /// A cloth over the head (a headscarf, a hood): from <paramref name="foot"/> up over the top, open round the face below
    /// <paramref name="brow"/> by <paramref name="half"/> (rad either side of the front), <paramref name="ease"/> off the skull
    /// (and <paramref name="fullness"/> more behind), as thick as <paramref name="thick"/>; below the ears it falls straight.
    /// </summary>
    static void Hooded(Kit k, Fit f, float foot, float brow, float half, float ease, float fullness, float thick)
    {
        var rows = new List<Vector3[]>();
        const int levels = 12, n = 25;
        for (int l = 0; l <= levels; l++)
        {
            float t = l / (float)levels;
            float y = foot + (1.8f - foot) * MathF.Sin(t * MathF.PI / 2);
            // The face's opening closes over the brow.
            float g = y < brow ? half : half * MathF.Max(0, 1 - (y - brow) / 0.035f);
            g = MathF.Max(g, 0.02f);
            float shrink = MathF.Cos(MathF.Pow(MathF.Max(0, (y - 1.72f) / 0.085f), 2.2f) * MathF.PI / 2);
            rows.Add([.. Bearings(MathF.PI + g, MathF.Tau + MathF.PI - g, n).Select(a =>
            {
                float back = MathF.Max(0, MathF.Cos(a));
                float r = MathF.Max(f.Head.Radius(MathF.Min(y, 1.79f), a), y < 1.62f ? f.Head.Radius(1.62f, a) * 0.92f : 0) + ease + fullness * back;
                if (y > 1.72f)
                    r = (f.Head.Radius(1.72f, a) + ease + fullness * back) * MathF.Max(0.02f, shrink);
                return new Vector3(MathF.Sin(a) * r, y + (l == levels ? 0.006f : 0), f.Head.AxisZ + MathF.Cos(a) * r);
            })]);
        }
        Thick(k, rows, thick, from: new Vector3(0, 1.64f, f.Head.AxisZ));
    }

    /// <summary>
    /// A plaid shawl: thrown round the shoulders and over the arms like a short cape, down the back to its point, its two
    /// ends brought across the chest, the left's over the right's, and tucked in at the waist; red, checked in dark bands.
    /// </summary>
    void Shawl(Kit k, Fit f)
    {
        var red = new Vector3(0.52f, 0.14f, 0.1f);
        var dark = new Vector3(0.12f, 0.09f, 0.08f);
        var cross = new Vector3(0.26f, 0.08f, 0.07f);
        // Round the shoulders: from the neck's root out over the arms (it hangs clear of them, as a cape does), open in
        // front, longest behind, where it comes to its point.
        float Radius(float y, float a)
        {
            float side = MathF.Abs(MathF.Sin(a));
            float cape = side * (0.21f + MathF.Max(0, 1.5f - y) * 0.45f);
            return MathF.Max(f.Body.Radius(MathF.Max(y, 1.1f), a) + 0.02f, cape) + 0.004f * MathF.Max(0, 1.5f - y) / 0.4f;
        }
        var rows = new List<Vector3[]>();
        const int levels = 9, n = 25;
        for (int l = 0; l <= levels; l++)
        {
            float t = l / (float)levels, y = 1.51f - (1.51f - 1.08f) * t;
            // The front opening widens down to the arms, then the back narrows to the point.
            float open = 0.42f + 0.45f * MathF.Min(1, t * 2.5f);
            float w = t < 0.45f ? MathF.PI - open : (MathF.PI - open) * MathF.Max(0.04f, (1 - t) / 0.55f);
            rows.Add([.. Bearings(-w, w, n).Select(a =>
            {
                float r = Radius(y, a);
                return new Vector3(MathF.Sin(a) * r, y, f.Body.AxisZ + MathF.Cos(a) * r);
            })]);
        }
        Checked(k, rows, red, dark, cross, f.Body.AxisZ);
        // Its ends from the shoulders across the chest to the waist: the right's under, the left's over.
        foreach (float side in (float[])[-1, 1])
        {
            var across = new List<Vector3[]>();
            for (int i = 0; i <= 8; i++)
            {
                float t = i / 8f, y = 1.47f - (1.47f - 0.98f) * t;
                float x = side * (0.16f - 0.29f * t), half = 0.1f - 0.025f * t;
                float o = 0.03f + (side > 0 ? 0.008f : 0) + 0.01f * (1 - t);
                across.Add([.. Enumerable.Range(0, 4).Select(j => Front(f.Body, y + (j - 1.5f) * half * 0.35f * side, x + (j - 1.5f) * half / 1.5f, o))]);
            }
            Checked(k, across, red, dark, cross, f.Body.AxisZ);
        }
    }

    /// <summary>A cloth over <paramref name="rows"/> in a plaid: its squares <paramref name="base"/>, crossed by bands of
    /// <paramref name="band"/> one way and the other, <paramref name="cross"/> where they cross.</summary>
    void Checked(Kit k, List<Vector3[]> rows, Vector3 @base, Vector3 band, Vector3 cross, float axisZ)
    {
        int m = rows.Count, n = rows[0].Length;
        var middle = new Vector3(0, rows.Average(r => r[n / 2].Y), axisZ);
        Weave(k, "wool", @base * 0.7f, 0.65f, 0.02f, 0.25f);
        Thick(k, rows, 0.004f, from: middle, face: false);
        foreach (int kind in (int[])[0, 1, 2])
        {
            Weave(k, "wool", kind == 2 ? cross : kind == 1 ? band * 0.6f + @base * 0.4f : @base, 0.65f, 0.02f, 0.25f);
            for (int i = 0; i + 1 < m; i++)
                for (int j = 0; j + 1 < n; j++)
                {
                    int here = (i % 3 == 1 ? 1 : 0) + (j % 3 == 1 ? 1 : 0);
                    if (here == kind)
                        k.Loft([[rows[i][j], rows[i][j + 1]], [rows[i + 1][j], rows[i + 1][j + 1]]], facing: rows[i][j] - middle);
                }
        }
    }

    /// <summary>A skirt from the waist (<paramref name="top"/> m) down to <paramref name="hem"/>, hung from the hips and
    /// flaring out by <paramref name="flare"/> at the hem, folded.</summary>
    void Skirt(Kit k, Fit f, Vector3 colour, float top, float hem, float flare)
    {
        Weave(k, "wool", colour, 0.65f, 0.03f, 0.3f);
        Thick(k, SkirtRows(f, top, hem, flare, 0.012f, 40), 0.006f, closed: true);
        // The waistband.
        Weave(k, "wool", colour * 0.8f, 0.65f, 0.03f, 0.3f);
        Thick(k, [f.Body.Ring(top - 0.035f, 0.018f, 32), f.Body.Ring(top + 0.005f, 0.016f, 32)], 0.008f, closed: true);
    }

    static List<Vector3[]> SkirtRows(Fit f, float top, float hem, float flare, float ease, int n)
    {
        var rows = new List<Vector3[]>();
        const int levels = 10;
        for (int l = 0; l <= levels; l++)
        {
            float y = top + (hem - top) * l / levels;
            rows.Add([.. Enumerable.Range(0, n).Select(j =>
            {
                float a = j * MathF.Tau / n, r = SkirtRadius(f, top, hem, flare, ease, y, a);
                return new Vector3(MathF.Sin(a) * r, y, f.Body.AxisZ + MathF.Cos(a) * r);
            })]);
        }
        return rows;
    }

    /// <summary>How far out a skirt hung from <paramref name="top"/> is at <paramref name="y"/>, <paramref name="bearing"/>.</summary>
    static float SkirtRadius(Fit f, float top, float hem, float flare, float ease, float y, float bearing)
    {
        float t = Math.Clamp((top - y) / (top - hem), 0, 1);
        return Hang(f.Body, top, y, bearing) + ease + flare * MathF.Pow(t, 1.4f) + Folds(bearing, y, 0.012f * t, 8, 3);
    }

    /// <summary>The point <paramref name="x"/> across the front of a surface given by its <paramref name="radius"/> at each bearing.</summary>
    static Vector3 Across(Func<float, float> radius, float axisZ, float y, float x)
    {
        float bearing = MathF.PI;
        for (int i = 0; i < 5; i++)
            bearing = MathF.PI - MathF.Asin(Math.Clamp(x / MathF.Max(radius(bearing), MathF.Abs(x) + 0.001f), -1, 1));
        float r = radius(bearing);
        return new Vector3(MathF.Sin(bearing) * r, y, axisZ + MathF.Cos(bearing) * r);
    }

    /// <summary>A long apron over the skirt, lying on it, tied at the waist with its bow behind.</summary>
    void Apron(Kit k, Fit f)
    {
        Weave(k, "wool", new Vector3(0.62f, 0.58f, 0.5f), 0.6f, 0.02f, 0.3f);
        var rows = new List<Vector3[]>();
        for (int l = 0; l <= 8; l++)
        {
            // Down the skirt's front from the waist to a hand above its hem, lying on it.
            float t = l / 8f, y = 0.975f - (0.975f - 0.38f) * t, half = 0.19f + 0.07f * t;
            // Over the skirt's folds, not between them: as far out as the skirt comes nearby.
            rows.Add([.. Enumerable.Range(0, 13).Select(i =>
                Across(a => MathF.Max(SkirtRadius(f, 0.98f, 0.3f, 0.11f, 0.012f, y, a - 0.08f), MathF.Max(SkirtRadius(f, 0.98f, 0.3f, 0.11f, 0.012f, y, a),
                    SkirtRadius(f, 0.98f, 0.3f, 0.11f, 0.012f, y, a + 0.08f))) + 0.012f, f.Body.AxisZ, y, -half + 2 * half * i / 12))]);
        }
        Thick(k, rows, 0.004f, from: new Vector3(0, 0.7f, f.Body.AxisZ));
        // The band round the waist and its bow behind.
        Weave(k, "wool", new Vector3(0.5f, 0.46f, 0.38f), 0.6f, 0.02f, 0.3f);
        Thick(k, [f.Body.Ring(0.955f, 0.03f, 32), f.Body.Ring(0.99f, 0.028f, 32)], 0.004f, closed: true);
        var bow = f.Body.At(0.97f, 0, 0.04f);
        foreach (float side in (float[])[-1, 1])
        {
            k.Lathe(bow + new Vector3(side * 0.035f, -0.015f, 0), [new(0, 0), new(0.022f, 0.008f), new(0.024f, 0.022f), new(0, 0.03f)], 6);
            Strap(k, [bow, bow + new Vector3(side * 0.02f, -0.09f, 0.01f), bow + new Vector3(side * 0.03f, -0.2f, 0.0f)], 0.03f, 0.004f, f.Body.AxisZ);
        }
    }

    /// <summary>A ring of keys at the right hip.</summary>
    static void Keys(Kit k, Fit f)
    {
        var ring = f.Body.At(0.9f, -MathF.PI * 0.68f, 0.03f);
        Iron(k);
        k.Shade(0.8f);
        k.Lathe(ring + new Vector3(0, -0.004f, 0), [new(0.032f, 0), new(0.034f, 0.004f), new(0.032f, 0.008f)], 12, capTop: false);
        Brass(k);
        for (int i = 0; i < 5; i++)
        {
            float a = -0.6f + i * 0.3f;
            var top = ring + new Vector3(MathF.Sin(a) * 0.03f, -0.03f, MathF.Cos(a) * 0.012f);
            var tip = top + new Vector3(MathF.Sin(a) * 0.025f, -0.095f, 0);
            k.Cylinder(top, tip, 0.004f, 5);
            k.Disc(top, Vector3.Normalize(new Vector3(0, 0, -1)), 0.009f, 8);
            k.BoxAt(tip + new Vector3(0.004f, 0.012f, 0), new Vector3(0.008f, 0.004f, 0.002f));
        }
    }

    /// <summary>A crate of greens and tomatoes, held by its slatted side at the left fist.</summary>
    void Crate(Kit k)
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
        // What's in it: cabbages and leeks, a few tomatoes, heaped.
        var rng = new Random(61);
        for (int i = 0; i < 11; i++)
        {
            bool tomato = i % 3 == 2;
            Colour(k, tomato ? new Vector3(0.6f, 0.12f, 0.06f) : new Vector3(0.24f, 0.4f, 0.13f), 0.4f, tomato ? 0.45f : 0.1f);
            float r = tomato ? 0.033f : 0.055f;
            var c = new Vector3(X + (float)(rng.NextDouble() - 0.5) * (W - 0.1f), Y0 + H - 0.03f + (float)rng.NextDouble() * 0.03f, (float)(rng.NextDouble() - 0.5) * (L - 0.1f));
            k.Lathe(c - new Vector3(0, r, 0), [new(0.0f, 0), new(r * 0.7f, r * 0.2f), new(r, r * 0.8f), new(r * 0.85f, r * 1.5f), new(r * 0.3f, r * 1.9f), new(0, r * 2)], 10);
        }
    }

    // ------------------------------------------------------------------ the lamplighter

    /// <summary>A deep hood over the head, its edge standing round the face, down onto the shoulders as a cowl.</summary>
    void Hood(Kit k, Fit f)
    {
        Weave(k, "wool", new Vector3(0.4f, 0.31f, 0.22f), 0.65f, 0.02f, 0.3f);
        var rows = new List<Vector3[]>();
        const int levels = 13, n = 25;
        for (int l = 0; l <= levels; l++)
        {
            float t = l / (float)levels;
            float y = 1.38f + (1.87f - 1.38f) * MathF.Sin(t * MathF.PI / 2);
            float g = y < 1.74f ? 1.05f : 1.05f * MathF.Max(0, 1 - (y - 1.74f) / 0.04f);
            if (y < 1.5f)
                g = 1.05f - (1.5f - y) * 2.5f;
            g = MathF.Max(g, 0.02f);
            float shrink = MathF.Cos(MathF.Pow(MathF.Max(0, (y - 1.74f) / 0.13f), 1.8f) * MathF.PI / 2);
            rows.Add([.. Bearings(MathF.PI + g, MathF.Tau + MathF.PI - g, n).Select(a =>
            {
                float r;
                if (y < 1.5f)
                {
                    // The cowl: out over the shoulders.
                    float s = (1.5f - y) / 0.12f;
                    r = (f.Head.Radius(1.55f, a) + 0.05f) * (1 - s) + (f.Body.Radius(MathF.Max(y, 1.42f), a) + 0.03f) * s;
                }
                else
                    r = MathF.Max(f.Head.Radius(MathF.Min(y, 1.74f), a), f.Head.Radius(1.62f, a) * 0.95f) + 0.045f + 0.012f * MathF.Max(0, MathF.Cos(a));
                if (y > 1.74f)
                    r *= MathF.Max(0.02f, shrink);
                // Its peak a little forward over the brow.
                float z = f.Head.AxisZ - 0.03f * MathF.Max(0, (y - 1.74f) / 0.13f);
                return new Vector3(MathF.Sin(a) * r, y, z + MathF.Cos(a) * r);
            })]);
        }
        Thick(k, rows, 0.014f, from: new Vector3(0, 1.62f, f.Head.AxisZ));
    }

    /// <summary>
    /// The lamplighter's mask: a black facepiece with a little brass lamp before it, its glass burning (lit by
    /// <see cref="Dress"/>), and a hose from it over his shoulder to the bottles on his back.
    /// </summary>
    void LampMask(Kit k, Fit f, bool facepiece = true)
    {
        Rubber(k);
        if (facepiece)
            Facepiece(k, f, 1.7f, 1.51f, 0.95f, 0.012f, snout: 0.02f);
        // The lamp: a brass cage, its glass glowing, a little chimney and its cap.
        var c = MaskFlame;
        Brass(k);
        foreach (float x in (float[])[-1, 1])
            foreach (float z in (float[])[-1, 1])
                k.Cylinder(c + new Vector3(x * 0.042f, -0.06f, z * 0.038f), c + new Vector3(x * 0.042f, 0.06f, z * 0.038f), 0.005f, 5);
        k.BevelBox(c + new Vector3(-0.052f, 0.058f, -0.048f), c + new Vector3(0.052f, 0.072f, 0.048f), 0.004f);
        k.BevelBox(c + new Vector3(-0.052f, -0.072f, -0.048f), c + new Vector3(0.052f, -0.058f, 0.048f), 0.004f);
        k.Cylinder(c + new Vector3(0, 0.072f, 0), c + new Vector3(0, 0.11f, 0), 0.014f, 8);
        k.Lathe(c + new Vector3(0, 0.11f, 0), [new(0.026f, 0), new(0, 0.016f)], 8);
        // Its arm back to the mask's brow.
        k.Cylinder(c + new Vector3(0, 0.0f, 0.038f), Front(f.Head, 1.66f, 0, 0.02f), 0.008f, 6);
        Glow(k, new Vector3(1.0f, 0.75f, 0.35f), 1.6f);
        k.BoxAt(c, new Vector3(0.036f, 0.052f, 0.032f));
        k.Emissive = 0;
        // The hose from the mask's chin over the right shoulder, sagging.
        Rubber(k);
        var hose = new List<Vector3>();
        for (int i = 0; i <= 10; i++)
        {
            float t = i / 10f;
            var p = Vector3.Lerp(Front(f.Head, 1.54f, 0.03f, 0.02f), f.Body.At(1.5f, -MathF.PI * 0.55f, 0.03f), t);
            hose.Add(p - new Vector3(0, 0.04f * MathF.Sin(t * MathF.PI), 0));
        }
        for (int i = 0; i + 1 < hose.Count; i++)
            k.Cylinder(hose[i], hose[i + 1], 0.013f, 7);
    }

    /// <summary>Two bottles on his back in a harness, their valves, the hose's other end over his shoulder.</summary>
    void Bottles(Kit k, Fit f)
    {
        foreach (float x in (float[])[-0.065f, 0.065f])
        {
            var b = Back(f.Body, 0.98f, x, 0.07f);
            Iron(k);
            k.Tint = new Vector3(0.58f, 0.52f, 0.4f);
            k.Lathe(b, [new(0, 0), new(0.05f, 0.005f), new(0.06f, 0.03f), new(0.06f, 0.4f), new(0.045f, 0.45f), new(0.016f, 0.47f), new(0, 0.475f)], 14);
            Brass(k);
            k.Cylinder(b + new Vector3(0, 0.47f, 0), b + new Vector3(0, 0.52f, 0), 0.016f, 8);
            k.Cylinder(b + new Vector3(-0.02f, 0.5f, 0), b + new Vector3(0.02f, 0.5f, 0), 0.008f, 6);
        }
        // The harness: straps over the shoulders and a band round them.
        Weave(k, "leather", new Vector3(0.2f, 0.13f, 0.08f), 0.6f, 0.15f);
        foreach (float side in (float[])[-1, 1])
            Strap(k, OverShoulder(f, side * 0.13f, 0.014f, 1.1f, 1.2f), 0.035f, 0.005f, f.Body.AxisZ);
        Thick(k, [Bearings(-0.9f, 0.9f, 9).Select(a => f.Body.At(1.08f, a, 0.012f)).ToArray(),
            Bearings(-0.9f, 0.9f, 9).Select(a => f.Body.At(1.12f, a, 0.012f)).ToArray()], 0.005f, from: new Vector3(0, 1.1f, f.Body.AxisZ));
        Rubber(k);
        var from = Back(f.Body, 1.5f, 0.065f, 0.07f);
        var to = f.Body.At(1.5f, -MathF.PI * 0.55f, 0.03f);
        for (int i = 0; i < 6; i++)
        {
            float t0 = i / 6f, t1 = (i + 1) / 6f;
            k.Cylinder(Vector3.Lerp(from, to, t0) + new Vector3(0, 0.03f * MathF.Sin(t0 * MathF.PI), 0), Vector3.Lerp(from, to, t1) + new Vector3(0, 0.03f * MathF.Sin(t1 * MathF.PI), 0), 0.013f, 7);
        }
    }

    /// <summary>A lamplighter's pole, held upright: the lamp on its head, under a little roof, out on an arm.</summary>
    static void LampPole(Kit k)
    {
        Wood(k);
        k.Cylinder(new Vector3(0, -0.85f, 0), new Vector3(0, 1.05f, 0), 0.018f, 8);
        Iron(k);
        k.Shade(0.5f);
        k.Cylinder(new Vector3(0, 1.02f, 0), new Vector3(0, 1.02f, PoleFlame.Z), 0.007f, 5);
        k.Cylinder(new Vector3(0, 0.96f, 0), new Vector3(0, 1.02f, PoleFlame.Z * 0.6f), 0.005f, 5);
        var c = PoleFlame;
        foreach (float x in (float[])[-1, 1])
            foreach (float z in (float[])[-1, 1])
                k.Cylinder(c + new Vector3(x * 0.05f, -0.07f, z * 0.05f), c + new Vector3(x * 0.05f, 0.08f, z * 0.05f), 0.005f, 5);
        k.Lathe(c + new Vector3(0, 0.08f, 0), [new(0.08f, 0), new(0.075f, 0.008f), new(0.0f, 0.06f)], 10);
        k.BevelBox(c + new Vector3(-0.06f, -0.083f, -0.06f), c + new Vector3(0.06f, -0.069f, 0.06f), 0.004f);
        Glow(k, new Vector3(1.0f, 0.75f, 0.35f), 1.8f);
        k.BoxAt(c, new Vector3(0.042f, 0.062f, 0.042f));
        k.Emissive = 0;
    }

    // ------------------------------------------------------------------ the linesman

    /// <summary>A flat cap, its crown full and drawn forward over a short stiff peak (with a pair of goggles pushed up on its
    /// front when <paramref name="goggles"/>).</summary>
    void FlatCap(Kit k, Fit f, Vector3 colour, bool goggles)
    {
        Weave(k, "wool", colour, 0.65f, 0.03f, 0.2f);
        // The band sits lower in front than behind.
        static float Band(float a) => 1.722f + 0.018f * MathF.Cos(a);
        var crown = Crown(f, Band, 0.014f, 1.81f, 0.9f, n: 28, levels: 6, extra: (a, t) =>
            // Full and drawn forward: out over the peak in front, its top flatter.
            MathF.Sin(t * MathF.PI * 0.9f) * (0.018f + 0.022f * MathF.Max(0, -MathF.Cos(a))));
        for (int l = 1; l < crown.Count; l++)
        {
            float t = l / (float)(crown.Count - 1);
            crown[l] = [.. crown[l].Select(p => p + new Vector3(0, -0.035f * t * t, -0.022f * MathF.Sin(t * MathF.PI)))];
        }
        k.Loft(crown, closed: true, facing: Vector3.Zero);
        k.Cap(crown[^1], Vector3.UnitY);
        // The peak: short, stiff, curved over the brow.
        var peak = new List<Vector3[]>();
        foreach (var (t, drop) in new[] { (0f, 0f), (0.5f, 0.006f), (1f, 0.016f) })
            peak.Add([.. Bearings(MathF.PI - 0.95f, MathF.PI + 0.95f, 11).Select(a =>
            {
                float y = Band(a) - drop, r = f.Head.Radius(Band(a), a) + 0.014f + t * 0.055f * MathF.Pow(MathF.Max(0, -MathF.Cos(a)), 0.7f);
                return new Vector3(MathF.Sin(a) * r, y, f.Head.AxisZ + MathF.Cos(a) * r);
            })]);
        Thick(k, peak, 0.005f, outward: Vector3.UnitY);
        if (!goggles)
            return;
        // The goggles pushed up on the cap's front, their strap round it.
        foreach (float side in (float[])[-1, 1])
        {
            var lens = Front(f.Head, 1.755f, side * 0.042f, 0.05f) + new Vector3(0, 0.012f, -0.012f);
            var normal = Vector3.Normalize(new Vector3(side * 0.2f, 0.45f, -1));
            Brass(k);
            k.Cylinder(lens - normal * 0.014f, lens + normal * 0.008f, 0.028f, 14);
            Glow(k, new Vector3(0.22f, 0.28f, 0.28f), 0.12f);
            k.Disc(lens + normal * 0.0085f, normal, 0.022f, 14);
            k.Emissive = 0;
        }
        Brass(k);
        k.Cylinder(Front(f.Head, 1.755f, -0.013f, 0.05f) + new Vector3(0, 0.012f, -0.014f), Front(f.Head, 1.755f, 0.013f, 0.05f) + new Vector3(0, 0.012f, -0.014f), 0.005f, 6);
        Weave(k, "leather", new Vector3(0.12f, 0.1f, 0.08f), 0.6f, 0.1f);
        Vector3 On(int level, float a, float lift) => crown[level][(int)MathF.Round((a + MathF.Tau) % MathF.Tau / MathF.Tau * 28) % 28]
            + new Vector3(0, lift, 0) + Vector3.Normalize(new Vector3(MathF.Sin(a), 0, MathF.Cos(a))) * 0.004f;
        var strap = Bearings(-MathF.PI * 0.82f, MathF.PI * 0.82f, 17);
        Thick(k, [[.. strap.Select(a => On(2, a, -0.006f))], [.. strap.Select(a => On(2, a, 0.012f))]], 0.003f, from: new Vector3(0, 1.76f, f.Head.AxisZ));
    }

    /// <summary>A wool wrap wound over the nose and mouth and round the neck, folded, knotted behind, its tails down the back.</summary>
    void Wrap(Kit k, Fit f, Vector3 colour)
    {
        Weave(k, "wool", colour, 0.65f, 0.02f, 0.25f);
        const int n = 30;
        var rows = new List<Vector3[]>();
        const int levels = 8;
        for (int l = 0; l <= levels; l++)
        {
            float t = l / (float)levels, y = 1.49f + (1.645f - 1.49f) * t;
            // Wound in turns: a fold out, a fold in, up the face.
            float turn = 0.007f * (l % 2 == 1 ? 1 : 0);
            rows.Add([.. Enumerable.Range(0, n).Select(j =>
            {
                float a = j * MathF.Tau / n;
                float r = MathF.Max(f.Head.Radius(MathF.Max(y, 1.5f), a), f.Head.Radius(1.6f, a) * 0.86f) + 0.02f + turn + Folds(a, y, 0.004f);
                // Over the nose it's held off the face by it.
                return new Vector3(MathF.Sin(a) * r, y, f.Head.AxisZ + MathF.Cos(a) * r);
            })]);
        }
        Thick(k, rows, 0.008f, closed: true);
        // The knot behind and its tails.
        var knot = Back(f.Head, 1.57f, 0.02f, 0.035f);
        k.Lathe(knot - new Vector3(0, 0.02f, 0), [new(0, 0), new(0.022f, 0.008f), new(0.026f, 0.02f), new(0.018f, 0.034f), new(0, 0.04f)], 8);
        foreach (float side in (float[])[-1, 1])
            Strap(k, [knot, knot + new Vector3(side * 0.02f, -0.06f, 0.02f), knot + new Vector3(side * 0.03f, -0.13f, 0.03f)], 0.035f, 0.005f, f.Head.AxisZ);
    }

    /// <summary>A lineman's belt: leather round the waist, its buckle, its pouches, a pair of pliers and a hammer hung from it.</summary>
    void ToolBelt(Kit k, Fit f)
    {
        Weave(k, "leather", new Vector3(0.24f, 0.16f, 0.1f), 0.6f, 0.2f);
        Thick(k, [f.Body.Ring(0.925f, 0.014f, 32), f.Body.Ring(0.985f, 0.014f, 32)], 0.008f, closed: true);
        foreach (float a in (float[])[-2.1f, 2.35f, 1.55f])
        {
            var at = f.Body.At(0.9f, a, 0.04f);
            k.With(Matrix4x4.CreateRotationY(a) * Matrix4x4.CreateTranslation(at), () =>
            {
                k.BevelBox(new Vector3(-0.045f, -0.07f, -0.022f), new Vector3(0.045f, 0.06f, 0.022f), 0.01f);
                k.BevelBox(new Vector3(-0.048f, 0.03f, -0.026f), new Vector3(0.048f, 0.065f, 0.026f), 0.008f);
            });
        }
        Brass(k);
        var buckle = Front(f.Body, 0.955f, 0, 0.02f);
        k.BevelBox(buckle - new Vector3(0.03f, 0.028f, 0.004f), buckle + new Vector3(0.03f, 0.028f, 0.004f), 0.004f);
        Iron(k);
        k.Shade(0.6f);
        var pl = f.Body.At(0.93f, MathF.PI * 0.6f, 0.03f);
        k.Cylinder(pl, pl + new Vector3(0.012f, -0.17f, -0.01f), 0.007f, 5);
        k.Cylinder(pl, pl + new Vector3(-0.004f, -0.17f, 0.006f), 0.007f, 5);
        Wood(k);
        var hm = f.Body.At(0.95f, -MathF.PI * 0.42f, 0.035f);
        k.Cylinder(hm, hm + new Vector3(0.005f, -0.24f, 0), 0.012f, 6);
        Iron(k);
        k.Shade(0.7f);
        k.BevelBox(hm + new Vector3(-0.015f, -0.27f, -0.05f), hm + new Vector3(0.015f, -0.235f, 0.04f), 0.005f);
    }

    /// <summary>A coil of cable hung at his right hip on a strap across the chest from the left shoulder.</summary>
    void Coil(Kit k, Fit f)
    {
        Weave(k, "leather", new Vector3(0.12f, 0.1f, 0.08f), 0.4f, 0.4f);
        var c = f.Body.At(0.98f, -MathF.PI / 2, 0.09f);
        const int n = 24, m = 8;
        foreach (float wind in (float[])[0, 0.012f, -0.012f])
        {
            var rings = new List<Vector3[]>();
            for (int i = 0; i <= n; i++)
            {
                float a = i * MathF.Tau / n;
                var centre = c + new Vector3(wind, MathF.Sin(a) * 0.16f, MathF.Cos(a) * 0.16f);
                var outward = new Vector3(0, MathF.Sin(a), MathF.Cos(a));
                rings.Add([.. Enumerable.Range(0, m).Select(j =>
                {
                    float b = j * MathF.Tau / m;
                    return centre + outward * (MathF.Cos(b) * 0.012f) + Vector3.UnitX * (MathF.Sin(b) * 0.012f);
                })]);
            }
            k.Loft(rings, closed: true, facing: Vector3.Zero);
        }
        Weave(k, "leather", new Vector3(0.2f, 0.14f, 0.09f), 0.6f, 0.15f);
        // From the coil up across the chest, over the left shoulder, and down the back to the right hip.
        var path = new List<Vector3> { c + new Vector3(-0.02f, 0.17f, 0) };
        for (int i = 0; i <= 5; i++)
        {
            float t = i / 5f;
            path.Add(Front(f.Body, 1.12f + 0.32f * t, -0.2f + 0.33f * t, 0.016f));
        }
        var over = OverShoulder(f, 0.13f, 0.016f, 1.12f, 1.44f);
        over.Reverse();
        path.AddRange(over.Skip(1));
        for (int i = 1; i <= 4; i++)
            path.Add(Back(f.Body, 1.12f - 0.03f * i, 0.13f - 0.08f * i, 0.016f));
        Strap(k, path, 0.035f, 0.005f, f.Body.AxisZ);
    }

    // ------------------------------------------------------------------ the preacher

    /// <summary>A wide-brimmed black hat of felt, its crown low and flat, its band, its brim's edge rolled up a little.</summary>
    void WideHat(Kit k, Fit f)
    {
        Weave(k, "wool", new Vector3(0.07f, 0.065f, 0.06f), 0.5f, 0.06f, 0.25f);
        static float Band(float a) => 1.72f;
        var crown = Crown(f, Band, 0.016f, 1.865f, 1.0f, extra: (a, t) => -0.006f * t);
        crown[^1] = [.. crown[^1].Select(p => p with { Y = p.Y - 0.008f })];
        k.Loft(crown, closed: true, facing: Vector3.Zero);
        k.Cap(crown[^1], Vector3.UnitY);
        Brim(k, f, Band, 0.016f, 0.16f, 0.015f, 0.008f, roll: 0.022f);
        Weave(k, "wool", new Vector3(0.14f, 0.11f, 0.08f), 0.5f, 0.12f, 0.25f);
        Thick(k, [Out(crown[0], f, 0.003f), Out([.. crown[0].Select(p => p + new Vector3(0, 0.03f, 0))], f, 0.002f)], 0.003f, closed: true);
    }

    /// <summary>
    /// A veil hung from under the hat's brim over the face to the chest: black gauze, seen through face on and dense where
    /// it's seen edge on (the ghosts' screen-door membrane, its cold rim off: <see cref="Skinner.GhostWear"/>), falling in
    /// soft folds.
    /// </summary>
    void Veil(Kit k, Fit f)
    {
        k.Use("cream", new Vector3(0.02f, 0.02f, 0.02f), Skinner.GhostWear, 0.05f, tile: 1);
        k.Tint = new Vector3(0.02f, 0.02f, 0.02f);
        var rows = new List<Vector3[]>();
        foreach (var (y, more) in new[] { (1.714f, 0.06f), (1.66f, 0.064f), (1.6f, 0.07f), (1.53f, 0.078f), (1.47f, 0.085f) })
            rows.Add([.. Enumerable.Range(0, 32).Select(j =>
            {
                float a = j * MathF.Tau / 32;
                return f.Head.At(1.68f, a, more + Folds(a, 0, 0.012f * (1.72f - y) / 0.25f, 11, 0)) with { Y = y };
            })]);
        k.Loft(rows, closed: true, twoSided: true);
    }

    /// <summary>A stole down his front over the cassock, gold, a cross near each end, its ends fringed.</summary>
    void Stole(Kit k, Fit f)
    {
        var gold = new Vector3(0.55f, 0.43f, 0.16f);
        foreach (float side in (float[])[-1, 1])
        {
            Weave(k, "wool", gold, 0.5f, 0.25f, 0.2f);
            var path = OverShoulder(f, side * 0.1f, 0.01f, 1.47f, 1.42f, 3).Skip(2).ToList();
            for (int i = 1; i <= 12; i++)
            {
                float t = i / 12f, y = 1.42f - (1.42f - 0.6f) * t;
                path.Add(Front(f.Body, y, side * (0.1f - 0.03f * MathF.Min(1, t * 3)), 0.01f + 0.004f * t));
            }
            Strap(k, path, 0.085f, 0.004f, f.Body.AxisZ);
            Weave(k, "wool", new Vector3(0.12f, 0.08f, 0.04f), 0.5f, 0.2f, 0.2f);
            var cross = Front(f.Body, 0.74f, side * 0.07f, 0.0165f);
            k.BoxAt(cross, new Vector3(0.007f, 0.04f, 0.002f));
            k.BoxAt(cross + new Vector3(0, 0.012f, 0), new Vector3(0.025f, 0.007f, 0.002f));
            Brass(k);
            for (int i = 0; i < 6; i++)
            {
                var top = Front(f.Body, 0.6f, side * 0.07f + (i - 2.5f) * 0.013f, 0.015f);
                k.Rod(top, top + new Vector3(0, -0.04f, 0), 0.0025f, 3);
            }
        }
    }

    /// <summary>A pectoral cross on a chain round his neck.</summary>
    void Cross(Kit k, Fit f)
    {
        Brass(k);
        var c = Front(f.Body, 1.22f, 0, 0.02f);
        k.BevelBox(c - new Vector3(0.011f, 0.05f, 0.005f), c + new Vector3(0.011f, 0.05f, 0.005f), 0.003f);
        k.BevelBox(c + new Vector3(-0.033f, 0.012f, -0.005f), c + new Vector3(0.033f, 0.032f, 0.005f), 0.003f);
        foreach (float side in (float[])[-1, 1])
        {
            var prev = c + new Vector3(0, 0.05f, 0);
            for (int i = 1; i <= 6; i++)
            {
                float t = i / 6f;
                var p = t < 0.8f ? Front(f.Body, 1.27f + 0.21f * t / 0.8f, side * 0.09f * t / 0.8f, 0.016f) : new Vector3(side * 0.09f, 1.53f, -0.05f + (t - 0.8f) * 0.4f);
                k.Cylinder(prev, p, 0.0025f, 4);
                prev = p;
            }
        }
    }

    /// <summary>A brass cross in his hand on the socket (its +Y out of the fist's thumb side).</summary>
    static void HandCross(Kit k)
    {
        Brass(k);
        k.BevelBox(new Vector3(-0.012f, -0.07f, -0.012f), new Vector3(0.012f, 0.23f, 0.012f), 0.004f);
        k.BevelBox(new Vector3(-0.012f, 0.138f, -0.06f), new Vector3(0.012f, 0.162f, 0.06f), 0.004f);
    }

    // ------------------------------------------------------------------ the butcher

    /// <summary>A knitted cap, close to the head, its cuff turned up.</summary>
    void Beanie(Kit k, Fit f)
    {
        Weave(k, "wool", new Vector3(0.4f, 0.4f, 0.38f), 0.7f, 0f, 0.12f);
        static float Band(float a) => 1.69f;
        var crown = Crown(f, Band, 0.013f, 1.835f, 0.2f, levels: 8);
        k.Loft(crown, closed: true, facing: Vector3.Zero);
        k.Cap(crown[^1], Vector3.UnitY);
        // The cuff: turned up, thick.
        Thick(k, [Crown(f, a => 1.685f, 0.026f, 1.69f, 1, levels: 1)[0], Crown(f, a => 1.735f, 0.024f, 1.74f, 1, levels: 1)[0]], 0.012f, closed: true);
    }

    /// <summary>A butcher's bib apron from the chest to the knees, lying on the belly and hanging from it, stained.</summary>
    void BibApron(Kit k, Fit f)
    {
        Weave(k, "wool", new Vector3(0.66f, 0.6f, 0.44f), 0.6f, 0.04f, 0.3f);
        // On the chest and the belly, and below the belly hung from it (as far out as the belly comes).
        Vector3 P(float x, float y) =>
            Across(a => (y >= 1.05f ? f.Body.Radius(y, a) : Hang(f.Body, 1.12f, y, a)) + 0.016f, f.Body.AxisZ, y, x);
        var rows = new List<Vector3[]>();
        foreach (var (y, half) in new[] { (1.42f, 0.12f), (1.3f, 0.13f), (1.15f, 0.17f), (1.02f, 0.22f), (0.86f, 0.235f), (0.7f, 0.24f), (0.52f, 0.245f) })
            rows.Add([.. Enumerable.Range(0, 9).Select(i => P(-half + i * 2 * half / 8, y))]);
        Thick(k, rows, 0.004f, from: new Vector3(0, 1.0f, f.Body.AxisZ));
        // The blood on it: dark smears, more low down where he wipes his hands.
        Colour(k, new Vector3(0.3f, 0.035f, 0.03f), 0.4f, 0.25f);
        var rng = new Random(77);
        for (int i = 0; i < 16; i++)
        {
            float y = 0.58f + (float)rng.NextDouble() * 0.78f, x = ((float)rng.NextDouble() - 0.5f) * 0.32f, r = 0.012f + (float)rng.NextDouble() * 0.03f;
            if (MathF.Abs(x) > (y > 1.1f ? 0.1f : 0.2f))
                continue;
            var at = P(x, y);
            var normal = Vector3.Normalize(new Vector3(at.X, 0, at.Z - f.Body.AxisZ));
            // An irregular blot, longer down (it ran).
            const int sides = 9;
            var ring = new Vector3[sides];
            for (int s = 0; s < sides; s++)
            {
                float a = s * MathF.Tau / sides, rr = r * (0.7f + 0.5f * (float)rng.NextDouble());
                ring[s] = P(x + MathF.Cos(a) * rr, y + MathF.Sin(a) * rr * (MathF.Sin(a) < 0 ? 1.6f : 1)) + normal * 0.0015f;
            }
            k.Cap(ring, normal);
        }
        // Its neck strap and the ties round the back.
        Weave(k, "wool", new Vector3(0.42f, 0.36f, 0.26f), 0.6f, 0.04f, 0.3f);
        foreach (float side in (float[])[-1, 1])
        {
            var neck = OverShoulder(f, side * 0.1f, 0.016f, 1.4f, 1.42f);
            neck.Add(P(side * 0.1f, 1.42f));
            Strap(k, neck, 0.022f, 0.003f, f.Body.AxisZ);
            Strap(k, [.. Bearings(side * MathF.PI * 0.68f, side * 0.05f, 8).Select(a => f.Body.At(0.99f, a, 0.012f))], 0.022f, 0.003f, f.Body.AxisZ);
        }
    }

    /// <summary>A butcher's cleaver-backed knife on the hand's socket: the handle in the fist, the blade out of the thumb side.</summary>
    static void Knife(Kit k)
    {
        Wood(k);
        k.BevelBox(new Vector3(-0.012f, -0.06f, -0.016f), new Vector3(0.012f, 0.06f, 0.016f), 0.005f);
        Brass(k);
        k.BevelBox(new Vector3(-0.014f, 0.055f, -0.024f), new Vector3(0.014f, 0.068f, 0.022f), 0.003f);
        Iron(k);
        k.Shade(1.5f);
        // The blade: thick at its back, ground to an edge, its point swept up.
        var spine = new[] { new Vector3(0, 0.068f, 0.016f), new Vector3(0, 0.2f, 0.016f), new Vector3(0, 0.285f, 0.006f) };
        var edge = new[] { new Vector3(0, 0.068f, -0.022f), new Vector3(0, 0.2f, -0.02f), new Vector3(0, 0.285f, 0.004f) };
        var back = spine.Select(p => p + new Vector3(0.0025f, 0, 0)).ToArray();
        var backL = spine.Select(p => p - new Vector3(0.0025f, 0, 0)).ToArray();
        k.Loft([back, edge, backL], twoSided: true);
        Colour(k, new Vector3(0.28f, 0.035f, 0.03f), 0.3f, 0.35f);
        k.Quad(new Vector3(0.0028f, 0.17f, 0.008f), new Vector3(0.0028f, 0.17f, -0.012f), new Vector3(0.0028f, 0.27f, 0.002f), new Vector3(0.0028f, 0.27f, 0.008f), twoSided: true);
    }

    // ------------------------------------------------------------------ the harbour master

    /// <summary>A peaked cap: navy, its crown wide and flat over a band, a black shining peak, a gold cord, a brass badge.</summary>
    void CaptainsCap(Kit k, Fit f)
    {
        Weave(k, "wool", new Vector3(0.08f, 0.09f, 0.15f), 0.5f, 0.06f, 0.2f);
        static float Band(float a) => 1.705f;
        var rows = new List<Vector3[]>();
        const int n = 32;
        foreach (var (y, more, tilt) in new[] { (1.705f, 0.014f, 0f), (1.75f, 0.016f, 0f), (1.785f, 0.05f, 0.012f), (1.8f, 0.058f, 0.016f), (1.808f, 0.05f, 0.017f) })
            rows.Add([.. Enumerable.Range(0, n).Select(j =>
            {
                float a = j * MathF.Tau / n, front = MathF.Max(0, -MathF.Cos(a));
                float r = f.Head.Radius(MathF.Min(y, 1.75f), a) * (y > 1.75f ? 1.0f : 1) + more + front * (y > 1.75f ? 0.02f : 0);
                return new Vector3(MathF.Sin(a) * r, y + tilt * front - tilt * 0.4f * MathF.Max(0, MathF.Cos(a)), f.Head.AxisZ + MathF.Cos(a) * r);
            })]);
        Thick(k, rows, 0.006f, closed: true);
        k.Cap(rows[^1], Vector3.UnitY);
        // The peak: black, shining, curved down.
        Colour(k, new Vector3(0.03f, 0.03f, 0.03f), 0.3f, 0.6f);
        var peak = new List<Vector3[]>();
        foreach (var (t, drop) in new[] { (0f, 0f), (0.5f, 0.01f), (1f, 0.03f) })
            peak.Add([.. Bearings(MathF.PI - 0.95f, MathF.PI + 0.95f, 13).Select(a =>
            {
                float r = f.Head.Radius(Band(a), a) + 0.014f + t * 0.062f * MathF.Pow(MathF.Max(0, -MathF.Cos(a)), 0.7f);
                return new Vector3(MathF.Sin(a) * r, Band(a) - drop, f.Head.AxisZ + MathF.Cos(a) * r);
            })]);
        Thick(k, peak, 0.005f, outward: Vector3.UnitY);
        // The band above the peak, black; the gold cord across it.
        Thick(k, [f.Head.Ring(1.707f, 0.017f, n), f.Head.Ring(1.738f, 0.017f, n)], 0.003f, closed: true);
        Brass(k);
        k.Cylinder(Front(f.Head, 1.715f, -0.075f, 0.022f), Front(f.Head, 1.715f, 0.075f, 0.022f), 0.0035f, 5);
        k.With(Matrix4x4.CreateTranslation(Front(f.Head, 1.755f, 0, 0.02f)), () =>
        {
            k.Lathe(new Vector3(0, -0.016f, 0), [new(0, 0), new(0.014f, 0.008f), new(0.016f, 0.016f), new(0.012f, 0.026f), new(0, 0.032f)], 8);
        });
    }

    /// <summary>A brass eyeglass in his right eye, its cord down to his coat.</summary>
    void Eyeglass(Kit k, Fit f)
    {
        var eye = Front(f.Head, EyeY, 0.042f, 0.004f);
        var normal = Vector3.Normalize(new Vector3(0.2f, 0, -1));
        Brass(k);
        k.Cylinder(eye - normal * 0.004f, eye + normal * 0.01f, 0.021f, 14);
        Glow(k, new Vector3(0.35f, 0.4f, 0.4f), 0.15f);
        k.Disc(eye + normal * 0.0105f, normal, 0.017f, 14);
        k.Emissive = 0;
        Colour(k, new Vector3(0.1f, 0.08f, 0.06f), 0.5f, 0.1f);
        var prev = eye + new Vector3(0, -0.02f, 0);
        for (int i = 1; i <= 6; i++)
        {
            float t = i / 6f;
            var p = Vector3.Lerp(eye + new Vector3(0.01f, -0.02f, 0), Front(f.Body, 1.36f, 0.1f, 0.012f), t) + new Vector3(0, 0, -0.03f * MathF.Sin(t * MathF.PI));
            k.Cylinder(prev, p, 0.0018f, 3);
            prev = p;
        }
    }

    /// <summary>His greatcoat's brass buttons, two rows down its double-breasted front, and its belt.</summary>
    void Buttons(Kit k, Fit f)
    {
        Brass(k);
        foreach (float x in (float[])[-0.075f, 0.075f])
            for (int i = 0; i < 4; i++)
            {
                float y = 1.36f - i * 0.115f;
                var at = Front(f.Body, y, x * (1 - 0.06f * i), 0.002f);
                var normal = Vector3.Normalize(new Vector3(at.X, 0, at.Z - f.Body.AxisZ));
                k.Cylinder(at, at + normal * 0.005f, 0.013f, 10);
                k.Disc(at + normal * 0.0052f, normal, 0.011f, 10);
            }
    }

    /// <summary>A brass spyglass, closed, on the hand's socket: its two draws and its leather grip.</summary>
    static void Spyglass(Kit k)
    {
        Brass(k);
        k.Cylinder(new Vector3(0, -0.07f, 0), new Vector3(0, 0.12f, 0), 0.028f, 14);
        k.Cylinder(new Vector3(0, 0.12f, 0), new Vector3(0, 0.2f, 0), 0.024f, 14);
        k.Cylinder(new Vector3(0, 0.2f, 0), new Vector3(0, 0.24f, 0), 0.02f, 14);
        foreach (float y in (float[])[-0.07f, 0.12f, 0.2f])
            k.Cylinder(new Vector3(0, y - 0.004f, 0), new Vector3(0, y + 0.004f, 0), 0.031f - (y > 0.15f ? 0.006f : y > 0 ? 0.002f : 0), 14);
        Colour(k, new Vector3(0.15f, 0.1f, 0.06f), 0.5f, 0.2f);
        k.Cylinder(new Vector3(0, -0.045f, 0), new Vector3(0, 0.06f, 0), 0.0305f, 14);
        Glow(k, new Vector3(0.3f, 0.35f, 0.35f), 0.1f);
        k.Disc(new Vector3(0, 0.2405f, 0), Vector3.UnitY, 0.016f, 10);
        k.Emissive = 0;
    }

    // ------------------------------------------------------------------ the drifter

    /// <summary>A hip flask on the hand's socket: pewter, curved, its cap.</summary>
    static void Flask(Kit k)
    {
        Iron(k);
        k.Shade(1.6f);
        var rows = new List<Vector3[]>();
        foreach (var (y, w) in new[] { (-0.06f, 0.03f), (-0.055f, 0.036f), (0.04f, 0.036f), (0.065f, 0.026f), (0.075f, 0.01f) })
            rows.Add([.. Enumerable.Range(0, 16).Select(i =>
            {
                float a = i * MathF.Tau / 16;
                return new Vector3(MathF.Cos(a) * 0.012f, y, MathF.Sin(a) * w);
            })]);
        k.Loft(rows, closed: true, facing: Vector3.Zero);
        k.Cap(rows[0], -Vector3.UnitY);
        k.Cylinder(new Vector3(0, 0.07f, 0), new Vector3(0, 0.092f, 0), 0.01f, 8);
    }

    // ------------------------------------------------------------------ the schoolteacher

    /// <summary>Her hair up in a bun at the back of her head, its pins.</summary>
    void Bun(Kit k, Fit f)
    {
        Weave(k, "fleece", new Vector3(0.22f, 0.13f, 0.08f), 0.5f, 0.06f, 0.08f);
        var c = Back(f.Head, 1.7f, 0, 0.035f);
        k.Lathe(c - new Vector3(0, 0.04f, 0.0f), [new(0, 0), new(0.035f, 0.01f), new(0.048f, 0.04f), new(0.038f, 0.07f), new(0.012f, 0.082f), new(0, 0.084f)], 14);
        Iron(k);
        k.Shade(0.5f);
        k.Cylinder(c + new Vector3(-0.06f, 0.01f, 0.01f), c + new Vector3(0.06f, 0.02f, 0.0f), 0.002f, 3);
    }

    /// <summary>Round wire spectacles on her nose, their arms back to her ears.</summary>
    void Spectacles(Kit k, Fit f)
    {
        Colour(k, new Vector3(0.12f, 0.11f, 0.1f), 0.4f, 0.4f);
        foreach (float side in (float[])[-1, 1])
        {
            var eye = Front(f.Head, EyeY - 0.003f, side * 0.042f, 0.016f);
            var normal = Vector3.Normalize(new Vector3(side * 0.18f, 0, -1));
            var right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, normal));
            const int n = 16;
            var ring = new Vector3[n];
            var ringB = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float a = i * MathF.Tau / n;
                ring[i] = eye + right * (MathF.Cos(a) * 0.023f) + Vector3.UnitY * (MathF.Sin(a) * 0.021f);
                ringB[i] = eye + right * (MathF.Cos(a) * 0.027f) + Vector3.UnitY * (MathF.Sin(a) * 0.025f);
            }
            // (Rims only: a lit grey disc for a lens hid her eyes, which the baked face now paints open; note 993.)
            k.Loft([ring, ringB], closed: true, twoSided: true);
            // The arm back along the side of the head to the ear.
            var hinge = eye + new Vector3(side * 0.027f, 0, 0);
            k.Cylinder(hinge, f.Head.At(EyeY, side * MathF.PI * 0.55f, 0.01f), 0.0018f, 3);
        }
        k.Cylinder(Front(f.Head, EyeY + 0.004f, -0.016f, 0.012f), Front(f.Head, EyeY + 0.004f, 0.016f, 0.012f), 0.002f, 3);
    }

    /// <summary>Three books held at the left fist by their spines, hanging at her side, a strap round them.</summary>
    static void Books(Kit k)
    {
        var colours = new[] { new Vector3(0.36f, 0.12f, 0.08f), new Vector3(0.12f, 0.2f, 0.12f), new Vector3(0.32f, 0.25f, 0.12f) };
        for (int i = 0; i < 3; i++)
        {
            Colour(k, colours[i], 0.5f, 0.1f);
            float x = -0.04f - i * 0.035f;
            k.BevelBox(new Vector3(x - 0.015f, -0.25f + i * 0.01f, -0.1f), new Vector3(x + 0.015f, 0.0f - i * 0.01f, 0.1f), 0.004f);
            Colour(k, new Vector3(0.75f, 0.7f, 0.6f), 0.5f, 0.05f);
            k.Box(new Vector3(x - 0.012f, -0.245f + i * 0.01f, -0.098f), new Vector3(x + 0.012f, -0.005f - i * 0.01f, 0.101f));
        }
        Colour(k, new Vector3(0.18f, 0.12f, 0.07f), 0.6f, 0.15f);
        k.Box(new Vector3(-0.15f, -0.14f, -0.012f), new Vector3(-0.015f, -0.12f, 0.012f));
    }
}
