using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Sim.Enemies;

/// <summary>What the Hanger is doing (replicated in <see cref="Enemy.Height"/>), for its clips and cues.</summary>
public enum HangerMode : byte { Hidden, Grab, Haul, Hold, Hit, Retreat }

/// <summary>
/// THE HANGER · movement · outside (GDD §21; ARCHITECTURE §8 note 586; the director, 9 Oct 2026: "if you touch a strand that's
/// hanging, it grabs you. It's a simple positional awareness"; docs/design/creatures/hanger.md). It lives in a house's roof
/// space, and its strands hang from the ceiling to about chest height. Walk into one and it has you, hauling you up the
/// strand; a friend prising you free (holding Use beside you) or a blow on it lets you go, and it draws back into the roof a
/// while. Its strands quivering are its telegraph, from the moment anyone's in its house. Rule: don't touch the strands.
/// </summary>
/// <remarks>
/// One to a house, in its roof space: <see cref="Enemy.Local"/> its body (in the world, up under the roof);
/// <see cref="Enemy.Extra2"/> the house (an index into the stop walls' <c>OpenHouses</c>); <see cref="Enemy.Extra"/> the
/// player it has (−1 none); <see cref="Enemy.Height"/> its <see cref="HangerMode"/>. Its strands are <see cref="Strands"/>:
/// alike on every machine from the house and its id, so they're never sent.
/// </remarks>
public sealed class Hanger(int id) : Enemy(id)
{
    /// <summary>The strands' bottoms hang this high off the floor (a hand at about chest height brushes them), from the ceiling.</summary>
    public const double StrandBottom = 0.9, Ceiling = 2.6;

    double _modeSeconds;
    Double3 _from;
    List<Double3> _strands = [];
    int _strand = -1;

    public override EnemyKind Kind => EnemyKind.Hanger;
    public override PressureZone Zone => PressureZone.Outside;
    public override Sense Sense => Sense.Movement;
    public override Want Want => Want.Kill;
    /// <summary>It belongs to its house: the director never dismisses it for want of company.</summary>
    public override bool StaysAboard => true;
    /// <summary>A friend holding Use beside the one it has prises them off the strand.</summary>
    public override bool PullsFree => true;
    public override double MeleeRadius => 1.0;

    public HangerMode Mode => (HangerMode)(int)Height;
    public int House => (int)Extra2;
    /// <summary>Its strands' feet, in the world (on the floor under each: the strand hangs from the ceiling to <see cref="StrandBottom"/> above it).</summary>
    public IReadOnlyList<Double3> StrandsAt => _strands;

    /// <summary>
    /// Where its strands hang, in the building's own plan (metres along and across it from its middle, as
    /// <see cref="StopWalls.OpenHouse.World"/> takes them): <paramref name="count"/> of them, spread over the home's floor clear
    /// of its walls, from <paramref name="seed"/> (the Hanger's id), so every machine has the same.
    /// </summary>
    public static List<(double X, double Y)> Strands(StopWalls.OpenHouse house, int seed, int count)
    {
        var dice = new Pcg32((ulong)(uint)seed, 0x48_414E_4745UL);
        var plan = house.Plan;
        double hx = Math.Max(0.4, plan.HalfX - 0.6), hy = Math.Max(0.4, plan.HalfY - 0.6);
        var found = new List<(double, double)>();
        for (int i = 0; i < count; i++)
            found.Add((plan.X + (dice.NextDouble() * 2 - 1) * hx, plan.Y + (dice.NextDouble() * 2 - 1) * hy));
        return found;
    }

    /// <summary>Up in <paramref name="house"/>'s roof over its middle, its strands hung.</summary>
    public static Hanger In(int id, StopWalls.OpenHouse house, HangerTuning t) =>
        new(id)
        {
            Attached = Loose,
            Local = house.Middle + Double3.Up * (Ceiling - 0.3),
            Extra = -1,
            Extra2 = house.Index,
            Health = t.Health,
            _strands = [.. Strands(house, id, t.Strands).Select(p => house.World(p.X, p.Y))],
        };

    void SetMode(HangerMode mode)
    {
        if (Mode != mode)
            _modeSeconds = 0;
        Height = (double)mode;
    }

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.Hanger;
        double dt = SimConstants.TickSeconds;
        _modeSeconds += dt;
        if (ctx.Train.Walls is not { } walls)
            return;
        // Let go of (a friend, a blow), or done: it draws back into the roof a while.
        if (Phase is SpinePhase.BreakOff or SpinePhase.Punish && Mode is HangerMode.Grab or HangerMode.Haul or HangerMode.Hold)
        {
            Extra = -1;
            _strand = -1;
            Enter(ctx, SpinePhase.Dormant);
            SetMode(Phase == SpinePhase.Punish ? HangerMode.Retreat : HangerMode.Hit);
        }
        if (Phase == SpinePhase.Grab && Holding >= 0 && _strand >= 0)
        {
            // Hauled up the strand: from where they were caught to haulTo up it over haulSeconds, then held there.
            // (Timed from the grab, the spine's own clock: the modes are only for its clips.)
            double up = Math.Min(1, PhaseSeconds / t.HaulSeconds) * t.HaulTo;
            if (Mode == HangerMode.Grab && PhaseSeconds > 0.3)
                SetMode(HangerMode.Haul);
            if (Mode == HangerMode.Haul && PhaseSeconds >= t.HaulSeconds)
                SetMode(HangerMode.Hold);
            ctx.Lift(Holding, _from + Double3.Up * up);
            return;
        }
        switch (Mode)
        {
            case HangerMode.Hit:
                if (_modeSeconds >= 0.6)
                    SetMode(HangerMode.Retreat);
                return;
            case HangerMode.Retreat:
                if (_modeSeconds >= t.RetreatSeconds)
                    SetMode(HangerMode.Hidden);
                return;
        }
        // Hidden: its strands quiver once anyone's in its house (the telegraph); a walk into one and it has them.
        var living = ctx.LivingCrew().Where(c => !c.Player.State.Has(PlayerFlags.Held)).OrderBy(c => c.Player.Id).ToList();
        bool someone = living.Any(c => walls.InHouse(House, c.World + Double3.Up * 0.5));
        if (!someone)
        {
            Enter(ctx, SpinePhase.Dormant);
            return;
        }
        Enter(ctx, SpinePhase.Telegraph);
        foreach (var c in living)
            for (int i = 0; i < _strands.Count; i++)
                if (HouseWays.Flat(c.World - _strands[i]) <= t.TouchWithin && Enter(ctx, SpinePhase.Commit) && Grab(ctx, c.Player.Id, t.GrabSeconds))
                {
                    Extra = c.Player.Id;
                    _strand = i;
                    _from = _strands[i] with { Y = c.World.Y };
                    SetMode(HangerMode.Grab);
                    return;
                }
    }

    protected override void Punish(EnemyContext ctx, int victim) => Kill(ctx, victim, DeathCause.Taken);
}
