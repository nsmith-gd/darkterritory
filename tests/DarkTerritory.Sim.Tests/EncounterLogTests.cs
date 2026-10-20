using System.Text.Json;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The encounter telemetry (the director's brief §8; queue #600, note 880): a pack is one encounter (a car fire none), a failed attempt makes
/// none and is told apart when it was charged, what nobody's spawn call claimed is the world's own, an encounter ends killed
/// or gone, a quiet stretch past the tuning's droughtSeconds is a drought, and the log only watches: a night is the same with
/// it off, and the same night twice reports the same.
/// </summary>
public class EncounterLogTests
{
    static readonly PlayerTuning P = Tuning.Player;

    /// <summary>A quiet night (the director holds), the crew on the roofs, past the grace: only what a test puts in is about.</summary>
    static Night Quiet()
    {
        var n = new Night(6, 14, enemies: HoundRunTests.Quiet with { Director = HoundRunTests.Quiet.Director with { Roster = ["none"] } });
        n.Crew[1] = PlayerMotor.SpawnInCab(n.Train, P);
        n.Crew[2] = PlayerMotor.SpawnOnRoof(n.Train, 3, 0, P);
        return n;
    }

    /// <summary>A stand-in of the given kind that does nothing, on a car's floor: only the test moves it on.</summary>
    sealed class Still(int id, EnemyKind kind) : Enemy(id)
    {
        public override EnemyKind Kind => kind;
        public override PressureZone Zone => PressureZone.Interior;
        public override Sense Sense => Sense.Sound;
        protected override void Tick(EnemyContext ctx)
        {
        }
    }

    static Still Put(Night n, EnemyKind kind)
    {
        var e = n.World.AddEnemy(id => new Still(id, kind));
        e.Attached = 3;
        return e;
    }

    [Fact]
    public void APackIsOneEncounterAndACarFireIsNone()
    {
        var n = Quiet();
        n.Run(1);
        var log = n.World.Encounters!;
        int mark = n.World.NextEnemyId;
        for (int i = 0; i < 3; i++)
            Put(n, EnemyKind.CinderHound);
        Put(n, EnemyKind.CarFire);
        var came = log.Came(n.World, SpawnChannel.Special, mark);
        var pack = Assert.Single(came);
        Assert.Equal(EnemyKind.CinderHound, pack.Kind);
        Assert.Equal(3, pack.Members.Count);
        Assert.Equal(SpawnChannel.Special, pack.Channel);
        // Claimed once: the sweep doesn't count them again as the world's own, nor the fire at all.
        n.Run(2);
        Assert.Single(log.Encounters);
    }

    [Fact]
    public void AFailedAttemptMakesNoEncounterAndIsToldApartWhenCharged()
    {
        var n = Quiet();
        n.Run(1);
        var log = n.World.Encounters!;
        log.Attempt(n.World, SpawnChannel.Pressure, EnemyKind.Climber, false, null, true, n.World.NextEnemyId);
        log.Attempt(n.World, SpawnChannel.Free, EnemyKind.Climber, false, "no warren", false, n.World.NextEnemyId);
        Assert.Empty(log.Encounters);
        Assert.All(log.Attempts, a => Assert.Equal(-1, a.Encounter));
        var report = EncounterReport.Of(log, n.World);
        Assert.Equal(new ChannelAttempts(1, 0, 1), report.Attempts["pressure"]);
        Assert.Equal(new ChannelAttempts(1, 0, 0), report.Attempts["free"]);
        Assert.Equal(1, report.Failures["no placement"]);
        Assert.Equal(1, report.Failures["no warren"]);
    }

    [Fact]
    public void WhatNoSpawnClaimedIsTheWorldsOwnAndEndsKilledOrGone()
    {
        var n = Quiet();
        n.Run(1);
        var log = n.World.Encounters!;
        var slain = Put(n, EnemyKind.Climber);
        var sent = Put(n, EnemyKind.Whistler);
        n.Run(1.1);
        Assert.Equal(2, log.Encounters.Count);
        Assert.All(log.Encounters, e => Assert.Equal(SpawnChannel.World, e.Channel));
        slain.Health = 0;
        slain.Dismiss();
        sent.Dismiss();
        n.Run(1.1);
        Assert.Equal(EncounterEnd.Killed, log.Encounters.Single(e => e.Kind == EnemyKind.Climber).End);
        Assert.Equal(EncounterEnd.Gone, log.Encounters.Single(e => e.Kind == EnemyKind.Whistler).End);
        Assert.All(log.Encounters, e => Assert.True(e.EndedAt >= 0));
    }

    [Fact]
    public void AQuietStretchPastTheDroughtSecondsIsADrought()
    {
        var n = Quiet();
        var log = n.World.Encounters!;
        n.Run(log.Tuning.DroughtSeconds + 5);
        var report = EncounterReport.Of(log, n.World);
        var drought = Assert.Single(report.Droughts);
        Assert.True(drought.Seconds >= log.Tuning.DroughtSeconds);
        Assert.Equal(report.LongestDrought, drought.Seconds);
        Assert.Equal(0, report.Meaningful);
    }

    /// <summary>A night of the director's own, the crew on the roofs: the plain tuning, so it sends what it sends.</summary>
    static Night Busy(bool telemetry)
    {
        var n = new Night(6, 14, seed: 3);
        n.Crew[1] = PlayerMotor.SpawnInCab(n.Train, P);
        n.Crew[2] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P);
        n.Crew[3] = PlayerMotor.SpawnOnRoof(n.Train, 5, 0, P);
        if (!telemetry)
            n.World.Encounters = null;
        n.Run(240);
        return n;
    }

    [Fact]
    public void TheLogOnlyWatchesAndTheSameNightReportsTheSame()
    {
        var on = Busy(true);
        var off = Busy(false);
        // Nothing reads it back: the director sent the same, and the creatures are where they were, with it off.
        Assert.Equal(off.World.Director!.Log, on.World.Director!.Log);
        Assert.Equal(off.World.ActiveEnemies.Select(e => (e.Id, e.Kind, e.Phase, e.WorldPosition(off.Train))),
            on.World.ActiveEnemies.Select(e => (e.Id, e.Kind, e.Phase, e.WorldPosition(on.Train))));
        var log = on.World.Encounters!;
        Assert.NotEmpty(log.Attempts);
        // Every placed attempt is an encounter of its kind, its members its own (nobody's counted twice).
        foreach (var a in log.Attempts.Where(a => a.Placed))
            Assert.Equal(a.Kind, log.Encounters[a.Encounter].Kind);
        var members = log.Encounters.SelectMany(e => e.Members).ToList();
        Assert.Equal(members.Count, members.Distinct().Count());
        // Deterministic: the same night twice, the same block.
        var again = Busy(true);
        Assert.Equal(JsonSerializer.Serialize(EncounterReport.Of(log, on.World)), JsonSerializer.Serialize(EncounterReport.Of(again.World.Encounters!, again.World)));
    }
}
