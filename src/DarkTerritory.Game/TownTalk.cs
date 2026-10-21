using Ballast;
using DarkTerritory.Sim.Towns;

namespace DarkTerritory.Game;

/// <summary>What a town card shows: a person's line, a paper, or a thing looked at (a centrepiece, a plaque, a door).</summary>
public enum TownCardKind : byte { Speech, Paper, Plate }

/// <summary>A card on the screen: its heading, its text (a person's typed out so far), and what Use does next.</summary>
public sealed record TownCard(TownCardKind Kind, string Heading, string Text, string Footer);

/// <summary>
/// Talking and reading in a fortress town, on this machine alone (GDD §3.1, App. F.1: "text-only lines from the
/// townspeople ... little notes"; ARCHITECTURE §8 note 281). A Use press at the town's prompt opens a card; Use again
/// hears the next line or turns to the next notice; walking off closes it. Nothing here changes the night, so nothing is
/// sent: the press is the app's to keep from the host (<see cref="Hud.TownTarget"/>).
/// </summary>
public sealed class TownTalk
{
    /// <summary>How many times each person has been talked to (by town and id): the next line is theirs to say, round again.</summary>
    readonly Dictionary<(string Town, int Id), int> _heard = [];

    /// <summary>
    /// The cards on the board of lost crews (GDD §3.1, §11; note 721): this player's ended campaigns from their profile
    /// (<see cref="Sim.Campaign.Logbook.Card"/>), the last ended first. The app sets it; empty, the board says nothing's posted.
    /// </summary>
    public IReadOnlyList<(string Title, string Text)> LostCrews { get; set; } = [];

    /// <summary>Whether a town target is the board of lost crews (note 721).</summary>
    public static bool IsLostCrews(Town town, TownTarget t) => t.Kind == TownTargetKind.Fixture && town.Plan.Fixtures[t.Index].Kind == "lostcrews";

    /// <summary>What's being read or listened to; null for nothing.</summary>
    public TownTarget? Open { get; private set; }
    /// <summary>The town it's in (the departure's, or the terminus's once the night's delivered, note 603); null for nothing open.</summary>
    public Town? In { get; private set; }
    /// <summary>The line being said (a person), or the notice being read (the board).</summary>
    public int Page { get; private set; }
    /// <summary>When the card opened or turned (seconds), for a line typing out.</summary>
    public double Since { get; private set; }

    /// <summary>
    /// The prompt for a thing in the town in front of you: what it is, and the action and its key, never what the action
    /// will do (GDD §32, the director's decisions of 7 Oct on #206: <c>ACTION : [KEY]</c>).
    /// </summary>
    /// <param name="wine">Nicki has a glass for you in reach (note 571): her prompt says the hold that takes it.</param>
    /// <param name="selling">The blacksmith's open for the crew (note 697: a campaign night's yard): their prompt says the press
    /// opens the catalogue (note 904: talking to them is buying from them).</param>
    public static string Prompt(Town town, TownTarget t, bool wine = false, bool selling = false) => t.Kind switch
    {
        TownTargetKind.Person when wine && town.Plan.People[t.Index].Hosting => $"{town.Plan.People[t.Index].Name.ToUpperInvariant()}   TALK : [E]   A GLASS : HOLD [E]",
        // Clearly the blacksmith (note 697), whatever the town calls them, and what they sell.
        TownTargetKind.Person when IsSmith(town.Plan.People[t.Index]) =>
            $"{town.Plan.People[t.Index].Name.ToUpperInvariant()}, BLACKSMITH   {(selling ? "UPGRADES" : "TALK")} : [E]",
        TownTargetKind.Person => $"{town.Plan.People[t.Index].Name.ToUpperInvariant()}   TALK : [E]",
        TownTargetKind.Board => $"THE BOARD ({town.Notices.Count} {(town.Notices.Count == 1 ? "NOTICE" : "NOTICES")})   READ : [E]",
        TownTargetKind.Paper => $"{town.Plan.Papers[t.Index].Title.ToUpperInvariant()}   READ : [E]",
        TownTargetKind.Door => $"{town.Plan.Buildings[t.Index].Name.ToUpperInvariant()}   KNOCK : [E]",
        TownTargetKind.House => $"{HouseName(town.Plan.Houses[t.Index]).ToUpperInvariant()}   {(town.Plan.Houses[t.Index].Kind == HouseKind.Lived ? "KNOCK" : "LOOK")} : [E]",
        TownTargetKind.Fixture when IsLostCrews(town, t) => "THE BOARD OF LOST CREWS   READ : [E]",
        _ => $"{town.Plan.Fixtures[t.Index].Name.ToUpperInvariant()}   LOOK : [E]",
    };

    /// <summary>The town's blacksmith, who does the trains (note 697).</summary>
    public static bool IsSmith(Townsperson p) => p.Role == "blacksmith";

    /// <summary>
    /// Whether a Use press on <paramref name="target"/> opens the blacksmith's catalogue rather than a card (note 904; the
    /// director, 16 Oct: "I would have assumed talking to him, I get the upgrades"): the town's blacksmith, while they sell.
    /// </summary>
    public static bool OpensForge(Town town, TownTarget? target, bool selling) =>
        selling && target is { Kind: TownTargetKind.Person } t && IsSmith(town.Plan.People[t.Index]);

    /// <summary>The blacksmith's card's foot when they aren't selling (note 904): where and when their wares are.</summary>
    public const string ForgeLater = "UPGRADES : AT THE FORGE, IN A CAMPAIGN NIGHT'S YARD";

    /// <summary>
    /// Whether the blacksmith's selling to this crew now (note 697): a campaign night (its upgrades are the campaign's), in
    /// the yard before the gate (GDD §9: the town's a safe space, and what's bought is fitted before the train goes).
    /// </summary>
    public static bool Selling(IPlaySession s) =>
        s is NetPlaySession { Setup.CampaignNight: true } && s.World.Run is { Phase: DarkTerritory.Sim.Run.RunPhase.Yard };

    /// <summary>
    /// A Use press with <paramref name="target"/> the town's prompt (null when the prompt is something else's): opens its
    /// card, or with that card open, hears the next line or turns to the next notice, closing after the last. True when
    /// the town took the press.
    /// </summary>
    public bool Use(Town town, TownTarget? target, double now)
    {
        if (target is not { } t)
        {
            Open = null;
            return false;
        }
        Since = now;
        // A card open in the other town is closed by a press in this one (note 603: the same thing's index in each).
        if (In != town)
            Open = null;
        In = town;
        if (Open == t)
        {
            switch (t.Kind)
            {
                case TownTargetKind.Person:
                    Page = Next(town, town.Plan.People[t.Index]);
                    return true;
                case TownTargetKind.Board when Page + 1 < town.Notices.Count:
                    Page++;
                    return true;
                case TownTargetKind.Fixture when IsLostCrews(town, t) && Page + 1 < LostCrews.Count:
                    Page++;
                    return true;
                default:
                    Open = null;
                    return true;
            }
        }
        Open = t;
        Page = t.Kind == TownTargetKind.Person ? Next(town, town.Plan.People[t.Index]) : 0;
        return true;
    }

    /// <summary>Shuts the card open, if any (the blacksmith's catalogue opening over it, note 697).</summary>
    public void Close() => Open = null;

    /// <summary>A house by its family: theirs, or for one nobody lives in now, the old one.</summary>
    public static string HouseName(TownHouse h) => h.Kind is HouseKind.Lived or HouseKind.Open ? $"the {h.Family} house" : $"the old {h.Family} house";

    int Next(Town town, Townsperson p)
    {
        int heard = _heard.GetValueOrDefault((town.Plan.Name, p.Id));
        _heard[(town.Plan.Name, p.Id)] = heard + 1;
        int page = p.Lines.Count == 0 ? 0 : heard % p.Lines.Count;
        // The custom's told once in a town (note 971): whoever tells it first says it; after, anybody's custom line is what
        // they say of it having been told (theirs too, the next time round).
        _instead = null;
        if (p.Custom.Contains(page) && !_toldIn.Add(town.Plan.Name))
            _instead = Told(town.Tuning, p);
        return page;
    }

    // The towns this crew's been told the custom in (note 971), on this machine; and what the person open says in place of
    // the custom's line, once it has been.
    readonly HashSet<string> _toldIn = [];
    string? _instead;

    /// <summary>Whether this machine's crew has been told the custom in <paramref name="town"/> (note 971).</summary>
    public bool Told(Town town) => _toldIn.Contains(town.Plan.Name);

    /// <summary>What <paramref name="p"/> says in place of the custom once it's been told (towns.json <c>told</c>, by their temperament).</summary>
    public static string Told(TownTuning t, Townsperson p)
    {
        var lines = p.Personality is { } who && t.Told.TryGetValue(who.Temperament, out var theirs) && theirs.Length > 0 ? theirs
            : t.Told.TryGetValue("plain", out var plain) ? plain : [];
        return lines.Length == 0 ? p.Lines[0] : lines[p.Id % lines.Length];
    }

    /// <summary>
    /// A household's quarrel overheard (note 971): the line being said now in the nearest house within <c>quarrels.hear</c>
    /// of <paramref name="eye"/> that's quarrelling, as a card; null with none. Nothing to press: it's theirs, not yours.
    /// </summary>
    public static TownCard? Overheard(Town town, Double3 eye, double now)
    {
        var q = town.Tuning.Quarrels;
        TownCard? card = null;
        double best = q.Hear;
        foreach (var quarrel in town.Plan.Quarrels)
        {
            var house = town.Plan.Houses[quarrel.House];
            var (fs, fd) = house.Rail(0, 0);
            double far = ((town.World(fs, fd) - eye) with { Y = 0 }).Length;
            if (far > best || QuarrelLine(q, quarrel, now) is not (var k and >= 0))
                continue;
            var (who, line) = quarrel.Lines[k];
            best = far;
            card = new TownCard(TownCardKind.Speech, $"Through the wall, {HouseName(house)}", $"{town.Plan.People[who].Name}: {line}", "");
        }
        return card;
    }

    /// <summary>
    /// Which of <paramref name="quarrel"/>'s lines is being said at <paramref name="now"/> (note 971): each house's on its own
    /// beat, for <c>lasts</c> s of every <c>every</c>, a line each <c>lineSeconds</c>; −1 between.
    /// </summary>
    public static int QuarrelLine(QuarrelTuning q, TownQuarrel quarrel, double now)
    {
        if (quarrel.Lines.Count == 0 || q.Every <= 0)
            return -1;
        double into = (now + quarrel.House * 37.0) % q.Every;
        if (into < 0)
            into += q.Every;
        return into >= q.Lasts ? -1 : (int)(into / Math.Max(0.1, q.LineSeconds)) % quarrel.Lines.Count;
    }

    /// <summary>Closes the card once you've walked off from it, or a person's line has been said and left long enough.</summary>
    public void Step(Town town, Double3 eye, double now)
    {
        // Whoever you're talking to stops on their round for you (note 353), on this machine.
        town.Hold(Open is { Kind: TownTargetKind.Person } held ? held.Index : -1);
        if (Open is not { } t)
            return;
        var tuning = town.Tuning;
        bool gone = (town.Where(t) - eye).Length > tuning.Reach.CloseBeyond;
        bool said = t.Kind == TownTargetKind.Person
            && now - Since > tuning.VoiceOf(town.Plan.People[t.Index]).Seconds(Line(town), tuning.TypePerSecond) + tuning.LingerSeconds;
        if (gone || said)
        {
            Open = null;
            town.Hold(-1);
        }
    }

    string Line(Town town) => Open is { Kind: TownTargetKind.Person } t && town.Plan.People[t.Index].Lines is { Count: > 0 } lines
        ? _instead ?? lines[Math.Clamp(Page, 0, lines.Count - 1)] : "";

    /// <summary>The card to draw now, or null.</summary>
    public TownCard? Card(Town town, double now)
    {
        if (Open is not { } t)
            return null;
        switch (t.Kind)
        {
            case TownTargetKind.Person:
                {
                    var p = town.Plan.People[t.Index];
                    string line = Line(town);
                    // In their own voice (note 963): a frayed one's in bursts, a hollow one's slow, a talker's quick.
                    int typed = town.Tuning.VoiceOf(p).Typed(line, town.Tuning.TypePerSecond, now - Since);
                    string again = p.Lines.Count > 1 && typed == line.Length ? "AGAIN : [E]" : "";
                    // Note 904: a press on the blacksmith opens the forge while they sell; a card is them not selling now.
                    if (IsSmith(p))
                        again = again.Length > 0 ? $"{again}   {ForgeLater}" : ForgeLater;
                    return new TownCard(TownCardKind.Speech, $"{p.Name}, {p.Title}", line[..typed], again);
                }
            case TownTargetKind.Board:
                {
                    var notices = town.Notices;
                    var n = notices[Math.Clamp(Page, 0, notices.Count - 1)];
                    string footer = Page + 1 < notices.Count ? $"({Page + 1}/{notices.Count})   NEXT : [E]" : $"({Page + 1}/{notices.Count})   DONE : [E]";
                    return new TownCard(TownCardKind.Paper, n.Title, n.Text, footer);
                }
            case TownTargetKind.Fixture when IsLostCrews(town, t) && LostCrews.Count > 0:
                {
                    // Note 721: each lost crew's card in turn, read close up as a notice is.
                    var (title, text) = LostCrews[Math.Clamp(Page, 0, LostCrews.Count - 1)];
                    string footer = Page + 1 < LostCrews.Count ? $"({Page + 1}/{LostCrews.Count})   NEXT : [E]" : $"({Page + 1}/{LostCrews.Count})   DONE : [E]";
                    return new TownCard(TownCardKind.Paper, title, text, footer);
                }
            case TownTargetKind.Fixture when IsLostCrews(town, t):
                return new TownCard(TownCardKind.Paper, town.Plan.Fixtures[t.Index].Name, town.Plan.Fixtures[t.Index].Text, "DONE : [E]");
            case TownTargetKind.Paper:
                {
                    var n = town.Plan.Papers[t.Index];
                    return new TownCard(TownCardKind.Paper, n.Title, n.Text, "PUT IT DOWN : [E]");
                }
            case TownTargetKind.Door:
                {
                    var b = town.Plan.Buildings[t.Index];
                    return new TownCard(TownCardKind.Plate, b.Name, b.Knock, "");
                }
            case TownTargetKind.House:
                {
                    var h = town.Plan.Houses[t.Index];
                    return new TownCard(TownCardKind.Plate, HouseName(h), h.Text, "");
                }
            default:
                {
                    var f = town.Plan.Fixtures[t.Index];
                    return new TownCard(TownCardKind.Plate, f.Name, f.Text, "");
                }
        }
    }
}
