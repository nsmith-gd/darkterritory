using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

/// <summary>
/// `dt trailer`: the game's trailer at L1 (the art checklist's "trailer"; Steam's Next Fest pulls one, GDD §36), cut from
/// the game itself, headless and repeatable. A solo night on the open line (or a generated one, --route) is warmed up to speed, then run on in
/// real time while the cameras cut between shots (the train coming out of the fog, its roofs, something stalking a car's
/// aisle, the cannon, the dawn), with title cards between on the game's own nameboard (Game/UiStyle). Each frame is
/// rendered as the screenshots are, written to out/trailer/frames, and encoded with ffmpeg (if it's there) to
/// out/trailer/trailer.mp4. Silent for now: the sound is `dt audio render`'s to add.
/// </summary>
static class TrailerCommands
{
    /// <summary>A beat of the trailer: how long, and what's seen (a card's lines, or a shot's camera and staging).</summary>
    sealed record Beat(double Seconds, string[]? Card = null, Func<TrainOnLine, double, Camera>? Shot = null, string Stage = "", float Dawn = 0);

    public static object Run(string content, string[] args)
    {
        // The open line by default (a generated night starts in its fortress's yard, walls and platform all round);
        // --route tier:seed for a generated one, warmed up longer to get out of the yard.
        string spec = Str(args, "--route", "");
        int fps = (int)Opt(args, "--fps", 24), width = (int)Opt(args, "--width", 1280), height = (int)Opt(args, "--height", 720), cars = (int)Opt(args, "--cars", 6);
        // --short: every beat a third as long (a quick look at the cut).
        double pace = args.Contains("--short") ? 1 / 3.0 : 1;
        string dir = Str(args, "--out", "out/trailer");
        string frames = Path.Combine(dir, "frames");
        if (Directory.Exists(frames))
            Directory.Delete(frames, true);
        Directory.CreateDirectory(frames);

        var route = spec.Length > 0 ? DarkTerritory.Sim.LineGen.Routes.Generate(content, spec, cars) : null;
        var session = route is not null ? new PrototypeSession(content, route, cars, enemies: false) : new PrototypeSession(content, Str(args, "--line", "test-loop"), cars);
        var train = session.Train;
        // Out of the yard and up to speed before anything's seen.
        session.Controls.Throttle = 0.85;
        for (int i = 0; i < Opt(args, "--warm", route is null ? 20 : 120) * SimConstants.TickRate; i++)
            session.Step(default);

        static Double3 Line(TrainOnLine t, double s, double lateral, double up)
        {
            var at = t.Line.Sample(s);
            return at.Position + Double3.Cross(at.Tangent, Double3.Up).Normalized * lateral + Double3.Up * up;
        }
        double ahead = train.Dynamics.Distance + 70;
        var beats = new List<Beat>
        {
            new(3.5, ["DARK TERRITORY"]),
            // Out of the fog: a fixed camera low by the line ahead, the engine bearing down on it, its lamp.
            new(6, Shot: (t, _) => Camera.LookAt(Line(t, ahead, -4.5, 1.4), t.Frames[0].ToWorld(new Double3(0, 2.4, 0)), 50)),
            new(2.5, ["THE LAST RAILWAY RUNS AT NIGHT"]),
            // Its roofs, riding with car 2: the crew at work.
            new(5, Shot: (t, _) => Views.Get("roof", t, 2), Stage: "crew"),
            new(2.5, ["SOMETHING IS ABOARD"]),
            // A car's aisle, and what's stalking it.
            new(4.5, Shot: (t, _) => Views.Get("inside", t, 2), Stage: "tippy"),
            // The cannon off the guard van.
            new(4, Shot: (t, _) => Views.Get("cannon", t, 2), Stage: "cannon"),
            new(3, ["KEEP THE FIRE.", "KEEP THE LAMPS LIT.", "BRING THEM HOME."]),
            // The dawn coming up, from beside the line as the train goes by.
            new(5, Shot: (t, s) => Camera.LookAt(t.Frames[0].ToWorld(new Double3(7, 1.6, -6 - s * 2)), t.Frames[1].ToWorld(new Double3(0, 2.4, 0)), 58), Dawn: 0.85f),
            new(4.5, ["DARK TERRITORY", "WISHLIST ON STEAM"]),
        };

        using var gpu = new GpuContext("dt trailer");
        using var renderer = new GreyboxRenderer(gpu, width, height);
        var look = Look.Load(content);
        look.Dress(renderer);
        var mesh = new MeshBuilder();
        var overlay = new Overlay();
        int frame = 0;
        double time = 0;
        var watch = Stopwatch.StartNew();
        foreach (var beat in beats)
        {
            int count = (int)Math.Round(beat.Seconds * pace * fps);
            List<Enemy>? staged = beat.Stage switch
            {
                "tippy" => Staging.Tippy(Staging.Threats(train), train, "in"),
                _ => null,
            };
            for (int f = 0; f < count; f++, frame++)
            {
                double into = f / (double)fps;
                // The night runs on under the cards and the shots alike, a frame's worth of ticks at a time.
                for (int k = 0; k < SimConstants.TickRate / fps + (frame % fps < SimConstants.TickRate % fps ? 1 : 0); k++)
                    session.Step(default);
                time += 1.0 / fps;
                overlay.Clear();
                var lighting = Views.Lighting(train, look, beat.Dawn);
                if (route is not null)
                {
                    lighting.FogDensity = Views.FogDensity(route, train);
                    lighting.Frost = look.Tuning.Atmosphere.Cold.Frost(route.Weather.Cold);
                }
                Camera camera;
                if (beat.Card is { } lines)
                {
                    // A card: the night's sky and fog behind it, the nameboard and its lines fading in and out.
                    camera = Camera.LookAt(Line(train, train.Dynamics.Distance + 400, 0, 30), Line(train, train.Dynamics.Distance + 900, 0, 34), 60);
                    mesh.Clear();
                    float fade = (float)Math.Clamp(Math.Min(into / 0.6, (beat.Seconds * pace - into) / 0.6), 0, 1);
                    Card(overlay, width, height, lines, fade);
                    lighting.MoonStrength *= 0.4f;
                }
                else
                {
                    camera = beat.Shot!(train, into);
                    if (beat.Stage == "cannon" && train.Vehicles[^1] is { HasGun: true } gun && f % fps == fps / 3)
                        gun.Gun.LastShotTick = (uint)session.World.Tick;
                    new GreyboxScene
                    {
                        Look = look,
                        Route = route,
                        Run = session.World.Run,
                        Holdouts = session.World.Holdouts,
                        Vehicles = train.Vehicles,
                        Bodies = session.World.Bodies.All,
                        Time = time,
                        Tick = session.World.Tick,
                        Controls = session.Controls,
                        Enemies = staged,
                        Crew = beat.Stage == "crew" ? Staging.Working(train, content) : null,
                    }.Build(mesh, train, camera.Position);
                    // Into and out of each shot through black.
                    float edge = (float)Math.Clamp(Math.Min(into / 0.35, (beat.Seconds * pace - into) / 0.35), 0, 1);
                    if (edge < 1)
                        overlay.Rect(0, 0, width, height, new Vector4(0, 0, 0, 1 - edge));
                }
                var pixels = renderer.Render(mesh, camera, lighting, lighting.FogColor, overlay);
                PngWriter.Write(Path.Combine(frames, $"f{frame:00000}.png"), pixels, width, height, 1);
            }
        }
        // ffmpeg, if this machine has it: H.264 at the frame rate, for Steam's upload (the frames stay either way).
        string mp4 = Path.Combine(dir, "trailer.mp4");
        bool encoded = false;
        try
        {
            using var ff = Process.Start(new ProcessStartInfo("ffmpeg", $"-y -loglevel error -framerate {fps} -i {Path.Combine(frames, "f%05d.png")} -c:v libx264 -pix_fmt yuv420p -crf 18 {mp4}")
            { RedirectStandardError = true });
            ff!.WaitForExit();
            encoded = ff.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
        return new { frames = frame, fps, seconds = Math.Round(frame / (double)fps, 1), width, height, video = encoded ? Path.GetFullPath(mp4) : null, renderSeconds = Math.Round(watch.Elapsed.TotalSeconds) };
    }

    /// <summary>A title card: the first line on the game's nameboard, the rest under it in the lamp's amber.</summary>
    static void Card(Overlay o, int width, int height, string[] lines, float fade)
    {
        o.Rect(0, 0, width, height, new Vector4(0, 0, 0, 0.55f + 0.35f * fade));
        int scale = Math.Max(2, width / 160);
        if (lines[0] == "DARK TERRITORY")
        {
            float w = o.Font.Measure(lines[0], scale) + 12 * scale;
            float h = UiStyle.Nameboard(o, (width - w) / 2, height * 0.38f, lines[0], scale);
            for (int i = 1; i < lines.Length; i++)
                o.TextCentred(width / 2f, height * 0.38f + h + scale * 6 + (i - 1) * scale * 10, lines[i], new Vector4(1, 0.7f, 0.3f, fade), Math.Max(1, scale / 2));
        }
        else
            for (int i = 0; i < lines.Length; i++)
                o.TextCentred(width / 2f, height * 0.42f + (i - (lines.Length - 1) / 2f) * scale * 9, lines[i], new Vector4(0.88f, 0.84f, 0.74f, fade), Math.Max(1, scale * 2 / 3));
        // Fades through black at either end.
        if (fade < 1)
            o.Rect(0, 0, width, height, new Vector4(0, 0, 0, 1 - fade));
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
