namespace DarkTerritory.Sim.Enemies;

/// <summary>
/// Why an encounter the director wanted didn't come (the spawn director overhaul's brief §3 "Encounter debt"; note 876). Only
/// the director's own failures, a kind that couldn't be placed or none that fit, leave a debt; the rest are intended quiet,
/// and owe nothing, whatever the tuning says (§1.A, §6: safe zones, the departure grace, post-crisis recovery and the
/// terminus are kept; a full engagement cap is danger already going on).
/// </summary>
public enum DebtCause
{
    /// <summary>A kind was picked but its placement found nowhere legal to put it (#603's failed attempt).</summary>
    NoPlacement,
    /// <summary>Nothing about tonight was eligible here and now (its rules, its night's cap, its chance).</summary>
    Ineligible,
    /// <summary>The train in a safe zone: the yard, a town's walls. Owes nothing.</summary>
    SafeZone,
    /// <summary>The departure grace at the start of a night. Owes nothing.</summary>
    Grace,
    /// <summary>The aftermath of a crisis, a facility's lull: intended recovery. Owes nothing.</summary>
    Recovery,
    /// <summary>The terminus's no-spawn approach. Owes nothing.</summary>
    Terminus,
    /// <summary>The engagement caps full: the crew already have their hands full. Owes nothing.</summary>
    Capped,
}

/// <summary>
/// Bounded encounter debt (the brief §3; note 874's contract for #607; note 876): part of the encounter rate the director
/// couldn't place, remembered, so that a temporarily ineligible spawn doesn't silently erase the night's threat progression.
/// <list type="bullet">
/// <item><see cref="Owe"/> remembers a share of an unmet opportunity, by its <see cref="DebtCause"/>, up to a cap;</item>
/// <item><see cref="Decay"/> forgets it over time and refills the release allowance;</item>
/// <item><see cref="Hold"/> empties the allowance while the director is held (a safe zone, the grace, recovery), so a backlog
/// comes back at the release rate once it's let go, never as a burst;</item>
/// <item><see cref="Take"/> repays what the allowance and the caller's headroom (the engagement caps) allow.</item>
/// </list>
/// A value, pure and deterministic: plain arithmetic, no draws, no clock, the tuning passed in. The caller (#608) adds what
/// <see cref="Take"/> gives to its own encounter opportunities; this decides nothing about what comes or where.
/// </summary>
/// <param name="Owed">Encounters owed, 0..<see cref="EncounterDebtTuning.Cap"/>.</param>
/// <param name="Allowance">Encounters that may be repaid now, 0..<see cref="EncounterDebtTuning.ReleaseBurst"/>.</param>
public readonly record struct EncounterDebt(double Owed, double Allowance)
{
    /// <summary>Whether <paramref name="cause"/> is a quiet the game means, which never leaves a debt.</summary>
    public static bool Intended(DebtCause cause) => cause is not (DebtCause.NoPlacement or DebtCause.Ineligible);

    /// <summary>
    /// <paramref name="unmet"/> encounter opportunities (≥ 0, fractions allowed) not met for <paramref name="cause"/>: their
    /// share by cause is owed, to at most the cap. An intended quiet owes nothing.
    /// </summary>
    public EncounterDebt Owe(double unmet, DebtCause cause, EncounterDebtTuning t)
    {
        if (!t.On || Intended(cause) || !(unmet > 0)) return this;
        double share = t.Share.TryGetValue(Key(cause), out double s) ? Math.Clamp(s, 0, 1) : 0;
        return this with { Owed = Math.Min(Math.Max(t.Cap, 0), Owed + unmet * share) };
    }

    /// <summary>
    /// <paramref name="dt"/> seconds on: the owed decays (a mean life of <see cref="EncounterDebtTuning.MeanLifeSeconds"/>,
    /// stepped linearly so it's the same arithmetic on every machine) and the allowance refills at the release rate.
    /// </summary>
    public EncounterDebt Decay(double dt, EncounterDebtTuning t)
    {
        if (!(dt > 0)) return this;
        double owed = t.MeanLifeSeconds > 0 ? Owed * Math.Max(0, 1 - dt / t.MeanLifeSeconds) : Owed;
        double allowance = Math.Min(Math.Max(t.ReleaseBurst, 0), Allowance + Math.Max(t.ReleasePerMinute, 0) * dt / 60);
        return new(owed, allowance);
    }

    /// <summary>
    /// The director held (a safe zone, the grace, recovery): nothing may be repaid until the allowance refills from empty
    /// at the release rate after it's let go. What's owed stays, and keeps decaying.
    /// </summary>
    public EncounterDebt Hold() => this with { Allowance = 0 };

    /// <summary>
    /// Repays up to <paramref name="allowed"/> encounters (the caller's headroom under the engagement caps, ≥ 0): at most
    /// what's owed and what the allowance holds. <paramref name="taken"/> is what was repaid, for the caller to add to its
    /// opportunities.
    /// </summary>
    public EncounterDebt Take(double allowed, EncounterDebtTuning t, out double taken)
    {
        taken = t.On && allowed > 0 ? Math.Min(allowed, Math.Min(Owed, Allowance)) : 0;
        if (taken <= 0)
        {
            taken = 0;
            return this;
        }
        return new(Owed - taken, Allowance - taken);
    }

    /// <summary>The cause's key in <see cref="EncounterDebtTuning.Share"/>: its name, camelCase as the JSON has it.</summary>
    public static string Key(DebtCause cause)
    {
        string name = cause.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}

/// <summary>
/// The encounter pacing's tuning (the brief §3; note 874): enemies.json <c>director.encounters</c>. Partial, so each overhaul
/// item adds its own block in its own file (#606 the rate, #607 the debt) without the two touching. Field docs live in the JSON.
/// </summary>
public sealed partial record EncountersTuning
{
    /// <summary>Bounded encounter debt (#607, note 876): enemies.json <c>director.encounters.debt</c>.</summary>
    public EncounterDebtTuning Debt { get; init; } = new();
}

/// <summary>
/// The encounter debt's numbers (the brief §3; note 876). Mirror of enemies.json <c>director.encounters.debt</c>; field docs
/// live there.
/// </summary>
public sealed record EncounterDebtTuning
{
    public bool On { get; init; } = true;
    /// <summary>The share of an unmet opportunity owed, by <see cref="EncounterDebt.Key"/> of its cause; a cause not named, none.</summary>
    public Dictionary<string, double> Share { get; init; } = new() { ["noPlacement"] = 0.6, ["ineligible"] = 0.4 };
    public double Cap { get; init; } = 2;
    public double MeanLifeSeconds { get; init; } = 240;
    public double ReleasePerMinute { get; init; } = 0.5;
    public double ReleaseBurst { get; init; } = 1;
}
