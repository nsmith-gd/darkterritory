using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.LineGen;

/// <summary>
/// The track's lethal checks on a generated line (plan §7.3, §8.5, §9.6, §22.5), host side each tick: a curve taken
/// above its derail speed, a weak bridge crossed with more cars than its limit (the span goes under the first car past
/// it), running onto a washout. And brass rammed above cutting speed damages the engine (GDD §22 "ram it and pay").
/// Every one of them has a tell a train obeying its authority meets in time; the validator drove it.
/// </summary>
public static class TrackRules
{
    /// <summary>What ended it, for the report and the HUD; null while the train's on the rails.</summary>
    public static string? Step(World world, LinePlan plan, double dt)
    {
        if (world.Derailed)
            return null;
        var train = world.Train;
        var rake = train.Dynamics;
        var r = plan.Rules;
        double v = rake.Speed;
        // A curve too fast anywhere under the train: √(a_derail R).
        double k = 0, at = rake.Distance;
        foreach (var car in train.Cars)
            if (rake.Consist.IndexOf(car.Index) >= 0)
            {
                double mid = car.FrontDistance - car.Length / 2, kc = Math.Abs(train.Line.Sample(rake.Path, mid).Curvature);
                if (kc > k)
                    (k, at) = (kc, mid);
            }
        if (k > 1e-9 && v > Math.Sqrt(r.ADerail / k))
            return Derail(world, BendCause(plan, train.Line, rake.Path, at, v, Math.Sqrt(r.ADerail / k)));

        var (edge, s) = Locate(plan, train.Line, rake.Path, rake.Distance);
        foreach (var st in plan.Structures)
        {
            if (st.Edge != edge)
                continue;
            if (st.Type == StructureType.Washout && s >= st.S0 && s <= st.S1)
                return Derail(world, "ran onto the washout");
            // §22.5: over its limit, the span collapses under the first car past it.
            if (st.Weak is { } weak && r.WeakBridgeCollapses && rake.Consist.CarCount > weak.MaxCars && s >= st.S0)
            {
                double pitch = train.Dynamics.Tuning.Geometry.CarLength + train.Dynamics.Tuning.Geometry.CouplingGap;
                double overLimit = train.Dynamics.Tuning.Geometry.EngineLength + weak.MaxCars * pitch;
                // The first car past the limit is on the span.
                if (s - overLimit >= st.S0 && s - overLimit <= st.S1 + pitch)
                    return Derail(world, $"{st.Name} gave way under car {weak.MaxCars + 1}");
            }
            if (st.Type == StructureType.BrassField && s >= st.S0 && s <= st.S1 && v > r.BrassCuttingSpeed)
            {
                double damage = r.BrassDamagePerSpeedSquared * (v - r.BrassCuttingSpeed) * (v - r.BrassCuttingSpeed) * dt;
                var engine = rake.Consist.Vehicles[0];
                engine.Integrity = Math.Max(0, engine.Integrity - damage);
            }
        }
        return null;
    }

    /// <summary>
    /// T121 playtest ("if derailment happens people should know they took the corner too hard and by how much"): the
    /// bend's posted figure from its board, the speed it was taken at, and how far over the board that was, in the km/h the
    /// boards and the cab map are painted in. Where no board stands before it, against what the bend holds.
    /// </summary>
    public static string BendCause(LinePlan plan, RailLine line, int path, double distance, double v, double holds)
    {
        int kmh = (int)Math.Round(v * 3.6);
        var (edge, s) = Locate(plan, line, path, distance);
        var board = plan.Signage.Where(b => b is { Type: "speedBoard", Required: true } && b.Edge == edge && b.S <= s && b.S >= s - 900)
            .OrderByDescending(b => b.S).FirstOrDefault();
        if (board is not null && int.TryParse(board.Text, System.Globalization.CultureInfo.InvariantCulture, out int posted))
            return $"took the {posted} km/h bend at {kmh} km/h, {kmh - posted} km/h too fast";
        return $"took the bend at {kmh} km/h, {kmh - (int)Math.Round(holds * 3.6)} km/h over the {holds * 3.6:0} km/h it holds";
    }

    static string Derail(World world, string why)
    {
        world.Derail(why);
        return why;
    }

    /// <summary>A path position as an edge of the plan and its own distance.</summary>
    public static (string Edge, double S) Locate(LinePlan plan, RailLine line, int path, double distance)
    {
        double main = line.MainDistance(path, distance);
        if (!double.IsNaN(main))
            return ("main", main);
        int branch = RailLine.ViaOf(path) is var via and >= 0 ? via : path;
        var b = line.Branches[branch];
        double along = RailLine.ViaOf(path) >= 0 ? distance - b.Offset - b.Toe : distance - b.Toe;
        return (plan.EdgeOfBranch(branch)?.Edge ?? "main", along);
    }
}
