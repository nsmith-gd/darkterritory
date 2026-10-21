using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The one pacing model in the director (the spawn director overhaul's brief §2 issues 1 and 2, §3; note 908): the encounter
/// clock owes λ, every placed encounter pays it, and the free channel sends what's owed. Here the pressure never spends, so
/// the free channel is all there is.
/// </summary>
public class OnePacingModelTests
{
    static readonly DirectorTuning D = Tuning.Enemies.Director;

    /// <summary>Only the rule-less about (as NightCurveTests), a pressure that never spends, the free spawns not waiting for a first threat.</summary>
    static EnemyTuning Driven(bool on = true) => Tuning.Enemies with
    {
        Director = D with
        {
            Pressure = D.Pressure with { Threshold = 1e9, Max = 1e9, PressAt = 1e9 },
            Curve = D.Curve with { AfterFirst = false },
            NightChance = D.Costs.Keys.Where(k => !D.Curve.Kinds.ContainsKey(k)).ToDictionary(k => k, _ => 0.0),
            Encounters = D.Encounters with { Drive = D.Encounters.Drive with { On = on } },
        },
    };

    /// <summary>A world on a plain 40 km line (no route: progress is the distance over 20 km), the train stood <paramref name="at"/> m along.</summary>
    static World Plain(EnemyTuning tuning, double at, int crew, int cars = 6, ulong seed = 1)
    {
        var line = new Rail.RailLine(new Rail.LineDefinition("t", [new Rail.TrackSegment(40_000)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, cars, 1)), line, at);
        var world = new World(train, Tuning.Combat);
        world.EnableEnemies(tuning, null, seed, crew, authority: true);
        return world;
    }

    static int Sent(World world, int seconds)
    {
        int sent = 0;
        for (int i = 0; i < seconds; i++)
            if (world.Director!.Decide(world, 1_000 + i, [], 500) is not null)
                sent++;
        return sent;
    }

    /// <summary>
    /// The free channel sends about what the clock owes over the time it ran (the brief §3: λ's encounters, not a roll that
    /// may never land), and each one paid the clock.
    /// </summary>
    [Fact]
    public void TheFreeChannelSendsWhatTheClockOwes()
    {
        var world = Plain(Driven(), at: 10_000, crew: 1);
        int sent = Sent(world, 1_800);
        var d = world.Director!;
        double owed = d.EncounterRate * 30;
        Assert.True(d.EncounterRate > 0);
        Assert.InRange(sent, (int)Math.Floor(owed * 0.7), (int)Math.Ceiling(owed * 1.2) + 1);
        Assert.All(d.Log, l => Assert.True(l.Free));
        Assert.True(d.Pacing.Due < d.Pacing.Threshold + 1.0 / 60 * d.EncounterRate + 1e-9);
    }

    /// <summary>Issue 2: more crew, more free encounters, not fewer (the old crew fade gave four a half of one's).</summary>
    [Fact]
    public void MoreCrewMoreFreeEncounters()
    {
        int one = 0, eight = 0;
        for (ulong seed = 1; seed <= 4; seed++)
        {
            one += Sent(Plain(Driven(), at: 15_000, crew: 1, seed: seed), 1_200);
            eight += Sent(Plain(Driven(), at: 15_000, crew: 8, seed: seed), 1_200);
        }
        Assert.True(eight > one, $"solo {one}, eight {eight}");
    }

    /// <summary>A longer train, more free encounters (the brief §1 C), at the same crew.</summary>
    [Fact]
    public void ALongerTrainMoreFreeEncounters()
    {
        int short_ = 0, long_ = 0;
        for (ulong seed = 1; seed <= 4; seed++)
        {
            short_ += Sent(Plain(Driven(), at: 15_000, crew: 2, cars: 3, seed: seed), 1_200);
            long_ += Sent(Plain(Driven(), at: 15_000, crew: 2, cars: 18, seed: seed), 1_200);
        }
        Assert.True(long_ > short_, $"3 cars {short_}, 18 cars {long_}");
    }

    /// <summary>Issue 1: the pressure's own rate for a solo crew on a short train is the rate's floor, not a third of a four's.</summary>
    [Fact]
    public void ASoloCrewsPressureIsNoLongerAThird()
    {
        var on = Plain(Driven(), at: 10_000, crew: 1, cars: 2);
        var off = Plain(Driven(on: false), at: 10_000, crew: 1, cars: 2);
        Sent(on, 1);
        Sent(off, 1);
        Assert.True(on.Director!.Terms.Rate > off.Director!.Terms.Rate * 2, $"on {on.Director.Terms.Rate}, off {off.Director.Terms.Rate}");
    }

    /// <summary>
    /// <c>drive.pressureGate</c> (note 908): a pressure pressed every second still waits for the clock to owe one; off (null),
    /// it spends as it would alone.
    /// </summary>
    [Fact]
    public void ThePressureGateHoldsThePressuresSpendsForTheClock()
    {
        static EnemyTuning Pressed(double? gate) => Tuning.Enemies with
        {
            Director = D with
            {
                Pressure = D.Pressure with { Threshold = 1, PressAt = 1, Start = 18, Max = 18, ReliefPerCost = 0 },
                Draw = D.Draw with { Enabled = false },
                Curve = D.Curve with { Free = false },
                Encounters = D.Encounters with { Drive = D.Encounters.Drive with { PressureGate = gate } },
            },
        };
        var off = Plain(Pressed(null), at: 10_000, crew: 2);
        var held = Plain(Pressed(1e9), at: 10_000, crew: 2);
        Assert.True(Sent(off, 600) > 0, "a pressed pressure with no gate sends");
        Assert.Equal(0, Sent(held, 600));
        Assert.Equal("pacing", held.Director!.HeldBecause);
    }

    /// <summary>The same night twice is the same night (replays, the host's).</summary>
    [Fact]
    public void ItIsDeterministic()
    {
        var a = Plain(Driven(), at: 12_000, crew: 3, seed: 9);
        var b = Plain(Driven(), at: 12_000, crew: 3, seed: 9);
        Assert.Equal(Sent(a, 900), Sent(b, 900));
        Assert.Equal(a.Director!.Pacing, b.Director!.Pacing);
        Assert.Equal(a.Director.Log.Select(l => (l.Tick, l.Kind)), b.Director.Log.Select(l => (l.Tick, l.Kind)));
    }
}
