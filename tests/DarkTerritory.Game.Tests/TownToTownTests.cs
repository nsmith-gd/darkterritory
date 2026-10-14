using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Towns;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// Town to town, without leaving it (queue #336, ARCHITECTURE §8 note 604): after a delivered night the front end stands in
/// the town the crew arrived in, and the loading card names it and the night ahead.
/// </summary>
public class TownToTownTests
{
    static readonly string Content = DataFile.FindContentRoot();

    [Fact]
    public void TheFrontEndStandsInTheTownTheCrewArrivedIn()
    {
        var towns = TownContent.Load(Content)!;
        var route = Routes.Generate(Content, "frontier:7", 6);
        double gate = route.GateOr(RouteTuning.Load(Content).YardLength);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File)), 6, 1)),
            route.Build(), route.Plan!.Terminus.GateM + 300);
        var world = new World(train, DataFile.Load<Sim.Combat.CombatTuning>(Path.Combine(Content, Sim.Combat.CombatTuning.File)));
        world.EnableRun(DataFile.Load<Sim.Run.RunTuning>(Path.Combine(Content, Sim.Run.RunTuning.File)), route, gate, authority: true);
        world.EnableTown(towns, route, gate, [], arrival: TownAt.Terminus(route, LineGenContent.Cached(Content).Config.Tiers.Fortress.Identities));
        var backdrop = new TownBackdrop(route, train, world.Run, world.Arrival!, world.Town);
        // Seen from the arrival town's square: the town it stands in is that one, not the departure's.
        Assert.Same(world.Arrival, world.TownAt(backdrop.Camera.Position));
        var scene = backdrop.Scene(null);
        Assert.Same(world.Arrival, scene.Arrival);
        Assert.Same(world.Town, scene.Town);
    }

    [Fact]
    public void TheLoadingCardNamesTheTownAndTheNightAhead()
    {
        Assert.Equal(("FORT BOUDREAU", "BUILDING THE NIGHT AHEAD"), TownBackdrop.Card("Fort Boudreau", null));
        Assert.Equal(("FORT BOUDREAU", "THE NIGHT TO GRIEVE"), TownBackdrop.Card("Fort Boudreau", "Grieve"));
        // A joiner following its host (note 606): the same card, the host picking the night till it's up.
        Assert.Equal(("FORT BOUDREAU", "THE HOST IS PICKING THE NIGHT AHEAD"), TownBackdrop.Card("Fort Boudreau", null, following: true));
        Assert.Equal(("FORT BOUDREAU", "THE NIGHT TO GRIEVE"), TownBackdrop.Card("Fort Boudreau", "Grieve", following: true));
    }
}
