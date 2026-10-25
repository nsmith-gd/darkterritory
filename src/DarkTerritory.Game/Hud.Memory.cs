using System.Numerics;
using Ballast.Render;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game;

/// <summary>
/// Crew memory on the screen (GDD §9, §11; the director, 14 Oct 2026; ARCHITECTURE §8 note 721): the crew photo as a small
/// framed print on a delivered night's end card, and the logbook's page, a full sheet in the paper's style (the route card's
/// and the town's notices', <see cref="PaperSheet"/>).
/// </summary>
public static partial class Hud
{
    /// <summary>Crew memory's numbers (memory.json): the logbook's place and reach for the prompt. The app loads the file's.</summary>
    public static MemoryTuning Remembering { get; set; } = new();

    /// <summary>The crew photo taken on this machine tonight (note 721), for the end card's print; null for none.</summary>
    [ThreadStatic] static Still? _photo;

    /// <summary>The print's size on the end card (canvas pixels).</summary>
    const int PrintWidth = 112, PrintHeight = 63;
    const string PrintCaption = "AT THE GATE";

    /// <summary>
    /// The crew photo as a print on the end card (note 721): white-bordered, a little shadow under it, at the top left
    /// (the night's corner is clear once it's over), its caption under it in fine print.
    /// </summary>
    static void Print(Overlay o, Still photo)
    {
        using var plate = UiStyle.OnPlate(o);
        float x = 12, y = 10, border = 3;
        o.Rect(x + 2, y + 2, PrintWidth + 2 * border, PrintHeight + 2 * border + 6, new Vector4(0, 0, 0, 0.45f));
        o.Rect(x, y, PrintWidth + 2 * border, PrintHeight + 2 * border + 6, new Vector4(0.9f, 0.88f, 0.82f, 1));
        o.Image(x + border, y + border, PrintWidth, PrintHeight, photo.Rgba, photo.Width, photo.Height, PrintWidth * 2, PrintHeight * 2, levels: 24);
        o.Text(Overlay.Snap(x + border + (PrintWidth - o.Measure(PrintCaption, Fine)) / 2, Fine), y + border + PrintHeight + 1, PrintCaption, PaperInk, Fine, shadow: false);
    }

    /// <summary>The yard photographer's gallery for the end card (note 1030): the latest prints as thumbnails, and how many in all.</summary>
    [ThreadStatic] static (IReadOnlyList<GalleryPrint> Prints, int Total)? _gallery;

    /// <summary>
    /// The yard photographer's gallery on a night's end card (note 1030): a row at the top right, over the headline and across
    /// from the crew photo's print, the campaign's latest prints, tonight's at the right. As many as fit beside the print
    /// at this TEXT SIZE, smaller where the headline's nearer the top. Every night's end card, or (memory.json
    /// <c>gallery.everyNight</c> off) only the one whose night ends the campaign.
    /// </summary>
    static void EndGallery(Overlay o, int width, int height, IPlaySession s, RunReport r)
    {
        var t = Remembering.Gallery;
        if (_gallery is not { Prints.Count: > 0 } g || !t.EveryNight && !(r.End == RunEnd.DawnMissed && s.World.DawnEndsCampaign))
            return;
        const float top = 8, mount = 4, gap = 4;
        int line = o.Font.LineHeight;
        float right = width - 12, left = _photo is null ? 12 : 12 + PrintWidth + 6 + 10;
        float lettering = MathF.Ceiling(line * Fine) * 2 + 4;
        float h = MathF.Min(t.CardHeight, MathF.Floor(height * 0.28f - 6 - top - lettering - mount)), w = MathF.Round(h * t.CardWidth / t.CardHeight);
        int fits = (int)((right - left + gap) / (w + mount + gap));
        if (h < 12 || fits < 1)
            return;
        var shown = g.Prints.Skip(Math.Max(0, g.Prints.Count - fits)).ToList();
        using var plate = UiStyle.OnPlate(o);
        Gallery(o, right, top, shown, g.Total, w, h, shown.Count, Fine);
    }

    /// <summary>
    /// The gallery drawn (note 1030), its top right at (<paramref name="right"/>, <paramref name="top"/>): its heading in fine
    /// print, then the prints <paramref name="w"/> × <paramref name="h"/> in rows of <paramref name="columns"/>, each on its
    /// cream mount with its night under it, the latest at the right of the last row. The menu's campaign end draws it too.
    /// Returns the y under it.
    /// </summary>
    public static float Gallery(Overlay o, float right, float top, IReadOnlyList<GalleryPrint> prints, int total, float w, float h, int columns, float fine)
    {
        if (prints.Count == 0)
            return top;
        const float border = 2, gap = 4;
        int line = o.Font.LineHeight;
        float caption = MathF.Ceiling(line * fine) + 1;
        var (at, blockW, blockH) = YardGallery.Layout(prints.Count, columns, w + 2 * border, h + 2 * border, gap, caption);
        float left = right - blockW;
        string heading = YardGallery.Heading(prints.Count, total);
        o.Text(Overlay.Snap(right - o.Measure(heading, fine), fine), top, heading, Ink, fine);
        top += MathF.Ceiling(line * fine) + 3;
        for (int i = 0; i < prints.Count; i++)
        {
            var (night, print) = prints[i];
            float x = left + at[i].X, y = top + at[i].Y;
            o.Rect(x + 1, y + 1, w + 2 * border, h + 2 * border + caption, new Vector4(0, 0, 0, 0.45f));
            o.Rect(x, y, w + 2 * border, h + 2 * border + caption, new Vector4(0.9f, 0.88f, 0.82f, 1));
            o.Image(x + border, y + border, w, h, print.Rgba, print.Width, print.Height, print.Width, print.Height, levels: 24);
            // "NIGHT 12" where it goes under the print, the number alone where it doesn't.
            string text = YardGallery.Caption(night);
            if (o.Measure(text, fine) > w)
                text = night.ToString(System.Globalization.CultureInfo.InvariantCulture);
            o.Text(Overlay.Snap(x + border + (w - o.Measure(text, fine)) / 2, fine), y + border + h + 1, text, PaperInk, fine, shadow: false);
        }
        return top + blockH;
    }

    /// <summary>
    /// The logbook open (note 721): one night's page on a sheet over the screen. Its heading (whose log), the night's number
    /// large, the line and the date, how it ended, the photo at the top right if this machine has it, the report's losses,
    /// the commendations, the net, and at its foot which page and the keys that turn it. An empty book says so.
    /// </summary>
    /// <param name="crew">The crew's name, or "" on a night that's no campaign's.</param>
    public static void LogbookPage(Overlay o, int width, int height, string crew, IReadOnlyList<LogPage> pages, int index, Still? photo)
    {
        using var plate = UiStyle.OnPlate(o);
        int line = o.Font.LineHeight;
        float w = Math.Min(width - 24, 420), h = Math.Min(height - 20, 250);
        float x = MathF.Round((width - w) / 2), y = MathF.Round((height - h) / 2);
        // The book's dark board round the page, then the page.
        o.Rect(x - 5, y - 5, w + 10, h + 10, new Vector4(0.22f, 0.1f, 0.08f, 0.96f));
        o.Rect(x, y, w, h, PaperSheet with { W = 1 });
        o.Outline(x, y, w, h, PaperRule);
        float left = x + 8, ty = y + 6;
        string whose = crew.Length > 0 ? $"THE LOG OF {crew.ToUpperInvariant()}" : "THE CREW'S LOG";
        o.Text(left, ty, whose, PaperStamp, Fine, shadow: false);
        ty += line * Fine + 4;
        string foot = pages.Count > 0 ? $"({index + 1}/{pages.Count})   TURN : [A] [D]   CLOSE : [E]" : "CLOSE : [E]";
        string footer = Bound(foot);
        float footY = y + h - 6 - line * Fine;
        UiStyle.Keyed(o, x + w - 8 - UiStyle.MeasureKeyed(o, footer, Fine), footY, footer, PaperStamp, Fine);
        if (pages.Count == 0)
        {
            foreach (var row in Wrap("NOTHING WRITTEN YET. EACH NIGHT'S PAGE GOES IN AT ITS END: THE CLERK'S REPORT, THE COMMENDATIONS, THE NET.", Math.Max(16, (int)((w - 16) / o.Font.Advance))))
            {
                o.Text(left, ty, row, PaperInk, shadow: false);
                ty += line;
            }
            return;
        }
        var page = pages[Math.Clamp(index, 0, pages.Count - 1)];
        // The photo at the top right, pasted in, its own white border.
        float photoW = photo is null ? 0 : MathF.Min(168, w * 0.42f), photoH = photoW * 9 / 16;
        float photoX = x + w - 8 - photoW, photoBottom = ty + photoH + 6;
        if (photo is not null)
        {
            o.Rect(photoX - 2, ty - 2, photoW + 4, photoH + 4, new Vector4(0.95f, 0.94f, 0.9f, 1));
            o.Image(photoX, ty, photoW, photoH, photo.Rgba, photo.Width, photo.Height, (int)photoW * 2, (int)photoH * 2, levels: 24);
        }
        o.Text(left, ty, $"NIGHT {page.Night}", PaperStamp, 2, shadow: false);
        ty += line * 2 + 2;
        // Each row as wide as the page leaves it: beside the photo, narrower.
        void Row(string text, Vector4 colour, string indent = "")
        {
            bool first = true;
            float room = (ty < photoBottom ? photoX - 8 : x + w - 8) - left;
            foreach (var part in Wrap(text, Math.Max(12, (int)(room / o.Font.Advance) - indent.Length)))
            {
                if (ty > footY - line)
                    return;
                o.Text(left, ty, first ? part : indent + part, colour, shadow: false);
                ty += line;
                first = false;
            }
        }
        Row(page.Route.ToUpperInvariant(), PaperInk);
        if (page.Date.Length > 0)
            Row(page.Date.ToUpperInvariant(), PaperRule with { W = 1 });
        ty += 3;
        Row(page.Line, PaperInk);
        ty = Math.Max(ty + 4, photo is null ? 0 : photoBottom);
        if (page.Losses.Count > 0)
        {
            Row("THE INCIDENT REPORT", PaperStamp);
            foreach (var loss in page.Losses)
                Row(loss.ToUpperInvariant(), PaperInk, "  ");
            ty += 3;
        }
        if (page.Commendations.Count > 0)
        {
            Row("COMMENDED", PaperStamp);
            foreach (var c in page.Commendations)
                Row(c.ToUpperInvariant(), PaperInk, "  ");
            ty += 3;
        }
        Row($"NET {page.Net:0} SCRIP", PaperInk);
    }
}
