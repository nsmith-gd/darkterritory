using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
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
    /// <summary>On the winch's first handle, or its second (spec D.2: "2 mandatory").</summary>
    Winch0,
    Winch1,
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

    /// <summary>A crew member says where they are: a vehicle id, or <see cref="PlayerState.World"/> on the ground.</summary>
    public void Say(int member, StopJob job, in PlayerState s) => _crew[member] = new(job, s.Alive ? s.Parent : PlayerState.World, s.Alive);

    public IEnumerable<Call> Crew => _crew.Values;
    public bool Has(StopJob job) => _crew.Values.Any(c => c.Alive && c.Job == job);
    /// <summary>A shunter and two for the winch, alive: the crew a winch stop needs.</summary>
    public bool CanWorkWinch => Has(StopJob.Shunter) && Has(StopJob.Winch0) && Has(StopJob.Winch1);

    /// <summary>Everyone alive with a part at the stop is on one of these vehicles.</summary>
    public bool Riding(IReadOnlyCollection<int> vehicles) =>
        _crew.Values.Where(c => c.Alive && c.Job is StopJob.Shunter or StopJob.Winch0 or StopJob.Winch1).All(c => vehicles.Contains(c.Vehicle));

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
    public static StopPlan? Ahead(World world, double from, IReadOnlySet<int> done)
    {
        if (world.Run is not { } run)
            return null;
        var train = world.Train;
        double points = world.Switches?.Tuning.PointsLength ?? 12;
        var g = train.Dynamics.Tuning.Geometry;
        StopPlan? best = null;
        foreach (var site in run.Sites)
        {
            // Crates can't be got into a car yet (T34), so a stop's worth making for its winch.
            if (site is not { Spur: >= 0, SledsLeft: > 0 } || !site.Has(ModuleKind.Winch) || done.Contains(site.Index))
                continue;
            var spur = train.Line.Branches[site.Spur];
            double hold = spur.Toe - points - 2;
            if (hold < from - 5 || best is not null && hold >= best.Hold)
                continue;
            int fit = SpurDrill.Capacity(g, spur, points);
            var vehicles = train.Dynamics.Consist.Vehicles;
            best = new StopPlan(site.Index, site, spur, hold, fit, vehicles.Count - 1 > fit ? vehicles[fit].Id : -1);
        }
        return best;
    }

    /// <summary>The engine's rake is standing where it stops for this, on the main line.</summary>
    public bool StandingAt(TrainOnLine train) =>
        train.OnMain && Math.Abs(train.Dynamics.Velocity) < 0.05 && Math.Abs(train.Dynamics.Distance - Hold) < 3;
}

/// <summary>One facility stop as the driver worked it, for the harness report.</summary>
/// <param name="Legs">Seconds spent on each leg, by name.</param>
public sealed record StopRecord(int Facility, string Kind, double Seconds, int SledsHauled, IReadOnlyDictionary<string, double> Legs);

/// <summary>
/// The driver's side of a facility stop, through the cab's controls and nothing else (the scripted
/// <see cref="SpurDrill"/>, done by a crew): stop short of the spur's points; once the rest are cut off, the switch is
/// over and the ones riding in are aboard, run the empties in to the buffer stop; wait for the winch; back out onto the
/// cars left waiting, at a crawl so they couple; stop clear of the points; once the switch is back and everyone's
/// aboard, go. It only stops where the crew can work the stop (<see cref="CrewCalls.CanWorkWinch"/>).
/// </summary>
public sealed class StopDriver(CrewCalls calls)
{
    public enum Leg : byte { Cruise, Approach, Held, SpurIn, Loading, BackOut, Clear, Depart }

    // Long enough for a crew to do their part at walking pace; past it, the stop is given up rather than the night.
    const double HeldGiveUp = 240, LoadingGiveUp = 420, AboardGiveUp = 120;

    readonly HashSet<int> _done = [];
    readonly List<StopRecord> _log = [];
    readonly Dictionary<string, double> _legs = [];
    int _ticks, _legStart, _stopStart, _stillTicks, _sledsAtStart;

    public Leg Doing { get; private set; }
    public StopPlan? Plan { get; private set; }
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
                    if (!calls.CanWorkWinch || train.Rakes.Count > 1 || !train.OnMain || StopPlan.Ahead(world, engine.Distance, _done) is not { } plan)
                        return null;
                    if (plan.Hold - engine.Distance > StoppingDistance(engine) + 80)
                        return null;
                    Plan = plan;
                    _legs.Clear();
                    _stopStart = _ticks;
                    _sledsAtStart = plan.Site.SledsLeft;
                    Begin(Leg.Approach);
                    return Toward(world, plan.Hold, +1, CruiseSpeed);
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
                    if (set && cut && calls.Riding(EngineRake(train)))
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
                    bool loaded = p.Site.SledsLeft == 0 || !calls.Has(StopJob.Winch0) || !calls.Has(StopJob.Winch1) || Waited > LoadingGiveUp;
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
                    return Toward(world, Plan!.Hold + 50, +1, 3); // brakes and flips the reverser at a stand
                Finish();
                return null; // away as usual
            default:
                return null;
        }
    }

    void Finish()
    {
        var p = Plan!;
        Begin(Leg.Cruise);
        _done.Add(p.Facility);
        _log.Add(new StopRecord(p.Facility, p.Site.Feature.Facility?.ToString() ?? "", Math.Round(Seconds(_ticks - _stopStart), 1),
            _sledsAtStart - p.Site.SledsLeft, new Dictionary<string, double>(_legs)));
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
    /// <summary>Walking beside the train, keep this far from its track's centreline (a car is 3 m wide).</summary>
    const double Clear = 2.4;
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
    /// <summary>What it's doing (for tests and traces).</summary>
    public string Doing { get; private set; } = "";
    /// <summary>Stops it has done its part at.</summary>
    public int Worked { get; private set; }

    /// <summary>This tick's intent for its part in a stop; null when there's nothing for it to do (walk as usual).</summary>
    public PlayerIntent? Decide(in PlayerState self, World world)
    {
        calls.Say(member, job, self);
        Doing = "";
        if (job == StopJob.None || !self.Alive || world.Run is null)
            return null;
        var train = world.Train;
        if (_plan is null)
        {
            // The train's standing short of the points for a stop (the driver only makes the ones the crew can work).
            if (!calls.CanWorkWinch || StopPlan.Ahead(world, train.Dynamics.Distance - 10, _done) is not { } plan || !plan.StandingAt(train))
                return null;
            _plan = plan;
            _wentIn = _reachedEnd = false;
        }
        var p = _plan;
        if (train.Dynamics.Path == p.Spur.Index && train.Dynamics.Distance > p.Spur.Toe)
            _wentIn = true;
        // The train's back together and away up the main line: that stop's over, done or not.
        if (train.OnMain && train.Rakes.Count == 1 && train.Dynamics.Distance > p.Hold + 20 && !train.Diverging(p.Spur.Index))
        {
            _done.Add(p.Facility);
            _plan = null;
            return null;
        }
        // Mid-air, nothing to do; on a ladder or inside a car, the walker knows the way out.
        if (self.Surface == Surface.Air)
            return new PlayerIntent();
        if (self.Surface == Surface.Ladder || self.Surface == Surface.Deck && self.Parent > 0)
            return null;
        // Too cold to keep at it: into the cab if it's near (the walker's way into a car if not), until properly warm again.
        if (cold is not null && self.Cold >= cold.OnsetSeconds * 0.85 && !PlayerMotor.NearHeat(self, train))
            _warming = true;
        else if (self.Cold <= WarmAgain)
            _warming = false;
        if (_warming)
        {
            var cab = train.Frames[0].ToWorld(train.Frames[0].Shape.Cab!.Value.Centre);
            var warming = (PlayerMotor.WorldPosition(self, train) - cab).Length < CabNear ? Ride(self, train, p) : null;
            Doing = "warming";
            return warming;
        }
        return job == StopJob.Shunter ? Shunt(self, world, p) : Crank(self, world, p);
    }

    PlayerIntent? Shunt(in PlayerState self, World world, StopPlan p)
    {
        var train = world.Train;
        bool set = train.Diverging(p.Spur.Index);
        if (!_wentIn)
        {
            if (!set && p.CutBehind >= 0 && train.Rakes.Count == 1)
                return Cut(self, train, p);
            return set ? Ride(self, train, p) : Throw(self, train, p);
        }
        // In the cab while the empties are down the spur, until the whole train's back together short of the points.
        bool back = train.Rakes.Count == 1 && train.OnMain && Math.Abs(train.Dynamics.Velocity) < 0.05 && train.Dynamics.Distance <= p.Hold + 3;
        if (!back)
            return Ride(self, train, p);
        return set ? Throw(self, train, p) : Aboard(self, p);
    }

    PlayerIntent? Crank(in PlayerState self, World world, StopPlan p)
    {
        var train = world.Train;
        var engine = train.Dynamics;
        bool atEnd = engine.Path == p.Spur.Index && engine.Distance > p.Spur.End - 6 && Math.Abs(engine.Velocity) < 0.05;
        _reachedEnd |= atEnd;
        if (!_reachedEnd)
            return Ride(self, train, p);
        if (!atEnd || p.Site.SledsLeft == 0)
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
    PlayerIntent? Throw(in PlayerState self, TrainOnLine train, StopPlan p)
    {
        if (self.Parent != PlayerState.World)
            return GetDown(self, train, p.Spur.Side);
        Doing = "at the switch";
        var stand = TrackPoint(train.Line, RailLine.MainPath, p.Spur.Toe, p.Spur.Side * StandOff);
        var (step, there) = WalkTo(self, train.Line, RailLine.MainPath, stand, null);
        return there ? new PlayerIntent { Buttons = PlayerButtons.Use } : step;
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
        Doing = "to the cab";
        int side = p.Spur.Side;
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
            intent.Buttons |= PlayerButtons.Jump;
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
