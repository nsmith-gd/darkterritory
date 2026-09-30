using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;

namespace DarkTerritory.Sim.LineGen;

/// <summary>
/// A Line Plan as the rest of the game has always seen a night: a <see cref="Route.Route"/> of track, features along
/// the main line, branches and weather. The plan itself rides along (<see cref="Route.Route.Plan"/>) for what reads more.
/// </summary>
public static class PlanRoutes
{
    public static Route.Route ToRoute(this LinePlan plan, RouteTuning tuning)
    {
        var main = plan.Edge("main");
        var line = new LineDefinition(plan.Route.Id, main.Segments);
        var branches = plan.Alignment.Where(a => a.Role != EdgeRole.Main).OrderBy(a => a.Branch)
            .Select(a => new BranchDefinition(a.Role switch
            {
                EdgeRole.Alternate => BranchKind.Alternate,
                EdgeRole.Spur => BranchKind.Spur,
                _ => BranchKind.DeadLine,
            }, a.Toe, a.Side, a.Segments)
            {
                Rejoin = a.Rejoin,
                // §6.3: a junction whose main line is washed out starts the night set for its alternate.
                StartsDiverging = plan.Graph.Nodes.Any(n => n.Type == NodeType.JunctionFacing && n.DefaultEdge == a.Edge),
            }).ToList();

        var features = new List<RouteFeature>();
        foreach (var st in plan.Structures.Where(s => s.Edge == "main"))
            switch (st.Type)
            {
                case StructureType.Tunnel:
                    features.Add(new RouteFeature(FeatureKind.Tunnel, st.S0, st.S1));
                    break;
                case StructureType.Trestle or StructureType.Girder or StructureType.Truss or StructureType.Viaduct:
                    features.Add(new RouteFeature(FeatureKind.Bridge, st.S0, st.S1, MaxCars: st.Weak?.MaxCars ?? 0));
                    break;
            }
        foreach (var poi in plan.Pois)
        {
            // The facility's zone as the run has always had it: a spur's points SpurToe into it (tuning's "junctions").
            double start = poi.SpurEdge is not null ? poi.S - tuning.Junctions.SpurToe : poi.S - tuning.PoiZoneHalfLength;
            features.Add(new RouteFeature(FeatureKind.Facility, start, start + 2 * tuning.PoiZoneHalfLength, poi.Side, Facility: poi.Type));
        }
        foreach (var n in plan.Graph.Nodes.Where(n => n.Type == NodeType.JunctionFacing))
        {
            var edge = plan.Graph.Edges.FirstOrDefault(e => e.From == n.Id && e.Role != EdgeRole.Main);
            int side = edge is null ? 0 : plan.Alignment.First(a => a.Edge == edge.Id).Side;
            features.Add(new RouteFeature(FeatureKind.Junction, n.S, n.S + 30, side));
        }
        foreach (var z in plan.Director.SleeperPlaced.Where(z => z.Edge == "main"))
            features.Add(new RouteFeature(FeatureKind.Sleepers, z.S0, z.S1));
        foreach (var z in plan.Director.GreasePlaced.Where(z => z.Edge == "main"))
            features.Add(new RouteFeature(FeatureKind.Grease, z.S0, z.S1));
        // The bog and the tar ponds the line crosses: the Drift's ground (App. B.4).
        foreach (var bog in plan.Water.Where(b => b.Edge == "main" && b.Type is "marsh" or "contaminatedMarsh"))
            features.Add(new RouteFeature(FeatureKind.Marsh, bog.S0, bog.S1));
        features.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.Kind.CompareTo(b.Kind));

        var w = plan.Weather;
        var weather = new RouteWeather(w.FogDensity, w.Rain, w.Cold, w.Wind);
        ulong seed = ulong.TryParse(plan.Route.Id.Split('-')[^1], out var sd) ? sd : 0;
        return new Route.Route(plan.Route.Id, plan.Route.Tier, seed, line, features, weather, plan.RouteCard.DawnS)
        {
            Branches = branches,
            Plan = plan,
            Gate = plan.GateM,
        };
    }
}
