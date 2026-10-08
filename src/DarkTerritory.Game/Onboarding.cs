using System.Numerics;
using Ballast;
using Ballast.Render;

namespace DarkTerritory.Game;

/// <summary>
/// A new player's first nights (note 350; GDD §32 "Accessibility"; the director, 8 Oct 2026: "these are all quite important"),
/// kept to "learned, not told": a tip on the loading screen, and the core controls on a card in the yard for the first
/// <see cref="Data.FirstNights"/> nights. What an action does and what a creature wants are still the night's to teach.
/// </summary>
public static class Onboarding
{
    public const string File = "ui/tips.json";

    public sealed record Data
    {
        public int FirstNights { get; init; } = 3;
        public string[] Tips { get; init; } = [];
    }

    public static Data Load(string content) =>
        System.IO.File.Exists(Path.Combine(content, File)) ? DataFile.Load<Data>(Path.Combine(content, File)) : new();

    /// <summary>The tip for a night (its number in the player's nights), or null with none to show or FIRST NIGHTS off.</summary>
    public static string? Tip(Data d, Settings settings, int night) =>
        settings.FirstNights && d.Tips.Length > 0 ? d.Tips[((night % d.Tips.Length) + d.Tips.Length) % d.Tips.Length] : null;

    /// <summary>Whether the yard's card is up: FIRST NIGHTS on, and fewer of this player's nights over than the file's count.</summary>
    public static bool FirstNight(Data d, Settings settings, int nightsOver) => settings.FirstNights && nightsOver < d.FirstNights;

    /// <summary>
    /// The card's rows: the core controls, each in the prompts' form ("TALK : HOLD [V]") with the player's own keys. The
    /// cab's own, the gun's and the rest are said where you are, as ever (note 285's corner).
    /// </summary>
    public static IReadOnlyList<string> Card(Settings keys)
    {
        string K(Control c) => $"[{Controls.KeyLabel(keys.KeyFor(c))}]";
        // HOLD KEYS on TOGGLE (note 399): those are a press, not a hold.
        string Hold(Control c) => keys.ToggleHolds && HoldLatch.Latches(c) ? "" : "HOLD ";
        return
        [
            $"MOVE : {K(Control.Forward)}{K(Control.Left)}{K(Control.Back)}{K(Control.Right)}   RUN : {K(Control.Run)}   JUMP : {K(Control.Jump)}",
            $"USE : {K(Control.Use)}   SWING : {K(Control.Swing)}   LADDER : {K(Control.Ladder)}",
            $"TALK : {Hold(Control.Talk)}{K(Control.Talk)}   RADIO : {Hold(Control.Radio)}{K(Control.Radio)}",
            $"CREW : {Hold(Control.Roster)}{K(Control.Roster)}   ROUTE CARD : {K(Control.RouteCard)}   SUPPLIES : {K(Control.Supplies)}",
            "MENU : [ESC]",
        ];
    }

    /// <summary>
    /// The yard's card (note 350): its rows down the left of the frame in fine print, a heading over them, no plate (note 285:
    /// no frames in play). <paramref name="fade"/> 0..1 as it comes and goes.
    /// </summary>
    public static void DrawCard(Overlay o, int width, int height, Settings keys, float k, float fade, Vector4 ink, Vector4 heading)
    {
        if (fade <= 0)
            return;
        var rows = Card(keys);
        float row = (o.Font.LineHeight + 4) * k;
        float y = MathF.Round(height * 0.32f), x = 8;
        o.Text(x, y, "YOUR FIRST NIGHTS", heading with { W = heading.W * fade }, k);
        y += row + 2 * k;
        foreach (var r in rows)
            // A row wider than the frame (TEXT SIZE on a small window, note 351) breaks between its controls.
            foreach (var line in Fit(o, r, width - 2 * x, k))
            {
                UiStyle.Keyed(o, x, y, line, ink with { W = ink.W * fade }, k);
                y += row;
            }
    }

    /// <summary>A card row as lines no wider than <paramref name="width"/>, broken only between its controls ("   ").</summary>
    static IEnumerable<string> Fit(Overlay o, string row, float width, float k)
    {
        string line = "";
        foreach (var part in row.Split("   "))
        {
            string wider = line.Length == 0 ? part : line + "   " + part;
            if (line.Length > 0 && UiStyle.MeasureKeyed(o, wider, k) > width)
            {
                yield return line;
                line = part;
            }
            else
                line = wider;
        }
        if (line.Length > 0)
            yield return line;
    }

    /// <summary>The loading screen (the app's, while the night is built): what's being done, the line under it, and the night's tip.</summary>
    public static void DrawLoading(Overlay o, int width, int height, string doing, string under, string? tip)
    {
        o.TextCentred(width / 2f, height / 2f - 10, doing, new Vector4(0.95f, 0.7f, 0.3f, 1), scale: 2);
        o.TextCentred(width / 2f, height / 2f + 14, under, new Vector4(0.6f, 0.6f, 0.6f, 1));
        if (tip is null)
            return;
        // Wrapped to the frame's middle (TEXT SIZE, note 347, makes the frame smaller).
        float y = height - 40;
        foreach (var line in UiStyle.Wrap(o, tip.ToUpperInvariant(), width - 40))
        {
            o.TextCentred(width / 2f, y, line, new Vector4(0.8f, 0.77f, 0.68f, 1));
            y += 10;
        }
    }
}
