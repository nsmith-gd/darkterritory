using System.Diagnostics;
using System.Globalization;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

/// <summary>
/// `dt perf`: a frame's cost against the frame-rate targets (content/tuning/perf.json). Each standard view with the
/// crew on the roof and the threats about, drawn as a flat screen draws it and as a headset does (two eyes, and the
/// desktop's mirror), for a run of frames. The main thread's share (the scene built, uploaded to the GPU, recorded) is
/// timed on this machine's CPU; the GPU's passes by timestamps. Counts (triangles, draws) are the same on any machine.
/// </summary>
static class PerfCommands
{
    public static object Run(TrainTuning t, string content, string[] args)
    {
        var tuning = DataFile.Load<PerfTuning>(Path.Combine(content, PerfTuning.File));
        int frames = (int)Opt(args, "--frames", 12);
        var views = Str(args, "--views", string.Join(',', Views.Names)).Split(',');
        var line = RailLine.Load(Path.Combine(content, "lines", Str(args, "--line", "test-loop") + ".json"));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(t, (int)Opt(args, "--cars", 6), 1)), line, Opt(args, "--at", 1200));
        var look = args.Contains("--greybox") ? null : Look.Load(content);
        var scene = new GreyboxScene
        {
            DrawDistance = 400,
            Look = look,
            Time = 0.37,
            Crew = Staging.Crew(train, content),
            Enemies = Staging.Threats(train),
        };
        var lighting = Views.Lighting(train, look);

        using var gpu = new GpuContext("dt perf");
        var mesh = new MeshBuilder();
        object Measure(string name, PerfTarget target)
        {
            // The eyes as VrView makes them (each its own shadow maps; the moon's at 1024), and the flat view's renderer
            // for the mirror.
            var eyes = Enumerable.Range(0, target.Eyes)
                .Select(_ => new GreyboxRenderer(gpu, target.Width, target.Height, moonShadowSize: target.Eyes > 1 ? 1024 : 2048)).ToList();
            var mirror = target.Mirror ? new GreyboxRenderer(gpu, tuning.Pc.Width, tuning.Pc.Height) : null;
            var all = mirror is null ? eyes : [.. eyes, mirror];
            foreach (var r in all)
                look?.Dress(r);
            try
            {
                var rows = views.Select(view =>
                {
                    var camera = Views.Get(view, train);
                    var build = new List<double>();
                    var prepare = new List<double>();
                    var record = new List<double>();
                    var submit = new List<double>();
                    var passes = new Dictionary<string, List<double>>();
                    int triangles = 0, maxDraws = 0;
                    FrameStats stats = default;
                    // Two warm-up frames (the first cooks the kit's pieces and uploads them), then the measured run.
                    for (int f = -2; f < frames; f++)
                    {
                        scene.Time = 0.37 + f * 1e-4;
                        scene.Timings = f >= 0 ? scene.Timings ?? [] : null;
                        var clock = Stopwatch.StartNew();
                        scene.Build(mesh, train, camera.Position);
                        double built = clock.Elapsed.TotalMilliseconds;
                        double prepared = 0, recorded = 0, submitted = 0;
                        triangles = 0;
                        maxDraws = 0;
                        for (int e = 0; e < all.Count; e++)
                        {
                            var r = all[e];
                            // Each eye a few centimetres to its side of the body's eye point, as a headset's are.
                            var eye = camera;
                            if (e < eyes.Count && eyes.Count > 1)
                                eye.EyeOffset = System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Cross(camera.Forward, System.Numerics.Vector3.UnitY)) * (e == 0 ? -0.032f : 0.032f);
                            clock.Restart();
                            r.Prepare(mesh);
                            prepared += clock.Elapsed.TotalMilliseconds;
                            clock.Restart();
                            double inRecord = 0;
                            gpu.Submit(cmd =>
                            {
                                var inner = Stopwatch.StartNew();
                                r.Record(cmd, eye, lighting, lighting.FogColor);
                                inRecord = inner.Elapsed.TotalMilliseconds;
                            });
                            submitted += clock.Elapsed.TotalMilliseconds - inRecord;
                            recorded += inRecord;
                            stats = r.Stats;
                            triangles += stats.Triangles + stats.LampTriangles + stats.MoonTriangles;
                            maxDraws = Math.Max(maxDraws, Math.Max(stats.Draws, Math.Max(stats.LampDraws, stats.MoonDraws)));
                            if (f >= 0)
                                foreach (var (pass, ms) in r.PassTimes())
                                {
                                    if (!passes.TryGetValue(pass, out var list))
                                        passes[pass] = list = [];
                                    if (list.Count <= f)
                                        list.Add(0);
                                    list[f] += ms;
                                }
                        }
                        if (f < 0)
                            continue;
                        build.Add(built);
                        prepare.Add(prepared);
                        record.Add(recorded);
                        submit.Add(submitted);
                    }
                    double cpu = Median(build) + Median(prepare) + Median(record);
                    var parts = scene.Timings!.ToDictionary(p => p.Key, p => new { ms = Math.Round(p.Value.Ms / frames, 2), triangles = p.Value.Triangles / frames });
                    scene.Timings = null;
                    return new
                    {
                        view,
                        triangles,
                        maxPassDraws = maxDraws,
                        sceneTriangles = stats.Triangles,
                        lampTriangles = stats.LampTriangles,
                        moonTriangles = stats.MoonTriangles,
                        lights = stats.Lights,
                        soupTriangles = mesh.Count / 3,
                        instances = mesh.Instances.Count,
                        buildParts = parts,
                        cpuMs = Math.Round(cpu, 2),
                        buildMs = Math.Round(Median(build), 2),
                        prepareMs = Math.Round(Median(prepare), 2),
                        recordMs = Math.Round(Median(record), 2),
                        // Submitted to done, less the recording: the GPU's work, and the wait for it.
                        gpuWaitMs = Math.Round(Median(submit), 2),
                        gpuPassMs = passes.ToDictionary(p => p.Key, p => Math.Round(Median(p.Value), 2)),
                    };
                }).ToList();
                double cpuBudget = target.FrameMs * tuning.CpuShare;
                return new
                {
                    target = name,
                    fps = target.Fps,
                    frameMs = Math.Round(target.FrameMs, 2),
                    resolution = $"{target.Width}x{target.Height}" + (target.Eyes > 1 ? $" x{target.Eyes} eyes" : "") + (mirror is not null ? $" + {tuning.Pc.Width}x{tuning.Pc.Height} mirror" : ""),
                    cpuBudgetMs = Math.Round(cpuBudget, 2),
                    worstCpuMs = rows.Max(r => r.cpuMs),
                    cpuWithin = rows.All(r => r.cpuMs <= cpuBudget),
                    countsWithin = rows.All(r => r.triangles <= tuning.MaxFrameTriangles && r.maxPassDraws <= tuning.MaxPassDraws),
                    views = rows,
                };
            }
            finally
            {
                foreach (var r in all)
                    r.Dispose();
            }
        }

        return new
        {
            device = gpu.DeviceName,
            cpu = CpuName(),
            frames,
            // Lavapipe rasterises on the CPU: its pass times rank the passes against each other, they aren't a GPU's.
            gpuNote = gpu.DeviceName.Contains("llvmpipe") ? "software rasteriser: GPU times are relative costs only" : null,
            maxFrameTriangles = tuning.MaxFrameTriangles,
            maxPassDraws = tuning.MaxPassDraws,
            pc = Str(args, "--only", "") is "vr" ? null : Measure("pc", tuning.Pc),
            vr = Str(args, "--only", "") is "pc" ? null : Measure("vr", tuning.Vr),
        };
    }

    static double Median(List<double> xs)
    {
        if (xs.Count == 0)
            return 0;
        var sorted = xs.Order().ToList();
        return sorted[sorted.Count / 2];
    }

    static string CpuName()
    {
        try
        {
            return File.ReadLines("/proc/cpuinfo").FirstOrDefault(l => l.StartsWith("model name"))?.Split(':', 2)[1].Trim() ?? Environment.ProcessorCount + " cores";
        }
        catch (IOException)
        {
            return Environment.ProcessorCount + " cores";
        }
    }

    static string Str(string[] args, string name, string fallback)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
    }

    static double Opt(string[] args, string name, double fallback)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? double.Parse(args[i + 1], CultureInfo.InvariantCulture) : fallback;
    }
}
