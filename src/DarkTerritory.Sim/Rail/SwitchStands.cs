using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Rail;

/// <summary>A switch a player threw this tick, or tried to.</summary>
/// <param name="Moved">False when a wheel on the points held them.</param>
public readonly record struct SwitchThrow(int Branch, int PlayerId, bool Diverging, bool Moved);

/// <summary>
/// The switch stands beside every branch's points (GDD §17: "switches are thrown by hand. Someone is on the ground at
/// every junction, alone, calling the route"). A player stands at one and holds Use to throw it; the host decides.
/// </summary>
public sealed class SwitchStands(JunctionTuning tuning)
{
    // Who has a hand on which lever, and for how long (the ground isn't part of the train, so not ActionProgress).
    readonly Dictionary<int, (int Branch, double Held)> _held = new();

    public JunctionTuning Tuning { get; } = tuning;

    /// <summary>A branch's lever, at hand height beside its points on the branch's side.</summary>
    public Double3 LeverAt(RailLine line, int branch)
    {
        var b = line.Branches[branch];
        var t = line.Sample(b.Toe);
        var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
        return t.Position + right * (b.Side * Tuning.LeverOffset) + Double3.Up * 0.9;
    }

    /// <summary>The switch a player could put their hands on (the HUD's prompt, and <see cref="CrewAct"/>).</summary>
    public int? InReach(in PlayerState s, TrainOnLine train)
    {
        if (!s.Alive || train.Line.Branches.Count == 0)
            return null;
        var at = PlayerMotor.WorldPosition(s, train);
        foreach (var b in train.Line.Branches)
            if ((at - LeverAt(train.Line, b.Index)).Length <= Tuning.LeverReach)
                return b.Index;
        return null;
    }

    /// <summary>How far through throwing it a player is, 0..1 (host only: clients see the switch move when it has).</summary>
    public double Progress(int playerId) => _held.TryGetValue(playerId, out var h) ? Math.Min(1, h.Held / Tuning.ThrowSeconds) : 0;

    /// <summary>Host: a player's hands this tick. Holding Use at a stand long enough throws it over, once per hold.</summary>
    public SwitchThrow? CrewAct(in PlayerState s, in PlayerIntent intent, int playerId, TrainOnLine train)
    {
        if (InReach(s, train) is not { } branch || !intent.Has(PlayerButtons.Use) || intent.MoveZ > 0.5)
        {
            _held.Remove(playerId);
            return null;
        }
        double before = _held.TryGetValue(playerId, out var h) && h.Branch == branch ? h.Held : 0;
        double after = before + SimConstants.TickSeconds;
        _held[playerId] = (branch, after);
        if (before >= Tuning.ThrowSeconds || after < Tuning.ThrowSeconds)
            return null;
        bool diverge = !train.Diverging(branch);
        return new SwitchThrow(branch, playerId, diverge, train.ThrowSwitch(branch, diverge, Tuning.PointsLength));
    }
}
