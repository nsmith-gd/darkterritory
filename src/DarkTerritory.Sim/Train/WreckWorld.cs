using Ballast;
using Ballast.Physics;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Sim.Train;

/// <summary>
/// What the derailment meets past the ground and the train (GDD E.3 "What collides": "tunnel bores, ... static props";
/// note 1055): the line's walls (<see cref="StopWalls"/>: the lineside's trees, boulders and poles, the stops' buildings, the
/// forts) and a tunnel's rock (<see cref="ITrackConditions.Bore"/>). Built from the route alike on every machine, so every
/// client shoots the same film. A wall that stands or goes with the night (an open house's door, a gate's leaf, a coaling
/// tower Tower Jaw can bring down) isn't one of them: a client may shoot the film a moment after the host, and they must
/// agree.
/// </summary>
public sealed class WreckWorld(StopWalls? walls, ITrackConditions? land)
{
    /// <summary>
    /// A sphere of <paramref name="r"/> at <paramref name="p"/> against the world's solids: the deepest push-out, if it's in
    /// one. <paramref name="near"/>: only these walls (<see cref="Around"/>, gathered once for a body's joints; a forest's
    /// cell holds hundreds), or all near it.
    /// </summary>
    public Contact? Touch(Double3 p, double r, Near? near = null)
    {
        Contact? best = null;
        double deepest = 0;
        foreach (var w in near?.Walls ?? Fixed(p))
        {
            var local = w.ToLocal(p);
            if (Collide.SphereBox(local, r, w.Box.Min, w.Box.Max) is not { } t)
                continue;
            double depth = (t.Position - local).Length;
            if (depth > deepest)
                (best, deepest) = (new Contact(w.ToWorld(t.Position), w.DirToWorld(t.Normal)), depth);
        }
        if (near?.Bore != false && land?.Bore(p, r) is { } held && (held - p).Length is var into and > 1e-9 && into > deepest)
            best = new Contact(held, (held - p) * (1 / into));
        return best;
    }

    /// <summary>
    /// The walls within <paramref name="reach"/> of a point (its nearest point on their boxes), from the cells round it:
    /// what a body there can meet this substep, so each of its joints asks only these.
    /// </summary>
    public Near Around(Double3 p, double reach)
    {
        var found = new List<Wall>();
        bool bore = land?.NearBore(p, reach) == true;
        if (walls is null)
            return new(found, bore);
        foreach (var w in walls.Within(p, reach))
        {
            if (w.Door != 0 || w.Gate != 0 || w.Tower != 0)
                continue;
            var l = w.ToLocal(p);
            var b = w.Box;
            var outside = new Double3(Math.Max(0, Math.Abs(l.X) - b.Max.X), Math.Max(0, Math.Max(b.Min.Y - l.Y, l.Y - b.Max.Y)), Math.Max(0, Math.Abs(l.Z) - b.Max.Z));
            if (outside.Length <= reach && !found.Contains(w))
                found.Add(w);
        }
        return new(found, bore);
    }

    /// <summary>What's within reach of a body this substep: the walls, and whether a tunnel's bore is (<see cref="Around"/>).</summary>
    public sealed record Near(IReadOnlyList<Wall> Walls, bool Bore);

    /// <summary>The walls near a point that stand all night (see the class's remarks).</summary>
    public IEnumerable<Wall> Fixed(Double3 p)
    {
        if (walls is null)
            yield break;
        foreach (var w in walls.Near(p))
            if (w.Door == 0 && w.Gate == 0 && w.Tower == 0)
                yield return w;
    }

    /// <summary>
    /// The upright lines a wall stands on (a tree's trunk, a pole, a building's corners): its middle and its box's corners,
    /// each from its foot to its top. A car lying any way it's fallen meets them along their length.
    /// </summary>
    public static IEnumerable<(Double3 Foot, Double3 Top)> Uprights(Near near)
    {
        foreach (var w in near.Walls)
        {
            yield return (w.ToWorld(new Double3(0, w.Bottom, 0)), w.ToWorld(new Double3(0, w.Top, 0)));
            foreach (var (x, z) in new[] { (-1.0, -1.0), (-1.0, 1.0), (1.0, -1.0), (1.0, 1.0) })
                yield return (w.ToWorld(new Double3(x * w.HalfLength, w.Bottom, z * w.HalfWidth)), w.ToWorld(new Double3(x * w.HalfLength, w.Top, z * w.HalfWidth)));
        }
    }
}
