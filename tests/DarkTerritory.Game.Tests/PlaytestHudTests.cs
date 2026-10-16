using Ballast;
using DarkTerritory.Game;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// Note 837, the director's playtest of 16 Oct: line 11, "I don't think we need the warning you're going too fast around the
/// bend" (the tilt tells the driver); line 25, "The incident report is good, but it should go away after delivered".
/// </summary>
public sealed class PlaytestHudTests
{
    static readonly string Content = DataFile.FindContentRoot();

    [Fact]
    public void TheCabsBendWarningIsOffAndTheRoofsIsKept()
    {
        var tuning = DataFile.Load<HudTuning>(Path.Combine(Content, HudTuning.File));
        Assert.False(tuning.BendWarning);
        // The roof rider's GET OFF THE ROOF still comes up: that one's a crewmate's life.
        var route = Sim.LineGen.Routes.Generate(Content, "deadLines:1", 6);
        Assert.NotNull(Hud.RoofWarningLines(Staging.RoofWarning(Content, route, 6, "bend")));
    }

    [Fact]
    public void ADeliveredRunsReportGoesAfterAWhileButNotWithCommendationsToGive()
    {
        var route = Sim.LineGen.Routes.Generate(Content, "frontier:7", 6);
        var s = new PrototypeSession(Content, route, 6, enemies: false);
        s.World.Run!.MirrorReport(Staging.Report(s.World, RunEnd.Delivered));
        Assert.False(Hud.DeliveredReportGone(s));
        uint shown = s.World.Tick;
        s.World.Tick = shown + (uint)Math.Ceiling((Hud.Tuning.DeliveredReportSeconds - 1) * SimConstants.TickRate);
        Assert.False(Hud.DeliveredReportGone(s));
        s.World.Tick = shown + (uint)Math.Ceiling((Hud.Tuning.DeliveredReportSeconds + 1) * SimConstants.TickRate);
        Assert.True(Hud.DeliveredReportGone(s));
        // A commendation still to give keeps it up.
        Hud.StagedCommendPick = ("CREWMATE", "STEADY HAND", false);
        try
        {
            Assert.False(Hud.DeliveredReportGone(s));
        }
        finally
        {
            Hud.StagedCommendPick = null;
        }
    }
}
