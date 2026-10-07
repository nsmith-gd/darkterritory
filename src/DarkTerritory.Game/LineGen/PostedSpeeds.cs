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
            // The bend it stands before: the sharpest curve in the next 600 m, and as far either side as it's nearly as sharp.
            double kMax = 0, sMax = b.S;
            for (double s = b.S; s <= Math.Min(length, b.S + 600); s += 5)
            {
                double k = Math.Abs(line.Sample(RailLine.MainPath, s).Curvature);
                if (k > kMax)
                    (kMax, sMax) = (k, s);
            }
            // Only a bend this board's figure is for: one that would derail a train within half as much again of it.
            if (kMax < 1e-6 || Math.Sqrt(plan.Rules.ADerail / kMax) > 1.5 * b.Value!.Value + 1)
                continue;
            double s0 = sMax, s1 = sMax;
            while (s0 > b.S && Math.Abs(line.Sample(RailLine.MainPath, s0 - 5).Curvature) > kMax * 0.6)
                s0 -= 5;
            while (s1 < length && s1 < b.S + 900 && Math.Abs(line.Sample(RailLine.MainPath, s1 + 5).Curvature) > kMax * 0.6)
                s1 += 5;
            // The board's own figure (rounded down to the 5 the boards are painted in), so the map and the board agree.
            int kmh = int.TryParse(b.Text, System.Globalization.CultureInfo.InvariantCulture, out int painted) ? painted : (int)(b.Value!.Value * 3.6);
            bends.Add((s0, Math.Max(s1, s0 + 10), kmh, null));
        }
        return bends;
    }
}
