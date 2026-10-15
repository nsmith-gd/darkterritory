using DarkTerritory.Sim.Run;

namespace DarkTerritory.Sim.Campaign;

/// <summary>
/// One night's page in the crew's logbook (GDD §11 "What a crew keeps"; the director, 14 Oct 2026; ARCHITECTURE §8 note
/// 721): which night, the line, the date it was run by the keeper's clock, how it ended, the incident report's losses (the
/// cause-of-death lines and the train's own end), the commendations and the tally's net. Kept in the host's campaign save
/// and in every player's own profile, from the report every machine was sent.
/// </summary>
/// <param name="Night">The campaign's night number (1 for the first).</param>
/// <param name="Route">The line, as the page heads it ("Frontier, Hollin Halt to Barrow Bay").</param>
/// <param name="Date">The date line, by the machine that wrote the page (the sim never reads a clock).</param>
public sealed record LogPage(int Night, string Route, string Date, RunEnd End, double Net)
{
    /// <summary>How the night ended, as the end card says it: "DELIVERED. 5 CARS, 1 LOST. CREW HOME 3".</summary>
    public string Line { get; init; } = "";
    /// <summary>The report's losses in the clerk's words, "PRIYA: Mauled by the hounds at km 4. ...", in the order they happened.</summary>
    public IReadOnlyList<string> Losses { get; init; } = [];
    /// <summary>The night's commendations: "DAVE: Kept the Fire, from Priya".</summary>
    public IReadOnlyList<string> Commendations { get; init; } = [];
    /// <summary>The crew photo's file on the machine that wrote the page (note 721), or "" for none (not delivered, or not seen here).</summary>
    public string Photo { get; init; } = "";
}

/// <summary>
/// A crew this player rode with (note 721), in their profile: its name and campaign key, the pages of its logbook this
/// machine saw, and whether the campaign's over (a train taken at dawn, note 632), which posts it on the board of lost crews.
/// </summary>
public sealed record CrewLog(string Key, string Name)
{
    public IReadOnlyList<LogPage> Pages { get; init; } = [];
    public bool Over { get; init; }
}

/// <summary>The crew's memory (note 721): the logbook's pages and the board of lost crews' cards. Pure: reports in, pages out.</summary>
public static class Logbook
{
    /// <summary>
    /// A campaign's key, the same on every machine (the setup carries it): the crew's own id (<see cref="CampaignState.CrewId"/>,
    /// note 724), the one its travelling save is kept under, so two crews both called "The Crew" on different hosts keep two
    /// logbooks, and a crew hosted by another member keeps writing the same one.
    /// </summary>
    public static string Key(CampaignState s) => s.CrewId;

    /// <summary>How a night ended, in the end card's capitals.</summary>
    /// <param name="wakers">The Wakers were out (a train out at dawn is taken by them, not just late).</param>
    public static string Ended(RunEnd end, bool wakers = true) => end switch
    {
        RunEnd.Delivered => "DELIVERED",
        RunEnd.Derailed => "DERAILED",
        RunEnd.CrewLost => "THE WHOLE CREW LOST",
        RunEnd.DawnMissed => wakers ? "TAKEN AT DAWN" : "STILL OUT WHEN THE LINE WENT LIVE",
        RunEnd.Stranded => "STRANDED",
        _ => "UNFINISHED",
    };

    /// <summary>The report lines a page keeps: the deaths and the train's own end (a derailment, stranded), as the report reads them.</summary>
    public static bool Kept(IncidentKind kind) => kind is IncidentKind.Death or IncidentKind.Derailed or IncidentKind.Stranded;

    /// <summary>
    /// The page for a night from its incident report (GDD App. D.12), as every machine has it.
    /// </summary>
    /// <param name="commended">The night's commendations by name: to whom, which, from whom.</param>
    public static LogPage Page(RunReport r, int night, string route, string date, IEnumerable<(string To, string What, string From)> commended,
        bool wakers = true, string photo = "") =>
        new(night, route, date, r.End, r.Net)
        {
            Line = r.End == RunEnd.Delivered ? $"{Ended(r.End)}. {r.CarsDelivered} CARS, {r.CarsLost} LOST. CREW HOME {r.CrewHome}"
                : $"{Ended(r.End, wakers)}. {r.CarsLost} CARS LOST. CREW HOME {r.CrewHome}",
            Losses = [.. r.Lines.Where(l => Kept(l.Kind)).Select(l => l.Who.Length > 0 ? $"{l.Who.ToUpperInvariant()}: {l.Text}" : l.Text)],
            Commendations = [.. commended.Select(c => $"{c.To.ToUpperInvariant()}: {c.What}, from {c.From}")],
            Photo = photo,
        };

    /// <summary>
    /// The book with <paramref name="page"/> in it: in its night's place, replacing an earlier copy of that night's page (a
    /// night written again as its commendations come in, or a rejoin's report), keeping a photo the old copy had and the
    /// new one doesn't.
    /// </summary>
    public static IReadOnlyList<LogPage> With(IReadOnlyList<LogPage> book, LogPage page)
    {
        var old = book.FirstOrDefault(p => p.Night == page.Night);
        if (old is not null && page.Photo.Length == 0 && old.Photo.Length > 0)
            page = page with { Photo = old.Photo };
        return [.. book.Where(p => p.Night != page.Night).Append(page).OrderBy(p => p.Night)];
    }

    /// <summary>
    /// A crew's log in a profile with this night's page added (note 721), the crew's name kept up to date, and closed for
    /// good when the night ended the campaign.
    /// </summary>
    public static IReadOnlyList<CrewLog> Remember(IReadOnlyList<CrewLog> crews, string key, string name, LogPage page, bool over)
    {
        var had = crews.FirstOrDefault(c => c.Key == key) ?? new CrewLog(key, name);
        var log = had with { Name = name.Length > 0 ? name : had.Name, Pages = With(had.Pages, page), Over = had.Over || over };
        return [.. crews.Where(c => c.Key != key).Append(log)];
    }

    /// <summary>The crews posted on the board of lost crews (note 721): the ended ones, the last ended first.</summary>
    public static IReadOnlyList<CrewLog> Lost(IReadOnlyList<CrewLog> crews) =>
        [.. crews.Where(c => c.Over && c.Pages.Count > 0).Reverse()];

    /// <summary>
    /// A lost crew's card on the board (note 721): its name over how many nights it ran, how it ended ("TAKEN AT DAWN"), and
    /// the cause line of its last night (its first loss, or how the night ended).
    /// </summary>
    public static (string Title, string Text) Card(CrewLog crew)
    {
        var last = crew.Pages[^1];
        int nights = crew.Pages.Max(p => p.Night);
        string cause = last.Losses.Count > 0 ? last.Losses[0] : last.Line;
        return (crew.Name, $"Ran {nights} {(nights == 1 ? "night" : "nights")}. {Ended(last.End)}. {cause}");
    }
}
