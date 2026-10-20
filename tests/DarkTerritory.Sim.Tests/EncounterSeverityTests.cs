using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Encounter severity and droughts (the director's brief §4, §6; queue #610, note 893), from world state. A creature lying in
/// wait near the train is presence (level 1), not an encounter met. One threatening or engaging is harassment (level 2). Two
/// harassing apart is a compound crisis (level 3). A quiet second is the director's rest, presence, or a true drought, and an
/// active danger outranks the rest.
/// </summary>
public class EncounterSeverityTests
{
    static readonly PlayerTuning P = Tuning.Player;

    [Fact]
    public void ALevelIsReadFromTheMembersStates()
    {
        Assert.Equal(0, EncounterSeverity.Level([ThreatState.Dormant], present: false));
        Assert.Equal(1, EncounterSeverity.Level([ThreatState.Dormant], present: true));
        Assert.Equal(1, EncounterSeverity.Level([ThreatState.Ambient, ThreatState.Resolved], present: true));
        Assert.Equal(0, EncounterSeverity.Level([ThreatState.Resolved], present: true));
        // One of a pack at the crew is the pack harassing them, near or not.
        Assert.Equal(2, EncounterSeverity.Level([ThreatState.Dormant, ThreatState.Threatening], present: false));
        Assert.Equal(2, EncounterSeverity.Level([ThreatState.Engaging], present: false));
    }

    [Fact]
    public void TwoProblemsApartAreACompoundCrisisAndTwoTogetherAreNot()
    {
        var a = new Double3(0, 0, 0);
        Assert.Equal([true, true], EncounterSeverity.Compound([a, new Double3(0, 0, -20)], 15));
        Assert.Equal([false, false], EncounterSeverity.Compound([a, new Double3(0, 0, -5)], 15));
        // Three on one car are one fight; a third far down the train makes every one of them a crisis.
        Assert.Equal([false, false, false], EncounterSeverity.Compound([a, new Double3(0, 0, -5), new Double3(0, 0, -10)], 15));
        Assert.Equal([true, true, true], EncounterSeverity.Compound([a, new Double3(0, 0, -5), new Double3(0, 0, -40)], 15));
        Assert.Equal([false], EncounterSeverity.Compound([a], 0));
        // 0 apart: any two at once.
        Assert.Equal([true, true], EncounterSeverity.Compound([a, a], 0));
    }

    [Fact]
    public void AnActiveDangerOutranksTheRestAndTheRestOutranksPresence()
    {
        Assert.Equal(QuietKind.Sustained, EncounterSeverity.Classify(2, resting: true));
        Assert.Equal(QuietKind.Sustained, EncounterSeverity.Classify(3, resting: false));
        Assert.Equal(QuietKind.Recovery, EncounterSeverity.Classify(1, resting: true));
        Assert.Equal(QuietKind.Presence, EncounterSeverity.Classify(1, resting: false));
        Assert.Equal(QuietKind.Drought, EncounterSeverity.Classify(0, resting: false));
        string[] holds = Tuning.Enemies.Director.Telemetry.RecoveryHolds;
        Assert.True(EncounterSeverity.Resting("banned (grace)", holds));
        Assert.True(EncounterSeverity.Resting("aftermath", holds));
        Assert.False(EncounterSeverity.Resting("building", holds));
        Assert.False(EncounterSeverity.Resting("sent", holds));
    }

    /// <summary>A stand-in that does nothing but what the test sets: its kind, its phase, on a car.</summary>
    sealed class Still(int id, EnemyKind kind) : Enemy(id)
    {
        public override EnemyKind Kind => kind;
        public override PressureZone Zone => PressureZone.Interior;
        public override Sense Sense => Sense.Sound;
        protected override void Tick(EnemyContext ctx)
        {
        }
    }

    static Still Put(Night n, EnemyKind kind, int car, SpinePhase phase = SpinePhase.Dormant)
    {
        var e = n.World.AddEnemy(id => new Still(id, kind));
        e.Restore(phase, 0, 1, car, default, 0, 0, 0, 0, 0);
        return e;
    }

    /// <summary>A night the director sends nothing on, the crew in the cab and on car 3's roof, run past its grace.</summary>
    static Night Quiet()
    {
        var n = new Night(6, 14, enemies: HoundRunTests.Quiet with { Director = HoundRunTests.Quiet.Director with { Roster = ["none"] } });
        n.Crew[1] = PlayerMotor.SpawnInCab(n.Train, P);
        n.Crew[2] = PlayerMotor.SpawnOnRoof(n.Train, 3, 0, P);
        n.Run(n.World.Director!.Grace + 2);
        return n;
    }

    [Fact]
    public void SomethingLyingInWaitNearTheTrainIsPresenceNotAnEncounterMet()
    {
        var n = Quiet();
        var log = n.World.Encounters!;
        Put(n, EnemyKind.Whistler, 5);
        n.Run(5);
        var e = Assert.Single(log.Encounters);
        Assert.Equal(1, e.Severity);
        Assert.False(e.Meaningful);
        Assert.Equal(QuietKind.Presence, log.Now);
        Assert.Equal(0, log.DroughtNow);
    }

    [Fact]
    public void OneAtTheCrewIsHarassmentAndTwoApartACompoundCrisis()
    {
        var n = Quiet();
        var log = n.World.Encounters!;
        var first = Put(n, EnemyKind.Climber, 1, SpinePhase.Alert);
        n.Run(3);
        var one = Assert.Single(log.Encounters);
        Assert.Equal(2, one.Severity);
        Assert.True(one.Meaningful && one.HarassedAt >= 0);
        Assert.Equal(-1, one.CompoundAt);
        Assert.Equal(QuietKind.Sustained, log.Now);
        Put(n, EnemyKind.Climber, 5, SpinePhase.Alert);
        n.Run(3);
        Assert.Equal(2, log.Encounters.Count);
        Assert.All(log.Encounters, e => Assert.Equal(3, e.Severity));
        Assert.All(log.Encounters, e => Assert.True(e.CompoundAt >= 0 && e.SecondsAt[3] > 0));
        // The first, gone, leaves the second one problem, not a crisis.
        first.Dismiss();
        n.Run(3);
        Assert.True(log.Encounters[1].SecondsAt[2] > 0);
        Assert.Equal(EncounterReport.Of(log, n.World).FirstSeconds[1], one.HarassedAt);
    }

    [Fact]
    public void NothingAboutAndNoRestIsATrueDroughtAndTheGraceIsNot()
    {
        var n = Quiet();
        var log = n.World.Encounters!;
        n.Run(log.Tuning.DroughtSeconds + 5);
        Assert.Equal(QuietKind.Drought, log.Now);
        Assert.True(log.DroughtNow >= log.Tuning.DroughtSeconds);
        var report = EncounterReport.Of(log, n.World);
        var drought = Assert.Single(report.TrueDroughts);
        Assert.True(drought.Seconds >= log.Tuning.DroughtSeconds);
        // The grace was the director's rest, not part of the drought.
        Assert.True(report.Quiet["recovery"] > 0);
        Assert.True(drought.From >= n.World.Director!.Grace - 1);
        // Something come to lie in wait by the train ends it: presence, not a drought.
        Put(n, EnemyKind.Whistler, 4);
        n.Run(2);
        Assert.Equal(QuietKind.Presence, log.Now);
        Assert.Equal(0, log.DroughtNow);
    }
}
