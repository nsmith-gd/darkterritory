using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Stops;

namespace DarkTerritory.Sim.Enemies;

/// <summary>
/// Where the stops' layouts say the creatures live (level-design H.2; GDD B.6; notes 309, 314): the spawns read them, and
/// so does the Whistler for where it carries someone.
/// </summary>
public static class CreatureSites
{
    /// <summary>
    /// Every site of <paramref name="kind"/> at the generated stops within <paramref name="around"/> of the engine, in the
    /// world, on the ground there, in the route's order. None on a hand-laid route, whose stops have no layouts.
    /// </summary>
    public static List<(Double3 At, double Radius)> Of(World world, LairKind kind, double around)
    {
        var found = new List<(Double3, double)>();
        if (world.Route is not { } route)
            return found;
        var train = world.Train;
        double front = train.Dynamics.Distance, hint = 0;
        foreach (var f in route.Features)
        {
            if (f.Stop is not { } stop || front < f.Start - around || front > f.End + around)
                continue;
            foreach (var lair in stop.Lairs.Where(l => l.Kind == kind))
            {
                var at = Run.Run.StopWorld(train.Line, f, lair.At);
                found.Add((at with { Y = PlayerMotor.GroundAt(at, train.Line, ref hint) }, lair.Radius));
            }
        }
        return found;
    }
}
