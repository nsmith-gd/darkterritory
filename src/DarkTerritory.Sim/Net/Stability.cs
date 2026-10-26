namespace DarkTerritory.Sim.Net;

/// <summary>Mirror of content/tuning/balance.json <c>stability</c>: GDD §34's desync and crash sweep (note 1032). Field docs live there.</summary>
public sealed record StabilityTuning
{
    public IReadOnlyList<int> Crews { get; init; } = [2, 3, 4, 5, 6, 7, 8];
    public double Seconds { get; init; } = 120;
    public double PerfectMaxCorrectionM { get; init; } = 0.01;
    public double RoughMaxCorrectionM { get; init; } = 2;
    public double RoughCorrectionsPerTicks { get; init; } = 180;
    public double SnapshotShare { get; init; } = 0.9;
}

/// <summary>One crew size on one link: the clients that drifted from the host, or the crash, and the verdict.</summary>
/// <param name="Crash">The exception that ended the night, if one did (§34 "crash detection").</param>
/// <param name="Desynced">Each client past the link's limits, with by how much.</param>
public sealed record StabilityRow(int Crew, string Link, bool Pass, int Clients, double MaxCorrectionM, int Corrections, string? Crash,
    IReadOnlyList<string> Desynced);

public sealed record StabilityReport(IReadOnlyList<StabilityRow> Rows, bool Pass);

/// <summary>
/// GDD §34 "Stability: desync and crash detection across 2–8 clients" (note 1032), judged on harness nights over a quiet
/// line: on a perfect link a client's prediction matches the host's (within a few millimetres, the round-trip-late cab input of
/// <c>NetcodeTests.OnAPerfectLinkPredictionMatchesTheHostExactly</c>); on the rough link it corrects by little and rarely,
/// as the nightly soak holds eight; every client stays connected, hears nearly every snapshot, and the night doesn't throw.
/// `dt harness stability` runs the sweep.
/// </summary>
public static class Stability
{
    public static StabilityRow Judge(int crew, string link, HarnessReport? r, string? crash, StabilityTuning t)
    {
        if (r is null || crash is not null)
            return new StabilityRow(crew, link, false, 0, 0, 0, crash ?? "no report", []);
        bool perfect = link == "perfect";
        double max = perfect ? t.PerfectMaxCorrectionM : t.RoughMaxCorrectionM;
        double corrections = perfect ? double.MaxValue : r.Ticks / t.RoughCorrectionsPerTicks;
        var desynced = new List<string>();
        foreach (var c in r.Clients)
        {
            if (c.MaxCorrectionM >= max)
                desynced.Add($"player {c.Id} corrected by {c.MaxCorrectionM} m (under {max} m)");
            else if (c.Corrections >= Math.Max(1, corrections))
                desynced.Add($"player {c.Id} corrected {c.Corrections} times (under {corrections:0})");
            if (c.Snapshots <= t.SnapshotShare * r.Ticks)
                desynced.Add($"player {c.Id} heard {c.Snapshots} of {r.Ticks} snapshots");
        }
        if (r.Clients.Count != crew)
            desynced.Add($"{r.Clients.Count} of {crew} clients in the crew");
        return new StabilityRow(crew, link, desynced.Count == 0, r.Clients.Count, r.MaxCorrectionM, r.Clients.Sum(c => c.Corrections), null, desynced);
    }

    public static StabilityReport Report(IReadOnlyList<StabilityRow> rows) => new(rows, rows.All(r => r.Pass));
}
