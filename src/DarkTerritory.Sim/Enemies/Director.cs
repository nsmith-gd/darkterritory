using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Enemies;

/// <summary>A decision the director made, for the harness's pacing and cap audit (App. B.9).</summary>
public readonly record struct DirectorSpawn(uint Tick, EnemyKind Kind, double Cost, double TrainDistance, int ActiveInZone, int ActiveTotal);

/// <summary>
/// The pressure director (GDD App. B.1). Enemies aren't rolled independently: a budget is spent across the
/// run against a rising curve (15% before the first facility, 45% across the middle, 40% in the final
/// approach), with hard caps per zone and overall, a grace period at the gate, a trough after every spawn,
/// and silence in the final 500 m. Sleepers are level content and aren't spent from the budget.
/// </summary>
public sealed class Director
{
    readonly DirectorTuning _t;
    readonly Route.Route? _route;
    Pcg32 _rng;
    double _cooldown;

    /// <summary>
    /// How much more often the director comes. The Vigil sets it: spec C.2's "every noise-triggered spawn weight
    /// doubles" read as spawn rate, because doubling every weight alike wouldn't change which one is picked.
    /// </summary>
    public double RateMultiplier { get; set; } = 1;
    double _spent;

    public Director(DirectorTuning tuning, Route.Route? route, ulong seed, int cars, int crew)
    {
        _t = tuning;
        _route = route;
        _rng = new Pcg32(seed, 0xD1EC7);
        var tier = route?.Tier ?? RouteTier.Frontier;
        string key = char.ToLowerInvariant(tier.ToString()[0]) + tier.ToString()[1..];
        double baseBudget = tuning.BaseBudget.GetValueOrDefault(key, 70);
        double lengthMultiplier = 1 + tuning.LengthPerCarBeyondThird * Math.Max(0, cars - 3);
        double crewMultiplier = Math.Min(tuning.CrewCap, tuning.CrewBase + tuning.CrewPerPlayer * crew);
        Budget = baseBudget * lengthMultiplier * crewMultiplier;
        Crew = crew;
    }

    public double Budget { get; }
    public double Spent => _spent;
    public int Crew { get; }
    public List<DirectorSpawn> Log { get; } = new();

    public double Cost(EnemyKind kind) => _t.Costs.GetValueOrDefault(kind switch
    {
        EnemyKind.CinderHound => "cinderHounds",
        EnemyKind.Clinger => "clingers",
        EnemyKind.Hollow => "hollow",
        EnemyKind.Switchman => "switchman",
        EnemyKind.SootChildren => "sootChildren",
        EnemyKind.Dragger => "draggers",
        EnemyKind.Rattle => "rattle",
        EnemyKind.Lamplighter => "lamplighters",
        _ => "sleepers",
    }, 2);

    /// <summary>How much of the budget may have been spent by this point along the line.</summary>
    public double Allowance(double distance)
    {
        if (_route is null)
            return Budget * Math.Clamp(distance / 20_000, 0, 1);
        var facilities = _route.Of(FeatureKind.Facility).ToList();
        double first = facilities.Count > 0 ? facilities[0].Start : _route.Length * 0.25;
        double last = facilities.Count > 0 ? facilities[^1].End : _route.Length * 0.75;
        var s = _t.PhaseShares;
        double share = distance < first ? s[0] * distance / first
            : distance < last ? s[0] + s[1] * (distance - first) / Math.Max(1, last - first)
            : s[0] + s[1] + s[2] * (distance - last) / Math.Max(1, _route.Length - last);
        return Budget * Math.Clamp(share, 0, 1);
    }

    int MaxConcurrent => Crew >= 6 ? _t.MaxConcurrentLargeCrew : _t.MaxConcurrentSmallCrew;

    /// <summary>At or near one of the route's facilities: within <paramref name="margin"/> of its span.</summary>
    bool NearFacility(double distance, double margin) =>
        _route is not null && _route.Of(FeatureKind.Facility).Any(f => distance >= f.Start - margin && distance <= f.End + margin);

    /// <summary>Called once a second by the world. Returns what to spawn, if anything.</summary>
    public EnemyKind? Decide(World world, double elapsed, IReadOnlyList<Enemy> active, double noSpawnFinal)
    {
        _cooldown -= 1;
        var train = world.Train;
        double s = train.Dynamics.Distance;
        if (elapsed < _t.GraceSeconds || _cooldown > 0)
            return null;
        if (_route is not null && s > _route.Length - noSpawnFinal)
            return null;
        int total = active.Count(e => !e.Gone && e.Kind != EnemyKind.Sleepers);
        if (total >= MaxConcurrent)
            return null;
        double available = Allowance(s) - _spent;

        var options = new List<(EnemyKind Kind, double Weight)>();
        int cargoCars = train.Dynamics.Consist.Vehicles.Count(v => v.Kind == VehicleKind.Cargo);
        int Zone(PressureZone z) => active.Count(e => !e.Gone && e.Zone == z && e.Kind != EnemyKind.Sleepers);
        // App. B.4: Clingers need a middle (length >= 3).
        if (cargoCars >= 1 && train.Dynamics.Consist.CarCount >= 3 && Zone(PressureZone.Flank) < _t.MaxConcurrentZone)
            options.Add((EnemyKind.Clinger, 1));
        // App. B.3: Hounds need a rear car and sustained speed; weight up when the boiler runs hot.
        if (train.Dynamics.Consist.CarCount >= 1 && train.Dynamics.Speed >= 8 && Zone(PressureZone.Rear) < _t.MaxConcurrentZone
            && !active.Any(e => !e.Gone && e.Kind == EnemyKind.CinderHound))
        {
            double w = 1;
            if (train.BoilerTuning is { } bt && train.Boiler.Pressure > bt.WorkingBandMax)
                w *= _t.HoundsHotBoilerWeight;
            options.Add((EnemyKind.CinderHound, w));
        }
        // App. B.7: the Switchman works junctions on the Frontier and beyond, on a route with enough of them to have a
        // network, and there's never more than one corrupted human about. It needs a dead line's points ahead in its window.
        if (_route is { } r && r.Tier >= RouteTier.Frontier && world.Enemies is { } et
            && r.Branches.Count(b => b.Kind == Rail.BranchKind.DeadLine) >= et.Switchman.MinJunctions
            && !active.Any(e => !e.Gone && e.Kind == EnemyKind.Switchman) && Zone(PressureZone.Forward) < _t.MaxConcurrentZone
            && Switchman.Junction(world, et.Switchman) is not null)
            options.Add((EnemyKind.Switchman, 1));
        // App. B.6: Soot Children near facilities, never for a solo player, and only after someone's spoken lately (there
        // has to be a voice to steal). One at a time. Weight up per crew member outside.
        if (world.Enemies is { } st && NearFacility(s, st.SootChildren.NearFacility)
            && !active.Any(e => !e.Gone && e.Kind == EnemyKind.SootChildren) && Zone(PressureZone.Structural) < _t.MaxConcurrentZone
            && SootChildren.Choose(world, st.SootChildren, world.CrewThisTick) is not null)
            options.Add((EnemyKind.SootChildren, 1 + world.CrewThisTick.Count(c => c.State.Alive && PlayerMotor.Space(c.State, train) == PlayerMotor.Outside)));
        // App. B.4: Draggers under a train of two or more, woken by someone on the roofs. Weight up per roof walker.
        int onRoofs = world.CrewThisTick.Count(c => c.State is { Alive: true, Surface: Surface.Roof } r && r.Parent > 0);
        if (world.Enemies is { } dt && onRoofs > 0 && train.Dynamics.Consist.CarCount >= dt.Draggers.MinCars && Zone(PressureZone.Flank) < _t.MaxConcurrentZone
            && active.Count(e => !e.Gone && e.Kind == EnemyKind.Dragger) < dt.Draggers.MaxAttached)
            options.Add((EnemyKind.Dragger, onRoofs));
        // App. B.5: the Rattle takes a coupling gap during a facility stop, on a train of two or more (the engine's rake,
        // loading). One at a time. Weight up on longer consists.
        int rake = train.Dynamics.Consist.Vehicles.Count - 1;
        if (world.Enemies is { } rt && world.Run is { Phase: Run.RunPhase.AtFacility } && rake >= rt.Rattle.MinCars
            && !active.Any(e => !e.Gone && e.Kind == EnemyKind.Rattle) && Zone(PressureZone.Interior) < _t.MaxConcurrentZone
            && Rattle.Nests(world).Count > 0)
            options.Add((EnemyKind.Rattle, 1 + rt.Rattle.PerCarWeight * (rake - rt.Rattle.MinCars)));
        // App. B.6: Lamplighters work the lineside, any tier, only while there's a lit lamp to draw them (x0 with every light
        // out), and not in a tunnel or at a facility. Weight x2 at night depth (the back half of the route).
        if (world.Enemies is { } lt && world.LampShining && Zone(PressureZone.Structural) < _t.MaxConcurrentZone
            && active.Count(e => !e.Gone && e.Kind == EnemyKind.Lamplighter) < lt.Lamplighters.MaxActive
            && !(_route is { } lr && lr.Features.Any(f => f.Kind is FeatureKind.Tunnel or FeatureKind.Facility && f.Contains(s))))
            options.Add((EnemyKind.Lamplighter, _route is { } dr && s > dr.Length * 0.5 ? lt.Lamplighters.DepthWeight : 1));
        options.RemoveAll(o => Cost(o.Kind) > available);
        if (options.Count == 0)
            return null;

        double pick = _rng.NextDouble() * options.Sum(o => o.Weight);
        var kind = options[^1].Kind;
        foreach (var o in options)
        {
            if (pick < o.Weight)
            {
                kind = o.Kind;
                break;
            }
            pick -= o.Weight;
        }
        Charge(world, kind, active);
        _cooldown = _rng.Range(_t.CooldownSeconds[0], _t.CooldownSeconds[1]) / Math.Max(1e-6, RateMultiplier);
        return kind;
    }

    /// <summary>Condition-triggered enemies (the Hollow) cost budget only when they actually fire (App. B.5).</summary>
    public void Charge(World world, EnemyKind kind, IReadOnlyList<Enemy> active)
    {
        _spent += Cost(kind);
        var zone = kind switch
        {
            EnemyKind.CinderHound => PressureZone.Rear,
            EnemyKind.Clinger or EnemyKind.Dragger => PressureZone.Flank,
            EnemyKind.Switchman => PressureZone.Forward,
            EnemyKind.SootChildren or EnemyKind.Lamplighter => PressureZone.Structural,
            _ => PressureZone.Interior,
        };
        Log.Add(new DirectorSpawn(world.Tick, kind, Cost(kind), world.Train.Dynamics.Distance,
            active.Count(e => !e.Gone && e.Zone == zone && e.Kind != EnemyKind.Sleepers) + 1,
            active.Count(e => !e.Gone && e.Kind != EnemyKind.Sleepers) + 1));
    }

    public Pcg32 Rng => _rng;
    public double NextRange(double min, double max) => _rng.Range(min, max);
}
