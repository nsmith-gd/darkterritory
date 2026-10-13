using Ballast;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Stops;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Queue #89, ARCHITECTURE §8 note 352. The director, 8 Oct 2026: "I spent all this work pulling into a yard switch line
/// that had no loot and watched the loot spawn in the yard after I'd already stopped the train. Loot should spawn in all
/// yard lines in early game and mid game."
/// </summary>
public class YardLootTests
{
    static readonly StopTuning S = Tuning.Route.Stops!;
    static readonly StopContext Cx = Tuning.Route.StopContext;
    static readonly LootTuning L = DataFile.Load<LootTuning>(Path.Combine(DataFile.FindContentRoot(), LootTuning.File));

    [Theory]
    [InlineData(RouteTier.Local)]
    [InlineData(RouteTier.Frontier)]
    [InlineData(RouteTier.DeadLines)]
    public void OnTheEarlyAndMidTiersEveryYardTrackHasSomethingToLoadBesideIt(RouteTier tier)
    {
        Assert.True(S.Tiers[tier].EveryTrack, $"{tier}: stops.json everyTrack is off");
        foreach (var kind in new[] { StopKind.Yard, StopKind.YardAndVillage })
            foreach (var facility in new FacilityKind?[] { null, FacilityKind.Foundry, FacilityKind.Switchyard })
                for (ulong seed = 1; seed <= 40; seed++)
                {
                    var stop = StopGenerator.Generate(S, tier, seed, kind, Cx with { Facility = facility });
                    foreach (var tr in stop.Tracks)
                        Assert.True(stop.Containers.Any(c => Beside(stop, c, tr)),
                            $"{tier} {kind} {facility} seed {seed}: yard track {tr.Index} has nothing beside its loading face");
                }
    }

    [Fact]
    public void DeepTerritoryIsLeftToItsDice()
    {
        // The flag is by tier, and "early game and mid game" is the first three: deep territory's stock lies where the dice put it.
        Assert.False(S.Tiers[RouteTier.DeepTerritory].EveryTrack);
    }

    [Fact]
    public void WhatTheDiceDealtIsUnchangedAndOnlyBareTracksGainAStack()
    {
        // The stack for a bare track is the last container laid, with no dice: the rest of the stop is what it was.
        var off = S with { Tiers = S.Tiers with { Frontier = S.Tiers.Frontier with { EveryTrack = false } } };
        int added = 0;
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var before = StopGenerator.Generate(off, RouteTier.Frontier, seed, StopKind.YardAndVillage, Cx);
            var after = StopGenerator.Generate(S, RouteTier.Frontier, seed, StopKind.YardAndVillage, Cx);
            if (before.Attempt != after.Attempt)
                continue;
            Assert.Equal(before.Tracks.Count, after.Tracks.Count);
            Assert.Equal(before.Buildings.Count, after.Buildings.Count);
            Assert.Equal(before.Containers, after.Containers.Take(before.Containers.Count));
            Assert.All(after.Containers.Skip(before.Containers.Count), c => Assert.Equal(ContainerKind.CrateStack, c.Kind));
            added += after.Containers.Count - before.Containers.Count;
        }
        Assert.True(added > 0, "no frontier stop in 40 had a bare yard track: the test proves nothing");
    }

    [Fact]
    public void AStopsLootIsOutBeforeAnyoneIsNearEnoughToSeeIt()
    {
        Assert.True(L.StockAhead > Tuning.Enemies.InterestRadius, "a client could be sent a stop's loot as it comes out");
        var (far, k, _) = Approaching(L.StockAhead + 300);
        far.StepRun([]);
        Assert.False(far.Run!.Stocked(k), "out while the train's still a long way off");

        var (near, k2, _) = Approaching(L.StockAhead - 100);
        int loose = Loose(near);
        near.StepRun([]);
        Assert.True(near.Run!.Stocked(k2), "not out as the train came up to it at speed");
        Assert.True(Loose(near) > loose, "stocked, and nothing came out");
    }

    [Fact]
    public void WithNoLookAheadItComesOutWhenTheTrainStopsAsItDid()
    {
        var (world, k, f) = Approaching(-50, loot: L with { StockAhead = 0 });
        world.StepRun([]);
        Assert.False(world.Run!.Stocked(k), "out at speed with no look-ahead");
        world.Train.Dynamics.Velocity = 0;
        world.StepRun([]);
        Assert.True(world.Run.Stocked(k));
    }

    [Fact]
    public void AFacilitysCratesComeOutWithItsStopsLoot()
    {
        for (ulong seed = 1; seed <= 60; seed++)
        {
            var route = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, seed);
            double gate = route.GateOr(Tuning.Route.YardLength);
            World At(double distance)
            {
                var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 0)), route.Build(), distance, Tuning.Boiler);
                train.Dynamics.Velocity = 20;
                var w = new World(train, Tuning.Combat);
                w.EnableBodies();
                w.EnableRun(Tuning.Run, route, gate, authority: true, FacilityTests.F, L);
                return w;
            }
            var probe = At(gate + 100).Run!.Sites;
            int i = probe.ToList().FindIndex(s => s is { CrateCount: > 0 } && s.Feature.Start - L.StockAhead > gate + 100);
            if (i < 0)
                continue;
            var world = At(probe[i]!.Feature.Start - L.StockAhead + 100);
            var site = world.Run!.Sites[i]!;
            Assert.False(site.Stocked);
            world.StepRun([]);
            Assert.True(site.Stocked, "the facility's crates weren't out as the train came up to it");
            return;
        }
        Assert.Fail("no frontier night in 60 had a facility with crates past the gate");
    }

    /// <summary>A host night on a generated line, the engine <paramref name="before"/> m short of the first yard stop, at speed.</summary>
    static (World World, int Stop, RouteFeature Feature) Approaching(double before, RouteTier tier = RouteTier.Frontier, LootTuning? loot = null)
    {
        for (ulong seed = 1; ; seed++)
        {
            var route = RouteGenerator.Generate(Tuning.Route, tier, seed);
            double gate = route.GateOr(Tuning.Route.YardLength);
            int k = 0;
            foreach (var f in route.Features)
            {
                if (f.Stop is not { } stop)
                    continue;
                if (stop.Tracks.Count > 0 && f.Start - Math.Max(before, 0) > gate + 50)
                {
                    var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 0)), route.Build(), f.Start - before, Tuning.Boiler);
                    train.Dynamics.Velocity = 20;
                    var world = new World(train, Tuning.Combat);
                    world.EnableBodies();
                    world.EnableRun(Tuning.Run, route, gate, authority: true, FacilityTests.F, loot ?? L);
                    return (world, k, f);
                }
                k++;
            }
        }
    }

    static int Loose(World w) => w.Bodies.All.Count(b => b.Kind is BodyKind.Cargo or BodyKind.Heavy or BodyKind.Loot && b.Carrier < 0);

    /// <summary>
    /// A container beside a yard track: its crane bay, or in a yard shed the track stands beside, on the track's side
    /// (nearer it than the shed's other track).
    /// </summary>
    static bool Beside(StopLayout l, StopContainer c, YardTrack tr)
    {
        if (c.Kind == ContainerKind.CraneBay)
            return c.Track == tr.Index;
        if (c.Zone != StopZone.Yard || c.Building < 0 || !l.Buildings[c.Building].Tracks.Contains(tr.Index))
            return false;
        return l.Buildings[c.Building].Tracks.MinBy(i => l.Tracks[i].Path.Min(p => Pt.Distance(p, c.At))) == tr.Index;
    }
}
