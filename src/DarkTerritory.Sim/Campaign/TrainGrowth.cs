using System.Globalization;

namespace DarkTerritory.Sim.Campaign;

/// <summary>
/// Corruption growing on the train (campaign.json <c>growth</c>; GDD Part Eleven q8; ARCHITECTURE §8 note 1039). Look only,
/// off until the director answers note 1026's Q2. Field docs live in that file.
/// </summary>
public sealed record GrowthTuning(bool On = false, double PerBoarder = 0.18, double ToCouplings = 0.6, double ToTender = 0.05,
    double PerNight = 0.04, double Most = 1, double ScrapeCost = 35)
{
    /// <summary>How grown a spot is a night on, with <paramref name="boarders"/>' share of new growth: what's there spreads, never past the most.</summary>
    public double Night(double was, double boarders) => Math.Clamp(was + (was > 0 ? PerNight : 0) + boarders, 0, Most);
}

/// <summary>
/// How grown over each part of the train is (note 1039), 0 clean to 1 black all over: <see cref="Cars"/> by vehicle from the
/// engine (0 the engine, whose growth is in its tender), <see cref="Couplings"/> by the coupling behind each vehicle (0 the
/// engine's to car 1). Kept in the campaign save; a night's world draws it (presentation only, the same on every machine).
/// </summary>
public sealed record TrainGrowth(double[] Cars, double[] Couplings)
{
    public static readonly TrainGrowth None = new([], []);

    public double Car(int vehicle) => vehicle >= 0 && vehicle < Cars.Length ? Cars[vehicle] : 0;
    public double Coupling(int behind) => behind >= 0 && behind < Couplings.Length ? Couplings[behind] : 0;
    public bool Any => Cars.Any(g => g > 0) || Couplings.Any(g => g > 0);

    /// <summary>
    /// A night settled (<paramref name="t"/>): every spot already grown spreads, and where a creature got on
    /// (<paramref name="boarded"/>, by vehicle) it's grown more: under that car, at its two couplings, and a little in the
    /// tender. Kept to <paramref name="vehicles"/> (the engine and the cars still on it).
    /// </summary>
    public TrainGrowth Night(GrowthTuning t, IReadOnlyList<int> boarded, int vehicles)
    {
        int Boarded(int v) => v >= 0 && v < boarded.Count ? boarded[v] : 0;
        int all = boarded.Sum();
        var cars = new double[vehicles];
        for (int v = 0; v < vehicles; v++)
            cars[v] = t.Night(Car(v), t.PerBoarder * Boarded(v) + (v == 0 ? t.ToTender * t.PerBoarder * all : 0));
        var couplings = new double[Math.Max(0, vehicles - 1)];
        for (int c = 0; c < couplings.Length; c++)
            couplings[c] = t.Night(Coupling(c), t.ToCouplings * t.PerBoarder * (Boarded(c) + Boarded(c + 1)));
        return new(cars, couplings);
    }

    /// <summary>
    /// A developer's look at it (note 1039; <c>dt screenshot --growth</c>, the dev app's <c>--growth</c>): a number grows the
    /// whole train that much (0..1) over <paramref name="vehicles"/>; otherwise a <see cref="Spec"/>.
    /// </summary>
    public static TrainGrowth? Dev(string arg, int vehicles) =>
        double.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out double g)
            ? new(Enumerable.Repeat(Math.Clamp(g, 0, 1), vehicles).ToArray(), Enumerable.Repeat(Math.Clamp(g, 0, 1), Math.Max(0, vehicles - 1)).ToArray())
            : Parse(arg);

    /// <summary>How it rides to every machine (the night's setup): percent, the cars then the couplings, <c>0,40,12/20,0</c>.</summary>
    public string Spec => string.Join(',', Cars.Select(Pct)) + "/" + string.Join(',', Couplings.Select(Pct));

    static string Pct(double g) => ((int)Math.Round(g * 100)).ToString(CultureInfo.InvariantCulture);

    /// <summary>A <see cref="Spec"/> read back (null for none or what doesn't read as one).</summary>
    public static TrainGrowth? Parse(string? spec)
    {
        if (spec is not { Length: > 0 } || spec.Split('/') is not [var cars, var couplings])
            return null;
        static double[]? Read(string s)
        {
            if (s.Length == 0)
                return [];
            var parts = s.Split(',');
            var all = new double[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                if (int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int p) && p >= 0)
                    all[i] = Math.Min(100, p) / 100.0;
                else
                    return null;
            return all;
        }
        return Read(cars) is { } c && Read(couplings) is { } k ? new(c, k) : null;
    }
}
