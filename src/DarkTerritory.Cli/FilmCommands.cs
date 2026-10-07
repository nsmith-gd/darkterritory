using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Ballast;
using Ballast.Audio;
using Ballast.Render;
using DarkTerritory.Game;
using DarkTerritory.Game.Art;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

/// <summary>
/// `dt film` (ARCHITECTURE §8 note 251): the whole derailment as a player sees and hears it (GDD v1.4 App. E; notes 170,
/// 174, 177), as a video. A hosted night with --crew aboard (the rest bots) is derailed once, then stepped tick by tick
/// through the first person, the chase-view replay, the film's cut and the cause card. Every frame is drawn at --fps the way
/// the app draws it (the beat's pick is <see cref="DerailSequence.Show"/>, the app's own; the cards are the HUD's) to
/// --out/frame_NNNN.png; the sound is the game's own mixer, offline, driven tick by tick as the app drives it (the opera
/// the host drew, the wreck's crash and grind, the game muffled under the music) to --out/derailment.wav; and both are
/// encoded with ffmpeg (on the PATH, --ffmpeg, or the one Python's imageio-ffmpeg bundles) to --out/derailment.mp4, with
/// a contact sheet beside it.
/// </summary>
static class FilmCommands
{
    const int UiWidth = 480, UiHeight = 270;

    public static object Run(string content, string[] args)
    {
        int crew = (int)Opt(args, "--crew", 8), cars = (int)Opt(args, "--cars", 6), fps = (int)Opt(args, "--fps", 24);
        int width = (int)Opt(args, "--width", 1280), height = (int)Opt(args, "--height", 720);
        double speed = Opt(args, "--speed", 20);
        string route = Str(args, "--route", "frontier:7"), dir = Str(args, "--out", "out/film");
        Directory.CreateDirectory(dir);
        foreach (var old in Directory.GetFiles(dir, "frame_*.png"))
            File.Delete(old);
        var watch = Stopwatch.StartNew();
        // The host's own name on its card (the GDD's example crewman), not the machine's login.
        NetPlaySession.PlayerName = Str(args, "--name", "Dave");
        using var session = NetPlaySession.HostGame(content, new SessionSetup(Route: route, Cars: cars, Enemies: false), port: 0, bots: Math.Max(0, crew - 1));
        var hostWorld = session.Host!.World;
        // --track id: that track, instead of the host's draw from a fresh bag.
        if (Str(args, "--track", "") is { Length: > 0 } wanted)
            hostWorld.Music = new DarkTerritory.Sim.Music.MusicRotation([.. DarkTerritory.Sim.Music.MusicManifest.Load(content).Tracks.Where(t => t.Id == wanted)], hostWorld.WreckTuning.Music);
        // The sequence's timing is the host player's: their own first person, up to their own death once the film's shot
        // (App. E.2 step 1, the director's decision of 5 Oct 2026; IPlaySession.SequenceTuning), so it's read afresh each tick.
        var t = session.SequenceTuning;
        var sequence = new DerailSequence();
        var audio = new GameAudio(content);
        var mix = new List<float>();
        var block = new float[Audio.Block * 2];
        long rendered = 0;
        Camera camera = default;
        double tick = SimConstants.TickSeconds;
        bool seen = false;

        // One sim tick, as the app's loop has it: the session stepped, what was drawn kept for the replay, the audio updated
        // from where the camera is and mixed on to keep time (kept only from the frame the train's seen to come off).
        void Step()
        {
            session.Step(default);
            Thread.Sleep(1);
            t = session.SequenceTuning;
            var frames = session.InterpolatedFrames(1);
            if (!seen)
                camera = session.EyeCamera(frames, 1, 0, 0);
            sequence.Record(session.Tick * tick, frames, session.Crew(frames, 1), session.World.Derailed, camera,
                session.Player.Parent >= 0 ? session.Player.Parent : -1, t);
            bool wrecking = session.WreckCinematic && session.Train.Wreck is not null;
            var film = session.Film;
            audio.Music(session.World.DerailMusic, wrecking ? session.WreckSeconds : -1, t,
                film is null ? -1 : t.FirstPersonSeconds + t.ReplaySeconds + film.CauseAt);
            audio.Film(film, wrecking && film is not null && DerailSequence.Beat(t, session.WreckSeconds, film) == DerailBeat.Film
                ? film.CutAt(DerailSequence.FilmSeconds(t, session.WreckSeconds)) : null);
            var ears = session.Viewpoint;
            audio.Update(session.World, session.Controls, Listener.At(camera.Position, camera.Yaw), !PlayerMotor.Indoors(ears, session.Train), tick,
                PlayerMotor.Space(ears, session.Train));
            if (!seen && session.Train.Wreck is not null)
            {
                seen = true;
                rendered = 0;
            }
            long due = seen ? (long)Math.Ceiling((session.WreckSeconds + tick) * Audio.SampleRate) : rendered + (long)(tick * Audio.SampleRate);
            while (rendered < due)
            {
                audio.Mixer.Render(block);
                if (seen)
                    mix.AddRange(block);
                rendered += Audio.Block;
            }
        }

        // The night under way for --seconds (40), held at --speed so it's out of the fortress's yard and running when it
        // comes off (the replay shows the last few seconds of it), then off the rails.
        for (int i = 0; i < Opt(args, "--seconds", 40) * SimConstants.TickRate; i++)
        {
            hostWorld.Train.Dynamics.Velocity = Math.Min(speed, hostWorld.Train.Dynamics.Velocity + 2 * SimConstants.TickSeconds);
            Step();
        }
        hostWorld.Train.Dynamics.Velocity = speed;
        hostWorld.Train.RefreshFrames();
        // --gunner: the last of the crew sat in a gun's seat (T112) as it comes off, to see them thrown out of it.
        if (args.Contains("--gunner"))
            SeatAGunner(session);
        int kmh = (int)Math.Round(speed * 3.6);
        hostWorld.Derail(Str(args, "--cause", $"took the 45 km/h bend at {kmh} km/h, {Math.Max(0, kmh - 45)} km/h too fast"));
        var clock = Stopwatch.StartNew();
        while (session.Train.Wreck is null && clock.Elapsed.TotalSeconds < 30)
            Step();

        using var gpu = new GpuContext("dt film");
        using var renderer = new GreyboxRenderer(gpu, width, height) { OverlaySize = new Vector2(UiWidth, UiHeight) };
        var look = Look.Load(content);
        look.Dress(renderer);
        var mesh = new MeshBuilder();
        var overlay = new Overlay();
        var scene = new GreyboxScene
        {
            Look = look,
            Route = session.Route,
            Signs = session.World.Lineside?.Signs,
            SignRange = session.World.Lineside?.Tuning.LampSignRange ?? 350,
            Enemies = session.World.ActiveEnemies,
            Hits = session.World.Hits,
            Swings = session.World.Swings,
            Impacts = session.World.Impacts,
            Run = session.World.Run,
            Holdouts = session.World.Holdouts,
            Vehicles = session.Train.Vehicles,
            Handrails = session.Train.Dynamics.Tuning.Composition.Handrails,
            Diverging = session.Train.Diverging,
            Stands = session.World.Switches,
        };
        // E.4 O6 and E.1's slapstick, measured on every frame of a player's shot: their chest in the central 70% of the
        // frame, their height as a share of the frame's, off what's under them, and tipped from upright.
        var framing = new Dictionary<int, List<(bool Central, double Height, double Clear, double Tilt)>>();
        double hint = session.Train.Dynamics.Distance;
        double Ground(double x, double z) => PlayerMotor.GroundAt(new Double3(x, 0, z), session.Train.Line, ref hint);
        int frame = 0;
        WreckFilm? shot = null;
        for (; shot is null || frame < Math.Ceiling(DerailSequence.Length(t, shot) * fps - 1e-9); frame++)
        {
            t = session.SequenceTuning;
            double at = frame / (double)fps;
            while (session.WreckSeconds + 1e-9 < at)
                Step();
            // The film's shot off the frame loop: past the replay, wait for it rather than draw the orbit that stands in.
            clock.Restart();
            while (session.Film is null && at >= t.FirstPersonSeconds + t.ReplaySeconds && clock.Elapsed.TotalSeconds < 120)
                Thread.Sleep(5);
            shot = session.Film;
            if (shot is null && at >= t.FirstPersonSeconds + t.ReplaySeconds)
                return new { error = "no film was shot", wreckSeconds = session.WreckSeconds };
            // Between ticks, as the app's clock has it: the frame lands this far past the tick before.
            double alpha = Math.Clamp(1 - (session.WreckSeconds - at) / tick, 0, 1);
            var live = session.InterpolatedFrames(alpha);
            var shown = sequence.Show(session, live);
            camera = shown.Camera ?? session.EyeCamera(live, alpha, 0, 0);
            var frames = shown.Frames;
            DerailSequence.Dress(scene, shown, session, camera.Position, shown.Replay is null && shown.Filming is null ? session.Crew(frames, alpha) : []);
            Dress(scene, session, camera, at, frames);
            var lighting = Lighting(session, look, frames);
            DerailSequence.Fog(ref lighting, shown);
            scene.Build(mesh, session.Train.Line, frames, session.Train.Dynamics.Distance, camera.Position);
            overlay.Clear();
            Hud.Build(overlay, UiWidth, UiHeight, session, pixels: (float)height / UiHeight);
            var pixels = renderer.Render(mesh, camera, lighting, lighting.FogColor, overlay);
            PngWriter.Write(Path.Combine(dir, $"frame_{frame:0000}.png"), pixels, width, height, 1);
            if (shown.Filming is { Shot: { Kind: ShotKind.Player } subjectShot } && shown.Film is { } shooting
                && scene.Bodies?.FirstOrDefault(b => b.Owner == subjectShot.Subject) is { } body)
            {
                int doll = shooting.Start.Players.ToList().FindIndex(p => p.Id == subjectShot.Subject);
                var vp = camera.ViewProjection(width / (float)height);
                var screen = body.Pbd.Particles.Select(j =>
                {
                    var r = j.Position - camera.Position;
                    var c = Vector4.Transform(new Vector4((float)r.X, (float)r.Y, (float)r.Z, 1), vp);
                    return c.W > 0 ? new Vector2(c.X / c.W, c.Y / c.W) : new Vector2(float.NaN);
                }).ToArray();
                var chest = screen[1];
                double tall = screen.All(v => float.IsFinite(v.Y)) ? (screen.Max(v => v.Y) - screen.Min(v => v.Y)) / 2 : 0;
                var spine = body.Pbd.Particles[0].Position - body.Pbd.Particles[2].Position;
                double tilt = Math.Acos(Math.Clamp(spine.Normalized.Y, -1, 1)) * 180 / Math.PI;
                (framing.TryGetValue(subjectShot.Subject, out var list) ? list : framing[subjectShot.Subject] = []).Add(
                    (Math.Abs(chest.X) <= 0.7 && Math.Abs(chest.Y) <= 0.7, tall,
                        shooting.Clearance((int)Math.Round(shown.FilmAt * WreckFilm.Rate), doll, Ground), tilt));
            }
        }

        var film = shot!;
        string wav = Path.Combine(dir, "derailment.wav");
        int samples = (int)Math.Min(mix.Count, Math.Round(frame / (double)fps * Audio.SampleRate) * 2);
        Wav.Write(wav, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(mix)[..samples]);
        string spectrogram = Path.ChangeExtension(wav, ".png");
        PngWriter.Write(spectrogram, Spectrogram.Render([.. mix.Take(samples)], 1200, 300), 1200, 300, 1);
        var track = audio.Opera.Manifest.ByKey(session.World.DerailMusic);
        var (video, sheet, encodeError) = Encode(args, dir, fps, frame);
        return new
        {
            frames = frame,
            fps,
            seconds = Math.Round(frame / (double)fps, 2),
            width,
            height,
            dir = Path.GetFullPath(dir),
            wav = Path.GetFullPath(wav),
            spectrogram = Path.GetFullPath(spectrogram),
            video = video is null ? null : Path.GetFullPath(video),
            videoBytes = video is null ? 0 : new FileInfo(video).Length,
            contactSheet = sheet is null ? null : Path.GetFullPath(sheet),
            encodeError,
            shots = DerailSequence.Timeline(t, film).Select(b => new
            {
                beat = b.Beat.ToString(),
                kind = b.Shot?.Kind.ToString(),
                subject = b.Shot?.Subject,
                card = b.Shot?.Card,
                from = Math.Round(b.From, 2),
                to = Math.Round(b.To, 2),
                firstFrame = (int)Math.Ceiling(b.From * fps - 1e-9),
                recorded = b.Shot is { } s ? new[] { Math.Round(s.From, 2), Math.Round(s.To, 2) } : null,
                // The camera: how far from what it frames, and how steeply down on it (O10's fallback is 70 degrees at 12 m).
                distance = b.Shot is { } d ? Math.Round((d.Camera - d.Look).Length, 1) : (double?)null,
                elevation = b.Shot is { } e ? Math.Round(Math.Asin(Math.Clamp((e.Camera - e.Look).Normalized.Y, -1, 1)) * 180 / Math.PI) : (double?)null,
            }),
            music = track is null ? null : new
            {
                track = track.Id,
                track.Work,
                mood = track.Mood.ToString(),
                // The clip's in-point: where in the file it starts, on the replay's first frame (note 174).
                inPoint = Math.Round(track.StartFor(t.ReplayLeadSeconds), 3),
                startsAt = t.FirstPersonSeconds,
                hitAt = Math.Round(t.FirstPersonSeconds + track.Hit - track.StartFor(t.ReplayLeadSeconds), 3),
                fadedBy = Math.Round(t.FirstPersonSeconds + t.ReplaySeconds + film.CauseAt, 3),
                runsOutAt = Math.Round(t.FirstPersonSeconds + track.OutPoint - track.StartFor(t.ReplayLeadSeconds), 3),
            },
            crew = film.Start.Players.Select((p, doll) => new
            {
                p.Id,
                p.Name,
                p.Role,
                p.Seated,
                // App. E.2 step 1 (the director's decision of 5 Oct 2026): how they died in the film, when (recorded seconds),
                // how hard (m/s), and how long their own first person runs (to a little past it).
                death = new { at = Math.Round(film.Deaths[p.Id].At, 2), kind = film.Deaths[p.Id].Kind.ToString(), speed = Math.Round(film.Deaths[p.Id].Speed, 1), survived = film.Deaths[p.Id].Survived },
                firstPerson = Math.Round(film.FirstPersonOf(p.Id) ?? 0, 2),
                landings = film.Frames.Sum(f => f.Landings.Count(l => l.Doll == doll)),
                // As the film starts them: inside which car (−1 none), how fast, and how far over they ever go (degrees).
                inside = p.Inside,
                speedAtDerail = Math.Round(p.Velocity.Length, 1),
                tumble = Math.Round(film.Frames.Max(f => Math.Acos(Math.Clamp((f.Ragdolls[doll][0] - f.Ragdolls[doll][2]).Normalized.Y, -1, 1)) * 180 / Math.PI)),
                peak = Math.Round(film.Peaks[p.Id].Score, 2),
                peakAt = Math.Round(film.Peaks[p.Id].At, 2),
                // Over their own shot: how much of it they're in the middle of the frame (O6 asks 80%), how tall in it (O6:
                // 25-60%), how high off what's under them, how much of it in the air, and how far over they're tipped.
                shot = framing.TryGetValue(p.Id, out var f) ? new
                {
                    central = Math.Round(f.Count(x => x.Central) / (double)f.Count, 2),
                    height = new[] { Math.Round(f.Min(x => x.Height), 2), Math.Round(f.Max(x => x.Height), 2) },
                    maxClear = Math.Round(f.Max(x => x.Clear), 2),
                    airborne = Math.Round(f.Count(x => x.Clear > WreckFilm.AirborneAbove) / (double)f.Count, 2),
                    maxTilt = Math.Round(f.Max(x => x.Tilt)),
                } : null,
            }),
            renderSeconds = Math.Round(watch.Elapsed.TotalSeconds),
        };
    }

    /// <summary>
    /// Sits the last of the crew in the gun's seat of the first car with a gun (T112), the way the seat has them (on the
    /// carriage behind the breech, facing along the gun), and steps the night once so the host has them there.
    /// </summary>
    static void SeatAGunner(NetPlaySession session)
    {
        var host = session.Host!;
        var train = host.World.Train;
        int gun = Enumerable.Range(0, train.Vehicles.Count).FirstOrDefault(v => train.Vehicles[v].HasGun && DarkTerritory.Sim.Combat.Guns.Mount(train, v) is not null, -1);
        var last = host.Players.OrderBy(p => p.Id).LastOrDefault();
        if (gun < 0 || last.State.Health <= 0)
            return;
        var mount = DarkTerritory.Sim.Combat.Guns.Mount(train, gun)!.Value;
        var g = train.Vehicles[gun].Gun;
        var seat = DarkTerritory.Sim.Combat.Guns.SeatAt(mount, g, host.World.Combat!.Guns, mount.Position.Y - 0.9);
        host.SetPlayerState(last.Id, last.State with
        {
            Parent = gun,
            Position = seat,
            Velocity = Double3.Zero,
            Yaw = DarkTerritory.Sim.Combat.Guns.FacingYaw(mount) + g.Traverse,
            Surface = Surface.Roof,
            Flags = last.State.Flags | PlayerFlags.Seated,
        });
        session.Step(default);
    }

    /// <summary>What the app's loop sets on the scene each frame from the night's state, for a frame of the derailment.</summary>
    static void Dress(GreyboxScene scene, NetPlaySession session, Camera camera, double at, IReadOnlyList<CarFrame> frames)
    {
        var train = session.Train;
        var me = session.Player;
        scene.Own = !me.Alive || session.Watching >= 0 || session.WreckCinematic ? null
            : new OwnView((float)camera.Yaw, (float)camera.Pitch, CrewActs.Of(me, session.PlayerId, session.World), false, -1, session.PlayerId, DarkTerritory.Sim.Player.Kit.Held(me));
        scene.HeldHere = null;
        scene.Time = session.Tick * SimConstants.TickSeconds;
        scene.LampsOut = 0;
        scene.LampRange = 60;
        scene.FireGlow = train.BoilerTuning is { } bt ? GreyboxScene.FireLook(train.Boiler.Firebox, bt.FireboxCapacity) : 0.7f;
        scene.WrenchRacked = !train.Boiler.WrenchOut;
        scene.CordPulled = CrewActs.CrewWhistling(session.World);
        scene.Cut = SceneArt.Cuts(train);
        scene.FireDoorOpen = train.Boiler.FireDoorOpen;
        scene.SinceShovel = train.Boiler.SinceShovel;
        scene.ChoirGathering = session.World.Choir.Present ? 1 : (float)session.World.Choir.Build;
        scene.Tick = session.HostTick;
        scene.Pressure = (float)(train.BoilerTuning is { } pt ? train.Boiler.Pressure / pt.PressureMax : 0.78);
        scene.Tender = (float)(train.BoilerTuning is { TenderCapacity: > 0 } tt ? Math.Clamp(train.Boiler.Tender / tt.TenderCapacity, 0, 1) : 0.72);
        scene.LampLit = session.World.LampShining;
        scene.Venting = train.Boiler.Vented;
        scene.SafetyValve = train.Boiler.SafetyValveLifting;
        scene.Ruptured = train.Boiler.Ruptured;
        scene.DriversLocked = scene.Ruptured && train.BoilerTuning is { } rt && train.Dynamics.Speed > rt.RuptureCoastBelow;
        scene.Controls = session.Controls;
        if (session.Watching >= 0)
            scene.Crew = [.. (scene.Crew ?? []).Where(c => c.Id != session.Watching)];
    }

    /// <summary>The app's lighting for the frame: the engine's lamp, the night's weather, dawn if it's coming.</summary>
    static FrameLighting Lighting(NetPlaySession session, Look look, IReadOnlyList<CarFrame> frames)
    {
        var lighting = Views.Lighting(frames[0], look, session.World.Run is { } run ? look.DawnOf(run.DawnIn) : 0);
        lighting.Time = session.Tick * SimConstants.TickSeconds;
        if (!session.World.LampShining)
        {
            lighting.LampIntensity = 0;
            lighting.LampRange = 0.01f;
        }
        if (session.Route is { } r)
        {
            lighting.FogDensity = (float)r.Weather.FogDensity;
            lighting.Wetness = r.Weather.Wet ? 1 : 0;
            lighting.Frost = look.Tuning.Atmosphere.Cold.Frost(r.Weather.Cold);
            if (look.Tuning.Atmosphere.Wind is { } wind)
                (lighting.Wind, lighting.Gusts) = (wind.Of(r.Weather.Wind), wind.Gusts);
        }
        return lighting;
    }

    /// <summary>
    /// H.264 and AAC (CRF --crf, 23), and a contact sheet of 16 frames spread over it, with whichever ffmpeg there is:
    /// --ffmpeg, then the PATH's, then the static one Python's imageio-ffmpeg bundles (`pip install imageio-ffmpeg`).
    /// </summary>
    static (string? Video, string? Sheet, string? Error) Encode(string[] args, string dir, int fps, int frames)
    {
        var ffmpeg = Str(args, "--ffmpeg", "") is { Length: > 0 } given ? given : FindFfmpeg();
        if (ffmpeg is null)
            return (null, null, "no ffmpeg: pip install imageio-ffmpeg, or pass --ffmpeg");
        string mp4 = Path.Combine(dir, "derailment.mp4"), sheet = Path.Combine(dir, "contact.png");
        int crf = (int)Opt(args, "--crf", 23);
        string frameGlob = Path.Combine(dir, "frame_%04d.png"), wav = Path.Combine(dir, "derailment.wav");
        var (ok, error) = Ffmpeg(ffmpeg, ["-y", "-loglevel", "error", "-framerate", $"{fps}", "-i", frameGlob, "-i", wav, "-c:v", "libx264", "-preset", "slow",
            "-crf", $"{crf}", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "160k", "-shortest", "-movflags", "+faststart", mp4]);
        if (!ok)
            return (null, null, error);
        int every = Math.Max(1, frames / 16);
        Ffmpeg(ffmpeg, ["-y", "-loglevel", "error", "-i", frameGlob, "-vf", $"select=not(mod(n\\,{every})),scale=320:180,tile=4x4", "-frames:v", "1", sheet]);
        return (mp4, File.Exists(sheet) ? sheet : null, null);
    }

    internal static (bool Ok, string Error) Ffmpeg(string exe, string[] args)
    {
        var psi = new ProcessStartInfo(exe) { RedirectStandardError = true };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        string error = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode == 0, error);
    }

    internal static string? FindFfmpeg()
    {
        foreach (var d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            if (d.Length > 0 && File.Exists(Path.Combine(d, "ffmpeg")))
                return Path.Combine(d, "ffmpeg");
        try
        {
            var psi = new ProcessStartInfo("python3", ["-c", "import imageio_ffmpeg; print(imageio_ffmpeg.get_ffmpeg_exe())"]) { RedirectStandardOutput = true, RedirectStandardError = true };
            using var p = Process.Start(psi)!;
            string path = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit();
            return p.ExitCode == 0 && File.Exists(path) ? path : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
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
