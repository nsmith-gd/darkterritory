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
    /// <summary>How many times each person has been talked to (by id): the next line is theirs to say, round again.</summary>
    readonly Dictionary<int, int> _heard = [];

    /// <summary>What's being read or listened to; null for nothing.</summary>
    public TownTarget? Open { get; private set; }
    /// <summary>The line being said (a person), or the notice being read (the board).</summary>
    public int Page { get; private set; }
    /// <summary>When the card opened or turned (seconds), for a line typing out.</summary>
    public double Since { get; private set; }

    /// <summary>
    /// The prompt for a thing in the town in front of you: what it is, and the action and its key, never what the action
    /// will do (GDD §32, the director's decisions of 7 Oct on #206: <c>ACTION : [KEY]</c>).
    /// </summary>
    /// <param name="wine">Nicki has a glass for you in reach (note 551): her prompt says the hold that takes it.</param>
    public static string Prompt(Town town, TownTarget t, bool wine = false) => t.Kind switch
    {
        TownTargetKind.Person when wine && town.Plan.People[t.Index].Hosting => $"{town.Plan.People[t.Index].Name.ToUpperInvariant()}   TALK : [E]   A GLASS : HOLD [E]",
        TownTargetKind.Person => $"{town.Plan.People[t.Index].Name.ToUpperInvariant()}   TALK : [E]",
        TownTargetKind.Board => $"THE BOARD ({town.Notices.Count} {(town.Notices.Count == 1 ? "NOTICE" : "NOTICES")})   READ : [E]",
        TownTargetKind.Paper => $"{town.Plan.Papers[t.Index].Title.ToUpperInvariant()}   READ : [E]",
        TownTargetKind.Door => $"{town.Plan.Buildings[t.Index].Name.ToUpperInvariant()}   KNOCK : [E]",
        TownTargetKind.House => $"{HouseName(town.Plan.Houses[t.Index]).ToUpperInvariant()}   {(town.Plan.Houses[t.Index].Kind == HouseKind.Lived ? "KNOCK" : "LOOK")} : [E]",
        _ => $"{town.Plan.Fixtures[t.Index].Name.ToUpperInvariant()}   LOOK : [E]",
    };

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
        if (Open == t)
        {
            switch (t.Kind)
            {
                case TownTargetKind.Person:
                    Page = Next(town.Plan.People[t.Index]);
                    return true;
                case TownTargetKind.Board when Page + 1 < town.Notices.Count:
                    Page++;
                    return true;
                default:
                    Open = null;
                    return true;
            }
        }
        Open = t;
        Page = t.Kind == TownTargetKind.Person ? Next(town.Plan.People[t.Index]) : 0;
        return true;
    }

    /// <summary>A house by its family: theirs, or for one nobody lives in now, the old one.</summary>
    public static string HouseName(TownHouse h) => h.Kind is HouseKind.Lived or HouseKind.Open ? $"the {h.Family} house" : $"the old {h.Family} house";

    int Next(Townsperson p)
    {
        int heard = _heard.GetValueOrDefault(p.Id);
        _heard[p.Id] = heard + 1;
        return p.Lines.Count == 0 ? 0 : heard % p.Lines.Count;
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
        bool said = t.Kind == TownTargetKind.Person && now - Since > Line(town).Length / Math.Max(1, tuning.TypePerSecond) + tuning.LingerSeconds;
        if (gone || said)
        {
            Open = null;
            town.Hold(-1);
        }
    }

    string Line(Town town) => Open is { Kind: TownTargetKind.Person } t && town.Plan.People[t.Index].Lines is { Count: > 0 } lines
        ? lines[Math.Clamp(Page, 0, lines.Count - 1)] : "";

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
                    int typed = (int)Math.Clamp((now - Since) * town.Tuning.TypePerSecond, 0, line.Length);
                    return new TownCard(TownCardKind.Speech, $"{p.Name}, {p.Title}", line[..typed],
                        p.Lines.Count > 1 && typed == line.Length ? "AGAIN : [E]" : "");
                }
            case TownTargetKind.Board:
                {
                    var notices = town.Notices;
                    var n = notices[Math.Clamp(Page, 0, notices.Count - 1)];
                    string footer = Page + 1 < notices.Count ? $"({Page + 1}/{notices.Count})   NEXT : [E]" : $"({Page + 1}/{notices.Count})   DONE : [E]";
                    return new TownCard(TownCardKind.Paper, n.Title, n.Text, footer);
                }
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
