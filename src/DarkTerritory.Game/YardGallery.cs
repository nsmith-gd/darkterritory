using Ballast.Render;
using DarkTerritory.Sim.Campaign;

namespace DarkTerritory.Game;

/// <summary>A night's print in the gallery (note 1030): which night, and the print (full size, or a thumbnail of it).</summary>
public readonly record struct GalleryPrint(int Night, Still Print);

/// <summary>
/// The yard photographer's gallery (docs/design/upgrades.md §4: "the campaign's end card shows every one"; ARCHITECTURE §8
/// note 1030): his print of the crew leaving the yard, a night each (<see cref="YardPhotographer"/>, note 714), gathered from
/// the crew's logbook as this machine kept it (<see cref="LogPage.Yard"/>) with tonight's, shown on the night's end card and
/// the campaign's end, and laid out on one sheet in the night's folder. Presentation only: files read back, thumbnails cut.
/// </summary>
public sealed class YardGallery
{
    /// <summary>The sheet's file name in a night's folder: beside the night's own print, before the bookmarks.</summary>
    public const string SheetName = "00 yard gallery.png";

    readonly Dictionary<string, Still?> _read = [];
    readonly Dictionary<(Still, int, int), Still> _thumbs = [];

    /// <summary>
    /// The nights' print files from a logbook, in night order: each page that has one (a night without the photographer,
    /// or one this machine joined on the way, has none).
    /// </summary>
    public static IReadOnlyList<(int Night, string File)> Files(IReadOnlyList<LogPage> pages) =>
        [.. pages.Where(p => p.Yard.Length > 0).OrderBy(p => p.Night).DistinctBy(p => p.Night).Select(p => (p.Night, p.Yard))];

    /// <summary>
    /// The gallery: the logbook's prints read back from disk (cached; one that's gone is left out), and tonight's, as this
    /// machine took it, in its night's place (in place of an earlier copy of that night's), in night order.
    /// </summary>
    public IReadOnlyList<GalleryPrint> Prints(IReadOnlyList<LogPage> pages, int tonight = 0, Still? print = null)
    {
        var prints = new List<GalleryPrint>();
        foreach (var (night, file) in Files(pages))
            if (!(print is not null && night == tonight) && Read(file) is { } still)
                prints.Add(new GalleryPrint(night, still));
        if (print is not null)
            prints.Add(new GalleryPrint(tonight, print));
        return [.. prints.OrderBy(p => p.Night)];
    }

    /// <summary>A kept print read back (cached by its file), or null when it's gone.</summary>
    public Still? Read(string file)
    {
        if (!_read.TryGetValue(file, out var still))
            _read[file] = still = BookmarkAlbum.ReadPhoto(file);
        return still;
    }

    /// <summary>
    /// The prints as thumbnails <paramref name="w"/> × <paramref name="h"/> (cut once and kept, so a frame's drawing reads a
    /// pixel a cell rather than the whole print), the latest <paramref name="most"/> of them.
    /// </summary>
    public IReadOnlyList<GalleryPrint> Thumbs(IReadOnlyList<GalleryPrint> prints, int w, int h, int most)
    {
        var list = new List<GalleryPrint>();
        foreach (var p in prints.Skip(Math.Max(0, prints.Count - Math.Max(1, most))))
        {
            if (!_thumbs.TryGetValue((p.Print, w, h), out var thumb))
                _thumbs[(p.Print, w, h)] = thumb = BookmarkStills.Shrink(p.Print.Rgba, p.Print.Width, p.Print.Height, w, h);
            list.Add(p with { Print = thumb });
        }
        return list;
    }

    /// <summary>
    /// Where each of <paramref name="count"/> prints goes in a block <paramref name="columns"/> wide, from its top left: rows
    /// of <paramref name="w"/> × <paramref name="h"/> cells <paramref name="gap"/> apart, each with <paramref name="caption"/>
    /// under it for its night; a short last row is set to the right (the latest, tonight's, at the block's right-hand edge,
    /// the corner it's anchored on). And the block's size.
    /// </summary>
    public static (IReadOnlyList<(float X, float Y)> At, float Width, float Height) Layout(int count, int columns, float w, float h, float gap, float caption)
    {
        if (count <= 0)
            return ([], 0, 0);
        columns = Math.Clamp(columns, 1, count);
        int rows = (count + columns - 1) / columns;
        float width = columns * w + (columns - 1) * gap, height = rows * (h + caption) + (rows - 1) * gap;
        var at = new List<(float, float)>(count);
        // Filled from the end, so the first row is the short one and the newest row is full.
        int shortRow = count - (rows - 1) * columns;
        for (int i = 0; i < count; i++)
        {
            int row = i < shortRow ? 0 : 1 + (i - shortRow) / columns;
            int col = i < shortRow ? columns - shortRow + i : (i - shortRow) % columns;
            at.Add((col * (w + gap), row * (h + caption + gap)));
        }
        return (at, width, height);
    }

    /// <summary>The heading over it: "LEAVING THE YARD", and when there are more than shown, how many in all.</summary>
    public static string Heading(int shown, int total) => total > shown ? $"LEAVING THE YARD: THE LAST {shown} OF {total}" : "LEAVING THE YARD";

    /// <summary>A print's caption: its night.</summary>
    public static string Caption(int night) => night > 0 ? $"NIGHT {night}" : "TONIGHT";

    /// <summary>
    /// Every print on one sheet, full size (note 1030), for the night's folder: on cream card, <see cref="GalleryTuning.SheetColumns"/>
    /// to a row in night order, each with its night lettered under it, and the crew's name over them. Null with none.
    /// </summary>
    public static Still? Sheet(IReadOnlyList<GalleryPrint> prints, string crew, GalleryTuning t)
    {
        if (prints.Count == 0)
            return null;
        var font = BitmapFont.Default;
        // The crew's name over them lettered at three of the font's pixels to one, the nights at two.
        const int letter = 2, heading = 3;
        int pw = prints.Max(p => p.Print.Width), ph = prints.Max(p => p.Print.Height), m = Math.Max(4, t.SheetMargin);
        int columns = Math.Clamp(t.SheetColumns, 1, prints.Count), rows = (prints.Count + columns - 1) / columns;
        int line = font.LineHeight * letter, title = font.LineHeight * heading + m;
        int width = columns * (pw + m) + m, height = title + rows * (ph + line + m) + m;
        var rgba = new byte[width * height * 4];
        Fill(rgba, width, 0, 0, width, height, (226, 219, 198));
        string whose = crew.Length > 0 ? $"THE YARD PHOTOGRAPHER: {crew}" : "THE YARD PHOTOGRAPHER";
        Letter(rgba, width, font, m, m, whose.ToUpperInvariant(), heading, (40, 30, 24));
        for (int i = 0; i < prints.Count; i++)
        {
            var (night, print) = prints[i];
            int x = m + i % columns * (pw + m), y = title + m + i / columns * (ph + line + m);
            // A dark edge a pixel round each, as a print laid on the card.
            Fill(rgba, width, x - 1, y - 1, print.Width + 2, print.Height + 2, (70, 56, 44));
            for (int row = 0; row < print.Height; row++)
                print.Rgba.AsSpan(row * print.Width * 4, print.Width * 4).CopyTo(rgba.AsSpan(((y + row) * width + x) * 4));
            Letter(rgba, width, font, x, y + print.Height + letter * 2, Caption(night), letter, (70, 56, 44));
        }
        return new Still(width, height, rgba);
    }

    /// <summary>
    /// Writes the sheet into the night's folder (<see cref="SheetName"/>); the file, or null with no prints or when the disk
    /// won't have it (said, and the night goes on).
    /// </summary>
    public static string? Save(string night, IReadOnlyList<GalleryPrint> prints, string crew, GalleryTuning t)
    {
        if (Sheet(prints, crew, t) is not { } sheet)
            return null;
        try
        {
            string path = Path.Combine(night, SheetName);
            PngWriter.Write(path, sheet.Rgba, sheet.Width, sheet.Height);
            return path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"yard photographer: couldn't keep the gallery in {night} ({e.Message})");
            return null;
        }
    }

    static void Fill(byte[] rgba, int width, int x, int y, int w, int h, (byte R, byte G, byte B) c)
    {
        int height = rgba.Length / 4 / width;
        for (int py = Math.Max(0, y); py < Math.Min(height, y + h); py++)
            for (int px = Math.Max(0, x); px < Math.Min(width, x + w); px++)
            {
                int i = (py * width + px) * 4;
                (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]) = (c.R, c.G, c.B, 255);
            }
    }

    static void Letter(byte[] rgba, int width, BitmapFont font, int x, int y, string text, int scale, (byte R, byte G, byte B) c)
    {
        foreach (char ch in text)
        {
            var glyph = font.Glyph(ch);
            for (int gy = 0; gy < glyph.GetLength(0); gy++)
                for (int gx = 0; gx < glyph.GetLength(1); gx++)
                    if (glyph[gy, gx])
                        Fill(rgba, width, x + gx * scale, y + gy * scale, scale, scale, c);
            x += font.Advance * scale;
            if (x >= width)
                return;
        }
    }
}
