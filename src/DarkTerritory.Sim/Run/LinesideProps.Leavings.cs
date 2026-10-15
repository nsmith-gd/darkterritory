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
            foreach (var (piece, s, lateral) in laid)
                yield return PieceOf(piece.Name, s, lateral, yaw + piece.Turn, 1, LeavingSink - piece.Lift, piece.Variant, GroundAt(s, lateral));
        }
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
