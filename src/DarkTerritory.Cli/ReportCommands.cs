using Ballast;
using DarkTerritory.Game;

/// <summary>
/// `dt report [--problem] [--out dir]` (note 452): a report written as the game writes one, a crash's (an exception thrown
/// a few frames deep, with the lines a night prints) or with <c>--problem</c> a player's own, and the mail to the studio
/// it would open, so the form a developer gets can be read without a crash.
/// </summary>
static class ReportCommands
{
    public static int Run(string content, string[] args)
    {
        string dir = Path.GetFullPath(args.SkipWhile(a => a != "--out").Skip(1).FirstOrDefault() ?? "out/reports");
        var reports = new CrashReports(dir);
        // As the app's context says it (note 452), with a night under way.
        reports.Context("edition", EditionTuning.Load(content).Name);
        reports.Context("mods", "none");
        reports.Context("steam", "off");
        reports.Context("gpu", "llvmpipe (LLVM 17.0.6, 256 bits)");
        reports.Context("vr", "off");
        reports.Live = () =>
        [
            new ReportField("settings", ReportFields.Settings(new Settings { TextSize = 1.25, Captions = true })),
            new ReportField("doing", "a hosted night, frontier:7, 6 cars, 3 bots"),
            new ReportField("night", "running, 742 s in, km 9.41 at 13.2 m/s, 8 vehicles, player 1 alive, host, 4 aboard"),
        ];
        foreach (var line in new[] { "steam: off; LAN and direct IP still work", "GPU: llvmpipe (LLVM 17.0.6, 256 bits)",
            "a crew of 3 bots aboard", "hosting on UDP port 27960: others join with --join <this machine's address>:27960",
            "director: Cinder Hounds at km 9.3 (run, behind)" })
            reports.Remember(line);
        string? path = args.Contains("--problem")
            ? reports.WriteProblem("The roster showed two of me after Dunmore rejoined.")
            : reports.Write(Thrown());
        if (path is null)
        {
            Console.Error.WriteLine($"couldn't write a report in {dir}");
            return 1;
        }
        var report = Report.Load(path)!;
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
        {
            text = path,
            json = Path.ChangeExtension(path, ".json"),
            mailTo = report.MailTo(ReportsTuning.Load(content), path),
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    static Exception Thrown()
    {
        try
        {
            Boiler(null);
        }
        catch (Exception e)
        {
            return e;
        }
        return new InvalidOperationException();
    }

    static int Boiler(int[]? gauges) => gauges![0];
}
