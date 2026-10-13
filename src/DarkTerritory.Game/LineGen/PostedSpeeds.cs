using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Rail;

namespace DarkTerritory.Game.LineGen;

/// <summary>
/// A generated line's posted stretches on the main line (LineBuilder.Signage's speed boards: every bend that would derail
/// the engine at full steam, and each demand's), each with its figure and the stretch it's for: the bend the board stands
/// before, or a brass or weak-bridge limit's own. The cab's run map (GreyboxScene) and the route card's profile
/// (PlanHud.Profile) ink the same list, so the card, the map and the boards agree.
/// </summary>
public static class PostedSpeeds
{
    /// <summary>
    /// What the map and the card print for a posted stretch: the number alone (note 671, the director: "we just show the
    /// number ... we're trying to give information without tutorializing the whole game all at once"). No unit, no key, no
    /// word for what it's for: the red says which ones have you off.
    /// </summary>
    public static string Figure(int kmh) => kmh.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A stretch that has the train off the rails (a bend, a weak bridge), inked red; brass only costs the engine.</summary>
    public static bool Derails(string? why) => why != "BRASS";

    public static List<(double S0, double S1, int Kmh, string? Why)> Of(LinePlan plan, RailLine line, double length)
    {
        var bends = new List<(double S0, double S1, int Kmh, string? Why)>();
        foreach (var b in plan.Signage.Where(b => b is { Type: "speedBoard", Edge: "main", Required: true, Value: > 0 }))
        {
            // Note 266 (build 1121, "a maximum speed ... not on curves"): a board for something that isn't a bend (brass,
            // a weak bridge) is inked over its own stretch, with what it's for; it was put on the nearest curve, however gentle.
            var demand = b.For is null ? null : plan.Authority.Demands.FirstOrDefault(d => d.Id == b.For);
            if (demand is { Type: DemandType.Brass or DemandType.WeakBridge })
            {
                var limit = plan.Authority.Limits.FirstOrDefault(l => l.Edge == "main" && Math.Abs(l.S0 - demand.SReq) < 1);
                if (limit is null)
                    continue;
                int posted = int.TryParse(b.Text, System.Globalization.CultureInfo.InvariantCulture, out int p0) ? p0 : (int)(b.Value!.Value * 3.6);
                bends.Add((Math.Clamp(limit.S0, 0, length), Math.Clamp(Math.Max(limit.S1, limit.S0 + 10), 0, length), posted,
                    demand.Type == DemandType.Brass ? "BRASS" : "BRIDGE"));
                continue;
            }
            // The bends it's for. A demand's board can govern more than one: an S-bend has a limit on each curve and one
            // board before both (deepTerritory:2 at 33 km), and the director wants every stretch that can't take top speed
            // on the map (note 285). So each curve limit of its figure in its reach is a bend; a board with none (one of
            // Signage's own, a bend to a board) is for the sharpest curve in the next 600 m.
            var stretches = demand is null ? [] : plan.Authority.Limits
                .Where(l => l.Edge == "main" && l.Source == LimitSource.Curve && Math.Abs(l.VMs - b.Value!.Value) < 0.01 && l.S0 >= b.S - 1 && l.S0 <= b.S + 600)
                .Select(l => (From: l.S0, To: Math.Min(length, l.S1), Own: true)).ToList();
            if (stretches.Count == 0)
                stretches.Add((b.S, Math.Min(length, b.S + 600), false));
            foreach (var (from, to, own) in stretches)
            {
                // A curve limit's bend is the curve it starts on: it can run on over the next curve of an S-bend, and that
                // one's sharpest point was taken for it, leaving its own bare (note 665: frontier:15 at 7.3 km, a curve
                // derailing at 69 km/h with no red, its limit's sharpest point the 67 km/h curve after it; the director met
                // a bend with no red on the map). A board with no limit, the sharpest in its reach, as before.
                var curves = Curves(line, from, to).ToList();
                if (curves.Count == 0)
                    continue;
                var (sMax, kMax) = own ? curves[0] : curves.MaxBy(c => c.K);
                // Only a bend this board's figure is for: one that would derail a train within half as much again of it.
                if (Math.Sqrt(plan.Rules.ADerail / kMax) > 1.5 * b.Value!.Value + 1)
                    continue;
                // As far either side as it's nearly as sharp.
                double s0 = sMax, s1 = sMax;
                while (s0 > Math.Max(b.S, from - 30) && Math.Abs(line.Sample(RailLine.MainPath, s0 - 5).Curvature) > kMax * 0.6)
                    s0 -= 5;
                while (s1 < length && s1 < Math.Max(b.S + 900, to + 30) && Math.Abs(line.Sample(RailLine.MainPath, s1 + 5).Curvature) > kMax * 0.6)
                    s1 += 5;
                // The board's own figure (rounded down to the 5 the boards are painted in), so the map and the board agree.
                int kmh = int.TryParse(b.Text, System.Globalization.CultureInfo.InvariantCulture, out int painted) ? painted : (int)(b.Value!.Value * 3.6);
                bends.Add((s0, Math.Max(s1, s0 + 10), kmh, null));
            }
        }
        return bends;
    }

    /// <summary>
    /// The curves on the main line between <paramref name="from"/> and <paramref name="to"/>, each where it's sharpest and how
    /// sharp: a curve is a run of bend one way, ended by straight track or the bend turning the other way.
    /// </summary>
    static IEnumerable<(double S, double K)> Curves(RailLine line, double from, double to)
    {
        double kMax = 0, sMax = from;
        int side = 0;
        for (double s = from; s <= to; s += 5)
        {
            double c = line.Sample(RailLine.MainPath, s).Curvature, k = Math.Abs(c);
            int now = k < 1e-6 ? 0 : Math.Sign(c);
            if (now != side && kMax > 0)
            {
                yield return (sMax, kMax);
                kMax = 0;
            }
            side = now;
            if (now != 0 && k > kMax)
                (kMax, sMax) = (k, s);
        }
        if (kMax > 0)
            yield return (sMax, kMax);
    }
}
