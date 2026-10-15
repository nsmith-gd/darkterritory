using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The director's creature calls of 12 Oct 2026 (note 635; the inbox): the Knotter "once or twice a run. Sometimes never"
/// (<c>director.perNight</c>, <c>director.nightChance</c>), and the night things run at dawn, all but the Knotter and Hotbox,
/// "parasites that have train functions" (wakers.md §2; <c>wakers.nightFleesSeconds</c>, <c>wakers.staysAtDawn</c>).
/// </summary>
public class NightCallsTests
{
    static readonly EnemyTuning E = Tuning.Enemies;
    static readonly EnemyTuning Quiet = E with { Director = E.Director with { GraceMinSeconds = 1e9, GraceMaxSeconds = 1e9 } };

    [Fact]
    public void TheTuningIsTheDirectorsCall()
    {
        Assert.InRange(E.Director.PerNight["knotter"], 1, 2);
        Assert.InRange(E.Director.NightChance["knotter"], 0.5, 0.95);
        Assert.True(E.Wakers.NightFleesSeconds > 0);
        Assert.Equal(["hotbox", "knotter"], E.Wakers.StaysAtDawn.Order());
        Assert.False(E.Wakers.Flees(EnemyKind.Knotter));
        Assert.False(E.Wakers.Flees(EnemyKind.Hotbox));
        Assert.True(E.Wakers.Flees(EnemyKind.Climber));
        Assert.True(E.Wakers.Flees(EnemyKind.CinderHound));
    }

    [Fact]
    public void SomeNightsHaveNoKnotterAndTheSameSeedIsTheSameNight()
    {
        var t = E.Director with { NightChance = new() { ["knotter"] = 0.75 } };
        int seeds = 400;
        int out_ = Enumerable.Range(1, seeds).Count(s => new Director(t, null, (ulong)s, 6, 4).OutTonight(EnemyKind.Knotter));
        Assert.InRange(out_ / (double)seeds, 0.17, 0.33);
        for (ulong s = 1; s <= 20; s++)
            Assert.Equal(new Director(t, null, s, 6, 4).OutTonight(EnemyKind.Knotter), new Director(t, null, s, 3, 8).OutTonight(EnemyKind.Knotter));
        // A kind not named is always about; and a night's roll draws nothing from the director's own dice.
        Assert.DoesNotContain(Enumerable.Range(1, 50), s => new Director(t, null, (ulong)s, 6, 4).OutTonight(EnemyKind.Climber));
        var none = E.Director with { NightChance = [] };
        for (ulong s = 1; s <= 20; s++)
            Assert.Equal(new Director(none, null, s, 6, 4).NextRange(0, 1), new Director(t, null, s, 6, 4).NextRange(0, 1));
    }

    [Fact]
    public void WhatHasntComeTonightComesOnMoreAndEachOneSentWeighsItsKindDown()
    {
        // Note 742 (the director's second playtest, 14 Oct: "very one note ... the cinder hounds like three times"): across the
        // whole night, an unseen kind weighs more and every one of a kind already come weighs it down, its own way too.
        Assert.InRange(E.Director.UnseenWeight, 1.5, 4);
        Assert.InRange(E.Director.NightRepeat, 0.3, 0.9);
        var d = new Director(E.Director with { NightChance = [] }, null, 1, 6, 4);
        Assert.Equal(E.Director.UnseenWeight, d.NightVariety(EnemyKind.CinderHound));
        for (int i = 0; i < 3; i++)
            d.Log.Add(new DirectorSpawn((uint)i, EnemyKind.CinderHound, 2, 0, 0, 0));
        Assert.Equal(Math.Pow(E.Director.NightRepeat, 3), d.NightVariety(EnemyKind.CinderHound), 9);
        // Against a kind that hasn't come, three hounds in: the hound weighs a tenth or less of what it did.
        Assert.True(d.NightVariety(EnemyKind.CinderHound) / d.NightVariety(EnemyKind.Moose) < 0.1);
        d.Counted(EnemyKind.Householder);
        Assert.Equal(E.Director.NightRepeat, d.NightVariety(EnemyKind.Householder), 9);
        // Off (both 1), it's as it was.
        var off = new Director(E.Director with { NightChance = [], UnseenWeight = 1, NightRepeat = 1 }, null, 1, 6, 4);
        off.Log.Add(new DirectorSpawn(0, EnemyKind.CinderHound, 2, 0, 0, 0));
        Assert.Equal(1, off.NightVariety(EnemyKind.CinderHound));
        Assert.Equal(1, off.NightVariety(EnemyKind.Moose));
    }

    [Fact]
    public void TheDirectorSendsNoMoreKnottersThanItsNightsCap()
    {
        var n = new Night(dawn: 3600);
        var d = new Director(E.Director with { PerNight = new() { ["knotter"] = 2 }, NightChance = [] }, null, 1, 6, 4);
        for (int i = 0; i < 2; i++)
        {
            Assert.True(d.Tonight(EnemyKind.Knotter, n.World));
            d.Log.Add(new DirectorSpawn((uint)i, EnemyKind.Knotter, 3, 0, 0, 0));
        }
        Assert.False(d.Tonight(EnemyKind.Knotter, n.World));
        Assert.True(d.Tonight(EnemyKind.Climber, n.World));
    }

    [Fact]
    public void ADefaultCapHoldsEveryKindWithoutItsOwn()
    {
        // Note 654 (the director's D1-roster-piles-up): off, nothing but the named caps; on, each kind not named stops at it,
        // the house creatures counted as the director's own are, and a kind's own cap still wins (the Knotter's 2 over 1).
        Assert.Null(E.Director.PerNightDefault);
        var n = new Night(dawn: 3600);
        var d = new Director(E.Director with { PerNight = new() { ["knotter"] = 2 }, PerNightDefault = 1, NightChance = [] }, null, 1, 6, 4);
        Assert.True(d.Tonight(EnemyKind.Dragger, n.World));
        d.Log.Add(new DirectorSpawn(0, EnemyKind.Dragger, 2, 0, 0, 0));
        Assert.False(d.Tonight(EnemyKind.Dragger, n.World));
        Assert.True(d.Tonight(EnemyKind.Whistler, n.World));
        Assert.True(d.Tonight(EnemyKind.Lodger, n.World));
        d.Counted(EnemyKind.Lodger);
        Assert.False(d.Tonight(EnemyKind.Lodger, n.World));
        d.Log.Add(new DirectorSpawn(1, EnemyKind.Knotter, 3, 0, 0, 0));
        Assert.True(d.Tonight(EnemyKind.Knotter, n.World));
        d.Log.Add(new DirectorSpawn(2, EnemyKind.Knotter, 3, 0, 0, 0));
        Assert.False(d.Tonight(EnemyKind.Knotter, n.World));
    }

    [Fact]
    public void AMinuteBeforeDawnTheNightThingsGoToGroundButTheParasitesStay()
    {
        // Every kind about tonight (note 652's roster would leave the Climbers or the Hotbox out on this seed).
        var n = new Night(dawn: E.Wakers.NightFleesSeconds + 2, EveryKind.Of(Quiet));
        var train = n.World.Train;
        var shape = train.Frames[3].Shape;
        var hound = n.World.AddEnemy(id => new CinderHound(id, 900) { Health = E.CinderHounds.Health });
        hound.Restore(SpinePhase.Commit, 0, E.CinderHounds.Health, 3, new Double3(0.6, shape.RoofHeight, 0), 0, 0, 0, 900, 0);
        var climber = n.World.AddEnemy(id => Climber.Pacing(id, train, 2, 1, E.Climbers));
        var knotter = n.World.AddEnemy(id => Knotter.Into(id, train, 4, E.Knotter));
        var hotbox = n.World.AddEnemy(id => Hotbox.In(id, train, 5, true, 1, E.Hotbox));
        var d = n.World.Director!;

        n.Step(1);
        Assert.False(hound.Gone);
        Assert.False(climber.Gone);
        Assert.True(d.Tonight(EnemyKind.Climber, n.World));
        Assert.Equal(0, n.World.FledAtDawn);

        n.Step(2);
        Assert.True(hound.Gone);
        Assert.True(climber.Gone);
        Assert.False(knotter.Gone);
        Assert.False(hotbox.Gone);
        Assert.Equal(2, n.World.FledAtDawn);
        // And the director sends none of them now, but what stays.
        Assert.False(d.Tonight(EnemyKind.Climber, n.World));
        Assert.Equal(!d.OutTonight(EnemyKind.Knotter), d.Tonight(EnemyKind.Knotter, n.World));
        Assert.True(d.Tonight(EnemyKind.Hotbox, n.World));
    }

    [Fact]
    public void ANightOverStrandedSendsItsNightThingsAway()
    {
        // Note 661 (the director, stranded at Voss Green, 13 Oct: "now I've been picked up by something, even though I'm
        // stranded, but I'm not dead"): the engine broken down at a stand ends the night, and its night things go as at dawn.
        var n = new Night(dawn: 3600, EveryKind.Of(Quiet));
        var train = n.World.Train;
        n.World.Run!.Resume(900, -1, train.Boiler.Tender, 0);
        var shape = train.Frames[3].Shape;
        var hound = n.World.AddEnemy(id => new CinderHound(id, 900) { Health = E.CinderHounds.Health });
        hound.Restore(SpinePhase.Commit, 0, E.CinderHounds.Health, 3, new Double3(0.6, shape.RoofHeight, 0), 0, 0, 0, 900, 0);
        var climber = n.World.AddEnemy(id => Climber.Pacing(id, train, 2, 1, E.Climbers));
        var knotter = n.World.AddEnemy(id => Knotter.Into(id, train, 4, E.Knotter));
        var d = n.World.Director!;
        n.Step(1);
        Assert.False(hound.Gone);
        Assert.False(climber.Gone);

        // No wrench aboard to mend it (note 660: with one, the crew have their time first): stranded at once.
        n.NoWrench();
        train.Vehicles[0].Integrity = 0;
        n.Step(3);
        Assert.Equal(Sim.Run.RunEnd.Stranded, n.World.Run.End);
        Assert.True(hound.Gone);
        Assert.True(climber.Gone);
        Assert.False(knotter.Gone);
        Assert.False(d.Tonight(EnemyKind.Climber, n.World));
    }

    [Fact]
    public void OffTheNightThingsStay()
    {
        var off = Quiet with { Wakers = Quiet.Wakers with { NightFleesSeconds = -1 } };
        var n = new Night(dawn: 1, off);
        var climber = n.World.AddEnemy(id => Climber.Pacing(id, n.World.Train, 2, 1, E.Climbers));
        n.Step(2);
        Assert.False(climber.Gone);
        Assert.Equal(0, n.World.FledAtDawn);
    }

    /// <summary>A train stood out on frontier:7 with a crew of one in the cab, dawn <c>dawn</c> s into the night.</summary>
    sealed class Night
    {
        public readonly World World;
        PlayerState _player;

        public Night(double dawn, EnemyTuning? enemies = null)
        {
            var route = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, 7) with { DawnSeconds = dawn };
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), route.Build(), 4000, Tuning.Boiler);
            World = new World(train, Tuning.Combat);
            World.EnableRun(Tuning.Run, route, 600, authority: true);
            World.EnableEnemies(enemies ?? Quiet, null, 1, crew: 1, authority: true);
            _player = PlayerMotor.SpawnInCab(train, Tuning.Player);
        }

        /// <summary>The crew's wrench gone, and the cab rack's: nothing aboard to mend the engine with (note 660).</summary>
        public void NoWrench()
        {
            _player = _player with { Kit = Kit.Of([]) };
            World.Train.Boiler.WrenchOut = true;
        }

        public void Step(double seconds)
        {
            var controls = new TrainControls { Reverser = 1, Brake = 1 };
            for (int i = 0; i < seconds * SimConstants.TickRate; i++)
            {
                World.BeginTick();
                World.CrewAct(ref _player, default, 1);
                World.Step(controls);
                World.ApplyDamage(id => _player, (_, s) => _player = s, [1]);
                PlayerMotor.Step(ref _player, default, World.Train, Tuning.Player, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
                World.StepRun([_player]);
            }
        }
    }
}
