using Ballast;
using DarkTerritory.Dev.Tells;

namespace DarkTerritory.Dev.Tests;

/// <summary>
/// The visual tell audit (systems-spec A.4a, note 1037), A.3's audio audit's twin: a tell that can't be seen is a bug. The
/// fast few (tells.json <c>fast</c>) are drawn here in every night; <c>dt tells</c> draws them all.
/// </summary>
public class TellAuditTests
{
    static readonly string Content = DataFile.FindContentRoot();

    [Fact]
    public void TheFastTellsAreSeenAtTheirWarningDistanceInEveryNight()
    {
        var tuning = TellTuning.Load(Content);
        TellAudit.Report report;
        try { report = TellAudit.Run(Content, tuning.Fast, output: null); }
        catch (Ballast.Render.GpuUnavailableException e) { Assert.Skip($"no Vulkan: {e.Message}"); throw; }
        Assert.Empty(report.NotStaged);
        Assert.All(report.Tells, t => Assert.Equal(tuning.Tells[t.Kind].From.Length * tuning.Conditions.Count, t.Frames.Count));
        Assert.Empty(report.Failing);
    }

    [Fact]
    public void AFrameWithoutTheCreatureMeasuresNothingAndAPaleBlobOnBlackMeasuresItsStep()
    {
        var black = new byte[64 * 64 * 4];
        Assert.Equal((0, 0.0, 0.0), TellAudit.Measure(black, black, 3));
        var blob = (byte[])black.Clone();
        for (int i = 0; i < 100; i++)
            blob[i * 4] = blob[i * 4 + 1] = blob[i * 4 + 2] = 60;
        var (pixels, contrast, _) = TellAudit.Measure(blob, black, 3);
        Assert.Equal(100, pixels);
        // sRGB 60 is L* 25.
        Assert.InRange(contrast, 24, 26);
    }
}
