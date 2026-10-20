namespace DarkTerritory.Sim.Enemies;

/// <summary>
/// Player-relevant selection and placement (queue #609, note 895; the director's brief, docs/design/spawn-director-overhaul.md
/// §3 "Encounter selection" 6 and §5): pure functions the director and the spawn rules call, so the choice stays where it was
/// and only its weights move.
/// <list type="bullet">
/// <item><see cref="ZoneSpread"/>: a kind whose zone (the front, the rear, the flanks, the roofs and gaps, inside, outside)
/// already has engaged threats in it weighs down, so the night's threats come as separate problems in separate places (§4
/// level 3: "two spatially separated problems requiring the crew to communicate and reprioritize") rather than a second one
/// where the first already is. The conflict table's pairs (App. B.1) still weigh their combinations up.</item>
/// <item><see cref="Pick"/>: where along the train a rule puts what it sends, among the places its rule allows (a car, a gap,
/// a coupling): away from the threats already on the train (§5: "for longer trains, encourage a spatial distribution of
/// threats rather than repeatedly selecting one car"), and not on top of a crewmate (§5: "spawn placement should not always
/// target the player's exact position"). The places a rule allows are its own; this only weighs among them.</item>
/// </list>
/// Deterministic: <see cref="Pick"/> spends the same single draw the rule's uniform pick did, and with every place weighing
/// the same (nothing on the train, nobody near) it picks exactly what that uniform pick did.
/// </summary>
public static class EncounterPlacement
{
    /// <summary>
    /// The weight × for a kind in a zone with <paramref name="engagedInZone"/> engaged threats already in it:
    /// <see cref="SelectionTuning.ZoneRepeatWeight"/> to that power (1 with the selection off or nothing there).
    /// </summary>
    public static double ZoneSpread(int engagedInZone, SelectionTuning t) =>
        !t.On || engagedInZone <= 0 ? 1 : Ballast.DMath.Pow(t.ZoneRepeatWeight, engagedInZone);

    /// <summary>
    /// Each place's weight (<paramref name="places"/>, <paramref name="threats"/> and <paramref name="crew"/> as distances along
    /// the line, m): × down to <see cref="PlacementTuning.NearThreatWeight"/> for each threat within
    /// <see cref="PlacementTuning.ThreatSpreadM"/> (the nearer, the more), × <see cref="PlacementTuning.OnTopWeight"/> with a
    /// crewmate within <see cref="PlacementTuning.OnTopM"/>.
    /// </summary>
    public static double[] Weights(IReadOnlyList<double> places, IReadOnlyList<double> threats, IReadOnlyList<double> crew, PlacementTuning t)
    {
        var w = new double[places.Count];
        for (int i = 0; i < places.Count; i++)
        {
            double v = 1;
            if (t.On)
            {
                foreach (double th in threats)
                {
                    double d = Math.Abs(places[i] - th);
                    if (d < t.ThreatSpreadM)
                        v *= 1 - (1 - t.NearThreatWeight) * (1 - d / t.ThreatSpreadM);
                }
                foreach (double c in crew)
                    if (Math.Abs(places[i] - c) < t.OnTopM)
                    {
                        v *= t.OnTopWeight;
                        break;
                    }
            }
            w[i] = Math.Max(0, v);
        }
        return w;
    }

    /// <summary>
    /// The place to put it, by <see cref="Weights"/>, from <paramref name="draw"/>: the rule's own draw in [0, n − 1e-9) (as its
    /// uniform pick drew it). With every place weighing the same, the place that draw picked uniformly.
    /// </summary>
    public static int Pick(IReadOnlyList<double> places, IReadOnlyList<double> threats, IReadOnlyList<double> crew, double draw, PlacementTuning t)
    {
        int n = places.Count;
        if (n <= 1)
            return 0;
        var w = Weights(places, threats, crew, t);
        double total = w.Sum();
        if (total <= 0 || w.All(v => v == w[0]))
            return Math.Clamp((int)draw, 0, n - 1);
        double at = Math.Clamp(draw / (n - 1e-9), 0, 1) * total;
        for (int i = 0; i < n; i++)
        {
            if (at < w[i])
                return i;
            at -= w[i];
        }
        // Rounding at the top end: the last place that weighs anything.
        for (int i = n - 1; i >= 0; i--)
            if (w[i] > 0)
                return i;
        return n - 1;
    }
}

/// <summary>The selection's weights (note 895; enemies.json <c>director.encounters.selection</c>; the brief §3 "Encounter selection").</summary>
public sealed record SelectionTuning
{
    public bool On { get; init; } = true;
    /// <summary>× for each engaged threat already in the kind's zone.</summary>
    public double ZoneRepeatWeight { get; init; } = 0.5;
}

/// <summary>Where along the train (note 895; enemies.json <c>director.encounters.placement</c>; the brief §5).</summary>
public sealed record PlacementTuning
{
    public bool On { get; init; } = true;
    /// <summary>A threat on the train within this (m along the line) weighs a place down, the nearer the more.</summary>
    public double ThreatSpreadM { get; init; } = 40;
    /// <summary>× for a place right at a threat (rising to 1 at <see cref="ThreatSpreadM"/>).</summary>
    public double NearThreatWeight { get; init; } = 0.2;
    /// <summary>A crewmate within this (m along the line) of a place is on top of it.</summary>
    public double OnTopM { get; init; } = 6;
    /// <summary>× for a place with a crewmate on top of it.</summary>
    public double OnTopWeight { get; init; } = 0.3;
}

public sealed partial record EncounterTuning
{
    /// <summary>The selection's zone spread (note 895).</summary>
    public SelectionTuning Selection { get; init; } = new();
    /// <summary>Where along the train (note 895).</summary>
    public PlacementTuning Placement { get; init; } = new();
    /// <summary>A halt's own encounter (note 895; GDD D.15 question 3).</summary>
    public HaltTuning Halt { get; init; } = new();
}

/// <summary>
/// A halt's own (note 895; enemies.json <c>director.encounters.halt</c>; GDD D.15 question 3): one encounter while the train
/// stands at an abandoned village's halt, <see cref="LullSeconds"/> after it stopped there. Off until measured (the director's
/// "test creature spawn first").
/// </summary>
public sealed record HaltTuning
{
    public bool On { get; init; }
    public double LullSeconds { get; init; } = 15;
}
