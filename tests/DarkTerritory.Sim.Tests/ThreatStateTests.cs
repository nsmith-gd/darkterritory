using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The engagement states (the director overhaul's B2, queue #604, ARCHITECTURE §8 note 881; the director's brief,
/// docs/design/spawn-director-overhaul.md §2 issue 4): each enemy's <see cref="ThreatState"/> read from what it is doing,
/// never from distance alone, and a creature doing the train harm with nobody near it is a threat all the same.
/// </summary>
public class ThreatStateTests
{
    static readonly ThreatTuning T = Tuning.Enemies.Director.Threats;

    static Double3 Near(Night n, Enemy e, double metres) => e.WorldPosition(n.Train) + new Double3(metres, 0, 0);

    [Theory]
    [InlineData(EnemyKind.Dragger)]
    [InlineData(EnemyKind.Moose)]
    [InlineData(EnemyKind.Householder)]
    [InlineData(EnemyKind.Hanger)]
    public void WhatLiesInWaitIsDormantEvenWithSomeoneBesideIt(EnemyKind kind)
    {
        var n = new Night(4, speed: 0);
        var e = n.World.AddEnemy(id => Enemy.Blank(kind, id));
        Assert.Equal(ThreatState.Dormant, Threats.Of(e, n.World, [Near(n, e, 2)], T));
        Assert.False(Threats.Counts(Threats.Of(e, n.World, [Near(n, e, 2)], T)));
    }

    [Fact]
    public void OnePacingTheTrainIsAmbientFarFromTheCrewAndThreateningNearThem()
    {
        var n = new Night(4, speed: 12);
        var climber = n.World.AddEnemy(id => new Climber(id));
        climber.Restore(SpinePhase.Dormant, 0, 3, 2, new Double3(0, 4, 0), 0, 0, 0, 0, 0);
        // Cars away (a solo crew in the cab, it pacing the tail): about, but not the crew's problem yet.
        Assert.Equal(ThreatState.Ambient, Threats.Of(climber, n.World, [Near(n, climber, T.NearMetres * 3)], T));
        Assert.Equal(ThreatState.Ambient, Threats.Of(climber, n.World, [], T));
        // Someone in its car: it is.
        Assert.Equal(ThreatState.Threatening, Threats.Of(climber, n.World, [Near(n, climber, T.NearMetres / 2)], T));
        // Coming at them: engaging, wherever anyone stands.
        climber.Restore(SpinePhase.Telegraph, 0, 3, 2, new Double3(0, 4, 0), 0, 0, 0, 0, 0);
        Assert.Equal(ThreatState.Engaging, Threats.Of(climber, n.World, [], T));
    }

    [Fact]
    public void AHotboxInItsTruckThreatensWithNobodyNear()
    {
        var n = new Night(4, speed: 12);
        var hotbox = n.World.AddEnemy(id => Hotbox.In(id, n.Train, 2, rear: true, side: 1, Tuning.Enemies.Hotbox));
        Assert.Equal(SpinePhase.Dormant, hotbox.Phase);
        Assert.True(hotbox.Working(n.World));
        Assert.Equal(ThreatState.Threatening, Threats.Of(hotbox, n.World, [], T));
    }

    [Fact]
    public void AKnotterInACouplingThreatensWithNobodyNear()
    {
        var n = new Night(4, speed: 12);
        var knotter = n.World.AddEnemy(id => Knotter.Into(id, n.Train, 1, Tuning.Enemies.Knotter));
        Assert.Equal(ThreatState.Threatening, Threats.Of(knotter, n.World, [], T));
    }

    /// <summary>
    /// Note 906 (n1-A1's measure: a taut Knotter 34–50 m off held a solo cap for minutes as "engaging"): a Knotter that's the
    /// coupling is at its work, threatening, in its Telegraph or not; coiled round someone, it's engaging. With waitsEngage, as
    /// #604 had it.
    /// </summary>
    [Fact]
    public void AKnottersTautWaitIsWorkNotAnEngagement()
    {
        var n = new Night(4, speed: 12);
        var knotter = n.World.AddEnemy(id => Knotter.Into(id, n.Train, 1, Tuning.Enemies.Knotter));
        knotter.Restore(SpinePhase.Telegraph, 30, 3, 1, knotter.Local, 0, 0, (double)KnotterMode.Taut, 0, 0);
        Assert.False(T.WaitsEngage);
        Assert.Equal(ThreatState.Threatening, Threats.Of(knotter, n.World, [], T));
        Assert.Equal(ThreatState.Engaging, Threats.Of(knotter, n.World, [], T with { WaitsEngage = true }));
        knotter.Restore(SpinePhase.Commit, 0, 3, 1, knotter.Local, 0, 0, (double)KnotterMode.Coil, 0, 0, holding: 0);
        Assert.Equal(ThreatState.Engaging, Threats.Of(knotter, n.World, [], T));
    }

    /// <summary>Note 906: a Whistler watching its gap (its Commit, the train stood: 4 minutes at a stop) waits; carrying someone, it's engaging.</summary>
    [Fact]
    public void AWhistlerWatchingItsGapWaits()
    {
        var n = new Night(4, speed: 0);
        var whistler = n.World.AddEnemy(id => Enemy.Blank(EnemyKind.Whistler, id));
        whistler.Restore(SpinePhase.Commit, 240, 3, 2, new Double3(0, 1, 0), 0, 0, 0, 0, 0);
        Assert.NotEqual(ThreatState.Engaging, Threats.Of(whistler, n.World, [], T));
        Assert.False(Threats.Counts(Threats.Of(whistler, n.World, [], T)));
        // Someone at its gap: threatening (they're where it is).
        Assert.Equal(ThreatState.Threatening, Threats.Of(whistler, n.World, [Near(n, whistler, 2)], T));
        Assert.Equal(ThreatState.Engaging, Threats.Of(whistler, n.World, [], T with { WaitsEngage = true }));
        whistler.Restore(SpinePhase.Grab, 0, 3, -1, new Double3(5, 0, 0), 0, 0, 0, 0, 0, holding: 0);
        Assert.Equal(ThreatState.Engaging, Threats.Of(whistler, n.World, [], T));
    }

    [Fact]
    public void APackFeedingAboardThreatensWithNobodyNear()
    {
        var n = new Night(4, speed: 0);
        var hound = n.World.AddEnemy(id => new CinderHound(id, 0));
        // Its first tick reads the hounds' tuning (stayAboard).
        n.Run(1.0 / SimConstants.TickRate);
        hound.Restore(SpinePhase.Dormant, 5, 2, 3, new Double3(0, 2.4, 0), n.Train.Dynamics.Distance, 0, 0.6, 0, 0);
        Assert.True(hound.StaysAboard);
        Assert.Equal(ThreatState.Threatening, Threats.Of(hound, n.World, [], T));
    }

    [Fact]
    public void WhatsDoneWithIsResolvedAndWhatHuntsNobodyNeverCounts()
    {
        var n = new Night(4, speed: 12);
        var climber = n.World.AddEnemy(id => new Climber(id));
        climber.Restore(SpinePhase.BreakOff, 0, 3, -1, default, 0, 0, 0, 0, 0);
        Assert.Equal(ThreatState.Resolved, Threats.Of(climber, n.World, [], T));
        climber.Restore(SpinePhase.Gone, 0, 3, -1, default, 0, 0, 0, 0, 0);
        Assert.Equal(ThreatState.Resolved, Threats.Of(climber, n.World, [], T));
        var gannet = n.World.AddEnemy(id => Enemy.Blank(EnemyKind.Gannet, id));
        Assert.Equal(GannetMode.Away, ((Gannet)gannet).Mode);
        Assert.Equal(ThreatState.Resolved, Threats.Of(gannet, n.World, [], T));
        // The Mourners come for the dead (note 362): about, never on the caps.
        var mourner = n.World.AddEnemy(id => Enemy.Blank(EnemyKind.Mourners, id));
        Assert.False(Threats.Counts(Threats.Of(mourner, n.World, [Near(n, mourner, 1)], T)));
    }

    [Fact]
    public void EveryKindHasAStateAndTheSameEnemyAlwaysReadsTheSame()
    {
        // Deterministic and total: every kind reads a state, twice the same (the director reads it each second, host-side).
        var n = new Night(4, speed: 12);
        foreach (var kind in Enum.GetValues<EnemyKind>())
        {
            var e = n.World.AddEnemy(id => Enemy.Blank(kind, id));
            var a = Threats.Of(e, n.World, [], T);
            Assert.Equal(a, Threats.Of(e, n.World, [], T));
            Assert.True(Enum.IsDefined(a));
        }
    }
}

/// <summary>
/// The engaged cap counting threats, not bodies (note 881; the brief §2 issue 4: "An unseen monster several cars away should
/// not indefinitely suppress interesting encounters for a solo player"), with the fairness bound and the persistent creatures
/// kept.
/// </summary>
public class ThreatCapTests
{
    static Night Solo(EnemyTuning? enemies = null)
    {
        var n = new Night(8, speed: 12, enemies: enemies);
        n.Crew[1] = PlayerMotor.SpawnInCab(n.Train, Tuning.Player);
        n.Run(1.0 / SimConstants.TickRate);
        return n;
    }

    static Enemy LeftAtAYard(Night n)
    {
        // A yard's Freight Beetle the train has gone on from: 600 m back down the line, off to the side.
        var p = n.Train.Line.Sample(n.Train.Dynamics.Distance - 600);
        var side = Double3.Cross(p.Tangent, Double3.Up).Normalized;
        return n.World.AddEnemy(id => FreightBeetle.At(id, p.Position + side * 8, n.Train.Dynamics.Distance - 600, 0, Tuning.Enemies.FreightBeetle));
    }

    [Fact]
    public void WhatsLeftAtAStopOrPacingTheTailDoesntHoldASoloCrewsPlace()
    {
        var n = Solo();
        var d = n.World.Director!;
        var beetle = LeftAtAYard(n);
        var climber = n.World.AddEnemy(id => new Climber(id));
        climber.Restore(SpinePhase.Dormant, 0, 3, 7, new Double3(0, 4, 0), 0, 0, 0, 0, 0);
        // Both about (Director.Engaged, as the old cap counted them), neither on the crew: the cap is free.
        Assert.True(Director.Engaged(beetle));
        Assert.True(Director.Engaged(climber));
        Assert.Empty(d.Holding(n.World, n.World.ActiveEnemies));
        Assert.False(d.Crowded(n.World.ActiveEnemies));
        // Off (director.threats.on false), they held it, as before note 881.
        var off = Solo(Tuning.Enemies with { Director = Tuning.Enemies.Director with { Threats = new ThreatTuning { On = false } } });
        LeftAtAYard(off);
        Assert.Single(off.World.Director!.Holding(off.World, off.World.ActiveEnemies));
    }

    [Fact]
    public void AHotboxWithNobodyNearStillHoldsTheCap()
    {
        // A sustained problem is danger all the same: the Hotbox in the tail car's truck, the crew of one in the cab.
        var n = Solo();
        var hotbox = n.World.AddEnemy(id => Hotbox.In(id, n.Train, 7, rear: true, side: 1, Tuning.Enemies.Hotbox));
        Assert.Contains(hotbox, n.World.Director!.Holding(n.World, n.World.ActiveEnemies));
    }

    [Fact]
    public void BodiesAboutAreHeldToTheCapPlusTheAmbientRoom()
    {
        var n = Solo();
        var d = n.World.Director!;
        for (int i = 0; i < d.EngagedCap + Tuning.Enemies.Director.Threats.AmbientRoom; i++)
        {
            Assert.False(d.Crowded(n.World.ActiveEnemies));
            LeftAtAYard(n);
        }
        Assert.True(d.Crowded(n.World.ActiveEnemies));
    }

    [Fact]
    public void PersistentCreaturesKeepTheirPlacesOffTheCap()
    {
        // Off the cap is not dismissed (the brief: "Avoid solving this by indiscriminately despawning persistent monsters"): the
        // left-behind beetle and a Hotbox are both still there minutes on.
        var n = Solo();
        var beetle = LeftAtAYard(n);
        var hotbox = n.World.AddEnemy(id => Hotbox.In(id, n.Train, 7, rear: true, side: 1, Tuning.Enemies.Hotbox));
        n.Run(200);
        Assert.False(beetle.Gone);
        Assert.False(hotbox.Gone);
    }
}
