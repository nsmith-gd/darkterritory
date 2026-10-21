using Ballast;

namespace DarkTerritory.Sim.Run;

// The railway's leavings beside the line (note 792; note 325, E1's; GDD App. F.1's "the world is solid", its last lineside
// piece "still open"): dealt here from the night's seed by the art's rules (Leavings), each piece stood on its footprint
// (footprints.json, measured off its mesh), so they're walked into where they're drawn. The art draws them from this list.
public sealed partial class LinesideProps
{
    /// <summary>How near the line's middle no leaving comes (m): outside the cars' sides (train.json roofWidth 3, 1.5 a side) by half a metre (note 780).</summary>
    public const double LeavingClearM = 2;

    /// <summary>How deep a leaving's pieces sit in the ground (m): the art's.</summary>
    const double LeavingSink = 0.03;

    /// <summary>
    /// The leavings whose stretch starts in [<paramref name="from"/>, <paramref name="to"/>): each of a leaving's pieces as a
    /// piece of the lineside. Kept off the line's own ground as the art kept them (its stretch clear of a bridge or a
    /// tunnel's cutting, its spot and 3 m out past it off a branch's, a stop's, a road's or the water), and each piece off a
    /// stop's whole ground; one that would come within <see cref="LeavingClearM"/> of the line's middle is turned round, or
    /// if it still would isn't left there.
    /// </summary>
    IEnumerable<LinesideProp> LineLeavings(double from, double to)
    {
        foreach (var (_, yaw, laid) in Laid(from, to))
            foreach (var (piece, s, lateral) in laid)
                yield return PieceOf(piece.Name, s, lateral, yaw + piece.Turn, 1, LeavingSink - piece.Lift, piece.Variant, GroundAt(s, lateral));
    }

    /// <summary>
    /// The leavings left on the line (<see cref="LineLeavings"/>'s, before they're pieces): each as dealt, the way it's
    /// turned there, and its pieces where they lie. What a note left at one is put by (note 965).
    /// </summary>
    public IEnumerable<(Leavings.Dealt Leaving, double Yaw, List<(Leavings.Piece Piece, double S, double Lateral)> Pieces)> Laid(double from, double to)
    {
        foreach (var l in Leavings.Along(from, to, _seed))
        {
            if (l.Along < 0 || l.Along > _line.Length || !Clear(l.Slot * Leavings.Every)
                || !OffTheLine(l.Along, l.Lateral) || !OffTheLine(l.Along, l.Lateral + Math.Sign(l.Lateral) * 3))
                continue;
            var pieces = Leavings.Cluster(l.Kind, l.Variant).ToList();
            double yaw = l.Yaw;
            if (NearestLeaving(pieces, l.Lateral, yaw) < LeavingClearM && NearestLeaving(pieces, l.Lateral, yaw += Math.PI) < LeavingClearM)
                continue;
            // Each piece where it lies off the spot (a hut's ties 2.8 m along), and none on a stop's ground: a stop's yard has
            // its own piles about its buildings (the art's YardLeavings), and the line's are left out on the line.
            var laid = pieces.Select(piece => (Piece: piece, At: Leavings.Offset(piece, yaw))).Select(x => (x.Piece, S: l.Along + x.At.Along, Lateral: l.Lateral + x.At.Across)).ToList();
            if (laid.Any(x => !Free(x.S, x.Lateral)))
                continue;
            yield return (l, yaw, laid);
        }
    }

    /// <summary>
    /// Every piece of the line's leavings, by how far along it lies: dealt once, for what the rest of the lineside keeps off
    /// (<see cref="InALeaving"/>). Asked from the art's workers too, so made once whoever asks first.
    /// </summary>
    LinesideProp[] AllLeavings() =>
        LazyInitializer.EnsureInitialized(ref _allLeavings, () => [.. LineLeavings(0, _line.Length + Leavings.Every).OrderBy(p => p.Along)]);

    LinesideProp[]? _allLeavings;

    /// <summary>How far a tree, a boulder or a prop keeps from a leaving's piece (m), past both of their reaches.</summary>
    const double LeavingRoomM = 0.5;

    /// <summary>
    /// Whether <paramref name="p"/>, a tree, a boulder or a prop of the lineside, would stand in one of the railway's leavings
    /// (note 794; note 792's "not yet"): the woods and the biomes' props were dealt without asking where the leavings lie, and
    /// now and then a trunk grew up through a gangs' hut or a stack of stores, its wall inside the hut's. Each leaving piece's
    /// reach round its foot, and the prop's own (a trunk's, a boulder's or a piece's), and a little room between.
    /// </summary>
    bool InALeaving(LinesideProp p)
    {
        if (p.Kind == LinesideKind.Pole || (p.Kind == LinesideKind.Piece && Leavings.PieceNames.Contains(p.Species)))
            return false;
        var all = AllLeavings();
        double reach = p.Kind == LinesideKind.Tree ? p.Radius : Extent(p);
        // The leavings are sorted along the line; none reaches further than a hut's stack of ties and its rail (5 m).
        int lo = 0, hi = all.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (all[mid].Along < p.Along - reach - 6)
                lo = mid + 1;
            else
                hi = mid;
        }
        for (int i = lo; i < all.Length && all[i].Along <= p.Along + reach + 6; i++)
        {
            var l = all[i];
            if (double.Hypot(l.Along - p.Along, l.Lateral - p.Lateral) < Extent(l) + reach + LeavingRoomM)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Whether a small thing laid at <paramref name="along"/>, <paramref name="lateral"/> (a note left at a leaving, note 965)
    /// is clear of every leaving's pieces by <paramref name="room"/> past their reach, so it isn't lost in a hut's wall or a
    /// grave's mound.
    /// </summary>
    public bool ClearOfLeavings(double along, double lateral, double room)
    {
        foreach (var l in AllLeavings())
            if (Math.Abs(l.Along - along) < 12 && double.Hypot(l.Along - along, l.Lateral - lateral) < Extent(l) + room)
                return false;
        return true;
    }

    /// <summary>
    /// How near the line's middle a leaving laid <paramref name="lateral"/> out at <paramref name="yaw"/> comes (m, on its own
    /// side; less than nothing across it): the boxes of every piece of it, turned as they're laid (as the walls stand them).
    /// </summary>
    double NearestLeaving(List<Leavings.Piece> pieces, double lateral, double yaw)
    {
        double near = double.MaxValue;
        int side = Math.Sign(lateral);
        foreach (var piece in pieces)
        {
            var (across, _) = Leavings.Offset(piece, yaw);
            double turn = yaw + piece.Turn, c = DMath.Cos(turn), s = DMath.Sin(turn);
            foreach (var b in Boxes(piece.Name, piece.Variant))
            {
                double centre = lateral + across + b[0] * c + b[1] * s;
                double spread = Math.Abs(b[2] * DMath.Cos(turn + b[4])) + Math.Abs(b[3] * DMath.Sin(turn + b[4]));
                near = Math.Min(near, side * centre - spread);
            }
        }
        return near;
    }
}
