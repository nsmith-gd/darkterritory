using DarkTerritory.Dev.Feedback;
using DarkTerritory.Game;

namespace DarkTerritory.Dev.Tests;

/// <summary>
/// GDD §34.2, ARCHITECTURE §8 note 677: a developer build's reports (a note typed or spoken, REPORT A PROBLEM, a crash) all go
/// on the end of one text file beside the launch's log, in the game's own folder when it can be written, so the director
/// sends one file.
/// </summary>
public class TestReportsTests
{
    static string Scratch() => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "dt-tests-" + Guid.NewGuid().ToString("N"))).FullName;

    [Fact]
    public void ANoteAProblemAndACrashGoInOneFileBesideTheLog()
    {
        string game = Scratch();
        var crashes = new CrashReports(Path.Combine(game, "logs", "reports"));
        try
        {
            string? logs = TestReports.GameLogs(game);
            Assert.Equal(Path.Combine(game, "logs"), logs);
            crashes.StartLog(logs!);
            var dev = DevTools.Start(["--no-record"], crashes, logs);
            Assert.Equal(Path.Combine(logs!, TestReports.FileName), dev.Tests!.Path);
            Assert.Equal(Path.Combine(logs!, "notes"), dev.Notes!.Directory);

            Assert.NotNull(crashes.WriteProblem("the roster showed two of me"));
            var note = new FeedbackNote { Id = "fb-test-bundle", At = DateTime.UtcNow, Kind = "typed", Text = "dragger not seen on the side of any car" };
            string bundle = FeedbackBundle.Write(dev.Notes.Directory, note, null, null, 48000, crashes, dev.Tests);
            Assert.NotNull(crashes.Write(new InvalidOperationException("the boiler's gauge went negative")));

            Assert.Equal(3, dev.Tests.Count);
            string text = File.ReadAllText(dev.Tests.Path);
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(text, "DARK TERRITORY TEST REPORTS"));
            int one = text.IndexOf($"{TestReports.Rule}1 · ", StringComparison.Ordinal);
            int two = text.IndexOf($"{TestReports.Rule}2 · ", StringComparison.Ordinal);
            int three = text.IndexOf($"{TestReports.Rule}3 · ", StringComparison.Ordinal);
            Assert.True(one >= 0 && one < two && two < three, "the three in the order they were written");
            // Each as its own report reads: what happened, and whose folder holds the rest of a note.
            Assert.Contains("a problem reported (REPORT A PROBLEM)", text[one..two]);
            Assert.Contains("the roster showed two of me", text[one..two]);
            Assert.Contains("a typed note (F8)", text[two..three]);
            Assert.Contains("dragger not seen on the side of any car", text[two..three]);
            Assert.Contains($"folder    {Path.GetFullPath(bundle)}", text[two..three]);
            Assert.Contains("a crash", text[three..]);
            Assert.Contains("the boiler's gauge went negative", text[three..]);
            Assert.Contains("LAST ", text[three..]);
            // The reports' own files are still written, beside the log.
            Assert.True(File.Exists(Path.Combine(bundle, FeedbackBundle.ReportFile)));
            Assert.Equal(2, Directory.GetFiles(crashes.Directory, "*.txt").Length);
            Assert.Equal(Path.Combine(logs!, CrashReports.LatestLog), crashes.LogPath);
        }
        finally
        {
            crashes.StopLog();
            Directory.Delete(game, recursive: true);
        }
    }

    [Fact]
    public void ASpokenNoteNamesItsVoiceAndTheNextLaunchAddsToTheSameFile()
    {
        string logs = Scratch();
        try
        {
            var crashes = new CrashReports(Path.Combine(logs, "reports"));
            var tests = new TestReports(Path.Combine(logs, TestReports.FileName));
            var spoken = new FeedbackNote { Id = "fb-test-voice", At = DateTime.UtcNow, Kind = "voice", VoiceSeconds = 4.5 };
            FeedbackBundle.Write(Path.Combine(logs, "notes"), spoken, null, [0.1f, -0.1f], 48000, crashes, tests);
            // A later launch: a new TestReports on the same file numbers on from what's there.
            var later = new TestReports(tests.Path);
            Assert.Equal(2, later.Add(crashes.Take(null, DateTime.Now, "the lodger made no sound"), TestReports.Called(crashes.Take(null, DateTime.Now))));
            string text = File.ReadAllText(tests.Path);
            Assert.Contains($"a spoken note (F9), 4.5 s: {FeedbackBundle.VoiceFile}", text);
            Assert.Contains("(a spoken note)", text);
            Assert.Contains("the lodger made no sound", text);
        }
        finally
        {
            Directory.Delete(logs, recursive: true);
        }
    }

    [Fact]
    public void ANoteCarriesTheLastMinutesFramesAndTheNightsPerformanceGoesOnTheEnd()
    {
        // GDD §34.3, note 691: a note's frames beside it and in the file; the night's report under its own rule.
        string logs = Scratch();
        try
        {
            var crashes = new CrashReports(Path.Combine(logs, "reports"));
            var tests = new TestReports(Path.Combine(logs, TestReports.FileName));
            var watch = new FrameWatch();
            for (int i = 0; i < 600; i++)
            {
                watch.Begin();
                double ms = i == 500 ? 45 : 11;
                watch.Part("scene", ms);
                watch.Frame(ms, i / 60.0, () => "at 0.40 km, 0.0 m/s, AtStop");
            }
            var note = new FeedbackNote { Id = "fb-test-frames", At = DateTime.UtcNow, Kind = "typed", Text = "it's stuttering here in the town" };
            string bundle = FeedbackBundle.Write(Path.Combine(logs, "notes"), note, null, null, 48000, crashes, tests, watch.Story(599 / 60.0));
            Assert.StartsWith("FRAMES · the last ", File.ReadAllText(Path.Combine(bundle, FeedbackBundle.FramesFile)));
            Assert.Equal(2, tests.Add(DateTime.Now, "the night's performance", watch.Report("frontier:7 · to the night's end")));
            string text = File.ReadAllText(tests.Path);
            int two = text.IndexOf($"{TestReports.Rule}2 · ", StringComparison.Ordinal);
            Assert.True(two > 0);
            // The note's own report, then its frames: the hitch at 8.3 s, in the town.
            Assert.Contains("it's stuttering here in the town", text[..two]);
            Assert.Contains("FRAMES · the last ", text[..two]);
            Assert.Contains("45 ms (median 11), led by scene", text[..two]);
            Assert.Contains("the night's performance =====", text[two..]);
            Assert.Contains("PERFORMANCE · frontier:7 · to the night's end", text[two..]);
            Assert.Contains("hitches   1 ", text[two..]);
        }
        finally
        {
            Directory.Delete(logs, recursive: true);
        }
    }
}
