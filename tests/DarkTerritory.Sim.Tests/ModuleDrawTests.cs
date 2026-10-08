using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Each stop's own modules (queue #185, ARCHITECTURE §8 note 449; spec D.1: "POIs are procedurally assembled from a module
/// grammar, so no two facilities operate identically", "POI = SPUR TOPOLOGY + 2–4 LOADING MODULES + POWER STATE + SCALE").
/// </summary>
public class ModuleDrawTests
{
    /// <summary>The tuning as it ships: the draw on.</summary>
    static readonly FacilityTuning Content = DataFile.Load<FacilityTuning>(Path.Combine(DataFile.FindContentRoot(), FacilityTuning.File));
    static readonly FacilityKind[] Kinds =
        [FacilityKind.GrainElevator, FacilityKind.Switchyard, FacilityKind.Foundry, FacilityKind.Slaughterhouse, FacilityKind.WreckYard,
         FacilityKind.MineHead, FacilityKind.ChemicalWorks, FacilityKind.MilitaryDepot];

    static string Key(FacilityKind kind) => char.ToLowerInvariant(kind.ToString()[0]) + kind.ToString()[1..];
    static RouteFeature Facility(FacilityKind kind) => new(FeatureKind.Facility, 0, 300, 1, Facility: kind);
    static IReadOnlyList<ModuleKind> Extras(FacilityKind kind) =>
        Content.Extras.TryGetValue(Key(kind), out var names) ? [.. names.Select(n => Enum.Parse<ModuleKind>(n, ignoreCase: true))] : [];

    [Fact]
    public void EveryStopHasItsKindsSignatureAndTwoToFourDrawnFromItsKindsList()
    {
        Assert.True(Content.Draw.Enabled);
        Assert.Equal([2, 4], Content.Draw.Count);
        foreach (var kind in Kinds)
        {
            var all = Content.ModulesOf(kind);
            var offered = all.Concat(Extras(kind)).ToList();
            var seen = new HashSet<string>();
            for (ulong seed = 1; seed <= 200; seed++)
                for (int index = 0; index < 4; index++)
                {
                    var drawn = Content.ModulesFor(Facility(kind), seed, index);
                    Assert.Equal(all[0], drawn[0]);
                    Assert.InRange(drawn.Count, Math.Min(2, offered.Count), 4);
                    Assert.All(drawn, m => Assert.Contains(m, offered));
                    Assert.Equal(drawn.Count, drawn.Distinct().Count());
                    // Always in the same order: the kind's own, then its extras.
                    Assert.Equal(drawn, drawn.OrderBy(offered.IndexOf));
                    // The same stop on the same night has the same modules on every machine.
                    Assert.Equal(drawn, Content.ModulesFor(Facility(kind), seed, index));
                    seen.Add(string.Join(",", drawn));
                }
            // A kind with more than two to draw from varies from stop to stop; one with two always has both.
            if (offered.Count > 2)
                Assert.True(seen.Count >= 3, $"{kind} drew only {string.Join(" | ", seen)}");
            else
                Assert.Equal([string.Join(",", all)], seen);
        }
    }

    [Fact]
    public void WithTheDrawOffEveryStopHasItsKindsWholeListAndARoutesOwnModulesAreKept()
    {
        var off = FacilityTests.Whole(Content);
        foreach (var kind in Kinds)
            for (ulong seed = 1; seed <= 20; seed++)
                Assert.Equal(Content.ModulesOf(kind), off.ModulesFor(Facility(kind), seed, 0));
        // A route's own (dt edit, T44): those, drawn or not.
        var own = Facility(FacilityKind.MineHead) with { Modules = ["crates"] };
        for (ulong seed = 1; seed <= 20; seed++)
            Assert.Equal([ModuleKind.Crates], Content.ModulesFor(own, seed, 0));
    }

    [Fact]
    public void ANightsStopsOfOneKindAreNotAllAlike()
    {
        // Across the nights, the mine heads (four modules to draw from) and the foundries (three) come with different sets.
        var mines = new HashSet<string>();
        var foundries = new HashSet<string>();
        foreach (var tier in new[] { RouteTier.Frontier, RouteTier.DeadLines, RouteTier.DeepTerritory })
            for (ulong seed = 1; seed <= 30; seed++)
            {
                var route = RouteGenerator.Generate(Tuning.Route, tier, seed);
                var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), route.Build(), 900));
                world.EnableRun(Tuning.Run, route, 600, authority: true, Content);
                foreach (var site in world.Run!.Sites)
                    if (site?.Feature.Facility == FacilityKind.MineHead)
                        mines.Add(string.Join(",", site.Modules));
                    else if (site?.Feature.Facility == FacilityKind.Foundry)
                        foundries.Add(string.Join(",", site.Modules));
            }
        Assert.True(mines.Count >= 2, $"mine heads: {string.Join(" | ", mines)}");
        Assert.True(foundries.Count >= 2, $"foundries: {string.Join(" | ", foundries)}");
    }

    [Fact]
    public void AClientDrawsTheSameModulesAsTheHost()
    {
        for (ulong seed = 1; seed <= 10; seed++)
        {
            var route = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, seed);
            var line = route.Build();
            var host = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), line, 900));
            host.EnableRun(Tuning.Run, route, 600, authority: true, Content);
            var client = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), line, 900));
            client.EnableRun(Tuning.Run, route, 600, authority: false, Content);
            Assert.Equal(host.Run!.Sites.Select(s => s?.Modules), client.Run!.Sites.Select(s => s?.Modules));
        }
    }

    [Fact]
    public void AnExtraModuleStandsClearOfWhatItsKindAlreadyHas()
    {
        // Every kind with every module it can draw (its own and its extras): an extra's points are at least 3 m from each other
        // module's (on the flat), and off the yard's other tracks. (Its own list's were laid out together; the foundry's crane
        // legs stand nearer its crates than that, and always have.)
        foreach (var kind in Kinds.Where(k => Extras(k).Count > 0))
        {
            var t = FacilityTests.F with { Draw = Content.Draw with { Enabled = true, Chance = 1, Count = [2, 9] }, Extras = Content.Extras };
            var stop = new FacilityTests.Stop(kind, t);
            var sets = Sets(stop.Site);
            foreach (var extra in Extras(kind))
            {
                Assert.True(stop.Site.Has(extra), $"{kind} didn't draw its {extra}");
                foreach (var (other, points) in sets.Where(s => s.Key != extra))
                {
                    double nearest = sets[extra].SelectMany(a => points.Select(b => Flat(a - b))).Min();
                    Assert.True(nearest >= 3, $"{kind}'s {extra} is {nearest:0.0} m from its {other}");
                }
                foreach (int track in stop.World.Run!.YardTracks(stop.Site.Index).Where(b => b != stop.Site.Spur))
                {
                    var branch = stop.Train.Line.Branches[track];
                    for (double d = branch.Toe; d <= branch.End; d += 1)
                    {
                        var on = stop.Train.Line.Sample(track, d).Position;
                        Assert.All(sets[extra], p => Assert.True(Flat(p - on) >= 3, $"{kind}'s {extra} is on track {track}"));
                    }
                }
            }
        }
    }

    [Theory]
    [InlineData(FacilityKind.Switchyard, new[] { ModuleKind.Rakes, ModuleKind.Winch })]
    [InlineData(FacilityKind.Slaughterhouse, new[] { ModuleKind.Ramp, ModuleKind.Winch })]
    [InlineData(FacilityKind.WreckYard, new[] { ModuleKind.Wreck, ModuleKind.Crates })]
    [InlineData(FacilityKind.MineHead, new[] { ModuleKind.Lift, ModuleKind.Crates })]
    [InlineData(FacilityKind.Foundry, new[] { ModuleKind.Crane, ModuleKind.Winch })]
    public void ABotCrewWorksAStopWhateverItDrew(FacilityKind kind, ModuleKind[] modules)
    {
        // The bots go by what a stop has, not by its kind: a winch at a switchyard or a slaughterhouse, crates at a wreck yard, a
        // mine head with only its lift and crates, a foundry without its crates. Each is worked, and the train leaves loaded.
        var (route, facility) = FacilityWork.Find(Tuning.Route, kind, modules: modules)!.Value;
        var r = FacilityWork.Run(route, facility, Tuning.Train, Tuning.Player, Tuning.Boiler, Tuning.Run, Content, Tuning.Route.Junctions,
            cars: 8, hands: 2, seconds: 1500, yardLength: Tuning.Route.YardLength);
        string said = $"{string.Join(", ", r.Legs)}; {string.Join(", ", r.Doing)}";
        Assert.Equal(modules, r.Modules);
        Assert.True(r.Departed, said);
        Assert.Equal(r.Crew, r.Alive);
        Assert.True(r.LoadedAfter > r.LoadedBefore, $"nothing loaded: {said}");
        if (modules.Contains(ModuleKind.Winch))
            Assert.Contains("cranking", r.Doing);
        if (modules.Contains(ModuleKind.Crates))
            Assert.Contains("carrying", r.Doing);
    }

    /// <summary>Each module's points off the track: where it's worked and what stands there.</summary>
    static Dictionary<ModuleKind, List<Double3>> Sets(Site s)
    {
        var sets = new Dictionary<ModuleKind, List<Double3>>();
        void Add(ModuleKind m, IEnumerable<Double3> points)
        {
            if (s.Has(m))
                sets[m] = [.. points];
        }
        Add(ModuleKind.Crates, s.CrateStack.Concat(s.HeavyStack));
        if (s.Has(ModuleKind.Winch))
        {
            double run = (s.SledTo - s.SledFrom).Length;
            sets[ModuleKind.Winch] = [s.Capstan, .. s.Handles, .. Enumerable.Range(0, (int)(run / 2) + 1).Select(i => s.SledFrom + (s.SledTo - s.SledFrom) * Math.Min(1, i * 2 / run))];
        }
        Add(ModuleKind.Spout, [s.SpoutLever]);
        Add(ModuleKind.Lift, [s.Headframe, s.LiftLever]);
        Add(ModuleKind.Conveyor, [s.ConveyorStarter, .. Enumerable.Range(0, 9).Select(i => Double3.Lerp(s.ConveyorTail, s.ConveyorKnee, i / 8.0))]);
        Add(ModuleKind.Ramp, [s.RampTop, s.Pen]);
        Add(ModuleKind.Hose, [s.HoseStand]);
        if (s.Crane is { } c)
            sets[ModuleKind.Crane] = [c.Controls, .. Enumerable.Range(0, 4).Select(i => c.Corner(i / 2, i % 2)), .. c.Castings.Select(k => k.At)];
        Add(ModuleKind.Wreck, s.Heaps.Select(h => h.Centre));
        return sets;
    }

    static double Flat(Double3 v) => (v with { Y = 0 }).Length;
}
