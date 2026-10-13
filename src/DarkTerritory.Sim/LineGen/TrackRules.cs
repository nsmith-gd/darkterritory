using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.LineGen;

/// <summary>
/// The track's lethal checks on a generated line (plan §7.3, §8.5, §9.6, §22.5), host side each tick: a curve taken
/// above its derail speed, a weak bridge crossed with more cars than its limit (the span goes under the first car past
/// it), running onto a washout. And brass rammed above cutting speed damages the engine (GDD §22 "ram it and pay").
/// Every one of them has a tell a train obeying its authority meets in time; the validator drove it.
/// </summary>
/// <summary>A bend taken too fast, as the train has it this tick (<see cref="TrackRules.Assess"/>; note 265).</summary>
/// <param name="Stress">The bend under the train: 0 at its board's speed or under, 1 at its derailing speed.</param>
/// <param name="Warning">A bend under it or ahead, within the distance to brake, that the speed now would derail it on.</param>
/// <param name="AheadM">How far ahead that bend begins (0: the train's on it).</param>
/// <param name="DerailMs">What that bend derails a train above (m/s).</param>
/// <param name="PostedMs">What it's boarded at (m/s, linegen plan §8.5).</param>
/// <param name="OnIt">The train's on that bend.</param>
public readonly record struct BendStress(double Stress, bool Warning, double AheadM, double DerailMs, double PostedMs, bool OnIt);

public static class TrackRules
{
    /// <summary>How far on into a bend <see cref="Assess"/> looks for its tightest point (a 90° turn at 400 m is 630 m).</summary>
    const double MaxBendM = 800;

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
                double mid = car.FrontDistance - car.Length / 2, kc = Curvature(train, mid);
                if (kc > k)
                    (k, at) = (kc, mid);
            }
        // Note 266: the warning (Assess) counted on the host, tick by tick; a bend commits only once it's been up a full lead.
        var t = rake.Tuning.Overspeed;
        var stress = Assess(train, r, t);
        world.BendWarnSeconds = stress.Warning ? world.BendWarnSeconds + dt : 0;
        if (k > 1e-9 && v > Math.Sqrt(r.ADerail / k))
        {
            // The HUD and the cab's bell have had it up this long, this tick included.
            if (world.BendWarnSeconds + 1e-9 < t.LeadSeconds)
            {
                world.BendsSpared++;
            }
            else
            {
                // Too fast for it: the throttle's doing, or a Stoker's runaway (App. C.9; note 190).
                world.BendCommits.Add(world.BendWarnSeconds);
                string why = BendCause(plan, train.Line, rake.Path, at, v, Math.Sqrt(r.ADerail / k));
                world.Overspeed(why);
                return world.DerailCause;
            }
        }

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

    /// <summary>
    /// A bend too fast, as this train has it now (note 265): the stress of the bend under it (0 at its board's speed, 1 at
    /// its derailing speed), and whether the warning is up: a bend under the train, or ahead within the distance to brake
    /// below it (<see cref="OverspeedTuning.WarnDistance"/>), that the speed now would derail it on. From the train and the
    /// line alone, so every machine works it out the same and nothing is sent.
    /// </summary>
    public static BendStress Assess(TrainOnLine train, PlanRules r, OverspeedTuning t)
    {
        var rake = train.Dynamics;
        double v = rake.Speed;
        if (v < 0.5 || train.Wreck is not null)
            return default;
        double postShare = Math.Clamp(r.APost / r.ADerail, 0, 0.99);
        // Under the train: the sharpest bend any of its cars is on.
        double kOn = 0;
        foreach (var car in train.Cars)
            if (rake.Consist.IndexOf(car.Index) >= 0)
                kOn = Math.Max(kOn, Curvature(train, car.FrontDistance - car.Length / 2));
        double pull = v * v * kOn / r.ADerail;
        double stress = kOn < 1e-9 ? 0 : Math.Clamp((pull - postShare) / (1 - postShare), 0, 1);
        int travel = Math.Sign(rake.Velocity);
        double from = travel > 0 ? rake.Distance : rake.RearDistance, length = train.Line.PathLength(rake.Path);
        const double Step = 5;
        // Note 278: what's told is the bend's tightest point, not where the speed now first comes off on its way in (on a
        // sharp bend that's early in its easing, which read "derails over 78" on a bend that derails at 65).
        double Tightest(double k, double s)
        {
            for (double x = Step; x <= MaxBendM; x += Step)
            {
                double at = s + travel * x;
                if (at < 0 || at > length)
                    break;
                double kx = Curvature(train, at);
                if (kx < 1e-9)
                    break;
                k = Math.Max(k, kx);
            }
            return k;
        }
        if (kOn > 1e-9 && pull > 1)
        {
            double k = Tightest(kOn, from);
            return new BendStress(stress, true, 0, Math.Sqrt(r.ADerail / k), Math.Floor(Math.Sqrt(r.APost / k)), true);
        }
        // Ahead, the way it's going, out to where a bend that would take any speed short of a stop needs telling.
        double rated = rake.RatedBrakeDecel, reach = t.WarnDistance(v, 0, rated, t.LeadSeconds);
        for (double x = Step; x <= reach; x += Step)
        {
            double s = from + travel * x;
            if (s < 0 || s > length)
                break;
            double k = Curvature(train, s);
            if (k < 1e-9)
                continue;
            double vd = Math.Sqrt(r.ADerail / k);
            if (v > vd && x <= t.WarnDistance(v, vd, rated, t.LeadSeconds))
            {
                k = Tightest(k, s);
                return new BendStress(stress, true, x, Math.Sqrt(r.ADerail / k), Math.Floor(Math.Sqrt(r.APost / k)), false);
            }
        }
        return new BendStress(stress, false, 0, 0, 0, false);
    }

    /// <summary>
    /// The bend the train's on at <paramref name="s"/> along its path, as a derailment counts it: none on a yard's track (a
    /// facility's spur, its turnout's S-curve off the main line included). The director, 8 Oct 2026: "When turning into a
    /// yard, derailment is way too easy. Don't allow derailments when turning into and leaving a yard." (note 470). The main
    /// line's bends, an alternate's and a dead line's still take a train taken over them too fast.
    /// </summary>
    static double Curvature(TrainOnLine train, double s) =>
        InYard(train.Line, train.Dynamics.Path, s) ? 0 : Math.Abs(train.Line.Sample(train.Dynamics.Path, s).Curvature);

    /// <summary>Whether <paramref name="s"/> along <paramref name="path"/> is on a yard's track (a <see cref="BranchKind.Spur"/>), off the main line.</summary>
    public static bool InYard(RailLine line, int path, double s) =>
        path >= 0 && path < line.Branches.Count && line.Branches[path].Definition.Kind == BranchKind.Spur
        && double.IsNaN(line.MainDistance(path, s));

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
