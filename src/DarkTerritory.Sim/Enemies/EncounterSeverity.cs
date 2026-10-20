using Ballast;

namespace DarkTerritory.Sim.Enemies;

/// <summary>
/// What a quiet second on the run is (the director's brief §6; queue #610, note 893). The brief asks the director to tell
/// these apart and to answer only the last:
/// </summary>
public enum QuietKind : byte
{
    /// <summary>A creature is a problem now: some encounter at level 2 or 3 (a creature working on a train system counts, nobody near it).</summary>
    Sustained,
    /// <summary>Intentional: the grace, a crisis's aftermath, the facility lull, a fort, a ban on the line, the final stretch.</summary>
    Recovery,
    /// <summary>Quiet, but something is about near the crew or the train (level 1): anticipation, not nothing.</summary>
    Presence,
    /// <summary>A true drought: nothing about, nothing held back on purpose.</summary>
    Drought,
}

/// <summary>
/// The brief §4's levels of an encounter, read from world state, never from notifications (queue #610, note 893):
/// <list type="bullet">
/// <item>1, ambient presence: a member is about (not resolved) within the telemetry's <c>presenceMetres</c> of the train or a
/// living crewmate. This level rests on a proximity proxy: "near enough to be noticed", never "noticed".</item>
/// <item>2, active harassment: a member is threatening or engaging (#604's <see cref="ThreatState"/>: alert, near someone,
/// working on a train system, or at someone).</item>
/// <item>3, compound crisis: at level 2 while another encounter is at level 2 too, at least <c>compoundApartMetres</c> away:
/// two problems apart, so the crew has to split up or choose.</item>
/// </list>
/// Pure and deterministic: no draws, and the order the encounters come in doesn't change the answer.
/// </summary>
public static class EncounterSeverity
{
    /// <summary>An encounter's level now (0–2) from its living members' states and whether one is near enough to be present.</summary>
    public static int Level(IEnumerable<ThreatState> states, bool present)
    {
        bool about = false;
        foreach (var s in states)
        {
            if (Threats.Counts(s))
                return 2;
            about |= s != ThreatState.Resolved;
        }
        return about && present ? 1 : 0;
    }

    /// <summary>
    /// Which of the harassing encounters (level 2, each at <paramref name="at"/>) are compound: those with another harassing
    /// encounter at least <paramref name="apart"/> metres off (0: any other at all).
    /// </summary>
    public static bool[] Compound(IReadOnlyList<Double3> at, double apart)
    {
        var compound = new bool[at.Count];
        for (int i = 0; i < at.Count; i++)
            for (int j = i + 1; j < at.Count; j++)
                if ((at[i] - at[j]).Length >= apart)
                    compound[i] = compound[j] = true;
        return compound;
    }

    /// <summary>
    /// A second on the run: sustained while anything harasses the crew (an active danger outranks the director's own rest),
    /// else recovery when the director holds for a reason it rests by (<paramref name="resting"/>), else presence when
    /// something is about, else a drought.
    /// </summary>
    public static QuietKind Classify(int highest, bool resting) =>
        highest >= 2 ? QuietKind.Sustained : resting ? QuietKind.Recovery : highest == 1 ? QuietKind.Presence : QuietKind.Drought;

    /// <summary>Whether the director's held reason is one of its rests (the telemetry's <c>recoveryHolds</c>, each a prefix: "banned" covers "banned (grace)").</summary>
    public static bool Resting(string held, IReadOnlyList<string> holds)
    {
        foreach (var h in holds)
            if (held.StartsWith(h, StringComparison.Ordinal))
                return true;
        return false;
    }
}
