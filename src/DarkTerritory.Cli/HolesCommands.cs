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
/// counted beside the holes, for the land's "missing" textures. <c>--edges</c> (note 498): <c>main</c>, <c>branches</c> (each
/// alternate and dead line, the same cameras on its own track), <c>stops</c> (each stop's ground afoot, among its buildings),
/// any of them comma-separated, or <c>all</c>. Every frame listed carries its camera in the main line's coordinates, for
/// `dt screenshot --cam ... --target ...` to show it as the crew sees it.
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
        var found = new List<(string File, string Edge, string Shot, double At, int Side, int Pixels, double Nearest, int Flat, double OffMain, string Cam, string Target)>();
        int frames = 0, buried = 0;
        // Which ground is walked: the main line; the branches (the alternates and dead lines, a crew's when the Switchman or
        // the route card takes them there, each on its own track from its points); the stops close up, afoot; or all three.
        var edges = Str(args, "--edges", "main").Split(',').ToHashSet();
        bool all = edges.Contains("all");
        var walks = new List<(string Name, RailLine Track, double Toe, double From, double To)>();
        if (all || edges.Contains("main"))
            walks.Add(("main", line, 0, Opt(args, "--from", 200), Math.Min(Opt(args, "--to", line.Length), line.Length - 120)));
        if (all || edges.Contains("branches"))
            foreach (var b in line.Branches.Where(b => b.Kind is BranchKind.Alternate or BranchKind.DeadLine))
                walks.Add(($"b{b.Index}", b.Local, b.Toe, Math.Min(every / 2, b.Local.Length / 2), b.Local.Length - (b.Rejoins ? 40 : 20)));
        var views = new List<HoleView>();
        foreach (var (edge, track, toe, from, to) in walks)
            for (double s = from; s < to; s += every)
            {
                // The sides in turn: on the main line by where it is (as note 433's sweeps had them), on a branch by the step
                // (its steps fall on the half, which rounds to even every time).
                int side = (edge == "main" ? (int)Math.Round(s / every) : (int)Math.Round((s - from) / every)) % 2 == 0 ? 1 : -1;
                foreach (var (name, eye, target) in Shots)
                {
                    Double3 At(double[] p)
                    {
                        var q = track.Sample(Math.Clamp(s + p[0], 0, track.Length));
                        var right = Double3.Cross(q.Tangent, Double3.Up).Normalized;
                        return q.Position + right * (p[1] * side) + Double3.Up * p[2];
                    }
                    views.Add(new(edge, name, s, side, At(eye), At(target), eye[2], toe + s));
                }
            }
        if (all || edges.Contains("stops"))
            views.AddRange(StopViews(route, line, Opt(args, "--stop-every", 60)));
        foreach (var v in views)
        {
            if (only is not null && !only.Any(o => v.Shot == o || v.Shot.EndsWith("-" + o, StringComparison.Ordinal)))
                continue;
            var camera = Camera.LookAt(v.Eye, v.Target, 65);
            // The probe's slope: steeper down than the land under the eye, out at the reach, could be (the land falls
            // 30 m in it at most where it's looked for), so the land's far edge and a valley's far side never count.
            double over = camera.Position.Y - (line.Conditions?.Ground(camera.Position) ?? camera.Position.Y - v.Up);
            // An eye in the hill (beside a bore, in a cutting's wall) sees out through the land's back: no frame.
            if (over < 0.3)
            {
                buried++;
                continue;
            }
            renderer.HoleSlope = (float)(-(Math.Max(over, 0) + 30) / reach);
            // The train well back down the main line, out of the cameras' way (the scene's built round the eye).
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(t, 6, 1)), line, Math.Clamp(v.TrainAt - 700, 60, line.Length - 60));
            scene.Build(mesh, train, camera.Position);
            var lighting = Views.Lighting(train, look);
            Survey(ref lighting);
            var px = renderer.Render(mesh, camera, lighting, lighting.FogColor);
            frames++;
            int holes = Count(px, width, height, Magenta, out int lowest);
            int flat = untextured ? Count(px, width, height, Cyan, out _) : 0;
            if (holes < least && flat < least)
                continue;
            string file = Path.Combine(outDir, v.Edge == "main" ? $"{v.At:00000}-{v.Shot}.png" : $"{v.Edge}-{v.At:00000}-{v.Shot}.png");
            PngWriter.Write(file, px, width, height);
            // How far the eye is from the main line (off a branch, whether it's out on the main line's own land, 300 m each side),
            // and the camera in the main line's coordinates, for `dt screenshot --cam ... --target ...` to show it without the probe.
            var (cam, off) = OnMain(line, v.Eye, v.TrainAt);
            var (target, _) = OnMain(line, v.Target, v.TrainAt);
            found.Add((Path.GetFullPath(file), v.Edge, v.Shot, Math.Round(v.At), v.Side, holes, lowest, flat, off, cam, target));
        }
        renderer.HoleSlope = 0;
        renderer.ShowUntextured = false;
        return new
        {
            route = spec,
            km = Math.Round(line.Length / 1000, 2),
            walked = walks.Select(w => new { edge = w.Name, km = Math.Round(w.Track.Length / 1000, 2) }),
            stops = views.Where(v => v.Edge.StartsWith("stop", StringComparison.Ordinal)).Select(v => v.Edge).Distinct(),
            frames,
            buried,
            withHoles = found.Count(f => f.Pixels >= least),
            withUntextured = untextured ? found.Count(f => f.Flat >= least) : (int?)null,
            ms = clock.ElapsedMilliseconds,
            // Worst first; `lowestRow` is the lowest magenta row in the frame (0 at the top), the nearer the bottom the nearer the eye.
            // `untextured`: how many of its pixels are surfaces drawn without a texture (with --untextured).
            holes = found.OrderByDescending(f => f.Pixels).ThenByDescending(f => f.Flat)
                .Select(f => new { f.File, f.Edge, f.Shot, at = f.At, f.Side, f.Pixels, lowestRow = f.Nearest, untextured = f.Flat, offMain = f.OffMain, cam = f.Cam, target = f.Target }),
        };
    }

    /// <summary>
    /// One camera: the ground it's on (<c>main</c>, a branch <c>b3</c>, a stop <c>stop12400</c>), its name, how far along that
    /// ground, which side, where it stands and looks, how high it was put over its ground, and where along the main line it is.
    /// </summary>
    readonly record struct HoleView(string Edge, string Shot, double At, int Side, Double3 Eye, Double3 Target, double Up, double TrainAt);

    /// <summary>
    /// A stop close up, afoot (note 433's "not yet"): through its zone every <paramref name="step"/> m, either side of the line
    /// out among its buildings (25 and 60 m), an eye at a crewmate's height looking four ways, a little down, as one walks its
    /// village or yard. The cameras that end up in a house see its walls; those are the probe's sky only if it shows through.
    /// </summary>
    static IEnumerable<HoleView> StopViews(DarkTerritory.Sim.Route.Route route, RailLine line, double step)
    {
        foreach (var f in route.Features.Where(f => f.Stop is not null))
        {
            string edge = $"stop{f.Start:0}";
            for (double s = step / 2; s < f.Stop!.ZoneLength; s += step)
                foreach (double d in new[] { -60.0, -25, 25, 60 })
                {
                    var foot = DarkTerritory.Sim.Run.Run.StopWorld(line, f, new DarkTerritory.Sim.Stops.Pt(s, d));
                    var eye = foot with { Y = (line.Conditions?.Ground(foot) ?? foot.Y) + 1.7 };
                    var t = line.Sample(f.Start + s).Tangent;
                    var right = Double3.Cross(t, Double3.Up).Normalized;
                    foreach (var (name, dir) in new[] { ("ahead", t), ("back", t * -1), ("out", right * Math.Sign(d)), ("in", right * -Math.Sign(d)) })
                        yield return new HoleView(edge, $"{(d < 0 ? 'l' : 'r')}{Math.Abs(d):0}-{name}", s, Math.Sign(d), eye, eye + dir * 30 - Double3.Up * 1.5, 1.7, f.Start + s);
                }
        }
    }

    /// <summary>A world point in the main line's coordinates ("along,out,up", as `dt screenshot --cam` takes them), and how far off it it is.</summary>
    static (string Coords, double Off) OnMain(RailLine line, Double3 p, double hint)
    {
        line.Nearest(p, ref hint);
        var t = line.Sample(hint);
        var d = p - t.Position;
        double lateral = Double3.Dot(d, Double3.Cross(t.Tangent, Double3.Up).Normalized);
        var c = System.Globalization.CultureInfo.InvariantCulture;
        return (string.Create(c, $"{hint:0.#},{lateral:0.#},{d.Y:0.#}"), Math.Round(Math.Sqrt(d.X * d.X + d.Z * d.Z)));
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
