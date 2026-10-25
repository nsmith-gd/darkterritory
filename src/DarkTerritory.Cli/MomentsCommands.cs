using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Ballast;
using Ballast.Render;
using DarkTerritory.Dev.Moments;
using DarkTerritory.Dev.Replay;
using DarkTerritory.Game;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Net;

/// <summary>
/// `dt moments` (queue #256, ARCHITECTURE §8 note 1034; GDD D.15 question 5, "short clips may be worth the tech"): the
/// clip-worthy moments of a night, scored (<see cref="MomentScorer"/>: a death, a rescue, a grab broken late, someone left
/// behind, a derailment) from a recording, and a short reel of the best of them.
/// <list type="bullet">
/// <item><c>dt moments [--replay night.dtrec]</c>: a recorded night (a developer build records every night it hosts), or
/// without one a bot night hosted headless and recorded first (<c>--route frontier:7 --bots 4 --seconds 900 --cars 6</c>),
/// played through and scored: every moment, and the reel's (<c>--top 6</c>) -> out/moments/moments.json.</item>
/// <item><c>--reel</c>: the reel's moments drawn from the recording, each <c>--lead 1.5</c> s before it to <c>--tail 2.5</c>
/// s after, framed on whom it's of (else the chase view) at <c>--fps 8</c>, with a title frame's worth of its words in the
/// JSON -> out/moments/reel/frame_NNNN.png, and with ffmpeg (FilmCommands') reel.mp4 and a contact sheet.</item>
/// </list>
/// </summary>
static class MomentsCommands
{
    public static int Run(string content, string[] args)
    {
        var watch = Stopwatch.StartNew();
        string dir = Str(args, "--out", "out/moments");
        Directory.CreateDirectory(dir);
        var scores = MomentScores.Load(Str(args, "--scores", MomentScores.File));
        string file = Str(args, "--replay", "") is { Length: > 0 } given ? given : Record(content, args, dir);
        var scorer = new MomentScorer(scores);
        uint ticks;
        string night;
        using (var replay = NightReplay.Open(file, content))
        {
            var setup = SessionSetup.Decode(replay.Header.Setup);
            night = $"{setup.Route ?? setup.Line}, {setup.Cars} cars, crew of {replay.Header.Crew}";
            while (replay.Advance())
                scorer.Step(replay.Host.World, replay.Host.Players.Select(p => ((int)p.Id, p.State)));
            ticks = replay.Host.Tick;
        }
        var top = scorer.Top((int)Opt(args, "--top", 6));
        object? reel = args.Contains("--reel") ? Reel(content, args, file, top, Path.Combine(dir, "reel")) : null;
        var result = new
        {
            recording = Path.GetFullPath(file),
            night,
            minutes = Math.Round(ticks * SimConstants.TickSeconds / 60, 1),
            // The trended count: moments a night, and a minute.
            count = scorer.All.Count,
            perMinute = ticks == 0 ? 0 : Math.Round(scorer.All.Count / (ticks * SimConstants.TickSeconds / 60), 2),
            byKind = scorer.All.GroupBy(m => m.Kind).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count()),
            top = top.Select(Line),
            all = scorer.All.Select(Line),
            reel,
            wallSeconds = Math.Round(watch.Elapsed.TotalSeconds, 1),
        };
        File.WriteAllText(Path.Combine(dir, "moments.json"), JsonSerializer.Serialize(result, DataFile.Options));
        Console.WriteLine(JsonSerializer.Serialize(result, DataFile.Options));
        return 0;
    }

    static object Line(Moment m) => new { m.Kind, at = Clock(m.Seconds), m.Tick, score = Math.Round(m.Score, 1), m.Who, m.What, m.Where };

    static string Clock(double seconds) => $"{(int)(seconds / 60)}:{(int)(seconds % 60):00}";

    /// <summary>A bot night hosted headless and recorded (as `dt replay record` does), for scoring: its recording's path.</summary>
    static string Record(string content, string[] args, string dir)
    {
        var recorder = new NightRecorder(Path.Combine(dir, "recordings"), keep: 3);
        using (var night = NetPlaySession.HostGame(content, new SessionSetup(Route: Str(args, "--route", "frontier:7"), Cars: (int)Opt(args, "--cars", 6), Enemies: true),
            port: 0, bots: (int)Opt(args, "--bots", 4), tap: recorder))
        {
            int total = (int)(Opt(args, "--seconds", 900) * SimConstants.TickRate);
            for (int t = 0; t < total && night.World.Run is not { Over: true }; t++)
                night.Step(default);
        }
        return recorder.Current!;
    }

    /// <summary>The reel: each moment's frames drawn from the recording played on to it (in order: one pass), then encoded.</summary>
    static object Reel(string content, string[] args, string file, List<Moment> top, string dir)
    {
        if (Directory.Exists(dir))
            Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        int fps = (int)Opt(args, "--fps", 8), width = (int)Opt(args, "--width", 640), height = (int)Opt(args, "--height", 360);
        double lead = Opt(args, "--lead", 1.5), tail = Opt(args, "--tail", 2.5);
        using var replay = NightReplay.Open(file, content);
        using var shot = new WorldShot(content, replay.Host.World.Route, width, height);
        int frame = 0;
        var cuts = new List<object>();
        uint step = (uint)Math.Max(1, SimConstants.TickRate / fps);
        foreach (var m in top)
        {
            uint from = (uint)Math.Max(0, m.Tick - lead * SimConstants.TickRate), to = m.Tick + (uint)(tail * SimConstants.TickRate);
            // (A moment that overlaps the last's tail starts where that one ended.)
            replay.Run(from);
            int first = frame;
            for (uint t = Math.Max(from, replay.Host.Tick); t <= to && !replay.Finished; t += step)
            {
                replay.Run(t);
                var world = replay.Host.World;
                var crew = replay.Host.Players.ToList();
                // On whom it's of while they're there to frame; else the train from behind.
                Camera camera;
                try
                {
                    camera = WorldShot.Camera(m.Who > 0 && crew.Any(c => c.Id == m.Who) ? $"subject:crew:{m.Who}" : "chase", world, crew, aspect: (double)width / height,
                        player: replay.Host.PlayerTuning);
                }
                catch (ArgumentException)
                {
                    camera = WorldShot.Camera("chase", world, crew);
                }
                shot.Save(Path.Combine(dir, $"frame_{frame++:0000}.png"), shot.Render(world, crew, camera));
            }
            cuts.Add(new { m.Kind, at = Clock(m.Seconds), m.What, frames = $"{first}-{frame - 1}" });
        }
        var (video, sheet, error) = Encode(args, dir, fps, frame);
        return new { dir = Path.GetFullPath(dir), frames = frame, fps, cuts, video, sheet, error };
    }

    /// <summary>The frames as an MP4 (no sound) and a contact sheet, with whichever ffmpeg there is (FilmCommands').</summary>
    static (string? Video, string? Sheet, string? Error) Encode(string[] args, string dir, int fps, int frames)
    {
        if (frames == 0)
            return (null, null, "no frames");
        var ffmpeg = Str(args, "--ffmpeg", "") is { Length: > 0 } given ? given : FilmCommands.FindFfmpeg();
        if (ffmpeg is null)
            return (null, null, "no ffmpeg: pip install imageio-ffmpeg, or pass --ffmpeg (the frames are there)");
        string mp4 = Path.Combine(dir, "reel.mp4"), sheet = Path.Combine(dir, "contact.png"), glob = Path.Combine(dir, "frame_%04d.png");
        var (ok, error) = FilmCommands.Ffmpeg(ffmpeg, ["-y", "-loglevel", "error", "-framerate", $"{fps}", "-i", glob, "-c:v", "libx264", "-crf", "23",
            "-pix_fmt", "yuv420p", "-movflags", "+faststart", mp4]);
        if (!ok)
            return (null, null, error);
        int every = Math.Max(1, frames / 16);
        FilmCommands.Ffmpeg(ffmpeg, ["-y", "-loglevel", "error", "-i", glob, "-vf", $"select=not(mod(n\\,{every})),scale=320:180,tile=4x4", "-frames:v", "1", sheet]);
        return (mp4, File.Exists(sheet) ? sheet : null, null);
    }

    static string Str(string[] args, string name, string fallback)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
    }

    static double Opt(string[] args, string name, double fallback) =>
        Str(args, name, "") is { Length: > 0 } v ? double.Parse(v, CultureInfo.InvariantCulture) : fallback;
}
