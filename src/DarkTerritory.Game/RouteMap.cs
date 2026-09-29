using Ballast;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;

namespace DarkTerritory.Game;

/// <summary>
/// Top-down map and elevation profile of a route, as an RGBA image: how a designer (or an agent)
/// looks at a generated line without flying along it.
/// </summary>
public static class RouteMap
{
    public static byte[] Render(Route route, int width, int height)
    {
        var px = new byte[width * height * 4];
        Fill(px, width, height, 22, 24, 28);
        var line = route.Build();
        int mapH = height * 2 / 3, profileTop = mapH + 10;

        // Fit the plan view.
        var pts = Enumerable.Range(0, 801).Select(i => line.Sample(line.Length * i / 800)).ToList();
        double minX = pts.Min(p => p.Position.X), maxX = pts.Max(p => p.Position.X);
        double minZ = pts.Min(p => p.Position.Z), maxZ = pts.Max(p => p.Position.Z);
        double scale = Math.Min((width - 40) / Math.Max(1, maxX - minX), (mapH - 40) / Math.Max(1, maxZ - minZ));
        (int, int) Plan(Double3 p) => ((int)(20 + (p.X - minX) * scale), (int)(20 + (p.Z - minZ) * scale));

        void Track(double from, double to, byte r, byte g, byte b, int thick)
        {
            for (double s = from; s < to; s += Math.Max(1, line.Length / 4000))
            {
                var (x, y) = Plan(line.Sample(s).Position);
                Dot(px, width, height, x, y, thick, r, g, b);
            }
        }

        Track(0, line.Length, 120, 120, 125, 1);
        foreach (var f in route.Features)
        {
            switch (f.Kind)
            {
                case FeatureKind.Tunnel: Track(f.Start, f.End, 10, 10, 10, 3); break;
                case FeatureKind.Bridge: Track(f.Start, f.End, f.MaxCars > 0 ? (byte)230 : (byte)90, 140, 230, 3); break;
                case FeatureKind.Grease: Track(f.Start, f.End, 90, 200, 80, 2); break;
                case FeatureKind.Sleepers: { var (x, y) = Plan(line.Sample(f.Start).Position); Dot(px, width, height, x, y, 3, 220, 50, 50); break; }
                case FeatureKind.Junction: { var (x, y) = Plan(line.Sample(f.Start).Position); Dot(px, width, height, x, y, 3, 200, 200, 200); break; }
                case FeatureKind.Facility: { var (x, y) = Plan(line.Sample((f.Start + f.End) / 2).Position); Dot(px, width, height, x, y, 6, 240, 160, 60); break; }
            }
        }
        var (sx, sy) = Plan(line.Sample(0).Position);
        Dot(px, width, height, sx, sy, 7, 250, 250, 250);
        var (ex, ey) = Plan(line.Sample(line.Length).Position);
        Dot(px, width, height, ex, ey, 7, 250, 220, 120);

        // Elevation profile along the bottom.
        double minY = pts.Min(p => p.Position.Y), maxY = pts.Max(p => p.Position.Y);
        int profileH = height - profileTop - 10;
        for (int x = 0; x < width; x++)
        {
            var p = line.Sample(line.Length * x / (width - 1));
            int y = profileTop + profileH - (int)((p.Position.Y - minY) / Math.Max(1, maxY - minY) * profileH);
            bool tunnel = route.InTunnel(p.Distance);
            byte shade = (byte)(Math.Abs(p.GradePercent) >= 2.5 ? 230 : Math.Abs(p.GradePercent) >= 1.5 ? 170 : 110);
            for (int yy = y; yy < profileTop + profileH; yy++)
                Set(px, width, height, x, yy, tunnel ? (byte)40 : shade, tunnel ? (byte)40 : (byte)(shade * 0.8), tunnel ? (byte)40 : (byte)(shade * 0.6));
        }
        return px;
    }

    static void Fill(byte[] px, int w, int h, byte r, byte g, byte b)
    {
        for (int i = 0; i < w * h; i++)
        {
            px[i * 4] = r;
            px[i * 4 + 1] = g;
            px[i * 4 + 2] = b;
            px[i * 4 + 3] = 255;
        }
    }

    static void Dot(byte[] px, int w, int h, int cx, int cy, int radius, byte r, byte g, byte b)
    {
        for (int y = cy - radius / 2; y <= cy + radius / 2; y++)
            for (int x = cx - radius / 2; x <= cx + radius / 2; x++)
                Set(px, w, h, x, y, r, g, b);
    }

    static void Set(byte[] px, int w, int h, int x, int y, byte r, byte g, byte b)
    {
        if (x < 0 || y < 0 || x >= w || y >= h)
            return;
        int i = (y * w + x) * 4;
        px[i] = r;
        px[i + 1] = g;
        px[i + 2] = b;
    }
}
