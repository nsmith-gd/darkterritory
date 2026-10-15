using Ballast;

namespace DarkTerritory.Sim.Run;

// The woods keep off the lineside's buildings (note 800; note 797's "not yet"): the trees and boulders of a slot were dealt
// without asking where the road's homesteads, the shore's sheds and the biomes' houses, barns, tanks and churches stand, so
// a spruce grew up through a barn's roof or a burying ground's stones, its wall inside the building's. Nor did the buildings
// ask where each other stood (note 808): a church was dealt half inside a house, a chimney inside a tank.
public sealed partial class LinesideProps
{
    /// <summary>How far a tree's trunk or a boulder keeps from a building's wall (m), past its own reach.</summary>
    const double BuildingRoomM = 0.5;

    /// <summary>
    /// Every building of the lineside that stands (the road's, the shore's and the biomes' kit pieces, as <see cref="Props"/>
    /// deals them: none in a board's sight line or a leaving, nor in another building), with its walls, by how far along it
    /// stands. Dealt once, for what the woods keep off (<see cref="InABuilding"/>); asked from the art's workers too, so made
    /// once whoever asks first. The railway's leavings are kept off on their own (<see cref="InALeaving"/>).
    /// </summary>
    KitStanding KitBuildings() =>
        LazyInitializer.EnsureInitialized(ref _kitBuildings, () =>
        {
            double end = _line.Length + BlockM;
            var dealt = Roads(0, end, double.MaxValue).Concat(Shores(0, end, double.MaxValue)).Concat(Blocks(0, end, double.MaxValue))
                .Where(p => p.Kind == LinesideKind.Piece && !Hidden(p) && !InALeaving(p))
                .Select(p => (Piece: p, Walls: WallsOf(p).ToArray())).Where(b => b.Walls.Length > 0);
            // Each in the order it's dealt (the road's homesteads, then the shore's, then the biomes' by the block), standing
            // only if it's clear of every one already standing: one dealt into another isn't there (note 808).
            var standing = new List<(LinesideProp Piece, Wall[] Walls)>();
            var crowded = new HashSet<LinesideProp>();
            foreach (var b in dealt)
            {
                if (standing.Any(o => Crowds(o, b)))
                    crowded.Add(b.Piece);
                else
                    standing.Add(b);
            }
            return new KitStanding([.. standing.OrderBy(b => b.Piece.Along)], crowded);
        });

    KitStanding? _kitBuildings;

    /// <summary>The lineside's buildings that stand, by along, and those dealt into one already standing, which aren't there.</summary>
    sealed record KitStanding((LinesideProp Piece, Wall[] Walls)[] Standing, HashSet<LinesideProp> Crowded);

    /// <summary>
    /// Whether two buildings would stand in each other: any wall of one inside any of the other's. Only inside: a homestead's
    /// woodpile is stacked against its house. A stone wall's lengths run on into each other as they're laid, so they don't crowd.
    /// </summary>
    static bool Crowds((LinesideProp Piece, Wall[] Walls) a, (LinesideProp Piece, Wall[] Walls) b)
    {
        if (a.Piece.Species == "stoneWall" && b.Piece.Species == "stoneWall")
            return false;
        foreach (var w in a.Walls)
            foreach (var v in b.Walls)
                if (Overlap(w, v, 0))
                    return true;
        return false;
    }

    /// <summary>Whether two walls come within <paramref name="room"/> of each other, on the ground (the four axes they're turned on).</summary>
    static bool Overlap(Wall a, Wall b, double room)
    {
        double dx = b.Centre.X - a.Centre.X, dz = b.Centre.Z - a.Centre.Z;
        // Quick out: further apart than their half diagonals.
        double reach = double.Hypot(a.HalfLength, a.HalfWidth) + double.Hypot(b.HalfLength, b.HalfWidth) + room;
        if (dx * dx + dz * dz > reach * reach)
            return false;
        foreach (var (nx, nz) in new[] { (a.Axis.X, a.Axis.Z), (-a.Axis.Z, a.Axis.X), (b.Axis.X, b.Axis.Z), (-b.Axis.Z, b.Axis.X) })
        {
            double ra = a.HalfLength * Math.Abs(a.Axis.X * nx + a.Axis.Z * nz) + a.HalfWidth * Math.Abs(-a.Axis.Z * nx + a.Axis.X * nz);
            double rb = b.HalfLength * Math.Abs(b.Axis.X * nx + b.Axis.Z * nz) + b.HalfWidth * Math.Abs(-b.Axis.Z * nx + b.Axis.X * nz);
            if (Math.Abs(dx * nx + dz * nz) > ra + rb + room)
                return false;
        }
        return true;
    }

    /// <summary>The furthest any building reaches from its foot (m): how far along to look for one.</summary>
    double BuildingReach() => _buildingReach ??= KitBuildings().Standing.Select(b => Extent(b.Piece)).DefaultIfEmpty(0).Max();

    double? _buildingReach;

    /// <summary>
    /// Whether <paramref name="p"/> would stand in one of the lineside's buildings: a tree or a boulder (note 800) whose trunk
    /// (or its lump's reach) and a little room come inside one of a building's walls, as the walls stand them (what stands
    /// among a building's walls without touching one, a tree between a burying ground's rows or in a homestead's yard, stays);
    /// a building dealt into one already standing (note 808).
    /// </summary>
    bool InABuilding(LinesideProp p)
    {
        if (p.Kind == LinesideKind.Piece)
            return KitBuildings().Crowded.Contains(p);
        if (p.Kind is not (LinesideKind.Tree or LinesideKind.Rock))
            return false;
        var all = KitBuildings().Standing;
        double reach = (p.Kind == LinesideKind.Tree ? p.Radius : Extent(p)) + BuildingRoomM;
        // Along the line isn't across the land out on a curve's outside, so the window is wider than the reach.
        double window = (reach + BuildingReach()) * 1.5 + 5;
        int lo = 0, hi = all.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (all[mid].Piece.Along < p.Along - window)
                lo = mid + 1;
            else
                hi = mid;
        }
        Double3? at = null;
        for (int i = lo; i < all.Length && all[i].Piece.Along <= p.Along + window; i++)
            foreach (var w in all[i].Walls)
            {
                var local = w.ToLocal(at ??= World(p.Along, p.Lateral));
                if (Math.Abs(local.X) < w.HalfLength + reach && Math.Abs(local.Z) < w.HalfWidth + reach)
                    return true;
            }
        return false;
    }
}
