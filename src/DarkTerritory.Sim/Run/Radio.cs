namespace DarkTerritory.Sim.Run;

/// <summary>run.json <c>radio</c>: how long each line of the dispatcher's manifest and the clerk's tally stays on the air.</summary>
/// <param name="LineSeconds">Each line's turn, read flat (GDD §9: "with the same tone used for the coal"); the least a spoken one gets.</param>
/// <param name="PauseSeconds">Spoken (note 230), the breath after each line before the next: the reading's even pace.</param>
public sealed record RadioTuning(double LineSeconds = 1.6, double PauseSeconds = 0.5);

/// <summary>
/// The fortress on the radio (GDD v1.4 §9, WP9; ARCHITECTURE §8 note 178): at the gate the yard dispatcher reads the crew
/// out by name as a manifest, in the same breath as the coal; at the terminus the yard clerk tallies the run as the cars come
/// through, cargo by the car and bodies by the body, each fee flat. "The settlement does not mourn."
/// </summary>
public static class Radio
{
    /// <summary>
    /// The dispatcher at the gate: who's aboard by name, then the coal, the powder and shot, and the gates, all the same.
    /// <paramref name="crew"/> are the player ids aboard, in order.
    /// </summary>
    public static List<string> Manifest(World world, IEnumerable<int> crew)
    {
        var lines = new List<string> { "Yard to consist. Manifest follows." };
        foreach (int id in crew)
            lines.Add($"Crew: {IncidentLog.NameOf(world, id)}.");
        var train = world.Train;
        lines.Add($"Coal: {train.Boiler.Tender:0}.");
        int rounds = train.Vehicles.Where(v => v is not null).Sum(v => v.Gun.Ammo);
        if (rounds > 0)
            lines.Add($"Powder and shot: {rounds}.");
        int cars = train.Dynamics.Consist.CarCount;
        lines.Add($"Cars: {cars}.");
        // The contract's freight (note 182), in the same voice: "Freight: comet-derived material."
        var freight = train.Dynamics.Consist.Vehicles.Where(v => v.Kind == Train.VehicleKind.Cargo && v.Load > 0.01).Select(v => v.Cargo).Distinct().ToList();
        if (freight.Count > 0)
            lines.Add($"Freight: {string.Join(", ", freight.Select(Train.Cargoes.Name))}.");
        lines.Add("Gates open. Yard out.");
        return lines;
    }

    /// <summary>
    /// The clerk at the terminus: the cars and their cargo, then each death body by body (its fee, and the refund if the body
    /// came home), the mail, the costs, and the net. Every figure as flat as the last.
    /// </summary>
    public static List<string> Tally(RunReport report)
    {
        var lines = new List<string> { "Yard clerk. Consist received. Tally follows." };
        lines.Add($"Cars delivered: {report.CarsDelivered}. Cargo: {report.Gross:0}.");
        if (report.CarsLost > 0)
            lines.Add($"Cars lost: {report.CarsLost}.");
        // GDD §19: the most valuable cargo there is, read like the coal.
        if (report.ChildrenHome > 0)
            lines.Add($"Child survivor{(report.ChildrenHome == 1 ? "" : "s")}: {report.ChildrenHome}. Paid {report.ChildPay:0}.");
        foreach (var death in report.Lines.Where(l => l.Kind == IncidentKind.Death))
            lines.Add(death.Refund > 0
                ? $"{death.Who}. Body recovered. Fee {death.Fee:0}. Refund {death.Refund:0}."
                : $"{death.Who}. Body not recovered. Fee {death.Fee:0}.");
        if (report.Mail > 0)
            lines.Add($"Mail: {report.Mail:0}.");
        if (report.Scavenged > 0)
            lines.Add($"Salvage: {report.Scavenged:0}.");
        lines.Add($"Coal: {report.CoalCost:0}. Powder and shot: {report.AmmoCost:0}. Repairs: {report.RepairCost:0}.");
        lines.Add($"Net: {report.Net:0}. Next.");
        return lines;
    }

    /// <summary>
    /// GDD v1.4 App. E.9, the Stranded outro: the clerk over the pull-back, as flat as the coal. On the screen and said
    /// (note 232).
    /// </summary>
    public static string Stranded(double km) => $"Consist reported stranded at km {km:0}. Recovery at first light. Recovery is chargeable.";

    /// <summary>
    /// How many of <paramref name="lines"/> are on the air <paramref name="seconds"/> in, and the newest's share typed. With
    /// <paramref name="times"/> (each line's turn, from <see cref="Times"/>), at the voice's pace, typed as it's said;
    /// without, a line every <see cref="RadioTuning.LineSeconds"/>.
    /// </summary>
    public static (int Lines, double Typed) Reading(IReadOnlyList<string> lines, double seconds, RadioTuning t, IReadOnlyList<double>? times = null)
    {
        if (seconds < 0 || lines.Count == 0)
            return (0, 0);
        if (times is not null)
        {
            double start = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                double turn = i < times.Count ? times[i] : t.LineSeconds;
                if (seconds < start + turn || i == lines.Count - 1)
                    return (i + 1, Math.Clamp((seconds - start) / Math.Max(0.1, turn - t.PauseSeconds), 0, 1));
                start += turn;
            }
        }
        double at = seconds / Math.Max(0.1, t.LineSeconds);
        int done = Math.Min(lines.Count, (int)at + 1);
        return (done, done >= lines.Count && at >= lines.Count ? 1 : Math.Clamp((at - (done - 1)) * 1.6, 0, 1));
    }

    /// <summary>The whole reading's length: every line's turn, and the last held one more.</summary>
    public static double Length(IReadOnlyList<string> lines, RadioTuning t, IReadOnlyList<double>? times = null) =>
        (times is null ? lines.Count * t.LineSeconds : lines.Select((_, i) => i < times.Count ? times[i] : t.LineSeconds).Sum()) + t.LineSeconds;

    /// <summary>
    /// Each line's turn when it's spoken (note 230): as long as <paramref name="spoken"/> says saying it takes, and the pause
    /// after, never less than <see cref="RadioTuning.LineSeconds"/>. The voice is presentation, so each machine times its own.
    /// </summary>
    public static List<double> Times(IReadOnlyList<string> lines, RadioTuning t, Func<string, double> spoken) =>
        [.. lines.Select(l => Math.Max(t.LineSeconds, spoken(l) + t.PauseSeconds))];
}
