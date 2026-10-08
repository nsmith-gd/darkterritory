using System.Numerics;
using Ballast.Render;

namespace DarkTerritory.Game;

/// <summary>
/// The UI's own look (GDD §28, the art checklist's "hud-look": "the few on-screen elements in the game's own style,
/// readable in VR"): the railway's enamel and iron, drawn with the overlay's solid quads alone so it stays as cheap and as
/// sharp on a headset's panel as the plain rectangles were. A panel is a plate of blackened iron, bevelled (lit along its
/// top edge, shadowed along its foot), a thin brass trim line inset, a rivet at each corner. A key is a keycap: a raised
/// enamel button with its letter dark on it. A title is a station's nameboard: cream lettering on a dark enamel board,
/// double-lined, bolted on.
/// </summary>
public static class UiStyle
{
    public static readonly Vector4 Iron = new(0.055f, 0.05f, 0.045f, 0.8f);
    public static readonly Vector4 Bevel = new(0.26f, 0.23f, 0.19f, 0.75f);
    public static readonly Vector4 Shadow = new(0, 0, 0, 0.6f);
    public static readonly Vector4 Brass = new(0.62f, 0.47f, 0.24f, 0.55f);
    public static readonly Vector4 Rivet = new(0.7f, 0.56f, 0.32f, 0.9f);
    public static readonly Vector4 Enamel = new(0.86f, 0.82f, 0.71f, 1);
    public static readonly Vector4 EnamelInk = new(0.08f, 0.07f, 0.06f, 1);
    public static readonly Vector4 Lit = new(1.00f, 0.70f, 0.30f, 1);

    /// <summary>A panel's plate: iron, bevelled, brass-trimmed, riveted. <paramref name="accent"/> lights its trim (a held slot).</summary>
    public static void Plate(Overlay o, float x, float y, float w, float h, Vector4? accent = null, float alpha = 1)
    {
        x = MathF.Round(x);
        y = MathF.Round(y);
        w = MathF.Round(w);
        h = MathF.Round(h);
        if (w < 4 || h < 4)
        {
            o.Rect(x, y, w, h, Iron with { W = Iron.W * alpha });
            return;
        }
        o.Rect(x, y, w, h, Iron with { W = Iron.W * alpha });
        o.Rect(x, y, w, 1, Bevel with { W = Bevel.W * alpha });
        o.Rect(x, y + h - 1, w, 1, Shadow with { W = Shadow.W * alpha });
        o.Rect(x + w - 1, y + 1, 1, h - 2, Shadow with { W = Shadow.W * alpha * 0.6f });
        var trim = accent ?? Brass;
        if (w > 10 && h > 10)
        {
            o.Outline(x + 2, y + 2, w - 4, h - 4, trim with { W = trim.W * alpha * (accent is null ? 1 : 0.8f) });
            foreach (var (rx, ry) in new[] { (x + 1, y + 1), (x + w - 2, y + 1), (x + 1, y + h - 2), (x + w - 2, y + h - 2) })
                o.Rect(rx, ry, 1, 1, Rivet with { W = Rivet.W * alpha });
        }
        else
            o.Rect(x + 1, y + h - 2, w - 2, 1, trim with { W = trim.W * alpha });
    }

    /// <summary>A keycap with <paramref name="key"/> on it, its top-left at (x, y); returns its width.</summary>
    public static float Keycap(Overlay o, float x, float y, string key, float scale = 1)
    {
        float w = o.Measure(key, scale) + 6 * scale, h = (o.Font.Height + 5) * scale;
        x = Snap(x, scale);
        y = Snap(y, scale);
        o.Rect(x, y + scale, w, h, Shadow);
        o.Rect(x, y, w, h - scale, Enamel with { W = 0.92f });
        o.Rect(x, y, w, scale, new Vector4(1, 0.97f, 0.88f, 0.95f));
        o.Rect(x, y + h - 2 * scale, w, scale, new Vector4(0.5f, 0.47f, 0.4f, 1));
        o.Text(x + 3 * scale, y + 2 * scale, key, EnamelInk, scale, shadow: false);
        return w;
    }

    /// <summary>
    /// TEXT BACKING (note 404) set aside while a plate's drawn: what's written on a plate, a card or a strip has its own ground,
    /// and a band a line on it would only patch it. <c>using var plate = UiStyle.OnPlate(o);</c> puts it back at the scope's end.
    /// </summary>
    public static PlateScope OnPlate(Overlay o) => new(o);

    /// <summary><see cref="OnPlate"/>'s scope.</summary>
    public readonly struct PlateScope : IDisposable
    {
        readonly Overlay _o;
        readonly Vector4 _was;

        public PlateScope(Overlay o)
        {
            (_o, _was) = (o, o.Backing);
            o.Backing = default;
        }

        public void Dispose() => _o.Backing = _was;
    }

    /// <summary>Rounds to the font's pixel at this scale (half a canvas pixel in fine print), so glyphs stay crisp.</summary>
    static float Snap(float v, float scale) => MathF.Round(v / MathF.Min(1, scale)) * MathF.Min(1, scale);

    /// <summary>
    /// Text with its keys as keycaps: each "[KEY]" in <paramref name="text"/> drawn as a keycap, the rest as text. Returns
    /// the width; <see cref="MeasureKeyed"/> measures it without drawing.
    /// </summary>
    public static float Keyed(Overlay o, float x, float y, string text, Vector4 colour, float scale = 1)
    {
        // TEXT BACKING (note 404): one band behind the whole line, keycaps and all, rather than one a piece of text.
        var backing = o.Backing;
        o.Back(x, y, MeasureKeyed(o, text, scale) - scale, scale);
        o.Backing = default;
        float at = x;
        foreach (var (part, key) in Parts(text))
            at += key ? Keycap(o, at, y - 2 * scale, part, scale) + 3 * scale : o.Text(at, y, part, colour, scale) + scale;
        o.Backing = backing;
        return at - x;
    }

    public static float MeasureKeyed(Overlay o, string text, float scale = 1) =>
        Parts(text).Sum(p => p.Key ? o.Measure(p.Text, scale) + 9 * scale : o.Measure(p.Text, scale) + scale);

    static IEnumerable<(string Text, bool Key)> Parts(string text)
    {
        int i = 0;
        while (i < text.Length)
        {
            int open = text.IndexOf('[', i);
            int close = open < 0 ? -1 : text.IndexOf(']', open + 1);
            if (open < 0 || close < 0)
            {
                yield return (text[i..], false);
                yield break;
            }
            if (open > i)
                yield return (text[i..open], false);
            yield return (text[(open + 1)..close], true);
            i = close + 1;
        }
    }

    /// <summary>The five commendations (GDD App. D.12's starter set), each a badge on the run-end screen.</summary>
    public enum Commendation : byte { CameBackForMe, HeldTheSwitch, KeptTheFire, BroughtThemHome, LastOneStanding }

    public static string Name(Commendation c) => c switch
    {
        Commendation.CameBackForMe => "CAME BACK FOR ME",
        Commendation.HeldTheSwitch => "HELD THE SWITCH",
        Commendation.KeptTheFire => "KEPT THE FIRE",
        Commendation.BroughtThemHome => "BROUGHT THEM HOME",
        _ => "LAST ONE STANDING",
    };

    // Each badge's pictogram, 11 by 11: '#' the enamel's ink, '.' the field. A hand reached down to another; a point lever
    // thrown; a flame in the firebox door; the engine's front with its lamp lit; one figure standing.
    static readonly string[][] Pictograms =
    [
        ["...........", "..##.......", "..###......", "...###.....", "....###....", ".....####..", "......#.#..", "...#..#.#..", "..####.....", ".#####.....", "..###......"],
        ["........##.", ".......###.", "......###..", ".....###...", "....###....", "...###.....", "..###......", ".###.......", "#########..", "#.......#..", "#########.."],
        ["...........", ".....#.....", "....##.....", "....###....", "...####....", "...#####...", "..###.###..", "..##...##..", "..##...##..", "...#####...", "..........."],
        ["....###....", "...#...#...", "...#.#.#...", "...#...#...", "..#######..", ".#.......#.", ".#.#####.#.", ".#.......#.", ".#########.", "..#.....#..", "..........."],
        ["....###....", "....###....", ".....#.....", "...#####...", "..#.###.#..", "....###....", "....#.#....", "....#.#....", "...##.##...", "...........", "#########.."],
    ];

    /// <summary>
    /// A commendation's badge, its top-left at (x, y), <paramref name="scale"/> pixels to the pictogram's pixel: a cream
    /// enamel roundel (an octagon in pixels), brass-rimmed, its pictogram in dark ink, a ribbon of the crew's colour under
    /// it. Returns its width.
    /// </summary>
    public static float Badge(Overlay o, float x, float y, Commendation c, int scale = 1, Vector4? ribbon = null)
    {
        int n = 11, pad = 2, size = (n + pad * 2) * scale;
        x = MathF.Round(x);
        y = MathF.Round(y);
        // The ribbon, two tails down from behind it.
        var r = ribbon ?? new Vector4(0.55f, 0.16f, 0.12f, 1);
        o.Rect(x + size * 0.2f, y + size * 0.6f, size * 0.22f, size * 0.62f, r);
        o.Rect(x + size * 0.58f, y + size * 0.6f, size * 0.22f, size * 0.62f, r * new Vector4(0.8f, 0.8f, 0.8f, 1));
        // The roundel: an octagon of rows, brass rim then enamel.
        void Octagon(float inset, Vector4 colour)
        {
            float s0 = size - 2 * inset, cut = MathF.Round(s0 * 0.29f);
            for (int row = 0; row < s0; row++)
            {
                float d = row < cut ? cut - row : row >= s0 - cut ? row - (s0 - cut - 1) : 0;
                o.Rect(x + inset + d, y + inset + row, s0 - 2 * d, 1, colour);
            }
        }
        Octagon(0, Rivet);
        Octagon(scale, Enamel);
        var pic = Pictograms[(int)c];
        for (int row = 0; row < n; row++)
            for (int col = 0; col < n; col++)
                if (pic[row][col] == '#')
                    o.Rect(x + (pad + col) * scale, y + (pad + row) * scale, scale, scale, EnamelInk);
        return size;
    }

    /// <summary>
    /// A title as a station's nameboard (the art sheet's enamel signage): the lettering cream on a dark enamel board, a
    /// cream line round it inside a dark border, bolted at its ends. Returns the board's height.
    /// </summary>
    public static float Nameboard(Overlay o, float x, float y, string title, int scale)
    {
        float tw = o.Font.Measure(title, scale), th = o.Font.Height * scale;
        float pad = 3 * scale, w = tw + pad * 4, h = th + pad * 2;
        x = MathF.Round(x);
        y = MathF.Round(y);
        o.Rect(x + 2, y + 2, w, h, Shadow);
        o.Rect(x, y, w, h, new Vector4(0.09f, 0.1f, 0.12f, 0.92f));
        o.Outline(x + scale, y + scale, w - 2 * scale, h - 2 * scale, Enamel with { W = 0.85f }, MathF.Max(1, scale / 2f));
        foreach (float bx in new[] { x + pad * 0.9f, x + w - pad * 0.9f - 2 })
        {
            o.Rect(bx, y + h / 2 - 1, 2, 2, Rivet);
        }
        o.Text(x + pad * 2, y + pad, title, Enamel, scale, shadow: false);
        return h;
    }

    /// <summary><paramref name="text"/> in lines no wider than <paramref name="width"/>, broken between words.</summary>
    public static IEnumerable<string> Wrap(Overlay o, string text, float width)
    {
        string line = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string wider = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && o.Font.Measure(wider) > width)
            {
                yield return line;
                line = word;
            }
            else
                line = wider;
        }
        if (line.Length > 0)
            yield return line;
    }
}
