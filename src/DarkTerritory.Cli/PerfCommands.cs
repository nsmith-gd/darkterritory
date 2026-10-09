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
/// The headset's eyes are drawn the way tuning/vr.json says (multiview where the GPU has it), or --stereo multiview|per-eye.
/// --route tier:seed draws that night (its fog where the train is); with --town a,b (Staging.TownCamera's: square, over,
/// houses, crooked...) its departure town, the train at the gate, from those places instead of the standard views.
/// </summary>
static class PerfCommands
{
    public static object Run(TrainTuning t, string content, string[] args)
    {
        var tuning = DataFile.Load<PerfTuning>(Path.Combine(content, PerfTuning.File));
        int frames = (int)Opt(args, "--frames", 12);
        var views = Str(args, "--views", string.Join(',', Views.Names)).Split(',');
        int cars = (int)Opt(args, "--cars", 6);
        var route = Str(args, "--route", "") is { Length: > 0 } spec ? DarkTerritory.Sim.LineGen.Routes.Generate(content, spec, cars) : null;
        var line = route?.Build() ?? RailLine.Load(Path.Combine(content, "lines", Str(args, "--line", "test-loop") + ".json"));
        var consist = Consist.Uniform(t, cars, 1);
        double at = Opt(args, "--at", 1200);
        // --town: the night's departure town, as `dt screenshot --town` makes it, the train standing at its gate.
        double depart = at;
        var town = route is not null && args.Contains("--town") ? Staging.DepartureTown(content, route, line, consist.LengthMetres, out depart) : null;
        if (town is not null && !args.Contains("--at"))
            at = depart;
        var train = new TrainOnLine(new TrainDynamics(consist), line, at);
        var look = args.Contains("--greybox") ? null : Look.Load(content);
        var scene = new GreyboxScene
        {
            DrawDistance = 400,
            Look = look,
            Time = 0.37,
            Route = route,
            Town = town,
            Crew = Staging.Crew(train, content),
            Enemies = Staging.Threats(train),
        };
        var lighting = Views.Lighting(train, look);
        if (route is not null)
            lighting.FogDensity = Views.FogDensity(route, train);
        if (town is not null)
            views = Str(args, "--town", "square").Split(',');

        using var gpu = new GpuContext("dt perf");
        var mesh = new MeshBuilder();
        var wanted = Str(args, "--stereo", "") switch
        {
            "per-eye" => StereoPath.PerEye,
            "multiview" => StereoPath.Multiview,
            _ => DataFile.Load<VrTuning>(Path.Combine(content, VrTuning.File)).Stereo,
        };
        object Measure(string name, PerfTarget target)
        {
            // The eyes as VrView makes them, the moon's map at 1024: both in one multiview renderer, or one an eye, the left
            // drawing the shadow maps and the right sampling them. (The desktop's mirror is the left eye's image blitted,
            // VrView.Mirror: nothing to draw.)
            var stereo = target.Eyes > 1 ? VrView.Choose(wanted, gpu) : (StereoPath?)null;
            var eyes = stereo == StereoPath.Multiview
                ? [new GreyboxRenderer(gpu, target.Width, target.Height, moonShadowSize: 1024, views: 2)]
                : Enumerable.Range(0, target.Eyes)
                    .Select(_ => new GreyboxRenderer(gpu, target.Width, target.Height, moonShadowSize: target.Eyes > 1 ? 1024 : 2048)).ToList();
            for (int e = 1; e < eyes.Count; e++)
                eyes[e].ShadowsFrom = eyes[0];
            // (A headset's hand lamp unshadowed, as VrView has it.)
            foreach (var eye in eyes)
                eye.HandShadows = target.Eyes == 1 || VrView.HandShadows;
            var all = eyes;
            foreach (var r in all)
                look?.Dress(r);
            try
            {
                var rows = views.Select(view =>
                {
                    var camera = town is not null ? Staging.TownCamera(town, view) : Views.Get(view, train);
                    var build = new List<double>();
                    var prepare = new List<double>();
                    var record = new List<double>();
                    var submit = new List<double>();
                    var passes = new Dictionary<string, List<double>>();
                    int triangles = 0, maxDraws = 0, draws = 0;
                    FrameStats stats = default;
                    // Two warm-up frames (the first cooks the kit's pieces and uploads them), then the measured run.
                    for (int f = -2; f < frames; f++)
                    {
                        scene.Time = 0.37 + f * 1e-4;
                        scene.Timings = f >= 0 ? scene.Timings ?? [] : null;
                        scene.InstanceTriangles = f >= 0 ? scene.InstanceTriangles ?? [] : null;
                        var clock = Stopwatch.StartNew();
                        scene.Build(mesh, train, camera.Position);
                        double built = clock.Elapsed.TotalMilliseconds;
                        double prepared = 0, recorded = 0, submitted = 0;
                        triangles = 0;
                        maxDraws = 0;
                        draws = 0;
                        // Each eye a few centimetres to its side of the body's eye point, as a headset's are.
                        Camera Eye(int e)
                        {
                            var eye = camera;
                            if (target.Eyes > 1)
                                eye.EyeOffset = System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Cross(camera.Forward, System.Numerics.Vector3.UnitY)) * (e == 0 ? -0.032f : 0.032f);
                            return eye;
                        }
                        for (int e = 0; e < all.Count; e++)
                        {
                            var r = all[e];
                            Camera[] cameras = r.Views > 1 ? [Eye(0), Eye(1)] : [Eye(e)];
                            clock.Restart();
                            r.Prepare(mesh);
                            prepared += clock.Elapsed.TotalMilliseconds;
                            clock.Restart();
                            double inRecord = 0;
                            gpu.Submit(cmd =>
                            {
                                var inner = Stopwatch.StartNew();
                                r.Record(cmd, cameras, lighting, lighting.FogColor);
                                inRecord = inner.Elapsed.TotalMilliseconds;
                            });
                            submitted += clock.Elapsed.TotalMilliseconds - inRecord;
                            recorded += inRecord;
                            stats = r.Stats;
                            // A multiview pass's triangles go through the GPU once an eye.
                            triangles += stats.Triangles * stats.Views + stats.LampTriangles + stats.MoonTriangles + stats.HandTriangles;
                            draws += stats.Draws + stats.LampDraws + stats.MoonDraws + stats.HandDraws;
                            maxDraws = Math.Max(maxDraws, Math.Max(Math.Max(stats.Draws, stats.HandDraws), Math.Max(stats.LampDraws, stats.MoonDraws)));
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
                    var parts = scene.Timings!.ToDictionary(p => p.Key, p => new
                    {
                        ms = Math.Round(p.Value.Ms / frames, 2),
                        triangles = p.Value.Triangles / frames,
                        // What it put in the frame as pieces and models (before the renderer's culling).
                        instanceTriangles = scene.InstanceTriangles!.GetValueOrDefault(p.Key) / frames,
                    });
                    scene.Timings = null;
                    scene.InstanceTriangles = null;
                    return new
                    {
                        view,
                        triangles,
                        maxPassDraws = maxDraws,
                        // Every pass's draw calls in a frame, every eye's: what the CPU records and the driver checks.
                        drawsPerFrame = draws,
                        submitsPerFrame = all.Count,
                        sceneTriangles = stats.Triangles,
                        lampTriangles = stats.LampTriangles,
                        moonTriangles = stats.MoonTriangles,
                        handTriangles = stats.HandTriangles,
                        lights = stats.Lights,
                        soupTriangles = mesh.Count / 3,
                        instances = mesh.Instances.Count,
                        // The pieces and models that put the most triangles in the frame (before the renderer's culling).
                        heaviest = mesh.Instances.GroupBy(i => i.Asset.Name).Select(g => new { piece = g.Key, count = g.Count(), triangles = g.Sum(i => i.Asset.Triangles) })
                            .OrderByDescending(x => x.triangles).Take(args.Contains("--all-pieces") ? 10000 : 12).ToList(),
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
                    resolution = $"{target.Width}x{target.Height}" + (target.Eyes > 1 ? $" x{target.Eyes} eyes" : "") + (target.Mirror ? " (mirrored: the left eye)" : ""),
                    stereo = stereo?.ToString(),
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

        // --ride s: the train run on at --speed m/s for s seconds of frames at 60 fps from the roof, each frame built and drawn
        // (small): the worst frames, where a cell of the line or a house is cooked as it comes into reach (note 479). With
        // --town --walk, down the town's first street from the square at a walk instead.
        if (args.Contains("--ride"))
            return Ride(train, scene, lighting, look, gpu, tuning, Opt(args, "--ride", 20), Opt(args, "--speed", args.Contains("--walk") ? 1.6 : 25), route, town,
                args.Contains("--walk"));

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

    static object Ride(TrainOnLine train, GreyboxScene scene, FrameLighting lighting, Look? look, GpuContext gpu, PerfTuning tuning, double seconds, double speed,
        DarkTerritory.Sim.Route.Route? route, DarkTerritory.Sim.Towns.Town? town, bool walk)
    {
        var target = tuning.Pc;
        // (Drawn small: the CPU's frame is what's measured, its uploads included.)
        using var renderer = new GreyboxRenderer(gpu, 320, 180, moonShadowSize: 512);
        look?.Dress(renderer);
        var mesh = new MeshBuilder();
        var frames = new List<(double Build, double Prepare, double At, string Worst)>();
        const double dt = 1.0 / 60;
        long allocated = 0;
        int gen0 = 0, gen1 = 0, gen2 = 0;
        for (int f = 0; f < seconds / dt; f++)
        {
            if (!walk)
            {
                train.Dynamics.Distance = Math.Min(train.Line.Length - 1, train.Dynamics.Distance + speed * dt);
                train.RefreshFrames();
            }
            scene.Time = 0.37 + f * dt;
            if (route is not null)
                lighting.FogDensity = Views.FogDensity(route, train);
            scene.Fog = lighting;
            // --walk: in the town, down its first street from the square at a walk instead (its houses come near, note 479).
            var camera = town is not null && walk ? Staging.TownCamera(town, "houses") : Views.Get("roof", train);
            if (town is not null && walk)
                camera.Position += town.Direction(town.Plan.Square.S0, -1, 0) * (f * dt * speed);
            scene.Timings = [];
            if (f == 1)
                (allocated, gen0, gen1, gen2) = (GC.GetAllocatedBytesForCurrentThread(), GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
            var clock = Stopwatch.StartNew();
            scene.Build(mesh, train, camera.Position);
            double built = clock.Elapsed.TotalMilliseconds;
            clock.Restart();
            renderer.Prepare(mesh);
            double prepared = clock.Elapsed.TotalMilliseconds;
            gpu.Submit(cmd => renderer.Record(cmd, [camera], lighting, lighting.FogColor));
            frames.Add((built, prepared, train.Dynamics.Distance, scene.Timings.MaxBy(p => p.Value.Ms).Key));
        }
        scene.Timings = null;
        // The first frame cooks everything in reach: the loading screen's, not the ride's.
        var ride = frames.Skip(1).ToList();
        var cpu = ride.Select(x => x.Build + x.Prepare).Order().ToList();
        double budget = target.FrameMs * tuning.CpuShare;
        return new
        {
            device = gpu.DeviceName,
            seconds,
            speed,
            frames = ride.Count,
            firstFrameMs = Math.Round(frames[0].Build + frames[0].Prepare, 1),
            cpuBudgetMs = Math.Round(budget, 2),
            medianMs = Math.Round(cpu[cpu.Count / 2], 2),
            p99Ms = Math.Round(cpu[(int)(cpu.Count * 0.99)], 2),
            worstMs = Math.Round(cpu[^1], 2),
            // Frames over twice the budget: the hitches a player feels.
            hitches = ride.Count(x => x.Build + x.Prepare > budget * 2),
            // What the main thread allocates a frame, and the collections it costs over the ride (a collection's pause lands
            // in whichever part is running).
            allocatedKbPerFrame = Math.Round((GC.GetAllocatedBytesForCurrentThread() - allocated) / 1024.0 / Math.Max(1, ride.Count)),
            collections = new { gen0 = GC.CollectionCount(0) - gen0, gen1 = GC.CollectionCount(1) - gen1, gen2 = GC.CollectionCount(2) - gen2 },
            worst = ride.OrderByDescending(x => x.Build + x.Prepare).Take(8)
                .Select(x => new { at = Math.Round(x.At), buildMs = Math.Round(x.Build, 1), prepareMs = Math.Round(x.Prepare, 1), part = x.Worst }),
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
