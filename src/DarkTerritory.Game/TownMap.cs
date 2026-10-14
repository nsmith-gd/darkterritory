using DarkTerritory.Game.LineGen;
using DarkTerritory.Sim.Towns;

namespace DarkTerritory.Game;

/// <summary>
/// A town's plan drawn top-down (queue #424, note 692; the director, 13 Oct: "give me renders top down of the layouts of the
/// towns as you're working on it"), the way the director's town sketches are: the line along the middle, the way in from
/// the last night on the left, the way out to the next on the right, the wall round it, its quarters, streets, lanes and
/// houses. Drawn from the plan alone (no window), in the town's own rail frame: S along the line to the right, D across it,
/// the line's right (+D) down the image, as a map read facing the way out.
/// </summary>
public static class TownMap
{
    static readonly (byte, byte, byte) Paper = (23, 25, 27), Ground = (52, 50, 44), Street = (120, 112, 96), Lane = (88, 76, 60),
        WallInk = (176, 170, 158), Rail = (226, 220, 204), Lived = (196, 150, 88), Shut = (118, 108, 96), Lost = (70, 60, 54),
        Open = (240, 200, 110), Square = (96, 88, 74), Green = (58, 86, 52), Works = (110, 72, 52), People = (240, 236, 220),
        Label = (220, 214, 198), Plot = (62, 76, 40), Hard = (84, 80, 72), Civic = (150, 136, 112), Glass = (150, 170, 150), Piece = (140, 120, 100), Gate = (232, 168, 64), Water = (34, 54, 72), Deck = (124, 98, 70);

    /// <summary>Draws <paramref name="plan"/> to a PNG at <paramref name="path"/>, <paramref name="width"/> pixels wide.</summary>
    public static void Save(TownPlan plan, string path, int width = 1800)
    {
        var b = plan.Bounds;
        var fx = plan.Frame;
        // What's there: the wall, or the houses and the square where there's none, and the gates.
        double s0 = b?.Rear ?? plan.Houses.Select(h => h.S).Append(plan.Square.S0).Min() - 30;
        double s1 = b?.Gate ?? plan.Gate + 20;
        double dl = b?.Left ?? Math.Max(40, plan.Houses.Select(h => -h.D).Append(-plan.Square.WallD).Max() + 15);
        double dr = b?.Right ?? Math.Max(40, plan.Houses.Select(h => h.D).Append(plan.Square.WallD).Max() + 15);
        // Out to the land the town shapes past its wall (its margin), or a little past its wall.
        double margin = Math.Max(40, plan.Margin);
        s0 -= margin;
        s1 += margin;
        dl += margin;
        dr += margin;
        double k = width / (s1 - s0);
        int height = Math.Max(200, (int)Math.Ceiling((dl + dr) * k)) + 40;
        var c = new Canvas(width, height, Paper);
        (double X, double Y) At(double s, double d) => ((s - s0) * k, 40 + (d + dl) * k);

        void Poly(IReadOnlyList<(double S, double D)> pts, (byte, byte, byte) col)
        {
            var px = pts.Select(p => At(p.S, p.D)).ToArray();
            int y0 = Math.Max(0, (int)Math.Floor(px.Min(p => p.Y))), y1 = Math.Min(height - 1, (int)Math.Ceiling(px.Max(p => p.Y)));
            var xs = new List<double>();
            for (int y = y0; y <= y1; y++)
            {
                double yc = y + 0.5;
                xs.Clear();
                for (int i = 0; i < px.Length; i++)
                {
                    var (a, bb) = (px[i], px[(i + 1) % px.Length]);
                    if ((a.Y <= yc) != (bb.Y <= yc))
                        xs.Add(a.X + (yc - a.Y) / (bb.Y - a.Y) * (bb.X - a.X));
                }
                xs.Sort();
                for (int i = 0; i + 1 < xs.Count; i += 2)
                    for (int x = Math.Max(0, (int)Math.Round(xs[i])); x < Math.Min(width, (int)Math.Round(xs[i + 1])); x++)
                        c.Set(x, y, col);
            }
        }
        void Box(double sa, double sb, double da, double db, (byte, byte, byte) col) =>
            Poly([(sa, da), (sb, da), (sb, db), (sa, db)], col);
        void Seg(double sa, double da, double sb, double db, (byte, byte, byte) col, double metres)
        {
            var (xa, ya) = At(sa, da);
            var (xb, yb) = At(sb, db);
            c.Line((int)xa, (int)ya, (int)xb, (int)yb, col, Math.Max(0, (int)Math.Round(metres * k / 2)));
        }
        void Text(double s, double d, string text, (byte, byte, byte) col, int scale = 2)
        {
            var (x, y) = At(s, d);
            int w = text.Length * BitmapFontAdvance * scale;
            c.Text((int)Math.Clamp(x - w / 2.0, 4, width - w - 4), (int)y - 4 * scale, text, col, scale);
        }

        // The ground: a town type's as a shaded relief (its levels tinted, lit from the north-west, its water by depth, the
        // land it stands in out to its margin fading as it eases into the line's: note 692), else the yard's level inside the wall.
        var relief = TownGround.Of(plan);
        if (!relief.Flat)
        {
            double lo = -20, hi = 30;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    double s = s0 + x / k, d = (y - 40) / k - dl;
                    double w = relief.Blend(s, d);
                    if (w <= 0)
                        continue;
                    (byte, byte, byte) col;
                    if (relief.Water(s, d) is { } wl && !relief.Deck(s, d))
                    {
                        double depth = Math.Clamp((wl - relief.Bed(s, d)) / 6, 0, 1);
                        col = Mix(Water, (16, 28, 42), depth);
                    }
                    else
                    {
                        double h = relief.Level(s, d), t = Math.Clamp((h - lo) / (hi - lo), 0, 1);
                        double gx = relief.Level(s + 1.5, d) - relief.Level(s - 1.5, d), gy = relief.Level(s, d + 1.5) - relief.Level(s, d - 1.5);
                        double light = Math.Clamp(1 + (-gx - gy) * 0.08, 0.45, 1.5);
                        var tint = t < 0.4 ? Mix((40, 38, 36), Ground, t / 0.4) : Mix(Ground, (150, 140, 112), (t - 0.4) / 0.6);
                        col = Scale(relief.Deck(s, d) ? Deck : tint, light);
                    }
                    c.Set(x, y, Mix(Paper, col, w));
                }
        }
        else if (b?.Outline is { } outline)
            Poly(outline, Ground);
        else if (b is not null)
            Box(b.Rear, b.Gate, -b.Left, b.Right, Ground);
        foreach (var d in plan.Districts.Where(d => d.Kind is DistrictKind.Plots or DistrictKind.Yard))
            Poly(d.Outline, d.Kind == DistrictKind.Plots ? Plot : Hard);
        if (relief.Flat)
            foreach (var d in plan.Districts.OrderBy(d => d.Kind switch { DistrictKind.Water => 0, DistrictKind.Deck or DistrictKind.Stage => 2, _ => 1 }))
                Poly(d.Outline, d.Kind switch { DistrictKind.Water => Water, DistrictKind.Deck or DistrictKind.Stage => Deck, _ => Shade(Ground, d.Level) });
        // The walls the ground needs (retaining walls, rails at deep water), thin and pale.
        foreach (var (es0, ed0, es1, ed1, _, _) in relief.Edges())
            Seg(es0, ed0, es1, ed1, (190, 182, 160), 0.5);
        Box(plan.Square.S0, plan.Square.S1, 0, plan.Square.WallD, Square);
        if (plan.Green is { } g)
            Box(g.S0, g.S1, g.Side * g.Near, g.Side * g.Far, Green);
        if (plan.Works is { } wk)
            Box(wk.S0, wk.S1, wk.Side * wk.Near, wk.Side * wk.Far, Works);
        if (b is not null)
        {
            foreach (var st in b.Streets)
                for (double s = st.S0; s < st.S1; s += 4)
                    Seg(s, st.At(s), Math.Min(st.S1, s + 4), st.At(Math.Min(st.S1, s + 4)), Street, st.Width);
            foreach (var ln in b.Lanes)
            {
                double da = ln.Kinks is { Count: > 0 } kk ? kk[0].D : ln.D0, db = ln.Kinks is { Count: > 0 } k2 ? k2[^1].D : ln.D1;
                for (double d = da; d < db; d += 2)
                    Seg(ln.At(d), d, ln.At(Math.Min(db, d + 2)), Math.Min(db, d + 2), Lane, ln.Width);
            }
        }
        foreach (var w in plan.Ways)
            for (int i = 1; i < w.Points.Count; i++)
                Seg(w.Points[i - 1].S, w.Points[i - 1].D, w.Points[i].S, w.Points[i].D, w.Deck ? Deck : Street, w.Width);

        // The houses, their bodies as they stand (turned), by what they are now.
        foreach (var h in plan.Houses)
        {
            var col = h.Kind switch { HouseKind.Lived => Lived, HouseKind.Open => Open, HouseKind.Empty => Shut, _ => Lost };
            foreach (var (u0, u1, v0, v1, _) in h.Parts())
                Poly([h.Body(u0, v0), h.Body(u1, v0), h.Body(u1, v1), h.Body(u0, v1)], col);
        }
        foreach (var bd in plan.Buildings)
            Box(bd.S - bd.Length / 2, bd.S + bd.Length / 2, bd.D - bd.Depth / 2, bd.D + bd.Depth / 2, Civic);
        foreach (var f in plan.Fixtures.Where(f => f.House < 0 && f.SolidS > 0))
            Box(f.S - f.SolidS, f.S + f.SolidS, f.D - f.SolidD, f.D + f.SolidD, f.Kind is "glasshouse" ? Glass : Piece);
        foreach (var p in plan.People.Where(p => p.House < 0))
        {
            var (x, y) = At(p.S, p.D);
            c.Dot((int)x, (int)y, 2, People);
        }

        // The wall, its towers and its two gates: in from the last night, out to the next.
        if (b is not null)
        {
            // The wall as the Sim stands it (Fortresses.Round): its runs, gaps for the gatehouses, its towers.
            foreach (var (a, z) in Sim.Run.Fortresses.Runs(b))
                Seg(a.S, a.D, z.S, z.D, WallInk, 1.6);
            foreach (var (s, d) in Sim.Run.Fortresses.Towers(b))
                Box(s - 2.7, s + 2.7, d - 2.7, d + 2.7, WallInk);
        }
        // The line through the town, on past each gate, a tie every 6 m.
        Seg(s0, 0, s1, 0, Rail, 1.4);
        for (double s = Math.Ceiling(s0 / 6) * 6; s < s1; s += 6)
            Seg(s, -1.4, s, 1.4, Rail, 0.3);
        foreach (var (s, name) in new[] { (fx.Entry, "IN FROM THE LAST NIGHT"), (fx.Exit, "OUT TO THE NEXT") })
        {
            Box(s - 3.5, s + 3.5, -8.5, -3.5, Gate);
            Box(s - 3.5, s + 3.5, 3.5, 8.5, Gate);
            Text(s, -(b?.Left ?? 30) - 16, name, Gate);
        }
        // The yard the train stands in at the start of a night: the longest consist back from the way out (note 692).
        Seg(fx.Yard0, 4.5, fx.Yard1, 4.5, (90, 150, 200), 1.2);
        Text((fx.Yard0 + fx.Yard1) / 2, 12, $"YARD {fx.Yard1 - fx.Yard0:0} M", (120, 170, 220), 1);

        // The quarters' names.
        foreach (var d in plan.Districts.Where(d => d.Name.Length > 0))
        {
            double ms = d.Outline.Average(p => p.S), md = d.Outline.Average(p => p.D);
            // Off the line: a quarter round it (a marsh, a dyke) is named out on its far side; a terrace with another inside
            // it (a quarry's benches) along its edge nearest the line.
            if (Math.Abs(md) < 20)
                md = d.Outline.Max(p => p.D) * 0.8;
            else if (plan.Districts.Any(o => o != d && o.Name.Length > 0 && d.Holds(o.Outline.Average(p => p.S), o.Outline.Average(p => p.D))))
                md = md < 0 ? d.Outline.Max(p => p.D) - 10 : d.Outline.Min(p => p.D) + 10;
            Text(ms, md, d.Name.ToUpperInvariant(), Label, 2);
        }
        c.Text(16, 12, $"{plan.Name.ToUpperInvariant()}  -  {FormName(plan.Form)}  -  POP {plan.Population}  -  {plan.Houses.Count} HOUSES  -  {(s1 - s0 - 2 * margin):0} x {(dl + dr - 2 * margin):0} M", Label, 2);
        // A scale bar: 100 m.
        Seg(s1 - margin - 100, dr - 20, s1 - margin, dr - 20, Label, 1);
        Text(s1 - margin - 50, dr - 10, "100 M", Label, 1);
        c.Save(path);
    }

    const int BitmapFontAdvance = 6;
    /// <summary>The gatehouse's opening either side of the line (Fortresses.GateOuter).</summary>
    const double GateGap = 8.5;

    static (byte, byte, byte) Mix((byte R, byte G, byte B) a, (byte R, byte G, byte B) b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return ((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    }

    static (byte, byte, byte) Scale((byte R, byte G, byte B) c, double f) =>
        ((byte)Math.Min(255, c.R * f), (byte)Math.Min(255, c.G * f), (byte)Math.Min(255, c.B * f));

    static (byte, byte, byte) Shade((byte R, byte G, byte B) c, double level)
    {
        double f = Math.Clamp(1 + level / 30, 0.45, 1.6);
        return ((byte)Math.Min(255, c.R * f), (byte)Math.Min(255, c.G * f), (byte)Math.Min(255, c.B * f));
    }

    static string FormName(string form) => form.Length == 0 ? "STREET TOWN" : form.ToUpperInvariant();
}
