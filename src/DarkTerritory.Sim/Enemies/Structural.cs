using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Enemies;

/// <summary>
/// One of THE CHOIR's small flying ghosts (GDD v1.1 §21, App. A.7): the swarm that comes when the crew's been loud too long
/// (<see cref="Combat.ChoirState"/>). Each seizes anyone outside, on the roofs, or behind no closed door: a grab, broken only
/// by killing the one holding them (many blows, and they hit back hard). With nobody to seize they besiege the shut doors,
/// rattling and banging until the crew's quiet. Quiet held, they disperse; one crew member taken and the Choir's gone for the
/// rest of the run. Rule: hush, and shut every door. Fighting it is possible and almost always a mistake.
/// </summary>
/// <remarks>Loose in the world. <see cref="Enemy.Extra"/> is who it's after (−1: besieging).</remarks>
public sealed class ChoirGhost(int id) : Enemy(id)
{
    public override EnemyKind Kind => EnemyKind.Choir;
    public override PressureZone Zone => PressureZone.Structural;
    public override Sense Sense => Sense.Sound;
    public override Want Want => Want.Kill;
    public override double MeleeRadius => 0.7;
    /// <summary>A ball goes through a ghost (note 290); every round only feeds the Choir's meter.</summary>
    public override bool Exposed => false;
    public override bool Far => true;
    public int? Target => Extra >= 0 ? (int)Extra : null;

    public static ChoirGhost Around(int id, Double3 world, ChoirSwarmV11 t) => new(id) { Attached = Loose, Local = world, Extra = -1, Health = t.Health };

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.Choir;
        var train = ctx.Train;
        switch (Phase)
        {
            case SpinePhase.Dormant:
                Enter(ctx, SpinePhase.Telegraph); // the voices arrive
                return;
            case SpinePhase.Telegraph:
                if (PhaseSeconds >= ctx.Tuning.MinReactionSeconds)
                    Enter(ctx, SpinePhase.Commit);
                Circle(train, t);
                return;
            case SpinePhase.Commit:
                {
                    // SEIZE: a crewmate exposed (outside, on a roof, behind no shut door), not already held: loudest first (GDD
                    // v1.4 App. A.7: "whoever put the most into the meter during the build"), then the nearest.
                    var here = Local;
                    var exposed = ctx.LivingCrew().Where(c => PlayerMotor.Space(c.Player.State, train) == PlayerMotor.Outside && !c.Player.State.Has(PlayerFlags.Held))
                        .OrderByDescending(c => ctx.World.ChoirShare(c.Player.Id)).ThenBy(c => (c.World - here).Length).ThenBy(c => c.Player.Id).ToList();
                    // One seize at a time: it takes one crew member a run (A.7 LIMIT), so while one of the swarm has someone
                    // the rest wheel overhead. (Two ghosts each holding someone was two dead when the first let go.)
                    if (exposed.Count == 0 || ctx.World.ActiveEnemies.Any(e => e is ChoirGhost && e != this && e.Phase == SpinePhase.Grab))
                    {
                        // BESIEGE: at the doors, rattling, until it's quiet (or overhead while another has its one).
                        Extra = -1;
                        Circle(train, t);
                        return;
                    }
                    var prey = exposed[0];
                    Extra = prey.Player.Id;
                    var to = prey.World + Double3.Up * 1.4 - here;
                    double step = (t.FlySpeed + train.Dynamics.Speed) * SimConstants.TickSeconds; // they keep up with the train, and gain
                    if (to.Length > 0.8)
                    {
                        Local = here + to.Normalized * Math.Min(step, to.Length);
                        return;
                    }
                    Grab(ctx, prey.Player.Id, t.SeizeSeconds);
                    return;
                }
            case SpinePhase.Grab:
                if (ctx.Crew.FirstOrDefault(c => c.Player.Id == Holding).Player.State is { Alive: true } held)
                    Local = PlayerMotor.WorldPosition(held, train) + Double3.Up * 1.4;
                return;
            default:
                Enter(ctx, SpinePhase.Gone);
                return;
        }
    }

    /// <summary>Wheeling about over the train, around the engine's rake.</summary>
    void Circle(TrainOnLine train, ChoirSwarmV11 t)
    {
        double a = PhaseSeconds * 0.6 + Id;
        var middle = train.Line.Sample(train.Dynamics.Path, train.Dynamics.Distance - train.Dynamics.Consist.LengthMetres * 0.5).Position;
        var want = middle + new Double3(DMath.Cos(a) * t.Around, 6 + DMath.Sin(a * 1.7) * 1.5, DMath.Sin(a) * t.Around);
        var to = want - Local;
        double step = (t.FlySpeed + train.Dynamics.Speed) * SimConstants.TickSeconds; // they keep up with the train, and gain
        Local = to.Length <= step ? want : Local + to.Normalized * step;
    }

    // The tick it last hit back (note 272), or null.
    uint? _hitBack;

    /// <summary>
    /// They hit back hard: whoever strikes one takes a hit for it. One hit a hitBackEvery however fast it's struck (note 272,
    /// App. F.1's "a few big hits, never chip damage"): a flurry of blows costs a hit, not one per blow.
    /// </summary>
    public override void Struck(EnemyContext ctx, int by, double damage)
    {
        var t = ctx.Tuning.Choir;
        if (_hitBack is not { } last || ctx.Tick - last >= t.HitBackEvery * SimConstants.TickRate - 0.5)
        {
            _hitBack = ctx.Tick;
            ctx.Bite(by, t.HitBackDamage, DeathCause.Choir);
        }
        base.Struck(ctx, by, damage);
    }

    /// <summary>Seized: the Choir's one for the run (the world disperses the rest, and it's spent).</summary>
    protected override void Punish(EnemyContext ctx, int victim)
    {
        Kill(ctx, victim, DeathCause.Seized);
        ctx.World.ChoirTook();
        Enter(ctx, SpinePhase.BreakOff);
        Enter(ctx, SpinePhase.Gone);
    }
}
