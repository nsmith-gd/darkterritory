using System.Numerics;
using System.Reflection;
using Ballast;
using Ballast.Render;

namespace DarkTerritory.Game;

/// <summary>Mirror of content/tuning/look.json: the art pass's surface treatment (T39, GDD §25-28).</summary>
public sealed record LookTuning
{
    public const string File = "tuning/look.json";
    public float TexelsPerMetre { get; init; } = 128;
    public float Baked { get; init; } = 0.35f;
    /// <summary>By <see cref="Palette"/> colour name.</summary>
    public Dictionary<string, SurfaceMaterial> Materials { get; init; } = new();
}

/// <summary>
/// The look as the renderer takes it: each <see cref="Palette"/> colour named in <see cref="LookTuning.Materials"/> is
/// a material, and any other colour takes the nearest one's (a tinted wall, a lamp dimmed in a Vigil), so every
/// surface in the scene is something from §27's short list without the scene having to say so.
/// </summary>
public sealed class Look
{
    readonly (Vector3 Colour, SurfaceMaterial Material)[] _palette;
    readonly Dictionary<Vector3, SurfaceMaterial> _seen = new();

    public Look(LookTuning tuning)
    {
        Tuning = tuning;
        var named = typeof(Palette).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.FieldType == typeof(Vector3))
            .ToDictionary(f => f.Name, f => (Vector3)f.GetValue(null)!);
        var unknown = tuning.Materials.Keys.Where(k => !named.ContainsKey(k)).ToList();
        if (unknown.Count > 0)
            throw new InvalidDataException($"{LookTuning.File}: no palette colour called {string.Join(", ", unknown)}");
        _palette = [.. tuning.Materials.Select(m => (named[m.Key], m.Value))];
        Style = new SurfaceStyle(tuning.TexelsPerMetre, tuning.Baked, Material);
    }

    public LookTuning Tuning { get; }
    public SurfaceStyle Style { get; }

    public static Look Load(string content) => new(DataFile.Load<LookTuning>(Path.Combine(content, LookTuning.File)));

    /// <summary>The material for a colour: its own, or the nearest named colour's (by eye, in the linear palette).</summary>
    public SurfaceMaterial Material(Vector3 colour)
    {
        if (_seen.TryGetValue(colour, out var known))
            return known;
        var best = default(SurfaceMaterial);
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
