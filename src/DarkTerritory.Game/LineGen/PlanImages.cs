using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Rail;

namespace DarkTerritory.Game.LineGen;

/// <summary>A plain RGBA raster to draw plots on: lines, dots, boxes and the HUD's pixel font.</summary>
public sealed class Canvas
{
    public Canvas(int width, int height, (byte R, byte G, byte B) background)
    {
        Width = width;
        Height = height;
        Pixels = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++)
            (Pixels[i * 4], Pixels[i * 4 + 1], Pixels[i * 4 + 2], Pixels[i * 4 + 3]) = (background.R, background.G, background.B, 255);
    }

    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }

    public void Set(int x, int y, (byte R, byte G, byte B) c)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height)
            return;
        int i = (y * Width + x) * 4;
        (Pixels[i], Pixels[i + 1], Pixels[i + 2]) = c;
    }

    public void Dot(int x, int y, int r, (byte, byte, byte) c)
    {
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
                if (dx * dx + dy * dy <= r * r)
                    Set(x + dx, y + dy, c);
    }

    public void Line(int x0, int y0, int x1, int y1, (byte, byte, byte) c, int thick = 0)
    {
        int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
        for (int guard = 0; guard < 20000; guard++)
        {
            if (thick > 0)
                Dot(x0, y0, thick, c);
            else
                Set(x0, y0, c);
            if (x0 == x1 && y0 == y1)
                break;
            int e2 = 2 * err;
            if (e2 >= dy)
            {
                err += dy;
                x0 += sx;
            }
            if (e2 <= dx)
            {
                err += dx;
                y0 += sy;
            }
        }
    }

    public void Rect(int x0, int y0, int x1, int y1, (byte, byte, byte) c)
    {
        for (int y = Math.Max(0, Math.Min(y0, y1)); y <= Math.Min(Height - 1, Math.Max(y0, y1)); y++)
            for (int x = Math.Max(0, Math.Min(x0, x1)); x <= Math.Min(Width - 1, Math.Max(x0, x1)); x++)
                Set(x, y, c);
    }

    public void Text(int x, int y, string text, (byte, byte, byte) c, int scale = 1)
    {
        var font = BitmapFont.Default;
        foreach (char ch in text)
        {
            var g = font.Glyph(ch);
            for (int gy = 0; gy < g.GetLength(0); gy++)
                for (int gx = 0; gx < g.GetLength(1); gx++)
                    if (g[gy, gx])
                        for (int k = 0; k < scale * scale; k++)
                            Set(x + gx * scale + k % scale, y + gy * scale + k / scale, c);
            x += font.Advance * scale;
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        PngWriter.Write(path, Pixels, Width, Height);
    }
}

/// <summary>
/// `dt linegen generate`'s pictures (plan §20.2): a top-down map (edges, junctions, pieces coloured by type, tags) and
/// a profile plot (rail height, grade, communicated speed, ideal speed, tell zones).
/// </summary>
public static class PlanImages
{
    static readonly Dictionary<string, (byte, byte, byte)> PieceColours = new()
    {
        ["climb"] = (220, 120, 60),
        ["descent"] = (90, 150, 220),
        ["summit"] = (230, 170, 80),
        ["roller"] = (170, 140, 90),
        ["drop"] = (80, 90, 230),
        ["blindThroat"] = (200, 60, 200),
        ["ledge"] = (170, 170, 170),
        ["trestle"] = (240, 220, 90),
        ["river"] = (60, 200, 230),
        ["causeway"] = (70, 170, 120),
        ["tunnel"] = (15, 15, 15),
        ["brass"] = (230, 190, 40),
        ["momentum"] = (240, 60, 60),
        ["settlement"] = (250, 250, 250),
        ["region"] = (60, 90, 60),
    };

    static readonly Dictionary<EdgeRole, (byte, byte, byte)> EdgeColours = new()
    {
        [EdgeRole.Main] = (150, 150, 155),
        [EdgeRole.Alternate] = (120, 200, 140),
        [EdgeRole.DeadLine] = (170, 90, 70),
        [EdgeRole.Spur] = (230, 150, 60),
    };

    public static void Map(LinePlan plan, RailLine line, string path, int width = 1400, int height = 1000)
    {
        var c = new Canvas(width, height, (20, 22, 26));
        var pts = new List<Double3>();
        for (double s = 0; s <= line.Length; s += 50)
            pts.Add(line.Sample(s).Position);
        foreach (var b in line.Branches)
            for (double s = 0; s <= b.Local.Length; s += 50)
                pts.Add(b.Local.Sample(s).Position);
        double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X), minZ = pts.Min(p => p.Z), maxZ = pts.Max(p => p.Z);
        double scale = Math.Min((width - 60) / Math.Max(1, maxX - minX), (height - 90) / Math.Max(1, maxZ - minZ));
        (int, int) P(Double3 p) => ((int)(30 + (p.X - minX) * scale), (int)(60 + (p.Z - minZ) * scale));
        RailLine Of(string edge) => edge == "main" ? line : line.Branches[plan.Edge(edge).Branch].Local;

        void Draw(RailLine l, double a, double b, (byte, byte, byte) col, int thick)
        {
            var (px, py) = P(l.Sample(a).Position);
            for (double s = a + 10; s <= b + 9.99; s += 10)
            {
                var (x, y) = P(l.Sample(Math.Min(s, b)).Position);
                c.Line(px, py, x, y, col, thick);
                (px, py) = (x, y);
            }
        }
        foreach (var e in plan.Alignment)
            Draw(Of(e.Edge), 0, Of(e.Edge).Length, EdgeColours[e.Role], 1);
        foreach (var piece in plan.Pieces.Where(p => p.Kind != "region"))
            if (PieceColours.TryGetValue(piece.Kind, out var col))
                Draw(Of(piece.Edge), piece.S0, piece.S1, col, 2);
        foreach (var st in plan.Structures)
            if (st.Type is StructureType.Tunnel or StructureType.Trestle or StructureType.Girder or StructureType.Truss or StructureType.Washout)
                Draw(Of(st.Edge), st.S0, st.S1, st.Type == StructureType.Tunnel ? ((byte)5, (byte)5, (byte)5) : st.Type == StructureType.Washout ? ((byte)255, (byte)0, (byte)0)
                    : st.Weak is not null ? ((byte)255, (byte)80, (byte)80) : ((byte)255, (byte)240, (byte)150), 3);
        foreach (var n in plan.Graph.Nodes)
        {
            var (x, y) = P(line.Sample(n.S).Position);
            var col = n.Type switch
            {
                NodeType.JunctionFacing => ((byte)255, (byte)255, (byte)255),
                NodeType.JunctionTrailing => ((byte)160, (byte)160, (byte)160),
                NodeType.FacilityJunction => ((byte)240, (byte)150, (byte)50),
                _ => ((byte)250, (byte)220, (byte)120),
            };
            if (n.Type == NodeType.DeadEnd)
                continue;
            c.Dot(x, y, 4, col);
            if (n.Name is { } name)
                c.Text(x + 7, y - 3, name.Split(' ')[0], col);
        }
        foreach (var poi in plan.Pois)
        {
            var (x, y) = P(line.Sample(poi.S).Position);
            c.Dot(x, y, 7, (240, 160, 60));
            c.Text(x + 9, y + 6, poi.Name, (240, 180, 90));
        }
        foreach (var l in plan.Landmarks.Where(l => l.Type is "halt" or "town"))
        {
            var (x, y) = P(Of(l.Edge).Sample(l.S0).Position);
            c.Dot(x, y, 3, (250, 250, 250));
            c.Text(x + 6, y + 4, l.Name, (200, 200, 200));
        }
        var (gx, gy) = P(line.Sample(plan.GateM).Position);
        c.Dot(gx, gy, 6, (250, 250, 250));
        var (tx, ty) = P(line.Sample(plan.TerminusM).Position);
        c.Dot(tx, ty, 6, (250, 220, 120));
        c.Text(10, 10, $"{plan.Route.Id}  D {plan.Route.D:0.00}  {plan.Route.Region}  {plan.Consist.NPlan} cars  {plan.Km(plan.TerminusM):0.0} km  g_main {plan.Consist.MainGradePct:0.00}%", (230, 230, 230), 2);
        c.Text(10, 32, $"{plan.RouteCard.Title}", (180, 180, 180), 2);
        c.Save(path);
    }

    /// <summary>The main line's profile: rail height, grade, communicated and ideal speed, and each demand's tell zone.</summary>
    public static void Profile(LinePlan plan, RailLine line, string path, IReadOnlyList<(double S, double V)>? ideal = null, int width = 1600, int height = 900)
    {
        var c = new Canvas(width, height, (20, 22, 26));
        double len = line.Length;
        int left = 60, right = width - 20;
        double X(double s) => left + (right - left) * s / len;
        // Height band.
        int hTop = 40, hBot = 330;
        double minH = double.MaxValue, maxH = double.MinValue;
        for (double s = 0; s <= len; s += 20)
        {
            double y = line.Sample(s).Position.Y;
            (minH, maxH) = (Math.Min(minH, y), Math.Max(maxH, y));
        }
        double span = Math.Max(10, maxH - minH);
        int H(double y) => (int)(hBot - (y - minH) / span * (hBot - hTop));
        foreach (var piece in plan.Pieces.Where(p => p.Edge == "main" && p.Kind != "region"))
            if (PieceColours.TryGetValue(piece.Kind, out var col))
                c.Rect((int)X(piece.S0), hBot + 4, (int)X(piece.S1), hBot + 10, col);
        int lx = (int)X(0), ly = H(line.Sample(0).Position.Y);
        for (double s = 20; s <= len; s += 20)
        {
            int x = (int)X(s), y = H(line.Sample(s).Position.Y);
            c.Line(lx, ly, x, y, (200, 200, 200));
            (lx, ly) = (x, y);
        }
        c.Text(4, hTop, $"{maxH:0}m", (160, 160, 160));
        c.Text(4, hBot - 8, $"{minH:0}m", (160, 160, 160));
        // Grade band.
        int gMid = 420, gScale = 20;
        c.Line(left, gMid, right, gMid, (60, 60, 60));
        for (double s = 0; s < len; s += 10)
        {
            double g = line.Sample(s).GradePercent;
            c.Set((int)X(s), (int)(gMid - g * gScale), g >= 0 ? ((byte)220, (byte)120, (byte)60) : ((byte)90, (byte)150, (byte)220));
        }
        c.Text(4, gMid - 4, "grade", (160, 160, 160));
        // Speed band: communicated (white), ideal (green), limits and tell zones.
        int vBot = height - 40, vTop = 500;
        double vMax = 24;
        int V(double v) => (int)(vBot - v / vMax * (vBot - vTop));
        foreach (var d in plan.Authority.Demands.Where(d => d.Edge == "main"))
        {
            c.Rect((int)X(d.TellAt), V(d.VIn) - 1, (int)X(d.SReq), V(d.VIn) + 1, (120, 60, 60));
            c.Line((int)X(d.SReq), V(0), (int)X(d.SReq), V(d.VReq), (200, 80, 80));
        }
        double prev = double.NaN;
        for (double s = 0; s < len; s += 10)
        {
            double v = Communicated(plan, s);
            if (!double.IsNaN(prev))
                c.Line((int)X(s - 10), V(prev), (int)X(s), V(v), (230, 230, 230));
            prev = v;
        }
        if (ideal is not null)
            foreach (var (s, v) in ideal)
                c.Set((int)X(s), V(v), (90, 230, 120));
        for (int k = 0; k <= 22; k += 4)
            c.Text(4, V(k) - 3, $"{k}", (140, 140, 140));
        for (double km = 0; plan.GateM + km * 1000 <= len; km += 5)
        {
            int x = (int)X(plan.GateM + km * 1000);
            c.Line(x, height - 30, x, height - 24, (140, 140, 140));
            c.Text(x - 6, height - 20, $"{km:0}", (140, 140, 140));
        }
        c.Text(10, 10, $"{plan.Route.Id}  ideal {plan.Validation.IdealTransitS / 60:0.0} min  dawn {plan.RouteCard.DawnS / 60:0.0} min  slack {plan.Validation.DawnSlackS / 60:0.0} min", (230, 230, 230), 2);
        c.Save(path);
    }

    /// <summary>The communicated speed on the main line at <paramref name="s"/> (§9.1): the least of every limit in effect.</summary>
    public static double Communicated(LinePlan plan, double s)
    {
        double v = plan.Authority.LineSpeedMs;
        foreach (var l in plan.Authority.Limits)
            if (l.Edge == "main" && s >= l.S0 && s <= l.S1)
                v = Math.Min(v, l.VMs);
        foreach (var r in plan.Authority.Restricted)
            if (r.Edge == "main" && s >= r.S0 && s <= r.S1)
                v = Math.Min(v, r.VMs);
        return v;
    }
}
