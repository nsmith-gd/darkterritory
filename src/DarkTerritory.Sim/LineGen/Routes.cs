using DarkTerritory.Sim.Route;

namespace DarkTerritory.Sim.LineGen;

/// <summary>
/// Where every part of the game gets a night's line: a route spec ("frontier:7", or "frontier:7:0.4" with a severity)
/// and the departing consist, through the line generator. The same spec and consist is the same line on every machine
/// (§17.3: host and joiners generate it alike from the session's setup, their content checked by hash). Cached per
/// process: a line takes a second or two to generate and validate.
/// </summary>
public static class Routes
{
    static readonly Dictionary<(string Content, string Spec, int Cars), Route.Route> Cache = new();

    public static Route.Route Generate(string content, string spec, int cars)
    {
        var key = (Path.GetFullPath(content), spec.ToLowerInvariant(), Math.Max(1, cars));
        lock (Cache)
            if (Cache.TryGetValue(key, out var hit))
                return hit;
        var c = LineGenContent.Cached(content);
        var plan = LineGenerator.Generate(c, RunParameters.Parse(spec, cars));
        var route = plan.ToRoute(c.Route);
        lock (Cache)
            Cache[key] = route;
        return route;
    }

    /// <summary>A route from a plan saved or received whole (§17.4: the save keeps the plan, not just the seed).</summary>
    public static Route.Route FromPlan(string content, LinePlan plan) => plan.ToRoute(LineGenContent.Cached(content).Route);
}
