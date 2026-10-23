namespace DarkTerritory.Sim.Run;

/// <summary>run.json <c>radio</c>: how long each line of the dispatcher's manifest and the clerk's tally stays on the air.</summary>
/// <param name="LineSeconds">Each line's turn, read flat (GDD §9: "with the same tone used for the coal"); the least a spoken one gets.</param>
/// <param name="PauseSeconds">Spoken (note 240), the breath after each line before the next: the reading's even pace.</param>
/// <param name="Manifest">
/// Whether the dispatcher reads the manifest at the gate at all (note 264: the director dropped it, run.json says false).
/// </param>
/// <param name="KitLost">
/// GDD App. E.12 question 5, answered by the director on 7 Oct 2026 (note 308): the clerk says it on the radio, once, when
/// the train's last engineering kit is lost. False: the fitter's empty shelf is the only tell, as before.
/// </param>
public sealed record RadioTuning(double LineSeconds = 1.6, double PauseSeconds = 0.5, bool Manifest = true, bool KitLost = true);

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
    /// came home), the mail, the costs, and the net. Every figure as flat as the last. <paramref name="owned"/> are what the
    /// crew's catalogue purchases have the clerk say (note 678; <see cref="Campaign.Campaign.TallyLines"/>): a gold whistle
    /// read like the coal, before the costs.
    /// </summary>
    public static List<string> Tally(RunReport report, IReadOnlyList<string>? owned = null)
    {
        var lines = new List<string> { "Yard clerk. Consist received. Tally follows." };
        // Note 766: the cargo alone; the mail, the finds and a child home are read on their own lines, not in it too.
        lines.Add($"Cars delivered: {report.CarsDelivered}. Cargo: {Cargo(report):0}.");
        if (report.CarsLost > 0)
            lines.Add($"Cars lost: {report.CarsLost}.");
        // GDD §19: the most valuable cargo there is, read like the coal.
        if (report.ChildrenHome > 0)
            lines.Add($"Child survivor{(report.ChildrenHome == 1 ? "" : "s")}: {report.ChildrenHome}. Paid {report.ChildPay:0}.");
        // Each crewmate's bodies read once (the director's playtest of 23 Oct, line 12, note 994: "Body not recovered. Fee 350.
        // Body not recovered. Fee 350 ... I should only get body not recovered once"): a crewmate who died twice in the night
        // (freed from a Holdout, App. D.8, and lost again) is one line, their fees added, not the same line said twice. The
        // bodies brought home and those left are each read once a crewmate, in the order they first died.
        foreach (var group in report.Lines.Where(l => l.Kind == IncidentKind.Death).GroupBy(l => (l.Who, Home: l.Refund > 0)))
        {
            double fee = group.Sum(l => l.Fee), refund = group.Sum(l => l.Refund);
            lines.Add(group.Key.Home
                ? $"{group.Key.Who}. Body recovered. Fee {fee:0}. Refund {refund:0}."
                : $"{group.Key.Who}. Body not recovered. Fee {fee:0}.");
        }
        if (report.Mail > 0)
            lines.Add($"Mail: {report.Mail:0}.");
        if (report.Scavenged > 0)
            lines.Add($"Salvage: {report.Scavenged:0}.");
        if (owned is not null)
            lines.AddRange(owned.Select(line => Fill(line, report.Counts)));
        // Note 718 (the director, 14 Oct 2026: "as they play the game more often, they learn what those objects are"): the
        // night's best finds by name with what each paid, as flat as the coal, and who brought the most aboard.
        lines.AddRange(Prizes(report));
        // Note 766: what the net was ever short of without a word: the tow in, the town's brigade.
        if (report.Recovery > 0)
            lines.Add($"Recovery: {report.Recovery:0}.");
        if (report.BroughtInFees > 0)
            lines.Add($"Brigade fees: {report.BroughtInFees:0}.");
        lines.Add($"Coal: {report.CoalCost:0}. Powder and shot: {report.AmmoCost:0}. Repairs: {report.RepairCost:0}.");
        // Note 766 (the director, 13 Oct: "net negative 1607. So I need a better understanding as to why"): in the red, what
        // cost most, as flat as the rest.
        lines.Add(report.Net < 0 && Costliest(report) is var (what, much) ? $"Net: {report.Net:0}. Most of it {what}, {much:0}. Next." : $"Net: {report.Net:0}. Next.");
        return lines;
    }

    /// <summary>
    /// Note 972 (upgrades.md §8.5: "the tally reads 'Bar: eight drinks. Repairs: one window'"): an owned upgrade's tally line
    /// (campaign.json <c>tally</c>) with the night's counts read into it (<see cref="RunReport.Counts"/>). <c>{name}</c> is the
    /// count; <c>{name?counted|not}</c> reads the first when there's one or more of it (<c>#</c> in it the count) and the
    /// second when there's none. A name the night didn't count reads as nought; any other brace is left as it is (the
    /// engine's name is the session's, read in before).
    /// </summary>
    public static string Fill(string line, IReadOnlyDictionary<string, int> counts)
    {
        if (!line.Contains('{'))
            return line;
        line = System.Text.RegularExpressions.Regex.Replace(line, @"\{([A-Za-z]+)\?([^|{}]*)\|([^{}]*)\}", m =>
        {
            int n = counts.GetValueOrDefault(m.Groups[1].Value);
            return n > 0 ? m.Groups[2].Value.Replace("#", n.ToString(System.Globalization.CultureInfo.InvariantCulture)) : m.Groups[3].Value;
        });
        return System.Text.RegularExpressions.Regex.Replace(line, @"\{([A-Za-z]+)\}", m =>
            m.Groups[1].Value == "engine" ? m.Value : counts.GetValueOrDefault(m.Groups[1].Value).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>The freight's pay alone (note 766): the gross less the mail, the finds and a child home, each read on its own.</summary>
    public static double Cargo(RunReport r) => r.Gross - r.Mail - r.Scavenged - r.ChildPay;

    /// <summary>
    /// What made a night's net (note 766), each item signed (pay in, costs out), earned first: the cargo, a child home, the
    /// mail, the finds; then the crew lost and the bodies brought home against it, the tow in, the brigade, coal, powder and
    /// shot, repairs. Only what's not nothing. They add up to <see cref="RunReport.Net"/>.
    /// </summary>
    public static IReadOnlyList<(string Name, double Amount)> Ledger(RunReport r)
    {
        var items = new List<(string, double)>();
        void Add(string name, double amount)
        {
            if (Math.Abs(amount) >= 0.5)
                items.Add((name, amount));
        }
        Add("cargo", Cargo(r));
        Add("a child home", r.ChildPay);
        Add("mail", r.Mail);
        Add("finds", r.Scavenged);
        Add("the crew lost", -r.CrewLossFees);
        Add("bodies brought home", r.BodyRefunds);
        Add("the recovery", -r.Recovery);
        Add("the brigade", -r.BroughtInFees);
        Add("coal", -r.CoalCost);
        Add("powder and shot", -r.AmmoCost);
        Add("repairs", -r.RepairCost);
        return items;
    }

    /// <summary>What cost the night most (note 766), and how much; null if nothing did.</summary>
    public static (string Name, double Amount)? Costliest(RunReport r) =>
        Ledger(r).Where(i => i.Amount < 0).OrderBy(i => i.Amount).Select(i => ((string, double)?)(i.Name, -i.Amount)).FirstOrDefault();

    /// <summary>
    /// Note 718: the tally's lines for the night's finds, the worthiest kinds first ("Find: pocket watch, 110.", "Finds: candles,
    /// 3 at 25."), then the finder's line ("Light fingers: Dave, 1140."). Nothing on a night with no finds home.
    /// </summary>
    public static IEnumerable<string> Prizes(RunReport report)
    {
        foreach (var p in report.Prizes)
            yield return p.Count > 1 ? $"Finds: {Said(p.Name)}, {p.Count} at {p.Worth:0}." : $"Find: {Said(p.Name)}, {p.Worth:0}.";
        if (report.Finder.Length > 0 && report.FinderWorth > 0)
            yield return $"Light fingers: {report.Finder}, {report.FinderWorth:0}.";
    }

    /// <summary>A find's name as the clerk reads it, mid-line: "Pocket watch" is "pocket watch", "The Gaunt's hand" "the Gaunt's hand".</summary>
    static string Said(string name) => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];

    /// <summary>
    /// GDD v1.4 App. E.9, the Stranded outro: the clerk over the pull-back, as flat as the coal. On the screen and said
    /// (note 242).
    /// </summary>
    public static string Stranded(double km) => $"Consist reported stranded at km {km:0}. Recovery at first light. Recovery is chargeable.";

    /// <summary>
    /// GDD App. E.12 question 5 (note 308): the train's last engineering kit lost, the yard says so, flat, once. It says what's
    /// known (gone with a car, by its number), not what follows from it.
    /// </summary>
    /// <summary>
    /// The human cannonball (note 746: "Sam's been fired from the rear cannon"): the yard on the radio, once a crewmate's been
    /// fired from the guard van's gun.
    /// </summary>
    public static List<string> Fired(string name) => ["Yard to consist.", $"{name} fired from the rear cannon.", "Yard out."];

    public static List<string> KitLost(KitWhere kit) =>
    [
        "Yard to consist.",
        kit.Loss is KitLoss.CarTaken or KitLoss.LeftBehind && kit.Vehicle > 0
            ? $"Engineering kit reported lost with car {kit.Vehicle}." : "Engineering kit reported lost.",
        "Yard out.",
    ];

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
    /// Each line's turn when it's spoken (note 240): as long as <paramref name="spoken"/> says saying it takes, and the pause
    /// after, never less than <see cref="RadioTuning.LineSeconds"/>. The voice is presentation, so each machine times its own.
    /// </summary>
    public static List<double> Times(IReadOnlyList<string> lines, RadioTuning t, Func<string, double> spoken) =>
        [.. lines.Select(l => Math.Max(t.LineSeconds, spoken(l) + t.PauseSeconds))];
}
