using Ballast;
using DarkTerritory.Game;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// Reports to the studio (note 452; the director, 8 Oct: "point reports, crashes, logs, etc. into a nicely formatted
/// reporting structure that is dev friendly for debugging"): a report a developer reads at a glance, its JSON twin, a log
/// of every launch, and the mail the player sends it in.
/// </summary>
public sealed class ReportsTests : IDisposable
{
    static readonly string Content = DataFile.FindContentRoot();
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"dt-reports-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    static Exception Thrown()
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

    CrashReports Staged()
    {
        var reports = new CrashReports(Path.Combine(_dir, "crashes"));
        reports.Context("edition", "demo");
        reports.Context("gpu", "llvmpipe");
        reports.Context("gpu", "NVIDIA GeForce RTX 3060");
        reports.Live = () => [new ReportField("doing", "a hosted night, frontier:7, 6 cars")];
        reports.Remember("hosting on UDP port 27960");
        return reports;
    }

    [Fact]
    public void ACrashReportReadsAtAGlance()
    {
        var path = Staged().Write(Thrown(), new DateTime(2026, 10, 8, 3, 15, 22))!;
        Assert.Equal("crash-20261008-031522.txt", Path.GetFileName(path));
        var lines = File.ReadAllLines(path);
        Assert.Equal("DARK TERRITORY CRASH REPORT", lines[0]);
        // A header of labelled lines, every value in one column: the id, when, the build and its commit, the machine, the
        // context as last set (the gpu set twice says the second), what was going on asked as it's written.
        string Field(string name) => lines.Single(l => l.StartsWith(name + " ", StringComparison.Ordinal))[10..];
        Assert.Equal("crash-20261008-031522", Field("id"));
        Assert.StartsWith("2026-10-08 03:15:22", Field("time"));
        var (version, commit) = Report.Build();
        Assert.Equal($"{version}, commit {commit}", Field("build"));
        Assert.Matches("^[0-9a-f]{7,}$", commit);
        Assert.Equal("demo", Field("edition"));
        Assert.Equal("NVIDIA GeForce RTX 3060", Field("gpu"));
        Assert.Equal("a hosted night, frontier:7, 6 cars", Field("doing"));
        Assert.Contains(lines, l => l.StartsWith("os ", StringComparison.Ordinal));
        // Then the exception, its stack, and the lines it printed with when.
        Assert.Contains("EXCEPTION", lines);
        Assert.Contains(lines, l => l.Contains("InvalidOperationException: the boiler burst", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains(nameof(Thrown), StringComparison.Ordinal));
        Assert.Contains(lines, l => System.Text.RegularExpressions.Regex.IsMatch(l, @"^  \[\d\d:\d\d:\d\d\.\d{3}\] hosting on UDP port 27960$"));
    }

    [Fact]
    public void ItsJsonTwinSaysTheSameForTooling()
    {
        var path = Staged().Write(Thrown(), new DateTime(2026, 10, 8, 3, 15, 22))!;
        var json = Path.ChangeExtension(path, ".json");
        Assert.True(File.Exists(json));
        var r = Report.Load(path)!;
        Assert.Equal("crash-20261008-031522", r.Id);
        Assert.Equal(Report.Crash, r.Kind);
        Assert.Equal(typeof(InvalidOperationException).FullName, r.ExceptionType);
        Assert.Equal("the boiler burst", r.ExceptionMessage);
        Assert.Equal("NVIDIA GeForce RTX 3060", r.Fields.Single(f => f.Name == "gpu").Value);
        Assert.Single(r.Log);
        // Round trips as written, and its text from the twin is the text on disk.
        Assert.Equal(File.ReadAllText(path), r.ToText());
        Assert.Equal(r.ToJson(), File.ReadAllText(json));
        // A crash report's twin isn't a crash of its own: the notice counts the text files (note 411).
        Assert.Equal(1, CrashReports.Unseen(Path.GetDirectoryName(path)!)!.Count);
    }

    [Fact]
    public void WhatGoesWrongReadingTheNightStillLeavesAReport()
    {
        var reports = Staged();
        reports.Live = () => throw new NullReferenceException("the session's gone");
        var r = Report.Load(reports.Write(Thrown())!)!;
        Assert.Contains("the session's gone", r.Fields.Single(f => f.Name == "live").Value);
        Assert.Equal("the boiler burst", r.ExceptionMessage);
    }

    [Fact]
    public void AProblemReportIsACrashsWithoutTheException()
    {
        var path = Staged().WriteProblem("The roster showed two of me.", new DateTime(2026, 10, 8, 4, 0, 0))!;
        Assert.Equal("problem-20261008-040000.txt", Path.GetFileName(path));
        var text = File.ReadAllText(path);
        Assert.StartsWith("DARK TERRITORY PROBLEM REPORT", text);
        Assert.Contains("The roster showed two of me.", text);
        Assert.DoesNotContain("EXCEPTION", text);
        // It isn't the crash notice's (note 411).
        Assert.Null(CrashReports.Unseen(Path.GetDirectoryName(path)!));
    }

    [Fact]
    public void TheMailIsToTheStudioWithWhatItIsInTheSubjectAndTheHeaderInTheBody()
    {
        var tuning = ReportsTuning.Load(Content);
        Assert.Equal("nsmith@squidostudio.com", tuning.To);
        var path = Staged().Write(Thrown(), new DateTime(2026, 10, 8, 3, 15, 22))!;
        var mail = Report.Load(path)!.MailTo(tuning, path);
        Assert.StartsWith("mailto:nsmith@squidostudio.com?subject=", mail);
        var query = mail[(mail.IndexOf('?') + 1)..].Split('&').Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
        Assert.Contains("crash", query["subject"]);
        Assert.Contains("System.InvalidOperationException", query["subject"]);
        Assert.Contains("crash-20261008-031522", query["subject"]);
        Assert.Contains(Report.Build().Version, query["subject"]);
        // The body asks for the file, by name and folder, and carries the header and the exception's head.
        var body = query["body"];
        Assert.Contains("crash-20261008-031522.txt", body);
        Assert.Contains(Path.GetDirectoryName(path)!, body);
        Assert.Contains("gpu: NVIDIA GeForce RTX 3060", body);
        Assert.Contains("the boiler burst", body);
        Assert.True(body.Length <= tuning.MailBodyChars, $"{body.Length}");
        // A long one is cut where the tuning says.
        var cut = Report.Load(path)!.MailTo(tuning with { MailBodyChars = 200 }, path);
        Assert.True(Uri.UnescapeDataString(cut[(cut.IndexOf("body=", StringComparison.Ordinal) + 5)..]).Length <= 200);
    }

    [Fact]
    public void EachLaunchHasALogAndTheLastFewAreKept()
    {
        var logs = Path.Combine(_dir, "logs");
        for (int i = 0; i < 8; i++)
        {
            var r = new CrashReports(Path.Combine(_dir, "crashes"));
            r.StartLog(logs, keep: 3);
            r.Remember($"launch {i}");
            Assert.Equal(Path.Combine(logs, CrashReports.LatestLog), r.LogPath);
            // The log's where a report says it is.
            Assert.Equal(r.LogPath, Report.Load(r.WriteProblem(at: new DateTime(2026, 10, 8, 5, 0, i))!)!.Fields.Single(f => f.Name == "log").Value);
            // Let the file go, as the process ending does, and give the next launch a later start.
            r.StopLog();
            File.SetCreationTime(r.LogPath!, new DateTime(2026, 10, 8, 5, 0, i));
        }
        Assert.Contains("launch 7", File.ReadAllText(Path.Combine(logs, CrashReports.LatestLog)));
        var kept = Directory.GetFiles(logs, "log-*.txt").Order().ToList();
        Assert.Equal(3, kept.Count);
        Assert.Contains("launch 6", File.ReadAllText(kept[^1]));
    }

    FrontEnd Menu()
    {
        return new(DataFile.Load<CampaignTuning>(Path.Combine(Content, CampaignTuning.File)), DataFile.Load<RunTuning>(Path.Combine(Content, RunTuning.File)),
            new SaveSlots(Path.Combine(_dir, "saves"), 3), Path.Combine(_dir, "settings.json"), () => 42);
    }

    [Fact]
    public void TheCrashNoticeSendsTheReport()
    {
        var crashes = Path.Combine(_dir, "crashes");
        var path = Staged().Write(Thrown())!;
        var m = Menu();
        m.Reports = ReportsTuning.Load(Content);
        m.Crash = CrashReports.Unseen(crashes);
        m.Show(Screen.Crashed);
        Assert.Equal(["OK", "SEND THE REPORT", "OPEN THE REPORTS"], m.Items.Select(i => i.Label));
        m.Down();
        var mail = Assert.IsType<Launch.Mail>(m.Select());
        Assert.StartsWith("mailto:nsmith@squidostudio.com", mail.MailTo);
        Assert.Equal(crashes, mail.Folder);
        Assert.Equal(Screen.Crashed, m.Screen);
        // No address: nothing offers to send.
        var quiet = Menu();
        quiet.Reports = new ReportsTuning();
        quiet.Crash = CrashReports.Unseen(crashes);
        quiet.Show(Screen.Crashed);
        Assert.DoesNotContain(quiet.Items, i => i.Label == "SEND THE REPORT");
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void ReportAProblemIsInTheSettingsAndWritesOneToSend()
    {
        var reports = Staged();
        var m = Menu();
        m.Reports = ReportsTuning.Load(Content);
        m.ProblemReport = () => reports.WriteProblem();
        m.Show(Screen.Settings);
        int at = m.Items.ToList().FindIndex(i => i.Label == "REPORT A PROBLEM");
        Assert.True(at > 0);
        Assert.True(m.Items[at - 1].Heading);
        while (m.Selected != at)
            m.Down();
        var mail = Assert.IsType<Launch.Mail>(m.Select());
        Assert.Contains("problem", Uri.UnescapeDataString(mail.MailTo));
        Assert.Single(Directory.GetFiles(mail.Folder, "problem-*.txt"));
        Assert.Contains("Report written", m.Message);
        // It couldn't be written: said, and nothing opened.
        m.ProblemReport = () => null;
        Assert.Null(m.Select());
        Assert.Contains("couldn't", m.Message);
        // Without an address, or a way to write one, it isn't offered.
        var bare = Menu();
        bare.Show(Screen.Settings);
        Assert.DoesNotContain(bare.Items, i => i.Label == "REPORT A PROBLEM");
    }
}
