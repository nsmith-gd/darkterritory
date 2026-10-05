using System.Diagnostics;
using System.Globalization;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

/// <summary>
/// `dt playthrough`: a real night, photographed. The art checklist rates each piece in a staged view; this checks what a
/// crew actually sees. A solo night on a generated route runs with the director's enemies, the train driven by the line's
/// own authority (LineGen.Ride, the app's --ride), and every encounter is rendered as it happens, the game's own scene as
/// the app builds it: when each enemy is first there, and as it telegraphs, grabs and punishes, from beside it (or down
/// the aisle, if it's in a car). A chase shot every --every seconds shows the line between. Frames go to
/// out/playthrough with index.json (what, when, where), for the Look Review and the audit.
/// </summary>
static class PlaythroughCommands
{
    public static object Run(string content, string[] args)
    {
        string spec = Str(args, "--route", "frontier:7");
        int width = (int)Opt(args, "--width", 960), height = (int)Opt(args, "--height", 540), cars = (int)Opt(args, "--cars", 6);
        double minutes = Opt(args, "--minutes", 20), every = Opt(args, "--every", 90), gap = Opt(args, "--gap", 2);
        string dir = Str(args, "--out", "out/playthrough");
        if (Directory.Exists(dir))
            Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);

        var route = DarkTerritory.Sim.LineGen.Routes.Generate(content, spec, cars);
        var session = new PrototypeSession(content, route, cars, enemies: true);
        var train = session.Train;
        using var gpu = new GpuContext("dt playthrough");
        using var renderer = new GreyboxRenderer(gpu, width, height);
        var look = Look.Load(content);
        look.Dress(renderer);
        var mesh = new MeshBuilder();
        var overlay = new Overlay();
        var shots = new List<object>();
        var seen = new HashSet<(int, SpinePhase)>();
        double lastShot = double.NegativeInfinity, lastChase = 0;
        var watch = Stopwatch.StartNew();
        int ticks = (int)(minutes * 60 * SimConstants.TickRate);

        void Shoot(string name, Camera camera, string what)
        {
            double seconds = session.World.Tick * SimConstants.TickSeconds;
            var lighting = Views.Lighting(train, look);
            lighting.FogDensity = (float)route.Weather.FogDensity;
            lighting.Frost = look.Tuning.Atmosphere.Cold.Frost(route.Weather.Cold);
            var p = session.Player;
            new GreyboxScene
            {
                Look = look,
                Route = route,
                Signs = session.World.Lineside?.Signs,
                SignRange = session.World.Lineside?.Tuning.LampSignRange ?? 350,
                Enemies = session.World.ActiveEnemies,
                Hits = session.World.Hits,
                Impacts = session.World.Impacts,
                Run = session.World.Run,
                Holdouts = session.World.Holdouts,
                Vehicles = train.Vehicles,
                Bodies = session.World.Bodies.All,
                Diverging = train.Diverging,
                Stands = session.World.Switches,
                Time = seconds,
                Tick = session.World.Tick,
                Controls = session.Controls,
                Crew = [new Crewmate(1, PlayerMotor.WorldPosition(p, train), PlayerMotor.WorldYaw(p, train), p.Alive)],
            }.Build(mesh, train, camera.Position);
            overlay.Clear();
            var pixels = renderer.Render(mesh, camera, lighting, lighting.FogColor, overlay);
            string file = $"{shots.Count:000}-{name}.png";
            PngWriter.Write(Path.Combine(dir, file), pixels, width, height, 1);
            var engine = train.Frames[0].Origin;
            shots.Add(new
            {
                file,
                seconds = Math.Round(seconds, 1),
                km = Math.Round(train.Dynamics.Distance / 1000, 2),
                what,
                engine = new[] { Math.Round(engine.X), Math.Round(engine.Y), Math.Round(engine.Z) },
                camera = new[] { Math.Round(camera.Position.X), Math.Round(camera.Position.Y), Math.Round(camera.Position.Z) }
            });
            lastShot = seconds;
        }

        for (int t = 0; t < ticks && session.World.Run?.Over != true; t++)
        {
            if (route.Plan is { } plan)
                DarkTerritory.Game.LineGen.Ride.Drive(train, plan, ref session.Controls);
            session.Step(default);
            double now = session.World.Tick * SimConstants.TickSeconds;
            // Each enemy as it arrives and at each beat of its spine that a crew would be watching for.
            foreach (var e in session.World.EnemyEvents)
            {
                if (e.To is not (SpinePhase.Alert or SpinePhase.Telegraph or SpinePhase.Grab or SpinePhase.Punish) || now - lastShot < gap)
                    continue;
                if (!seen.Add((e.EnemyId, e.To)))
                    continue;
                var enemy = session.World.ActiveEnemies.FirstOrDefault(x => x.Id == e.EnemyId);
                if (enemy is null || enemy.Gone)
                    continue;
                Shoot($"{e.Kind}-{e.To}".ToLowerInvariant(), Beside(train, enemy), $"{e.Kind} {e.From} -> {e.To}");
            }
            if (now - lastChase >= every)
            {
                lastChase = now;
                Shoot("line", Views.Get("chase", train, 2), "the line");
            }
        }
        string index = Path.Combine(dir, "index.json");
        File.WriteAllText(index, System.Text.Json.JsonSerializer.Serialize(shots, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        return new
        {
            route = spec,
            seconds = Math.Round(session.World.Tick * SimConstants.TickSeconds),
            km = Math.Round(train.Dynamics.Distance / 1000, 2),
            over = session.World.Run?.Over ?? false,
            shots = shots.Count,
            index = Path.GetFullPath(index),
            renderSeconds = Math.Round(watch.Elapsed.TotalSeconds),
        };
    }

    /// <summary>
    /// Where a crewmate would see it from: down the aisle at it if it's inside a car (the car's room), else from beside it
    /// off the line, a little behind and above, the train in the shot.
    /// </summary>
    static Camera Beside(TrainOnLine train, Enemy e)
    {
        var at = e.WorldPosition(train);
        if (e.Attached >= 0 && e.Attached < train.Frames.Count && train.Frames[e.Attached].Shape.Interior is { } room
            && e.Local.X >= room.Min.X && e.Local.X <= room.Max.X && e.Local.Z >= room.Min.Z && e.Local.Z <= room.Max.Z
            && e.Local.Y >= room.Min.Y - 0.2 && e.Local.Y <= room.Max.Y)
        {
            var frame = train.Frames[e.Attached];
            double z = e.Local.Z < room.Centre.Z ? Math.Min(room.Max.Z - 0.4, e.Local.Z + 3.5) : Math.Max(room.Min.Z + 0.4, e.Local.Z - 3.5);
            var eye = frame.ToWorld(new Double3(0.2, room.Min.Y + 1.6, z));
            return Camera.LookAt(eye, frame.ToWorld(e.Local + Double3.Up * 0.8), 70);
        }
        double hint = train.Dynamics.Distance;
        train.Line.Nearest(at, ref hint);
        var line = train.Line.Sample(hint);
        var side = Double3.Cross(line.Tangent, Double3.Up).Normalized;
        // Off the side it's on (or the right, if it's on the line), so the train isn't between.
        double lateral = Double3.Dot(at - line.Position, side);
        var away = lateral < -0.5 ? side * -1 : side;
        var from = at + away * 7 + Double3.Up * 2.8 - line.Tangent * 5;
        return Camera.LookAt(from, at + Double3.Up * 1.0, 62);
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
