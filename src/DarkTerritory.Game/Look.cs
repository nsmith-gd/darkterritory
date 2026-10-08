using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Ballast;
using Ballast.Render;

using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>A material as look.json writes it: its wear and shine, and the texture it wears, if any.</summary>
public sealed record MaterialTuning
{
    public float Wear { get; init; }
    public float Shine { get; init; }
    /// <summary>A texture from content/art/textures/index.json, or none for the shader's weathered flat colour.</summary>
    public string? Texture { get; init; }
    /// <summary>Metres to one repeat, overriding the texture's own <c>tileMetres</c>.</summary>
    public float? Tile { get; init; }
}

/// <summary>
/// The water (look.json <c>water</c>; ARCHITECTURE §8 note 424): each kind's current and how open it lies to the wind,
/// which its surface carries to the shader (PlanArt.WaterSheet), and how wide the lap along its waterline is.
/// </summary>
public sealed record WaterTuning
{
    /// <summary>By kind: sea, fundy, dyke, river, tidal, lake, marsh.</summary>
    public Dictionary<string, WaterKind> Kinds { get; init; } = new();
    public float LapM { get; init; } = 5;

    public WaterKind Of(string kind) => Kinds.TryGetValue(kind, out var k) ? k : new();
}

/// <summary>
/// A kind of water: its current (m/s), how open it lies to the wind (0 a still pool, 1 the open sea), and its murk (0
/// clear dark water, giving back only the sky; 1 silty, its own colour lit as well).
/// </summary>
public sealed record WaterKind
{
    public float Current { get; init; }
    public float Open { get; init; } = 0.4f;
    public float Murk { get; init; }
}

/// <summary>The night's light and air (GDD §28): the cold moon, the headlamp, the height fog.</summary>
public sealed record AtmosphereTuning
{
    public float FogHeightFalloff { get; init; }
    public float FogFloor { get; init; } = 1;
    public Vector3? FogColour { get; init; }
    public float FogCurve { get; init; } = 1;
    /// <summary>Towards the moon (normalised on use): low over the horizon, so it's in the sky you look at.</summary>
    public Vector3? MoonDirection { get; init; }
    public Vector3? MoonColour { get; init; }
    public float? MoonStrength { get; init; }
    public float? Ambient { get; init; }
    public Vector3? LampColour { get; init; }
    public float? LampIntensity { get; init; }
    /// <summary>What the night goes over to as the dawn comes up (the run's dawn clock, GDD §21).</summary>
    public DawnTuning? Dawn { get; init; }
    /// <summary>What the night's cold does to how things look: frost, and breath.</summary>
    public ColdTuning Cold { get; init; } = new();
    /// <summary>What the Choir's cold does to the night as it gathers (App. A.7's arrival beat): the frame chills.</summary>
    public ChoirColdTuning? ChoirCold { get; init; }
    /// <summary>What the night's wind does to the foliage: how hard it blows, from where, how gusty.</summary>
    public WindTuning Wind { get; init; } = new();
    /// <summary>The light indoors (queue #211, note 475): a room's own fill, the houses' candles and lamps, the hand lamp, the sheds' lanterns.</summary>
    public InteriorTuning Interiors { get; init; } = new();
}

/// <summary>
/// The light indoors (the director, 8 Oct: "it feels almost impossible to see anything ... we do need interior lighting to
/// be functional"; GDD §28 "inside = warm, human, temporary safety"; ARCHITECTURE §8 note 475). Inside a Room (a car, an open
/// house, an open barn or shed, a yard's shed) the moon and sky are kept out; what's left is the room's own fill, which
/// was the night's ambient (0.1) times a warm tint, and the practical lights. The defaults are those old numbers.
/// </summary>
public sealed record InteriorTuning
{
    /// <summary>A room's fill (rgb), lamplight off the boards: what a corner no lamp reaches is lit by. Null: the old, the night's ambient times (0.55, 0.42, 0.3).</summary>
    public Vector3? Fill { get; init; }
    /// <summary>An open house's candle stub: its strength (times the lamp amber) and reach (m).</summary>
    public float Candle { get; init; } = 0.9f;
    public float CandleRange { get; init; } = 5.5f;
    /// <summary>An open house's oil lamp turned down: its strength and reach (m).</summary>
    public float Lamp { get; init; } = 0.8f;
    public float LampRange { get; init; } = 6.5f;
    /// <summary>A crewmate's hand lamp: its strength and reach (m).</summary>
    public float HandLamp { get; init; } = 1.8f;
    public float HandLampRange { get; init; } = 7f;
    /// <summary>A hurricane lantern turned low, hung in each open barn or shed and each roofed length of a yard shed: its strength (0: none), reach (m), and how high it hangs over the floor (m; under the eaves at the most).</summary>
    public float ShedLantern { get; init; }
    public float ShedLanternRange { get; init; } = 9f;
    public float ShedLanternHeight { get; init; } = 3.2f;
    /// <summary>A long yard shed has one each this far along it (m).</summary>
    public float ShedLanternSpacing { get; init; } = 10f;
}

/// <summary>
/// The wind (the checklist's "wind": smoke, steam, trees and grass moving with it): read off the route's weather wind
/// (0..1), a calm's breeze to a gale, from one quarter, rolling through in gusts. The foliage's cards and boughs bend
/// with it in the scene shader.
/// </summary>
public sealed record WindTuning
{
    public float Calm { get; init; } = 2;
    public float Full { get; init; } = 16;
    public Vector3 From { get; init; } = new(-0.7f, 0, -0.4f);
    public float Gusts { get; init; } = 0.7f;

    /// <summary>The wind at <paramref name="wind"/> (0..1): m/s, world axes, blowing away from <see cref="From"/>.</summary>
    public Vector3 Of(double wind) => -Vector3.Normalize(From) * (Calm + (Full - Calm) * Math.Clamp((float)wind, 0, 1));
}

/// <summary>
/// The cold (GDD §26: "frost on windows and metal, breath vapour"): read off the route's weather cold (0..1), so a deep
/// tier's night is rimed and every mouth smokes, a local one's isn't.
/// </summary>
public sealed record ColdTuning
{
    public float FrostFrom { get; init; } = 0.35f;
    public float FrostFull { get; init; } = 0.85f;
    public float BreathFrom { get; init; } = 0.15f;
    /// <summary>How far in from its frame the frost on the cab's glass reaches at its full (m, note 485).</summary>
    public float PaneReach { get; init; } = 0.16f;
    /// <summary>The frost on the glass: its colour (as the effects are, unlit) and how thick at the frame (0..1).</summary>
    public Vector3 PaneColour { get; init; } = new(0.5f, 0.57f, 0.64f);
    public float PaneDensity { get; init; } = 0.9f;
    /// <summary>How near the glass a mouth fogs it (m), and the fog's colour and thickness at the glass (0..1).</summary>
    public float FogReach { get; init; } = 0.7f;
    public Vector3 FogColour { get; init; } = new(0.42f, 0.45f, 0.48f);
    public float FogDensity { get; init; } = 0.8f;

    /// <summary>How heavy the frost is at <paramref name="cold"/>, 0..1.</summary>
    public float Frost(double cold) => Math.Clamp(((float)cold - FrostFrom) / Math.Max(1e-3f, FrostFull - FrostFrom), 0, 1);

    /// <summary>How much a breath shows at <paramref name="cold"/>, 0..1 (all of it by the frost's full).</summary>
    public float Breath(double cold) => Math.Clamp(((float)cold - BreathFrom) / Math.Max(1e-3f, FrostFull - BreathFrom), 0, 1);
}

/// <summary>
/// The dawn (GDD §21: the run is a race to it; the checklist's "the sky doesn't change"): over the last
/// <see cref="LeadSeconds"/> of the run's dawn clock the night's air and moon go over to these, a low grey light coming up
/// in the east that thins the dark without taking the fog away.
/// </summary>
public sealed record DawnTuning
{
    public float LeadSeconds { get; init; } = 300;
    public Vector3 FogColour { get; init; } = new(0.3f, 0.28f, 0.3f);
    /// <summary>Towards the sun, just over the horizon (normalised on use).</summary>
    public Vector3 SunDirection { get; init; } = new(0.85f, 0.12f, -0.3f);
    public Vector3 SunColour { get; init; } = new(0.95f, 0.68f, 0.52f);
    public float SunStrength { get; init; } = 0.9f;
    public float Ambient { get; init; } = 0.3f;
    /// <summary>The glow low on the sky on the sun's side as it comes up (the sky shader's dawn band).</summary>
    public Vector3 HorizonGlow { get; init; } = new(0.28f, 0.16f, 0.11f);
}

/// <summary>
/// The Choir's cold coming before it (GDD v1.2 App. A.7; the Look Review's "the frost reads only in the crop"): as it
/// gathers, the night's air goes over to a paler, colder blue and the moon's light with it, and a rime of frost comes on
/// the metal, the roofs and the glass, whatever the night's own cold, so the whole frame says it's coming, not just the
/// glitter in the air.
/// </summary>
public sealed record ChoirColdTuning
{
    public Vector3 FogColour { get; init; } = new(0.1f, 0.13f, 0.17f);
    public Vector3 MoonColour { get; init; } = new(0.55f, 0.68f, 0.95f);
    /// <summary>How much rime it brings on, at its full (0..1, the same frost the night's cold does).</summary>
    public float Rime { get; init; } = 0.8f;
}

/// <summary>
/// A car eaten from its rear end by a Car Hugger (GDD v1.2 App. A.3 FEED; Art/BiteKit, Shaders/bite.glsl), read off how
/// much of what was left of it has been eaten: <see cref="DarkTerritory.Sim.Train.Vehicle.Eaten"/> over eaten plus
/// integrity, so it's eaten through, the whole way, just as the sim drops it.
/// </summary>
public sealed record BiteTuning
{
    /// <summary>How far down the car's body it's eaten by the end, as a fraction of its length.</summary>
    public float Depth { get; init; } = 0.45f;
    /// <summary>How far its head pushes in through the end as it eats (m): past that it's reaching in and tearing.</summary>
    public float Advance { get; init; } = 1.4f;
    /// <summary>The side walls, which its hands hold, go this far ahead of its head at most (m).</summary>
    public float SideLead { get; init; } = 1.0f;
    /// <summary>Below this height (m over the rail) nothing is eaten: the underframe and the trucks, so it rolls on.</summary>
    public float Floor { get; init; } = 0.95f;
    /// <summary>The first bites take the rear platform, over this fraction of the eating.</summary>
    public float Platform { get; init; } = 0.08f;

    /// <summary>0 whole to 1 eaten through.</summary>
    public static float Fraction(double eaten, double integrity) => eaten <= 0 ? 0 : (float)Math.Clamp(eaten / Math.Max(1e-6, eaten + integrity), 0, 1);
}

/// <summary>Mirror of content/tuning/look.json: the art pass's surfaces, the grade and the post stack (GDD §25-28).</summary>
/// <summary>
/// The consist's wear and tear (pipeline plan, consist kit: "3 damage states per car; scars persist between runs as
/// decal and mask layers"), read off each car's integrity.
/// </summary>
public sealed record DamageTuning
{
    /// <summary>Integrity below the first is damaged (plate torn and bent, claw gouges), below the second wrecked (breached).</summary>
    public float[] States { get; init; } = [0.66f, 0.33f];
    /// <summary>The scar mask starts below this integrity...</summary>
    public float ScarsFrom { get; init; } = 0.95f;
    /// <summary>...and is at its worst by this one.</summary>
    public float ScarsFull { get; init; } = 0.1f;

    /// <summary>0 whole, 1 damaged, 2 wrecked.</summary>
    public int StateOf(double integrity) => integrity < States[1] ? 2 : integrity < States[0] ? 1 : 0;

    /// <summary>How much of the scar mask shows, 0..1.</summary>
    public float ScarOf(double integrity) => Math.Clamp((ScarsFrom - (float)integrity) / Math.Max(1e-3f, ScarsFrom - ScarsFull), 0, 1);
}

public sealed record LookTuning
{
    public const string File = "tuning/look.json";
    public float TexelsPerMetre { get; init; } = 128;
    /// <summary>Every material layer's size in the GPU array (the library is authored at 512: ARCHITECTURE §8 note 57).</summary>
    public int LayerSize { get; init; } = 512;
    /// <summary>The characters' and creatures' baked atlases (authored bigger than <see cref="LayerSize"/>) keep up to this.</summary>
    public int HeroLayerSize { get; init; } = 1024;
    /// <summary>The largest creatures' atlases (authored over <see cref="HeroLayerSize"/>) keep up to this, in arrays of
    /// their own (look.json bigHeroLayerSize; GDD §27's density on a monster spread over a car).</summary>
    public int BigHeroLayerSize { get; init; } = 2048;
    /// <summary>Past this far from the eye (m) a creature with a distance copy (content/art/models/&lt;name&gt;.lod1.glb) draws
    /// that instead (look.json creatureLodMetres; 0: never).</summary>
    public float CreatureLodMetres { get; init; }
    public float Baked { get; init; } = 0.35f;
    /// <summary>The crew's paint by player id, in turn (the flying cap and the scarf: CreatureArt.Crewmate), as multipliers.</summary>
    public float[][] CrewColours { get; init; } = [[1, 1, 1]];
    /// <summary>Each of <see cref="CrewColours"/> by name, for the settings' OUTFIT (note 298).</summary>
    public string[] CrewColourNames { get; init; } = [];

    /// <summary>A crewmate's paint colour.</summary>
    public System.Numerics.Vector3 CrewColour(int id)
    {
        var c = CrewColours.Length > 0 ? CrewColours[(id % CrewColours.Length + CrewColours.Length) % CrewColours.Length] : [1, 1, 1];
        return new System.Numerics.Vector3(c[0], c[1], c[2]);
    }

    /// <summary>By <see cref="Palette"/> colour name.</summary>
    public Dictionary<string, MaterialTuning> Materials { get; init; } = new();
    public ColourGrade Grade { get; init; } = new();
    public PostSettings Post { get; init; } = new();
    public AtmosphereTuning Atmosphere { get; init; } = new();
    public WaterTuning Water { get; init; } = new();
    public DamageTuning Damage { get; init; } = new();
    public BiteTuning Bite { get; init; } = new();
}

/// <summary>One entry of content/art/textures/index.json (written by tools/art/textures.py).</summary>
public sealed record TextureEntry
{
    public string Name { get; init; } = "";
    public string Diffuse { get; init; } = "";
    public string? Spec { get; init; }
    /// <summary>The tangent-space normal map, where the texture has relief (index.json "normal").</summary>
    public string? Normal { get; init; }
    /// <summary>Metres to one repeat; none for cards, atlases and decals (they're mapped whole, 1 m to the texture).</summary>
    public float? TileMetres { get; init; }
    public string Family { get; init; } = "";
    public bool AlphaTest { get; init; }
}

/// <summary>
/// The look as the renderer takes it: each <see cref="Palette"/> colour named in <see cref="LookTuning.Materials"/> is
/// a material, and any other colour takes the nearest one's (a tinted wall, a lamp dimmed to a glow), so every
/// surface in the scene is something from §27's short list without the scene having to say so. With textures, each
/// material wears one, tinted by how far the colour asked for is from its palette colour.
/// </summary>
public sealed class Look
{
    readonly (Vector3 Colour, SurfaceMaterial Material)[] _palette;
    readonly Dictionary<Vector3, SurfaceMaterial> _seen = new();
    readonly Dictionary<string, (int Layer, TextureEntry Entry)> _textures = new();

    /// <param name="textures">The textures available, in layer order (index.json's), or none for the untextured look.</param>
    public Look(LookTuning tuning, IReadOnlyList<TextureEntry>? textures = null)
    {
        Tuning = tuning;
        Textures = textures ?? [];
        for (int i = 0; i < Textures.Count; i++)
            _textures[Textures[i].Name] = (i, Textures[i]);
        var named = PaletteColours();
        var unknown = tuning.Materials.Keys.Where(k => !named.ContainsKey(k)).ToList();
        if (unknown.Count > 0)
            throw new InvalidDataException($"{LookTuning.File}: no palette colour called {string.Join(", ", unknown)}");
        _palette = [.. tuning.Materials.Select(m => (named[m.Key], ToSurface(m.Value, named[m.Key])))];
        Style = new SurfaceStyle(tuning.TexelsPerMetre, tuning.Baked, Material);
    }

    public LookTuning Tuning { get; }
    public IReadOnlyList<TextureEntry> Textures { get; }
    public SurfaceStyle Style { get; }
    /// <summary>The folder the textures were read from, when they were.</summary>
    public string? TextureRoot { get; init; }
    /// <summary>
    /// The one doorway (train.json <c>doorway</c>, ARCHITECTURE §8 note 110): the train's doors are built to it, and every
    /// door the art draws, on the train or off it, is this tall (<see cref="Art.Kit.Doorway"/>).
    /// </summary>
    public DoorwayTuning Doorway { get; init; } = new();
    /// <summary>A headset crewmate's body (vr.json <c>body</c>, T82): everyone draws it, headset or not.</summary>
    public VrBodyTuning VrBody { get; init; } = new();
    /// <summary>
    /// The stops' walls as the sim stands them (run.json <c>walls</c>, note 279): the art draws a shed's, the hero's and a
    /// Holdout's shell from <see cref="Sim.Run.StopWalls.Shell"/> with these, so its doors are where you walk in (note 387).
    /// </summary>
    public Sim.Run.WallTuning Walls { get; init; } = new();

    static Dictionary<string, Vector3> PaletteColours() =>
        typeof(Palette).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.FieldType == typeof(Vector3))
            .ToDictionary(f => f.Name, f => (Vector3)f.GetValue(null)!);

    SurfaceMaterial ToSurface(MaterialTuning m, Vector3 basis)
    {
        if (m.Texture is null || !_textures.TryGetValue(m.Texture, out var t))
            return new SurfaceMaterial(m.Wear, m.Shine);
        return new SurfaceMaterial(m.Wear, m.Shine, t.Layer, m.Tile ?? t.Entry.TileMetres ?? 1, basis);
    }

    /// <summary>The look from content: look.json, and the textures if they've been built (tools/art/textures.py).</summary>
    public static Look Load(string content)
    {
        var tuning = DataFile.Load<LookTuning>(Path.Combine(content, LookTuning.File));
        string trainFile = Path.Combine(content, TrainTuning.File);
        var doorway = System.IO.File.Exists(trainFile) ? DataFile.Load<TrainTuning>(trainFile).Geometry.Doorway : new DoorwayTuning();
        string vrFile = Path.Combine(content, VrTuning.File);
        var body = System.IO.File.Exists(vrFile) ? DataFile.Load<VrTuning>(vrFile).Body : new VrBodyTuning();
        string runFile = Path.Combine(content, Sim.Run.RunTuning.File);
        var walls = System.IO.File.Exists(runFile) ? DataFile.Load<Sim.Run.RunTuning>(runFile).Walls : new Sim.Run.WallTuning();
        string root = Path.Combine(content, "art", "textures");
        string index = Path.Combine(root, "index.json");
        if (!System.IO.File.Exists(index))
            return new Look(tuning) { Doorway = doorway, VrBody = body, Walls = walls };
        var entries = JsonSerializer.Deserialize<List<TextureEntry>>(System.IO.File.ReadAllText(index), DataFile.Options) ?? [];
        // The sourced props' own layers (tools/models writes them beside the library, index.models.json), after it.
        string models = Path.Combine(root, "index.models.json");
        if (System.IO.File.Exists(models))
            entries.AddRange(JsonSerializer.Deserialize<List<TextureEntry>>(System.IO.File.ReadAllText(models), DataFile.Options) ?? []);
        // A texture named in the index but not on disk is skipped (and so is its material's texture): the look degrades
        // to flat colour rather than failing to start.
        entries = [.. entries.Where(e => System.IO.File.Exists(Path.Combine(root, e.Diffuse)))];
        return new Look(tuning, entries) { TextureRoot = root, Doorway = doorway, VrBody = body, Walls = walls };
    }

    /// <summary>The texture layer called <paramref name="name"/>, or −1 when it isn't there.</summary>
    public int Layer(string name) => _textures.TryGetValue(name, out var t) ? t.Layer : -1;

    /// <summary>A material wearing the named texture directly (the kit's pieces ask for theirs by name).</summary>
    public SurfaceMaterial Surface(string texture, float wear = 0.6f, float? tile = null)
    {
        if (!_textures.TryGetValue(texture, out var t))
            return new SurfaceMaterial(wear, 0);
        return new SurfaceMaterial(wear, 0, t.Layer, tile ?? t.Entry.TileMetres ?? 1, Vector3.One);
    }

    RenderAssets? _assets;
    Art.SceneArt? _art;

    /// <summary>The kit's pieces as the scene places them (cooked on first use, kept).</summary>
    public Art.SceneArt Art => _art ??= new Art.SceneArt(this);

    /// <summary>Loads the look's textures, backdrop, grade and post settings into a renderer.</summary>
    public void Dress(GreyboxRenderer renderer) => renderer.Load(Sky is { } sky ? (_assets ??= Assets()) with { Backdrop = sky } : _assets ??= Assets());

    /// <summary>A night's own far horizon in place of the look's band (a generated line's, <see cref="Art.PlanSky"/>); null for the look's.</summary>
    public Image? Sky { get; set; }

    /// <summary>Everything the renderer needs: the material maps, the backdrop, the grade and the post settings.</summary>
    public RenderAssets Assets()
    {
        var layers = new List<MaterialLayer>();
        Image? backdrop = null;
        if (TextureRoot is not null)
            foreach (var t in Textures)
            {
                // A texture that won't read (half-written, corrupt) becomes flat grey, keeping every layer where the
                // materials expect it, rather than stopping the game.
                static Image Read(string path, Image fallback)
                {
                    try { return ImageFile.Load(path); }
                    catch (Exception e) when (e is InvalidOperationException or IOException)
                    {
                        Console.Error.WriteLine($"look: {Path.GetFileName(path)} unreadable ({e.Message}); drawing it flat");
                        return fallback;
                    }
                }
                var diffuse = Read(Path.Combine(TextureRoot, t.Diffuse), Image.Solid(4, 80, 80, 80));
                var spec = t.Spec is { } s && System.IO.File.Exists(Path.Combine(TextureRoot, s)) ? Read(Path.Combine(TextureRoot, s), Image.Solid(4, 20, 60, 0)) : Image.Solid(4, 20, 60, 0);
                var normal = t.Normal is { } n && System.IO.File.Exists(Path.Combine(TextureRoot, n)) ? Read(Path.Combine(TextureRoot, n), Image.Solid(4, 128, 128, 255)) : null;
                layers.Add(new MaterialLayer(t.Name, diffuse, spec, t.AlphaTest, normal));
                if (t.Family == "sky")
                    backdrop = diffuse;
            }
        return new RenderAssets { LayerSize = Tuning.LayerSize, HeroSize = Tuning.HeroLayerSize, BigHeroSize = Tuning.BigHeroLayerSize, Layers = layers, Backdrop = backdrop, Lut = Tuning.Grade.Bake(), Post = Tuning.Post };
    }

    /// <summary>The night's lighting with the look's atmosphere over it.</summary>
    public FrameLighting Apply(FrameLighting light)
    {
        var a = Tuning.Atmosphere;
        light.FogHeightFalloff = a.FogHeightFalloff;
        light.FogFloor = a.FogFloor;
        light.FogCurve = a.FogCurve;
        if (a.FogColour is { } fc)
            light.FogColor = fc;
        if (a.MoonDirection is { } md)
            light.MoonDirection = Vector3.Normalize(md);
        if (a.MoonColour is { } mc)
            light.MoonColour = mc;
        if (a.MoonStrength is { } ms)
            light.MoonStrength = ms;
        if (a.Ambient is { } am)
            light.Ambient = am;
        if (a.LampColour is { } lc)
            light.LampColour = lc;
        if (a.LampIntensity is { } li)
            light.LampIntensity = li;
        if (a.Interiors.Fill is { } fill)
            light.IndoorFill = fill;
        return light;
    }

    /// <summary>How far the dawn's come up (0 night .. 1 dawn) <paramref name="dawnIn"/> seconds before it (the run's dawn clock).</summary>
    public float DawnOf(double dawnIn) =>
        Tuning.Atmosphere.Dawn is { } d ? (float)Math.Clamp(1 - dawnIn / Math.Max(1, d.LeadSeconds), 0, 1) : 0;

    /// <summary>
    /// <paramref name="light"/> with the dawn <paramref name="t"/> of the way up (0..1, eased): the fog lightening to the
    /// dawn's grey, the moon's light going over to the low sun's from the east, the dark pockets filling.
    /// </summary>
    public FrameLighting Dawn(FrameLighting light, float t)
    {
        if (Tuning.Atmosphere.Dawn is not { } d || t <= 0)
            return light;
        t = Math.Clamp(t, 0, 1);
        t = t * t * (3 - 2 * t);
        light.FogColor = Vector3.Lerp(light.FogColor, d.FogColour, t);
        light.MoonDirection = Vector3.Normalize(Vector3.Lerp(light.MoonDirection, Vector3.Normalize(d.SunDirection), t));
        light.MoonColour = Vector3.Lerp(light.MoonColour, d.SunColour, t);
        light.MoonStrength = float.Lerp(light.MoonStrength, d.SunStrength, t);
        light.Ambient = float.Lerp(light.Ambient, d.Ambient, t);
        light.Dawn = t;
        light.DawnGlow = d.HorizonGlow;
        return light;
    }

    /// <summary>
    /// <paramref name="light"/> with the Choir's cold <paramref name="t"/> of the way on (0..1, GreyboxScene.ChoirCold): the
    /// fog and the moon going over to its cold blue, its rime on whatever frost the night already has. Applied after the
    /// route's weather, so the rime adds to the night's own.
    /// </summary>
    public FrameLighting Chill(FrameLighting light, float t)
    {
        if (Tuning.Atmosphere.ChoirCold is not { } c || t <= 0)
            return light;
        t = Math.Clamp(t, 0, 1);
        light.FogColor = Vector3.Lerp(light.FogColor, c.FogColour, t);
        light.MoonColour = Vector3.Lerp(light.MoonColour, c.MoonColour, t);
        light.Frost = MathF.Max(light.Frost, c.Rime * t);
        return light;
    }

    /// <summary>The material for a colour: its own, or the nearest named colour's (by eye, in the linear palette).</summary>
    public SurfaceMaterial Material(Vector3 colour)
    {
        if (_seen.TryGetValue(colour, out var known))
            return known;
        var best = new SurfaceMaterial(0, 0);
        float bestD = float.MaxValue;
        float bright = MathF.Max(1e-5f, colour.X + colour.Y + colour.Z);
        foreach (var (c, m) in _palette)
        {
            // By hue first and brightness only a little: a lamp dimmed to a glow is still a lamp, rust in shadow is
            // still rust. Brightness settles the greys, which share a hue.
            float cb = MathF.Max(1e-5f, c.X + c.Y + c.Z);
            float log = MathF.Log(bright / cb);
            float d = Vector3.DistanceSquared(c / cb, colour / bright) * 4 + log * log * 0.01f;
            if (d < bestD)
                (bestD, best) = (d, m);
        }
        if (_seen.Count < 4096)
            _seen[colour] = best;
        return best;
    }
}
