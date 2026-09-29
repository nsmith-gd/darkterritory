using System.Numerics;

namespace Ballast.Render;

/// <summary>An RGBA8 image, top row first.</summary>
public sealed record Image(int Width, int Height, byte[] Rgba)
{
    public static Image Solid(int size, byte r, byte g, byte b, byte a = 255)
    {
        var px = new byte[size * size * 4];
        for (int i = 0; i < px.Length; i += 4)
            (px[i], px[i + 1], px[i + 2], px[i + 3]) = (r, g, b, a);
        return new Image(size, size, px);
    }

    /// <summary>
    /// This image at another size: box-filtered down (the pipeline's rule, "box rather than Lanczos to keep grain
    /// crunchy"), nearest up (a 128 px texture in a 256 px layer keeps its big texels).
    /// </summary>
    public Image Resized(int width, int height)
    {
        if (width == Width && height == Height)
            return this;
        var px = new byte[width * height * 4];
        Span<int> sum = stackalloc int[4];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int x0 = x * Width / width, x1 = Math.Max(x0 + 1, (x + 1) * Width / width);
                int y0 = y * Height / height, y1 = Math.Max(y0 + 1, (y + 1) * Height / height);
                int n = 0;
                sum.Clear();
                for (int sy = y0; sy < y1; sy++)
                    for (int sx = x0; sx < x1; sx++, n++)
                        for (int c = 0; c < 4; c++)
                            sum[c] += Rgba[(sy * Width + sx) * 4 + c];
                for (int c = 0; c < 4; c++)
                    px[(y * width + x) * 4 + c] = (byte)((sum[c] + n / 2) / n);
            }
        return new Image(width, height, px);
    }
}

/// <summary>One material's maps (GDD §27, pipeline "at most three maps"): diffuse (alpha = cutout) and spec.</summary>
/// <param name="Spec">R: specular strength. G: gloss (Phong exponent 4..128). B: emissive mask.</param>
public sealed record MaterialLayer(string Name, Image Diffuse, Image Spec, bool AlphaTest = false);

/// <summary>The post stack's settings (pipeline "Lighting, VFX and post"): what 2006–2008 hardware shipped with.</summary>
public sealed record PostSettings
{
    /// <summary>Scene luminance above which things bloom.</summary>
    public float BloomThreshold { get; init; } = 0.7f;
    public float BloomStrength { get; init; } = 0.6f;
    public float Vignette { get; init; } = 0.35f;
    public float Grain { get; init; } = 0.035f;
    /// <summary>Colour levels per channel after the ordered dither: 48 bands a little, as a 2006 framebuffer did.</summary>
    public float ColourLevels { get; init; } = 48;
    /// <summary>The debug "PS2" look for art-direction comparison: no spec maps, harder banding, no bloom.</summary>
    public bool Ps2 { get; init; }
    /// <summary>How much of the shader's own grime still shows over a textured surface (breaks up the tiling).</summary>
    public float TexturedWear { get; init; } = 0.5f;
    /// <summary>How far the sky's backdrop silhouettes sink into the fog (0: stark, 1: gone).</summary>
    public float BackdropFog { get; init; } = 0.55f;
    /// <summary>How many degrees of elevation the backdrop band spans, top to bottom (its horizon is 85 % of the way down).</summary>
    public float BackdropDegrees { get; init; } = 30;
    /// <summary>How much brighter the sky's horizon haze is than the fog (so far is paler than near, not darker).</summary>
    public float HorizonGlow { get; init; } = 1.3f;
    public Vector3 SkyZenith { get; init; } = new(0.018f, 0.02f, 0.028f);
    /// <summary>Mip bias: a little positive, so distant surfaces shimmer (the pipeline's "intended pixel crawl").</summary>
    public float MipBias { get; init; } = 0.4f;
}

/// <summary>
/// What the renderer draws surfaces with: the material array (one size, every layer), the sky's backdrop band and the
/// colour grade. The game fills it from content; without it the renderer draws the flat greybox as it always has.
/// </summary>
public sealed record RenderAssets
{
    /// <summary>Every layer is resized to this (power of two).</summary>
    public int LayerSize { get; init; } = 256;
    public IReadOnlyList<MaterialLayer> Layers { get; init; } = [];
    /// <summary>A 360° band of distant silhouettes (alpha = silhouette), horizon 85 % of the way down, 90° tall.</summary>
    public Image? Backdrop { get; init; }
    /// <summary>A 16³ colour grade, display to display (<see cref="ColourGrade.Bake"/>).</summary>
    public byte[]? Lut { get; init; }
    public PostSettings Post { get; init; } = new();

    public int IndexOf(string name)
    {
        for (int i = 0; i < Layers.Count; i++)
            if (Layers[i].Name == name)
                return i;
        return -1;
    }
}

/// <summary>
/// The colour script as a grade (pipeline: "colour grade via 16³ LUT"): cold, crushed shadows, warm highlights kept for
/// practical light, and the whole thing desaturated toward the sheet's palette.
/// </summary>
public sealed record ColourGrade
{
    public const int Size = 16;
    public Vector3 Lift { get; init; } = new(0.0f, 0.005f, 0.015f);
    public Vector3 Gamma { get; init; } = new(1.0f, 1.0f, 1.0f);
    public Vector3 Gain { get; init; } = new(1.0f, 0.98f, 0.95f);
    public float Saturation { get; init; } = 0.8f;
    public float Contrast { get; init; } = 1.08f;
    /// <summary>Tint pushed into the shadows (a cold blue-grey night) and the highlights (lamp amber), by luminance.</summary>
    public Vector3 ShadowTint { get; init; } = new(0.92f, 0.98f, 1.1f);
    public Vector3 HighlightTint { get; init; } = new(1.06f, 1.0f, 0.9f);

    /// <summary>Applies the grade to one display-space colour.</summary>
    public Vector3 Apply(Vector3 c)
    {
        float luma = Vector3.Dot(c, new Vector3(0.299f, 0.587f, 0.114f));
        c = Vector3.Lerp(new Vector3(luma), c, Saturation);
        c = (c - new Vector3(0.5f)) * Contrast + new Vector3(0.5f);
        c = Vector3.Clamp(c, Vector3.Zero, Vector3.One);
        c = Lift + c * (Gain - Lift);
        c = new Vector3(MathF.Pow(MathF.Max(c.X, 0), 1 / Gamma.X), MathF.Pow(MathF.Max(c.Y, 0), 1 / Gamma.Y), MathF.Pow(MathF.Max(c.Z, 0), 1 / Gamma.Z));
        float t = Math.Clamp(luma * 1.6f, 0, 1);
        c *= Vector3.Lerp(ShadowTint, HighlightTint, t * t * (3 - 2 * t));
        return Vector3.Clamp(c, Vector3.Zero, Vector3.One);
    }

    /// <summary>The grade as a 16×16×16 RGBA8 table, red fastest.</summary>
    public byte[] Bake()
    {
        var lut = new byte[Size * Size * Size * 4];
        for (int b = 0; b < Size; b++)
            for (int g = 0; g < Size; g++)
                for (int r = 0; r < Size; r++)
                {
                    var c = Apply(new Vector3(r, g, b) / (Size - 1));
                    int i = ((b * Size + g) * Size + r) * 4;
                    lut[i] = (byte)MathF.Round(c.X * 255);
                    lut[i + 1] = (byte)MathF.Round(c.Y * 255);
                    lut[i + 2] = (byte)MathF.Round(c.Z * 255);
                    lut[i + 3] = 255;
                }
        return lut;
    }

    /// <summary>A table that changes nothing.</summary>
    public static byte[] Identity() => new ColourGrade { Lift = Vector3.Zero, Gain = Vector3.One, Saturation = 1, Contrast = 1, ShadowTint = Vector3.One, HighlightTint = Vector3.One }.Bake();
}
