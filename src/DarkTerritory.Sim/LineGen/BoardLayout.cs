namespace DarkTerritory.Sim.LineGen;

/// <summary>
/// A board's face as it's lettered (note 560): its words broken into <see cref="Lines"/>, the lettering's
/// <see cref="Pixel"/> (one of the font's pixels, m), and the face's <see cref="Width"/> and <see cref="Height"/> (m), the
/// margin round the words included.
/// </summary>
public sealed record BoardFace(IReadOnlyList<string> Lines, double Pixel, double Width, double Height, double Margin);

/// <summary>
/// How a lineside board's words are laid out on its face (signage.json, note 560): the sim stands the board and keeps its
/// sight lines clear (<see cref="Run.LinesideProps"/>), the art letters it (SignKit), and both size it here, alike.
/// The lettering is the HUD's 5x7 pixel font (Ballast.Render.BitmapFont.Default; SignKitTests holds the two together).
/// </summary>
public static class BoardLayout
{
    /// <summary>The font's glyph (pixels): 5 across, 7 up, 6 to the next character, 9 to the next line.</summary>
    public const int GlyphWidth = 5, GlyphHeight = 7, Advance = 6, LineHeight = 9;

    /// <summary>The face of a board of <paramref name="type"/> reading <paramref name="text"/>.</summary>
    public static BoardFace Of(string type, string text, BoardDef? def, BillboardRules rules)
    {
        def ??= new BoardDef(text, 3.5, 1, 150, true);
        var lines = Lines(type, text.ToUpperInvariant(), Math.Max(1, def.LineChars));
        int widest = Math.Max(1, lines.Max(l => l.Length));
        double px = Math.Max(0.01, def.LetterM) / GlyphHeight;
        double margin = rules.MarginLetters * def.LetterM;
        if (type == "tunnelPlate")
        {
            // Cut over its portal's arch: as big as fits the width it has.
            margin = Math.Min(margin, 0.3);
            px = Math.Min(px, (def.WidthM - 2 * margin) / (widest * Advance - 1));
        }
        double textW = (widest * Advance - 1) * px, textH = (lines.Count * LineHeight - 2) * px;
        double width = Math.Max(type == "tunnelPlate" ? 0 : def.WidthM, textW + 2 * margin);
        return new BoardFace(lines, px, width, textH + 2 * margin, margin);
    }

    /// <summary>The words on a board, broken into lines of at most <paramref name="chars"/>: a name above its figure, a long name over two.</summary>
    public static List<string> Lines(string type, string text, int chars)
    {
        // A tunnel's plate: its number over its name ("TUNNEL 2 - BLACKWELL", §13.3), as it's called on the radio.
        if (type == "tunnelPlate" && text.Split(" - ", 2) is [var number, var name])
            return [number, name];
        var split = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (split.Length <= 1 || type is "kmPost" or "minorPost" or "speedBoard" or "resumeBoard")
            return [text];
        // A distance keeps its unit ("2.0 KM" isn't broken).
        var words = new List<string>();
        foreach (var w in split)
            if (w == "KM" && words.Count > 0)
                words[^1] += " " + w;
            else
                words.Add(w);
        var lines = new List<string>();
        string current = "";
        foreach (var w in words)
        {
            if (current.Length > 0 && current.Length + 1 + w.Length > chars)
            {
                lines.Add(current);
                current = w;
            }
            else
                current = current.Length == 0 ? w : current + " " + w;
        }
        lines.Add(current);
        return lines;
    }
}
