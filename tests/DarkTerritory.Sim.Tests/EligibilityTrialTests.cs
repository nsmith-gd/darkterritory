using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 915 (queue #612; the director's answers to note 875, "test creature spawn first"): each eligibility trial is its own
/// tuning flag, off in content until it's measured and confirmed, and on it does only what it says: the Gannet's speeds, the
/// hound run's count, the rare kinds' chances, the free Climbers' speed, the Car Hugger's low ground.
/// </summary>
public class EligibilityTrialTests
{
    static readonly string Content = DataFile.FindContentRoot();

    [Fact]
    public void EveryTrialIsOffInContent()
    {
        var et = Tuning.Enemies;
        Assert.False(et.Gannet.LowArrive.On);
        Assert.False(et.Director.Run.FromLastStop.On);
        Assert.False(et.Director.NightChanceTrial.On);
        Assert.False(et.Climbers.FreeNeedsSpeed);
        Assert.False(et.CarHugger.LowGround.On);
        // Off, the numbers are spec B.13's and note 328's.
        Assert.Equal((18.0, 12.0), (et.Gannet.Arrive, et.Gannet.Stall));
        Assert.Equal(et.Director.Run.FromSpeed, et.Director.Run.FromSpeedOn(RouteTier.Frontier));
        Assert.Equal(et.CarHugger.LurkAheadMax, et.CarHugger.LurkAheadFar);
    }

    [Fact]
    public void OnTheGannetComesAtCruiseAndTheRunAtTheTiersLineSpeedLessFour()
    {
        var et = Tuning.Enemies;
        var g = et.Gannet with { LowArrive = et.Gannet.LowArrive with { On = true } };
        Assert.Equal((13.0, 10.0), (g.Arrive, g.Stall));
        var run = et.Director.Run with { FromLastStop = et.Director.Run.FromLastStop with { On = true } };
        Assert.Equal((16.0, 14.0, 12.0, 10.0), (run.FromSpeedOn(RouteTier.Local), run.FromSpeedOn(RouteTier.Frontier),
            run.FromSpeedOn(RouteTier.DeadLines), run.FromSpeedOn(RouteTier.DeepTerritory)));
    }

    [Fact]
    public void OnTheRareKindsAreAboutOnTheirTrialChancesAndTheRestRollAsTheyDid()
    {
        var d = Tuning.Enemies.Director;
        var on = d with { NightChanceTrial = d.NightChanceTrial with { On = true } };
        Assert.NotEmpty(on.NightChanceTrial.Chances);
        var route = Routes.Generate(Content, "frontier:7", 6);
        const int seeds = 600;
        var aboutOff = new Dictionary<string, int>();
        var aboutOn = new Dictionary<string, int>();
        int same = 0, others = 0;
        foreach (var kind in Spawns.Rules.Select(r => r.Kind))
        {
            string key = Director.Key(kind);
            aboutOff[key] = aboutOn[key] = 0;
        }
        for (ulong seed = 1; seed <= seeds; seed++)
        {
            var off = new Director(d, route, seed, 6, 2);
            var tried = new Director(on, route, seed, 6, 2);
            foreach (var kind in Spawns.Rules.Select(r => r.Kind))
            {
                string key = Director.Key(kind);
                aboutOff[key] += off.OutTonight(kind) ? 0 : 1;
                aboutOn[key] += tried.OutTonight(kind) ? 0 : 1;
                if (!on.NightChanceTrial.Chances.ContainsKey(key))
                {
                    others++;
                    same += off.OutTonight(kind) == tried.OutTonight(kind) ? 1 : 0;
                }
            }
        }
        foreach (var (key, chance) in on.NightChanceTrial.Chances)
        {
            double was = (double)aboutOff[key] / seeds, now = (double)aboutOn[key] / seeds;
            Assert.True(Math.Abs(now - chance) < 0.06, $"{key}: about {now:P0} on, its trial chance {chance:P0}");
            Assert.True(now > was, $"{key}: {was:P0} off, {now:P0} on");
        }
        // The same stream, the same order: the other kinds' rolls are as they were (the night's floor may bring one back
        // that a rare kind now about makes unneeded, so nearly all, not all).
        Assert.True(same >= others * 0.97, $"{same} of {others} other rolls the same");
    }

    [Fact]
    public void OnTheCarHuggerLurksOnTheLowGroundToo()
    {
        var et = Tuning.Enemies;
        var on = et.CarHugger with { LowGround = et.CarHugger.LowGround with { On = true } };
        // A line with a low stretch on its main line.
        var route = Enumerable.Range(1, 12).Select(s => Routes.Generate(Content, $"deadLines:{s}", 4))
            .First(r => r.Plan?.Biomes.Any(b => b.Edge == "main" && on.LowGround.Biomes.Contains(b.Biome)) == true);
        var line = route.Build();
        int offSpots = 0, onSpots = 0;
        for (double at = 300; at < route.RunLength - 1000; at += 150)
        {
            var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 1)), line, at), Tuning.Combat);
            world.EnableEnemies(et, route, 1, 2, authority: true);
            var was = CarHugger.Spot(world, et.CarHugger);
            var now = CarHugger.Spot(world, on);
            offSpots += was is null ? 0 : 1;
            onSpots += now is null ? 0 : 1;
            if (was is not null)
                Assert.NotNull(now);
            if (now is { } spot)
            {
                Assert.InRange(spot, at + on.LurkAheadMin, at + on.LurkAheadFar);
                Assert.True(Math.Abs(world.Train.Line.Sample(world.Train.Dynamics.Path, spot).GradePercent) <= on.MaxGrade);
            }
        }
        Assert.True(onSpots > offSpots, $"{onSpots} spots on, {offSpots} off");
    }
}
