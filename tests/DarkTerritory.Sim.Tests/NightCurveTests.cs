using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The night's curve and the free spawns on it (GDD App. B.1 "The night's curve"; spec B.10; ARCHITECTURE §8 note 849; the
/// director's playtest of 16 Oct 2026: "The longer a run goes, the more monsters just spawn in general ... I want more
/// monsters to spawn the longer the run goes that don't have a rules system. And then I want the weighting of the likelihood
/// of spawn for monsters with rules that they have to follow ... to increase"). The pressure's escalation climbs by the curve;
/// beside the pressure, the rule-less come free on it, more the later the night, under the hard caps and the kinds' nights.
/// </summary>
public class NightCurveTests
{
    static readonly DirectorTuning D = Tuning.Enemies.Director;

    /// <summary>
    /// The night's lottery (nightChance) isn't what's tested: the rule-less (curve.kinds) are every one about tonight, and every
    /// other kind is out (a stood train on a straight line meets the Whistler's, the Dragger's and the Track Doll's rules).
    /// </summary>
    static readonly Dictionary<string, double> OnlyTheFree =
        D.Costs.Keys.Where(k => !D.Curve.Kinds.ContainsKey(k)).ToDictionary(k => k, _ => 0.0);

    /// <summary>The director's tuning with a pressure that never spends (the free spawns are what's left), and the free spawns not waiting for a first threat.</summary>
    static EnemyTuning Curved(bool afterFirst = false, CurveTuning? curve = null) => Tuning.Enemies with
    {
        Director = D with
        {
            Pressure = D.Pressure with { Threshold = 1e9, Max = 1e9, PressAt = 1e9 },
            Curve = (curve ?? D.Curve) with { AfterFirst = afterFirst },
            NightChance = OnlyTheFree,
        },
    };

    /// <summary>A world on a plain 40 km line (no route: the night's progress is the distance over 20 km), the train stood <paramref name="at"/> m along.</summary>
    static World Plain(EnemyTuning tuning, double at, ulong seed = 1, int crew = 4)
    {
        var line = new Rail.RailLine(new Rail.LineDefinition("t", [new Rail.TrackSegment(40_000)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), line, at);
        var world = new World(train, Tuning.Combat);
        world.EnableEnemies(tuning, null, seed, crew, authority: true);
        return world;
    }

    /// <summary>The director's next <paramref name="seconds"/> decisions, past its grace: what it sent, in order.</summary>
    static List<EnemyKind> Think(World world, int seconds, double elapsed = 1_000)
    {
        var sent = new List<EnemyKind>();
        for (int i = 0; i < seconds; i++)
            if (world.Director!.Decide(world, elapsed + i, [], 500) is { } kind)
                sent.Add(kind);
        return sent;
    }

    [Fact]
    public void TheCurveIsTheNightsProgressAndTheEscalationClimbsByIt()
    {
        Assert.Equal(1, D.Curve.Power);
        var early = Plain(Curved(), at: 1_000);
        var late = Plain(Curved(), at: 19_000);
        Think(early, 1);
        Think(late, 1);
        Assert.Equal(0.05, early.Director!.Curve, 9);
        Assert.Equal(0.95, late.Director!.Curve, 9);
        Assert.Equal(1 + D.Pressure.Escalation * 0.05, early.Director.Terms.Escalation, 9);
        Assert.Equal(1 + D.Pressure.Escalation * 0.95, late.Director.Terms.Escalation, 9);
        // The spec's table: ×4 at the end, 3 an hour free at the gate rising to 18.
        Assert.Equal(3, D.Pressure.Escalation);
        Assert.Equal(3, D.Curve.AtGate);
        Assert.Equal(18, D.Curve.AtEnd);
        Assert.Equal(18, D.Curve.PerHour(1));
        Assert.Equal(3, D.Curve.PerHour(0));
        // By the crew: the whole curve for one or two, a quarter less a crewmate over that, never under 0.35.
        Assert.Equal(1, D.Curve.Fade(1));
        Assert.Equal(1, D.Curve.Fade(2));
        Assert.Equal(0.75, D.Curve.Fade(3), 9);
        Assert.Equal(0.5, D.Curve.Fade(4), 9);
        Assert.Equal(0.35, D.Curve.Fade(8), 9);
        Assert.Equal(9, D.Curve.PerHour(1, 4), 9);
    }

    [Fact]
    public void TheRuleLessComeFreeWithoutTheirRuleAndOffTheBudget()
    {
        // A train stood still mid-line with nobody on the ground: no rule lets anything in (the hounds want speed, the
        // Ribbits a crew on the ground at a stop). The curve sends the rule-less anyway, late in the night.
        var world = Plain(Curved(), at: 19_000);
        var d = world.Director!;
        var sent = Think(world, 1_800);
        Assert.NotEmpty(sent);
        Assert.All(sent, k => Assert.Contains(Director.Key(k), D.Curve.Kinds.Keys));
        Assert.All(d.Log, l => Assert.True(l.Free));
        Assert.Equal(sent.Count, d.Log.Count);
        // Nothing of the budget's was spent on them, and they count toward the night (the kind's cap and its variety).
        Assert.Equal(0, d.Spent);
        Assert.Equal(sent.Count(k => k == sent[0]), d.SentTonight(sent[0]));
        // The spawn goes without its rule: Climbers to a gap of a stood train (their rule wants it moving).
        var ctx = new SpawnContext(world, world.Enemies!, d) { Free = true };
        Assert.Contains(EnemyKind.Climber, sent);
        var rule = Spawns.For(EnemyKind.Climber)!;
        Assert.Null(rule.Weight(ctx));
        Assert.True(rule.Free!(ctx));
        Assert.True(rule.Spawn(ctx));
        Assert.Contains(world.ActiveEnemies, e => e.Kind == EnemyKind.Climber);
    }

    [Fact]
    public void MoreComeTheLaterTheNight()
    {
        int early = 0, late = 0;
        for (ulong seed = 1; seed <= 6; seed++)
        {
            early += Think(Plain(Curved(), at: 1_000, seed), 1_800).Count;
            late += Think(Plain(Curved(), at: 19_000, seed), 1_800).Count;
        }
        // Half an hour at the gate (3 an hour) against half an hour at the end (18): about one and a half against nine.
        Assert.True(late > early * 3, $"{early} free spawns early across six nights, {late} late");
        Assert.True(late >= 6 * 4, $"{late} late across six nights");
    }

    [Fact]
    public void TheSpacingAndTheHardCapsHold()
    {
        var world = Plain(Curved(), at: 19_000);
        var d = world.Director!;
        var ticks = new List<double>();
        for (int i = 0; i < 1_800; i++)
            if (d.Decide(world, 1_000 + i, [], 500) is not null)
                ticks.Add(1_000 + i);
        Assert.True(ticks.Count >= 2);
        for (int i = 1; i < ticks.Count; i++)
            Assert.True(ticks[i] - ticks[i - 1] >= D.Curve.SpacingSeconds);
        // A crew of one meets one thing at a time (App. B.1; note 336): with one engaged, nothing comes free.
        var solo = Plain(Curved(), at: 19_000, crew: 1);
        Assert.Equal(1, solo.Director!.EngagedCap);
        var climber = solo.AddEnemy(id => new Climber(id));
        Assert.True(Director.Engaged(climber));
        for (int i = 0; i < 1_800; i++)
            Assert.Null(solo.Director.Decide(solo, 1_000 + i, [climber], 500));
    }

    [Fact]
    public void NothingComesFreeBeforeTheNightsFirstThreatNorWithTheCurveOff()
    {
        Assert.True(D.Curve.AfterFirst && D.Draw.Enabled);
        var drawn = Plain(Curved(afterFirst: true), at: 19_000);
        Assert.Empty(Think(drawn, 1_800));
        var off = Plain(Curved(curve: D.Curve with { Free = false }), at: 19_000);
        Assert.Empty(Think(off, 1_800));
        // The escalation is the curve's with the free spawns off too.
        Assert.Equal(1 + D.Pressure.Escalation * 0.95, off.Director!.Terms.Escalation, 9);
    }

    /// <summary>
    /// The curve fills the gaps the rules leave (curve.fill): the pressure past its threshold with no rule met spends on the
    /// rule-less instead of nothing ("nothing fits" was most of a two-player night), charged and paced as any spend.
    /// </summary>
    [Fact]
    public void ThePressureSpendsOnTheRuleLessWhenNoRuleFits()
    {
        Assert.True(D.Curve.Fill && D.Curve.FillPressed);
        // The pressure as it is (it spends), the free spawns of their own off: whatever comes is the fill.
        var filling = Tuning.Enemies with { Director = D with { Curve = D.Curve with { Free = false }, NightChance = OnlyTheFree } };
        var world = Plain(filling, at: 10_000);
        var d = world.Director!;
        var sent = Think(world, 600);
        Assert.NotEmpty(sent);
        Assert.All(sent, k => Assert.Contains(Director.Key(k), D.Curve.Kinds.Keys));
        Assert.All(d.Log, l => Assert.True(l.Free));
        // A spend: charged to the budget, the pressure relieved, the cooldown kept; and only once pressed (a long quiet).
        Assert.Equal(sent.Sum(d.Cost), d.Spent);
        Assert.True(d.Pressure < D.Pressure.PressAt);
        Assert.All(d.Log, l => Assert.True(l.Paced));
        var off = Plain(filling with { Director = filling.Director with { Curve = filling.Director.Curve with { Fill = false } } }, at: 10_000);
        Assert.Empty(Think(off, 600));
        Assert.Equal("nothing fits", off.Director!.HeldBecause);
    }

    [Fact]
    public void TheSameNightSendsTheSameFreeSpawns()
    {
        var a = Think(Plain(Curved(), at: 19_000, seed: 3), 1_800);
        var b = Think(Plain(Curved(), at: 19_000, seed: 3), 1_800);
        Assert.Equal(a, b);
    }

    [Fact]
    public void EachFreeKindNeedsOnlyWhatItsSpawnNeeds()
    {
        var world = Plain(Curved(), at: 19_000);
        var ctx = new SpawnContext(world, world.Enemies!, world.Director!) { Free = true };
        // Nobody on the ground: the ground kinds don't come; a train with gaps lets the Climbers in, and the hounds once it moves.
        Assert.False(Spawns.For(EnemyKind.CinderHound)!.Free!(ctx));
        Assert.True(Spawns.For(EnemyKind.Climber)!.Free!(ctx));
        // ... not a slow one (the hounds' own minTrainSpeed is their rule, kept), a quick one yes.
        world.Train.Dynamics.Velocity = 5;
        Assert.False(Spawns.For(EnemyKind.CinderHound)!.Free!(ctx));
        world.Train.Dynamics.Velocity = 10;
        Assert.True(Spawns.For(EnemyKind.CinderHound)!.Free!(ctx));
        world.Train.Dynamics.Velocity = 0;
        Assert.False(Spawns.For(EnemyKind.Ribbit)!.Free!(ctx));
        Assert.False(Spawns.For(EnemyKind.Follower)!.Free!(ctx));
        Assert.False(Spawns.For(EnemyKind.SootChildren)!.Free!(ctx));
        Assert.False(Spawns.For(EnemyKind.Gaunt)!.Free!(ctx));
        // Stood: the Moose can graze beside it.
        Assert.True(Spawns.For(EnemyKind.Moose)!.Free!(ctx));
        // What comes by its rule alone has no free gate.
        Assert.Null(Spawns.For(EnemyKind.Whistler)!.Free);
        Assert.Null(Spawns.For(EnemyKind.Gannet)!.Free);
        Assert.Null(Spawns.For(EnemyKind.TowerJaw)!.Free);
        foreach (var name in D.Curve.Kinds.Keys)
            Assert.Contains(Spawns.Rules, r => Director.Key(r.Kind) == name && r.Free is not null);
    }
}
