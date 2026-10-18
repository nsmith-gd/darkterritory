using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>
/// The yard photographer (docs/design/upgrades.md §8.4; ARCHITECTURE §8 note 714): bought at the blacksmith, he stands
/// just past the yard's gate, off the line's right with his camera on its tripod, facing the engine as the night begins;
/// as she comes out through the gatehouse to him (the run's out of the yard), flash, and the crew's picture is taken from his lens. Presentation
/// only, like a bookmark's still (App. D.12): every machine takes its own from what it has of the world, shows it on the
/// HUD a while (the feedback: <see cref="Shown"/>) and keeps it, sepia, in the night's bookmarks folder.
/// </summary>
public sealed class YardPhotographer
{
    /// <summary>Where he stands: this far past the gate, this far off the line's right (m). His lens is at <see cref="LensHeight"/>.</summary>
    public const double PastGate = 24, Side = 3.2, LensHeight = 1.55;

    /// <summary>How far the engine's front is from him when he takes it (m): through the gatehouse, filling his frame.</summary>
    public const double ShootAt = 13;

    /// <summary>The photograph's size, and how long it's on the HUD after the flash (s).</summary>
    public const int Width = 480, Height = 300;
    public const double ShowSeconds = 7, FlashSeconds = 0.35;

    bool _sawYard;

    /// <summary>The night's photograph, once taken.</summary>
    public Still? Photo { get; private set; }

    /// <summary>When it was taken, by this machine's clock (s); NaN until it is.</summary>
    public double TakenAt { get; private set; } = double.NaN;

    /// <summary>A new night: none taken.</summary>
    public void Clear()
    {
        (_sawYard, Photo, TakenAt) = (false, null, double.NaN);
    }

    /// <summary>Where his feet are, and which way he faces (back down the line at the engine coming).</summary>
    public static (Double3 Feet, Double3 Facing) Stand(RailLine line, double gate)
    {
        var t = line.Sample(Math.Min(gate + PastGate, line.Length));
        var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
        var feet = t.Position + right * Side;
        // Turned a little in to the line, so the engine's face and the crew on her are in the picture.
        var facing = (t.Tangent * -1 + right * -0.45).Normalized;
        return (feet, facing);
    }

    /// <summary>His lens: from his camera on its tripod, on the engine's front and cab as she comes through the gate.</summary>
    public static Camera Lens(RailLine line, double gate, TrainOnLine train)
    {
        var (feet, facing) = Stand(line, gate);
        var engine = train.Frames[0];
        double half = engine.Shape.HalfLength;
        // Just out in front of his brass lens (Art.DressKit.Tripod's, 0.45 m out), not in the box.
        return Camera.LookAt(feet + facing * 0.5 + Double3.Up * LensHeight, engine.ToWorld(new Double3(0, 2.6, -half + 7)), 52);
    }

    /// <summary>
    /// Whether the picture's due this frame: the train's dressed with him, this machine saw the night in the yard (so a
    /// player who joins on the way has none), and she's out of it now and <see cref="ShootAt"/> from him.
    /// </summary>
    public bool Due(IPlaySession s, IReadOnlyList<string> dress)
    {
        if (Photo is not null || !dress.Contains("yardPhotographer") || s.World.Run is not { } run)
            return false;
        if (run.Phase == Sim.Run.RunPhase.Yard)
        {
            _sawYard = true;
            return false;
        }
        return _sawYard && s.Train.Dynamics.Distance >= run.YardLength + PastGate - ShootAt;
    }

    /// <summary>Keeps the frame drawn from his lens: cropped, shrunk and printed in sepia.</summary>
    public Still Keep(byte[] rgba, int width, int height, double now)
    {
        var still = BookmarkStills.Shrink(rgba, width, height, Width, Height);
        Sepia(still.Rgba);
        (Photo, TakenAt) = (still, now);
        return still;
    }

    /// <summary>Whether the flash is still going off (for its light at his tripod and the white over the HUD), 0..1.</summary>
    public double Flash(double now) => double.IsNaN(TakenAt) ? 0 : Math.Clamp(1 - (now - TakenAt) / FlashSeconds, 0, 1);

    /// <summary>Whether the photograph's on the HUD now (from the flash for <see cref="ShowSeconds"/>).</summary>
    public bool Shown(double now) => Photo is not null && now - TakenAt < ShowSeconds;

    /// <summary>Writes the photograph into the night's bookmarks folder; the file, or null when the disk won't have it.</summary>
    public string? Save(string night)
    {
        if (Photo is not { } p)
            return null;
        try
        {
            string path = Path.Combine(night, "00 yard photograph.png");
            PngWriter.Write(path, p.Rgba, p.Width, p.Height);
            return path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"yard photographer: couldn't keep the photograph in {night} ({e.Message})");
            return null;
        }
    }

    /// <summary>An old print's brown: each pixel's grey, toned warm, the blacks lifted a little, a vignette at the corners.</summary>
    public static void Sepia(byte[] rgba, int width = Width, int height = Height)
    {
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;
                double grey = (0.3 * rgba[i] + 0.59 * rgba[i + 1] + 0.11 * rgba[i + 2]) / 255;
                double dx = (x + 0.5) / width - 0.5, dy = (y + 0.5) / height - 0.5;
                double v = 0.08 + 0.92 * grey * (1 - 0.9 * (dx * dx + dy * dy));
                rgba[i] = (byte)Math.Clamp(v * 255 * 1.07 + 6, 0, 255);
                rgba[i + 1] = (byte)Math.Clamp(v * 255 * 0.86 + 4, 0, 255);
                rgba[i + 2] = (byte)Math.Clamp(v * 255 * 0.62, 0, 255);
                rgba[i + 3] = 255;
            }
    }
}
