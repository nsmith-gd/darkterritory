namespace DarkTerritory.Game.Tests;

/// <summary>
/// Note 549 (the director, 9 Oct 2026: "an extreme borderline unplayable performance drop that kept recurring throughout
/// gameplay"): a frame well over the night's run is written down with what it spent, and only those. Note 691 (GDD §34.3):
/// every hitch is kept, and the night's frames are reported whole.
/// </summary>
public class FrameWatchTests
{
    [Fact]
    public void ASlowFrameIsWrittenDownWithItsPartsAndAQuickOneIsNot()
    {
        var watch = new FrameWatch();
        for (int i = 0; i < 12; i++)
        {
            watch.Begin();
            watch.Mark("sim");
            Assert.Null(watch.End(i * 0.02, () => "here"));
        }
        watch.Begin();
        Thread.Sleep(150);
        watch.Mark("scene");
        var line = watch.End(1, () => "at 5.20 km");
        Assert.NotNull(line);
        Assert.StartsWith("slow frame ", line);
        Assert.Contains("scene ", line);
        Assert.Contains("at 5.20 km", line);
        Assert.Equal(1, watch.Slow);
        Assert.True(watch.Worst >= 150);
    }

    [Fact]
    public void ACrawlIsALineASecondButEveryFrameIsCounted()
    {
        var watch = new FrameWatch();
        int lines = 0;
        for (int i = 0; i < 3; i++)
        {
            watch.Begin();
            Thread.Sleep(110);
            if (watch.End(10 + i * 0.2, () => "") is not null)
                lines++;
        }
        Assert.Equal(1, lines);
        Assert.Equal(3, watch.Slow);
    }

    [Theory]
    [InlineData(90, double.NaN, false)]
    [InlineData(120, double.NaN, true)]
    [InlineData(120, 40, false)]
    [InlineData(200, 40, true)]
    public void SlowIsOverAHundredMillisecondsAndFourTimesTheNightsRun(double ms, double median, bool slow) =>
        Assert.Equal(slow, FrameWatch.IsSlow(ms, median));

    // A night of frames of known length: `ms` at each 60 Hz frame, its parts given.
    static FrameWatch Night(Func<int, double> ms, int frames, Func<int, string>? lead = null, Func<int, double>? yaw = null)
    {
        var watch = new FrameWatch();
        for (int i = 0; i < frames; i++)
        {
            watch.Begin();
            double t = ms(i);
            watch.Part(lead?.Invoke(i) ?? "scene", t * 0.8);
            watch.Part("render, present", t * 0.2);
            watch.View(yaw?.Invoke(i) ?? 0, 0);
            watch.Frame(t, i / 60.0, () => $"at frame {i}");
        }
        return watch;
    }

    [Fact]
    public void AStutterTooShortToBeSlowIsKeptAsAHitchWithItsPartsAndWhere()
    {
        // 11 ms frames, and every 100th 40 ms: never a slow frame (100 ms), every one a hitch the player feels.
        var watch = Night(i => i % 100 == 99 ? 40 : 11, 600, i => i % 100 == 99 ? "sim (3 ticks)" : "scene");
        Assert.Equal(0, watch.Slow);
        Assert.Equal(6, watch.Hitches);
        var h = watch.Kept[0];
        Assert.Equal(40, h.Ms);
        Assert.Equal("sim", h.Lead);
        Assert.Contains("sim (3 ticks) 32", h.Parts);
        Assert.Equal("at frame 99", h.Where);
        Assert.Equal(99 / 60.0, h.At, 6);
    }

    [Fact]
    public void TheNightsFirstFramesAndASteadyCrawlAreNotHitches()
    {
        // Loading's first frames have no run to be against; a steady 40 ms is slow but even, not a stutter.
        Assert.Equal(0, Night(i => i < 5 ? 300 : 11, 120).Hitches);
        Assert.Equal(0, Night(_ => 40, 300).Hitches);
        Assert.False(FrameWatch.IsHitch(24, 5));
        Assert.True(FrameWatch.IsHitch(25, 12));
        Assert.False(FrameWatch.IsHitch(30, 16));
    }

    [Fact]
    public void AHitchWhileTheViewTurnsFastIsCountedAsTurning()
    {
        // Turning 0.1 rad a frame at 60 Hz: 344°/s, past TurningFast.
        var watch = Night(i => i % 50 == 49 ? 45 : 11, 300, yaw: i => i * 0.1);
        Assert.Equal(6, watch.Hitches);
        Assert.Equal(6, watch.HitchesTurning);
        Assert.InRange(watch.Kept[0].Turn, 340, 350);
    }

    [Fact]
    public void WhatTheFrameCountedGoesWithItsHitch()
    {
        var watch = new FrameWatch();
        for (int i = 0; i < 40; i++)
        {
            watch.Begin();
            if (i == 30)
                FrameCounts.Add("cells cooked here");
            FrameCounts.Add("meshes uploaded", i == 30 ? 3 : 0);
            watch.Part("scene", i == 30 ? 60 : 10);
            watch.Frame(i == 30 ? 60 : 10, i / 60.0, () => "");
        }
        Assert.Equal("cells cooked here 1, meshes uploaded 3", Assert.Single(watch.Kept).Counts);
    }

    [Fact]
    public void TheNightsReportHasItsPercentilesBandsMinutesAndWorstHitches()
    {
        // Two minutes at 11 ms, the second with a hitch a second (the town).
        var watch = Night(i => i >= 3600 && i % 60 == 59 ? 48 : 11, 7200);
        string report = watch.Report("frontier:7 · to the night's end", ["audio     ran dry 0 times"]);
        Assert.StartsWith("PERFORMANCE · frontier:7 · to the night's end", report);
        Assert.Contains("p50 12 ms", report);
        Assert.Contains("hitches   60 ", report);
        Assert.Contains("led by    scene 60", report);
        Assert.Contains("audio     ran dry 0 times", report);
        Assert.Contains("≤ 11.2 ms 90 fps", report);
        var minutes = report.Split('\n').Where(l => l.StartsWith("     0  ", StringComparison.Ordinal) || l.StartsWith("     1  ", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, minutes.Count);
        Assert.Matches(@"^\s+0\s+3600\s+12\s+12\s+0\s+0\s+at frame 0", minutes[0].TrimEnd('\r'));
        Assert.Matches(@"^\s+1\s+3600\s+12\s+49\s+60\s+0\s+at frame 3600", minutes[1].TrimEnd('\r'));
        Assert.Contains("the worst hitches, the worst 12 of 60:", report);
        // A second or more is past the histogram: said so, not a made-up number.
        Assert.Contains("p50 ≥1000 ms", Night(_ => 1500, 20).Report("a crawl"));
    }

    [Fact]
    public void ANotesStoryIsTheLastMinuteOnly()
    {
        // A hitch at 10 s and one at 100 s: a note at 110 s carries the second only.
        var watch = Night(i => i == 600 || i == 6000 ? 70 : 11, 6600);
        string story = watch.Story(6599 / 60.0);
        Assert.StartsWith("FRAMES · the last 60 s (", story);
        Assert.Contains("; 1 hitches", story);
        Assert.Contains("at frame 6000", story);
        Assert.DoesNotMatch(new System.Text.RegularExpressions.Regex(@"at frame 600\r?$", System.Text.RegularExpressions.RegexOptions.Multiline), story);
        Assert.Equal("FRAMES · none timed yet", new FrameWatch().Story(5));
    }

    [Fact]
    public void TheReportSaysWhatEachPartCostsAFramePastTheFirstFrames()
    {
        // Note 695's find: a part that allocates megabytes every frame (the town's houses rebuilt) heads the table, and the
        // first frames' loading doesn't count.
        var watch = new FrameWatch();
        for (int i = 0; i < 100; i++)
        {
            watch.Begin();
            watch.Part("scene: town houses", i < 10 ? 500 : 8, i < 10 ? 800L << 20 : 10L << 20);
            watch.Part("sim (1 ticks)", 2, 0);
            watch.Frame(i < 10 ? 502 : 10, i / 60.0, () => "");
        }
        string report = watch.Report("a town");
        Assert.Matches(@"scene: town houses\s+8\.00\s+10\.00", report);
        Assert.Matches(@"\n  sim\s+2\.00\s+0\.00", report.Replace("\r", ""));
        // A hitch says what its parts allocated.
        watch.Begin();
        watch.Part("scene: town houses", 32, 3L << 20);
        watch.Part("sim (1 ticks)", 2, 0);
        watch.Frame(34, 2, () => "");
        Assert.Equal("scene: town houses 32 (3.0 MB), sim (1 ticks) 2", Assert.Single(watch.Kept).Parts);
        Assert.Equal("scene: town houses", watch.Kept[0].Lead);
    }
}
