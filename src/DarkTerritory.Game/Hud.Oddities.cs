using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game;

/// <summary>
/// The odd outcomes on the HUD (queue #464, note 730): the prompt for what answers a held Use (the bell rope, a wash at a
/// well, a postbox) and a find's way down a well, in note 285's form (what it is and the key, never what it'll do), and the
/// letter taken from a postbox (or a note picked up by the line, note 961), read out on this machine to the one who took it. Dave's painting has no prompt of its own:
/// sitting with him is found, not offered.
/// </summary>
public static partial class Hud
{
    /// <summary>A find or a toy in hand at a well's mouth (note 730): the press that drops it down, before the hands' put-down.</summary>
    static string? WellPrompt(IPlaySession s) => s.World.WellToDropIn(s.Player, s.PlayerId) is not null ? "DOWN THE WELL : [E]" : null;

    /// <summary>Empty-handed at an odd thing that answers a held Use (note 730), before the town's own prompts.</summary>
    static string? OddPrompt(IPlaySession s)
    {
        var world = s.World;
        var p = s.Player;
        if (world.Bodies.CarriedBy(s.PlayerId) is not null)
            return null;
        if (world.BellRopeInReach(p) is not null)
            return "THE BELL ROPE   PULL : HOLD [E]";
        if (p.Has(PlayerFlags.Sooted) && world.OddThingInReach(p, OddThingKind.Well) is not null)
            return "THE WELL   WASH : HOLD [E]";
        if (world.OddThingInReach(p, OddThingKind.Postbox) is { } box)
            return world.Oddities!.Done(box.Index) ? "THE POSTBOX   EMPTY" : "THE POSTBOX   OPEN : HOLD [E]";
        // A note on a doorstep (note 961); once it's picked up there's nothing there to say.
        if (world.OddThingInReach(p, OddThingKind.Note) is { } note && !world.Oddities!.Done(note.Index))
            return "A NOTE   PICK UP : HOLD [E]";
        return null;
    }

    // The letter this machine's player took last (note 730): the happening's id, and the card while it's up.
    static int _letterSeen = -1;
    static (double Until, int Box, TownCard Card)? _letter;

    /// <summary>How long a letter taken stays up (s), unless its reader walks off. Not a design number.</summary>
    const double LetterSeconds = 14;

    /// <summary>
    /// The letter this machine's player has just taken from a postbox (note 730), as a paper card: from the host's happening
    /// (its by is them), until it's been up a while or they've walked off from the box. Null for none.
    /// </summary>
    static TownCard? LetterCard(IPlaySession s, double now)
    {
        var world = s.World;
        if (world.Oddities is not { } odd)
            return null;
        foreach (var e in world.OddEvents)
            if (e.Kind == OddKind.Letter && e.By == s.PlayerId && e.Id != _letterSeen && e.Thing < odd.Things.Count)
            {
                // A postbox's letter, or a note picked up by the line (note 961): the same paper in the hand.
                var card = odd.LetterIn(e.Thing) is { } letter ? new TownCard(TownCardKind.Paper, $"To {letter.To}", letter.Text, "")
                    : odd.NoteIn(e.Thing) is { } note ? new TownCard(TownCardKind.Paper, note.Heading, note.Text, "") : null;
                if (card is null)
                    continue;
                _letterSeen = e.Id;
                _letter = (now + LetterSeconds, e.Thing, card);
            }
        if (_letter is not { } up)
            return null;
        var feet = PlayerMotor.WorldPosition(s.Player, s.Train);
        if (now > up.Until || up.Box >= odd.Things.Count || ((odd.At(up.Box) - feet) with { Y = 0 }).Length > LetterWalkOff)
        {
            _letter = null;
            return null;
        }
        return up.Card;
    }

    /// <summary>How far from the postbox its letter's reader walks before it's put away (m). Not a design number.</summary>
    const double LetterWalkOff = 4;
}
