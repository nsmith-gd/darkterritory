using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Run;

/// <summary>Mirror of content/tuning/run.json. Field docs live in that file.</summary>
public sealed record RunTuning(double StopBelowSpeed, double TerminusZone, double DawnGraceSeconds, ChuteTuning Chute, EconomyTuning Economy)
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
    }

    public RunTuning Tuning { get; set; }
    public Route.Route Route => _route;
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

        if (world.Derailed)
            Finish(world, crew, RunPhase.Failed, RunEnd.Derailed);
        else if (crew.Count > 0 && crew.All(c => !c.Alive))
            Finish(world, crew, RunPhase.Failed, RunEnd.CrewLost);
        else if (Seconds > _route.DawnSeconds + Tuning.DawnGraceSeconds)
            Finish(world, crew, RunPhase.Failed, RunEnd.DawnMissed);
        else if (engine.Speed < Tuning.StopBelowSpeed && front >= _route.Length - Tuning.TerminusZone)
            Finish(world, crew, RunPhase.Arrived, RunEnd.Delivered);
        else
        {
            int at = engine.Speed < Tuning.StopBelowSpeed ? _facilities.FindIndex(f => f.Contains(front)) : -1;
            Facility = at;
            Phase = at >= 0 ? RunPhase.AtFacility : RunPhase.Underway;
            if (at < 0)
                ChuteOpen = false;
        }
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
    public void CrewAct(in PlayerState s, in PlayerIntent intent, int playerId, TrainOnLine train)
    {
        bool holding = LeverInReach(s, train) && intent.Has(PlayerButtons.Use) && intent.MoveZ <= 0.5;
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

    /// <summary>Standing at a working chute's lever (the HUD's prompt, and <see cref="CrewAct"/>).</summary>
    public bool LeverInReach(in PlayerState s, TrainOnLine train) =>
        !Over && s.Alive && Facility >= 0 && _chuteLeft[Facility] > 0
        && (PlayerMotor.WorldPosition(s, train) - ChuteAt(_facilities[Facility], train.Line).Lever).Length <= Tuning.Chute.LeverReach;

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

    /// <summary>Client side: adopts the host's run state.</summary>
    public void Mirror(RunPhase phase, RunEnd end, double seconds, int facility, bool chuteOpen, double[] chuteLeft)
    {
        Phase = phase;
        End = end;
        Seconds = seconds;
        Facility = facility;
        ChuteOpen = chuteOpen;
        for (int i = 0; i < Math.Min(chuteLeft.Length, _chuteLeft.Length); i++)
            _chuteLeft[i] = chuteLeft[i];
    }

    public int FacilityCount => _facilities.Count;
}
