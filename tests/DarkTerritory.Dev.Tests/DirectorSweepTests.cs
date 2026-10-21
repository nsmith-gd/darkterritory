using System.Text.Json;
using Ballast;
using DarkTerritory.Dev.Encounters;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;

namespace DarkTerritory.Dev.Tests;

/// <summary>
/// `dt director sweep` (#601, note 888): its watch counts encounters, not bodies, from the spines alone; a hazard or an incident is
/// danger but never an encounter; the droughts start at the grace; the quick sweep is note 874's; and watching a night never
/// changes it.
/// </summary>
public class DirectorSweepTests
{
    static readonly string Content = DataFile.FindContentRoot();

    static Seen On(int id, EnemyKind kind, SpinePhase phase = SpinePhase.Telegraph, bool hazard = false) => new(id, kind, phase, hazard, !hazard);

    [Fact]
    public void APackComingOnTogetherIsOneEncounterAndAnotherKindIsAnother()
    {
        var w = new EncounterWatch();
        w.Tick(10, [On(1, EnemyKind.CinderHound)]);
        w.Tick(14, [On(1, EnemyKind.CinderHound), On(2, EnemyKind.CinderHound), On(3, EnemyKind.Ribbit)]);
        // A hound coming on well after the pack's first is a pack of its own.
        w.Tick(10 + EncounterWatch.DefaultGroupSeconds + 5, [On(4, EnemyKind.CinderHound)]);
        Assert.Equal(3, w.Encounters.Count);
        Assert.Equal(2, w.Encounters[0].Members);
        Assert.Equal([EnemyKind.CinderHound, EnemyKind.Ribbit, EnemyKind.CinderHound], w.Encounters.Select(e => e.Kind));
        Assert.Equal(2, w.MaxOverlap);
    }

    [Fact]
    public void OnlyASpineAtTelegraphOrPastComesOnAndAFireIsDangerNotAnEncounter()
    {
        var w = new EncounterWatch { Grace = 0 };
        for (int s = 0; s <= 100; s++)
            w.Tick(s, [On(1, EnemyKind.Gaunt, SpinePhase.Dormant), On(2, EnemyKind.Climber, SpinePhase.Alert),
                .. s >= 40 && s < 70 ? [On(3, EnemyKind.CarFire, SpinePhase.Alert, hazard: true)] : Array.Empty<Seen>()]);
        Assert.Empty(w.Encounters);
        // Nothing came on all night: one drought, the whole of it; the fire broke the danger's into two.
        Assert.Equal([100.0], w.Droughts);
        Assert.Equal([40.0, 31.0], w.DangerDroughts);
        // The Climber pacing in Alert held the cap all night with nothing on (§2 issue 4's case, measured).
        Assert.Equal(101, w.CappedIdleSeconds);
    }

    [Fact]
    public void DroughtsAreCountedFromTheGrace()
    {
        var w = new EncounterWatch();
        for (int s = 0; s <= 300; s++)
            w.Tick(s, s is >= 150 and < 160 ? [On(1, EnemyKind.Whistler)] : []);
        w.Grace = 60;
        Assert.Equal([90.0, 141.0], w.Droughts);
        Assert.Single(w.Encounters);
        Assert.Equal(10, w.Encounters[0].Seconds);
    }

    [Fact]
    public void TheQuickSweepIsNote874s()
    {
        var quick = DirectorSweep.Quick();
        Assert.Equal(16, quick.Count);
        Assert.All(quick, c => Assert.Equal("frontier:7", c.RouteSpec));
        Assert.Equal([1, 2, 4, 8], quick.Select(c => c.Crew).Distinct());
        Assert.Equal([1, 2, 3, 4], quick.Select(c => c.Seed).Distinct());
        // A tier alone runs the night's own route; the cells' crews and trains as given.
        var cell = Assert.Single(DirectorSweep.Grid(["deadLines"], DirectorSweep.ParseSessions("2x5", [], []), [9]));
        Assert.Equal("deadLines:9", cell.RouteSpec);
        Assert.Equal(["--bots", "2", "--cars", "5"], cell.Args(600).Skip(4).Take(4));
        Assert.Equal(20, DirectorSweep.ParseSessions(null, DirectorSweep.Crews, DirectorSweep.Trains).Count);
    }

    [Fact]
    public void AWatchedNightPlaysAsAnUnwatchedOneAndItsRowAndGroupRoundTrip()
    {
        var cell = new SweepCell("frontier:7", 2, 4, 3);
        HarnessReport Night(EncounterWatch? w)
        {
            var hosted = DarkTerritory.Game.HostedAsApp.For(Content, new DarkTerritory.Game.SessionSetup(Route: cell.RouteSpec, Cars: cell.Cars, Enemies: true), cell.Crew, 240);
            return Harness.Run(hosted.Line, hosted.Train, hosted.Player, hosted.Options with
            {
                Seed = cell.Seed,
                DirectorSeed = (ulong)cell.Seed,
                Observe = w is null ? null : (_, _, world) => w.Observe(world),
            }, hosted.Boiler);
        }
        var watch = new EncounterWatch();
        var watched = Night(watch);
        var plain = Night(null);
        Assert.Equal(plain.TrainDistance, watched.TrainDistance);
        Assert.Equal(plain.Threats!.Spawned, watched.Threats!.Spawned);
        Assert.Equal(plain.Threats.Director, watched.Threats.Director);
        watch.Grace = watched.Threats.Pressure!.Grace;
        var row = DirectorSweep.Row(cell, watched, watch, 1, JsonSerializer.SerializeToNode(watched, DataFile.Options));
        Assert.Equal(watched.Threats.Spawned.Values.Sum(), row.Sent);
        // #600's encounter block, copied whole, and its attempts read by name: placed is never more than tried.
        Assert.NotNull(row.Telemetry);
        Assert.InRange(row.Placed!.Value, 0, row.Tried!.Value);
        Assert.InRange(watch.Seconds, 235, 241);
        var report = new SweepReport("test", 240, DirectorSweep.Groups([row, row with { Cell = cell with { Seed = 4 } }]), [row]);
        var back = JsonSerializer.Deserialize<SweepReport>(JsonSerializer.Serialize(report, DataFile.Options), DataFile.Options)!;
        var g = Assert.Single(back.Groups);
        Assert.Equal(2, g.Nights);
        Assert.Equal(row.Encounters, g.Encounters);
        Assert.Contains("frontier:7 | 2 | 4", DirectorSweep.Table(back.Groups));
        Assert.Contains("→", DirectorSweep.Compare(report.Groups, back.Groups));
    }

    /// <summary>
    /// The quiets the brief keeps (#613, note 930) are what the halves' open time leaves out: the grace, the final stretch, the
    /// terminus's and the line's grace bans, a facility's lull; nothing else the director holds for.
    /// </summary>
    [Theory]
    [InlineData("grace", true)]
    [InlineData("final stretch", true)]
    [InlineData("facility lull", true)]
    [InlineData("banned (terminus_safe)", true)]
    [InlineData("banned (grace)", true)]
    [InlineData("at the cap", false)]
    [InlineData("building", false)]
    [InlineData("aftermath", false)]
    [InlineData("cooldown", false)]
    public void TheProtectedQuietsAreTheBriefsOwn(string why, bool kept) => Assert.Equal(kept, EncounterWatch.Protects(why));
}
