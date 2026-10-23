using Ballast;
using DarkTerritory.Sim.Combat;

namespace DarkTerritory.Sim.Run;

public sealed partial class StopWalls
{
    /// <summary>
    /// Note 1010 (the director's playtest of 23 Oct: "It should check a cast to be like, can you actually see this or not?"):
    /// the distance along the segment from <paramref name="from"/> towards <paramref name="to"/> to the first wall standing in
    /// its way (an open house's door only while it's shut, a coaling tower only while it stands, a gate's leaf only while it's
    /// shut, as <see cref="Near"/> has them), or +∞. Every cell the segment's box covers is looked in: a ping's ray is short.
    /// </summary>
    public double Raycast(Double3 from, Double3 to)
    {
        var d = to - from;
        double length = d.Length;
        if (length < 1e-9)
            return double.PositiveInfinity;
        var dir = d * (1 / length);
        var (x0, z0) = CellOf(Math.Min(from.X, to.X), Math.Min(from.Z, to.Z));
        var (x1, z1) = CellOf(Math.Max(from.X, to.X), Math.Max(from.Z, to.Z));
        double best = double.PositiveInfinity;
        var seen = new HashSet<int>();
        for (int x = x0; x <= x1; x++)
            for (int z = z0; z <= z1; z++)
            {
                if (!_cells.TryGetValue((x, z), out var list))
                    continue;
                foreach (int i in list)
                {
                    var w = _walls[i];
                    if (!seen.Add(i) || w.Door != 0 && !_shut.Contains(w.Door) || w.Tower != 0 && _felled.Contains(w.Tower)
                        || w.Gate != 0 && _gatesOpen.Contains(w.Gate))
                        continue;
                    var o = w.ToLocal(from);
                    var ld = new Double3(dir.X * w.Axis.X + dir.Z * w.Axis.Z, dir.Y, -dir.X * w.Axis.Z + dir.Z * w.Axis.X);
                    if (Guns.RayBox(o, ld, w.Box) is { } t && t <= length && t < best)
                        best = t;
                }
            }
        return best;
    }
}
