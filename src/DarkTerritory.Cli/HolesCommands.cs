using System.Diagnostics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

/// <summary>
/// `dt holes --route r`: where the land is seen through (ARCHITECTURE §8 note 433; the director, 8 Oct, GDD App. F.4:
/// "lots of textures in the landscape generation that are see through or missing"). Down the whole of a night's main
/// line, every <c>--every</c> metres, three cameras: at the track looking on down it, up over it looking ahead, and out
/// across the land to one side (the sides taken in turn). Each frame is drawn with the renderer's hole probe on
/// (<see cref="GreyboxRenderer.HoleSlope"/>): the sky is painted magenta wherever it shows lower than the land could
/// hide it, so a magenta pixel is a hole, whatever made it. The frames with any are saved to <c>--out</c> and listed,
/// worst first. <c>--untextured</c>: every surface drawn without a texture cyan too (<see cref="GreyboxRenderer.ShowUntextured"/>),
/// counted beside the holes, for the land's "missing" textures.
/// </summary>
static class HolesCommands
{
    /// <summary>The cameras at one place along the line: (name, eye, target), in line coordinates (along, out, up).</summary>
    static readonly (string Name, double[] Eye, double[] Target)[] Shots =
    [
        ("track", [0, 0, 2.5], [60, 0, 0.5]),
        ("high", [0, 18, 30], [90, 0, 0]),
        ("across", [0, 2, 3], [12, 200, -12]),
    ];

    public static object Run(TrainTuning t, string content, string[] args)
    {
        string spec = Str(args, "--route", "frontier:7");
        double every = Opt(args, "--every", 400);
        int width = (int)Opt(args, "--width", 640), height = (int)Opt(args, "--height", 360);
        // How many magenta pixels make a hole worth listing (a frame's corner, at 640 by 360, is 0.01 % of it a pixel).
        int least = (int)Opt(args, "--least", 12);
        // How far out a hole is looked for (m): past it, the land's own far edge may show the sky below the horizon.
        double reach = Opt(args, "--reach", 300);
        string outDir = Str(args, "--out", Path.Combine("out", "holes", spec.Replace(':', '-')));
        var only = Str(args, "--shots", "") is { Length: > 0 } names ? names.Split(',').ToHashSet() : null;
        Directory.CreateDirectory(outDir);
        foreach (var old in Directory.GetFiles(outDir, "*.png"))
            File.Delete(old);

        var route = DarkTerritory.Sim.LineGen.Routes.Generate(content, spec, 6);
        var line = route.Build();
        var look = Look.Load(content);
        look.Sky = DarkTerritory.Game.Art.PlanSky.For(route);
        using var gpu = new GpuContext("dt holes");
        using var renderer = new GreyboxRenderer(gpu, width, height);
        look.Dress(renderer);
        bool untextured = args.Contains("--untextured");
        renderer.ShowUntextured = untextured;
        var scene = new GreyboxScene { DrawDistance = (float)Opt(args, "--draw", 400), Look = look, Route = route, Time = 0.37 };
        var mesh = new MeshBuilder();
        var clock = Stopwatch.StartNew();
        var found = new List<(string File, string Shot, double At, int Side, int Pixels, double Nearest, int Flat)>();
        int frames = 0, buried = 0;
        double from = Opt(args, "--from", 200), to = Math.Min(Opt(args, "--to", line.Length), line.Length - 120);
        for (double s = from; s < to; s += every)
        {
            int side = (int)Math.Round(s / every) % 2 == 0 ? 1 : -1;
            // The train well back down the line, out of the cameras' way (the scene's built round the eye).
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(t, 6, 1)), line, Math.Max(60, s - 700));
            foreach (var (name, eye, target) in Shots)
            {
                if (only is not null && !only.Contains(name))
                    continue;
                Double3 At(double[] p)
                {
                    var q = line.Sample(Math.Clamp(s + p[0], 0, line.Length));
                    var right = Double3.Cross(q.Tangent, Double3.Up).Normalized;
                    return q.Position + right * (p[1] * side) + Double3.Up * p[2];
                }
                var camera = Camera.LookAt(At(eye), At(target), 65);
                // The probe's slope: steeper down than the land under the eye, out at the reach, could be (the land falls
                // 30 m in it at most where it's looked for), so the land's far edge and a valley's far side never count.
                double over = camera.Position.Y - (line.Conditions?.Ground(camera.Position) ?? camera.Position.Y - eye[2]);
                // An eye in the hill (beside a bore, in a cutting's wall) sees out through the land's back: no frame.
                if (over < 0.3)
                {
                    buried++;
                    continue;
                }
                renderer.HoleSlope = (float)(-(Math.Max(over, 0) + 30) / reach);
                scene.Build(mesh, train, camera.Position);
                var lighting = Views.Lighting(train, look);
                Survey(ref lighting);
                var px = renderer.Render(mesh, camera, lighting, lighting.FogColor);
                frames++;
                int holes = Count(px, width, height, Magenta, out int lowest);
                int flat = untextured ? Count(px, width, height, Cyan, out _) : 0;
                if (holes < least && flat < least)
                    continue;
                string file = Path.Combine(outDir, $"{s:00000}-{name}.png");
                PngWriter.Write(file, px, width, height);
                found.Add((Path.GetFullPath(file), name, Math.Round(s), side, holes, lowest, flat));
            }
        }
        renderer.HoleSlope = 0;
        renderer.ShowUntextured = false;
        return new
        {
            route = spec,
            km = Math.Round(line.Length / 1000, 2),
            frames,
            buried,
            withHoles = found.Count(f => f.Pixels >= least),
            withUntextured = untextured ? found.Count(f => f.Flat >= least) : (int?)null,
            ms = clock.ElapsedMilliseconds,
            // Worst first; `lowestRow` is the lowest magenta row in the frame (0 at the top), the nearer the bottom the nearer the eye.
            // `untextured`: how many of its pixels are surfaces drawn without a texture (with --untextured).
            holes = found.OrderByDescending(f => f.Pixels).ThenByDescending(f => f.Flat)
                .Select(f => new { f.File, f.Shot, at = f.At, f.Side, f.Pixels, lowestRow = f.Nearest, untextured = f.Flat }),
        };
    }

    /// <summary>A flat, bright, clear light (screenshot's --survey): the land's shape plain, and the probe's magenta clear of fog.</summary>
    static void Survey(ref FrameLighting lighting)
    {
        lighting.MoonDirection = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(0.4f, 0.8f, 0.3f));
        lighting.MoonColour = new System.Numerics.Vector3(1, 0.97f, 0.9f);
        lighting.MoonStrength = 2.2f;
        lighting.Ambient = 0.55f;
        lighting.FogColor = new System.Numerics.Vector3(0.62f, 0.64f, 0.66f);
        lighting.FogDensity = 0.0012f;
        lighting.Wetness = 0;
    }

    /// <summary>The hole probe's magenta: red and blue high, green low.</summary>
    static bool Magenta(byte r, byte g, byte b) => r > 180 && b > 180 && g < 90;

    /// <summary>The untextured probe's cyan: green and blue high, red low.</summary>
    static bool Cyan(byte r, byte g, byte b) => g > 180 && b > 180 && r < 90;

    /// <summary>How many pixels are a probe's colour, and the lowest row with one.</summary>
    static int Count(byte[] rgba, int width, int height, Func<byte, byte, byte, bool> probe, out int lowest)
    {
        int n = 0;
        lowest = -1;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;
                if (probe(rgba[i], rgba[i + 1], rgba[i + 2]))
                {
                    n++;
                    lowest = y;
                }
            }
        return n;
    }

    static string Str(string[] args, string name, string fallback)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
    }

    static double Opt(string[] args, string name, double fallback)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? double.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture) : fallback;
    }
}
