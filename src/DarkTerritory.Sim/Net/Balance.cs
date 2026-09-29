using DarkTerritory.Sim.Route;

namespace DarkTerritory.Sim.Net;

/// <summary>Mirror of content/tuning/balance.json: GDD §34's sweep targets. Field docs live in that file.</summary>
public sealed record BalanceTuning(int SurvivableCrew, double SurvivableDelivered, int NonTrivialCrew, double NonTrivialPunishes)
{
    public const string File = "tuning/balance.json";
}

/// <summary>One night of a sweep: a route, the crew's size, the train's length.</summary>
public sealed record BalanceNight(RouteTier Tier, ulong Seed, int Crew, int Cars);

/// <summary>How a night went, in the terms the sweep is judged by.</summary>
public sealed record BalanceRow(RouteTier Tier, ulong Seed, int Crew, int Cars, string End, bool Delivered, double Net, int CrewLost,
    IReadOnlyDictionary<string, int> DeathsByCause, int Punishes, int FairnessViolations, double Seconds, double DistanceKm);

/// <summary>The nights with one crew size (or train length) together.</summary>
public sealed record BalanceGroup(int Value, int Nights, double DeliveredRate, double MeanNet, double MeanCrewLost, double MeanPunishes);

public sealed record BalanceCheck(string Name, bool Pass, string Detail);

public sealed record BalanceReport(IReadOnlyList<BalanceRow> Nights, IReadOnlyList<BalanceGroup> ByCrew, IReadOnlyList<BalanceGroup> ByCars,
    IReadOnlyList<BalanceCheck> Checks, bool Pass);

/// <summary>
/// GDD §34's balance sweeps over harness nights (roadmap M7 "balance sweeps"): the crew-size sweep ("survivable at 2,
/// non-trivial at 8"), the train-length sweep ("where is the real progression cap?", reported, not judged: that's the
/// director's call), and App. A.1's fairness contract on every night. `dt balance` runs the nights; this judges them.
/// </summary>
public static class Balance
{
    /// <summary>The sweep's grid: every tier and seed, at every crew size and train length.</summary>
    public static IReadOnlyList<BalanceNight> Grid(IEnumerable<RouteTier> tiers, IEnumerable<ulong> seeds, IEnumerable<int> crews, IEnumerable<int> cars) =>
        [.. from t in tiers from s in seeds from c in crews from n in cars select new BalanceNight(t, s, c, n)];

    public static BalanceRow Row(BalanceNight n, HarnessReport r)
    {
        var run = r.Run;
        var threats = r.Threats;
        return new BalanceRow(n.Tier, n.Seed, n.Crew, n.Cars, run?.End.ToString() ?? "none", run?.End == Run.RunEnd.Delivered,
            run?.Net ?? 0, run?.CrewLost ?? r.Deaths, threats?.DeathsByCause ?? new Dictionary<string, int>(),
            threats?.Punishes.Values.Sum() ?? 0, threats?.FairnessViolations ?? 0, r.Seconds, run?.DistanceKm ?? r.TrainDistance / 1000);
    }

    public static BalanceReport Judge(IReadOnlyList<BalanceRow> rows, BalanceTuning t)
    {
        static IReadOnlyList<BalanceGroup> By(IEnumerable<BalanceRow> rows, Func<BalanceRow, int> key) =>
            [.. rows.GroupBy(key).OrderBy(g => g.Key).Select(g => new BalanceGroup(g.Key, g.Count(),
                Math.Round(g.Average(r => r.Delivered ? 1.0 : 0.0), 3), Math.Round(g.Average(r => r.Net)), Math.Round(g.Average(r => (double)r.CrewLost), 2),
                Math.Round(g.Average(r => (double)r.Punishes), 1)))];
        var byCrew = By(rows, r => r.Crew);
        var byCars = By(rows, r => r.Cars);
        var checks = new List<BalanceCheck>();
        if (byCrew.FirstOrDefault(g => g.Value == t.SurvivableCrew) is { } small)
            checks.Add(new BalanceCheck($"survivable at {t.SurvivableCrew}", small.DeliveredRate >= t.SurvivableDelivered,
                $"{small.DeliveredRate:P0} of {small.Nights} nights delivered (at least {t.SurvivableDelivered:P0})"));
        if (byCrew.FirstOrDefault(g => g.Value == t.NonTrivialCrew) is { } large)
            checks.Add(new BalanceCheck($"non-trivial at {t.NonTrivialCrew}", large.MeanPunishes >= t.NonTrivialPunishes,
                $"{large.MeanPunishes} punishes a night (at least {t.NonTrivialPunishes})"));
        int unfair = rows.Sum(r => r.FairnessViolations);
        checks.Add(new BalanceCheck("fair (App. A.1)", unfair == 0, $"{unfair} commits without the reaction window"));
        return new BalanceReport(rows, byCrew, byCars, checks, checks.All(c => c.Pass));
    }
}
