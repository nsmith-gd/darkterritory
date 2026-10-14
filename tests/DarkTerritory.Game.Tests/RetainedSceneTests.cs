using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// What stays as it is is cooked once and drawn where it is (ARCHITECTURE §8 note 695; the director, 13 Oct: "it doesn't
/// really make sense that we're ... rebuilding things in real time, frame by frame"): standing in a town, on the roof by
/// a facility's spurs, in the cab with its map, frame after frame builds no mesh, and what it does build again each frame
/// (what moves or changes by the frame: the figures, the flames, the levers) stays inside tuning/perf.json's budget.
/// </summary>
public class RetainedSceneTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);
    static readonly TrainTuning Trains = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
    static readonly PerfTuning Perf = DataFile.Load<PerfTuning>(Path.Combine(Content, PerfTuning.File));

    public static TheoryData<string> Places => ["town square", "town houses", "roof by the spurs", "cab by the spurs"];

    [Theory]
    [MemberData(nameof(Places))]
    public void AStandingFrameBuildsNoMeshAndLittleSoup(string place)
    {
        var route = Sim.LineGen.Routes.Generate(Content, "frontier:7", 6);
        double gate = route.GateOr(Sim.Route.RouteTuning.Load(Content).YardLength);
        var consist = Consist.Uniform(Trains, 6, 1);
        var line = route.Build();
        Staging.DepartureTown(Content, route, line, consist.LengthMetres, out double depart);
        // frontier:7's spurs at 2.56, 2.61 and 2.65 km (a facility's), and its dead line at 3.3.
        bool inTown = place.StartsWith("town", StringComparison.Ordinal);
        var train = new TrainOnLine(new TrainDynamics(consist), line, inTown ? depart : 2640);
        var world = new Sim.World(train, DataFile.Load<Sim.Combat.CombatTuning>(Path.Combine(Content, Sim.Combat.CombatTuning.File)));
        world.EnableRun(DataFile.Load<Sim.Run.RunTuning>(Path.Combine(Content, Sim.Run.RunTuning.File)), route, gate, authority: true);
        world.EnableTown(Sim.Towns.TownContent.Load(Content)!, route, gate, [],
            arrival: Sim.Towns.TownAt.Terminus(route, Sim.LineGen.LineGenContent.Cached(Content).Config.Tiers.Fortress.Identities));
        var scene = new GreyboxScene
        {
            Look = Look,
            Time = 0.37,
            Route = route,
            Run = world.Run,
            Town = world.Town,
            Arrival = world.Arrival,
            Crew = Staging.Crew(train, Content),
            Enemies = Staging.Threats(train),
        };
        var eye = place switch
        {
            "town square" => Staging.TownCamera(world.Town!, "square").Position,
            "town houses" => Staging.TownCamera(world.Town!, "houses").Position,
            "roof by the spurs" => train.Frames[2].ToWorld(new Double3(0, 5, 0)),
            _ => train.Frames[0].ToWorld(new Double3(0, 2.2, 1)),
        };
        var mesh = new MeshBuilder();
        for (int i = 0; i < 3; i++)
        {
            scene.Time += 1 / 60.0;
            scene.Build(mesh, train, eye);
        }
        long made = MeshAsset.MadeOnThisThread;
        int worst = 0;
        for (int i = 0; i < 5; i++)
        {
            scene.Time += 1 / 60.0;
            scene.Build(mesh, train, eye);
            worst = Math.Max(worst, mesh.Count / 3);
        }
        TestContext.Current.TestOutputHelper?.WriteLine($"{place}: {worst} triangles built again a frame, {mesh.Instances.Count} drawn where they are");
        Assert.Equal(0, MeshAsset.MadeOnThisThread - made);
        Assert.True(worst <= Perf.MaxRebuiltTriangles, $"{place}: {worst} triangles built again every frame (at most {Perf.MaxRebuiltTriangles})");
    }
}
