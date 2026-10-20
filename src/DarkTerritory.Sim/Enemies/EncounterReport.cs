namespace DarkTerritory.Sim.Enemies;

/// <summary>
/// The <c>encounters</c> block of <c>dt harness</c> and <c>dt playthrough</c> (the director's brief §8; queue #600, note 880),
/// from a night's <see cref="EncounterLog"/>, and what <c>dt director sweep</c> (#601) reads. Counts are encounters (a pack is
/// one), not bodies; <see cref="Inferred"/> holds the perception proxy apart from the measured facts.
/// </summary>
/// <param name="Attempts">The director's spawn attempts by channel: tried, placed, and charged with nothing placed (issue 3).</param>
/// <param name="Failures">Failed attempts by reason.</param>
/// <param name="Count">Encounters, every channel; <paramref name="Meaningful"/> those that harassed the crew or the train (level 2+, note 893).</param>
/// <param name="Bodies">Creatures put in, the packs' every member.</param>
/// <param name="ByQuarter">Encounters, then meaningful ones, in each quarter of the run's progress (§7: the latter half busier).</param>
/// <param name="FirstSeconds">The first encounter's run seconds, the first second one harassed the crew, and the first compound one (−1 for none).</param>
/// <param name="Droughts">Stretches out on the run of at least the tuning's droughtSeconds with nothing confronting the crew.</param>
/// <param name="Concurrent">Seconds out on the run with 0, 1, 2, 3 and 4+ encounters active at once.</param>
/// <param name="Suppressed">Seconds out on the run the director held, by its reason ("sent" the seconds it didn't).</param>
/// <param name="Severity">Encounters by the highest level each reached (note 893: 0 unnoticed, 1 presence, 2 harassment, 3 compound).</param>
/// <param name="Quiet">Seconds out on the run by what each was (§6): sustained danger, the director's rest, presence, a true drought.</param>
/// <param name="TrueDroughts">Unbroken true droughts of at least droughtSeconds: what the director is to answer.</param>
public sealed record EncounterReport(
    IReadOnlyDictionary<string, ChannelAttempts> Attempts,
    IReadOnlyDictionary<string, int> Failures,
    int Count, int Meaningful, int Bodies, int Kinds,
    IReadOnlyDictionary<string, int> ByKind,
    IReadOnlyDictionary<string, int> ByChannel,
    IReadOnlyDictionary<string, int> ByWhere,
    IReadOnlyDictionary<string, int> ByPart,
    int[][] ByQuarter,
    double[] FirstSeconds,
    EncounterEnds Ends,
    IReadOnlyList<DroughtReport> Droughts, double LongestDrought,
    int[] Concurrent, int PeakActive, int PeakBodies,
    IReadOnlyDictionary<string, int> Suppressed,
    IReadOnlyDictionary<string, int> Severity,
    IReadOnlyDictionary<string, int> Quiet,
    IReadOnlyList<DroughtReport> TrueDroughts, double LongestTrueDrought,
    double DebtPeak,
    InferredReport Inferred,
    IReadOnlyList<EncounterLine> List)
{
    /// <summary>
    /// The block from <paramref name="log"/> as the night stands: a drought still running is closed at its last second
    /// (<see cref="EncounterLog.Finish"/>).
    /// </summary>
    public static EncounterReport Of(EncounterLog log, World world)
    {
        log.Finish(world);
        var all = log.Encounters;
        static double R(double x) => Math.Round(x, 1);
        static SortedDictionary<string, int> Tally(IEnumerable<string> keys) =>
            new(keys.GroupBy(k => k).ToDictionary(g => g.Key, g => g.Count()), StringComparer.Ordinal);
        int Quarter(double p) => Math.Clamp((int)(p * 4), 0, 3);
        var quarters = new int[2][] { new int[4], new int[4] };
        foreach (var e in all)
        {
            quarters[0][Quarter(e.Progress)]++;
            if (e.Meaningful)
                quarters[1][Quarter(e.Progress)]++;
        }
        var meaningful = all.Where(e => e.Meaningful).ToList();
        return new EncounterReport(
            new SortedDictionary<string, ChannelAttempts>(log.Attempts.GroupBy(a => Name(a.Channel)).ToDictionary(g => g.Key,
                g => new ChannelAttempts(g.Count(), g.Count(a => a.Placed), g.Count(a => !a.Placed && a.Charged))), StringComparer.Ordinal),
            Tally(log.Attempts.Where(a => !a.Placed).Select(a => a.Failure ?? "")),
            all.Count, meaningful.Count, all.Sum(e => e.Members.Count), all.Select(e => e.Kind).Distinct().Count(),
            Tally(all.Select(e => e.Kind.ToString())),
            Tally(all.Select(e => Name(e.Channel))),
            Tally(all.Select(e => e.Where)),
            Tally(all.Where(e => e.Part != "").Select(e => e.Part)),
            quarters,
            [all.Count > 0 ? R(all[0].Seconds) : -1, meaningful.Count > 0 ? R(meaningful.Min(e => e.HarassedAt)) : -1,
                all.Where(e => e.CompoundAt >= 0).Select(e => R(e.CompoundAt)).DefaultIfEmpty(-1).Min()],
            new EncounterEnds(all.Count(e => e.EndedAt >= 0 && e.End == EncounterEnd.Killed), all.Count(e => e.EndedAt >= 0 && e.End == EncounterEnd.Gone),
                all.Count(e => e.EndedAt < 0), R(all.Sum(e => e.EngagedSeconds)), R(all.Sum(e => e.DormantSeconds)),
                all.Sum(e => e.Grabs), all.Sum(e => e.Punishes)),
            [.. log.Droughts.Select(d => new DroughtReport(R(d.From), R(d.To), R(d.Seconds), Math.Round(d.Progress, 2), d.Held))],
            R(log.Droughts.Select(d => d.Seconds).DefaultIfEmpty(0).Max()),
            [.. log.Concurrent], log.PeakActive, log.PeakBodies,
            new SortedDictionary<string, int>(log.Suppressed, StringComparer.Ordinal),
            Tally(all.Select(e => e.Severity.ToString(System.Globalization.CultureInfo.InvariantCulture))),
            new SortedDictionary<string, int>(Enum.GetValues<QuietKind>().ToDictionary(q => q.ToString().ToLowerInvariant(), q => log.QuietSeconds[(int)q]), StringComparer.Ordinal),
            [.. log.TrueDroughts.Select(d => new DroughtReport(R(d.From), R(d.To), R(d.Seconds), Math.Round(d.Progress, 2), d.Held))],
            R(log.TrueDroughts.Select(d => d.Seconds).DefaultIfEmpty(0).Max()),
            Math.Round(log.DebtPeak, 2),
            new InferredReport(log.Tuning.NearMetres, all.Count(e => e.NearAt >= 0),
                all.Where(e => !double.IsInfinity(e.NearestMetres)).Select(e => e.NearestMetres).DefaultIfEmpty(-1).Average() is var m && m >= 0 ? R(m) : -1),
            [.. all.Select(e => new EncounterLine(e.Index, e.Kind.ToString(), Name(e.Channel), e.Members.Count, e.Where, e.Vehicle, e.Part, e.Afoot,
                R(e.Seconds), Math.Round(e.Progress, 2), e.Crew, e.Cars, e.Overlap, R(e.EngagedAt), R(e.ConfrontedAt), R(e.EndedAt),
                e.EndedAt < 0 ? "about" : e.End.ToString().ToLowerInvariant(), e.Severity, R(e.HarassedAt), R(e.CompoundAt), [.. e.SecondsAt],
                R(e.NearAt), double.IsInfinity(e.NearestMetres) ? -1 : R(e.NearestMetres)))]);
    }

    static string Name(SpawnChannel c) => c.ToString().ToLowerInvariant();
}

/// <summary>A channel's attempts: tried, placed, and charged before placement with nothing placed (the brief's issue 3).</summary>
public sealed record ChannelAttempts(int Tried, int Placed, int ChargedUnplaced);

/// <summary>How the encounters ended (measured): killed, gone (left, dismissed, fled at dawn), still about; their engaged and dormant seconds; grabs and punishes.</summary>
public sealed record EncounterEnds(int Killed, int Gone, int About, double EngagedSeconds, double DormantSeconds, int Grabs, int Punishes);

/// <summary>A drought: run seconds from and to, its length, the run's progress at its end, and the director's commonest reason meanwhile.</summary>
public sealed record DroughtReport(double From, double To, double Seconds, double Progress, string Held);

/// <summary>Inferred, not measured (§8): encounters a living crewmate came within <paramref name="NearMetres"/> of ("near", never "seen"), and the mean closest approach.</summary>
public sealed record InferredReport(double NearMetres, int Near, double MeanNearestMetres);

/// <summary>One encounter, for the sweep and for reading a night through: seconds are the run's, −1 for never.</summary>
public sealed record EncounterLine(int Index, string Kind, string Channel, int Members, string Where, int Vehicle, string Part, bool Afoot,
    double Seconds, double Progress, int Crew, int Cars, int Overlap, double EngagedAt, double ConfrontedAt, double EndedAt, string End,
    int Severity, double HarassedAt, double CompoundAt, int[] SecondsAt, double NearAt, double NearestMetres);
