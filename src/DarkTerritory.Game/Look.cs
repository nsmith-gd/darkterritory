using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Ballast;
using Ballast.Render;

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
}

/// <summary>Mirror of content/tuning/look.json: the art pass's surfaces, the grade and the post stack (GDD §25-28).</summary>
public sealed record LookTuning
{
    public const string File = "tuning/look.json";
    public float TexelsPerMetre { get; init; } = 128;
    public float Baked { get; init; } = 0.35f;
    /// <summary>By <see cref="Palette"/> colour name.</summary>
    public Dictionary<string, MaterialTuning> Materials { get; init; } = new();
    public ColourGrade Grade { get; init; } = new();
    public PostSettings Post { get; init; } = new();
    public AtmosphereTuning Atmosphere { get; init; } = new();
}

/// <summary>One entry of content/art/textures/index.json (written by tools/art/textures.py).</summary>
public sealed record TextureEntry
{
    public string Name { get; init; } = "";
    public string Diffuse { get; init; } = "";
    public string? Spec { get; init; }
    /// <summary>Metres to one repeat; none for cards, atlases and decals (they're mapped whole, 1 m to the texture).</summary>
    public float? TileMetres { get; init; }
    public string Family { get; init; } = "";
    public bool AlphaTest { get; init; }
}

/// <summary>
/// The look as the renderer takes it: each <see cref="Palette"/> colour named in <see cref="LookTuning.Materials"/> is
/// a material, and any other colour takes the nearest one's (a tinted wall, a lamp dimmed in a Vigil), so every
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
        string root = Path.Combine(content, "art", "textures");
        string index = Path.Combine(root, "index.json");
        if (!System.IO.File.Exists(index))
            return new Look(tuning);
        var entries = JsonSerializer.Deserialize<List<TextureEntry>>(System.IO.File.ReadAllText(index), DataFile.Options) ?? [];
        // A texture named in the index but not on disk is skipped (and so is its material's texture): the look degrades
        // to flat colour rather than failing to start.
        entries = [.. entries.Where(e => System.IO.File.Exists(Path.Combine(root, e.Diffuse)))];
        return new Look(tuning, entries) { TextureRoot = root };
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
                layers.Add(new MaterialLayer(t.Name, diffuse, spec, t.AlphaTest));
                if (t.Family == "sky")
                    backdrop = diffuse;
            }
        return new RenderAssets { Layers = layers, Backdrop = backdrop, Lut = Tuning.Grade.Bake(), Post = Tuning.Post };
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
