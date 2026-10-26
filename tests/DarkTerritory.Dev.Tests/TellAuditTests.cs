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

    /// <summary>
    /// Note 1043: what the gym's call alone couldn't stand (the Follower on a crewmate stepped down, the Track Doll ahead of a
    /// moving train, the Choir's ghosts come, the Tapper behind a gunner, Hotbox glowing, the Freight Beetle seen on its track)
    /// is staged, and is in its frame: something of it differs from the frame without it from every viewpoint. (Whether it's
    /// seen enough is the audit's table, not this.)
    /// </summary>
    [Fact]
    public void TheCreaturesTheGymCantStandAloneAreStagedAndInFrame()
    {
        string[] staged = ["follower", "trackDoll", "choir", "tapper", "hotbox", "freightBeetle"];
        TellAudit.Report report;
        try { report = TellAudit.Run(Content, staged, ["clear"], output: null); }
        catch (Ballast.Render.GpuUnavailableException e) { Assert.Skip($"no Vulkan: {e.Message}"); throw; }
        Assert.Empty(report.NotStaged);
        Assert.Equal(staged.Length, report.Tells.Count);
        Assert.All(report.Tells, t => Assert.All(t.Frames, f => Assert.True(f.Pixels > 0, $"{t.Kind} from {f.From}: nothing of it in the frame")));
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
