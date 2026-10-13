namespace DarkTerritory.Game.Tests;

/// <summary>
/// Note 549 (the director, 9 Oct 2026: "an extreme borderline unplayable performance drop that kept recurring throughout
/// gameplay"): a frame well over the night's run is written down with what it spent, and only those.
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
}
