using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

public partial class StopCrewTests
{
    // Note 1028 (level-design D.1, D.2, I.4; note 1026's rank 3): a bot crew clears a blocked siding.

    /// <summary>
    /// A frontier (or dead lines) night with a yard whose layout blocked a siding (note 294), a free road for its derelicts up
    /// the yard past it, and its own track with live power: the route and the facility's index.
    /// </summary>
    static (Route.Route Route, int Facility) BlockedSidingWithARoad()
    {
        foreach (var tier in new[] { RouteTier.Frontier, RouteTier.DeadLines })
            for (ulong seed = 1; seed < 200; seed++)
            {
                var route = RouteGenerator.Generate(Tuning.Route, tier, seed);
                var facilities = route.Of(FeatureKind.Facility).ToList();
                for (int i = 0; i < facilities.Count; i++)
                {
                    var f = facilities[i];
                    if (f.Stop?.Tracks.Any(t => t.Blocked) != true || f.Stop.Power != Stops.PowerState.Live)
                        continue;
                    var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 8, 0.5)), route.Build(), f.Start - 600, Tuning.Boiler));
                    world.EnableRun(Tuning.Run, route, 600, authority: true, F);
                    var run = world.Run!;
                    var line = world.Train.Line;
                    var tracks = run.YardTracks(i);
                    var blocked = run.BlockedTracks(line, i);
                    if (tracks.Count > 0 && blocked.Any(b => tracks.Any(t => t != tracks[0] && blocked.All(x => x.Branch != t)
                        && line.Branches[t].Toe > line.Branches[b.Branch].Toe + 1)))
                        return (route, i);
                }
            }
        throw new InvalidOperationException("no route has a blocked siding with a free road past it");
    }

    [Fact]
    public void ABotCrewClearsABlockedSidingAndPutsItsDerelictsAwayOnAFreeRoad()
    {
        var (route, facility) = BlockedSidingWithARoad();
        var r = FacilityWork.Run(route, facility, T, P, Tuning.Boiler, Tuning.Run, F, Tuning.Route.Junctions, cars: 8, hands: 2, seconds: 1800,
            yardLength: Tuning.Route.YardLength);
        string said = $"legs {string.Join(">", r.Legs)}; did {string.Join(", ", r.Doing)}; stops {string.Join(", ", r.Stops.Select(s => $"{s.Kind} {s.Seconds:0}"))}";
        Assert.True(r.Departed, $"never left: {said}");
        Assert.Contains(r.Stops, s => s.Kind.Contains("Clear"));
        Assert.Contains(r.Stops, s => s.Kind.Contains("PutAway"));
        Assert.True(r.StillBlocked == 0, $"{r.StillBlocked} derelicts still on the blocked sidings: {said}");
        Assert.True(r.DerelictsAboard == 0, $"{r.DerelictsAboard} derelicts still ahead of the engine: {said}");
        Assert.Contains("to the derelicts", r.Doing);
        Assert.Equal(r.Crew, r.Alive);
    }

    [Fact]
    public void WithClearingOffTheCrewWorksRoundABlockedSidingAsBefore()
    {
        var (route, facility) = BlockedSidingWithARoad();
        var off = F with { Crew = F.Crew with { ClearSidings = false } };
        var r = FacilityWork.Run(route, facility, T, P, Tuning.Boiler, Tuning.Run, off, Tuning.Route.Junctions, cars: 8, hands: 2, seconds: 1800,
            yardLength: Tuning.Route.YardLength);
        Assert.True(r.Departed, $"never left: legs {string.Join(">", r.Legs)}");
        Assert.DoesNotContain(r.Stops, s => s.Kind.Contains("Clear") || s.Kind.Contains("PutAway"));
        Assert.True(r.StillBlocked > 0);
        Assert.Equal(0, r.DerelictsAboard);
    }

    [Fact]
    public void DerelictsSomeonePlayingDrewOutFirstArentTheBotsToClearOrPutAway()
    {
        // A person in the crew got there first (note 294's own shunting): coupled up to the derelicts and drew them out. The
        // siding's clear, so the bots have nothing to clear, and the derelicts ahead of the engine are theirs to keep there or
        // put where they like: no put-away for them (only for derelicts the bots drew out, CrewCalls.PutAwayOn).
        var (route, facility) = BlockedSidingWithARoad();
        var f = route.Of(FeatureKind.Facility).ToList()[facility];
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 8, 0.5)), route.Build(), f.Start - 600, Tuning.Boiler);
        var world = new World(train, Tuning.Combat);
        world.EnableBodies();
        world.EnableRun(Tuning.Run, route, 600, authority: true, F);
        var calls = new CrewCalls();
        var cab = PlayerMotor.SpawnInCab(train, P);
        calls.Say(0, StopJob.Driver, cab);
        calls.Say(1, StopJob.Shunter, cab);
        var plans = Plans(world, f.Start - 600, calls);
        var clear = Assert.Single(plans, p => p.Clears);
        Assert.True(clear.PickUp && clear.Road >= 0 && train.Line.Branches[clear.Road].Toe > clear.Spur.Toe);

        var rake = train.Rakes.Single(r => r.Path == clear.Spur.Index && r.Consist.Vehicles.All(v => v.Derelict));
        var state = train.Capture();
        int e = Array.FindIndex(state.Rakes, r => r.Vehicles.Contains(0));
        state.Rakes[e] = state.Rakes[e] with { Path = rake.Path, Distance = rake.RearDistance - T.Geometry.CouplingGap - 1, Velocity = 0.8 };
        train.Restore(state);
        for (int t = 0; t < 5 * SimConstants.TickRate && train.Rakes.Contains(rake); t++)
            train.Step(SimConstants.TickSeconds, new TrainControls { Reverser = 1 });
        Assert.NotEmpty(StopPlan.DerelictsAhead(train));
        Assert.DoesNotContain(Plans(world, f.Start - 600, calls), p => p.Clears || p.PutAway);
    }

    /// <summary>Every stop a crew would make from <paramref name="from"/> on, in order.</summary>
    static List<StopPlan> Plans(World world, double from, CrewCalls calls)
    {
        var done = new HashSet<int>();
        var plans = new List<StopPlan>();
        for (var p = StopPlan.Ahead(world, from, done, calls); p is not null && plans.Count < 32; p = StopPlan.Ahead(world, from, done, calls))
        {
            plans.Add(p);
            done.Add(p.Key);
        }
        return plans;
    }
}
