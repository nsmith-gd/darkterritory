using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Towns;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// A town's people (the director, 8 Oct 2026: "We need townsfolk models who wear some sort of respirator mask or oxygen
/// mask or other breathing apparatuses to indicate the air is foul"; queue #90, ARCHITECTURE §8 note 353): each breathes
/// through gear of the kinds their town's character keeps (houses.json <c>gear</c>), drawn from who they are.
/// </summary>
public class TownsfolkTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TownContent Towns = TownContent.Load(Content)!;

    /// <summary>The town on <paramref name="spec"/>'s departure fortress, of <paramref name="people"/>, its character <paramref name="character"/> (any when null).</summary>
    static Town Plan(string spec, int people, string? character = null)
    {
        var looks = character is null ? Towns.Looks
            : Towns.Looks with { Characters = [.. Towns.Looks.Characters.Where(c => c.Id == character)], ByTrade = [] };
        var content = Towns with { Tuning = Towns.Tuning with { Population = [people, people] }, Looks = looks };
        var route = Routes.Generate(Content, spec, 6);
        double gate = route.GateOr(RouteTuning.Load(Content).YardLength);
        var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), route.Build(), gate - 8), Tuning.Combat);
        world.EnableRun(Tuning.Run, route, gate, authority: true);
        world.EnableTown(content, route, gate, []);
        return world.Town!;
    }

    [Fact]
    public void EveryoneBreathesThroughGearOfTheKindsTheirTownKeeps()
    {
        var town = Plan("frontier:7", 900);
        Assert.All(town.Plan.People, p => Assert.Contains(p.Gear, TownGear.Kinds));
        // More than one kind in a town of all sorts.
        Assert.True(town.Plan.People.Select(p => p.Gear).Distinct().Count() >= 2, string.Join(", ", town.Plan.People.Select(p => p.Gear)));

        // A company issues its masks: only the respirator and the mine's rebreather.
        var company = Plan("local:3", 900, "company");
        Assert.Equal("company", company.Plan.Character);
        Assert.All(company.Plan.People, p => Assert.Contains(p.Gear, (string[])["respirator", "rebreather"]));
        // A cove makes its own: wraps the most.
        var cove = Plan("local:3", 900, "cove");
        var kinds = cove.Plan.People.GroupBy(p => p.Gear).OrderByDescending(g => g.Count()).ToList();
        Assert.Equal("wrap", kinds[0].Key);
    }

    [Fact]
    public void WhatSomebodyWearsIsTheirsAndTheSameEveryTime()
    {
        var a = Plan("frontier:7", 600);
        var b = Plan("frontier:7", 600);
        Assert.Equal(a.Plan.People.Select(p => (p.Name, p.Gear)), b.Plan.People.Select(p => (p.Name, p.Gear)));
        // The pick runs in the kinds' order, whatever order the file lists them in.
        var weights = new Dictionary<string, double> { ["wrap"] = 1, ["respirator"] = 1 };
        var flipped = new Dictionary<string, double> { ["respirator"] = 1, ["wrap"] = 1 };
        for (int i = 0; i < 200; i++)
        {
            ulong h = Streams.Mix(7, "gear", "", i);
            Assert.Equal(TownGear.Pick(weights, h), TownGear.Pick(flipped, h));
        }
        Assert.Equal("respirator", TownGear.Pick(new Dictionary<string, double>(), 12345));
    }
}
