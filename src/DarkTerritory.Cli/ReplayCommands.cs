using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Ballast;
using Ballast.Dev;
using DarkTerritory.Dev.Replay;
using DarkTerritory.Game;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Net;

/// <summary>
/// `dt replay` (ARCHITECTURE §8 note 515): recorded nights, made and played again.
/// <list type="bullet">
/// <item><c>dt replay &lt;file&gt; [--to tick|m:ss] [--shot file.png] [--view eye:N|subject:gaunt|chase|roof|…] [--car n]</c>: builds the night
/// from its recording and plays it, checking every tick against what the host sent; reports where it parted (if it did),
/// the marks made in it, and the crew, train and run where it stopped; <c>--shot</c> draws it from a camera there.
/// Exit 1 if it parted from the recording.</item>
/// <item><c>dt replay record [--route frontier:7] [--cars 6] [--bots 4] [--seconds 120] [--no-enemies] [--voice] [--out dir]</c>:
/// a night hosted headless with a bot crew, recorded as a developer build records one, with what recording cost.</item>
/// <item><c>dt replay list [--dir d]</c>: the recordings this machine has (a developer build's, in the app data).</item>
/// </list>
/// </summary>
static class ReplayCommands
{
    public static int Run(string content, string[] args) => args switch
    {
        ["replay", "record", ..] => Record(content, args),
        ["replay", "list", ..] => List(Str(args, "--dir", NightRecorder.DefaultDirectory)),
        ["replay", var file, ..] when File.Exists(file) => Play(content, file, args),
        _ => Usage(),
    };

    static int Usage()
    {
        Console.Error.WriteLine("usage: dt replay <file.dtrec> [--to tick|m:ss] [--shot file.png --view eye:N|subject:<kind|crew:N|car:N|engine>|chase|…] | dt replay record [--route r] [--bots n] [--seconds s] | dt replay list");
        return 2;
    }

    static int Record(string content, string[] args)
    {
        string route = Str(args, "--route", "frontier:7");
        int bots = (int)Opt(args, "--bots", 4), cars = (int)Opt(args, "--cars", 6);
        double seconds = Opt(args, "--seconds", 120);
        var recorder = new NightRecorder(Str(args, "--out", "out/recordings"), voice: args.Contains("--voice"));
        var watch = Stopwatch.StartNew();
        long ticks;
        RunReportLine? end;
        using (var night = NetPlaySession.HostGame(content, new SessionSetup(Route: route, Cars: cars, Enemies: !args.Contains("--no-enemies")),
            port: 0, bots: bots, tap: recorder))
        {
            int total = (int)(seconds * SimConstants.TickRate);
            for (int t = 0; t < total; t++)
                night.Step(default);
            ticks = night.Host!.Tick;
            end = night.World.Run is { } run ? new RunReportLine(run.Phase.ToString(), run.End.ToString(), Math.Round(night.Train.Dynamics.Distance / 1000, 2)) : null;
        }
        double wall = watch.Elapsed.TotalSeconds;
        var (spent, polls, raw) = recorder.Cost;
        long bytes = new FileInfo(recorder.Current!).Length;
        double minutes = ticks / (double)SimConstants.TickRate / 60;
        Print(new
        {
            file = Path.GetFullPath(recorder.Current!),
            route,
            bots,
            ticks,
            polls,
            run = end,
            bytes,
            rawBytes = raw,
            bytesPerMinute = (long)(bytes / Math.Max(minutes, 1e-9)),
            // What recording cost the host's thread: copying what arrived, digesting what went, writing records (the
            // compression is a background thread's). Against the 33 ms a tick has at 30 Hz.
            recorderMicrosPerTick = Math.Round(spent.TotalMilliseconds * 1000 / Math.Max(1, polls), 2),
            wallSeconds = Math.Round(wall, 1),
        });
        return 0;
    }

    sealed record RunReportLine(string Phase, string End, double Km);

    static int List(string dir)
    {
        var files = Directory.Exists(dir)
            ? new DirectoryInfo(dir).GetFiles("*" + TransportLog.Extension).OrderByDescending(f => f.Name, StringComparer.Ordinal).ToList()
            : [];
        Print(new
        {
            dir = Path.GetFullPath(dir),
            recordings = files.Select(f =>
            {
                NightHeader? h = null;
                try
                {
                    h = TransportLogReader.Read(f.FullName).FirstOrDefault() is LogHeader lh ? NightHeader.FromJson(lh.Json) : null;
                }
                catch (Exception e) when (e is InvalidDataException or IOException or JsonException)
                {
                }
                return new { file = f.Name, bytes = f.Length, recordedAt = h?.RecordedAt, commit = h?.Commit, setup = h is null ? null : SessionSetup.Decode(h.Setup) is var s ? $"{s.Route ?? s.Line}, {s.Cars} cars" : null };
            }),
        });
        return 0;
    }

    static int Play(string content, string file, string[] args)
    {
        var watch = Stopwatch.StartNew();
        using var replay = NightReplay.Open(file, content);
        uint? to = Str(args, "--to", "") is { Length: > 0 } at ? NightReplay.TickAt(at) : null;
        replay.Run(to);
        var host = replay.Host;
        var world = host.World;
        var crew = host.Players.ToList();
        string? shot = null;
        ShotCamera? aim = null;
        if (Str(args, "--shot", "") is { Length: > 0 } png)
        {
            int width = (int)Opt(args, "--width", 1280), height = (int)Opt(args, "--height", 720);
            string view = Str(args, "--view", "chase");
            try
            {
                aim = WorldShot.Aim(view, world, crew, (int)Opt(args, "--car", 2), (double)width / height, host.PlayerTuning);
            }
            catch (ArgumentException e)
            {
                // A subject that isn't there (note 524) or a view that isn't one: said, rather than a frame of something else.
                Console.Error.WriteLine($"dt replay: {e.Message}");
                return 2;
            }
            using var shooter = new WorldShot(content, world.Route, width, height);
            shooter.Save(png, shooter.Render(world, crew, aim.Camera, WorldShot.EyeOf(view)));
            shot = Path.GetFullPath(png);
        }
        var (_, commit) = Report.Build();
        var setup = SessionSetup.Decode(replay.Header.Setup);
        Print(new
        {
            file = Path.GetFullPath(file),
            recorded = new { replay.Header.RecordedAt, replay.Header.Commit, night = $"{setup.Route ?? setup.Line}, {setup.Cars} cars", replay.Header.Crew, replay.Header.Voice },
            thisBuild = commit,
            sameBuild = commit == replay.Header.Commit,
            contentDifferences = replay.ContentDifferences,
            steps = replay.Steps,
            tick = host.Tick,
            seconds = Math.Round(host.Tick * SimConstants.TickSeconds, 1),
            // The first tick whose sends differed from the recording's: null when the replay is the night, tick for tick.
            divergedAt = replay.DivergedAt,
            truncated = replay.Truncated,
            end = replay.End,
            marks = replay.Marks.Select(m => new { m.Kind, m.Tick, seconds = Math.Round(m.Tick * SimConstants.TickSeconds, 1), fields = m.Fields }),
            crew = crew.Select(c => new
            {
                id = c.Id,
                name = world.Names.GetValueOrDefault(c.Id),
                alive = c.State.Alive,
                health = c.State.Health,
                surface = c.State.Surface.ToString(),
                // The car they're aboard (its index in the train), or -1 on the ground.
                car = c.State.Parent,
                death = c.State.Alive ? null : c.State.Death.ToString(),
            }),
            train = new { km = Math.Round(world.Train.Dynamics.Distance / 1000, 3), kmh = Math.Round(world.Train.Dynamics.Speed * 3.6, 1), cars = world.Train.OwnVehicles },
            run = world.Run is { } run ? new { phase = run.Phase.ToString(), end = run.End.ToString() } : null,
            enemies = world.ActiveEnemies.Count(e => !e.Gone),
            shot,
            // A subject view's (note 524): what it framed, and what's wrong with the frame if anything is.
            subject = aim?.Subject,
            subjectNote = aim?.Note,
            wallSeconds = Math.Round(watch.Elapsed.TotalSeconds, 1),
        });
        return replay.DivergedAt is null ? 0 : 1;
    }

    static void Print(object value) => Console.WriteLine(JsonSerializer.Serialize(value, DataFile.Options));

    static string Str(string[] args, string name, string fallback)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
    }

    static double Opt(string[] args, string name, double fallback) =>
        Str(args, name, "") is { Length: > 0 } v ? double.Parse(v, CultureInfo.InvariantCulture) : fallback;
}
