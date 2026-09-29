using Ballast;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.LineGen;

/// <summary>
/// A way through the line (plan §16.1): the main line, taking the alternates it names at their forks. Route distance
/// runs along it from the back of the fortress yard; each stretch is an edge's own distance.
/// </summary>
public sealed class PlanRouteWay
{
    public PlanRouteWay(LinePlan plan, RailLine line, IReadOnlyCollection<string> alternates)
    {
        Plan = plan;
        Line = line;
        Alternates = alternates;
        var stretches = new List<(string Edge, double S0, double S1, double From)>();
        double at = 0, s = 0;
        foreach (var a in plan.Alignment.Where(a => a.Role == EdgeRole.Alternate && alternates.Contains(a.Edge)).OrderBy(a => a.Toe))
        {
            stretches.Add(("main", s, a.Toe, at));
            at += a.Toe - s;
            stretches.Add((a.Edge, 0, a.Length, at));
            at += a.Length;
            s = a.Rejoin!.Value;
        }
        stretches.Add(("main", s, plan.LengthM, at));
        at += plan.LengthM - s;
        Stretches = stretches;
        Length = at;
    }

    public LinePlan Plan { get; }
    public RailLine Line { get; }
    public IReadOnlyCollection<string> Alternates { get; }
    public IReadOnlyList<(string Edge, double S0, double S1, double From)> Stretches { get; }
    public double Length { get; }

    /// <summary>The edge and its distance at a route distance.</summary>
    public (string Edge, double S) At(double d)
    {
        foreach (var st in Stretches)
            if (d <= st.From + (st.S1 - st.S0) + 1e-9)
                return (st.Edge, st.S0 + Math.Max(0, d - st.From));
        var last = Stretches[^1];
        return (last.Edge, last.S1);
    }

    /// <summary>The route distance of a point on an edge, or null when the route doesn't run there.</summary>
    public double? Of(string edge, double s)
    {
        foreach (var st in Stretches)
            if (st.Edge == edge && s >= st.S0 - 1e-6 && s <= st.S1 + 1e-6)
                return st.From + s - st.S0;
        return null;
    }

    /// <summary>Where a rake's front is, as an edge and a distance on it, from its path on the rail model.</summary>
    public (string Edge, double S) Locate(int path, double distance)
    {
        string EdgeOf(int branch) => Plan.Alignment.First(a => a.Branch == branch).Edge;
        if (path == RailLine.MainPath)
            return ("main", distance);
        if (RailLine.ViaOf(path) is var via and >= 0)
        {
            var b = Line.Branches[via];
            return distance >= b.Rejoin ? ("main", distance) : (EdgeOf(via), distance - b.Offset - b.Toe);
        }
        var br = Line.Branches[path];
        if (distance <= br.Toe)
            return ("main", distance);
        if (br.Rejoins && distance >= br.End)
            return ("main", distance + br.Offset);
        return (EdgeOf(path), distance - br.Toe);
    }

    /// <summary>The communicated speed at a route distance (§9.1): the least of every limit in effect there.</summary>
    public double Communicated(double d)
    {
        var (edge, s) = At(d);
        double v = Plan.Authority.LineSpeedMs;
        foreach (var l in Plan.Authority.Limits)
            if (l.Edge == edge && s >= l.S0 && s <= l.S1)
                v = Math.Min(v, l.VMs);
        foreach (var r in Plan.Authority.Restricted)
            if (r.Edge == edge && s >= r.S0 && s <= r.S1)
                v = Math.Min(v, r.VMs);
        return v;
    }

    /// <summary>The demands on this way, with their route distances.</summary>
    public IEnumerable<(PlanDemand Demand, double At, double TellAt)> Demands()
    {
        foreach (var d in Plan.Authority.Demands)
            if (Of(d.Edge, d.SReq) is { } at)
                yield return (d, at, at - (d.SReq - d.TellAt));
    }
}

/// <summary>
/// The ideal driver's speed profile (plan §16.1), by the classic forward–backward method: the communicated speed,
/// under a braking curve back from every demand and stop at the loaded consist's real deceleration, less gravity on
/// descents and the worst brake fade measured there.
/// </summary>
public sealed class SpeedProfile
{
    public const double Step = 5;
    readonly double[] _target;

    public SpeedProfile(PlanRouteWay way, double brake, IReadOnlyList<double> stops, Func<double, double>? fadeAt = null, double adhesion = 1, double margin = 0.85)
    {
        Way = way;
        Stops = [.. stops.OrderBy(x => x)];
        _stopBrake = brake * adhesion * margin;
        int n = (int)Math.Ceiling(way.Length / Step) + 2;
        _target = new double[n];
        var grade = new double[n];
        for (int i = 0; i < n; i++)
        {
            double d = Math.Min(i * Step, way.Length);
            _target[i] = way.Communicated(d);
            var (edge, s) = way.At(d);
            var line = edge == "main" ? way.Line : way.Line.Branches[way.Plan.Edge(edge).Branch].Local;
            grade[i] = line.Sample(s).GradePercent;
        }
        // Backward: braking curves (v² grows by 2 b Δ going back up the line), gravity taking from b on descents.
        for (int i = n - 2; i >= 0; i--)
        {
            double f = fadeAt?.Invoke(i * Step) ?? 1;
            double g = grade[i] < 0 ? 9.81 * Math.Sin(Math.Atan(-grade[i] / 100)) : 0;
            double b = Math.Max(0.02, brake * adhesion * f * margin - g);
            _target[i] = Math.Min(_target[i], Math.Sqrt(_target[i + 1] * _target[i + 1] + 2 * b * Step));
        }
    }

    public PlanRouteWay Way { get; }
    /// <summary>Where the ideal driver stops (each facility, §16.1), in route distance.</summary>
    public IReadOnlyList<double> Stops { get; }
    readonly double _stopBrake;

    /// <summary>The speed to be at, <paramref name="d"/> along the way, braking for the next stop not yet made.</summary>
    public double Target(double d, double? nextStop = null)
    {
        double f = Math.Clamp(d / Step, 0, _target.Length - 1);
        int i = (int)f;
        double v = i + 1 < _target.Length ? _target[i] + (_target[i + 1] - _target[i]) * (f - i) : _target[i];
        if (nextStop is { } stop && stop >= d)
            v = Math.Min(v, Math.Sqrt(2 * _stopBrake * (stop - d)));
        return v;
    }
}

/// <summary>What a drive of a way found.</summary>
public sealed record DriveResult(bool Survived, string? Failure, double TransitSeconds, double MinBrake, IReadOnlyDictionary<string, double> FadeAt,
    double WorstOverspeed, string? WorstOverspeedAt, IReadOnlyList<(double D, double V)> Trace, bool Stalled);

/// <summary>
/// Drives a way on the shared train simulation (plan §16): the real <see cref="TrainOnLine"/> at the real tick rate,
/// with the loaded departing consist. The ideal driver keeps to the profile and stops at every facility; the sloppy one
/// (§16.2) runs 10% over and reacts late, and only has to survive.
/// </summary>
public static class Drivers
{
    public sealed record Options(bool Sloppy, double SpeedFactor, double TReact, IReadOnlyList<double> Stops, double MaxSeconds, double ADerail, double Band,
        double StartDistance, double TransitFrom, double TransitTo);

    public static DriveResult Drive(PlanRouteWay way, SpeedProfile profile, TrainTuning tuning, int cars, Options o)
    {
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(tuning, cars, 1)), way.Line, o.StartDistance);
        foreach (var a in way.Plan.Alignment.Where(a => a.Role == EdgeRole.Alternate && way.Alternates.Contains(a.Edge)))
            train.ThrowSwitch(a.Branch, true, 0);
        var d = train.Dynamics;
        d.Velocity = way.Communicated(o.StartDistance);
        var demands = way.Demands().OrderBy(x => x.At).ToList();
        var fade = new Dictionary<string, double>();
        var trace = new List<(double, double)>();
        var weak = way.Plan.Structures.Where(s => s.Weak is not null).ToList();
        var washouts = way.Plan.Structures.Where(s => s.Type == StructureType.Washout).ToList();
        double minBrake = 1, worstOver = 0, stalledFor = 0, dwell = 0, started = double.NaN, arrived = double.NaN;
        string? worstAt = null;
        int stop = 0;
        var stops = o.Stops.OrderBy(x => x).ToList();
        int ticks = (int)(o.MaxSeconds * SimConstants.TickRate);
        int next = 0;
        for (int tick = 0; tick < ticks; tick++)
        {
            var (edge, s) = way.Locate(d.Path, d.Distance);
            double rd = way.Of(edge, s) ?? 0;
            double v = d.Speed;
            if (double.IsNaN(started) && rd >= o.TransitFrom)
                started = tick * SimConstants.TickSeconds;
            if (rd >= o.TransitTo)
            {
                arrived = tick * SimConstants.TickSeconds;
                break;
            }
            // The lethal checks (§7.3): a curve too fast, a bridge overloaded, a washout.
            double k = 0;
            foreach (var car in train.Cars)
                k = Math.Max(k, Math.Abs(train.Line.Sample(d.Path, car.FrontDistance - car.Length / 2).Curvature));
            if (k > 0 && v > Math.Sqrt(o.ADerail / k))
                return Fail($"derailed on a curve at {rd:0} m of the way ({v:0.0} m/s, R {1 / k:0} m)");
            foreach (var w in washouts)
                if (w.Edge == edge && s >= w.S0 && s <= w.S1)
                    return Fail($"ran onto the washout on {w.Edge} at {w.S0:0} m");
            foreach (var b in weak)
                if (b.Edge == edge && s >= b.S0 && s <= b.S1 && cars > b.Weak!.MaxCars)
                    return Fail($"{b.Name} collapsed under car {b.Weak.MaxCars + 1} ({cars} cars over a {b.Weak.MaxCars}-car limit)");
            // What the ideal driver held to each demand, and the brakes approaching it (demands in order: the ones in
            // hand are from the next unpassed on to those whose tell zone has started).
            while (next < demands.Count && rd >= demands[next].At)
            {
                var (demand, at, _) = demands[next++];
                double over = v - demand.VReq;
                // Stops and junctions aren't speed limits: a stop is the driver's own business, and a junction's is optional.
                if (over > worstOver && demand.Type is not (DemandType.FacingJunction or DemandType.YardLimit or DemandType.FacilityStop))
                    (worstOver, worstAt) = (over, $"{demand.Type} {demand.Id} at {at:0} m");
            }
            for (int i = next; i < demands.Count && demands[i].TellAt <= rd + 3000; i++)
                if (rd >= demands[i].TellAt && rd <= demands[i].At)
                    fade[demands[i].Demand.Id] = Math.Min(fade.GetValueOrDefault(demands[i].Demand.Id, 1), d.BrakeEfficiency);
            minBrake = Math.Min(minBrake, d.BrakeEfficiency);
            if (tick % 15 == 0)
                trace.Add((Math.Round(rd), Math.Round(v, 2)));

            var controls = new TrainControls { Reverser = 1 };
            double target;
            if (o.Sloppy)
                target = SloppyTarget(way, demands, rd, v, o);
            else
                target = profile.Target(rd + v * SimConstants.TickSeconds, stop < stops.Count ? stops[stop] : null);
            // Creep up to a planned stop rather than standing short of it.
            if (stop < stops.Count && rd < stops[stop] - 4 && stops[stop] - rd < 40)
                target = Math.Max(target, 1.2);
            // A planned stop: brake to it, stand a moment, go on (the dawn check adds the time at the facility).
            if (stop < stops.Count && rd >= stops[stop] - 4)
            {
                if (v > 0.05)
                    controls.Brake = 1;
                else if ((dwell += SimConstants.TickSeconds) > 2)
                {
                    stop++;
                    dwell = 0;
                }
                train.Step(SimConstants.TickSeconds, controls);
                continue;
            }
            // Keep within [target − 2 band, target]: brake at the target, full throttle below the band, ease between.
            if (v > target)
                controls.Brake = 1;
            else if (v < target - 2 * o.Band)
                controls.Throttle = 1;
            else
                controls.Throttle = v < target - o.Band ? 0.5 : 0;
            train.Step(SimConstants.TickSeconds, controls);
            // A stall: full throttle, not moving, and nowhere it meant to stop.
            stalledFor = controls.Throttle >= 1 && d.Speed < 0.05 ? stalledFor + SimConstants.TickSeconds : 0;
            if (stalledFor > 8)
                return Fail($"stalled at {rd:0} m of the way", stalled: true);
        }
        if (double.IsNaN(arrived))
            return Fail("didn't arrive in time");
        return new DriveResult(true, null, arrived - (double.IsNaN(started) ? 0 : started), minBrake, fade, worstOver, worstAt, trace, false);

        DriveResult Fail(string why, bool stalled = false) => new(false, why, double.NaN, minBrake, fade, worstOver, worstAt, trace, stalled);
    }

    /// <summary>
    /// §16.2: 10% over the communicated speed, and braking only once a demand's tell has been seen and t_react gone by.
    /// </summary>
    static double SloppyTarget(PlanRouteWay way, List<(PlanDemand Demand, double At, double TellAt)> demands, double rd, double v, Options o)
    {
        double target = o.SpeedFactor * way.Communicated(rd);
        foreach (var (demand, at, tellAt) in demands)
        {
            if (demand.Type is DemandType.FacingJunction or DemandType.FacilityStop or DemandType.YardLimit)
                continue;
            // Seen the tell, reacted, and not yet past the demand: brake for it.
            if (rd >= tellAt + v * o.TReact && rd < at)
                target = Math.Min(target, o.SpeedFactor * demand.VReq);
        }
        return target;
    }
}
