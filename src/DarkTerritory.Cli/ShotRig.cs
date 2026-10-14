using Ballast.Render;
using DarkTerritory.Game;

/// <summary>
/// The GPU, renderer and look a screenshot draws with (note 517). One screenshot makes its own and lets them go. A batch
/// (<c>dt screenshot --batch file</c>) keeps one dressed renderer for every shot of its size: the look's 270 material layers
/// are decoded and uploaded once, and each shot after the first only swaps the night's sky and resets the post settings.
/// A batch is byte for byte the same batch again (the review packet draws main's and a pull request's this way), and each of
/// its shots is the shot drawn on its own within the packet's tolerance (<c>dt screenshot --batch … --check</c>): the reused
/// look's art caches leave a level or two in a few pixels, never a change anyone would see.
/// </summary>
static class ShotRig
{
    /// <summary>Set for a batch's length: shots keep the rig instead of disposing it.</summary>
    public static bool Batching { get; set; }

    sealed record Kept(GpuContext Gpu, GreyboxRenderer Renderer, Look? Look, int Width, int Height, bool Greybox, object? Sky, PostSettings Post);

    static Kept? _kept;

    /// <summary>
    /// The rig for a shot of this size and look, dressed for <paramref name="sky"/> (the night's own horizon, or null for the
    /// look's). <paramref name="owned"/> is what the caller disposes when it's done (null in a batch: the batch does).
    /// </summary>
    public static (GpuContext Gpu, GreyboxRenderer Renderer, Look? Look) Take(string content, bool greybox, int width, int height,
        Image? sky, object? skyKey, out IDisposable? owned)
    {
        owned = null;
        if (Batching && _kept is { } k && k.Width == width && k.Height == height && k.Greybox == greybox)
        {
            if (k.Look is { } l)
            {
                if (!Equals(k.Sky, skyKey))
                {
                    l.Sky = sky;
                    l.DressSky(k.Renderer);
                    _kept = k with { Sky = skyKey };
                }
            }
            // As it was dressed: a shot before may have changed it (--ps2).
            k.Renderer.Post = k.Post;
            return (k.Gpu, k.Renderer, k.Look);
        }
        // A batch's rig of another size goes first: two dressed rigs at once is twice the look on the GPU (2 GB more on
        // lavapipe at the gallery's HUD shot).
        if (Batching)
            Release();
        var gpu = new GpuContext("dt screenshot");
        var renderer = new GreyboxRenderer(gpu, width, height);
        var look = greybox ? null : Look.Load(content);
        if (look is not null)
        {
            look.Sky = sky;
            look.Dress(renderer);
        }
        if (Batching)
        {
            _kept = new Kept(gpu, renderer, look, width, height, greybox, skyKey, renderer.Post);
        }
        else
            owned = new Owned(renderer, gpu);
        return (gpu, renderer, look);
    }

    /// <summary>The batch's rig let go (its end, or a shot of another size).</summary>
    public static void Release()
    {
        if (_kept is { } k)
        {
            k.Renderer.Dispose();
            k.Gpu.Dispose();
        }
        _kept = null;
    }

    sealed class Owned(GreyboxRenderer renderer, GpuContext gpu) : IDisposable
    {
        public void Dispose()
        {
            renderer.Dispose();
            gpu.Dispose();
        }
    }
}
