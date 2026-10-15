using Ballast;

namespace DarkTerritory.Sim.Run;

// The woods keep off the lineside's buildings (note 800; note 797's "not yet"): the trees and boulders of a slot were dealt
// without asking where the road's homesteads, the shore's sheds and the biomes' houses, barns, tanks and churches stand, so
// a spruce grew up through a barn's roof or a burying ground's stones, its wall inside the building's.
public sealed partial class LinesideProps
{
    /// <summary>How far a tree's trunk or a boulder keeps from a building's wall (m), past its own reach.</summary>
    const double BuildingRoomM = 0.5;

    /// <summary>
    /// Every building of the lineside that stands (the road's, the shore's and the biomes' kit pieces, as <see cref="Props"/>
    /// deals them: none in a board's sight line or a leaving), with its walls, by how far along it stands. Dealt once, for
    /// what the woods keep off (<see cref="InABuilding"/>); asked from the art's workers too, so made once whoever asks first.
    /// The railway's leavings are kept off on their own (<see cref="InALeaving"/>).
    /// </summary>
    (LinesideProp Piece, Wall[] Walls)[] KitBuildings() =>
        LazyInitializer.EnsureInitialized(ref _kitBuildings, () =>
        {
            double end = _line.Length + BlockM;
            return [.. Roads(0, end, double.MaxValue).Concat(Shores(0, end, double.MaxValue)).Concat(Blocks(0, end, double.MaxValue))
                .Where(p => p.Kind == LinesideKind.Piece && !Hidden(p) && !InALeaving(p))
                .Select(p => (Piece: p, Walls: WallsOf(p).ToArray())).Where(b => b.Walls.Length > 0).OrderBy(b => b.Piece.Along)];
        });

    (LinesideProp Piece, Wall[] Walls)[]? _kitBuildings;

    /// <summary>The furthest any building reaches from its foot (m): how far along to look for one.</summary>
    double BuildingReach() => _buildingReach ??= KitBuildings().Select(b => Extent(b.Piece)).DefaultIfEmpty(0).Max();

    double? _buildingReach;

    /// <summary>
    /// Whether <paramref name="p"/>, a tree or a boulder, would stand in one of the lineside's buildings (note 800): its trunk
    /// (or its lump's reach) and a little room inside one of the building's walls, as the walls stand them. What stands among
    /// a building's walls without touching one (a tree between a burying ground's rows, in a homestead's yard) stays.
    /// </summary>
    bool InABuilding(LinesideProp p)
    {
        if (p.Kind is not (LinesideKind.Tree or LinesideKind.Rock))
            return false;
        var all = KitBuildings();
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
