namespace DarkTerritory.Sim.Enemies;

/// <summary>
/// The one pacing model's clock (the spawn director overhaul's brief §3; note 874's #608; note 908): encounters the night
/// owes the crew, at the encounter rate λ (<see cref="EncounterPacing.Rate"/>), and what it couldn't place, remembered as
/// bounded debt (<see cref="EncounterDebt"/>).
/// <list type="bullet">
/// <item>Every director second the night isn't held for a quiet it means, <see cref="Due"/> gains λ a second; held (a safe
/// zone, the grace, recovery, the terminus, the caps full) it gains nothing, and the debt's release is held too.</item>
/// <item>Every encounter placed, by any channel (the pressure's spend, its fill, the stops' own, a free spawn, a special), pays
/// one off (<see cref="Paid"/>): a pack is one. So the channels share one account, and none adds to another's rate.</item>
/// <item>Past <see cref="Threshold"/> the free channel sends one (the night's guarantee: with nothing else placed, the crew
/// meet λ's encounters, not repeated rolls that may never land). The threshold is drawn afresh about 1 at each payment, so
/// the intervals aren't fixed.</item>
/// <item><see cref="Due"/> holds at most <see cref="DriveTuning.DueCap"/>; what λ would add past it, the director wanting an
/// encounter and finding none it could place, goes to the debt by why, which gives it back at its release rate once there's
/// room and headroom under the caps.</item>
/// </list>
/// A value, pure and deterministic: the director keeps one and steps it once a second; it draws nothing (the threshold is
/// passed in).
/// </summary>
/// <param name="Progress">How far into the night, never going back (<see cref="EncounterPacing.Progress"/>).</param>
/// <param name="Due">Encounters owed now, <see cref="DriveTuning.DueFloor"/>..<see cref="DriveTuning.DueCap"/>.</param>
/// <param name="Threshold">Where the free channel sends the next one.</param>
/// <param name="Debt">What went unmet, remembered.</param>
public readonly record struct EncounterClock(double Progress, double Due, double Threshold, EncounterDebt Debt)
{
    /// <summary>A night's clock at the gate: nothing owed, the first threshold at 1.</summary>
    public static EncounterClock Start => new(0, 0, 1, default);

    /// <summary>The free channel's next is due.</summary>
    public bool Ready => Due >= Threshold;

    /// <summary>
    /// One director second at <paramref name="perSecond"/> encounters a second (λ / 60). <paramref name="held"/>: the quiet the
    /// game means that holds the director this second (an <see cref="EncounterDebt.Intended"/> cause), or null. What λ adds
    /// past the cap is owed for <paramref name="unmet"/>; the debt is repaid into the room under the cap, at most
    /// <paramref name="headroom"/> (the engaged caps' room).
    /// </summary>
    public EncounterClock Second(double perSecond, DebtCause? held, DebtCause unmet, double headroom, EncountersTuning t)
    {
        var debt = Debt.Decay(1, t.Debt);
        if (held is { } why && EncounterDebt.Intended(why))
            return this with { Debt = debt.Hold() };
        double cap = Math.Max(t.Drive.DueCap, 0);
        double due = Due + Math.Max(0, perSecond);
        if (due > cap)
        {
            debt = debt.Owe(due - cap, unmet, t.Debt);
            due = cap;
        }
        else
        {
            debt = debt.Take(Math.Min(cap - due, Math.Max(0, headroom)), t.Debt, out double taken);
            due += taken;
        }
        return this with { Due = due, Debt = debt };
    }

    /// <summary>An encounter placed (any channel): one paid off, to no lower than the floor, and the next threshold set.</summary>
    public EncounterClock Paid(double nextThreshold, EncountersTuning t) =>
        this with { Due = Math.Max(Math.Min(t.Drive.DueFloor, 0), Due - 1), Threshold = nextThreshold };

    /// <summary>The next threshold for a draw <paramref name="u"/> in [0, 1): 1 ± <see cref="DriveTuning.Jitter"/>.</summary>
    public static double NextThreshold(double u, EncountersTuning t)
    {
        double j = Math.Clamp(t.Drive.Jitter, 0, 0.9);
        return 1 - j + 2 * j * u;
    }
}

// The one pacing model's switches (#608, note 908): enemies.json director.encounters.drive. Mirror of it; field docs there.
public sealed partial record EncountersTuning
{
    /// <summary>λ and the debt driving the director's channels (#608, note 908): enemies.json <c>director.encounters.drive</c>.</summary>
    public DriveTuning Drive { get; init; } = new();
}

/// <summary>The one pacing model's numbers (note 908). Mirror of enemies.json <c>director.encounters.drive</c>; field docs live there.</summary>
public sealed record DriveTuning
{
    public bool On { get; init; } = true;
    public double DueCap { get; init; } = 1.5;
    public double DueFloor { get; init; } = -0.25;
    public double Jitter { get; init; } = 0.35;
    public bool PressureByLambda { get; init; } = true;
    public double? PressureGate { get; init; }
}
