using System.Numerics;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

public sealed partial class WorldArt
{
    /// <summary>
    /// What the railway left beside a hand-laid line (GDD App. F.3, the director: "the same three things over and over again";
    /// note 325): dealt as the sim deals a generated line's (Sim.Run.Leavings, note 792), which the art draws from the sim's list
    /// there (PlanDressing). A hand-laid line has no sim lineside, so they're dealt and drawn here, from the art's seed.
    /// </summary>
    void Leavings(MeshBuilder mesh, double from, double to, int seed, Func<double, bool> clear, Func<double, double, bool> onBranch,
        Func<double, double, float, float, float, Matrix4x4> place)
    {
        foreach (var l in Sim.Run.Leavings.Along(from, to, unchecked((ulong)seed)))
        {
            if (!clear(l.Slot * Sim.Run.Leavings.Every) || onBranch(l.Along, l.Lateral) || onBranch(l.Along, l.Lateral + Math.Sign(l.Lateral) * 3))
                continue;
            var pieces = Leaving(l.Kind, l.Variant).Where(p => p.Piece is not null).ToList();
            // Clear of the cars going by (note 780): one that would come within LeavingClearM of the line's middle is turned
            // round; if it still would, it isn't left there.
            float yaw = (float)l.Yaw;
            if (Nearest(pieces, l.Lateral, yaw) < Sim.Run.LinesideProps.LeavingClearM && Nearest(pieces, l.Lateral, yaw += MathF.PI) < Sim.Run.LinesideProps.LeavingClearM)
                continue;
            foreach (var (piece, offset, lift, turn) in pieces)
            {
                // The offset turns with the piece: a cluster keeps its shape whichever way it's laid.
                var o = Vector3.Transform(offset, Matrix4x4.CreateRotationY(yaw));
                var m = place(l.Along - o.Z, l.Lateral + o.X, yaw + turn, 1, 0.03f);
                mesh.Instances.Add(new MeshInstance(piece!, Matrix4x4.CreateTranslation(0, lift, 0) * m));
            }
        }
    }

    /// <summary>
    /// How near the line's middle a leaving laid <paramref name="lateral"/> out at <paramref name="yaw"/> comes (m, on its own
    /// side; less than nothing across it): every vertex of every piece of it, as it's placed (a piece's +X out to the right).
    /// </summary>
    static double Nearest(List<(MeshAsset? Piece, Vector3 Offset, float Lift, float Turn)> pieces, double lateral, float yaw)
    {
        double near = double.MaxValue;
        int side = Math.Sign(lateral);
        foreach (var (piece, offset, _, turn) in pieces)
        {
            float ox = Vector3.Transform(offset, Matrix4x4.CreateRotationY(yaw)).X;
            var spin = Matrix4x4.CreateRotationY(yaw + turn);
            foreach (var v in piece!.Vertices)
                near = Math.Min(near, side * (lateral + ox + Vector3.Transform(v.Position, spin).X));
        }
        return near;
    }

    /// <summary>
    /// A leaving's pieces (Sim.Run.Leavings.Cluster: one, or a little cluster), each the lineside's piece by its sim name
    /// (LinesideFootprints), with its offset from the spot, its lift and its own turn: the line's and a stop's yard's alike.
    /// </summary>
    IEnumerable<(MeshAsset? Piece, Vector3 Offset, float Lift, float Turn)> Leaving(string name, int v) =>
        Sim.Run.Leavings.Cluster(name, v).Select(p =>
            (LinesideFootprints.ByName.TryGetValue(p.Name, out var kind) ? Piece(kind.Cached(p.Variant), () => kind.Make(_look, p.Variant)) : null,
             new Vector3((float)p.X, 0, (float)p.Z), (float)p.Lift, (float)p.Turn));
}
