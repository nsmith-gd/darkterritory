namespace DarkTerritory.Sim.Enemies;

/// <summary>
/// Why a spawn rule put nothing in the world (the spawn overhaul's issue 3, note 885): the director chose a kind whose gates said
/// yes, and its placement found no way in. A failed attempt commits nothing (<see cref="Director.Commit"/>).
/// </summary>
public enum SpawnFailure : byte
{
    /// <summary>Placed.</summary>
    None,
    /// <summary>The kind has no spawn rule (<see cref="Spawns.For"/> is null).</summary>
    NoRule,
    /// <summary>A gate the placement checks again at the moment of placing said no (the Moose to a train that's moving, a kind switched off).</summary>
    Gated,
    /// <summary>Nobody where it needs someone: no crewmate on the ground, on the roofs, or out on an excursion.</summary>
    NoTarget,
    /// <summary>Nowhere of the train's or the world's it needs: no lit car, no car for it, no crane, no freight, no structure, no spot clear.</summary>
    NoSpot,
    /// <summary>No coupling gap, or none free of the crew.</summary>
    NoGap,
    /// <summary>What lives at the stop has its sites there, and none is near the crew (note 868's Ribbits).</summary>
    NoSite,
}

/// <summary>
/// What a spawn rule's placement did (<see cref="SpawnRule.Spawn"/>): placed, with the ids of every member it put in (a pack is
/// one encounter of several), or failed, with why.
/// </summary>
public readonly record struct SpawnOutcome(SpawnFailure Failure, IReadOnlyList<int> Members)
{
    public bool Placed => Failure == SpawnFailure.None;

    public static SpawnOutcome Failed(SpawnFailure why) => new(why, []);

    public static SpawnOutcome Of(IReadOnlyList<int> members) => new(SpawnFailure.None, members);

    /// <summary>Placed, members unknown: for tools and tests that account without putting anything in the world.</summary>
    public static readonly SpawnOutcome Assumed = new(SpawnFailure.None, []);
}

/// <summary>
/// A spawn the director has chosen and not yet accounted (note 874's shared contract; the brief's issue 3: choose, validate,
/// place, then commit). <see cref="Director.Choose"/> makes it with every draw the choice takes already drawn (the cooldown's
/// included), so a night's dice run in the same order whether it places or not; <see cref="Director.Commit"/> charges the
/// pressure, starts the cooldown, writes the log and counts the night's caps only when its outcome placed.
/// </summary>
/// <param name="Kind">What was chosen.</param>
/// <param name="Channel">The channel it came by.</param>
/// <param name="Tick">The tick it was chosen on.</param>
/// <param name="Distance">The train's front along the line then.</param>
/// <param name="InZone">The engaged threats in its zone then, counting it (the log's <see cref="DirectorSpawn.ActiveInZone"/>).</param>
/// <param name="Engaged">The engaged threats then, counting it.</param>
public sealed record SpawnAttempt(EnemyKind Kind, SpawnChannel Channel, uint Tick, double Distance, int InZone, int Engaged)
{
    /// <summary>Spawned without its situational rule (the free spawn, the pressure's fill): <see cref="SpawnContext.Free"/>.</summary>
    public bool Free { get; init; }
    /// <summary>The pressure's: pressed through the cooldown or over the budget's curve.</summary>
    public bool Paced { get; init; }
    /// <summary>The pressure's: the cooldown drawn when it was chosen, started only if it places.</summary>
    public double Cooldown { get; init; }
    /// <summary>The stops' own: the stop's key (<see cref="Director.StopAt"/>) and whether it's the stop's boarder.</summary>
    public (int Key, bool Boarder)? Stop { get; init; }
    /// <summary>Note 999: the crewmate it was sent at and for what they were doing, when its kind answers that doing.</summary>
    public Aim? Aim { get; init; }
}

/// <summary>An attempt as it went (note 885), for the telemetry (#600's <c>EncounterLog</c>) and the harness: one a decision.</summary>
/// <param name="Members">How many it put in: a pack is one attempt of several.</param>
public readonly record struct SpawnAttemptResult(uint Tick, EnemyKind Kind, SpawnChannel Channel, SpawnFailure Failure, int Members, double Distance)
{
    public bool Placed => Failure == SpawnFailure.None;
}

/// <summary>#603's block of <c>director.encounters</c> (note 885); the rest in #606's and #607's files.</summary>
public sealed partial record EncountersTuning
{
    /// <summary>The spawn transaction's retries (note 885).</summary>
    public SpawnAttemptTuning Attempts { get; init; } = new();
}

/// <summary>
/// What a failed attempt gives back (note 885; the brief §2 issue 3: "if placement fails, record the failure reason and retry
/// appropriately"). The pressure keeps what it would have spent and the cooldown isn't started, so it tries again the next
/// second; the kind that failed sits out <see cref="FailHoldSeconds"/> of the pressure's and the stops' choices, so the
/// director picks something else rather than the same kind into the same wall. A free spawn that fired and failed stays owed
/// for <see cref="FreeRetrySeconds"/>: the next seconds' choices fire on it without a new roll (the roll is still made each
/// second, so the curve's dice are as they were).
/// </summary>
public sealed record SpawnAttemptTuning
{
    public double FailHoldSeconds { get; init; } = 10;
    public double FreeRetrySeconds { get; init; } = 20;
}
