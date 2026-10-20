namespace DarkTerritory.Sim.Enemies;

/// <summary>
/// Concurrency and overlap by crew and train length (the director's brief §1 B–C, §6; plan note 874; note 906, queue #611):
/// how many threats the crew may have on them at once, and which overlaps a crew can answer at all. All of it grows
/// gradually with the crew alive and the cars, with no breakpoint at eight cars or six crew, so four and eight crews meet
/// overlapping threats across the train and a long train holds more; and a crew of one is never set a dilemma only two can
/// answer. Pure statics on counts: nothing drawn, no world read, the same on the host and every client. The director reads
/// them with <see cref="ConcurrencyTuning.On"/> (note 906).
/// </summary>
public static class Concurrency
{
    /// <summary>
    /// The threats each living crewmate may have on the crew at once with a train of <paramref name="cars"/>: <c>share</c>,
    /// rising by <c>sharePerCar</c> a car past <c>carsFrom</c>, to at most <c>shareMax</c>.
    /// </summary>
    public static double Share(int cars, ConcurrencyTuning t) =>
        Math.Min(Math.Max(t.Share, t.ShareMax), t.Share + Math.Max(0, t.SharePerCar) * Math.Max(0, cars - t.CarsFrom));

    /// <summary>
    /// The engaged cap (App. B.1's hard caps, note 336): the most threats (#604's threatening and engaging) on the crew at
    /// once, <paramref name="living"/> × <see cref="Share"/> rounded half up, at least 1 while anyone's alive and at most
    /// <c>max</c>; none with nobody alive. The living, not the crew the night began with: a crew that's lost people meets as
    /// many at once as the smaller crew it is.
    /// </summary>
    public static int EngagedCap(int living, int cars, ConcurrencyTuning t) =>
        living <= 0 ? 0 : Math.Clamp(HalfUp(living * Share(cars, t)), 1, Math.Max(1, t.Max));

    /// <summary>
    /// The most threats on any one crewmate at once (the census's perTarget): <see cref="Share"/> rounded half up, 1 to
    /// <c>perCrewmateMax</c>: two each from about seven cars (0.75 + 0.2 × 4 = 1.55), as note 647's long train.
    /// </summary>
    public static int PerCrewmate(int cars, ConcurrencyTuning t) =>
        Math.Clamp(HalfUp(Share(cars, t)), 1, Math.Max(1, t.PerCrewmateMax));

    /// <summary>
    /// Bodies about off the cap (#604's ambient room): <c>ambientRoom</c>, and <c>ambientPerCar</c> more a car past
    /// <c>carsFrom</c>, rounded down: a long train has more of its length for something to be pacing where nobody is.
    /// </summary>
    public static int AmbientRoom(int cars, ConcurrencyTuning t) =>
        Math.Max(0, t.AmbientRoom + (int)Math.Floor(Math.Max(0, t.AmbientPerCar) * Math.Max(0, cars - t.CarsFrom) + 1e-9));

    /// <summary>
    /// The conflict pairs a night seeds (App. B.1 "contradiction seeding"): the tier's (<paramref name="tierPairs"/>,
    /// pairsPerRun) × (1 + <c>pairsPerCrew</c> a living crewmate over one), rounded half up: a bigger crew has more hands to
    /// split, so more nights set it two problems at once.
    /// </summary>
    public static int PairsWanted(int tierPairs, int living, ConcurrencyTuning t) =>
        tierPairs <= 0 ? 0 : Math.Max(0, HalfUp(tierPairs * (1 + Math.Max(0, t.PairsPerCrew) * (Math.Max(1, living) - 1))));

    /// <summary>
    /// Whether the conflict pair <paramref name="pair"/> ("a+b", either order) may be made for <paramref name="living"/>: a
    /// pair whose two answers need two people at once (<c>pairMinCrew</c>: run fast for the hounds vs. stop for the Hotbox) is
    /// never made for fewer. A pair not named needs one.
    /// </summary>
    public static bool PairAllowed(string pair, int living, ConcurrencyTuning t) => living >= PairMinCrew(pair, t);

    /// <summary>The crew <paramref name="pair"/> needs (<c>pairMinCrew</c>, named in either order; 1 if not named).</summary>
    public static int PairMinCrew(string pair, ConcurrencyTuning t)
    {
        if (t.PairMinCrew.TryGetValue(pair, out int n))
            return n;
        int plus = pair.IndexOf('+');
        return plus > 0 && t.PairMinCrew.TryGetValue($"{pair[(plus + 1)..]}+{pair[..plus]}", out n) ? n : 1;
    }

    /// <summary>
    /// The relief when crew are lost (§1 B, §6: "difficulty relief when significant crew members are lost"): (alive ÷ the
    /// crew in the night)^<c>reliefPower</c> × (1 − <c>hurtRelief</c> × the share of the living badly hurt), 0..1, a factor on
    /// the encounter rate. The rate already counts only the living (#606); this is the further mercy for a crew that's losing.
    /// </summary>
    public static double Relief(int alive, int joined, int hurt, ConcurrencyTuning t)
    {
        if (alive <= 0)
            return 0;
        double share = Math.Clamp((double)alive / Math.Max(alive, joined), 0, 1);
        double hurtShare = Math.Clamp((double)hurt / alive, 0, 1);
        return Math.Clamp(Ballast.DMath.Pow(share, Math.Max(0, t.ReliefPower)) * (1 - Math.Clamp(t.HurtRelief, 0, 1) * hurtShare), 0, 1);
    }

    /// <summary>
    /// Whether a threat holds its place on the engaged cap (note 906; the brief §2 issue 4, §6: "an unseen monster several cars
    /// away should not indefinitely suppress interesting encounters for a solo player"): one holding someone always does; one
    /// that's had no living crewmate within <c>farMetres</c> for <paramref name="farSeconds"/> of <c>farHoldSeconds</c> or more
    /// doesn't (it still counts as danger for the pacing: it's only off the cap). One at work on the train (<paramref
    /// name="working"/>: a pack feeding aboard, a Hotbox in its truck, a Brakeman at the brakes) holds it however far it is
    /// (§6: "an active monster damaging a train system should count as ongoing danger"), but for the kinds in
    /// <c>farWorkers</c> (<paramref name="kind"/>), whose work is a state the train is left in, not a harm that grows: the
    /// Knotter that's become the coupling. <c>farHoldSeconds</c> 0: always holds.
    /// </summary>
    public static bool HoldsCap(bool holding, bool working, string kind, double farSeconds, ConcurrencyTuning t) =>
        holding || t.FarHoldSeconds <= 0 || farSeconds < t.FarHoldSeconds || working && !t.FarWorkers.Contains(kind);

    static int HalfUp(double x) => (int)Math.Floor(x + 0.5 + 1e-9);
}

// The concurrency numbers (note 906) in director.encounters beside the rate (#606) and the debt (#607).
public sealed partial record EncountersTuning
{
    public ConcurrencyTuning Concurrency { get; init; } = new();
}

/// <summary>Mirror of enemies.json <c>director.encounters.concurrency</c> (note 906); field docs live there.</summary>
public sealed record ConcurrencyTuning
{
    public bool On { get; init; } = true;
    public double Share { get; init; } = 0.75;
    public double SharePerCar { get; init; } = 0.2;
    public int CarsFrom { get; init; } = 3;
    public double ShareMax { get; init; } = 2;
    public int Max { get; init; } = 12;
    public int PerCrewmateMax { get; init; } = 2;
    public int AmbientRoom { get; init; } = 1;
    public double AmbientPerCar { get; init; } = 0.1;
    public double PairsPerCrew { get; init; } = 0.25;
    public Dictionary<string, int> PairMinCrew { get; init; } = new();
    public double ReliefPower { get; init; } = 1;
    public double HurtBelow { get; init; } = 35;
    public double HurtRelief { get; init; } = 0.25;
    public double FarMetres { get; init; } = 30;
    public double FarHoldSeconds { get; init; } = 60;
    public bool IncidentsHoldCap { get; init; } = true;
    public string[] FarWorkers { get; init; } = [];
}
