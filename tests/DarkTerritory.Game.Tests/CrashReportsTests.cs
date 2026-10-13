using Ballast;
using Ballast.Render;
using DarkTerritory.Game;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// Crash reports (T33): the exception and what the game said last, in a file a player can send; and (note 411) the next
/// launch's title saying so, since the console that said where it is was never seen by a player launched from Steam.
/// </summary>
public sealed class CrashReportsTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"dt-crash-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void AReportHasTheExceptionAndTheLastLines()
    {
        var reports = new CrashReports(_dir, keepLines: 3);
        foreach (var line in new[] { "one", "two", "GPU: llvmpipe", "hosting on UDP port 27015" })
            reports.Remember(line);
        Assert.Equal(["two", "GPU: llvmpipe", "hosting on UDP port 27015"], reports.Recent);

        Exception thrown;
        try
        {
            throw new InvalidOperationException("the boiler burst");
        }
        catch (InvalidOperationException e)
        {
            thrown = e;
        }
        var path = reports.Write(thrown, new DateTime(2026, 9, 29, 7, 30, 0));
        Assert.Equal(Path.Combine(_dir, "crash-20260929-073000.txt"), path);
        var text = File.ReadAllText(path!);
        Assert.Contains("the boiler burst", text);
        Assert.Contains(nameof(AReportHasTheExceptionAndTheLastLines), text); // the stack
        Assert.Contains("hosting on UDP port 27015", text);
        Assert.DoesNotContain("  one", text);
    }

    Exception Thrown()
    {
        try
        {
            throw new InvalidOperationException("the boiler burst");
        }
        catch (InvalidOperationException e)
        {
            return e;
        }
    }

    [Fact]
    public void AReportNotYetSeenIsSaidUntilItsPutAway()
    {
        // No folder, nothing in it, nothing new: no notice.
        Assert.Null(CrashReports.Unseen(_dir));
        Directory.CreateDirectory(_dir);
        Assert.Null(CrashReports.Unseen(_dir));
        var reports = new CrashReports(_dir);
        reports.Write(Thrown(), new DateTime(2026, 10, 7, 23, 10, 0));
        var newest = reports.Write(Thrown(), new DateTime(2026, 10, 8, 3, 15, 22));
        var notice = CrashReports.Unseen(_dir);
        Assert.NotNull(notice);
        Assert.Equal(newest, notice!.Newest);
        Assert.Equal(2, notice.Count);
        Assert.Equal(_dir, notice.Directory);
        // Put away, they're not said again; a crash after that is.
        CrashReports.MarkSeen(notice);
        Assert.Null(CrashReports.Unseen(_dir));
        var later = reports.Write(Thrown(), new DateTime(2026, 10, 8, 4, 0, 0));
        Assert.Equal(new CrashNotice(_dir, later!, 1), CrashReports.Unseen(_dir));
    }

    FrontEnd Menu()
    {
        var content = DataFile.FindContentRoot();
        return new(DataFile.Load<CampaignTuning>(Path.Combine(content, CampaignTuning.File)), DataFile.Load<RunTuning>(Path.Combine(content, RunTuning.File)),
            new SaveSlots(Path.Combine(_dir, "saves"), 3), Path.Combine(_dir, "settings.json"), () => 42);
    }

    [Fact]
    public void TheNoticeSaysWhereTheReportIsAndOkPutsItAway()
    {
        var crashes = Path.Combine(_dir, "crashes");
        var path = new CrashReports(crashes).Write(Thrown(), new DateTime(2026, 10, 8, 3, 15, 22))!;
        var m = Menu();
        // Nothing to say: the title.
        m.Show(Screen.Crashed);
        Assert.Equal(Screen.Title, m.Screen);
        m.Crash = CrashReports.Unseen(crashes);
        m.Show(Screen.Crashed);
        Assert.Equal(Screen.Crashed, m.Screen);
        // OK first and lit, so the Enter pressed arriving puts it away; where the report is under both.
        Assert.Equal(["OK", "OPEN THE REPORTS"], m.Items.Select(i => i.Label));
        Assert.Equal(0, m.Selected);
        Assert.All(m.Items, i => Assert.Contains(path, i.Detail));
        // OPEN THE REPORTS is the app's (the system's file browser), and the notice stays up.
        m.Down();
        Assert.Equal(new Launch.OpenFolder(crashes), m.Select());
        Assert.Equal(Screen.Crashed, m.Screen);
        // OK: the title, and not said again.
        m.Up();
        Assert.Null(m.Select());
        Assert.Equal(Screen.Title, m.Screen);
        Assert.Null(m.Crash);
        Assert.Null(CrashReports.Unseen(crashes));
    }

    [Fact]
    public void EscapeIsItsOk()
    {
        var crashes = Path.Combine(_dir, "crashes");
        new CrashReports(crashes).Write(Thrown());
        var m = Menu();
        m.Crash = CrashReports.Unseen(crashes);
        m.Show(Screen.Crashed);
        MenuInput.Apply(m, MenuKey.Back);
        Assert.Equal(Screen.Title, m.Screen);
        Assert.Null(CrashReports.Unseen(crashes));
    }

    public static TheoryData<double> Sizes() => [.. Settings.TextSizes];

    [Theory]
    [MemberData(nameof(Sizes))]
    public void ALongPathIsBrokenAfterASlashAndStaysInTheFrame(double size)
    {
        // A Windows profile's path, longer than any line of the menu's: broken where it has to be, never off the side.
        var m = Menu();
        m.Crash = new CrashNotice("C:/Users/Alexandria-Montgomery/AppData/Local/DarkTerritory/crashes",
            "C:/Users/Alexandria-Montgomery/AppData/Local/DarkTerritory/crashes/crash-20261008-031522.txt", 3);
        m.Show(Screen.Crashed);
        var (w, h) = new Settings { TextSize = size }.Canvas;
        var o = new Overlay();
        m.Draw(o, w, h);
        Assert.All(o.Vertices, v => Assert.InRange(v.Position.X, -2, w + 2));
        Assert.All(o.Vertices, v => Assert.InRange(v.Position.Y, -2, h + 2));
        var rows = UiStyle.Wrap(o, m.Crash.Newest, 200).ToList();
        Assert.True(rows.Count > 1);
        Assert.All(rows, r => Assert.True(o.Font.Measure(r) <= 200, r));
        Assert.Equal(m.Crash.Newest, string.Concat(rows.Select(r => r.Trim())));
        Assert.All(rows.SkipLast(1), r => Assert.EndsWith("/", r));
    }
}
