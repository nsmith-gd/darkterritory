using System.Reflection;

namespace Ballast.Render;

/// <summary>
/// A pixel font authored as text (Fonts/ballast-5x7.txt): each glyph is rows of '#' and '.'. Drawn a pixel per
/// font pixel into the low-res frame, so it's as crisp and as chunky as everything else on screen.
/// </summary>
public sealed class BitmapFont
{
    readonly Dictionary<char, bool[,]> _glyphs;

    BitmapFont(Dictionary<char, bool[,]> glyphs, int width, int height)
    {
        _glyphs = glyphs;
        Width = width;
        Height = height;
    }

    public int Width { get; }
    public int Height { get; }
    /// <summary>Advance per character: the glyph and one column of space.</summary>
    public int Advance => Width + 1;
    public int LineHeight => Height + 2;

    public static BitmapFont Default { get; } = Load(ReadEmbedded("Fonts/ballast-5x7.txt"));

    static string ReadEmbedded(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new FileNotFoundException("embedded font missing", name);
        return new StreamReader(stream).ReadToEnd();
    }

    public static BitmapFont Load(string text)
    {
        // Comments start "# " (a glyph row is only '#' and '.', so it never does).
        var lines = text.Replace("\r\n", "\n").Split('\n').Where(l => l.Length > 0 && !l.StartsWith("# ", StringComparison.Ordinal)).ToList();
        var glyphs = new Dictionary<char, bool[,]>();
        int width = 0, height = 0;
        for (int i = 0; i < lines.Count;)
        {
            if (!lines[i].StartsWith("char ", StringComparison.Ordinal))
                throw new InvalidDataException($"expected 'char X', found '{lines[i]}'");
            string name = lines[i][5..];
            char c = name == "space" ? ' ' : name[0];
            var rows = new List<string>();
            for (i++; i < lines.Count && !lines[i].StartsWith("char ", StringComparison.Ordinal); i++)
                rows.Add(lines[i]);
            height = rows.Count;
            width = rows.Max(r => r.Length);
            var bits = new bool[height, width];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < rows[y].Length; x++)
                    bits[y, x] = rows[y][x] == '#';
            glyphs[c] = bits;
        }
        return new BitmapFont(glyphs, width, height);
    }

    /// <summary>The glyph drawn for a character: capitals for lower case, close stand-ins for typography, '?' otherwise.</summary>
    public bool[,] Glyph(char c)
    {
        c = c switch
        {
            '—' or '–' or '−' => '-',
            '·' or '•' => '.',
            '×' => 'X',
            '“' or '”' or '„' => '"',
            '‘' or '’' => '\'',
            _ => Plain(char.ToUpperInvariant(c)),
        };
        return _glyphs.TryGetValue(c, out var g) ? g : _glyphs['?'];
    }

    /// <summary>An accented capital as its plain letter (no accents in the font): "LA DONNA È MOBILE" reads, not "LA DONNA ? MOBILE".</summary>
    static char Plain(char c) => c switch
    {
        'À' or 'Á' or 'Â' or 'Ã' or 'Ä' or 'Å' => 'A',
        'Ç' => 'C',
        'È' or 'É' or 'Ê' or 'Ë' => 'E',
        'Ì' or 'Í' or 'Î' or 'Ï' => 'I',
        'Ñ' => 'N',
        'Ò' or 'Ó' or 'Ô' or 'Õ' or 'Ö' or 'Ø' => 'O',
        'Ù' or 'Ú' or 'Û' or 'Ü' => 'U',
        'Ý' or 'Ÿ' => 'Y',
        _ => c,
    };

    public int Measure(string text, int scale = 1) => text.Length == 0 ? 0 : (text.Length * Advance - 1) * scale;
}
