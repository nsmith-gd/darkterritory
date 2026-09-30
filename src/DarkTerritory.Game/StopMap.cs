using DarkTerritory.Sim.Stops;

namespace DarkTerritory.Game;

/// <summary>
/// A stop's layout drawn top-down as an RGBA image, the way the level-design sketches are: the main line up the
/// middle, dashed track, solid roads, timber sheds, plaster houses, amber loot. How a designer (or an agent) looks at a
/// generated yard without walking it. North is up the line (the direction the train arrives in).
/// </summary>
public static class StopMap
{
    public static byte[] Render(StopLayout l, int size)
    {
        var px = new byte[size * size * 4];
        Fill(px, size, 24, 28, 24);
        // Fitted to what's there (the track, the buildings, the roads inside the zone), with a margin: s runs up the image.
        var pts = l.Buildings.Select(b => b.Centre).Concat(l.Tracks.SelectMany(t => t.Path)).Concat(l.Roads.SelectMany(r => r.Points)).Concat(l.Lairs.Select(x => x.At))
            .Where(p => p.S >= -40 && p.S <= l.ZoneLength + 40).Append(new Pt(0, 0)).ToList();
        double sMin = pts.Min(p => p.S), sMax = pts.Max(p => p.S), dMin = pts.Min(p => p.D), dMax = pts.Max(p => p.D);
        double span = Math.Max(sMax - sMin, dMax - dMin) + 60, k = size / span;
        double sMid = (sMin + sMax) / 2, dMid = (dMin + dMax) / 2, s0 = sMid - span / 2, s1 = sMid + span / 2;
        (double X, double Y) At(Pt p) => (size / 2 + (p.D - dMid) * k, size - (p.S - s0) * k);

        // Loaded cars waiting on the main line.
        for (double s = l.CutFront - l.CutLength; s < l.CutFront; s += 0.5)
            Disc(px, size, At(new Pt(s, 0)), 2.2 * k, 90, 96, 90);
        Dashed(px, size, [new Pt(s0, 0), new Pt(s1, 0)], At, 1.6 * k, 7, 5, 235, 230, 216);
        foreach (var tr in l.Tracks)
            Dashed(px, size, tr.Path, At, 0.9 * k, 3, 3, 225, 220, 205);
        foreach (var r in l.Roads)
            Line(px, size, r.Points, At, (r.Kind == RoadKind.Through ? 1.3 : 1.0) * k, r.Kind == RoadKind.Lane ? (byte)150 : (byte)225, 222, 210);
        foreach (var tr in l.Tracks)
            if (tr.Crane is { } rw)
            {
                double d = tr.FaceStart.D;
                foreach (double e in new[] { -rw.Reach, rw.Reach })
                    Line(px, size, [new Pt(rw.From, d + e), new Pt(rw.To, d + e)], At, 0.5 * k, 200, 196, 182);
            }
        // A prison car's spare siding, under it.
        foreach (var ho in l.Holdouts)
            if (ho.Siding.Count > 1)
                Dashed(px, size, ho.Siding, At, 0.9 * k, 2, 2, 150, 146, 136);
        foreach (var b in l.Buildings)
        {
            var (r, g, bl) = b.Kind switch
            {
                // The Holdouts (App. D.4): iron and soot, where everything else is timber and plaster.
                BuildingKind.PrisonCar => (92, 104, 96),
                BuildingKind.Lockup => (104, 108, 116),
                BuildingKind.SignalBox or BuildingKind.LampRoom or BuildingKind.WaterTower => (70, 66, 72),
                // The powerhouse: lit amber when it's live, grey when it's low, black when it's dead.
                BuildingKind.Powerhouse => l.Power switch { PowerState.Live => (214, 170, 60), PowerState.Low => (120, 110, 90), _ => (40, 40, 40) },
                BuildingKind.Shed => (139, 94, 58),
                BuildingKind.Hero => (176, 120, 64),
                BuildingKind.Outbuilding => (122, 88, 58),
                BuildingKind.Barn => (112, 84, 56),
                BuildingKind.Well => (109, 127, 140),
                _ => b.Variant switch { 0 => (184, 133, 124), 1 => (154, 115, 148), 2 => (125, 139, 155), 3 => (168, 143, 134), 4 => (184, 154, 106), _ => (178, 122, 120) },
            };
            if (b.Shape == HouseShape.Square)
                (r, g, bl) = (127, 138, 120);
            var parts = b.Parts.Count > 0 ? b.Parts : [new FootprintPart(0, 0, b.Length, b.Width)];
            foreach (var part in parts)
            {
                var c = World(b, part.X, part.Y);
                var pb = b with { S = c.S, D = c.D, Length = part.Length, Width = part.Width };
                Quad(px, size, Corners(pb, 0.8).Select(At).ToArray(), 10, 11, 9);
                Quad(px, size, Corners(pb, 0).Select(At).ToArray(), (byte)r, (byte)g, (byte)bl);
            }
        }
        foreach (var c in l.Containers)
        {
            var (r, g, b) = c.Zone == StopZone.Yard ? (226, 168, 75) : (240, 200, 110);
            Disc(px, size, At(c.At), Math.Max(1.5, (c.Kind is ContainerKind.CraneBay or ContainerKind.Strongroom ? 1.6 : 1.1) * k), (byte)r, (byte)g, (byte)b);
        }
        // Where the outside creatures live (B.6, B.8): rings, by who.
        foreach (var lair in l.Lairs)
        {
            var (r, g, b) = lair.Kind switch
            {
                LairKind.Warren => (120, 190, 90),
                LairKind.GauntRoost => (180, 120, 220),
                LairKind.FollowerGround => (80, 190, 190),
                LairKind.SootCall => (230, 90, 80),
                LairKind.GrumblerPerch => (240, 150, 60),
                _ => (110, 130, 230),
            };
            Ring(px, size, At(lair.At), Math.Max(3, lair.Radius * k), (byte)r, (byte)g, (byte)b);
            Disc(px, size, At(lair.At), 1.5, (byte)r, (byte)g, (byte)b);
        }
        // The Holdouts' lamps and doors, and where the consist stops (what D.4 measures from).
        foreach (var ho in l.Holdouts)
        {
            Ring(px, size, At(l.Buildings[ho.Building].Centre), 9 * k, 250, 235, 170);
            Disc(px, size, At(ho.Lamp), Math.Max(2, 1.2 * k), 255, 240, 160);
            Disc(px, size, At(ho.Door), Math.Max(1.5, 0.8 * k), 240, 240, 240);
        }
        var sp = At(l.StopPoint);
        for (int i = -5; i <= 5; i++)
        {
            Put(px, size, (int)sp.X + i, (int)sp.Y, 250, 250, 250);
            Put(px, size, (int)sp.X, (int)sp.Y + i, 250, 250, 250);
        }
        if (l.Crossing is { } x)
            Ring(px, size, At(x), 5 * k, 208, 87, 74);
        if (l.Halt is { } h)
            Quad(px, size, Corners(new StopBuilding(BuildingKind.Outbuilding, StopZone.Village, h.S, h.D, 34, 3, 0), 0).Select(At).ToArray(), 190, 185, 170);
        // A 50 m scale bar.
        for (int i = 0; i < (int)(50 * k); i++)
            Put(px, size, 14 + i, size - 14, 233, 229, 216);
        return px;
    }

    static Pt World(StopBuilding b, double x, double y)
    {
        var u = new Pt(Math.Cos(b.Yaw), Math.Sin(b.Yaw));
        return b.Centre + u * x + u.Normal * y;
    }

    static Pt[] Corners(StopBuilding b, double pad)
    {
        var u = new Pt(Math.Cos(b.Yaw), Math.Sin(b.Yaw));
        var v = u.Normal;
        double hl = b.Length / 2 + pad, hw = b.Width / 2 + pad;
        return [b.Centre + u * hl + v * hw, b.Centre - u * hl + v * hw, b.Centre - u * hl - v * hw, b.Centre + u * hl - v * hw];
    }

    static void Fill(byte[] px, int size, byte r, byte g, byte b)
    {
        for (int i = 0; i < size * size; i++)
        {
            px[i * 4] = r; px[i * 4 + 1] = g; px[i * 4 + 2] = b; px[i * 4 + 3] = 255;
        }
    }

    static void Put(byte[] px, int size, int x, int y, byte r, byte g, byte b)
    {
        if (x < 0 || y < 0 || x >= size || y >= size)
            return;
        int i = (y * size + x) * 4;
        px[i] = r; px[i + 1] = g; px[i + 2] = b; px[i + 3] = 255;
    }

    static void Disc(byte[] px, int size, (double X, double Y) c, double radius, byte r, byte g, byte b)
    {
        radius = Math.Max(radius, 0.6);
        for (int y = (int)(c.Y - radius); y <= (int)(c.Y + radius); y++)
            for (int x = (int)(c.X - radius); x <= (int)(c.X + radius); x++)
                if ((x + 0.5 - c.X) * (x + 0.5 - c.X) + (y + 0.5 - c.Y) * (y + 0.5 - c.Y) <= radius * radius)
                    Put(px, size, x, y, r, g, b);
    }

    static void Ring(byte[] px, int size, (double X, double Y) c, double radius, byte r, byte g, byte b)
    {
        for (double a = 0; a < Math.PI * 2; a += 0.5 / Math.Max(radius, 1))
            Put(px, size, (int)(c.X + Math.Cos(a) * radius), (int)(c.Y + Math.Sin(a) * radius), r, g, b);
    }

    static void Line(byte[] px, int size, IReadOnlyList<Pt> pts, Func<Pt, (double X, double Y)> at, double width, byte r, byte g, byte b)
    {
        for (int i = 1; i < pts.Count; i++)
        {
            var a = at(pts[i - 1]);
            var e = at(pts[i]);
            double len = Math.Max(1, double.Hypot(e.X - a.X, e.Y - a.Y));
            for (double t = 0; t <= len; t += 0.5)
                Disc(px, size, (a.X + (e.X - a.X) * t / len, a.Y + (e.Y - a.Y) * t / len), width / 2, r, g, b);
        }
    }

    static void Dashed(byte[] px, int size, IReadOnlyList<Pt> pts, Func<Pt, (double X, double Y)> at, double width, double on, double off, byte r, byte g, byte b)
    {
        double run = 0;
        for (int i = 1; i < pts.Count; i++)
        {
            var a = at(pts[i - 1]);
            var e = at(pts[i]);
            double len = Math.Max(0.001, double.Hypot(e.X - a.X, e.Y - a.Y));
            for (double t = 0; t <= len; t += 0.5, run += 0.5)
                if (run % (on + off) < on)
                    Disc(px, size, (a.X + (e.X - a.X) * t / len, a.Y + (e.Y - a.Y) * t / len), width / 2, r, g, b);
        }
    }

    /// <summary>Fills a convex quad (a footprint).</summary>
    static void Quad(byte[] px, int size, (double X, double Y)[] q, byte r, byte g, byte b)
    {
        double x0 = q.Min(p => p.X), x1 = q.Max(p => p.X), y0 = q.Min(p => p.Y), y1 = q.Max(p => p.Y);
        for (int y = (int)Math.Floor(y0); y <= (int)Math.Ceiling(y1); y++)
            for (int x = (int)Math.Floor(x0); x <= (int)Math.Ceiling(x1); x++)
            {
                double cx = x + 0.5, cy = y + 0.5;
                bool pos = false, neg = false;
                for (int i = 0; i < q.Length; i++)
                {
                    var a = q[i];
                    var e = q[(i + 1) % q.Length];
                    double cross = (e.X - a.X) * (cy - a.Y) - (e.Y - a.Y) * (cx - a.X);
                    pos |= cross > 0;
                    neg |= cross < 0;
                }
                if (!(pos && neg))
                    Put(px, size, x, y, r, g, b);
            }
    }
}
