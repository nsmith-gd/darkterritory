using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Enemies;

/// <summary>A decision the director made, for the harness's pacing and cap audit (App. B.9).</summary>
/// <param name="Paced">Sent because it had been quiet too long (<see cref="DirectorTuning.PaceSeconds"/>), not on the budget's curve.</param>
public readonly record struct DirectorSpawn(uint Tick, EnemyKind Kind, double Cost, double TrainDistance, int ActiveInZone, int ActiveTotal, bool Paced = false);

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
        EnemyKind.Deadman => "deadman",
        EnemyKind.Stoker => "stoker",
        EnemyKind.Ferryman => "ferryman",
        EnemyKind.CarFire => "carFire",
        EnemyKind.LooseLoad => "looseLoad",
        EnemyKind.Gnawers => "gnawers",
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
        // Quiet too long (after the playtest: a reward or a problem every 30 s at most, 20 s ideally): something now, the
        // cooldown and the budget's curve notwithstanding. The caps and each kind's gates still hold.
        bool due = world.QuietSeconds >= _t.PaceSeconds;
        if (elapsed < _t.GraceSeconds || _cooldown > 0 && !due)
            return null;
        if (_route is not null && s > _route.Length - noSpawnFinal)
            return null;
        int total = active.Count(Engaged);
        if (total >= MaxConcurrent)
            return null;
        double available = Allowance(s) - _spent;

        var options = new List<(EnemyKind Kind, double Weight)>();
        int cargoCars = train.Dynamics.Consist.Vehicles.Count(v => v.Kind == VehicleKind.Cargo);
        int Zone(PressureZone z) => active.Count(e => Engaged(e) && e.Zone == z);
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
        // App. B.5: the Stoker gets into the firebox during a stop with it unattended (nobody in the cab), any tier.
        if (world.Enemies is { } kt && train.BoilerTuning is not null && !train.Boiler.Ruptured && train.Dynamics.Speed < kt.Stoker.StoppedBelow
            && world.CabEmptySeconds >= kt.Stoker.UnattendedSeconds && Zone(PressureZone.Interior) < _t.MaxConcurrentZone
            && !active.Any(e => !e.Gone && e.Kind == EnemyKind.Stoker))
            options.Add((EnemyKind.Stoker, 1));
        // App. B.2: the Ferryman on the Frontier and beyond, mid-to-late run only, once per run, on a long straight with a clear
        // sightline, and only with a working forward lamp (not smashed). The train has to be coming on at some speed.
        if (_route is { } fr && fr.Tier >= RouteTier.Frontier && world.Enemies is { } ft && s >= fr.Length * ft.Ferryman.MidRunFrom
            && world.LampOutSeconds <= 0 && train.Dynamics.Speed >= ft.Ferryman.MinSpeed && Zone(PressureZone.Forward) < _t.MaxConcurrentZone
            && !Log.Any(l => l.Kind == EnemyKind.Ferryman) && Ferryman.ClearAhead(world, ft.Ferryman))
            options.Add((EnemyKind.Ferryman, 1));
        // The in-car incidents: a cargo car in the engine's rake, with a load for the ones that live in it, at most so many of
        // each about, and one of a kind a car. Fires come on more with the boiler hot and throwing cinders.
        if (world.Enemies is { } it && Zone(PressureZone.Interior) < _t.MaxConcurrentZone)
        {
            // Less often at a stop: the crew's all hands on the loading, and it's the facility's own threats' turn.
            // And less for a small crew, who've fewer hands to spare from the roofs and the gun (a crew of two is one hand).
            double hands = Math.Clamp((Crew - 1) / _t.IncidentFullCrew, 0.2, 1);
            double atStop = (world.Run is { Phase: Run.RunPhase.AtFacility } ? 0.4 : 1) * _t.IncidentWeight * hands;
            bool Room(EnemyKind kind, int max, double minLoad) => active.Count(e => !e.Gone && e.Kind == kind) < max
                && IncidentCars(world, kind, minLoad).Any();
            if (Room(EnemyKind.CarFire, it.CarFire.MaxActive, 0))
                options.Add((EnemyKind.CarFire, atStop * (train.BoilerTuning is { } fb && train.Boiler.Pressure > fb.WorkingBandMax ? 1.5 : 1)));
            if (Room(EnemyKind.LooseLoad, it.LooseLoad.MaxActive, it.LooseLoad.MinLoad))
                options.Add((EnemyKind.LooseLoad, atStop));
            if (Room(EnemyKind.Gnawers, it.Gnawers.MaxActive, it.Gnawers.MinLoad))
                options.Add((EnemyKind.Gnawers, atStop));
        }
        if (due && options.Count > 0)
            available = Math.Max(available, _t.PacedCost);
        options.RemoveAll(o => Cost(o.Kind) > available);
        // Sent because it's been quiet: something that shows itself at once. A Dragger under a car's edge, or a Rattle in its
        // gap, lies silent until someone comes near: that's no answer to a quiet night, if there's anything else to send.
        if (due && options.Any(o => o.Kind is not (EnemyKind.Dragger or EnemyKind.Rattle)))
            options.RemoveAll(o => o.Kind is EnemyKind.Dragger or EnemyKind.Rattle);
        if (options.Count == 0)
            return null;
        // Variety: a kind sent lately comes on less (the Lamplighters were half of everything in the playtest).
        var recent = Log.TakeLast(_t.VarietyWindow).Select(l => l.Kind).ToList();
        options = [.. options.Select(o => (o.Kind, o.Weight / Math.Pow(2, recent.Count(k => k == o.Kind))))];

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
        Charge(world, kind, active, paced: due && _cooldown > 0);
        _cooldown = _rng.Range(_t.CooldownSeconds[0], _t.CooldownSeconds[1]) / Math.Max(1e-6, RateMultiplier);
        return kind;
    }

    /// <summary>
    /// App. B.1's caps are on what's active: a Dragger lying dormant under a car's edge all night, a Rattle waiting in its
    /// gap, or a Lamplighter that's lost the light and only lingers, isn't pressure (the playtest found them holding the
    /// caps full, and the night went quiet).
    /// </summary>
    static bool Engaged(Enemy e) => !e.Gone && e.Kind != EnemyKind.Sleepers
        && e.Phase is SpinePhase.Alert or SpinePhase.Telegraph or SpinePhase.Commit or SpinePhase.Punish;

    /// <summary>Cargo cars in the engine's rake an incident of this kind could take: loaded enough, and without one already.</summary>
    public static IEnumerable<int> IncidentCars(World world, EnemyKind kind, double minLoad) =>
        world.Train.Dynamics.Consist.Vehicles.Where(v => v.Kind == VehicleKind.Cargo && v.Load >= minLoad
            && !world.ActiveEnemies.Any(e => !e.Gone && e.Kind == kind && e.Attached == v.Id)).Select(v => v.Id);

    /// <summary>Condition-triggered enemies (the Hollow) cost budget only when they actually fire (App. B.5).</summary>
    public void Charge(World world, EnemyKind kind, IReadOnlyList<Enemy> active, bool paced = false)
    {
        _spent += Cost(kind);
        var zone = kind switch
        {
            EnemyKind.CinderHound => PressureZone.Rear,
            EnemyKind.Clinger or EnemyKind.Dragger => PressureZone.Flank,
            EnemyKind.Switchman or EnemyKind.Ferryman => PressureZone.Forward,
            EnemyKind.SootChildren or EnemyKind.Lamplighter => PressureZone.Structural,
            _ => PressureZone.Interior,
        };
        Log.Add(new DirectorSpawn(world.Tick, kind, Cost(kind), world.Train.Dynamics.Distance,
            active.Count(e => Engaged(e) && e.Zone == zone) + 1, active.Count(Engaged) + 1, paced));
    }

    public Pcg32 Rng => _rng;
    public double NextRange(double min, double max) => _rng.Range(min, max);
}
