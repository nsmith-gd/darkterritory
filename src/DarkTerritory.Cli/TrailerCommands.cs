using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Ballast;
using Ballast.Audio;
using Ballast.Render;
using DarkTerritory.Game;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

/// <summary>
/// `dt trailer`: the game's trailer at L1 (the art checklist's "trailer"; Steam's Next Fest pulls one, GDD §36), cut from
/// the game itself, headless and repeatable. A solo night on the open line (or a generated one, --route) is warmed up to
/// speed, then run on in real time while the cameras cut between shots: the train coming out of the fog, its roofs, the
/// hounds running beside it, the Switchman at his lever with the lamp coming on, something in a car's aisle, a Car Hugger on
/// the rear car, the Choir over the guard van, the cannon, the dawn. Title cards come between on the game's own nameboard
/// (Game/UiStyle). The creatures are staged as the screenshots stage them (Staging), and go on with what they're doing.
/// Each frame is rendered as the screenshots are, to out/trailer/frames. The sound is the game's own mixer, offline, heard
/// from each shot's camera tick by tick as the app hears it: the train, the beat's creatures' tells, the guns (queue #46).
/// It's written to out/trailer/trailer.wav with its spectrogram. Both are encoded with ffmpeg (--ffmpeg, the PATH's, or
/// imageio-ffmpeg's) to out/trailer/trailer.mp4, with a contact sheet beside it.
/// </summary>
static class TrailerCommands
{
    /// <summary>
    /// A beat of the trailer: how long, and what's seen (a card's lines, or a shot's camera and staging). Stage names what's
    /// staged; Heard, which of the staged the mixer hears (the rest are seen but kept quiet, so the beat's own tell isn't
    /// lost under every other's); Alone, only those drawn too. Fixed: the staging and the camera are taken once, as the
    /// beat starts, and stay where they are while the train comes on (the Switchman's lever is by the line, not on the train).
    /// </summary>
    sealed record Beat(double Seconds, string[]? Card = null, Func<TrainOnLine, double, Camera>? Shot = null, string Stage = "", float Dawn = 0,
        EnemyKind[]? Heard = null, bool Alone = false, bool Fixed = false);

    public static object Run(string content, string[] args)
    {
        // The open line by default (a generated night starts in its fortress's yard, walls and platform all round);
        // --route tier:seed for a generated one, warmed up longer to get out of the yard.
        string spec = Str(args, "--route", "");
        int fps = (int)Opt(args, "--fps", 24), width = (int)Opt(args, "--width", 1280), height = (int)Opt(args, "--height", 720), cars = (int)Opt(args, "--cars", 6);
        // --short: every beat a third as long (a quick look at the cut).
        double pace = args.Contains("--short") ? 1 / 3.0 : 1;
        // --beats i,j: only those beats (by their place in the cut, from 0), for looking at a shot without the rest.
        var only = Str(args, "--beats", "") is { Length: > 0 } picked ? picked.Split(',').Select(int.Parse).ToHashSet() : null;
        string dir = Str(args, "--out", "out/trailer");
        string frames = Path.Combine(dir, "frames");
        if (Directory.Exists(frames))
            Directory.Delete(frames, true);
        Directory.CreateDirectory(frames);

        var route = spec.Length > 0 ? DarkTerritory.Sim.LineGen.Routes.Generate(content, spec, cars) : null;
        var session = route is not null ? new PrototypeSession(content, route, cars, enemies: false) : new PrototypeSession(content, Str(args, "--line", "test-loop"), cars);
        var train = session.Train;
        var world = session.World;
        var combat = DataFile.Load<CombatTuning>(Path.Combine(content, CombatTuning.File));
        // Out of the yard and up to speed before anything's seen (a steam-driven session starts with its brake on).
        session.Controls.Throttle = 0.85;
        session.Controls.Brake = 0;
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
            // Its roofs, from beside car 2: the crew at work.
            new(4.5, Shot: (t, _) => Views.Get("crewside", t, 2), Stage: "crew"),
            new(2.5, ["SOMETHING RUNS BESIDE IT"]),
            // Behind the train, riding with the pack: the Cinder Hounds running it down, the guard van's lamp ahead of them.
            new(4, Shot: (t, _) => Camera.LookAt(Line(t, t.Dynamics.RearDistance - 15, -1.3, 0.75), Line(t, t.Dynamics.RearDistance - 3, 0.3, 1.15), 56),
                Stage: "hounds", Heard: [EnemyKind.CinderHound]),
            // Up the line, the Switchman at his lever, his hand on it, and the train's lamp coming at him out of the fog.
            new(4.5, Shot: (t, _) => Camera.LookAt(Line(t, t.Dynamics.Distance + 66, 5.5, 1.5), Line(t, t.Dynamics.Distance + 52, 2.6, 1.3), 44),
                Stage: "switchman", Heard: [EnemyKind.Switchman], Alone: true, Fixed: true),
            new(2.5, ["SOMETHING IS ABOARD"]),
            // A car's aisle, and what's tiptoeing down it.
            new(4, Shot: (t, _) => Views.Get("inside", t, 2), Stage: "tippy", Heard: [EnemyKind.TippyToesie]),
            // A Car Hugger on the rear car's end, eating it.
            new(4, Shot: (t, _) => Views.Get("board", t, 2), Stage: "threats", Heard: [EnemyKind.CarHugger]),
            // The Choir's ghosts over the guard van, and the swarm's voices.
            new(4, Shot: (t, _) => Views.Get("choir", t, 2), Stage: "choir", Heard: []),
            // The cannon off the guard van, at the hounds behind.
            new(3.5, Shot: (t, _) => Views.Get("cannon", t, 2), Stage: "cannon", Heard: [EnemyKind.CinderHound]),
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
        // The mixer, offline: every take decoded the moment it's asked for, so a render is the same every time.
        var audio = new GameAudio(content);
        audio.Bank.Samples.InlineBytes = long.MaxValue;
        var mix = new List<float>();
        var block = new float[Audio.Block * 2];
        long rendered = 0, ticks = 0;
        int frame = 0;
        double time = 0;
        var watch = Stopwatch.StartNew();
        var log = new List<object>();
        for (int b = 0; b < beats.Count; b++)
        {
            if (only is not null && !only.Contains(b))
                continue;
            var beat = beats[b];
            int count = (int)Math.Round(beat.Seconds * pace * fps);
            Camera? held = beat.Fixed && beat.Shot is { } shot ? shot(train, 0) : null;
            var staged = beat.Fixed ? Staged(beat, train) : null;
            log.Add(new { beat = b, at = Math.Round(frame / (double)fps, 1), kmh = Math.Round(train.Dynamics.Velocity * 3.6), what = beat.Card?[0] ?? beat.Stage });
            for (int f = 0; f < count; f++, frame++)
            {
                double into = f / (double)fps;
                // What's staged goes on with what it's doing: staged afresh where the train is now (a fixed beat's, once), and
                // that far into it.
                if (!beat.Fixed)
                    staged = Later(Staged(beat, train), into);
                else if (f > 0 && staged is not null)
                    Later(staged, 1.0 / fps);
                Camera camera = beat.Card is not null ? CardCamera(train) : held ?? beat.Shot!(train, into);
                bool fire = beat.Stage == "cannon" && f % fps == fps / 3;
                // The night runs on under the cards and the shots alike, a frame's worth of ticks at a time, and the mixer with it,
                // heard from the shot's camera (under a card, from car 1's roof).
                for (int k = 0; k < SimConstants.TickRate / fps + (frame % fps < SimConstants.TickRate % fps ? 1 : 0); k++)
                {
                    session.Step(default);
                    if (fire && k == 0 && train.Vehicles[^1] is { HasGun: true } gun && Guns.Mount(train, train.Frames.Count - 1) is { } mount)
                    {
                        gun.Gun.LastShotTick = (uint)world.Tick;
                        audio.Play("gunshot", train.Frames[^1].ToWorld(mount.Position));
                    }
                    Hear(beat, beat.Card is null ? camera : null, staged);
                }
                time += 1.0 / fps;
                overlay.Clear();
                var lighting = Views.Lighting(train, look, beat.Dawn);
                if (route is not null)
                {
                    lighting.FogDensity = (float)route.Weather.FogDensity;
                    lighting.Frost = look.Tuning.Atmosphere.Cold.Frost(route.Weather.Cold);
                }
                if (beat.Card is { } lines)
                {
                    // A card: the night's sky and fog behind it, the nameboard and its lines fading in and out.
                    mesh.Clear();
                    float fade = (float)Math.Clamp(Math.Min(into / 0.6, (beat.Seconds * pace - into) / 0.6), 0, 1);
                    Card(overlay, width, height, lines, fade);
                    lighting.MoonStrength *= 0.4f;
                }
                else
                {
                    new GreyboxScene
                    {
                        Look = look,
                        Route = route,
                        Run = world.Run,
                        Holdouts = world.Holdouts,
                        Vehicles = train.Vehicles,
                        Bodies = world.Bodies.All,
                        Time = time,
                        Tick = world.Tick,
                        Controls = session.Controls,
                        Enemies = beat.Alone && staged is not null && beat.Heard is { } kinds ? [.. staged.Where(e => kinds.Contains(e.Kind))] : staged,
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

        // One tick heard: the beat's creatures mirrored into the world for the mixer to find (and taken out again, so the
        // night itself never meets them), the Choir's swarm for its beat, then the mixer run on to keep time with the ticks.
        void Hear(Beat beat, Camera? camera, List<Enemy>? staged)
        {
            var own = world.ActiveEnemies.ToList();
            var choir = world.Choir;
            if (staged is not null && beat.Heard is { } kinds)
                world.MirrorEnemies(staged.Where(e => kinds.Contains(e.Kind)));
            if (beat.Stage == "choir")
                world.Choir = new ChoirState { Present = true, Build = 1, Loudness = combat.Choir.MaxLoudness };
            var roof = train.Frames[Math.Min(1, train.Frames.Count - 1)];
            var (ear, yaw) = camera is { } c ? (c.Position, c.Yaw) : (roof.ToWorld(new Double3(0, roof.Shape.RoofHeight + 1.7, 0)), roof.Heading);
            bool inside = beat.Stage == "tippy";
            audio.Update(world, session.Controls, Listener.At(ear, yaw), exposed: !inside, SimConstants.TickSeconds, inside ? Math.Min(2, train.Frames.Count - 1) : PlayerMotor.Outside);
            world.MirrorEnemies(own);
            world.Choir = choir;
            ticks++;
            long due = (long)Math.Ceiling(ticks * SimConstants.TickSeconds * Audio.SampleRate);
            while (rendered < due)
            {
                audio.Mixer.Render(block);
                mix.AddRange(block);
                rendered += Audio.Block;
            }
        }

        string wav = Path.Combine(dir, "trailer.wav");
        int samples = (int)Math.Min(mix.Count, Math.Round(frame / (double)fps * Audio.SampleRate) * 2);
        Wav.Write(wav, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(mix)[..samples]);
        string spectrogram = Path.ChangeExtension(wav, ".png");
        PngWriter.Write(spectrogram, Spectrogram.Render([.. mix.Take(samples)], 1200, 300), 1200, 300, 1);
        var (video, sheet, error) = Encode(args, dir, frames, fps, frame);
        return new
        {
            frames = frame,
            fps,
            seconds = Math.Round(frame / (double)fps, 1),
            width,
            height,
            beats = log,
            wav = Path.GetFullPath(wav),
            spectrogram = Path.GetFullPath(spectrogram),
            video = video is null ? null : Path.GetFullPath(video),
            contactSheet = sheet is null ? null : Path.GetFullPath(sheet),
            error,
            renderSeconds = Math.Round(watch.Elapsed.TotalSeconds),
        };
    }

    /// <summary>The beat's creatures, staged as the screenshots stage them (dt screenshot --threats and its modes).</summary>
    static List<Enemy>? Staged(Beat beat, TrainOnLine train) => beat.Stage switch
    {
        "" or "crew" => null,
        "tippy" => Staging.Tippy(Staging.Threats(train), train, "in"),
        // His hand on the lever (COMMIT): waiting to throw it under the train.
        "switchman" => Staging.Switchman(Staging.Threats(train), "grip"),
        "hounds" => Pack(Staging.Threats(train), train),
        _ => Staging.Threats(train),
    };

    /// <summary>The staged pack brought in close behind the guard van (the screenshots' stand 14 to 26 m back): a lunge off
    /// its rear end, as a pack runs a train down (App. A.1 COMMIT).</summary>
    static List<Enemy> Pack(List<Enemy> threats, TrainOnLine train)
    {
        int i = 0;
        foreach (var h in threats.OfType<CinderHound>().Where(h => h.Attached < 0))
        {
            h.Restore(h.Phase, h.PhaseSeconds + 0.37 * i, h.Health, h.Attached, h.Local, train.Dynamics.RearDistance - 3.5 - 2.5 * i, (i % 2 == 0 ? 1 : -1) * (1.6 + 0.4 * i),
                h.Height, h.Extra, h.Extra2);
            i++;
        }
        return threats;
    }

    /// <summary>Every staged creature that much further into what it's doing (dt screenshot --later's).</summary>
    static List<Enemy>? Later(List<Enemy>? enemies, double seconds)
    {
        if (enemies is not null && seconds != 0)
            foreach (var e in enemies)
                e.Restore(e.Phase, e.PhaseSeconds + seconds, e.Health, e.Attached, e.Local, e.LineDistance, e.Lateral, e.Height, e.Extra, e.Extra2,
                    e.Holding, e.GrabWindow);
        return enemies;
    }

    /// <summary>Under a card: the night's sky and fog up the line ahead.</summary>
    static Camera CardCamera(TrainOnLine t)
    {
        static Double3 Up(TrainOnLine t, double s, double up) => t.Line.Sample(s).Position + Double3.Up * up;
        return Camera.LookAt(Up(t, t.Dynamics.Distance + 400, 30), Up(t, t.Dynamics.Distance + 900, 34), 60);
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

    /// <summary>
    /// H.264 and AAC (CRF --crf, 20) for Steam's upload, and a contact sheet of 16 frames spread over it (the frames and the
    /// WAV stay either way), with whichever ffmpeg there is (FilmCommands').
    /// </summary>
    static (string? Video, string? Sheet, string? Error) Encode(string[] args, string dir, string frames, int fps, int count)
    {
        var ffmpeg = Str(args, "--ffmpeg", "") is { Length: > 0 } given ? given : FilmCommands.FindFfmpeg();
        if (ffmpeg is null)
            return (null, null, "no ffmpeg: pip install imageio-ffmpeg, or pass --ffmpeg");
        string mp4 = Path.Combine(dir, "trailer.mp4"), sheet = Path.Combine(dir, "contact.png"), glob = Path.Combine(frames, "f%05d.png");
        var (ok, error) = FilmCommands.Ffmpeg(ffmpeg, ["-y", "-loglevel", "error", "-framerate", $"{fps}", "-i", glob, "-i", Path.Combine(dir, "trailer.wav"),
            "-c:v", "libx264", "-preset", "slow", "-crf", $"{(int)Opt(args, "--crf", 20)}", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k", "-shortest",
            "-movflags", "+faststart", mp4]);
        if (!ok)
            return (null, null, error);
        int every = Math.Max(1, count / 16);
        FilmCommands.Ffmpeg(ffmpeg, ["-y", "-loglevel", "error", "-i", glob, "-vf", $"select=not(mod(n\\,{every})),scale=320:180,tile=4x4", "-frames:v", "1", sheet]);
        return (mp4, File.Exists(sheet) ? sheet : null, null);
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
