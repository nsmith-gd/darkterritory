using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Rail;

namespace DarkTerritory.Sim.Run;

/// <summary>
/// A pine standing beside an alternate or a dead line (ARCHITECTURE §8 note 432, after notes 371 and 389): which branch, how far
/// <see cref="Along"/> its own track and <see cref="Lateral"/> out from it (right positive), turned <see cref="Yaw"/>,
/// <see cref="Height"/> tall, the art's mesh <see cref="Variant"/>, and the land's height at its foot.
/// </summary>
public readonly record struct BranchTree(int Branch, double Along, double Lateral, double Yaw, double Height, int Variant, double Ground)
{
    /// <summary>Its trunk's radius at the ground: the pine's (WorldKit.Boughs, 0.015 of its height), as the main line's.</summary>
    public double Radius => Math.Clamp(Height * 0.015, 0.08, 0.45);
}

public sealed partial class LinesideProps
{
    /// <summary>A branch's land is drawn, and its pines dealt, in cells this long along it (PlanArt.BranchCell's, 100 m).</summary>
    public const double BranchCellM = 100;
    /// <summary>How many pines a branch's cell tries at, and how far out from its track they stand (m): the art's as it was.</summary>
    const int BranchTries = 10;
    const double BranchNearM = 12, BranchSpreadM = 55;
    /// <summary>How far out the main line's own land and woods run (WorldArt.PlanLateral's 300 m, less its edge): a branch's pines leave it be.</summary>
    public const double MainLandM = 290;

    /// <summary>
    /// A branch's pines from <paramref name="from"/> to <paramref name="to"/> along its own track (note 432): a stand
    /// out beyond its verge, ten tries a 100 m cell on a stream of its own off the night's seed (the
    /// branch's edge and the cell), each try's draws made before it's tested, so a stretch asked in pieces deals as the
    /// whole. None where the main line's own land runs (its woods are its), in water, or within 9 m of any track.
    /// </summary>
    public IEnumerable<BranchTree> BranchTrees(int branch, double from, double to)
    {
        if (branch < 0 || branch >= _line.Branches.Count || _plan.EdgeOfBranch(branch) is not { } edge)
            yield break;
        var local = _line.Branches[branch].Local;
        long first = (long)Math.Floor(Math.Max(0, from) / BranchCellM), last = (long)Math.Floor(Math.Min(to, local.Length - 1e-6) / BranchCellM);
        for (long i = first; i <= last; i++)
        {
            double a = i * BranchCellM, b = Math.Min((i + 1) * BranchCellM, local.Length);
            var rng = Streams.Rng(_seed, "lineside-branch", edge.Edge, i);
            for (int k = 0; k < BranchTries; k++)
            {
                double s = a + rng.NextDouble() * (b - a), side = rng.NextDouble(), out_ = rng.NextDouble(), yaw = rng.NextDouble() * 2 * Math.PI,
                    tall = rng.NextDouble();
                int variant = (int)(rng.NextDouble() * 4);
                if (s < from || s >= to)
                    continue;
                double lateral = (side < 0.5 ? -1 : 1) * (BranchNearM + out_ * BranchSpreadM);
                var t = local.Sample(s);
                var w = t.Position + Double3.Cross(t.Tangent, Double3.Up).Normalized * lateral;
                if (OnMainsLand(w) || _terrain.WaterAt(w.X, w.Z) is not null || _terrain.Nearby(w.X, w.Z, 12).Any(n => Math.Abs(n.Lateral) < 9))
                    continue;
                yield return new BranchTree(branch, s, lateral, yaw, 7 + tall * 10, variant, _terrain.Height(w.X, w.Z) - 0.15);
            }
        }
    }

    /// <summary>Where the main line's own land runs (its woods are dealt by <see cref="Props"/>).</summary>
    bool OnMainsLand(Double3 w)
    {
        foreach (var n in _terrain.Nearby(w.X, w.Z, MainLandM + 10))
            if (n.Edge == _main)
                return Math.Abs(n.Lateral) < MainLandM;
        return false;
    }

    /// <summary>
    /// Every branch's pines out to <paramref name="reach"/> from its own track, stood as walls: a trunk as the box round its
    /// radius, along its track, to its top (as the main line's trees, <see cref="WallsOf"/>).
    /// </summary>
    public IEnumerable<Wall> BranchWalls(double reach)
    {
        lock (_branchSolids)
        {
            if (!_branchSolids.TryGetValue(reach, out var walls))
            {
                walls = [];
                foreach (var a in _plan.Alignment.Where(a => a.Role is EdgeRole.Alternate or EdgeRole.DeadLine))
                {
                    if (a.Branch < 0 || a.Branch >= _line.Branches.Count)
                        continue;
                    var local = _line.Branches[a.Branch].Local;
                    foreach (var tree in BranchTrees(a.Branch, 0, local.Length))
                        if (Math.Abs(tree.Lateral) - tree.Radius <= reach)
                            walls.Add(WallOf(local, tree));
                }
                _branchSolids[reach] = walls;
            }
            return walls;
        }
    }

    readonly Dictionary<double, List<Wall>> _branchSolids = [];

    /// <summary>A branch's pine as it stands: its trunk's box, along its track.</summary>
    public static Wall WallOf(RailLine local, BranchTree tree)
    {
        var t = local.Sample(Math.Clamp(tree.Along, 0, local.Length));
        var along = new Double3(t.Tangent.X, 0, t.Tangent.Z).Normalized;
        var at = (t.Position + Double3.Cross(t.Tangent, Double3.Up).Normalized * tree.Lateral) with { Y = 0 };
        return new Wall(at, along, tree.Radius, tree.Radius, tree.Ground - 3, tree.Ground + tree.Height);
    }
}
