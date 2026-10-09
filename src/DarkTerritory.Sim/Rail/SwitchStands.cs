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
/// every junction, alone, calling the route"). A player stands at one and holds Use to throw it; the host decides. With
/// spec F.3's powered switch thrower fitted, the cab's lever throws the next points ahead the same way (note 196).
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

    /// <summary>
    /// The switch a player could put their hands on (the HUD's prompt, and <see cref="CrewAct"/>): a stand's, or with the
    /// powered thrower fitted, the next points ahead from its lever in the cab (<see cref="FromCab"/>).
    /// </summary>
    /// <param name="hand">When hands are reported (T29), a reaching hand has to be on the lever.</param>
    public int? InReach(in PlayerState s, TrainOnLine train, HandTuning? hand = null) =>
        AtStand(s, train, hand) ?? (AtThrower(s, train, hand) ? PointsAhead(train) : null);

    /// <summary>The stand whose lever a player has their hands on.</summary>
    int? AtStand(in PlayerState s, TrainOnLine train, HandTuning? hand)
    {
        // Not from the cab: cab forward (note 276) it can stand right over a toe, but its walls are between the crew and the
        // lever, and getting down to it is the ground excursion the powered thrower saves (spec F.3).
        if (!s.Alive || train.Line.Branches.Count == 0 || PlayerMotor.InCab(s, train))
            return null;
        var at = PlayerMotor.WorldPosition(s, train);
        foreach (var b in train.Line.Branches)
        {
            var lever = LeverAt(train.Line, b.Index);
            if (PlayerMotor.Grips(s, train, hand, lever, (at - lever).Length <= Tuning.LeverReach))
                return b.Index;
        }
        return null;
    }

    /// <summary>
    /// At the powered switch thrower's lever in the cab (spec F.3 "removes the ground excursion at junctions"; note 196),
    /// and it's fitted (train.json <c>composition.switchThrower</c>).
    /// </summary>
    public static bool AtThrower(in PlayerState s, TrainOnLine train, HandTuning? hand = null) =>
        s.Alive && train.Dynamics.Tuning.Composition.SwitchThrower && s.Parent == 0 && PlayerMotor.InCab(s, train)
        && CrewActions.Nearest(s, train, hand) == InteractableKind.Points;

    /// <summary>
    /// The points the powered thrower reaches: the nearest ahead of the engine's front on the main line, within
    /// <c>composition.thrower.reach</c>. Null with none, or with the engine off the main line (down a branch, there are none
    /// ahead of it).
    /// </summary>
    public static int? PointsAhead(TrainOnLine train)
    {
        double front = train.Line.MainDistance(train.Dynamics.Path, train.Dynamics.Distance);
        if (double.IsNaN(front))
            return null;
        double reach = train.Dynamics.Tuning.Composition.Thrower.Reach;
        int? best = null;
        foreach (var b in train.Line.Branches)
            if (b.Toe >= front && b.Toe - front <= reach && (best is not { } k || b.Toe < train.Line.Branches[k].Toe))
                best = b.Index;
        return best;
    }

    /// <summary>Whether the train's slow enough for the powered thrower (<c>composition.thrower.maxSpeed</c>, either way).</summary>
    public static bool SlowEnough(TrainOnLine train) => Math.Abs(train.Dynamics.Velocity) <= train.Dynamics.Tuning.Composition.Thrower.MaxSpeed;

    /// <summary>Who's at the cab's lever, the switch it reaches (null with none), and whether it'll throw now (the HUD's prompt).</summary>
    public static (int? Branch, bool Slow)? CabLever(in PlayerState s, TrainOnLine train, HandTuning? hand = null) =>
        AtThrower(s, train, hand) ? (PointsAhead(train), SlowEnough(train)) : null;

    /// <summary>
    /// Host: a cannonball that found a stand's lever (the director, 9 Oct 2026, note 593: "someone should be able to throw a
    /// switch by shooting it with the cannon if their aim is good enough"). Its flight from the muzzle to where it came down
    /// passes within <see cref="JunctionTuning.ShotReach"/> of the lever: over it goes, in the gunner's name, as a hand on it
    /// would throw it (not with a wheel on the points). The nearest lever to its line, if it passes more than one.
    /// </summary>
    public SwitchThrow? Shot(in Combat.GunShot shot, TrainOnLine train)
    {
        if (Tuning.ShotReach <= 0 || train.Line.Branches.Count == 0)
            return null;
        var end = shot.Muzzle + shot.Direction.Normalized * shot.Distance;
        int? best = null;
        double nearest = Tuning.ShotReach;
        foreach (var b in train.Line.Branches)
        {
            double d = Apart(LeverAt(train.Line, b.Index), shot.Muzzle, end);
            if (d <= nearest)
                (nearest, best) = (d, b.Index);
        }
        if (best is not { } branch)
            return null;
        bool diverge = !train.Diverging(branch);
        return new SwitchThrow(branch, shot.Shooter, diverge, train.ThrowSwitch(branch, diverge, Tuning.PointsLength));
    }

    /// <summary>How far <paramref name="p"/> is from the segment <paramref name="a"/>–<paramref name="b"/>.</summary>
    static double Apart(Double3 p, Double3 a, Double3 b)
    {
        var ab = b - a;
        double len2 = Double3.Dot(ab, ab);
        double t = len2 < 1e-12 ? 0 : Math.Clamp(Double3.Dot(p - a, ab) / len2, 0, 1);
        return (p - (a + ab * t)).Length;
    }

    /// <summary>How far through throwing it a player is, 0..1 (host only: clients see the switch move when it has).</summary>
    public double Progress(int playerId) => _held.TryGetValue(playerId, out var h) ? Math.Min(1, h.Held / Tuning.ThrowSeconds) : 0;

    /// <summary>Host: a player's hands this tick. Holding Use at a stand long enough throws it over, once per hold.</summary>
    public SwitchThrow? CrewAct(in PlayerState s, in PlayerIntent intent, int playerId, TrainOnLine train, HandTuning? hand = null)
    {
        // From the cab (note 196), only with the train slowed for it.
        int? reached = AtStand(s, train, hand) ?? (AtThrower(s, train, hand) && SlowEnough(train) ? PointsAhead(train) : null);
        if (reached is not { } branch || !intent.Has(PlayerButtons.Use) || intent.MoveZ > 0.5)
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
