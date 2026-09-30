using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Bots;

/// <summary>A crew member's part in working a facility stop (GDD §17).</summary>
public enum StopJob : byte
{
    /// <summary>No part: on the roofs as usual, but says whether they're aboard.</summary>
    None,
    /// <summary>On the regulator: stops short of the points, runs the empties in, backs out onto the rest, goes.</summary>
    Driver,
    /// <summary>On the ground: cuts the train, throws the switch, rides in and out in the cab, sets it back.</summary>
    Shunter,
    /// <summary>On the winch's first handle, or its second (spec D.2: "2 mandatory"). With no winch to work, they carry crates.</summary>
    Winch0,
    Winch1,
    /// <summary>Carries the crates off the ground and up into the cars (spec D.2 manual crates).</summary>
    Crates,
}

/// <summary>
/// What a crew working a stop tell each other: who has which part, and where each of them is. People say it on the
/// radio ("I'm in the cab", "on the ground at the points"); bots share this instead. It carries only what they'd say.
/// Everything they do still goes through intent (CLAUDE.md), and what they see comes from their own client's world.
/// </summary>
public sealed class CrewCalls
{
    public readonly record struct Call(StopJob Job, int Vehicle, bool Alive);

    readonly SortedDictionary<int, Call> _crew = new();
    readonly Dictionary<int, int> _carryingTo = new();
    readonly Dictionary<int, Double3> _standing = new();

    /// <summary>"I'm here": a bot that knows its player id says where it stands, so a hand coming to help knows which end is free.</summary>
    public void Standing(int playerId, Double3 world) => _standing[playerId] = world;
    public Double3? Where(int playerId) => _standing.TryGetValue(playerId, out var at) ? at : null;

    /// <summary>"This one's for car n": a crate hand says which car the crate in its arms is going to (−1 when empty-handed).</summary>
    public void CarryingTo(int member, int car) => _carryingTo[member] = car;
    /// <summary>Crates other hands are taking to a car.</summary>
    public int BoundFor(int car, int except) => _carryingTo.Count(c => c.Key != except && c.Value == car);

    /// <summary>A crew member says where they are: a vehicle id, or <see cref="PlayerState.World"/> on the ground.</summary>
    public void Say(int member, StopJob job, in PlayerState s) => _crew[member] = new(job, s.Alive ? s.Parent : PlayerState.World, s.Alive);

    public IEnumerable<Call> Crew => _crew.Values;

    /// <summary>
    /// Whether this member should take over a part whoever had it has died with (the shunter mauled, say): the first of the rest of the
    /// crew alive with a part of their own does, as a crew would sort it out on the radio.
    /// </summary>
    public bool StandIn(int member, StopJob part) =>
        !Has(part) && _crew.Values.Any(c => !c.Alive && c.Job == part) && _crew.Where(c => c.Value.Alive && c.Value.Job is not (StopJob.None or StopJob.Driver)).Select(c => c.Key).DefaultIfEmpty(-1).Min() == member;
    public bool Has(StopJob job) => _crew.Values.Any(c => c.Alive && c.Job == job);
    /// <summary>A shunter and two for the winch, alive: the crew a winch stop needs.</summary>
    public bool CanWorkWinch => Has(StopJob.Shunter) && Has(StopJob.Winch0) && Has(StopJob.Winch1);
    /// <summary>Hands for the crates: everyone with a part but the shunter (the winch pair carry where there's no winch).</summary>
    public int CrateHands => _crew.Values.Count(c => c.Alive && c.Job is StopJob.Winch0 or StopJob.Winch1 or StopJob.Crates);
    /// <summary>Crate hands that know their own player id, so can take a heavy crate (T45).</summary>
    public int HeavyHands => _crew.Count(c => c.Value.Alive && c.Value.Job is StopJob.Winch0 or StopJob.Winch1 or StopJob.Crates && _knows.Contains(c.Key));
    readonly HashSet<int> _knows = [];
    /// <summary>A member says it knows its own player id.</summary>
    public void Knows(int member) => _knows.Add(member);

    readonly Dictionary<int, int> _shutting = new();

    /// <summary>
    /// Which door a hand shuts once the crates are in (T50): the one it has claimed while that's still open, else the
    /// nearest open one nobody else alive has claimed (two hands at one door undo each other). Null when there's none left
    /// for it. Claims go with the door: a hand that's gone aboard, or away to warm, holds none, so no door waits on it.
    /// </summary>
    public int? ClaimDoor(int member, IReadOnlyList<int> open, Func<int, double> distance)
    {
        foreach (var gone in _shutting.Where(c => !open.Contains(c.Value) || !(_crew.TryGetValue(c.Key, out var who) && who.Alive)).Select(c => c.Key).ToList())
            _shutting.Remove(gone);
        if (_shutting.TryGetValue(member, out var mine))
            return mine;
        var free = open.Where(car => !_shutting.ContainsValue(car)).OrderBy(distance).Select(car => (int?)car).FirstOrDefault();
        if (free is { } car)
            _shutting[member] = car;
        return free;
    }

    /// <summary>A hand lets go of the door it claimed (it's gone off to do something else).</summary>
    public void Unclaim(int member) => _shutting.Remove(member);

    /// <summary>A site this crew can load at: a winch with the pair for it, or crates with anyone to carry them.</summary>
    public bool CanWork(Site site) => Has(StopJob.Shunter)
        && (site.Has(ModuleKind.Winch) && site.SledsLeft > 0 && CanWorkWinch || site.Has(ModuleKind.Crates) && site.CrateCount > 0 && CrateHands > 0
            || site.Crane is { Left: > 0 } && CanWorkWinch);

    /// <summary>
    /// Everyone alive with a part at the stop is on one of these vehicles, bar anyone gone in out of the cold ("I'm getting
    /// warm in car nine"): they stay where they are, and the train comes back for the cut they're in (T64).
    /// </summary>
    public bool Riding(IReadOnlyCollection<int> vehicles) =>
        _crew.Where(c => c.Value.Alive && c.Value.Job is not (StopJob.None or StopJob.Driver) && !(_warming.Contains(c.Key) && c.Value.Vehicle != PlayerState.World))
            .All(c => vehicles.Contains(c.Value.Vehicle));

    readonly HashSet<int> _warming = [];
    /// <summary>A member says it's going in to get warm, or that it's back out.</summary>
    public void Warming(int member, bool on)
    {
        if (on)
            _warming.Add(member);
        else
            _warming.Remove(member);
    }

    /// <summary>Nobody alive is on the ground.</summary>
    public bool AllAboard => _crew.Values.All(c => !c.Alive || c.Vehicle != PlayerState.World);
}

/// <summary>
/// A facility stop as the crew see it coming (GDD §17): a spur with a winch down it, the point short of its points where
/// the engine stands, and how many cars go in behind it. Everyone works it out the same way from the route.
/// </summary>
/// <param name="Hold">Where the engine's front stands on the main line: two metres short of the points' reach.</param>
/// <param name="CutBehind">The vehicle whose rear coupling is cut to leave the rest waiting, or −1 if the train fits.</param>
public sealed record StopPlan(int Facility, Site Site, Branch Spur, double Hold, int Fit, int CutBehind)
{
    /// <summary>The next stop the crew can work whose points are ahead of <paramref name="from"/>, if any.</summary>
    public static StopPlan? Ahead(World world, double from, IReadOnlySet<int> done, CrewCalls calls)
    {
        if (world.Run is not { } run)
            return null;
        var train = world.Train;
        double points = world.Switches?.Tuning.PointsLength ?? 12;
        var g = train.Dynamics.Tuning.Geometry;
        StopPlan? best = null;
        foreach (var site in run.Sites)
        {
            if (site is not { Spur: >= 0 } || done.Contains(site.Index) || !calls.CanWork(site))
                continue;
            var spur = train.Line.Branches[site.Spur];
            double hold = spur.Toe - points - 2;
            if (hold < from - 5 || best is not null && hold >= best.Hold)
                continue;
            int fit = SpurDrill.Capacity(g, spur, points);
            var vehicles = train.Dynamics.Consist.Vehicles;
            // Nothing to stop for if the cars that would go in have no room.
            if (!vehicles.Skip(1).Take(fit).Any(v => v.Kind == VehicleKind.Cargo && v.Load < 1 - 1e-6))
                continue;
            best = new StopPlan(site.Index, site, spur, hold, fit, vehicles.Count - 1 > fit ? vehicles[fit].Id : -1);
        }
        return best;
    }

    /// <summary>The engine's rake is standing where it stops for this, on the main line.</summary>
    public bool StandingAt(TrainOnLine train) =>
        train.OnMain && Math.Abs(train.Dynamics.Velocity) < 0.05 && Math.Abs(train.Dynamics.Distance - Hold) < 3;

    /// <summary>The engine's rake is at the buffer stop down the spur, standing (where the loading's done).</summary>
    public bool AtTheEnd(TrainOnLine train) =>
        train.Dynamics.Path == Spur.Index && train.Dynamics.Distance > Spur.End - 6 && Math.Abs(train.Dynamics.Velocity) < 0.05;

    /// <summary>Cargo cars in the engine's rake with their sliding door on the site's side open, in order along the train.</summary>
    public IEnumerable<int> OpenSideDoors(TrainOnLine train) =>
        train.Dynamics.Consist.Vehicles.Where(v => v.Kind == VehicleKind.Cargo
            && StopHand.SideDoor(train.Frames[v.Id].Shape, Site.Side) is { } door && v.DoorOpen(door)).Select(v => v.Id);

    /// <summary>Cargo cars in the engine's rake with room for more.</summary>
    public static IEnumerable<Vehicle> WithRoom(TrainOnLine train) =>
        train.Dynamics.Consist.Vehicles.Where(v => v.Kind == VehicleKind.Cargo && v.Load < 1 - 1e-6);

    /// <summary>
    /// Crates still to go in: in someone's arms, put down inside a car with room and not yet stowed, or lying where a
    /// hand can pick them up (<see cref="Loose"/>); and a car with room for them. Once they're all in (or nothing has
    /// room), the crates are done.
    /// </summary>
    /// <param name="hands">Crate hands in the crew: two can take a heavy crate between them, one can lend a hand to someone holding one (T45).</param>
    public bool CratesToLoad(World world, int hands = 0)
    {
        if (!Site.Has(ModuleKind.Crates) || !Site.Stocked)
            return Site.Has(ModuleKind.Crates) && Site.CrateCount > 0;
        var train = world.Train;
        var room = WithRoom(train).Select(v => v.Id).ToHashSet();
        if (room.Count == 0)
            return false;
        // A heavy crate while two have it up or it's down inside a car; held by one, while there's a hand to lend; lying
        // loose, while there are two to take it (T45). Otherwise it doesn't keep the train.
        return world.Bodies.All.Any(b => b.Kind == Physics.BodyKind.Cargo
            && (b.Carrier >= 0 || room.Contains(b.Parent) && Inside(train, b) || Loose(world, b))
            || b.Kind == Physics.BodyKind.Heavy && (b.Lifted || room.Contains(b.Parent) && Inside(train, b)
                || b.Carrier >= 0 && hands >= 1 || hands >= 2 && Loose(world, b)));
    }

    /// <summary>
    /// A crate a hand can pick up from the ground: lying at the site, or left on a car's steps or landing (put down to open
    /// a door), which is in reach from beside them.
    /// </summary>
    public bool Loose(World world, Physics.Body b)
    {
        if (b.Kind is not (Physics.BodyKind.Cargo or Physics.BodyKind.Heavy) || b.Carrier >= 0)
            return false;
        if (b.Parent == PlayerState.World)
        {
            // At the site, on its side of the train (there's no carrying one round the train).
            if (Site.CrateStack.Length == 0 || (b.Centre - Site.CrateStack[0]).Length >= 60)
                return false;
            double hint = Site.Mid;
            var s = Site.Track.Sample(Site.Track.Nearest(b.Centre, ref hint).Distance);
            return Math.Sign(Double3.Dot(b.Centre - s.Position, Double3.Cross(s.Tangent, Double3.Up))) == Site.Side;
        }
        return world.Train.Dynamics.Consist.IndexOf(b.Parent) >= 0 && !Inside(world.Train, b) && b.Pbd.Asleep;
    }

    internal static bool Inside(TrainOnLine train, Physics.Body b) =>
        b.Parent > 0 && train.Frames[b.Parent].Shape.Interior is { } room && room.Contains(b.Pbd.Particles[0].Position);
}

/// <summary>One facility stop as the driver worked it, for the harness report.</summary>
/// <param name="Legs">Seconds spent on each leg, by name.</param>
/// <param name="Coal">Coal taken into the tender (a coaling stop).</param>
/// <param name="Castings">Castings the crane put on the cars at this stop (T54).</param>
public sealed record StopRecord(int Facility, string Kind, double Seconds, int SledsHauled, IReadOnlyDictionary<string, double> Legs, double Coal = 0, int Castings = 0);

/// <summary>
/// A dead line's switch set against the train (App. A.7, the Switchman's work): its lamp reads wrong from the cab. The
/// crew stops short of its points and someone sets it back on the ground; if the train's already down the dead line, it
/// backs out first.
/// </summary>
/// <param name="Hold">Where the engine's front stands to wait: two metres short of the points' reach.</param>
public sealed record SwitchPlan(Branch Branch, double Hold)
{
    /// <summary>How far ahead a switch stand's lamp reads from the cab.</summary>
    const double LampSeen = 1200;

    /// <summary>The nearest dead line ahead whose switch is set for it, in sight of the cab.</summary>
    public static SwitchPlan? Ahead(World world)
    {
        var train = world.Train;
        double front = train.Dynamics.Distance, points = world.Switches?.Tuning.PointsLength ?? 12;
        return train.Line.Branches.Where(b => b.Kind == BranchKind.DeadLine && train.Diverging(b.Index)
                && b.Toe - points - 2 >= front - 3 && b.Toe - front <= LampSeen)
            .OrderBy(b => b.Toe).Select(b => new SwitchPlan(b, b.Toe - points - 2)).FirstOrDefault();
    }

    /// <summary>The engine's down a dead line past its points: it took a switch set wrong.</summary>
    public static SwitchPlan? DownOne(World world)
    {
        var train = world.Train;
        int path = train.Dynamics.Path;
        if (path < 0 || path >= train.Line.Branches.Count || train.Line.Branches[path] is not { Kind: BranchKind.DeadLine } b
            || train.Dynamics.Distance <= b.Toe)
            return null;
        return new SwitchPlan(b, b.Toe - (world.Switches?.Tuning.PointsLength ?? 12) - 2);
    }

    /// <summary>
    /// The whole train's standing short of the points on the main line: at the hold, or further back (backed off a dead
    /// line, the driver stops wherever the train's clear of them, which can be well short of the hold).
    /// </summary>
    public bool StandingAt(TrainOnLine train) =>
        train.OnMain && train.Rakes.Count == 1 && Math.Abs(train.Dynamics.Velocity) < 0.05 && train.Dynamics.Distance - Hold < 3;
}

/// <summary>
/// A coaling stop (GDD §18, spec D.2 gravity chute): the tower stands over the main line, and the engine stops with its
/// tender under the spout while someone on the ground works the chute's lever.
/// </summary>
/// <param name="Hold">Where the engine's front stands: the tender's middle under the spout.</param>
public sealed record CoalPlan(int Facility, double Spout, double Hold, Double3 Lever)
{
    /// <summary>Worth stopping for: this much of the tender to fill.</summary>
    const double WorthFilling = 0.25;

    /// <summary>The next coaling tower ahead with coal left, if the tender has room enough and someone can work the lever.</summary>
    public static CoalPlan? Ahead(World world, double from, IReadOnlySet<int> done, CrewCalls calls)
    {
        var train = world.Train;
        if (world.Run is not { } run || train.BoilerTuning is not { } bt || !calls.Has(StopJob.Shunter)
            || bt.TenderCapacity - train.Boiler.Tender < bt.TenderCapacity * WorthFilling)
            return null;
        var g = train.Dynamics.Tuning.Geometry;
        double tender = g.EngineLength - g.Engine.TenderLength / 2;
        var facilities = run.Route.Of(FeatureKind.Facility).ToList();
        for (int i = 0; i < facilities.Count; i++)
        {
            if (facilities[i].Facility != FacilityKind.CoalingTower || done.Contains(i) || run.ChuteLeft(i) <= 0)
                continue;
            var (spout, lever) = run.ChuteAt(facilities[i], train.Line);
            if (spout + tender >= from - 5)
                return new CoalPlan(i, spout, spout + tender, lever);
        }
        return null;
    }

    /// <summary>The engine's standing with its tender under the spout (well inside run.json's spout tolerance).</summary>
    public bool StandingAt(TrainOnLine train) =>
        train.OnMain && train.Rakes.Count == 1 && Math.Abs(train.Dynamics.Velocity) < 0.05 && Math.Abs(train.Dynamics.Distance - Hold) < 1.5;
}

/// <summary>
/// The driver's side of a facility stop, through the cab's controls and nothing else (the scripted
/// <see cref="SpurDrill"/>, done by a crew): stop short of the spur's points; once the rest are cut off, the switch is
/// over and the ones riding in are aboard, run the empties in to the buffer stop; wait for the winch; back out onto the
/// cars left waiting, at a crawl so they couple; stop clear of the points; once the switch is back and everyone's
/// aboard, go. It only stops where the crew can work the stop (<see cref="CrewCalls.CanWorkWinch"/>).
/// </summary>
public sealed class StopDriver(CrewCalls calls)
{
    public enum Leg : byte { Cruise, Approach, Held, SpurIn, Loading, BackOut, Clear, Depart, ToCoal, Coaling, ToSwitch, OffDeadLine, SetBack, Forward }

    // Long enough for a crew to do their part at walking pace; past it, the stop is given up rather than the night.
    const double HeldGiveUp = 240, LoadingGiveUp = 300, AboardGiveUp = 120, CoalGiveUp = 150;
    /// <summary>Seconds a facility stop (or a coaling stop) takes a crew, to leave spare before the dawn.</summary>
    const double StopAllowance = 600, CoalAllowance = 120;

    readonly HashSet<int> _done = [];
    readonly List<StopRecord> _log = [];
    readonly Dictionary<string, double> _legs = [];
    int _ticks, _legStart, _stopStart, _stillTicks, _sledsAtStart;

    public Leg Doing { get; private set; }

    /// <summary>
    /// A switch the train stands waiting on to be set back with nobody alive to do it (the shunter's dead and no hand has
    /// taken it over): the driver has to get down and do it (a crew of two, one of them lost). Null otherwise.
    /// </summary>
    public Branch? SetBackAlone(World world)
    {
        var train = world.Train;
        Branch? branch = Doing switch
        {
            Leg.SetBack => Switch?.Branch,
            Leg.Clear => Plan?.Spur,
            _ => null,
        };
        return branch is { } b && train.Diverging(b.Index) && !calls.Has(StopJob.Shunter) && Waited > AloneAfter ? b : null;
    }

    /// <summary>Seconds the driver waits for someone else to take a dead shunter's part before it gets down itself.</summary>
    const double AloneAfter = 15;
    public StopPlan? Plan { get; private set; }
    /// <summary>The coaling stop it's making, if that's what it's doing.</summary>
    public CoalPlan? Coal { get; private set; }
    /// <summary>The switch set wrong it's stopped for (or backing off the dead line of).</summary>
    public SwitchPlan? Switch { get; private set; }
    readonly HashSet<int> _coaled = [];
    double _tenderAtStart;
    /// <summary>The speed it runs up to a stop at (the driver's cruise).</summary>
    public double CruiseSpeed { get; set; } = 14;
    /// <summary>The stops worked so far.</summary>
    public IReadOnlyList<StopRecord> Log => _log;

    double Seconds(int ticks) => ticks * SimConstants.TickSeconds;
    double Waited => Seconds(_ticks - _legStart);

    void Begin(Leg leg)
    {
        if (Doing != Leg.Cruise)
            _legs[Doing.ToString()] = Math.Round(_legs.GetValueOrDefault(Doing.ToString()) + Waited, 1);
        Doing = leg;
        _legStart = _ticks;
    }

    /// <summary>This tick's cab intent while working a stop; null while there's none to work (drive as usual).</summary>
    public PlayerIntent? Decide(in PlayerState self, World world)
    {
        _ticks++;
        var train = world.Train;
        var engine = train.Dynamics;
        _stillTicks = Math.Abs(engine.Velocity) < 0.05 ? _stillTicks + 1 : 0;
        if (!self.Alive || !PlayerMotor.InCab(self, train))
            return null;
        bool still = _stillTicks >= SimConstants.TickRate;
        switch (Doing)
        {
            case Leg.Cruise:
                {
                    // Down a dead line (a switch set wrong, taken): stop, and back out onto the main line.
                    if (SwitchPlan.DownOne(world) is { } down)
                    {
                        BeginSwitch(down, Leg.OffDeadLine);
                        return Hold(world);
                    }
                    if (train.Rakes.Count > 1 || !train.OnMain || world.Run is not { } run)
                        return null;
                    // A switch lamp ahead reading wrong: stop short of its points and have it set back (App. A.7).
                    if (SwitchPlan.Ahead(world) is { } wrong && wrong.Hold <= engine.Distance + StoppingDistance(engine) + 80)
                    {
                        BeginSwitch(wrong, Leg.ToSwitch);
                        return Toward(world, wrong.Hold, +1, CruiseSpeed);
                    }
                    // Every stop is optional (GDD §18): only one there's time for before the dawn, after the run to the end of
                    // the line. The tender's the exception once it's low: without coal there's no getting there at all.
                    double spare = run.DawnIn - (run.Route.Length - engine.Distance) / CruiseSpeed;
                    var plan = spare > StopAllowance ? StopPlan.Ahead(world, engine.Distance, _done, calls) : null;
                    bool low = train.BoilerTuning is { } bt && train.Boiler.Tender < bt.TenderCapacity * 0.2;
                    var coal = spare > CoalAllowance || low ? CoalPlan.Ahead(world, engine.Distance, _coaled, calls) : null;
                    double reach = engine.Distance + StoppingDistance(engine) + 80;
                    // Whichever comes first, when it's near enough to start stopping for.
                    if (coal is not null && coal.Hold <= reach && (plan is null || coal.Hold < plan.Hold))
                    {
                        Coal = coal;
                        _legs.Clear();
                        _stopStart = _ticks;
                        _tenderAtStart = train.Boiler.Tender;
                        Begin(Leg.ToCoal);
                        return Toward(world, coal.Hold, +1, CruiseSpeed);
                    }
                    if (plan is null || plan.Hold > reach)
                        return null;
                    Plan = plan;
                    _legs.Clear();
                    _stopStart = _ticks;
                    _sledsAtStart = plan.Site.SledsLeft;
                    Begin(Leg.Approach);
                    return Toward(world, plan.Hold, +1, CruiseSpeed);
                }
            case Leg.ToSwitch:
                {
                    var w = Switch!;
                    if (SwitchPlan.DownOne(world) is not null)
                    {
                        Begin(Leg.OffDeadLine);
                        return Hold(world);
                    }
                    if (still && w.StandingAt(train))
                    {
                        Begin(Leg.SetBack);
                        return Hold(world);
                    }
                    return engine.Distance > w.Hold + 3 && (still || engine.Velocity < 0) ? Toward(world, w.Hold, -1, 1) : Toward(world, w.Hold, +1, CruiseSpeed);
                }
            case Leg.OffDeadLine:
                {
                    var w = Switch!;
                    if (train.OnMain && still && engine.Distance <= w.Hold + 3)
                    {
                        Begin(Leg.SetBack);
                        return Hold(world);
                    }
                    return Toward(world, w.Hold, -1, 3);
                }
            case Leg.SetBack:
                // Until it's set back for the main line and everyone's aboard (or given the time to be, as at a stop). Nobody
                // to do it, nobody goes anywhere: over those points is the dead line again.
                if (!train.Diverging(Switch!.Branch.Index) && (calls.AllAboard || Waited > AboardGiveUp))
                    Begin(Leg.Forward);
                return Hold(world);
            case Leg.Forward:
                if (world.Controls.Reverser < 0)
                    return Toward(world, Switch!.Hold + 50, +1, 3); // flips it at a stand
                FinishSwitch();
                return null;
            case Leg.ToCoal:
                {
                    var c = Coal!;
                    if (c.StandingAt(train) && still)
                    {
                        Begin(Leg.Coaling);
                        return Hold(world);
                    }
                    // A little past it (or short and stopped): creep to it.
                    return engine.Distance > c.Hold + 0.5 && (still || engine.Velocity < 0) ? Toward(world, c.Hold, -1, 1) : Toward(world, c.Hold, +1, CruiseSpeed);
                }
            case Leg.Coaling:
                {
                    // Until the chute's been opened and shut again (or nobody's come to it), and everyone's back aboard.
                    bool poured = train.Boiler.Tender > _tenderAtStart + 1 && world.Run is { ChuteOpen: false };
                    bool nobody = !calls.Has(StopJob.Shunter) || Waited > CoalGiveUp;
                    if ((poured || nobody) && (calls.AllAboard || Waited > CoalGiveUp + AboardGiveUp))
                        Begin(Leg.Depart);
                    return Hold(world);
                }
            case Leg.Approach:
                {
                    var p = Plan!;
                    if (still && Math.Abs(engine.Distance - p.Hold) < 3)
                    {
                        Begin(Leg.Held);
                        return Hold(world);
                    }
                    // Overran it: back up to it (the points won't go over with a wheel on them).
                    return engine.Distance > p.Hold + 3 && (still || engine.Velocity < 0) ? Toward(world, p.Hold, -1, 1) : Toward(world, p.Hold, +1, CruiseSpeed);
                }
            case Leg.Held:
                {
                    var p = Plan!;
                    bool set = train.Diverging(p.Spur.Index);
                    bool cut = p.CutBehind < 0 || train.Rakes.Count > 1;
                    if (!set && (!calls.Has(StopJob.Shunter) || Waited > HeldGiveUp))
                    {
                        // Nobody to throw it: couple back up if the rest were cut off, and go.
                        Begin(train.Rakes.Count > 1 ? Leg.BackOut : Leg.Clear);
                        return Hold(world);
                    }
                    // Everyone with a part aboard the engine's rake, or long enough waiting that someone isn't coming (kept off
                    // it by something in a car, say: T64's Climbers): in without them, as the loading leg goes on without them.
                    if (set && cut && (calls.Riding(EngineRake(train)) || Waited > HeldGiveUp + AboardGiveUp))
                        Begin(Leg.SpurIn);
                    return Hold(world);
                }
            case Leg.SpurIn:
                {
                    var p = Plan!;
                    if (engine.Path == p.Spur.Index && still && engine.Distance > p.Spur.End - 6)
                    {
                        Begin(Leg.Loading);
                        return Hold(world);
                    }
                    return Toward(world, p.Spur.End - 1, +1, 3);
                }
            case Leg.Loading:
                {
                    var p = Plan!;
                    // Winched: nothing left to haul, nobody to haul it, or nowhere for it to go (T66: the cars the sleds load
                    // full already, with the crane's castings, and cranking on hauls nothing).
                    bool winched = !p.Site.Has(ModuleKind.Winch) || p.Site.SledsLeft == 0 || !calls.CanWorkWinch
                        || world.Run is { } wr && !wr.SledHasRoom(train, p.Site);
                    // The castings on (T54): the pair on the crane, until there's none left or no room under the gantry.
                    bool craned = p.Site.Crane is not { } crane || !calls.CanWorkWinch || StopHand.CraneTarget(crane, train) is null;
                    // Crates in, and the doors they went in by shut again: nobody moves a train with its doors open.
                    bool crated = calls.CrateHands == 0 || !p.CratesToLoad(world, calls.HeavyHands) && !p.OpenSideDoors(train).Any();
                    bool loaded = winched && crated && craned || Waited > LoadingGiveUp;
                    if (loaded && (calls.Riding(EngineRake(train)) || Waited > LoadingGiveUp + AboardGiveUp))
                        Begin(Leg.BackOut);
                    return Hold(world);
                }
            case Leg.BackOut:
                {
                    var p = Plan!;
                    bool together = train.Rakes.Count == 1;
                    if (together && train.OnMain && still && engine.Distance <= p.Hold + 3)
                    {
                        Begin(Leg.Clear);
                        return Hold(world);
                    }
                    // Onto the cars left waiting: aim a little into them so the rakes touch (at a crawl, so they couple) rather
                    // than stop just short; then the whole train back clear of the points.
                    var g = engine.Tuning.Geometry;
                    double target = together ? p.Hold : train.Rakes.First(r => r != engine).Distance + g.CouplingGap - 0.5;
                    bool close = !together && engine.RearDistance - target < 15;
                    return Toward(world, target, -1, close ? 0.8 : 3, rear: !together);
                }
            case Leg.Clear:
                if (!train.Diverging(Plan!.Spur.Index) && (calls.AllAboard || Waited > AboardGiveUp))
                    Begin(Leg.Depart);
                return Hold(world);
            case Leg.Depart:
                if (world.Controls.Reverser < 0)
                    return Toward(world, (Plan?.Hold ?? Coal!.Hold) + 50, +1, 3); // brakes and flips the reverser at a stand
                if (Coal is not null)
                    FinishCoaling(train);
                else
                    Finish();
                return null; // away as usual
            default:
                return null;
        }
    }

    void BeginSwitch(SwitchPlan plan, Leg leg)
    {
        Switch = plan;
        _legs.Clear();
        _stopStart = _ticks;
        Begin(leg);
    }

    void FinishSwitch()
    {
        Begin(Leg.Cruise);
        _log.Add(new StopRecord(-1, "SwitchSetBack", Math.Round(Seconds(_ticks - _stopStart), 1), 0, new Dictionary<string, double>(_legs)));
        Switch = null;
    }

    void FinishCoaling(TrainOnLine train)
    {
        var c = Coal!;
        Begin(Leg.Cruise);
        _coaled.Add(c.Facility);
        _log.Add(new StopRecord(c.Facility, nameof(FacilityKind.CoalingTower), Math.Round(Seconds(_ticks - _stopStart), 1), 0,
            new Dictionary<string, double>(_legs), Math.Round(train.Boiler.Tender - _tenderAtStart, 1)));
        Coal = null;
    }

    void Finish()
    {
        var p = Plan!;
        Begin(Leg.Cruise);
        _done.Add(p.Facility);
        _log.Add(new StopRecord(p.Facility, p.Site.Feature.Facility?.ToString() ?? "", Math.Round(Seconds(_ticks - _stopStart), 1),
            _sledsAtStart - p.Site.SledsLeft, new Dictionary<string, double>(_legs),
            Castings: p.Site.Crane?.Castings.Count(c => c.State == CastingState.Loaded) ?? 0));
        Plan = null;
    }

    static IReadOnlyCollection<int> EngineRake(TrainOnLine train) => [.. train.Dynamics.Consist.Vehicles.Select(v => v.Id)];

    static double BrakeRate(TrainDynamics engine) => Math.Max(0.1, engine.MaxBrakeForce / engine.Consist.MassTonnes * 0.5);
    static double StoppingDistance(TrainDynamics engine) => engine.Speed * engine.Speed / (2 * BrakeRate(engine));

    /// <summary>Throttle notches to move from where it's set to <paramref name="to"/>.</summary>
    static sbyte Notch(in TrainControls c, double to) => (sbyte)Math.Clamp(Math.Round((to - c.Throttle) * 4), -4, 4);

    static PlayerIntent Hold(World world) => new() { Buttons = PlayerButtons.Brake, ThrottleNotch = Notch(world.Controls, 0) };

    /// <summary>
    /// Drives the engine's rake to put its front (or with <paramref name="rear"/>, its back) at a distance, at up to
    /// <paramref name="top"/> m/s and gently at the end, on the regulator and the brake handle like someone watching the
    /// ground: the reverser first, which only moves at a stand.
    /// </summary>
    static PlayerIntent Toward(World world, double target, int direction, double top, bool rear = false)
    {
        var engine = world.Train.Dynamics;
        var controls = world.Controls;
        if ((controls.Reverser >= 0 ? 1 : -1) != direction)
        {
            var stop = Hold(world);
            if (Math.Abs(engine.Velocity) < 0.05)
                stop.Buttons |= PlayerButtons.Reverser;
            return stop;
        }
        double left = (target - (rear ? engine.RearDistance : engine.Distance)) * direction;
        if (left <= 0.3)
            return Hold(world);
        double wanted = Math.Min(top, Math.Sqrt(2 * BrakeRate(engine) * left));
        double speed = engine.Velocity * direction;
        return speed < wanted - 0.3 ? new PlayerIntent { ThrottleNotch = Notch(controls, 0.5) }
            : speed > wanted + 0.2 ? Hold(world)
            : new PlayerIntent { ThrottleNotch = Notch(controls, 0) };
    }
}

/// <summary>
/// A crew member's part on the ground at a facility stop (GDD §17: "someone is on the ground at every junction"), all
/// through intent. The shunter drops into the gap and cuts the train, walks up to the stand and throws the switch, rides
/// in and out in the cab (it's warm there), then sets it back and climbs aboard. The two on the winch ride in, crank it
/// together until the sleds are in, and climb aboard. Out in the cold too long, they go and get warm first (the walker
/// does that) and come back to it. With no part, or nothing to do this tick, it returns null and the walker walks.
/// </summary>
public sealed class StopHand(StopJob job, CrewCalls calls, int member, ColdTuning? cold = null)
{
    /// <summary>Walking beside the train, keep this far from its track's centreline (a car is 3 m wide, 4.2 at its steps).</summary>
    const double Clear = 2.6;
    /// <summary>Stand this far from the track at the switch stand: in reach of the lever (2.6 m out), clear of the train.</summary>
    const double StandOff = 3.2;

    /// <summary>Warm enough again to go back to it (from 85% of the onset).</summary>
    const double WarmAgain = 5;
    /// <summary>The cab's the place to get warm when it's this near: no doors to fight over, and the driver's there.</summary>
    const double CabNear = 60;

    readonly HashSet<int> _done = [];
    StopPlan? _plan;
    bool _wentIn, _reachedEnd, _warming;

    public StopJob Job => job;
    /// <summary>
    /// The bot's own player id, once it has one (the harness says): heavy crates (T45) need to know which end is whose.
    /// Without it, a hand leaves heavy crates to others.
    /// </summary>
    public int? PlayerId { get; set; }
    /// <summary>What it's doing (for tests and traces).</summary>
    public string Doing { get; private set; } = "";
    /// <summary>Stops it has done its part at.</summary>
    public int Worked { get; private set; }

    /// <summary>It's gone in to get warm (and says where it is), or come out again.</summary>
    public void Warming(in PlayerState self, bool on)
    {
        calls.Warming(member, on);
        if (on)
            calls.Say(member, job, self);
    }

    /// <summary>This tick's intent for its part in a stop; null when there's nothing for it to do (walk as usual).</summary>
    public PlayerIntent? Decide(in PlayerState self, World world)
    {
        // Someone has to shunt, and set the switches back (GDD §17): with the shunter dead, a hand takes it over, between
        // stops (never mid-part).
        if (job is not (StopJob.None or StopJob.Driver or StopJob.Shunter) && self.Alive && _plan is null && _coal is null && _switch is null
            && calls.StandIn(member, StopJob.Shunter))
            job = StopJob.Shunter;
        calls.Say(member, job, self);
        if (PlayerId is { } id)
        {
            calls.Knows(member);
            calls.Standing(id, PlayerMotor.WorldPosition(self, world.Train));
        }
        Doing = "";
        if (job == StopJob.None || !self.Alive || world.Run is null)
            return null;
        var train = world.Train;
        if (job == StopJob.Shunter && _plan is null && _coal is null && SettingBack(self, world, out var setting))
            return setting;
        if (job == StopJob.Shunter && _plan is null && Coaling(self, world, out var coaling))
            return coaling;
        if (_plan is null)
        {
            // The train's standing short of the points for a stop (the driver only makes the ones the crew can work).
            if (StopPlan.Ahead(world, train.Dynamics.Distance - 10, _done, calls) is not { } plan || !plan.StandingAt(train))
                return null;
            _plan = plan;
            _wentIn = _reachedEnd = false;
        }
        var p = _plan;
        if (train.Dynamics.Path == p.Spur.Index && train.Dynamics.Distance > p.Spur.Toe)
            _wentIn = true;
        _reachedEnd |= p.AtTheEnd(train);
        // The train's back together and away up the main line: that stop's over, done or not.
        if (train.OnMain && train.Rakes.Count == 1 && train.Dynamics.Distance > p.Hold + 20 && !train.Diverging(p.Spur.Index))
        {
            _done.Add(p.Facility);
            _plan = null;
            return null;
        }
        // Mid-air, nothing to do; on a ladder or inside a car, the walker knows the way out (unless it's in there to load).
        var part = Part(p, world);
        if (self.Surface == Surface.Air)
            return new PlayerIntent();
        if (self.Surface == Surface.Ladder || self.Surface == Surface.Deck && self.Parent > 0 && !(part == StopJob.Crates && _reachedEnd))
            return null;
        // Too cold to keep at it: into the cab if it's near (the walker's way into a car if not), until properly warm again.
        if (cold is not null && self.Cold >= cold.OnsetSeconds * 0.85 && !PlayerMotor.NearHeat(self, train))
        {
            _warming = true;
            calls.Unclaim(member);
        }
        else if (self.Cold <= WarmAgain)
            _warming = false;
        if (_warming)
        {
            // Nobody climbs into the cab with freight in their arms.
            if (self.Has(PlayerFlags.Heavy))
                return Press();
            var cab = train.Frames[0].ToWorld(train.Frames[0].Shape.Cab!.Value.Centre);
            var warming = (PlayerMotor.WorldPosition(self, train) - cab).Length < CabNear ? Ride(self, train, p) : null;
            Doing = "warming";
            return warming;
        }
        // The gantry crane (T54, spec D.2): the pair work it first, one at the controls and one rigging, while the cars under
        // the gantry still have room (the crates and the sleds would fill them); then the winch.
        if (job is StopJob.Winch0 or StopJob.Winch1 && p.Site.Crane is { } crane
            && (CraneTarget(crane, train) is not null || crane.Hooked is not null))
            return job == StopJob.Winch0 ? Operate(self, world, p, crane) : RigCasting(self, world, p, crane);
        if (_atControls)
        {
            // Done at the crane: let go of the controls (and so step down) before anything else.
            _atControls = false;
            return new PlayerIntent();
        }
        return part switch
        {
            StopJob.Shunter => Shunt(self, world, p),
            StopJob.Crates => Carry(self, world, p),
            _ => Crank(self, world, p),
        };
    }

    bool _atControls, _fired;
    (int Casting, double Bridge, double Trolley)? _castingAt;
    (int Car, double Bridge, double Trolley)? _carAt;

    /// <summary>
    /// What's left for the crane: the next casting still stacked (in order, so the operator and the rigger agree on it
    /// without seeing each other: spec D.3's "blind instruction", called as the plan) and a cargo car with room that the
    /// hook reaches over. Null when there's nothing to load or nowhere to put it.
    /// </summary>
    public static (int Casting, int Car)? CraneTarget(Crane crane, TrainOnLine train)
    {
        int casting = Array.FindIndex(crane.Castings, c => c.State == CastingState.Stacked);
        if (casting < 0 && crane.Hooked is null)
            return null;
        return RoofFor(crane, train) is { } roof ? (casting, roof.Car) : null;
    }

    /// <summary>
    /// A cargo car with room and where on its roof the hook reaches (anywhere along it, clear of its ends: the gantry may
    /// only span part of a car), the nearest car first: null for none in reach.
    /// </summary>
    static (int Car, double Bridge, double Trolley)? RoofFor(Crane crane, TrainOnLine train)
    {
        (int Car, double Bridge, double Trolley, double Off)? best = null;
        foreach (var v in StopPlan.WithRoom(train))
        {
            var frame = train.Frames[v.Id];
            double half = frame.Shape.HalfLength - 1;
            for (double z = -half; z <= half + 1e-9; z += 1)
            {
                var (b, x, off) = crane.Over(frame.ToWorld(new Double3(0, frame.Shape.RoofHeight, z)));
                if (off < 0.3 && (best is null || Math.Abs(z) < Math.Abs(best.Value.Off)))
                    best = (v.Id, b, x, z);
            }
            if (best is { } found && found.Car == v.Id)
                break;
        }
        return best is { } w ? (w.Car, w.Bridge, w.Trolley) : null;
    }

    /// <summary>
    /// At the crane's controls (spec D.2): down on the stand's side, to the stand, and holding Use there, the stick drives
    /// the crane. Hook down over the next casting and held there while it's rigged; up high, over a car with room, down onto
    /// its roof, and let go (only ever when it's set down: a load let go of high kills).
    /// </summary>
    PlayerIntent? Operate(in PlayerState self, World world, StopPlan p, Crane c)
    {
        var train = world.Train;
        if (!_reachedEnd)
            return Ride(self, train, p);
        if (self.Parent != PlayerState.World)
            return GetDown(self, train, SideOf(train, p, c.Controls, self.LineHint));
        if (!_atControls)
        {
            var (step, there) = WalkTo(self, train.Line, p.Spur.Index, c.Controls, null);
            if (!there && ((self.Position - c.Controls) with { Y = 0 }).Length > c.Tuning.ControlsReach - 0.4)
            {
                Doing = "to the crane";
                return step;
            }
            _atControls = true;
        }
        Doing = "at the crane";
        var drive = new PlayerIntent { Buttons = PlayerButtons.Use };
        var t = c.Tuning;
        if (c.Hooked is null)
        {
            _fired = false;
            _carAt = null;
            if (CraneTarget(c, train) is not { Casting: >= 0 } target)
                return drive;
            if (_castingAt is not { } at || at.Casting != target.Casting)
            {
                var (b, x, _) = c.Over(c.Castings[target.Casting].At);
                _castingAt = at = (target.Casting, b, x);
            }
            bool over = Steer(ref drive, c, at.Bridge, at.Trolley);
            // Traversing, the hook up out of the way; over the casting, down to where the rigger can reach it.
            if (!over && c.Hook < t.Height - 0.5)
                drive.Buttons |= PlayerButtons.Jump;
            else if (over && c.Hook > t.RigHeight - 0.8)
                drive.Buttons |= PlayerButtons.Brake;
            return drive;
        }
        _castingAt = null;
        if (_carAt is not { } car || train.Vehicles[car.Car].Load >= 1 - 1e-6)
        {
            if (RoofFor(c, train) is not { } roof)
                return drive;
            _carAt = car = roof;
        }
        // Up first, clear of the roofs; then across; then down onto it and let go once it's sitting on the roof.
        if (c.Hook < t.Height - 0.3 && !Near(c, car.Bridge, car.Trolley))
        {
            drive.Buttons |= PlayerButtons.Jump;
            return drive;
        }
        if (!Steer(ref drive, c, car.Bridge, car.Trolley))
            return drive;
        var (under, roofY) = c.Under(train);
        double above = c.HookedBase.Y - roofY;
        if (under == car.Car && above <= Math.Min(0.3, t.DropAbove - 0.1))
        {
            if (!_fired)
            {
                drive.Buttons |= PlayerButtons.Fire;
                _fired = true;
            }
            return drive;
        }
        drive.Buttons |= PlayerButtons.Brake;
        return drive;
    }

    /// <summary>The stick towards a bridge and trolley setting (a little ahead of time for the lag); true once it's there.</summary>
    static bool Steer(ref PlayerIntent drive, Crane c, double bridge, double trolley)
    {
        double db = bridge - c.Bridge, dx = trolley - c.Trolley;
        drive.MoveZ = Math.Abs(db) < 0.04 ? 0 : (float)Math.Clamp(db * 2.5, -1, 1);
        drive.MoveX = Math.Abs(dx) < 0.04 ? 0 : (float)Math.Clamp(dx * 2.5, -1, 1);
        return Near(c, bridge, trolley);
    }

    static bool Near(Crane c, double bridge, double trolley) => Math.Abs(bridge - c.Bridge) < 0.08 && Math.Abs(trolley - c.Trolley) < 0.08;

    /// <summary>
    /// Rigging on the ground (spec D.2): down on the castings' side, beside the next one, and once the hook's come down
    /// over it, holding Use until it's hooked on. Then back out of the way while it goes up and over.
    /// </summary>
    PlayerIntent? RigCasting(in PlayerState self, World world, StopPlan p, Crane c)
    {
        var train = world.Train;
        if (!_reachedEnd)
            return Ride(self, train, p);
        int next = Array.FindIndex(c.Castings, k => k.State == CastingState.Stacked);
        var at = next >= 0 ? c.Castings[next].At : c.Castings[0].At;
        if (self.Parent != PlayerState.World)
            return GetDown(self, train, SideOf(train, p, at, self.LineHint));
        var (along, across) = TrackCoords(train.Line, p.Spur.Index, at, self.LineHint);
        if (c.Hooked is not null || next < 0)
        {
            // Stand off beyond the stack while it's lifted away.
            Doing = "standing clear";
            return WalkTo(self, train.Line, p.Spur.Index, TrackPoint(train.Line, p.Spur.Index, along, across + Math.Sign(across) * 2.5), null).Step;
        }
        var stand = TrackPoint(train.Line, p.Spur.Index, along + 1.0, across);
        var (step, there) = WalkTo(self, train.Line, p.Spur.Index, stand, null);
        if (!there && ((self.Position - stand) with { Y = 0 }).Length > 0.35)
        {
            Doing = "to the castings";
            return step;
        }
        if (c.Riggable(PlayerMotor.WorldPosition(self, train)) is null)
        {
            Doing = "calling the hook down";
            return new PlayerIntent();
        }
        Doing = "rigging";
        return new PlayerIntent { Buttons = PlayerButtons.Use };
    }

    /// <summary>Which side of the spur a point on the ground is (+1 right looking up it).</summary>
    static int SideOf(TrainOnLine train, StopPlan p, Double3 world, double hint) =>
        TrackCoords(train.Line, p.Spur.Index, world, hint).Across >= 0 ? 1 : -1;

    /// <summary>The part this stop: the winch pair carry crates where there's no winch, or once its sleds are in.</summary>
    StopJob Part(StopPlan p, World world) => job is StopJob.Winch0 or StopJob.Winch1
        && (!p.Site.Has(ModuleKind.Winch) || p.Site.SledsLeft == 0 || world.Run is { } r && !r.SledHasRoom(world.Train, p.Site))
        && p.Site.Has(ModuleKind.Crates) ? StopJob.Crates : job;

    PlayerIntent? Shunt(in PlayerState self, World world, StopPlan p)
    {
        var train = world.Train;
        bool set = train.Diverging(p.Spur.Index);
        if (!_wentIn)
        {
            if (!set && p.CutBehind >= 0 && train.Rakes.Count == 1)
                return Cut(self, train, p);
            return set ? Ride(self, train, p) : Throw(self, train, p.Spur);
        }
        // In the cab while the empties are down the spur, until the whole train's back together short of the points.
        bool back = train.Rakes.Count == 1 && train.OnMain && Math.Abs(train.Dynamics.Velocity) < 0.05 && train.Dynamics.Distance <= p.Hold + 3;
        if (!back)
            return Ride(self, train, p);
        return set ? Throw(self, train, p.Spur) : Aboard(self, p);
    }

    PlayerIntent? Crank(in PlayerState self, World world, StopPlan p)
    {
        var train = world.Train;
        var engine = train.Dynamics;
        bool atEnd = p.AtTheEnd(train);
        if (!_reachedEnd)
            return Ride(self, train, p);
        // Nothing to haul, or nowhere for a sled to go (T66): aboard.
        if (!atEnd || p.Site.SledsLeft == 0 || world.Run is { } r && !r.SledHasRoom(train, p.Site))
            return Aboard(self, p);
        if (self.Parent != PlayerState.World)
            return GetDown(self, train, p.Spur.Side);
        // At its handle, on the track side of it, holding on. It only turns with both handles held (spec D.2).
        var handle = p.Site.Handles[job == StopJob.Winch0 ? 0 : 1];
        var (along, across) = TrackCoords(train.Line, p.Spur.Index, handle, self.LineHint);
        var stand = TrackPoint(train.Line, p.Spur.Index, along, Math.Sign(across) * (Math.Abs(across) - 0.4));
        var (step, there) = WalkTo(self, train.Line, p.Spur.Index, stand, null);
        if (!there)
            return step;
        Doing = "cranking";
        return new PlayerIntent { Buttons = PlayerButtons.Use };
    }

    CoalPlan? _coal;
    readonly HashSet<int> _coaled = [];
    bool _poured;
    SwitchPlan? _switch;

    /// <summary>
    /// A switch set wrong ahead (true while there's one to see to): once the train's standing short of its points, down to
    /// the stand and set it back for the main line (App. A.7: "verify every switch on the ground"), then aboard.
    /// </summary>
    bool SettingBack(in PlayerState self, World world, out PlayerIntent? intent)
    {
        intent = null;
        var train = world.Train;
        if (_switch is null)
        {
            if (SwitchPlan.Ahead(world) is not { } plan || !plan.StandingAt(train))
                return false;
            _switch = plan;
        }
        var branch = _switch.Branch;
        if (!train.Diverging(branch.Index))
        {
            // Set back: aboard (the walker climbs the nearest car), and done.
            if (self.Parent == PlayerState.World && self.Surface == Surface.Ground)
            {
                Doing = "boarding";
                return true;
            }
            _switch = null;
            return false;
        }
        if (!_switch.StandingAt(train))
            return false; // it's moving: wait aboard for it to stop
        if (self.Surface == Surface.Air)
        {
            intent = new PlayerIntent();
            return true;
        }
        if (self.Surface == Surface.Ladder || self.Surface == Surface.Deck && self.Parent > 0)
            return true; // the walker knows the way off a ladder or out of a car
        intent = Throw(self, train, branch);
        return true;
    }

    /// <summary>
    /// A coaling stop (true while there's one to work): down to the chute's lever beside the line, open it, shut it again a
    /// moment before the tender's full (the overflow damages the engine) or once the tower's empty, and back aboard.
    /// </summary>
    bool Coaling(in PlayerState self, World world, out PlayerIntent? intent)
    {
        intent = null;
        var train = world.Train;
        var run = world.Run!;
        if (_coal is null)
        {
            if (CoalPlan.Ahead(world, train.Dynamics.Distance - 10, _coaled, calls) is not { } plan || !plan.StandingAt(train))
                return false;
            _coal = plan;
            _poured = false;
        }
        var coal = _coal;
        // The train's gone on: that stop's over.
        if (Math.Abs(train.Dynamics.Distance - coal.Hold) > 5 || train.BoilerTuning is not { } bt)
        {
            _coaled.Add(coal.Facility);
            _coal = null;
            return false;
        }
        if (self.Surface == Surface.Air)
        {
            intent = new PlayerIntent();
            return true;
        }
        if (self.Surface == Surface.Ladder || self.Surface == Surface.Deck && self.Parent > 0)
            return true; // the walker knows the way off a ladder or out of a car
        bool open = run.ChuteOpen;
        _poured |= open;
        if (_poured && !open)
        {
            // Opened and shut again: aboard (the walker climbs the nearest car), and done.
            if (self.Parent == PlayerState.World)
            {
                Doing = "boarding";
                return true;
            }
            _coaled.Add(coal.Facility);
            _coal = null;
            return false;
        }
        bool enough = train.Boiler.Tender >= bt.TenderCapacity - run.Tuning.Chute.PourPerSecond * 1.5 || run.ChuteLeft(coal.Facility) <= 0;
        var (along, across) = TrackCoords(train.Line, RailLine.MainPath, coal.Lever, self.LineHint);
        if (self.Parent != PlayerState.World)
        {
            intent = GetDown(self, train, Math.Sign(across));
            return true;
        }
        // Just beyond the lever from the track, in reach of it.
        var stand = TrackPoint(train.Line, RailLine.MainPath, along, Math.Sign(across) * (Math.Abs(across) + 0.5));
        var (step, there) = WalkTo(self, train.Line, RailLine.MainPath, stand, null);
        if (!there)
        {
            Doing = "to the chute";
            intent = step;
            return true;
        }
        bool wantOpen = !enough;
        Doing = open == wantOpen ? "at the chute" : open ? "shutting the chute" : "opening the chute";
        intent = open == wantOpen ? new PlayerIntent() : new PlayerIntent { Buttons = PlayerButtons.Use };
        return true;
    }

    int _car = -1;
    bool _pressed;

    /// <summary>Use for one tick, then not: picking up and putting down are on the press, not the hold.</summary>
    PlayerIntent Press()
    {
        _pressed = !_pressed;
        return _pressed ? new PlayerIntent { Buttons = PlayerButtons.Use } : new PlayerIntent();
    }

    /// <summary>
    /// Crates off the ground and into the cars (spec D.2): pick one up at the stack, walk it to the car with the most room,
    /// up the steps to its side door (opened first: Use with your arms full puts the crate down), in, and put it down
    /// inside, where it's stowed once it lies still. Then back for the next, until they're in or there's no room.
    /// </summary>
    PlayerIntent? Carry(in PlayerState self, World world, StopPlan p)
    {
        var train = world.Train;
        if (!_reachedEnd)
            return Ride(self, train, p);
        bool heavy = self.Has(PlayerFlags.Heavy);
        int side = p.Site.Side;
        // The train's leaving: down with it if it's in our arms, and aboard.
        if (!p.AtTheEnd(train))
        {
            _car = -1;
            return heavy ? Press() : Aboard(self, p);
        }
        // Heavy crates (T45): holding an end, wait for a hand; at the back end of one, follow it in; someone holding one
        // alone, go and take the other end.
        var mine = PlayerId is { } me ? world.Bodies.CarriedBy(me) : null;
        if (mine is { Kind: Physics.BodyKind.Heavy })
        {
            if (!mine.Lifted)
            {
                Doing = "holding an end, waiting for a hand";
                calls.CarryingTo(member, -1);
                return new PlayerIntent();
            }
            if (mine.Second == PlayerId)
                return Follow(self, world, mine);
        }
        else if (mine is null && PlayerId is not null && Wanting(world, p) is { } wanting)
            return LendAHand(self, world, wanting);
        // Nothing more to carry: the doors shut behind us (an open car is a cold one), and aboard.
        if (!heavy && !p.CratesToLoad(world, calls.HeavyHands))
        {
            calls.CarryingTo(member, -1);
            return OpenSideDoor(world, p, calls, member, self) is { } car ? ShutUp(self, world, p, car) : Aboard(self, p);
        }
        if (!heavy || _car < 0)
            _car = Roomiest(world, p, calls, member);
        calls.CarryingTo(member, heavy ? _car : -1);
        // Every car's room spoken for by crates on their way in: wait for them to land (then either there's room again,
        // or the crates are done and the doors want shutting). Going aboard now would leave this stop for good.
        if (_car < 0)
        {
            if (heavy)
                return Press();
            Doing = "waiting for room";
            return new PlayerIntent();
        }
        var frame = train.Frames[_car];
        var shape = frame.Shape;
        var layout = train.Dynamics.Tuning.Geometry.Interior!;
        int door = SideDoor(shape, side) ?? 0;
        bool open = train.Vehicles[_car].DoorOpen(door);
        double w = shape.Bounds.Max.X, sd = layout.SideDoorWidth / 2;
        var landing = new Double3(side * (w + layout.StepWidth / 2), layout.FloorHeight, 0);
        double facingIn = side > 0 ? Math.PI / 2 : -Math.PI / 2; // looking across the car from its door
        bool onThisCar = self.Parent == _car && self.Surface == Surface.Deck;
        bool inside = onThisCar && Math.Abs(self.Position.X) < w - layout.WallThickness;

        if (inside)
        {
            if (!heavy)
                return Walk(self, landing, facingIn + Math.PI, "out of the car");
            // In the middle of the car, facing away from the door: the crate goes down in front, inside the walls.
            var drop = new Double3(0, layout.FloorHeight, 0);
            if (!Near(self, drop, 0.25) || !Aligned(self, facingIn))
                return Walk(self, drop, facingIn, "carrying in");
            Doing = "putting it down";
            return Press();
        }
        if (onThisCar)
        {
            // On its steps or the landing.
            if (!open)
            {
                if (heavy)
                {
                    Doing = "putting it down to open up";
                    return Press();
                }
                if (!Near(self, landing, 0.25) || !Aligned(self, facingIn))
                    return Walk(self, landing, facingIn, "up to the door");
                Doing = "opening up";
                return new PlayerIntent { Buttons = PlayerButtons.Use };
            }
            if (heavy)
                return Math.Abs(self.Position.Z) > sd - 0.3
                    ? Walk(self, landing, facingIn, "up to the door with it")
                    : Walk(self, new Double3(0, layout.FloorHeight, 0), facingIn, "carrying in");
            // Down the steps to the ground, forward along the car, and off the bottom tread.
            return Walk(self, landing with { Y = 0, Z = -sd - 4 * layout.StepDepth - 0.5 }, Math.PI, "down the steps");
        }
        if (self.Parent != PlayerState.World)
            return GetDown(self, train, side);
        // On the far side of the train from the steps there's no way round on foot: put it down, over the train (the
        // walker climbs the nearest car; off it, we get down on this side), and back to it.
        var (_, across) = TrackCoords(train.Line, p.Spur.Index, self.Position, self.LineHint);
        if (Math.Sign(across) != side && Math.Abs(across) > 1.2)
        {
            Doing = "over the train";
            return heavy ? Press() : null;
        }
        // On the ground: the door first, then a crate, then up the steps with it.
        var foot = frame.ToWorld(landing with { Y = 0, Z = -sd - 4 * layout.StepDepth - 0.4 });
        if (!open || heavy)
        {
            // In the lane in front of the steps, go on up them along the treads; anywhere else (beside the landing too: its
            // side is a wall as high as the floor), to the foot of them first.
            var local = frame.ToLocal(PlayerMotor.WorldPosition(self, train));
            double outward = side * local.X - w;
            bool inLane = outward > 0.1 && outward < layout.StepWidth - 0.1 && local.Z > -sd - 4 * layout.StepDepth - 1.2 && local.Z < -sd - 0.3;
            if (inLane)
                return Head(self, frame.ToWorld(landing with { Y = 0, Z = local.Z + 1.5 }), heavy ? "up the steps with it" : "up the steps");
            Doing = heavy ? "carrying" : "to the door";
            // Arrived at the foot but a hand's width outside the lane (the 100-night playtest's crate hands, stood there for
            // good with the crates in their arms): straight on up from there.
            var (walk, atFoot) = WalkTo(self, train.Line, p.Spur.Index, foot, null);
            return atFoot ? Head(self, frame.ToWorld(landing with { Y = 0, Z = local.Z + 1.5 }), heavy ? "up the steps with it" : "up the steps") : walk;
        }
        // Nothing loose just now, though some are still on their way in (in someone's arms, settling): wait by the stack
        // for the next, rather than give up the stop.
        // The light ones gone, two hands take a heavy one between them: one takes an end, and the other comes to help.
        var crate = Crate(world, p, self)
            ?? (PlayerId is not null && calls.HeavyHands >= 2 ? HeavyCrate(world, p, self) : null);
        if (crate is null)
        {
            Doing = "waiting for a crate";
            return new PlayerIntent();
        }
        var at = crate.Parent == PlayerState.World ? crate.Centre : train.Frames[crate.Parent].ToWorld(crate.Centre);
        var from = new Double3(self.Position.X - at.X, 0, self.Position.Z - at.Z);
        var stand = at + (from.Length > 0.01 ? from.Normalized : new Double3(1, 0, 0)) * 0.8;
        var (step, there) = WalkTo(self, train.Line, p.Spur.Index, stand with { Y = at.Y }, null);
        if (!there && (Flat(self.Position) - Flat(stand)).Length > 0.25)
        {
            Doing = "to a crate";
            return step;
        }
        double yaw = Math.Atan2(-(at.X - self.Position.X), -(at.Z - self.Position.Z));
        if (!Aligned(self, yaw))
            return new PlayerIntent { LookYaw = Turn(self, yaw) };
        Doing = "picking one up";
        return Press();
    }

    /// <summary>A heavy crate at the site with one holding it, waiting for a hand (a bot or anyone else).</summary>
    Physics.Body? Wanting(World world, StopPlan p) =>
        world.Bodies.All.FirstOrDefault(b => b.Kind == Physics.BodyKind.Heavy && b.Carrier >= 0 && b.Carrier != PlayerId && b.Second < 0
            && (Physics.Bodies.WorldCentre(b, world.Train) - p.Site.CrateStack.FirstOrDefault()).Length < 60);

    /// <summary>
    /// To the free end of a heavy crate someone's holding: across it from them (if they've said where they are, else from
    /// the side we come from), facing it, and take hold.
    /// </summary>
    PlayerIntent? LendAHand(in PlayerState self, World world, Physics.Body crate)
    {
        var train = world.Train;
        var at = Physics.Bodies.WorldCentre(crate, train);
        var me = PlayerMotor.WorldPosition(self, train);
        var away = (calls.Where(crate.Carrier) is { } holder ? at - holder : me - at) with { Y = 0 };
        var stand = at + (away.Length > 0.01 ? away.Normalized : new Double3(1, 0, 0)) * 0.9;
        if (self.Parent != PlayerState.World)
            return GetDown(self, train, _plan!.Site.Side);
        var (step, there) = WalkTo(self, train.Line, _plan!.Spur.Index, stand with { Y = at.Y }, null);
        if (!there && (Flat(self.Position) - Flat(stand)).Length > 0.25)
        {
            Doing = "to lend a hand";
            return step;
        }
        double yaw = Math.Atan2(-(at.X - self.Position.X), -(at.Z - self.Position.Z));
        if (!Aligned(self, yaw))
            return new PlayerIntent { LookYaw = Turn(self, yaw) };
        Doing = "taking the other end";
        return Press();
    }

    /// <summary>Where the heavy crate in our hands has been, most recent last (T45): the back end walks where the front end went.</summary>
    readonly List<Double3> _trail = [];
    int _following = -1;

    /// <summary>
    /// The back end of a heavy crate: follow where it's been, a pace behind it, so the steps and the door come in the order
    /// the front end took them. Whoever has the front leads (and puts it down), a bot or not.
    /// </summary>
    PlayerIntent Follow(in PlayerState self, World world, Physics.Body crate)
    {
        var train = world.Train;
        var at = Physics.Bodies.WorldCentre(crate, train);
        if (_following != crate.Id)
        {
            _trail.Clear();
            _following = crate.Id;
        }
        if (_trail.Count == 0 || (_trail[^1] - at).Length > 0.2)
            _trail.Add(at);
        // After it, it's this car we'll be walking out of.
        if (crate.Parent > 0)
            _car = crate.Parent;
        // The point on the trail a pace back from the crate.
        var target = _trail[^1];
        double back = 0;
        for (int i = _trail.Count - 1; i > 0 && back < TrailBehind; i--)
        {
            back += (_trail[i] - _trail[i - 1]).Length;
            target = _trail[i - 1];
        }
        var local = self.Parent == PlayerState.World ? target : train.Frames[self.Parent].ToLocal(target);
        if (back < TrailBehind * 0.5 || Near(self, local, 0.3))
        {
            // Keep facing it while the front end stands.
            var to = self.Parent == PlayerState.World ? at : train.Frames[self.Parent].ToLocal(at);
            double yaw = Math.Atan2(-(to.X - self.Position.X), -(to.Z - self.Position.Z));
            Doing = "holding the back end";
            return new PlayerIntent { LookYaw = Turn(self, yaw) };
        }
        return Head(self, local, "carrying the back end");
    }

    /// <summary>How far behind the crate, along where it's been, the back end walks.</summary>
    const double TrailBehind = 1.0;

    /// <summary>
    /// The cargo car in the engine's rake with the most room, counting crates already put down in it and the ones others
    /// say they're taking there; of equal ones, the nearest the stack.
    /// </summary>
    static int Roomiest(World world, StopPlan p, CrewCalls calls, int member)
    {
        var train = world.Train;
        // Put down inside a car and settling: that room's taken. One left on a car's steps isn't in it (T50).
        var pending = world.Bodies.All.Where(b => b.Kind == Physics.BodyKind.Cargo && b.Carrier < 0 && b.Parent > 0 && StopPlan.Inside(train, b))
            .GroupBy(b => b.Parent).ToDictionary(g => g.Key, g => g.Count());
        var stack = p.Site.CrateStack.Length > 0 ? p.Site.CrateStack[0] : train.Frames[0].Origin;
        return StopPlan.WithRoom(train).Select(v => (v.Id, Room: 1 - v.Load - p.Site.LoadPerCrate * (pending.GetValueOrDefault(v.Id) + calls.BoundFor(v.Id, member))))
            .Where(x => x.Room > 1e-6).OrderByDescending(x => Math.Round(x.Room, 3)).ThenBy(x => (train.Frames[x.Id].Origin - stack).Length)
            .Select(x => x.Id).DefaultIfEmpty(-1).First();
    }

    /// <summary>The nearest crate a hand can pick up from the ground (<see cref="StopPlan.Loose"/>) on the working side.</summary>
    static Physics.Body? Crate(World world, StopPlan p, in PlayerState self)
    {
        var train = world.Train;
        var me = self.Position;
        Double3 At(Physics.Body b) => b.Parent == PlayerState.World ? b.Centre : train.Frames[b.Parent].ToWorld(b.Centre);
        return world.Bodies.All.Where(b => b.Kind == Physics.BodyKind.Cargo && p.Loose(world, b)).OrderBy(b => (At(b) - me).Length).FirstOrDefault();
    }

    /// <summary>The nearest heavy crate lying loose on the working side (T45).</summary>
    static Physics.Body? HeavyCrate(World world, StopPlan p, in PlayerState self)
    {
        var train = world.Train;
        var me = self.Position;
        return world.Bodies.All.Where(b => b.Kind == Physics.BodyKind.Heavy && p.Loose(world, b))
            .OrderBy(b => (Physics.Bodies.WorldCentre(b, train) - me).Length).FirstOrDefault();
    }

    /// <summary>A cargo car in the engine's rake with its door on the working side still open, that's this hand's to shut.</summary>
    static int? OpenSideDoor(World world, StopPlan p, CrewCalls calls, int member, in PlayerState self)
    {
        var train = world.Train;
        var me = PlayerMotor.WorldPosition(self, train);
        return calls.ClaimDoor(member, [.. p.OpenSideDoors(train)], car => (train.Frames[car].Origin - me).Length);
    }

    /// <summary>A car's sliding door on one side (+1 right), if it has one.</summary>
    public static int? SideDoor(CarShape shape, int side) =>
        shape.DoorList.Where(d => Math.Sign(d.Box.Centre.X) == side && Math.Abs(d.Box.Centre.Z) < 1).Select(d => (int?)d.Index).FirstOrDefault();

    /// <summary>Up a car's steps to its side door and shut it.</summary>
    PlayerIntent? ShutUp(in PlayerState self, World world, StopPlan p, int car)
    {
        _car = car;
        var train = world.Train;
        var layout = train.Dynamics.Tuning.Geometry.Interior!;
        var frame = train.Frames[car];
        int side = p.Site.Side;
        double w = frame.Shape.Bounds.Max.X, sd = layout.SideDoorWidth / 2;
        var landing = new Double3(side * (w + layout.StepWidth / 2), layout.FloorHeight, 0);
        double facingIn = side > 0 ? Math.PI / 2 : -Math.PI / 2;
        if (self.Parent == car && self.Surface == Surface.Deck)
        {
            if (!Near(self, landing, 0.25) || !Aligned(self, facingIn))
                return Walk(self, landing, facingIn, "shutting up");
            Doing = "shutting up";
            return new PlayerIntent { Buttons = PlayerButtons.Use };
        }
        if (self.Parent != PlayerState.World)
            return GetDown(self, train, side);
        var local = frame.ToLocal(PlayerMotor.WorldPosition(self, train));
        double outward = side * local.X - w;
        if (outward > 0.1 && outward < layout.StepWidth - 0.1 && local.Z > -sd - 4 * layout.StepDepth - 1.2 && local.Z < -sd - 0.3)
            return Head(self, frame.ToWorld(landing with { Y = 0, Z = local.Z + 1.5 }), "shutting up");
        Doing = "shutting up";
        return WalkTo(self, train.Line, p.Spur.Index, frame.ToWorld(landing with { Y = 0, Z = -sd - 4 * layout.StepDepth - 0.4 }), null).Step;
    }

    /// <summary>A step towards a point on the player's own car (its frame), facing a way at the end.</summary>
    PlayerIntent Walk(in PlayerState self, Double3 target, double yaw, string doing)
    {
        Doing = doing;
        var (step, _) = WarmUp.Steer(self, target, yaw);
        return step;
    }

    /// <summary>Straight at a world point, facing where it's going (up a flight of steps).</summary>
    PlayerIntent Head(in PlayerState self, Double3 target, string doing)
    {
        Doing = doing;
        var to = new Double3(target.X - self.Position.X, 0, target.Z - self.Position.Z);
        double turn = Wrap(Math.Atan2(-to.X, -to.Z) - self.Yaw);
        return new PlayerIntent { LookYaw = (float)Math.Clamp(turn, -0.4, 0.4), MoveZ = Math.Abs(turn) < 0.3 ? 0.6f : 0 };
    }

    static bool Near(in PlayerState self, Double3 target, double within) => (Flat(self.Position) - Flat(target)).Length < within;
    static Double3 Flat(Double3 v) => new(v.X, 0, v.Z);

    /// <summary>Done here once aboard: on the ground, the walker climbs the nearest car's ladder.</summary>
    PlayerIntent? Aboard(in PlayerState self, StopPlan p)
    {
        if (self.Parent == PlayerState.World)
        {
            Doing = "boarding";
            return null;
        }
        _done.Add(p.Facility);
        _plan = null;
        Worked++;
        return null;
    }

    /// <summary>Down into the gap behind the cut car along the roofs, and on the coupler plate, hold Use to cut it.</summary>
    PlayerIntent? Cut(in PlayerState self, TrainOnLine train, StopPlan p)
    {
        int car = p.CutBehind;
        if (self.Surface == Surface.Coupler && self.Parent == car)
        {
            // Facing out to the right, away from both doors: Use facing one works the door instead.
            const double yaw = -Math.PI / 2;
            if (!Aligned(self, yaw))
                return new PlayerIntent { LookYaw = Turn(self, yaw) };
            Doing = "cutting";
            return new PlayerIntent { Buttons = PlayerButtons.Use };
        }
        // On the ground or in the wrong gap, the walker gets back up onto the roofs.
        if (self.Surface != Surface.Roof || self.Parent <= 0)
            return null;
        Doing = "to the cut";
        int here = self.Parent;
        int direction = here <= car ? +1 : -1; // +1 walks toward the back
        bool last = here == car || here == train.VehicleBehind(car);
        return AlongRoofs(self, train, direction, jumpGaps: !last);
    }

    /// <summary>Off the train on the switch's side, to the stand beside the points, and hold Use until they go over.</summary>
    PlayerIntent? Throw(in PlayerState self, TrainOnLine train, Branch branch)
    {
        if (self.Parent != PlayerState.World)
            return GetDown(self, train, branch.Side);
        Doing = "at the switch";
        var stand = TrackPoint(train.Line, RailLine.MainPath, branch.Toe, branch.Side * StandOff);
        var (step, there) = WalkTo(self, train.Line, RailLine.MainPath, stand, null);
        return there ? new PlayerIntent { Buttons = PlayerButtons.Use } : step;
    }

    /// <summary>
    /// The driver on its own (<see cref="StopDriver.SetBackAlone"/>): down out of the cab, the switch set back, and back up
    /// into the cab. Null once it's back at the controls with the switch right.
    /// </summary>
    public PlayerIntent? SetBackAlone(in PlayerState self, World world, Branch? branch)
    {
        var train = world.Train;
        if (branch is { } b && train.Diverging(b.Index))
            return self.Surface == Surface.Air ? new PlayerIntent() : Throw(self, train, b);
        if (PlayerMotor.InCab(self, train))
            return null;
        if (self.Surface == Surface.Air || Math.Abs(train.Dynamics.Velocity) > 0.05)
            return new PlayerIntent();
        return IntoCab(self, train, self.LineHint >= 0 && self.Parent == PlayerState.World ? Side(train, self.Position, self.LineHint) : 1);
    }

    /// <summary>Which side of the main line a point on the ground is (+1 right).</summary>
    static int Side(TrainOnLine train, Double3 at, double lineHint)
    {
        var t = train.Line.Sample(RailLine.MainPath, lineHint);
        var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
        return Double3.Dot(at - t.Position, right) >= 0 ? 1 : -1;
    }

    /// <summary>Into the engine's cab up its steps on the working side, and stand there (it's warm while the fire's lit).</summary>
    PlayerIntent? Ride(in PlayerState self, TrainOnLine train, StopPlan p)
    {
        if (PlayerMotor.InCab(self, train))
        {
            Doing = "in the cab";
            return new PlayerIntent();
        }
        if (self.Parent != PlayerState.World)
            return GetDown(self, train, p.Spur.Side);
        // You don't climb onto an engine that's moving.
        if (Math.Abs(train.Dynamics.Velocity) > 0.05)
            return new PlayerIntent();
        // By the cab's door on the side it's on (the crane's operator is across the track from the rest, T54): there's no
        // walking through the train.
        return IntoCab(self, train, SideOf(train, p, self.Position, self.LineHint));
    }

    /// <summary>From the ground on one side (+1 right), to the foot of the cab's steps there and up into it.</summary>
    PlayerIntent? IntoCab(in PlayerState self, TrainOnLine train, int side)
    {
        Doing = "to the cab";
        var engine = train.Frames[0];
        var foot = engine.ToWorld(new Double3(side * (engine.Shape.Bounds.Max.X + 0.5), 0, CabDoorZ(train)));
        var inward = engine.DirToWorld(new Double3(-side, 0, 0));
        var (step, there) = WalkTo(self, train.Line, train.Dynamics.Path, foot, Math.Atan2(-inward.X, -inward.Z));
        return there ? new PlayerIntent { MoveZ = 1, Buttons = PlayerButtons.Use } : step;
    }

    /// <summary>The middle of the cab's side doorways along the engine (between the side wall and the back pillar).</summary>
    static double CabDoorZ(TrainOnLine train)
    {
        var g = train.Dynamics.Tuning.Geometry;
        double cabBack = g.EngineLength / 2 - g.Engine.TenderLength;
        return (cabBack - g.Engine.DoorWidth + cabBack - 0.15) / 2;
    }

    /// <summary>
    /// Off the train onto the ballast on one side (+1 right): out through the cab's doorway and off its step, or off the
    /// edge of a roof or the coupler plate, slowly enough to land without a roll (the train is standing).
    /// </summary>
    PlayerIntent? GetDown(in PlayerState self, TrainOnLine train, int side)
    {
        double facing = side > 0 ? -Math.PI / 2 : Math.PI / 2;
        // Never off anything that's moving.
        if (self.Parent >= 0 && Math.Abs(train.RakeOf(self.Parent).Velocity) > 0.05)
            return new PlayerIntent();
        // In the cab or on the engine's deck by its doorway.
        if (self.Parent == 0 && self.Surface == Surface.Deck)
        {
            Doing = "out of the cab";
            return Edge(self, new Double3(side * (train.Frames[0].Shape.Bounds.Max.X + 1), self.Position.Y, CabDoorZ(train)), facing);
        }
        if (self.Surface is Surface.Roof or Surface.Coupler && self.Parent >= 0)
        {
            Doing = "getting down";
            return Edge(self, new Double3(side * (train.Frames[self.Parent].Shape.Bounds.Max.X + 1), self.Position.Y, self.Position.Z), facing);
        }
        return null;
    }

    /// <summary>Walk to a point on the player's own car in its frame, facing a way, at a stroll (so it's not a roll).</summary>
    static PlayerIntent Edge(in PlayerState self, Double3 target, double yaw)
    {
        if (!Aligned(self, yaw))
            return new PlayerIntent { LookYaw = Turn(self, yaw) };
        var (step, _) = WarmUp.Steer(self, target, yaw);
        step.MoveX = Math.Clamp(step.MoveX, -0.5f, 0.5f);
        step.MoveZ = Math.Clamp(step.MoveZ, -0.5f, 0.5f);
        return step;
    }

    /// <summary>Along the roofs toward the back (+1) or front (−1), jumping the gaps unless it's to step off into one.</summary>
    static PlayerIntent AlongRoofs(in PlayerState self, TrainOnLine train, int direction, bool jumpGaps)
    {
        double yaw = direction < 0 ? 0 : Math.PI;
        if (!Aligned(self, yaw))
            return new PlayerIntent { LookYaw = Turn(self, yaw) };
        double half = train.Frames[self.Parent].Shape.HalfLength, z = self.Position.Z;
        double lateral = Math.Clamp(-self.Position.X * 0.8 * (direction < 0 ? 1 : -1), -1, 1);
        var intent = new PlayerIntent { MoveZ = 1, MoveX = (float)lateral, Buttons = jumpGaps ? PlayerButtons.Run : PlayerButtons.None };
        bool nearEnd = direction < 0 ? z < -half + 0.45 : z > half - 0.45;
        int beyond = direction < 0 ? train.VehicleAhead(self.Parent) : train.VehicleBehind(self.Parent);
        if (jumpGaps && nearEnd && beyond > 0)
        {
            if (WarmUp.CanJumpGap(self, train, null))
                intent.Buttons |= PlayerButtons.Jump;
            else
                intent.MoveZ = 0; // square up on the centreline first (or wait out the curve)
        }
        return intent;
    }

    /// <summary>A point beside a path: along it, and across it (+ right, looking up the line).</summary>
    static Double3 TrackPoint(RailLine line, int path, double along, double across)
    {
        var s = line.Sample(path, along);
        return s.Position + Double3.Cross(s.Tangent, Double3.Up).Normalized * across;
    }

    /// <summary>Where a world point is in terms of a path: how far along it, and how far across (+ right).</summary>
    static (double Along, double Across) TrackCoords(RailLine line, int path, Double3 world, double hint)
    {
        var (_, along) = line.Nearest(world, ref hint);
        var s = line.Sample(path, along);
        return (along, Double3.Dot(world - s.Position, Double3.Cross(s.Tangent, Double3.Up).Normalized));
    }

    /// <summary>
    /// One step on foot towards a point on the ground, going round the train rather than into it: out to the side first
    /// if it's close to the track, along beside it, then in. True once it's there (and facing <paramref name="yaw"/>).
    /// </summary>
    static (PlayerIntent Step, bool There) WalkTo(in PlayerState self, RailLine line, int path, Double3 target, double? yaw)
    {
        var (a, x) = TrackCoords(line, path, self.Position, self.LineHint);
        var (ta, tx) = TrackCoords(line, path, target, self.LineHint);
        int side = tx >= 0 ? 1 : -1;
        double da = ta - a;
        var aim = Math.Abs(da) > 1.5 && Math.Abs(x) < Clear - 0.2 ? TrackPoint(line, path, a, side * Clear)
            : Math.Abs(da) > 6 ? TrackPoint(line, path, a + Math.Sign(da) * 6, side * Math.Max(Clear, Math.Abs(tx)))
            : target;
        var to = new Double3(aim.X - self.Position.X, 0, aim.Z - self.Position.Z);
        double distance = to.Length;
        if (aim == target && distance < 0.25)
            return yaw is not { } y || Aligned(self, y) ? (new PlayerIntent(), true) : (new PlayerIntent { LookYaw = Turn(self, y) }, false);
        double heading = Math.Atan2(-to.X, -to.Z);
        double turn = Wrap(heading - self.Yaw);
        bool far = (target - self.Position).Length > 4;
        return (new PlayerIntent
        {
            LookYaw = (float)Math.Clamp(turn, -0.4, 0.4),
            MoveZ = Math.Abs(turn) < 0.3 ? (float)Math.Clamp(distance * 1.5, 0.2, 1) : 0,
            Buttons = far ? PlayerButtons.Run : PlayerButtons.None,
        }, false);
    }

    static float Turn(in PlayerState self, double yaw) => (float)Math.Clamp(Wrap(yaw - self.Yaw), -0.5, 0.5);
    static bool Aligned(in PlayerState self, double yaw) => Math.Abs(Wrap(yaw - self.Yaw)) < 0.1;
    static double Wrap(double a) => Math.IEEERemainder(a, 2 * Math.PI);
}
