using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 915 (queue #612's diversity half; "test creature spawn first"): #590's night cap, its fresh kinds (Nell's, note 863,
/// ported) and a feeding pack sated are each their own flag, off in content until measured and decided; on, each does what it
/// says and no more.
/// </summary>
public class DiversityTrialTests
{
    static readonly DirectorTuning D = Tuning.Enemies.Director;

    /// <summary>A bare 40 km line at 13 m/s for 20 km, the director thinking a second at a time (note 863's ride).</summary>
    static (List<EnemyKind> Sent, Director D) Ride(DirectorTuning tuning, ulong seed = 3)
    {
        var line = new Rail.RailLine(new Rail.LineDefinition("t", [new Rail.TrackSegment(40_000)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 8, 1)), line, 0);
        var world = new World(train, Tuning.Combat);
        world.EnableEnemies(Tuning.Enemies with { Director = tuning }, null, seed, crew: 4, authority: true);
        var sent = new List<EnemyKind>();
        for (int t = 0; t < 20_000 / 13; t++)
        {
            train.Dynamics.Distance = 13.0 * t;
            if (world.Director!.Decide(world, t, [], 500) is { } kind)
                sent.Add(kind);
        }
        return (sent, world.Director!);
    }

    static DirectorTuning Capped(bool fresh) => D with { NightCap = D.NightCap with { On = true }, Fresh = D.Fresh with { On = fresh } };

    [Fact]
    public void TheCapTheFreshKindsAndTheSatedPackAreOffInContent()
    {
        Assert.False(D.NightCap.On);
        Assert.False(D.Fresh.On);
        Assert.False(Tuning.Enemies.CinderHounds.Sated.On);
        // Off, the cap is perNightDefault's (none) and a pack counts once, as the log has it (note 885).
        Assert.Equal(D.PerNightDefault, D.CapFor("cinderHounds"));
        Assert.True(D.PackCountsOnce);
    }

    [Fact]
    public void OnTheCapHoldsEveryKindToItsThreeAndAKindsOwnCapStillWins()
    {
        var capped = Capped(fresh: false);
        Assert.Equal(capped.NightCap.PerNightDefault, capped.CapFor("cinderHounds"));
        Assert.Equal(D.PerNight["knotter"], capped.CapFor("knotter"));
        var (sent, _) = Ride(capped);
        Assert.NotEmpty(sent);
        Assert.All(sent.GroupBy(k => k), g => Assert.True(g.Count() <= capped.CapFor(Director.Key(g.Key)), $"{g.Key} ×{g.Count()}"));
    }

    [Fact]
    public void OnFreshKindsComeWhenTheNightsKindsHaveHadTheirThree()
    {
        // Note 863 (the director, 19 Oct: "I don't want the night to run into a soft ending but I dont want repeats of the same
        // monsters over and over"): with the cap at 3, once a kind about tonight has had its three and nothing fits, a kind the
        // night's chances rolled out comes in, drawn by its chance among those whose rule fits; never one at a chance of 0.
        var (off, dOff) = Ride(Capped(fresh: false));
        var tried = Capped(fresh: true);
        var (on, dOn) = Ride(tried);
        Assert.Empty(dOff.FreshBrought);
        Assert.NotEmpty(dOn.FreshBrought);
        Assert.True(on.Count > off.Count, $"{on.Count} sent with fresh kinds, {off.Count} without");
        Assert.True(on.Distinct().Count() > off.Distinct().Count());
        foreach (var key in dOn.FreshBrought)
        {
            Assert.True(tried.ChanceFor(key, tried.NightChance.GetValueOrDefault(key, 0)) > 0, key);
            Assert.True(dOff.OutTonight(Spawns.Rules.First(r => Director.Key(r.Kind) == key).Kind), $"{key} was about anyway");
        }
        Assert.All(on.GroupBy(k => k), g => Assert.True(g.Count() <= tried.CapFor(Director.Key(g.Key)), $"{g.Key} ×{g.Count()}"));
        Assert.True(dOn.FreshBrought.Count <= tried.Fresh.MaxPerNight);
        // The same night brings the same; a chance of 0 is the director's never.
        Assert.Equal(dOn.FreshBrought, Ride(tried).D.FreshBrought);
        string first = dOn.FreshBrought[0];
        var never = Ride(tried with
        {
            NightChance = new(tried.NightChance) { [first] = 0 },
            NightChanceTrial = tried.NightChanceTrial with { Chances = new(tried.NightChanceTrial.Chances) { [first] = 0 } },
        }).D;
        Assert.DoesNotContain(first, never.FreshBrought);
    }

    [Fact]
    public void APackCountsOnceTowardItsCapUnlessToldOtherwise()
    {
        foreach (bool once in new[] { true, false })
        {
            var d = new Director(D with { PackCountsOnce = once, NightChance = [] }, null, 1, 6, 4);
            d.Log.Add(new DirectorSpawn(0, EnemyKind.CinderHound, 2, 0, 0, 0, Members: 4));
            Assert.Equal(once ? 1 : 4, d.SentTonight(EnemyKind.CinderHound));
        }
    }

    [Fact]
    public void OnASatedPackLeavesInsteadOfLightingAnother()
    {
        foreach (bool sated in new[] { false, true })
        {
            var quiet = HoundRunTests.Quiet;
            var enemies = quiet with
            {
                CinderHounds = quiet.CinderHounds with
                {
                    Patrol = quiet.CinderHounds.Patrol with { On = false },
                    Sated = quiet.CinderHounds.Sated with { On = sated },
                },
            };
            var n = new Night(4, speed: 10, enemies: enemies);
            int car = n.Train.Dynamics.Consist.Vehicles[^1].Id;
            var hound = n.World.AddEnemy(id => new CinderHound(id, id));
            hound.Restore(SpinePhase.Commit, 0, enemies.CinderHounds.Health, car, new Double3(0.6, n.Train.Frames[car].Shape.RoofHeight, 0), 0, 0, 0.6, hound.Id, 0);
            // Its pack has lit its fill already tonight.
            n.World.FiresByPack[hound.Pack] = enemies.CinderHounds.Sated.Fires;
            n.Run(enemies.CinderHounds.IgniteEverySeconds + enemies.CinderHounds.Aboard.IgniteWindUpSeconds + 1);
            bool lit = n.World.ActiveEnemies.Any(e => e is CarFire { Gone: false } f && f.Attached == car);
            Assert.Equal(!sated, lit);
            Assert.Equal(sated, hound.Gone);
            // Sated, it goes as it would bored: not driven off (which a once-a-night kind would read as "may come back").
            if (sated)
                Assert.DoesNotContain(EnemyKind.CinderHound, n.World.DrivenOff);
        }
    }
}
