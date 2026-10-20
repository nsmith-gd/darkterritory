using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 875 (queue #605; the spawn director overhaul's brief §2 issue 5): the eligibility audit reads every kind's gates a second
/// at a time without moving the night (it draws no dice), and its gates are the rules' own: a kind whose rule is met has every
/// gate the audit names for it holding, so a rule changed in <see cref="Spawns"/> without its gates in <see cref="Eligibility"/>
/// fails here.
/// </summary>
public class EligibilityTests
{
    [Fact]
    public void SamplingANightLeavesItAsItWasAndAMetRuleHasEveryGateHolding()
    {
        int sampled = 0, met = 0, failed = 0;
        var broken = new List<string>();
        var looked = Night(world =>
        {
            foreach (var k in Eligibility.Sample(world))
            {
                sampled++;
                if (k.RuleMet)
                {
                    met++;
                    if (k.Gates.FirstOrDefault(g => !g.Holds) is { Gate: not null } g)
                        broken.Add($"{k.Kind} at {world.Tick / 30} s: rule met, '{g.Gate}' doesn't hold");
                }
                else if (k.SituationBlocked is not null)
                    failed++;
            }
        });
        var unlooked = Night(null);

        Assert.Empty(broken);
        Assert.True(met > 0 && failed > 0, $"{met} met, {failed} blocked of {sampled}: the night tested nothing");
        Assert.Equal(unlooked.TrainDistance, looked.TrainDistance);
        Assert.Equal(unlooked.Threats?.Spawned, looked.Threats?.Spawned);
        Assert.Equal(unlooked.Deaths, looked.Deaths);
    }

    [Fact]
    public void EveryKindTheDirectorSendsHasItsGatesNamed()
    {
        // The kinds of the spend table each have a gate list in the audit (a kind added to Spawns without one is unaudited).
        foreach (var rule in Spawns.Rules)
            Assert.True(Eligibility.Named(rule.Kind), $"{rule.Kind} has no gates in Eligibility.Gates");
    }

    static HarnessReport Night(Action<World>? look)
    {
        var content = DataFile.FindContentRoot();
        const int cars = 4;
        var route = LineGen.Routes.Generate(content, "frontier:7", cars);
        double gate = route.GateOr(Tuning.Route.YardLength);
        double from = Tuning.Run.DepartFrom(gate, Train.Consist.Uniform(Tuning.Train, cars, 1).LengthMetres);
        return Harness.Run(route.Build(), Tuning.Train, Tuning.Player, new HarnessOptions
        {
            Observe = (tick, _, world) =>
            {
                if (look is not null && tick % SimConstants.TickRate == 0)
                    look(world);
            },
            Bots = 2,
            Cars = cars,
            Seconds = 420,
            Seed = 1,
            Link = Ballast.Net.LinkConditions.Perfect,
            StartDistance = from,
            WalkAboard = from < gate,
            Combat = Tuning.Combat,
            Enemies = Tuning.Enemies,
            Route = route,
            Run = Tuning.Run,
            Facilities = DataFile.Load<FacilityTuning>(Path.Combine(content, FacilityTuning.File)),
            Sight = DataFile.Load<Route.SightTuning>(Path.Combine(content, Route.SightTuning.File)),
            YardLength = gate,
            Holdouts = Tuning.Holdouts,
        }, Tuning.Boiler);
    }
}
