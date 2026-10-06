using Ballast;
using Ballast.Render;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game.Tests;

/// <summary>GDD §9 (note 178): the clerk's tally plays over the radio before a delivered night's end screen, then gives way to it.</summary>
public class RadioCardTests
{
    static readonly string Content = DataFile.FindContentRoot();

    [Fact]
    public void TheManifestAtTheGateIsDroppedAndTheTallyStays()
    {
        // Note 267 (the director's notes on build 1121: the opening reading is "a bit long... a pain for localization... a
        // bit cheesy"): run.json radio.manifest is off; the clerk's tally home is untouched (the test below).
        using var night = NetPlaySession.HostGame(Content, new SessionSetup(Route: "frontier:7", Cars: 4, Enemies: false), port: 0);
        Assert.False(night.World.Run!.Tuning.Radio.Manifest);
        night.World.Run.Resume(0, -1, night.World.Train.Boiler.Tender, 0);
        Assert.NotEqual(RunPhase.Yard, night.World.Run.Phase);
        for (int i = 0; i < 3 * SimConstants.TickRate; i++)
        {
            night.Step(default);
            Assert.Null(night.RadioReading);
        }
    }

    [Fact]
    public void TheClerkTalliesBeforeTheEndScreen()
    {
        using var night = NetPlaySession.HostGame(Content, new SessionSetup(Route: "frontier:7", Cars: 4, Enemies: false), port: 0);
        for (int i = 0; i < 10; i++)
            night.Step(default);
        Assert.False(night.ClerkTally);
        var report = new RunReport(RunEnd.Delivered, 1000, 12, 3, 0, 2, 1240, 40, 12, 30, 1158, 1, 0);
        night.World.Run!.MirrorReport(report);
        night.Step(default);
        Assert.True(night.ClerkTally);
        Assert.Equal(Radio.Tally(report), night.RadioReading);
        // The HUD has the radio card up, and not the end screen's DELIVERED.
        var hud = new Overlay();
        Hud.Build(hud, 480, 270, night);
        Assert.True(hud.Count > 0);
        double length = Radio.Length(Radio.Tally(report), night.World.Run.Tuning.Radio);
        for (int i = 0; i < length * SimConstants.TickRate + 2; i++)
            night.Step(default);
        Assert.False(night.ClerkTally);
    }
}
