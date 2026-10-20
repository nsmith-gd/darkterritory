using Ballast;
using DarkTerritory.Sim.Enemies;

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
        Assert.Equal(SpinePhase.Dormant, climber.Phase);
        // Cars away (a solo crew in the cab, it pacing the tail): about, but not the crew's problem yet.
        Assert.Equal(ThreatState.Ambient, Threats.Of(climber, n.World, [Near(n, climber, T.NearMetres * 3)], T));
        Assert.Equal(ThreatState.Ambient, Threats.Of(climber, n.World, [], T));
        // Someone in its car: it is.
        Assert.Equal(ThreatState.Threatening, Threats.Of(climber, n.World, [Near(n, climber, T.NearMetres / 2)], T));
        // Coming at them: engaging, wherever anyone stands.
        climber.Restore(SpinePhase.Telegraph, 0, 3, -1, default, 0, 0, 0, 0, 0);
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
