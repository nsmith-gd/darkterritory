using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Run;

/// <summary>Mirror of content/tuning/run.json. Field docs live in that file.</summary>
public sealed record RunTuning(double StopBelowSpeed, double TerminusZone, double DawnGraceSeconds, ChuteTuning Chute, EconomyTuning Economy, double DepartureLoad)
{
    public const string File = "tuning/run.json";
}

public sealed record ChuteTuning(double LeverReach, double LeverSeconds, double SpoutTolerance, double PourPerSecond, double Capacity, double OverfillDamagePerUnit);

public sealed record EconomyTuning(Dictionary<string, double> PerCar, double CoalPerUnit, double RoundsPerRound, double RepairPerIntegrity);

/// <summary>GDD §9: FORTRESS → WILDERNESS → FACILITY → WILDERNESS → TERMINUS.</summary>
public enum RunPhase : byte { Yard, Underway, AtFacility, Arrived, Failed }

public enum RunEnd : byte { None, Delivered, Derailed, CrewLost, DawnMissed }

/// <summary>What a night came to (spec F.1): everything still attached to the locomotive counts.</summary>
/// <param name="RevivedAtGate">Bodies brought home aboard: revived free at the gate (spec C.2), and counted in CrewHome.</param>
public sealed record RunReport(RunEnd End, double Seconds, double DistanceKm, int CarsDelivered, int CarsLost, double CargoDelivered,
    double Gross, double CoalCost, double AmmoCost, double RepairCost, double Net, int CrewHome, int CrewLost, int RevivedAtGate = 0);

/// <summary>
/// One night's run, host-authoritative (clients mirror it for the HUD). The yard gate opens the run and
/// starts the dawn clock; facilities are stops you choose to make; the terminus ends it well; derailment,
/// losing the whole crew, or still being out when the line goes live ends it badly.
/// </summary>
public sealed class Run
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
        _visited = new bool[_facilities.Count];
        _departed = new bool[_facilities.Count];
    }

    readonly int[] _spurs;
    readonly bool[] _visited, _departed;

    /// <summary>The branch a facility's machinery is on (GDD §17), or <see cref="RailLine.MainPath"/> for one on the main line.</summary>
    public int SpurOf(int facility) => facility >= 0 && facility < _spurs.Length ? _spurs[facility] : RailLine.MainPath;

    /// <summary>
    /// Down a mine head's spur, where the radio dies (spec A.5 "dies in tunnels and mine spurs"; GDD §17 "the spur
    /// descends underground. Radio blackout in and out"): aboard a rake standing on it, or on the ground beside it.
    /// </summary>
    public bool Underground(in PlayerState s, TrainOnLine train)
    {
        int path;
        if (s.Parent != PlayerState.World && s.Parent < train.Vehicles.Count)
            path = train.RakeOf(s.Parent).Path;
        else
        {
            double hint = s.LineHint;
            path = train.Line.Nearest(s.Position, ref hint).Path;
        }
        if (path == RailLine.MainPath)
            return false;
        for (int i = 0; i < _facilities.Count; i++)
            if (_spurs[i] == path && _facilities[i].Facility == FacilityKind.MineHead)
                return true;
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
            if (_spurs[i] >= 0 && _spurs[i] < line.Branches.Count)
            {
                // Laid out from where the first cars stand with the engine up at the buffer stop.
                var spur = line.Branches[_spurs[i]];
                double mid = spur.Local.Length - t.SpurLayout;
                return new Site(i, f, modules, t, spur.Local, mid, spur.Side, spur.Toe + mid, crates, spur.Index, heavy);
            }
            int side = f.Side == 0 ? 1 : f.Side;
            double centre = (f.Start + f.End) / 2;
            return new Site(i, f, modules, t, line, centre, side, centre, crates, heavy: heavy);
        })];
    }

    public Site? CurrentSite => Facility >= 0 && Facility < _sites.Length ? _sites[Facility] : null;
    public RunPhase Phase { get; private set; }
    public RunEnd End { get; private set; }
    /// <summary>Seconds since the gates opened (the dawn clock runs from departure).</summary>
    public double Seconds { get; private set; }
    public double DawnIn => _route.DawnSeconds - Seconds;
    public bool LineLive => Seconds >= _route.DawnSeconds;
    /// <summary>The facility the train is stopped at, or −1.</summary>
    public int Facility { get; private set; } = -1;
    public RouteFeature? FacilityFeature => Facility >= 0 ? _facilities[Facility] : null;
    public bool ChuteOpen { get; private set; }
    public double ChuteLeft(int facility) => facility >= 0 && facility < _chuteLeft.Length ? _chuteLeft[facility] : 0;
    public bool Over => Phase is RunPhase.Arrived or RunPhase.Failed;
    public RunReport? Report { get; private set; }

    /// <summary>Advances the run after the world has stepped. <paramref name="crew"/> is everyone's authoritative state.</summary>
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
        Pour(train, dt);
        StepSites(world, dt);

        if (world.Derailed)
            Finish(world, crew, RunPhase.Failed, RunEnd.Derailed);
        else if (crew.Count > 0 && crew.All(c => !c.Alive))
            Finish(world, crew, RunPhase.Failed, RunEnd.CrewLost);
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
            if (_spurs[i] >= 0 ? path == _spurs[i] && !train.OnMain : train.OnMain && _facilities[i].Contains(front))
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
        world.Bodies.HeavySpan = t.Crates.Heavy.Span;
        if (CurrentSite is { } site)
        {
            if (!site.Stocked && Phase == RunPhase.AtFacility)
            {
                site.Stocked = true;
                foreach (var at in site.CrateStack)
                    world.Bodies.SpawnCargo(at, site.CrateLineHint);
                foreach (var at in site.HeavyStack)
                    world.Bodies.SpawnCargo(at, site.CrateLineHint, t.Crates.Heavy.Radius);
            }
            Crank(site, t.Winch, dt);
            if (site.Crane is { } crane)
                Operate(world, crane, dt);
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
    void Operate(World world, Crane crane, double dt)
    {
        _drop = null;
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
    /// Where a sled's load goes: into the cargo car with room nearest the sled, in the train standing by it (one of its
    /// vehicles within reach). The load's handed along the train to it: a train can't put its cars in a different order at
    /// a spur, so the cars nearest the winch fill at the first stop and the next winch loads the ones behind them.
    /// </summary>
    static Vehicle? CargoCarNear(TrainOnLine train, Double3 at, double reach)
    {
        double Distance(Vehicle v) => (train.Frames[v.Id].Origin - at).Length;
        var rake = train.Rakes.FirstOrDefault(r => r.Consist.Vehicles.Any(v => Distance(v) <= reach));
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
        if (CurrentSite is { } site && CrankInReach(s, train, hand) is { } crank && intent.Has(PlayerButtons.Use) && intent.MoveZ <= 0.5)
        {
            site.Cranking[crank.Handle] = playerId;
            site.HandAngle[crank.Handle] = crank.Angle;
        }
        // The crane (T48): at its controls, the operator's intent drives it this tick; on the ground at the hook, rigging.
        if (!Over && CurrentSite?.Crane is { } crane && s.Alive)
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
        double tender = engine.Distance - (geometry.EngineLength - geometry.Engine.TenderLength / 2);
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
        Report = Tally(world, crew);
    }

    /// <summary>Spec F.1: pay on delivered cargo that survives; running costs are what the night burned and broke.</summary>
    public RunReport Tally(World world, IReadOnlyCollection<PlayerState> crew)
    {
        var train = world.Train;
        var engine = EngineRake(train);
        var attached = engine.Consist.Vehicles.Select(v => v.Id).ToHashSet();
        var e = Tuning.Economy;
        string tier = char.ToLowerInvariant(_route.Tier.ToString()[0]) + _route.Tier.ToString()[1..];
        double perCar = e.PerCar.GetValueOrDefault(tier, 700);
        var cargo = train.Vehicles.Where(v => v.Kind == VehicleKind.Cargo).ToList();
        var home = cargo.Where(v => attached.Contains(v.Id)).ToList();
        bool delivered = End == RunEnd.Delivered;
        double cargoValue = home.Sum(v => v.Load * v.CargoIntegrity);
        double gross = delivered ? perCar * cargoValue : 0;
        double coal = Math.Max(0, _tenderAtDeparture + _coalLoaded - train.Boiler.Tender) * e.CoalPerUnit;
        double ammo = Math.Max(0, _ammoAtDeparture - train.Vehicles.Sum(v => v.Gun.Ammo)) * e.RoundsPerRound;
        double repairs = train.Vehicles.Where(v => attached.Contains(v.Id)).Sum(v => 1 - v.Integrity) * e.RepairPerIntegrity;
        // Home is aboard, or at least with the train: someone mid-jump between roofs, or who stepped down at
        // the terminus, made it. Anyone further than this from every attached car didn't.
        const double WithTheTrain = 40;
        int crewHome = crew.Count(c => c.Alive && (c.Parent != PlayerState.World && attached.Contains(c.Parent)
            || attached.Any(id => (train.Frames[id].Origin - PlayerMotor.WorldPosition(c, train)).Length < WithTheTrain)));
        // Spec C.2 "the alternative": a body carried to the terminus is revived free at the gate.
        int revived = delivered ? world.Bodies.All.Count(b => b.Kind == Physics.BodyKind.Ragdoll
            && (attached.Contains(b.Parent) || b.Carrier >= 0)) : 0;
        revived = Math.Min(revived, crew.Count(c => !c.Alive));
        return new RunReport(End, Math.Round(Seconds, 1), Math.Round(engine.Distance / 1000, 2), home.Count, cargo.Count - home.Count,
            Math.Round(cargoValue, 2), Math.Round(gross), Math.Round(coal), Math.Round(ammo), Math.Round(repairs),
            Math.Round(gross - coal - ammo - repairs), crewHome + revived, crew.Count - crewHome - revived, revived);
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

    /// <summary>Client side: adopts the host's run state.</summary>
    public void Mirror(RunPhase phase, RunEnd end, double seconds, int facility, bool chuteOpen, double[] chuteLeft,
        IReadOnlyList<SiteState>? sites = null)
    {
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
