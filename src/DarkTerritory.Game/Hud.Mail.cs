using Ballast.Render;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;

namespace DarkTerritory.Game;

/// <summary>
/// The mail cranes on the HUD (note 673; the director, 13 Oct: "I still don't know what the mail is suggesting"). The
/// board ("MAIL &gt;") was all there was: what it's for was nowhere. In note 285's form, as the cranes come: the board read,
/// its crane at the top of the screen, which side and how far, as a place is; in its last seconds, the same for anyone in
/// a car; at the open door on its side, the crosshair says the hook; and a bag caught, what was in it.
/// </summary>
public static partial class Hud
{
    /// <summary>
    /// The next crane coming up for the car you're in (its side doors, where the bag's caught from): its board read by the
    /// lamp (Lineside.Read: dark, it's a crane you never saw coming), how far (m) and how long (s). Null off the train, or
    /// with none ahead. From the line and the train as this machine has them.
    /// </summary>
    public static (Drop Drop, double Metres, double Seconds)? MailAhead(IPlaySession s)
    {
        var p = s.Player;
        var train = s.Train;
        if (s.World.Lineside is not { } lineside || !p.Alive || p.Parent < 0 || p.Parent >= train.Frames.Count)
            return null;
        double doors = train.Cars[p.Parent].FrontDistance - train.Frames[p.Parent].Shape.HalfLength;
        (Drop Drop, double Metres, double Seconds)? next = null;
        foreach (var sign in lineside.Signs)
            if (sign is { Kind: SignKind.Drop, Drop: { } d } && lineside.Read(sign.Id) && !lineside.Passed(d.Id) && d.At >= doors - 1
                && (next is null || d.At - doors < next.Value.Metres))
            {
                double metres = Math.Max(0, d.At - doors), speed = train.Dynamics.Speed;
                next = (d, metres, speed > 0.1 ? metres / speed : double.PositiveInfinity);
            }
        return next;
    }

    /// <summary>
    /// A crane's two lines at the top of the screen: what it is, on which side (the board's arrow), and how far; and, as the
    /// board posts it, the speed the hook holds a bag at (note 727: over it, it tears), the number alone (note 671).
    /// </summary>
    public static (string Head, string Line) MailLines(Drop drop, double metres, int limitKmh = 0) =>
        (drop.Side > 0 ? "MAIL CRANE >" : "< MAIL CRANE",
            (metres >= 1000 ? $"{metres / 1000:0.0} KM" : $"{Math.Round(metres / 10) * 10:0} M") + (limitKmh > 0 ? $" / {limitKmh}" : ""));

    /// <summary>A bag hooked over the crane's speed (note 727): it tore off, and what was in it is gone.</summary>
    public const string TornLine = "THE BAG TORE: TOO FAST";

    /// <summary>A bag caught, at the top of the screen: what was in it, and what it's done.</summary>
    public static string CaughtLine(Drop drop) => drop.Kind switch
    {
        DropKind.Mail => $"MAIL: {drop.Amount:0} SCRIP",
        DropKind.Coal => "COAL: THE TENDER",
        DropKind.Ammo => "ROUNDS: THE GUNS",
        _ => "SPARES: A CAR MENDED",
    };

    /// <summary>
    /// The crosshair's word for it (note 673): in the open door on the crane's side, in its last seconds, the hook. Null
    /// anywhere else: the top of the screen has said which side.
    /// </summary>
    public static string? MailPrompt(IPlaySession s) =>
        MailAhead(s) is { } mail && mail.Seconds <= Tuning.MailLeadSeconds && s.World.Lineside!.AtTheDoor(s.Player, s.Train, mail.Drop)
            ? "HOOK THE BAG : HOLD [LMB]" : null;

    /// <summary>Brings the memory's crane up to now: a new one read, and the last one gone by (caught, where this machine knows).</summary>
    static void ObserveMail(IPlaySession s, Memory m, long now)
    {
        int ahead = MailAhead(s) is { } mail ? mail.Drop.Id : -1;
        if (ahead == m.MailDrop)
            return;
        // Lineside.Caught is the host's (solo, or hosting): a joiner's copy never hears of a catch, so it says nothing.
        if (m.MailDrop >= 0 && s.World.Lineside is { } lineside && m.MailDrop < lineside.AllDrops.Count
            && (lineside.Caught(m.MailDrop) || lineside.Torn(m.MailDrop)))
            (m.CaughtDrop, m.CaughtTick) = (m.MailDrop, now);
        (m.MailDrop, m.MailTick) = (ahead, now);
    }

    /// <summary>
    /// The crane's lines at the top centre, from <paramref name="y"/>; returns where the next line goes, and widens
    /// <paramref name="half"/> to the widest one's half width (note 764: the top left keeps out of them).
    /// </summary>
    static float MailTop(Overlay o, IPlaySession s, Memory m, float cx, float y, int line, float k, ref float half)
    {
        if (MailAhead(s) is { } mail)
        {
            // Called when the board's read; up again for its last seconds for anyone who could be at a door (not the cab).
            float a = Shown(Since(s, m.MailTick), Tuning.PlaceSeconds);
            if (mail.Seconds <= Tuning.MailLeadSeconds && !PlayerMotor.InCab(s.Player, s.Train))
                a = 1;
            if (a > 0)
            {
                int limit = s.World.Lineside!.Signs.FirstOrDefault(x => x.Drop?.Id == mail.Drop.Id)?.LimitKmh ?? 0;
                var (head, distance) = MailLines(mail.Drop, mail.Metres, limit);
                o.TextCentred(cx, y, head, Ink with { W = a }, 1);
                y += line;
                o.TextCentred(cx, y, distance, Dim with { W = a }, k);
                y += (line + 2) * k;
                half = Math.Max(half, Math.Max(o.Measure(head), o.Measure(distance, k)) / 2);
            }
        }
        if (m.CaughtDrop >= 0 && s.World.Lineside is { } lineside && m.CaughtDrop < lineside.AllDrops.Count
            && Shown(Since(s, m.CaughtTick), Tuning.PlaceSeconds) is > 0 and var c)
        {
            string caught = lineside.Torn(m.CaughtDrop) ? TornLine : CaughtLine(lineside.AllDrops[m.CaughtDrop]);
            o.TextCentred(cx, y, caught, Amber with { W = c }, 1);
            y += line + 2;
            half = Math.Max(half, o.Measure(caught) / 2);
        }
        return y;
    }
}
