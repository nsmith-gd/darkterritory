using Ballast;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Enemies;

/// <summary>
/// How far an enemy is into the crew's night (the director overhaul's B2, queue #604, note 881; the director's brief,
/// docs/design/spawn-director-overhaul.md §2 issue 4): read from what it is doing in the Sim, never from distance alone.
/// The engaged cap and the census count <see cref="Threatening"/> and <see cref="Engaging"/> (<see cref="Threats.Counts"/>):
/// a creature lying in wait, or one pacing a car nobody is near, is there, but it isn't the crew's problem yet.
/// </summary>
public enum ThreatState : byte
{
    /// <summary>Lying in wait: a Dragger under a car's edge, a Moose grazing, a Householder asleep in its house, a Brakeman hidden under the train.</summary>
    Dormant,
    /// <summary>About and on the move, with nobody near and nothing of the train's in its teeth: a Climber pacing a car the crew isn't in.</summary>
    Ambient,
    /// <summary>
    /// After the crew, or at the train: alert to someone, near a crewmate, or doing harm to a train system with nobody near
    /// (<see cref="Enemy.Working"/>: a Hotbox in a truck, a pack feeding aboard, a Knotter in a coupling, a Brakeman at the brakes).
    /// </summary>
    Threatening,
    /// <summary>At someone now: telegraphing, committing, holding someone, punishing (<see cref="Director.Confronting"/>).</summary>
    Engaging,
    /// <summary>Done with: gone, breaking off, peeled away (a Gannet away, a Beetle off its load, Tower Jaw driven off).</summary>
    Resolved,
}

/// <summary>Note 881: the engagement states' numbers (enemies.json <c>director.threats</c>; the brief §2 issue 4).</summary>
public sealed record ThreatTuning
{
    /// <summary>
    /// The engaged cap counts threats (<see cref="ThreatState.Threatening"/>, <see cref="ThreatState.Engaging"/>), not
    /// bodies; false: everything <see cref="Director.Engaged"/> holds the caps, as before note 881.
    /// </summary>
    public bool On { get; init; } = true;
    /// <summary>An ambient creature with a living crewmate this near (m) is threatening: it's where someone is.</summary>
    public double NearMetres { get; init; } = 20;
    /// <summary>
    /// Bodies the director may have about beyond the engaged cap (ambient ones, off the cap): the cap plus this many in all,
    /// so what's pacing cars nobody's in can't pile up into an unfair wave when the crew walks into it.
    /// </summary>
    public int AmbientRoom { get; init; } = 1;
}

/// <summary>The engagement states (note 881): one enemy's, and what the caps count.</summary>
public static class Threats
{
    /// <summary>What the caps count: a threat now, not a body that's merely about.</summary>
    public static bool Counts(ThreatState s) => s is ThreatState.Threatening or ThreatState.Engaging;

    /// <summary>
    /// A creature that hunts the crew at all. Hazards (the track's debris, the marsh, Dave, Jacob, the Wakers), the Mourners
    /// (note 362: they come for the dead) and the Pickers (note 592: the stop's own, after its loot) are about in the night,
    /// but never on the caps.
    /// </summary>
    public static bool Hunts(Enemy e) => !e.Hazard && e.Kind is not (EnemyKind.Mourners or EnemyKind.Pickers);

    /// <summary>The kinds that lie in wait when dormant (<see cref="Director.Engaged"/>'s list): dormant, they're not on the move.</summary>
    public static bool LiesInWait(EnemyKind kind) => kind is EnemyKind.Dragger or EnemyKind.Whistler or EnemyKind.CarHugger or EnemyKind.Gaunt
        or EnemyKind.TippyToesie or EnemyKind.Moose or EnemyKind.Lodger or EnemyKind.Householder or EnemyKind.Hanger or EnemyKind.HollowHouse;

    /// <summary><paramref name="e"/>'s state, the living crew's positions read from <paramref name="world"/>.</summary>
    public static ThreatState Of(Enemy e, World world, ThreatTuning t) => Of(e, world, LivingAt(world), t);

    /// <summary>Where each living crewmate stands (world), by id: what <see cref="Of(Enemy, World, IReadOnlyList{Double3}, ThreatTuning)"/> reads.</summary>
    public static List<Double3> LivingAt(World world) =>
        [.. world.CrewThisTick.Where(c => c.State.Alive).OrderBy(c => c.Id).Select(c => PlayerMotor.WorldPosition(c.State, world.Train))];

    /// <summary>
    /// <paramref name="e"/>'s state: resolved if it's done with; engaging if it's at someone; threatening if it's alert, doing
    /// the train harm, or on the move near a living crewmate (<paramref name="crewAt"/>); dormant if it lies in wait; else
    /// ambient. Host-side and deterministic: it reads only the enemy's own state, the train's and the crew's.
    /// </summary>
    public static ThreatState Of(Enemy e, World world, IReadOnlyList<Double3> crewAt, ThreatTuning t)
    {
        if (e.Gone || e.Phase == SpinePhase.BreakOff || Away(e))
            return ThreatState.Resolved;
        // A fire in a car (App. C.5) is the train's harm, someone in it or not: its Telegraph is its smoke, not a lunge.
        if (e is Incident)
            return ThreatState.Threatening;
        if (e.Holding >= 0 || Director.Confronting(e))
            return ThreatState.Engaging;
        if (!Hunts(e))
            return ThreatState.Ambient;
        if (e.Working(world))
            return ThreatState.Threatening;
        if (e is Brakeman { Mode: BrakemanMode.Hidden } || e.Phase == SpinePhase.Dormant && LiesInWait(e.Kind))
            return ThreatState.Dormant;
        if (e.Phase == SpinePhase.Alert)
            return ThreatState.Threatening;
        var at = e.WorldPosition(world.Train);
        foreach (var c in crewAt)
            if ((c - at).Length <= t.NearMetres)
                return ThreatState.Threatening;
        return ThreatState.Ambient;
    }

    /// <summary>Peeled off and away: a Gannet gone off over the trees (note 340), a Beetle driven off, Tower Jaw driven off or its structure down.</summary>
    static bool Away(Enemy e) => e is Gannet { Mode: GannetMode.Away } or FreightBeetle { Mode: BeetleMode.Away }
        or TowerJaw { Mode: TowerJawMode.Away or TowerJawMode.Wreck };
}
