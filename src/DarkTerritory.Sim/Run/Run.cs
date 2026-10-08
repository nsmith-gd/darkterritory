using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Stops;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Run;

/// <summary>Mirror of content/tuning/run.json. Field docs live in that file.</summary>
public sealed record RunTuning(double StopBelowSpeed, double TerminusZone, double DawnGraceSeconds, ChuteTuning Chute, EconomyTuning Economy, double DepartureLoad)
{
    public const string File = "tuning/run.json";

    /// <summary>How far short of the outer gate a night's engine starts (run.json <c>departShortOfGateM</c>).</summary>
    public double DepartShortOfGateM { get; init; } = 8;

    /// <summary>
    /// The fortress yard is a safe space until the run begins (run.json <c>yardIsSafe</c>; the director's decision of 6 Oct
    /// 2026, ARCHITECTURE §8 note 263): nothing spawns, the boiler and the fire hold, the cold doesn't bite.
    /// </summary>
    public bool YardIsSafe { get; init; } = true;

    /// <summary>The forts are safe from creatures all night (GDD §9, T128; ARCHITECTURE §8 note 273): run.json <c>forts</c>.</summary>
    public FortTuning Forts { get; init; } = new();
    public WallTuning Walls { get; init; } = new();

    /// <summary>Stranded, unable to repair (GDD v1.4 §23.2): run.json <c>stranded</c>.</summary>
    public StrandedTuning Stranded { get; init; } = new();

    /// <summary>The run-end screen's bookmarks (GDD v1.4 App. D.12, D.13): run.json <c>bookmarks</c>.</summary>
    public BookmarkTuning Bookmarks { get; init; } = new();

    /// <summary>The dispatcher and the clerk on the radio (GDD §9): run.json <c>radio</c>.</summary>
    public RadioTuning Radio { get; init; } = new();

    /// <summary>
    /// Where a night's engine starts: its front just short of the gate, ready to depart (the whole train still in the yard,
    /// so the run begins as it moves off), or as far back as the consist needs to fit on the line.
    /// </summary>
    public double DepartFrom(double gate, double consistLength) => Math.Max(consistLength + 5, gate - DepartShortOfGateM);
}

public sealed record ChuteTuning(double LeverReach, double LeverSeconds, double SpoutTolerance, double PourPerSecond, double Capacity, double OverfillDamagePerUnit);

public sealed record EconomyTuning(Dictionary<string, double> PerCar, double CoalPerUnit, double RoundsPerRound, double RepairPerIntegrity)
{
    /// <summary>
    /// What a car-load of each cargo pays, as a share of the tier's per-car value (run.json <c>economy.cargoRates</c>; GDD §18,
    /// §19, App. B.9; note 182). A cargo not listed pays 1, as goods do (spec F.1's table).
    /// </summary>
    public Dictionary<string, double> CargoRates { get; init; } = new();

    /// <summary>A rescued child brought home (GDD §19, App. B.9 "highest payout"), in the tier's per-car values.</summary>
    public double ChildPay { get; init; }

    public double Rate(CargoKind cargo) => CargoRates.GetValueOrDefault(Cargoes.Key(cargo is CargoKind.None ? CargoKind.Goods : cargo), 1);
}

/// <summary>GDD §9: FORTRESS → WILDERNESS → FACILITY → WILDERNESS → TERMINUS.</summary>
public enum RunPhase : byte { Yard, Underway, AtFacility, Arrived, Failed }

/// <summary>
/// GDD §9 "forts must be safe spaces that monsters never enter" (T128; note 273): the departure fortress (the line up to its
/// outer gate) and the terminus (from its gate on, unless it's a silent settlement whose gate linegen doesn't keep safe), out
/// to <paramref name="HalfWidthM"/> either side of the line. Field docs live in run.json <c>forts</c>.
/// </summary>
public sealed record FortTuning(bool Safe = true, double HalfWidthM = 80);

/// <summary>Note 279: the stops' buildings as walls (<see cref="StopWalls"/>). Field docs live in run.json <c>walls</c>.</summary>
public sealed record WallTuning(double WallM = 0.3, double BayDoorM = 4, double PersonDoorM = 1.2, double WellTopM = 0.85, double TopM = 9, double LinesideReachM = 40);

/// <summary>How a night ends (GDD v1.4 §23): <see cref="Stranded"/> is a ruptured boiler with the engineering kit lost (§23.2).</summary>
public enum RunEnd : byte { None, Delivered, Derailed, CrewLost, DawnMissed, Stranded }

/// <summary>What a night came to (spec F.1): everything still attached to the locomotive counts.</summary>
/// <param name="Scavenged">Scrip for village finds stowed aboard (level-design P12), paid with the cargo on delivery and in Gross.</param>
/// <param name="Deaths">In-run deaths (GDD App. D.9), each charged <paramref name="CrewLossFees"/>' share; <paramref name="BodiesHome"/>
/// of their bodies came home aboard, refunding <paramref name="BodyRefunds"/>. Net is after both.</param>
/// <param name="Mail">Pay caught off the mail cranes (sight.json drops), paid with the cargo at the terminus and in the gross.</param>
/// <param name="Recovery">Stranded (GDD v1.4 §23.2): the dawn freight's bill for towing the train in.</param>
/// <param name="KitLoss">Stranded: how the engineering kit was lost.</param>
/// <param name="Lines">The incident report (GDD v1.4 App. D.12), in the clerk's words, from the failure-attribution log (C.9).</param>
public sealed record RunReport(RunEnd End, double Seconds, double DistanceKm, int CarsDelivered, int CarsLost, double CargoDelivered,
    double Gross, double CoalCost, double AmmoCost, double RepairCost, double Net, int CrewHome, int CrewLost, double Scavenged = 0,
    int Deaths = 0, int BodiesHome = 0, double CrewLossFees = 0, double BodyRefunds = 0, double Mail = 0, double Recovery = 0,
    KitLoss KitLoss = KitLoss.None)
{
    public IReadOnlyList<ReportLine> Lines { get; init; } = [];
    /// <summary>
    /// Repair kits home beyond the train's own (train.json kit.repairKits): spares the fortress sold that weren't lost, and
    /// kits found at stops and brought in (GDD v1.4 App. E.12 question 4). The campaign keeps them as its spares; −1 for a
    /// report that doesn't say (the campaign's spares stand as they were).
    /// </summary>
    public int SpareKitsHome { get; init; } = -1;
    /// <summary>Everyone's look at the night's end, by name (GDD v1.4 App. D.8; note 181): a freed survivor stays that survivor.</summary>
    public IReadOnlyDictionary<string, string> Identities { get; init; } = new Dictionary<string, string>();
    /// <summary>
    /// The bookmarks on the run-end screen (GDD v1.4 App. D.12): the automatic ones D.13's cap keeps (each beside its line,
    /// <see cref="ReportLine.Marks"/>) and the dead's own, in the order they were made.
    /// </summary>
    public IReadOnlyList<Bookmark> Bookmarks { get; init; } = [];
    /// <summary>Rescued children brought home (GDD §19, App. B.9), and what they paid (in the gross: <c>economy.childPay</c> each).</summary>
    public int ChildrenHome { get; init; }
    public double ChildPay { get; init; }
}

/// <summary>
/// One night's run, host-authoritative (clients mirror it for the HUD). The yard gate opens the run and
/// starts the dawn clock; facilities are stops you choose to make; the terminus ends it well; derailment,
/// losing the whole crew, or still being out when the line goes live ends it badly.
/// </summary>
public sealed partial class Run
{
    readonly Route.Route _route;
    readonly List<RouteFeature> _facilities;
    readonly double[] _chuteLeft;
    double _tenderAtDeparture, _coalLoaded;
    int _ammoAtDeparture;

    public Run(RunTuning tuning, Route.Route route)
    {
        Tuning = tuning;
        _route = route;
        _facilities = route.Of(FeatureKind.Facility).ToList();
        _chuteLeft = _facilities.Select(f => f.Facility == FacilityKind.CoalingTower ? tuning.Chute.Capacity : 0).ToArray();
        // Each facility's spur, if it has one: the branch whose points are in its zone.
        _spurs = [.. _facilities.Select(f => route.Branches.Select((b, i) => (b, i)).Where(x => x.b.Kind == BranchKind.Spur && f.Contains(x.b.Toe))
            .Select(x => x.i).DefaultIfEmpty(RailLine.MainPath).First())];
        // A generated yard has several (level-design P5): the engine on any of them is at the facility.
        _yards = [.. _facilities.Select(f => route.Branches.Select((b, i) => (b, i)).Where(x => x.b.Kind == BranchKind.Spur && f.Contains(x.b.Toe))
            .Select(x => x.i).ToArray())];
        _visited = new bool[_facilities.Count];
        _departed = new bool[_facilities.Count];
    }

    readonly int[] _spurs;
    readonly int[][] _yards;
    readonly bool[] _visited, _departed;

    /// <summary>The branch a facility's machinery is on (GDD §17), or <see cref="RailLine.MainPath"/> for one on the main line.</summary>
    public int SpurOf(int facility) => facility >= 0 && facility < _spurs.Length ? _spurs[facility] : RailLine.MainPath;

    /// <summary>
    /// Down a mine head's spur, where the radio dies (spec A.5 "dies in tunnels and mine spurs"; GDD §17 "the spur
    /// descends underground. Radio blackout in and out"): aboard a rake standing on it, or on the ground beside it.
    /// </summary>
    /// <param name="reach">How far down the spur from its points a radio still carries (train.json kit.radioReach; F.3's
    /// radio range, note 196). With none, anywhere on the mine head's spur's path.</param>
    public bool Underground(in PlayerState s, TrainOnLine train, double reach = 0)
    {
        int path;
        double along;
        if (s.Parent != PlayerState.World && s.Parent < train.Vehicles.Count)
            (path, along) = (train.RakeOf(s.Parent).Path, train.Cars[s.Parent].FrontDistance);
        else
        {
            double hint = s.LineHint;
            (path, along) = train.Line.Nearest(s.Position, ref hint);
        }
        if (path == RailLine.MainPath)
            return false;
        for (int i = 0; i < _facilities.Count; i++)
            if (_spurs[i] == path && _facilities[i].Facility == FacilityKind.MineHead)
                return reach <= 0 || along - train.Line.Branches[path].Toe >= reach;
        return false;
    }

    /// <summary>
    /// The facility the train last left: stopped at it, then its engine out past the end of its zone on the main line
    /// (spec E's "leaving a POI", where the night autosaves). −1 until one has been.
    /// </summary>
    public int Departed { get; private set; } = -1;
    /// <summary>How many facilities the train has left (a departure is when this goes up).</summary>
    public int Departures { get; private set; }

    public RunTuning Tuning { get; set; }
    public Route.Route Route => _route;

    /// <summary>Each facility's loading modules (spec D), by facility index; null where there are none (the coaling tower).</summary>
    public IReadOnlyList<Site?> Sites => _sites;
    Site?[] _sites = [];
    FacilityTuning? _facilityTuning;
    // How long each loose cargo crate has lain still inside a cargo car.
    readonly Dictionary<int, double> _settling = new();

    /// <summary>Lays out every facility's loading modules from the route. Host and clients both do this; the host runs them.</summary>
    public void EnableSites(FacilityTuning t, RailLine line)
    {
        _facilityTuning = t;
        _sites = [.. _facilities.Select((f, i) =>
        {
            var modules = t.ModulesOf(f);
            if (modules.Count == 0)
                return null;
            int span = Math.Max(0, t.Crates.Count[1] - t.Crates.Count[0]);
            int crates = t.Crates.Count[0] + (int)((_route.Seed * 31 + (ulong)i * 17) % (ulong)(span + 1));
            var h = t.Crates.Heavy.Count;
            int heavy = h[0] + (int)((_route.Seed * 13 + (ulong)i * 29) % (ulong)(Math.Max(0, h[1] - h[0]) + 1));
            var hd = t.Ramp.Head;
            int head = hd[0] + (int)((_route.Seed * 7 + (ulong)i * 23) % (ulong)(Math.Max(0, hd[1] - hd[0]) + 1));
            // The wreck yard's heaps, and the salvage in each (note 187).
            int[]? salvage = modules.Contains(ModuleKind.Wreck) ? Salvage(t.Wreck, i) : null;
            if (_spurs[i] >= 0 && _spurs[i] < line.Branches.Count)
            {
                // Laid out from where the first cars stand with the engine up at the buffer stop.
                var spur = line.Branches[_spurs[i]];
                double mid = spur.Local.Length - t.SpurLayout;
                // The spout stands back along the track the cars stand on to be worked (level-design P16), clear of the points.
                double room = spur.Definition.Standing ?? spur.Local.Length - 14;
                return new Site(i, f, modules, t, spur.Local, mid, spur.Side, spur.Toe + mid, crates, spur.Index, heavy, room, head, salvage);
            }
            int side = f.Side == 0 ? 1 : f.Side;
            double centre = (f.Start + f.End) / 2;
            return new Site(i, f, modules, t, line, centre, side, centre, crates, heavy: heavy, head: head, salvage: salvage);
        })];
        // A generated yard's power and its powerhouse (level-design D.2), the door on the face towards the main line.
        foreach (var site in _sites)
            if (site?.Feature.Stop is { HasYard: true } stop)
            {
                site.Power = stop.Power;
                if (stop.Powerhouse >= 0 && stop.Buildings[stop.Powerhouse] is var ph)
                    site.Powerhouse = StopWorld(line, site.Feature, StopGenerator.DoorOf(ph, new Pt(ph.S, 0)));
            }
    }

    public Site? CurrentSite => Facility >= 0 && Facility < _sites.Length ? _sites[Facility] : null;
    public RunPhase Phase { get; private set; }
    public RunEnd End { get; private set; }
    /// <summary>Seconds since the gates opened (the dawn clock runs from departure).</summary>
    public double Seconds { get; private set; }
    /// <summary>Seconds at the facility this stop (the Gaunt comes on long stops, v1.1 App. B.6); 0 away from one.</summary>
    public double StopSeconds { get; private set; }
    /// <summary>The loading machinery going (the winch turning, the crane's hook moving): it's loud (v1.1 App. C.7).</summary>
    public bool Machinery => CurrentSite is { } site && (site.Turning || site.Crane?.Hooked is not null || site.Pouring || site.Herding);
    public double DawnIn => _route.DawnSeconds - Seconds;
    public bool LineLive => Seconds >= _route.DawnSeconds;
    /// <summary>The facility the train is stopped at, or −1.</summary>
    public int Facility { get; private set; } = -1;
    public RouteFeature? FacilityFeature => Facility >= 0 ? _facilities[Facility] : null;
    public bool ChuteOpen { get; private set; }
    public double ChuteLeft(int facility) => facility >= 0 && facility < _chuteLeft.Length ? _chuteLeft[facility] : 0;
    public bool Over => Phase is RunPhase.Arrived or RunPhase.Failed;
    /// <summary>Where the engineering kit is, as of this tick (host; GDD v1.4 §23.2).</summary>
    public KitWhere Kit { get; private set; }
    double _kitLostFor;
    bool _kitStocked;
    bool _wasRuptured;
    public RunReport? Report { get; private set; }

    /// <summary>Advances the run after the world has stepped. <paramref name="crew"/> is everyone's authoritative state.</summary>
    /// <summary>Pay in the mail bags caught so far tonight (sight.json drops): it pays at the terminus with the cargo.</summary>
    public double Mail { get; private set; }
    public void AddSalvage(double scrip) => Mail += scrip;

    public void Step(World world, IReadOnlyCollection<PlayerState> crew, double dt)
    {
        if (Over)
            return;
        var train = world.Train;
        var engine = EngineRake(train);
        double front = engine.Distance;
        if (Phase == RunPhase.Yard)
        {
            // The gates: past the end of the fortress yard, the run has begun.
            if (front < YardLength)
                return;
            Phase = RunPhase.Underway;
            _tenderAtDeparture = train.Boiler.Tender;
            _ammoAtDeparture = train.Vehicles.Sum(v => v.Gun.Ammo);
        }
        Seconds += dt;
        StopSeconds = Phase == RunPhase.AtFacility ? StopSeconds + dt : 0;
        Pour(train, dt);
        StepSites(world, dt);
        StepLoot(world, dt);

        // §23.2: the check runs every tick. Ruptured with the kit lost, the night ends once the train comes to rest (the
        // crew get the whole coast to work out what just happened).
        _kitStocked |= world.KitStocked || world.Bodies.All.Any(b => b.Kind == Physics.BodyKind.RepairKit && b.Claimed);
        Kit = EngineeringKit.Where(world, crew, Tuning.Stranded, _kitStocked);
        // App. C.9: a rupture, with who last fired or vented it, and how long it sat at 100 (the spec's hold, by then).
        if (train.Boiler.Ruptured && !_wasRuptured && train.BoilerTuning is { } bt)
        {
            var a = world.Attribution;
            world.Attribution.Add(new Incident(IncidentKind.Rupture, Seconds, -1, "Boiler ruptured",
                IncidentLog.At(world, train.Frames[0].Origin, front), a.Fireman,
                a.Fireman >= 0 ? $"Last fired: {{actor}}. At {bt.PressureMax:0} for {bt.RuptureHoldSeconds:0} s." : "Nobody had fired it."));
        }
        _wasRuptured = train.Boiler.Ruptured;
        _kitLostFor = Kit.Lost ? _kitLostFor + dt : 0;
        // Note 301: where the wrench mends the boiler (and everyone carries one), a lost kit strands nobody.
        bool stranded = !Train.Repairs.ByWrench(train) && train.Boiler.Ruptured && _kitLostFor >= Tuning.Stranded.LostForSeconds
            && engine.Speed < Tuning.StopBelowSpeed;

        if (world.Derailed)
            Finish(world, crew, RunPhase.Failed, RunEnd.Derailed);
        else if (crew.Count > 0 && crew.All(c => !c.Alive))
            Finish(world, crew, RunPhase.Failed, RunEnd.CrewLost);
        else if (stranded)
            Finish(world, crew, RunPhase.Failed, RunEnd.Stranded);
        else if (Seconds > _route.DawnSeconds + Tuning.DawnGraceSeconds)
            Finish(world, crew, RunPhase.Failed, RunEnd.DawnMissed);
        else if (engine.Speed < Tuning.StopBelowSpeed && train.OnMain && front >= _route.Length - Tuning.TerminusZone)
            Finish(world, crew, RunPhase.Arrived, RunEnd.Delivered);
        else
        {
            int at = engine.Speed < Tuning.StopBelowSpeed ? StoppedAt(train, front) : -1;
            Facility = at;
            Phase = at >= 0 ? RunPhase.AtFacility : RunPhase.Underway;
            if (at < 0)
                ChuteOpen = false;
            else
                _visited[at] = true;
            // Left one behind: out past the end of its zone on the main line, after stopping there.
            if (train.OnMain)
                for (int i = 0; i < _facilities.Count; i++)
                    if (_visited[i] && !_departed[i] && front > _facilities[i].End)
                    {
                        _departed[i] = true;
                        Departed = i;
                        Departures++;
                    }
        }
    }

    /// <summary>
    /// The facility the engine is stopped at: on its spur, for one with a spur (GDD §17: the machinery is down there,
    /// not on the main line); in its zone on the main line, for the coaling tower. Down a dead line is nowhere.
    /// </summary>
    int StoppedAt(TrainOnLine train, double front)
    {
        int path = train.Dynamics.Path;
        for (int i = 0; i < _facilities.Count; i++)
            if (_spurs[i] >= 0 ? _yards[i].Contains(path) && !train.OnMain : train.OnMain && _facilities[i].Contains(front))
                return i;
        return -1;
    }

    /// <summary>
    /// Spec D loading: crates come out when the train first stops at their facility; the winch hauls only while both
    /// handles turn; cargo that ends up still inside a cargo car is stowed as load.
    /// </summary>
    void StepSites(World world, double dt)
    {
        if (_facilityTuning is not { } t)
            return;
        var train = world.Train;
        StepLoading(world, t, dt);
        StepSetPieces(world, t, dt);
        // Whatever went into a car this tick (a sled, a casting, a crate) is this facility's cargo (App. B.8).
        if (FacilityFeature?.Facility is { } kind)
        {
            var cargo = t.CargoOf(kind);
            foreach (var v in train.Vehicles)
                if (v.Kind == VehicleKind.Cargo && v.Load > _loadSeen.GetValueOrDefault(v.Id, v.Load) + 1e-9)
                    v.Cargo = cargo;
        }
        foreach (var v in train.Vehicles)
            _loadSeen[v.Id] = v.Load;
    }

    readonly Dictionary<int, double> _loadSeen = new();

    /// <summary>A facility's crates and heavy crates out on its platform, as its cargo (spec D; note 352 for when).</summary>
    static void StockSite(World world, FacilityTuning t, Site site)
    {
        site.Stocked = true;
        var cargo = site.Feature.Facility is { } facility ? t.CargoOf(facility) : CargoKind.None;
        foreach (var at in site.CrateStack)
            world.Bodies.SpawnCargo(at, site.CrateLineHint, cargo: cargo);
        foreach (var at in site.HeavyStack)
            world.Bodies.SpawnCargo(at, site.CrateLineHint, t.Crates.Heavy.Radius, cargo);
    }

    void StepLoading(World world, FacilityTuning t, double dt)
    {
        var train = world.Train;
        world.Bodies.HeavySpan = t.Crates.Heavy.Span;
        // The facility's crates come out with its stop's loot, before the train's near enough to see them (note 352).
        if (_loot is { StockAhead: > 0 } loot)
        {
            var engine = EngineRake(train);
            foreach (var s in _sites)
                if (s is { Stocked: false } && Due(s.Feature, train, engine, loot.StockAhead))
                    StockSite(world, t, s);
        }
        if (CurrentSite is { } site)
        {
            if (!site.Stocked && Phase == RunPhase.AtFacility)
                StockSite(world, t, site);
            Crank(site, t.Winch, dt);
            Restart(world, site, t.Power, dt);
            _drop = null;
            foreach (var crane in site.Cranes)
            {
                crane.SpeedScale = site.Power switch { PowerState.Live => 1, PowerState.Low => t.Power.LowSpeed, _ => 0 };
                Operate(world, crane, dt);
            }
            if (site.Progress >= 1 && site.SledsLeft > 0 && CargoCarNear(train, site.SledTo, t.Winch.CarReach) is { } car)
            {
                car.Load = Math.Min(1, car.Load + t.Winch.LoadPerSled);
                site.SledsLeft--;
                site.Progress = 0;
            }
        }
        foreach (var s in _sites)
            if (s is not null)
            {
                s.Cranking[0] = s.Cranking[1] = -1;
                s.HandAngle[0] = s.HandAngle[1] = null;
            }

        // A crate put down (or thrown) inside a cargo car's walls, and lying still there, is loaded.
        foreach (var b in world.Bodies.All.Where(b => b.Kind is Physics.BodyKind.Cargo or Physics.BodyKind.Heavy).ToList())
        {
            bool stowed = b.Carrier < 0 && b.Parent > 0 && b.Parent < train.Vehicles.Count && train.Vehicles[b.Parent].Kind == VehicleKind.Cargo
                && train.Vehicles[b.Parent].Load < 1 && train.Frames[b.Parent].Shape.Interior is { } room && room.Contains(b.Pbd.Particles[0].Position);
            double still = stowed ? _settling.GetValueOrDefault(b.Id) + dt : 0;
            if (still < t.Crates.SettleSeconds)
            {
                _settling[b.Id] = still;
                continue;
            }
            var vehicle = train.Vehicles[b.Parent];
            vehicle.Load = Math.Min(1, vehicle.Load + (b.Kind == Physics.BodyKind.Heavy ? t.Crates.Heavy.LoadPerCrate : t.Crates.LoadPerCrate));
            world.Bodies.Remove(b);
            _settling.Remove(b.Id);
        }
    }

    PlayerIntent _craneIntent;

    /// <summary>
    /// The crane this tick (T48): whoever's at the controls drives it, and Fire lets the hook go. A casting let go of high
    /// falls, and it kills whoever's under where it lands: spec D.2 "dropped loads kill".
    /// </summary>
    /// <summary>
    /// Spec D.1's restart excursion (level-design D.2): someone holding Use at the powerhouse door, for as long as it
    /// takes, loudly, and the yard's power is live. Letting go starts it over.
    /// </summary>
    void Restart(World world, Site site, PowerTuning t, double dt)
    {
        if (site.Power == PowerState.Live || site.Restarter < 0)
        {
            site.Restart = 0;
            site.Restarter = -1;
            return;
        }
        site.Restart += dt;
        if (world.Combat is { } c)
            world.Choir.Loud(c.Choir, t.RestartRounds, dt);
        if (site.Restart >= t.RestartSeconds)
        {
            site.Power = PowerState.Live;
            site.Restart = 0;
        }
        site.Restarter = -1;
    }

    public PowerTuning PowerTuning => _facilityTuning?.Power ?? new();

    /// <summary>At a yard whose power's down, within reach of its powerhouse door (on foot).</summary>
    public bool PowerhouseInReach(in PlayerState s, TrainOnLine train) =>
        CurrentSite is { Power: not PowerState.Live, Powerhouse: { } door } && s.Alive && s.Parent == PlayerState.World
        && ((PlayerMotor.WorldPosition(s, train) - door) with { Y = 0 }).Length <= (_facilityTuning?.Power.Reach ?? 2);

    void Operate(World world, Crane crane, double dt)
    {
        if (crane.Operator >= 0)
        {
            crane.Drive(_craneIntent, world.Train, dt);
            if (crane.Pressed(_craneIntent.Has(PlayerButtons.Fire)) && crane.Release(world.Train) is { Fell: true } drop)
                _drop = (drop.Landed, crane.Tuning.CrushRadius);
        }
        else
            crane.Pressed(false);
        crane.Operator = -1;
    }

    // Where a casting let go of high came down last tick, and how wide it hits: each player's next CrewAct asks.
    (Double3 At, double Radius)? _drop;

    /// <summary>Under a casting that's just come down (T48): the host's crew step asks, and the world applies the hit.</summary>
    public bool Crushes(in PlayerState s, TrainOnLine train)
    {
        if (_drop is not { } d || !s.Alive)
            return false;
        var at = PlayerMotor.WorldPosition(s, train);
        return ((at - d.At) with { Y = 0 }).Length <= d.Radius && at.Y < d.At.Y + 2.5;
    }

    /// <summary>
    /// The drum (spec D.2, T43): each manned crank has a pace, a keyboard's hold the crank's own, a reaching hand's how
    /// fast it's going round (smoothed, forward only, no faster than the crank's pace: a headset hauls no faster than a
    /// keyboard). The drum goes at the slower one's pace, and stalls when the two are out of rhythm.
    /// </summary>
    static void Crank(Site site, WinchTuning w, double dt)
    {
        var c = w.Crank;
        for (int i = 0; i < 2; i++)
        {
            if (site.Cranking[i] < 0 || site.SledsLeft == 0)
            {
                site.Pace[i] = 0;
                site.LastAngle[i] = null;
            }
            else if (site.HandAngle[i] is { } angle)
            {
                double turned = site.LastAngle[i] is { } last ? Math.IEEERemainder(angle - last, 2 * Math.PI) : 0;
                double raw = Math.Clamp(turned / (2 * Math.PI * dt), -3 * c.RevsPerSecond, 3 * c.RevsPerSecond);
                site.LastAngle[i] = angle;
                site.Pace[i] += (raw - site.Pace[i]) * Math.Min(1, dt / c.SmoothSeconds);
            }
            else
            {
                site.Pace[i] = c.RevsPerSecond;
                site.LastAngle[i] = null;
            }
        }
        double a = Math.Clamp(site.Pace[0], 0, c.RevsPerSecond), b = Math.Clamp(site.Pace[1], 0, c.RevsPerSecond);
        double slow = Math.Min(a, b), fast = Math.Max(a, b);
        bool manned = site.SledsLeft > 0 && site.Cranking[0] >= 0 && site.Cranking[1] >= 0 && site.Cranking[0] != site.Cranking[1];
        site.Turning = manned && slow > 0 && slow >= c.InRhythm * fast;
        site.OutOfRhythm = manned && !site.Turning;
        if (!site.Turning)
            return;
        site.Crank = (site.Crank + slow * 2 * Math.PI * dt) % (2 * Math.PI);
        if (site.Progress < 1)
            site.Progress = Math.Min(1, site.Progress + slow / c.RevsPerSecond * w.Speed * dt / w.HaulMetres);
    }

    /// <summary>
    /// Whether a sled hauled now would go anywhere (T66): a cargo car with room within reach of where the sleds come in. With
    /// the cars there full (the crane's castings went on them, say), cranking on hauls nothing.
    /// </summary>
    public bool SledHasRoom(TrainOnLine train, Site site) =>
        _facilityTuning is { } t && CargoCarNear(train, site.SledTo, t.Winch.CarReach) is not null;

    /// <summary>
    /// Where a sled's load goes: into the cargo car with room nearest the sled, in the train standing by it (one of its
    /// vehicles within reach). The load's handed along the train to it: a train can't put its cars in a different order at
    /// a spur, so the cars nearest the winch fill at the first stop and the next winch loads the ones behind them.
    /// </summary>
    static Vehicle? CargoCarNear(TrainOnLine train, Double3 at, double reach)
    {
        double Distance(Vehicle v) => (train.Frames[v.Id].Origin - at).Length;
        var rake = train.Rakes.FirstOrDefault(r => !train.Standing(r) && r.Consist.Vehicles.Any(v => Distance(v) <= reach));
        return rake?.Consist.Vehicles.Where(v => v.Kind == VehicleKind.Cargo && v.Load < 1).OrderBy(Distance).FirstOrDefault();
    }

    /// <summary>The fortress yard's length, from generation (RouteTuning.YardLength); the gate is its end.</summary>
    public double YardLength { get; init; } = 600;

    static TrainDynamics EngineRake(TrainOnLine train) => train.Rakes.First(r => r.Consist.HasEngine);

    /// <summary>Where the coaling spout is over the line, and where its lever stands, for a facility.</summary>
    public (double SpoutAlong, Double3 Lever) ChuteAt(RouteFeature f, RailLine line)
    {
        double mid = (f.Start + f.End) / 2;
        var t = line.Sample(mid + 6);
        var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
        return (mid, t.Position + right * (f.Side * 3.2) + Double3.Up * 0.9);
    }

    /// <summary>
    /// A player's hands on the chute lever: holding Use near it for a moment opens the chute (or shuts it).
    /// Called from <see cref="World.CrewAct"/> before crew actions on the train.
    /// </summary>
    /// <param name="hand">When hands are reported (T29), a reaching hand has to be on the handle or the lever.</param>
    public void CrewAct(in PlayerState s, in PlayerIntent intent, int playerId, TrainOnLine train, HandTuning? hand = null)
    {
        SetPiecesAct(s, intent, playerId, train, hand);
        if (intent.Has(PlayerButtons.Use) && PowerhouseInReach(s, train) && CurrentSite is { } powered
            && (powered.Restarter < 0 || playerId < powered.Restarter))
            powered.Restarter = playerId;
        if (CurrentSite is { } site && CrankInReach(s, train, hand) is { } crank && intent.Has(PlayerButtons.Use) && intent.MoveZ <= 0.5)
        {
            site.Cranking[crank.Handle] = playerId;
            site.HandAngle[crank.Handle] = crank.Angle;
        }
        // The crane (T48): at its controls, the operator's intent drives it this tick; on the ground at the hook, rigging.
        if (!Over && CurrentSite is { } here && s.Alive)
            foreach (var crane in here.Cranes)
            {
                if (crane.AtControls(s, intent, train))
                {
                    crane.Operator = playerId;
                    _craneIntent = intent;
                }
                else if (s.Parent == PlayerState.World)
                    crane.Rig(playerId, PlayerMotor.WorldPosition(s, train), intent.Has(PlayerButtons.Use) && intent.MoveZ <= 0.5, SimConstants.TickSeconds);
            }
        bool holding = LeverInReach(s, train, hand) && intent.Has(PlayerButtons.Use) && intent.MoveZ <= 0.5;
        if (!holding)
        {
            _lever.Remove(playerId);
            return;
        }
        double before = _lever.GetValueOrDefault(playerId);
        _lever[playerId] = before + SimConstants.TickSeconds;
        if (before < Tuning.Chute.LeverSeconds && before + SimConstants.TickSeconds >= Tuning.Chute.LeverSeconds)
            ChuteOpen = !ChuteOpen;
    }

    /// <summary>The capstan handle a player is standing at, if the winch here has cargo left to haul.</summary>
    public int? HandleInReach(in PlayerState s, TrainOnLine train, HandTuning? hand = null) => CrankInReach(s, train, hand)?.Handle;

    /// <summary>
    /// The crank a player has hold of: standing at its hub, or (a reaching hand, T43) with the hand on the circle its grip
    /// goes round, and then the hand's angle round it.
    /// </summary>
    (int Handle, double? Angle)? CrankInReach(in PlayerState s, TrainOnLine train, HandTuning? hand)
    {
        if (Over || !s.Alive || CurrentSite is not { SledsLeft: > 0 } site || _facilityTuning is not { } t)
            return null;
        if (hand is not null && PlayerMotor.HandWorld(s, train) is { } h)
        {
            for (int i = 0; i < site.Handles.Length; i++)
                if (site.OnCrank(i, h, hand.Grab) is { } angle)
                    return (i, angle);
            return null;
        }
        var at = PlayerMotor.WorldPosition(s, train);
        for (int i = 0; i < site.Handles.Length; i++)
            if ((at + Double3.Up * 0.9 - site.Handles[i]).Length <= t.Winch.HandleReach)
                return (i, null);
        return null;
    }

    /// <summary>Standing at a working chute's lever (the HUD's prompt, and <see cref="CrewAct"/>).</summary>
    public bool LeverInReach(in PlayerState s, TrainOnLine train, HandTuning? hand = null)
    {
        if (Over || !s.Alive || Facility < 0 || _chuteLeft[Facility] <= 0)
            return false;
        var lever = ChuteAt(_facilities[Facility], train.Line).Lever;
        return PlayerMotor.Grips(s, train, hand, lever, (PlayerMotor.WorldPosition(s, train) - lever).Length <= Tuning.Chute.LeverReach);
    }

    // Who has a hand on the lever, and for how long (the ground isn't part of the train, so not ActionProgress).
    readonly Dictionary<int, double> _lever = new();

    /// <summary>Coal falls while the chute's open: into the tender if it's under the spout, onto the ballast if not.</summary>
    void Pour(TrainOnLine train, double dt)
    {
        if (!ChuteOpen || Facility < 0)
            return;
        var c = Tuning.Chute;
        double pour = Math.Min(c.PourPerSecond * dt, _chuteLeft[Facility]);
        _chuteLeft[Facility] -= pour;
        if (_chuteLeft[Facility] <= 1e-9)
            ChuteOpen = false;
        var engine = EngineRake(train);
        var geometry = train.Dynamics.Tuning.Geometry;
        double tender = engine.Distance - EnginePlan.Of(geometry).CoalFromFront;
        var (spout, _) = ChuteAt(_facilities[Facility], train.Line);
        if (Math.Abs(tender - spout) > c.SpoutTolerance || train.BoilerTuning is not { } bt)
            return; // spilt
        double room = bt.TenderCapacity - train.Boiler.Tender;
        double taken = Math.Min(pour, Math.Max(0, room));
        train.Boiler.Tender += taken;
        _coalLoaded += taken;
        // Overfill: it keeps coming, and it has to go somewhere.
        var vehicle = train.Vehicles[0];
        vehicle.Integrity = Math.Max(0, vehicle.Integrity - (pour - taken) * c.OverfillDamagePerUnit);
    }

    void Finish(World world, IReadOnlyCollection<PlayerState> crew, RunPhase phase, RunEnd end)
    {
        Phase = phase;
        End = end;
        ChuteOpen = false;
        var train = world.Train;
        var a = world.Attribution;
        string where = IncidentLog.At(world, train.Frames[0].Origin, EngineRake(train).Distance);
        // App. C.9's whole-train rows, and E.5's cause card: what took the train off the rails, at what speed, and who drove.
        if (end == RunEnd.Derailed)
            a.Add(new Incident(IncidentKind.Derailed, Seconds, -1, $"Consist derailed, {Kmh(world.DerailSpeed)}", where, world.DerailActor,
                $"{Capital(world.DerailCause ?? "cause not established")}. {(world.DerailAction is { Length: > 0 } blame ? blame : "Throttle: {actor}.")}"));
        else if (end == RunEnd.Stranded)
        {
            int coupler = Kit.Loss == KitLoss.LeftBehind && Kit.Vehicle > 0 ? a.CouplerPulledBy(Kit.Vehicle) : -1;
            string pulled = coupler >= 0 ? $" Coupler: {IncidentLog.NameOf(world, coupler)}." : "";
            a.Add(new Incident(IncidentKind.Stranded, Seconds, -1, "Consist stranded", where, a.KitHolder,
                $"{Capital(EngineeringKit.Line(Kit.Loss).ToLowerInvariant())}. Last held: {{actor}}.{pulled}"));
        }
        // D.12: a derailment's still of each crew member, or the outro's last frame.
        if (world.Authority)
            world.Bookmarks.End(world, end, world.LastCrew);
        Report = Tally(world, crew);
    }

    static string Kmh(double metresPerSecond) => $"{Math.Abs(metresPerSecond) * 3.6:0} km/h";
    static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>Spec F.1: pay on delivered cargo that survives; running costs are what the night burned and broke.</summary>
    public RunReport Tally(World world, IReadOnlyCollection<PlayerState> crew)
    {
        var train = world.Train;
        var engine = EngineRake(train);
        var attached = engine.Consist.Vehicles.Select(v => v.Id).ToHashSet();
        var e = Tuning.Economy;
        string tier = char.ToLowerInvariant(_route.Tier.ToString()[0]) + _route.Tier.ToString()[1..];
        double perCar = e.PerCar.GetValueOrDefault(tier, 700);
        // A switchyard's cars nobody coupled up to were never the crew's to lose (note 187).
        var standing = train.Rakes.Where(train.Standing).SelectMany(r => r.Consist.Vehicles).Select(v => v.Id).ToHashSet();
        // Nor a blocked siding's derelicts (note 294), unless they're brought home on the train.
        var cargo = train.Vehicles.Where(v => v.Kind == VehicleKind.Cargo && !standing.Contains(v.Id) && (!v.Derelict || attached.Contains(v.Id))).ToList();
        var home = cargo.Where(v => attached.Contains(v.Id)).ToList();
        bool delivered = End == RunEnd.Delivered;
        double cargoValue = home.Sum(v => v.Load * v.CargoIntegrity);
        // Each car pays by what's in it (run.json economy.cargoRates; note 182): the contract's freight, or a facility's.
        double freightPay = perCar * home.Sum(v => v.Load * v.CargoIntegrity * e.Rate(v.Cargo));
        // GDD §19, B.9: a rescued child home (in a car still on the engine, or in someone's arms) is the night's best pay.
        int children = delivered ? world.Bodies.All.Count(b => b.Kind == Physics.BodyKind.Child && (b.Carrier >= 0 || attached.Contains(b.Parent))) : 0;
        double childPay = Math.Round(children * e.ChildPay * perCar);
        double gross = delivered ? freightPay + childPay + Scavenged + Mail : 0;
        double coal = Math.Max(0, _tenderAtDeparture + _coalLoaded - train.Boiler.Tender) * e.CoalPerUnit;
        double ammo = Math.Max(0, _ammoAtDeparture - train.Vehicles.Sum(v => v.Gun.Ammo)) * e.RoundsPerRound;
        // A derelict was battered before the night began (note 294): not the crew's to mend.
        double repairs = train.Vehicles.Where(v => attached.Contains(v.Id) && !v.Derelict).Sum(v => 1 - v.Integrity) * e.RepairPerIntegrity;
        // Home is aboard, or at least with the train: someone mid-jump between roofs, or who stepped down at
        // the terminus, made it. Anyone further than this from every attached car didn't.
        const double WithTheTrain = 40;
        // Stranded (§23.2): the dawn freight tows the train in, and the living come home with it, wherever they're stood.
        bool stranded = End == RunEnd.Stranded;
        // Derailed, nobody comes home: the living on the derail tick are the wreck's, and die in it (App. E.2 step 1).
        int crewHome = End == RunEnd.Derailed ? 0 : crew.Count(c => c.Alive && (stranded || c.Parent != PlayerState.World && attached.Contains(c.Parent)
            || attached.Any(id => (train.Frames[id].Origin - PlayerMotor.WorldPosition(c, train)).Length < WithTheTrain)));
        double recovery = stranded ? Math.Round(Tuning.Stranded.RecoveryFee * perCar) : 0;
        // GDD App. D.9, bodies as loot: every death costs the crew a fee; every body brought home (stowed in a car still on
        // the engine, or carried aboard) refunds most of it, never all. A drop-out's body is neither.
        int deaths = 0, bodiesHome = 0;
        double fees = 0, refunds = 0, feeEach = 0, refundEach = 0;
        if (world.Holdouts?.Tuning is { } ht)
        {
            deaths = world.Bodies.Deaths;
            double fee = ht.CrewLossFee * perCar;
            feeEach = Math.Round(fee);
            refundEach = Math.Round(ht.BodyRefund * fee);
            bodiesHome = delivered ? world.Bodies.All.Count(b => b.Kind == Physics.BodyKind.Ragdoll && !b.DroppedOut
                && (attached.Contains(b.Parent) || b.Carrier >= 0)) : 0;
            fees = Math.Round(deaths * fee);
            refunds = Math.Round(bodiesHome * ht.BodyRefund * fee);
        }
        var (lines, shown) = MarkBookmarks(world, ReportLines(world, attached, delivered, feeEach, refundEach));
        return new RunReport(End, Math.Round(Seconds, 1), Math.Round(engine.Distance / 1000, 2), home.Count, cargo.Count - home.Count,
            Math.Round(cargoValue, 2), Math.Round(gross), Math.Round(coal), Math.Round(ammo), Math.Round(repairs),
            Math.Round(gross - coal - ammo - repairs - fees + refunds - recovery), crewHome, crew.Count - crewHome, delivered ? Math.Round(Scavenged) : 0,
            deaths, bodiesHome, fees, refunds, Math.Round(delivered ? Mail : 0), recovery, stranded ? Kit.Loss : KitLoss.None)
        {
            Lines = lines,
            Bookmarks = shown,
            ChildrenHome = children,
            ChildPay = childPay,
            // D.8: who everyone is now, for the campaign to carry into the next night.
            Identities = Identity.ByName(world, world.LastCrew.Select(c => c.Id)),
            // Home with the cars that are (in one of them, its floor or a locker) or in a living crewmate's hands.
            SpareKitsHome = Math.Max(0, world.Bodies.All.Count(b => b.Kind == Physics.BodyKind.RepairKit && b.Claimed
                && (b.Carrier >= 0 || attached.Contains(b.Parent))) - train.Dynamics.Tuning.Kit.RepairKits),
        };
    }

    /// <summary>
    /// The incident report (D.12): the attribution log read out, then the cars that didn't come home (C.9 "car lost,
    /// decoupled": who pulled the coupler, and what was inside), and how the night ended last.
    /// </summary>
    List<ReportLine> ReportLines(World world, HashSet<int> attached, bool delivered, double fee, double refund)
    {
        bool Home(int bodyId) => delivered && world.Bodies.All.FirstOrDefault(b => b.Id == bodyId) is { DroppedOut: false } b
            && (attached.Contains(b.Parent) || b.Carrier >= 0);
        var lines = IncidentLog.Lines(world, fee, refund, Home);
        int end = lines.FindIndex(l => l.Kind is IncidentKind.Derailed or IncidentKind.Stranded);
        var lost = new List<ReportLine>();
        var train = world.Train;
        foreach (var rake in train.Rakes.Where(r => r != train.Dynamics && !train.Standing(r)))
        {
            // A blocked siding's derelicts (note 294) were never the crew's: left anywhere, they're not lost.
            var ids = rake.Consist.Vehicles.Where(v => !v.Derelict).Select(v => v.Id).Where(id => !attached.Contains(id)).ToList();
            if (ids.Count == 0)
                continue;
            var vehicles = ids.Select(id => train.Vehicles[id]).ToList();
            string cars = ids.Count == 1 ? $"Car {ids[0]}" : $"Cars {ids.Min()}-{ids.Max()}";
            var taken = vehicles.FirstOrDefault(v => v.Taken);
            string what = taken is null ? $"{cars} lost" : taken.Eaten > 0 ? $"{cars} finished by the Car Hugger" : $"{cars} rolled away by the Passenger";
            string where = IncidentLog.At(world, train.Frames[ids[0]].Origin, rake.Distance);
            var inside = new List<string>();
            double freight = vehicles.Where(v => v.Kind == VehicleKind.Cargo).Sum(v => v.Load * v.CargoIntegrity);
            if (freight > 0.01)
                inside.Add($"freight, {freight:0.#} car-loads");
            foreach (var b in world.Bodies.All.Where(b => b.Kind == Physics.BodyKind.Ragdoll && ids.Contains(b.Parent) && b.Carrier < 0))
                inside.Add($"the body of {IncidentLog.NameOf(world, b.Owner)}");
            int puller = taken is null ? world.Attribution.CouplerPulledBy(ids.Min()) : -1;
            string action = (puller >= 0 ? $"Coupler: {IncidentLog.NameOf(world, puller)}. " : "") + (inside.Count > 0 ? $"Inside: {string.Join(", ", inside)}." : "Empty.");
            lost.Add(new ReportLine(IncidentKind.CarLost, "", $"{what} {where}. {action}"));
        }
        lines.InsertRange(end < 0 ? lines.Count : end, lost);
        return lines;
    }

    /// <summary>
    /// D.12: the automatic bookmarks D.13's cap keeps, each beside the line it belongs to: a GRAB's or PUNISH's beside the
    /// victim's death line if they died of it (within <see cref="BookmarkTuning.LineWindowSeconds"/>), a derailment's (one
    /// per crew member) beside the derailment's line, the outro's beside the stranding. A GRAB nobody died of gets a line of
    /// its own, in its place in the night. Returns the lines and the bookmarks shown (the dead's own too, after them).
    /// </summary>
    public static (List<ReportLine> Lines, List<Bookmark> Shown) MarkBookmarks(World world, List<ReportLine> lines)
    {
        var marks = world.Bookmarks;
        var t = marks.Tuning;
        int Line(Bookmark b)
        {
            if (b.Victim >= 0 && b.Kind is BookmarkKind.Grab or BookmarkKind.Punish)
            {
                int death = lines.FindIndex(l => l.Kind == IncidentKind.Death && l.Victim == b.Victim && l.Seconds >= b.Seconds - 1e-6
                    && l.Seconds <= b.Seconds + t.LineWindowSeconds);
                if (death >= 0)
                    return death;
            }
            return b.Kind switch
            {
                // A PUNISH that held nobody: beside the record it wrote the same tick (note 190: the doll struck, the nest).
                BookmarkKind.Punish when b.Victim < 0 => lines.FindIndex(l => IncidentLog.IsEvent(l.Kind) && Math.Abs(l.Seconds - b.Seconds) < 1e-6),
                BookmarkKind.Derail => lines.FindIndex(l => l.Kind == IncidentKind.Derailed),
                BookmarkKind.Stranded => lines.FindIndex(l => l.Kind == IncidentKind.Stranded),
                _ => -1,
            };
        }
        var kept = Bookmarks.Kept(marks.All, t, b => Line(b) >= 0);
        var beside = new Dictionary<int, List<int>>();
        var own = new List<ReportLine>();
        foreach (var b in kept)
        {
            int at = Line(b);
            if (at >= 0)
                (beside.TryGetValue(at, out var l) ? l : beside[at] = []).Add(b.Id);
            else
                own.Add(new ReportLine(IncidentKind.Grab, b.Victim >= 0 ? IncidentLog.NameOf(world, b.Victim) : "",
                    $"{b.What} {b.Where}.{(b.Victim >= 0 ? " Got away." : "")}")
                { Seconds = b.Seconds, Victim = b.Victim, Marks = [b.Id] });
        }
        var result = lines.Select((l, i) => beside.TryGetValue(i, out var ids) ? l with { Marks = ids } : l).ToList();
        foreach (var l in own)
        {
            int at = result.FindIndex(r => r.Seconds > l.Seconds || r.Kind is IncidentKind.Derailed or IncidentKind.Stranded);
            result.Insert(at < 0 ? result.Count : at, l);
        }
        var manual = marks.All.Where(b => b.Kind == BookmarkKind.Manual).Take(Math.Max(0, t.ManualPerRun));
        return (result, [.. kept.Concat(manual).OrderBy(b => b.Id)]);
    }

    /// <summary>
    /// A night resumed from its autosave (spec E): under way with the clock where it was, and every stop up to the one it
    /// last left already made (their chutes and modules spent), so the train pulls away from the save point again.
    /// </summary>
    /// <param name="tender">Coal aboard at the save, and <paramref name="ammo"/> rounds: the night's running costs count on from there.</param>
    public void Resume(double seconds, int departedFacility, double tender, int ammo)
    {
        Phase = RunPhase.Underway;
        Seconds = seconds;
        Facility = -1;
        _tenderAtDeparture = tender;
        _ammoAtDeparture = ammo;
        for (int i = 0; i <= departedFacility && i < _facilities.Count; i++)
        {
            _chuteLeft[i] = 0;
            _sites.ElementAtOrDefault(i)?.Mirror(new SiteState(true, 0, 0, false, false, 0));
            _visited[i] = _departed[i] = true;
        }
        Departed = departedFacility;
    }

    /// <summary>Client side: the host's report of the night (GDD v1.4 App. D.12), sent once it's over.</summary>
    public void MirrorReport(RunReport report) => Report = report;

    /// <summary>Client side: adopts the host's run state.</summary>
    public void Mirror(RunPhase phase, RunEnd end, double seconds, int facility, bool chuteOpen, double[] chuteLeft,
        IReadOnlyList<SiteState>? sites = null, double scavenged = 0, KitWhere? kit = null)
    {
        Scavenged = scavenged;
        if (kit is { } k)
            Kit = k;
        Phase = phase;
        End = end;
        Seconds = seconds;
        Facility = facility;
        ChuteOpen = chuteOpen;
        for (int i = 0; i < Math.Min(chuteLeft.Length, _chuteLeft.Length); i++)
            _chuteLeft[i] = chuteLeft[i];
        if (sites is not null)
            for (int i = 0; i < Math.Min(sites.Count, _sites.Length); i++)
                _sites[i]?.Mirror(sites[i]);
    }

    public int FacilityCount => _facilities.Count;
}
