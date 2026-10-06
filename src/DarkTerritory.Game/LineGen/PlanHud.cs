using System.Numerics;
using Ballast.Render;
using DarkTerritory.Sim.LineGen;

namespace DarkTerritory.Game.LineGen;

/// <summary>
/// A generated line on the flat screen: the route card the crew is handed at the fortress (linegen plan §9.7: the
/// timetable, Form 19, the known grades), and the designer's overlay (§20.2: chainage, the piece and its tags, the
/// communicated and allowed speeds, grade and curve, the director's pressure).
/// </summary>
public static class PlanHud
{
    static readonly Vector4 Paper = new(0.80f, 0.76f, 0.64f, 0.94f);
    static readonly Vector4 InkDark = new(0.10f, 0.09f, 0.08f, 1);
    static readonly Vector4 Rule = new(0.35f, 0.28f, 0.2f, 0.8f);
    static readonly Vector4 Stamp = new(0.55f, 0.12f, 0.08f, 1);
    static readonly Vector4 Ink = new(0.88f, 0.84f, 0.74f, 1);
    static readonly Vector4 Dim = new(0.60f, 0.58f, 0.53f, 1);
    static readonly Vector4 Panel = new(0.02f, 0.02f, 0.03f, 0.6f);

    /// <summary>The route card, as a sheet over the middle of the screen: what to expect, in the order it comes.</summary>
    /// <param name="page">Which side of it: a long night's card runs over.</param>
    /// <returns>How many sides it has.</returns>
    /// <param name="rail">The night's line: with it, the card's first side carries the line's profile under its title,
    /// drawn as the depot would ink it (its height along the night, its stops ticked in red), the climbs read at a glance.</param>
    public static int RouteCard(Overlay o, int width, int height, LinePlan plan, int page = 0, Sim.Rail.RailLine? rail = null)
    {
        var card = plan.RouteCard;
        int line = o.Font.LineHeight;
        float w = Math.Min(width - 16, 300), x = MathF.Round((width - w) / 2), y = 8;
        var rows = new List<(string Text, Vector4 Colour)>
        {
            (card.Title.ToUpperInvariant(), InkDark),
            ($"DAWN IN {card.DawnS / 60:0} MIN   LINE SPEED {Math.Floor(card.LineSpeedMs * 3.6 / 5) * 5:0} KM/H", InkDark),
            ("", InkDark),
            ("TIMETABLE", Stamp),
        };
        rows.AddRange(card.Timetable.Select(t => ($"{t.Km,5:0.0}  {t.Text}", InkDark)));
        if (card.Form19.Count > 0)
        {
            rows.Add(("", InkDark));
            rows.Add(("FORM 19: SPECIAL INSTRUCTIONS", Stamp));
            rows.AddRange(card.Form19.Select(t => ($"{t.Km,5:0.0}  {t.Text}", InkDark)));
        }
        if (card.KnownGrades.Count > 0)
        {
            rows.Add(("", InkDark));
            rows.Add(("KNOWN GRADES", Stamp));
            rows.AddRange(card.KnownGrades.Select(g => ($"{g.Route}: {g.RulingPct:0.0}% over {g.LengthKm:0.0} km{(g.Passable ? "" : " (NOT FOR THIS TRAIN)")}", InkDark)));
        }
        // Room under the title for the profile (first side only).
        const int profileH = 34;
        bool profile = rail is not null && page == 0 && rail.Length > 100;
        if (profile)
            rows.InsertRange(2, Enumerable.Repeat(("", InkDark), (profileH + line - 1) / line + 1));
        // Wrapped to the sheet; what doesn't fit the screen is cut off at the foot, as a real card is folded.
        int chars = (int)((w - 12) / o.Font.Advance);
        var wrapped = rows.SelectMany(r => Wrap(r.Text, chars).Select(t => (t, r.Colour))).ToList();
        int fits = Math.Max(2, (height - 24) / line) - 1;
        int pages = (wrapped.Count + fits - 1) / fits;
        page = Math.Clamp(page, 0, pages - 1);
        wrapped = [.. wrapped.Skip(page * fits).Take(fits)];
        if (page + 1 < pages)
            wrapped.Add(("(CONTINUED OVERLEAF: C)", Stamp));
        o.Rect(x, y, w, wrapped.Count * line + 10, Paper);
        o.Outline(x, y, w, wrapped.Count * line + 10, Rule);
        float ty = y + 5;
        foreach (var (text, colour) in wrapped)
        {
            o.Text(x + 6, ty, text, colour, shadow: false);
            ty += line;
        }
        if (profile)
            Profile(o, rail!, card, x + 6, y + 5 + 2 * line + 3, w - 12, profileH);
        return pages;
    }

    /// <summary>
    /// The line's height along the night in a box (<paramref name="x"/>, <paramref name="y"/>, w by h): a column of ink per
    /// pixel up to the ground's height there, the timetable's stops ticked in red above it, km marks along the foot.
    /// </summary>
    static void Profile(Overlay o, Sim.Rail.RailLine line, PlanRouteCard card, float x, float y, float w, float h)
    {
        int n = Math.Max(2, (int)w);
        var heights = new double[n];
        for (int i = 0; i < n; i++)
            heights[i] = line.Sample(line.Length * i / (n - 1)).Position.Y;
        double lo = heights.Min(), hi = Math.Max(lo + 10, heights.Max());
        o.Outline(x - 1, y - 1, w + 2, h + 2, Rule);
        for (int i = 0; i < n; i++)
        {
            float top = (float)((hi - heights[i]) / (hi - lo) * (h - 6)) + 4;
            o.Rect(x + i, y + top, 1, h - top, InkDark with { W = 0.75f });
        }
        double km = line.Length / 1000;
        for (int k = 5; k < km; k += 5)
            o.Rect(x + (float)(k / km * w), y + h - 3, 1, 3, Paper);
        foreach (var stop in card.Timetable)
        {
            float sx = x + (float)Math.Clamp(stop.Km / km, 0, 1) * (w - 1);
            o.Rect(sx, y, 1, 4, Stamp);
        }
    }

    static IEnumerable<string> Wrap(string text, int chars)
    {
        if (text.Length <= chars)
        {
            yield return text;
            yield break;
        }
        // The first line keeps its leading spaces (the km column's padding).
        string lead = text[..(text.Length - text.TrimStart().Length)];
        string current = lead;
        foreach (var word in text.TrimStart().Split(' '))
        {
            if (current.Trim().Length > 0 && current.Length + 1 + word.Length > chars)
            {
                yield return current;
                current = "       " + word;
            }
            else
                current = current.Trim().Length == 0 ? current + word : current + " " + word;
        }
        if (current.Trim().Length > 0)
            yield return current;
    }

    /// <summary>The overlay: where the engine is on the plan, and what the plan says about there.</summary>
    public static void Overlay(Overlay o, int width, int height, IPlaySession s, LinePlan plan)
    {
        var train = s.Train;
        var d = train.Dynamics;
        var (edge, at) = TrackRules.Locate(plan, train.Line, d.Path, d.Distance);
        var here = train.Line.Sample(d.Path, d.Distance);
        var piece = plan.Pieces.FirstOrDefault(p => p.Edge == edge && at >= p.S0 && at <= p.S1);
        var tags = plan.Director.Tags.Where(t => t.Edge == edge && at >= t.S0 && at <= t.S1).Select(t => t.Tag).Distinct();
        var way = new PlanRouteWay(plan, train.Line, edge == "main" ? [] : [edge]);
        double communicated = way.Of(edge, at) is { } rd ? way.Communicated(rd) : plan.Authority.LineSpeedMs;
        double allowed = LineAuthority.For(plan, train.Line).Allowed(train);
        var pressure = plan.Director.Pressure;
        double main = train.Line.MainDistance(d.Path, d.Distance);
        double p = double.IsNaN(main) || pressure.Values.Count == 0 ? double.NaN
            : pressure.Values[Math.Clamp((int)(main / pressure.StepM), 0, pressure.Values.Count - 1)];
        var lines = new List<(string, Vector4)>
        {
            ($"{plan.Route.Id}  D {plan.Route.D:0.00}  {plan.Route.Region}", Ink),
            ($"KM {(double.IsNaN(main) ? double.NaN : plan.Km(main)):0.000}  {edge} {at:0} M", Ink),
            ($"PIECE {piece?.Id ?? "-"} {piece?.Type ?? ""} x{piece?.Depth ?? 0}", piece is null ? Dim : Ink),
            ($"TAGS {string.Join(" ", tags)}", Dim),
            ($"SPEED {d.Speed * 3.6:0} / POSTED {communicated * 3.6:0} / BRAKE-TO {allowed * 3.6:0} KM/H", d.Speed > communicated + 0.5 ? new Vector4(1, 0.4f, 0.3f, 1) : Ink),
            ($"GRADE {here.GradePercent:+0.00;-0.00}%  R {(Math.Abs(here.Curvature) > 1e-6 ? $"{1 / Math.Abs(here.Curvature):0}" : "-")}", Dim),
            ($"PRESSURE {p:0.00}  DAWN {plan.RouteCard.DawnS / 60:0} MIN  SLACK {plan.Validation.DawnSlackS / 60:0.0}", Dim),
        };
        int line = o.Font.LineHeight;
        float w = lines.Max(l => o.Font.Measure(l.Item1)) + 8, y = height - 60 - lines.Count * line;
        o.Rect(width - w - 4, y - 3, w, lines.Count * line + 4, Panel);
        foreach (var (text, colour) in lines)
        {
            o.TextRight(width - 8, y, text, colour);
            y += line;
        }
    }
}
