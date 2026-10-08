using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Enemies;

/// <summary>
/// CINDER HOUNDS · heat, scent · rear (GDD v1.1 §21, App. A.3). A pack running the line behind the train, gaining on every
/// grade. Rule: keep the rear cannon crewed. A cannon shot at the pack drives it off, and it's loud (it feeds the loudness
/// meter, and the Choir). Any that board become a pack fight the crew bludgeons together: each takes several blows, and
/// bites hard; a crewmate bitten down to their last is pinned and mauled, and a friend has the rescue window to club it off.
/// Once aboard they stay (GDD App. F, 6 Oct 2026; note 269): left alone, they eat the car's supplies and keep setting it
/// alight, until they're killed or their car is cut loose.
/// </summary>
public sealed class CinderHound(int id, int pack) : Enemy(id)
{
    public override Want Want => Want.Kill;
    double _biteTimer, _boredTimer, _from;
    bool _stays;

    public override EnemyKind Kind => EnemyKind.CinderHound;
    public override PressureZone Zone => PressureZone.Rear;
    public override Sense Sense => Sense.Heat;
    /// <summary>Running on the line behind, it's in the open: the rear cannon's work (App. A.3). Aboard, the car's walls are round it.</summary>
    public override bool Exposed => Attached < 0 && !Gone;
    public override bool GunAnswers => true;
    /// <summary>Aboard, it's in reach of a tool (App. A.3 PACK FIGHT).</summary>
    public override double MeleeRadius => Attached >= 0 ? 0.8 : 0;
    public int Pack { get; } = pack;
    /// <summary>
    /// A runner in the hound run (note 328): sent at a fast train, faster than it, and answered one at a time by a ball landing
    /// near it, not by any round fired near the pack. Host only (the director's; a client's mirror never steps it).
    /// </summary>
    public bool Runner { get; init; }
    /// <summary>
    /// A runner of the lane ahead (note 405): put down in front of the train, it meets it, across the line in the lamp, and
    /// leaps aboard the first car behind the engine. The forward gun's.
    /// </summary>
    public bool Ahead { get; init; }
    /// <summary>A runner of a flank lane (note 418): put down out in the open abeam the train, it runs in to the car alongside.</summary>
    public bool Flank { get; init; }
    public override bool StaysAboard => _stays && Attached >= 0;

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.CinderHounds;
        _stays = t.StayAboard;
        Extra = Pack;
        var train = ctx.Train.Dynamics;
        // A runner (note 328) is sent off by a ball landing near it, and only that: the guns answer the run one hound at a time.
        if (Runner && Attached < 0 && Phase is SpinePhase.Telegraph or SpinePhase.Commit)
        {
            var at = WorldPosition(ctx.Train);
            double scatter = ctx.World.Director?.Tuning.Run.Scatter ?? 0;
            if (ctx.Landed.Any(l => (l - at).Length <= scatter))
            {
                ctx.World.Director?.RunnerEnded(Pack, 0);
                Enter(ctx, SpinePhase.BreakOff);
                Enter(ctx, SpinePhase.Gone);
                return;
            }
        }
        // Sustained fire from a gun in range drives a running pack off, dead or not (App. A.3 break off).
        else if (Attached < 0 && Phase is SpinePhase.Telegraph or SpinePhase.Commit && ctx.World.Combat is { } combat
            && ctx.RoundsNear(WorldPosition(ctx.Train), combat.Guns.Range, t.SuppressWindowSeconds) >= t.SuppressRounds)
        {
            Enter(ctx, SpinePhase.BreakOff);
            Enter(ctx, SpinePhase.Gone);
            return;
        }
        switch (Phase)
        {
            case SpinePhase.Dormant:
                Enter(ctx, SpinePhase.Telegraph); // acquired: the howl behind, closing
                break;
            case SpinePhase.Telegraph when Attached >= 0:
                // Snarling on the car after it's been clubbed off someone: at it again once the window's passed.
                if (PhaseSeconds >= ctx.Tuning.MinReactionSeconds)
                    Enter(ctx, SpinePhase.Commit);
                break;
            case SpinePhase.Telegraph when Ahead:
                // Howling in the lamp ahead, where it was put down: the train comes on.
                if (PhaseSeconds >= t.HowlSeconds)
                    Enter(ctx, SpinePhase.Commit);
                break;
            case SpinePhase.Telegraph:
                Run(ctx, t, closing: 0);
                if (PhaseSeconds >= t.HowlSeconds)
                    Enter(ctx, SpinePhase.Commit);
                break;
            case SpinePhase.Commit when Attached >= 0:
                // PACK FIGHT on the rear car, until it's clubbed off or there's nobody left to bite.
                if (ctx.Train.Dynamics.Consist.IndexOf(Attached) < 0)
                {
                    Enter(ctx, SpinePhase.Gone); // its car cut loose: it goes with it
                    break;
                }
                Maul(ctx, t);
                break;
            case SpinePhase.Commit when Ahead:
                Meet(ctx);
                break;
            case SpinePhase.Commit when Flank:
                Run(ctx, t, closing: 0);
                RunIn(ctx);
                break;
            case SpinePhase.Commit:
                double gap = train.RearDistance - LineDistance;
                if (gap > 250)
                {
                    Enter(ctx, SpinePhase.BreakOff); // outrun: the train is faster than they can sustain
                    Enter(ctx, SpinePhase.Gone);
                    break;
                }
                if (gap <= t.LeapDistance && (Runner || train.Speed <= t.MaxSpeed))
                {
                    Board(ctx);
                    break;
                }
                Run(ctx, t, Runner ? ctx.World.Director?.Tuning.Run.Closing ?? t.ClosingSpeed : t.ClosingSpeed);
                break;
            case SpinePhase.Grab:
                break;
        }
    }

    void Run(EnemyContext ctx, HoundTuning t, double closing)
    {
        // A runner keeps up with any train and closes on it (note 328: a hot train can't outrun the run).
        double speed = Runner ? ctx.Train.Dynamics.Speed + closing : Math.Min(t.MaxSpeed, ctx.Train.Dynamics.Speed + closing);
        LineDistance += speed * SimConstants.TickSeconds;
    }

    /// <summary>
    /// The lane ahead (note 405): running in against the train, across the line to its far side by the time they meet, and
    /// aboard the first car behind the engine as it comes alongside (the engine's hooded, nothing to leap onto: note 338).
    /// </summary>
    void Meet(EnemyContext ctx)
    {
        var run = ctx.World.Director?.Tuning.Run ?? new HoundRunTuning();
        var train = ctx.Train;
        var consist = train.Dynamics.Consist.Vehicles;
        int car = consist[Math.Min(1, consist.Count - 1)].Id;
        var frame = train.Frames[car];
        if (frame.ToLocal(WorldPosition(train)).Z >= -frame.Shape.HalfLength)
        {
            ctx.World.Director?.RunnerEnded(Pack, 2);
            Attached = car;
            Local = Spot(ctx, car, -(frame.Shape.HalfLength - 2.5), Lateral);
            Aboarded();
            return;
        }
        if (LineDistance < train.Dynamics.RearDistance)
        {
            Enter(ctx, SpinePhase.BreakOff); // the train's by: nothing left to leap onto
            Enter(ctx, SpinePhase.Gone);
            return;
        }
        double closing = train.Dynamics.Speed + run.AheadSpeed;
        double meet = Math.Max(SimConstants.TickSeconds, (LineDistance - train.Dynamics.Distance) / Math.Max(1, closing));
        if (_from == 0)
            _from = Math.Sign(Lateral); // the flank it came in from
        double across = -_from * run.AheadCross;
        Lateral += (across - Lateral) * Math.Min(1, SimConstants.TickSeconds / meet);
        LineDistance -= run.AheadSpeed * SimConstants.TickSeconds;
    }

    /// <summary>
    /// A flank lane (note 418): in across the open ground, keeping pace, and aboard the car it comes alongside (not the
    /// engine's hood: note 338), on its side, once it's at the car's edge. Abeam the engine (note 443), it falls back along
    /// it as it comes in, to be alongside the first car behind it at the edge.
    /// </summary>
    void RunIn(EnemyContext ctx)
    {
        var run = ctx.World.Director?.Tuning.Run ?? new HoundRunTuning();
        var train = ctx.Train;
        double edge = train.Frames[train.Dynamics.Consist.Vehicles[^1].Id].Shape.HalfWidth + 0.6;
        var (best, local, z) = Alongside(train);
        if (best < 0)
            return;
        double left = Math.Abs(Lateral) - edge;
        if (left > 0)
        {
            double step = Math.Min(left, run.FlankSpeed * SimConstants.TickSeconds);
            Lateral -= Math.Sign(Lateral) * step;
            // The way along to the nearest of that car, made in step with the way in (local −Z is up the line).
            LineDistance += (local - z) * (step / left);
            return;
        }
        ctx.World.Director?.RunnerEnded(Pack, 2);
        Attached = best;
        Local = Spot(ctx, best, z, Lateral);
        Aboarded();
    }

    /// <summary>The car (not the engine) nearest along the train to where this runner is, where it is on it, and the nearest of it it can leap onto.</summary>
    (int Car, double Local, double Z) Alongside(TrainOnLine train)
    {
        var at = WorldPosition(train);
        int best = -1;
        double nearest = double.MaxValue, here = 0, z = 0;
        foreach (var v in train.Dynamics.Consist.Vehicles)
        {
            if (v.IsEngine)
                continue;
            var f = train.Frames[v.Id];
            double local = f.ToLocal(at).Z, off = Math.Max(0, Math.Abs(local) - f.Shape.HalfLength);
            if (off < nearest)
                (best, nearest, here, z) = (v.Id, off, local, Math.Clamp(local, -f.Shape.HalfLength + 1, f.Shape.HalfLength - 1));
        }
        return (best, here, z);
    }

    void Board(EnemyContext ctx)
    {
        if (Runner)
            ctx.World.Director?.RunnerEnded(Pack, 2);
        int rear = ctx.Train.Dynamics.Consist.Vehicles[^1].Id;
        Attached = rear;
        Local = Spot(ctx, rear, ctx.Train.Frames[rear].Shape.HalfLength - 2.5, Lateral);
        Aboarded();
    }

    // How far apart hounds on a roof keep (m), and how far in from its ends they stand.
    const double SpotApart = 1.6, SpotFromEnd = 1;

    /// <summary>
    /// Where on <paramref name="car"/>'s roof a hound coming aboard stands (note 471; the director, 8 Oct 2026: "they overlap on
    /// each other when on top of the car, they should pick their own spots to be"): at <paramref name="z"/> on its own side if
    /// that's clear of every other hound there, else the nearest spot along that side that is (the far side once it's full).
    /// </summary>
    Double3 Spot(EnemyContext ctx, int car, double z, double lateral)
    {
        var shape = ctx.Train.Frames[car].Shape;
        double end = shape.HalfLength - SpotFromEnd, x = lateral > 0 ? 0.6 : -0.6;
        var taken = ctx.World.ActiveEnemies.Where(e => e is CinderHound && e != this && !e.Gone && e.Attached == car).Select(e => e.Local).ToList();
        // On its own side (the side it came up), at z, then out from it both ways a step at a time; the far side only if its
        // own is full.
        foreach (double side in (double[])[x, -x])
            for (int k = 0; k < 40; k++)
            {
                double step = (k + 1) / 2 * SpotApart * (k % 2 == 0 ? 1 : -1);
                var at = new Double3(side, shape.RoofHeight, Math.Clamp(z + step, -end, end));
                if (taken.All(o => (o - at).Length >= SpotApart))
                    return at;
            }
        return new Double3(x, shape.RoofHeight, Math.Clamp(z, -end, end));
    }

    /// <summary>
    /// Whether this hound, where it is, can get its teeth into someone (note 471; the director, 8 Oct 2026: "Cinder Hounds were
    /// able to grab me through the car. I was in the car, they were on top"): only on the same side of a car's walls as it.
    /// Up on the roofs it has whoever's out on the train (a roof, a ladder, a landing, the gap), never anyone inside a car or
    /// the cab under it; dropped into a car at an open door (note 472), whoever's in that car with it.
    /// </summary>
    bool Reaches(TrainOnLine train, in PlayerState s) =>
        Attached >= 0 && Inside(train) ? s.Parent == Attached && PlayerMotor.Indoors(s, train) : !PlayerMotor.Indoors(s, train);

    /// <summary>In its car (dropped in at an open door, note 472), not on its roof.</summary>
    bool Inside(TrainOnLine train) => train.Frames[Attached].Shape.Interior is not null && Local.Y < train.Frames[Attached].Shape.RoofHeight - 1;

    // ---- Aboard (note 472) ----
    // What a hound aboard is doing, and which way it faces, is in Lateral (as mode × 4 + facing: replicated as every enemy's
    // is, so the clients draw it, E1's clips); when it began, in LineDistance (the PhaseSeconds it began at). On the line
    // they're its place; aboard, the car's frame and Local are.

    /// <summary>What it's doing aboard (<see cref="HoundMode.Still"/> off the train).</summary>
    public HoundMode Aboard => Attached >= 0 ? Decode(Lateral).Mode : HoundMode.Still;
    /// <summary>Which way it faces aboard, in its car's frame: 0 forward (−Z), 1 back (+Z), 2 to +X, 3 to −X.</summary>
    public int Facing => Attached >= 0 ? Decode(Lateral).Face : 0;
    /// <summary>How long it's been at <see cref="Aboard"/>.</summary>
    public double ModeSeconds => Math.Max(0, PhaseSeconds - LineDistance);

    /// <summary>A hound aboard's mode and facing from its replicated <see cref="Enemy.Lateral"/>.</summary>
    public static (HoundMode Mode, int Face) Decode(double lateral)
    {
        int code = Math.Max(0, (int)Math.Round(lateral));
        return ((HoundMode)Math.Min(code / 4, (int)HoundMode.Sniff), code % 4);
    }

    void SetMode(HoundMode mode, int face)
    {
        if (Aboard != mode || Attached < 0)
            LineDistance = PhaseSeconds;
        Lateral = (int)mode * 4 + face;
    }

    /// <summary>Just aboard: stood still where it came up, facing up the train, as ever.</summary>
    void Aboarded()
    {
        LineDistance = PhaseSeconds;
        Lateral = 0;
        _dir = 0;
        _home = Attached;
    }

    int _home = -1;

    /// <summary>The car it came aboard (note 472): its patrol's ground is this car, the cars behind it, and
    /// <see cref="HoundPatrolTuning.CarsAhead"/> ahead of it.</summary>
    public int Home => _home >= 0 ? _home : Attached;

    /// <summary>
    /// The front of this hound's ground aboard (note 472): <see cref="HoundPatrolTuning.CarsAhead"/> cars ahead of
    /// <see cref="Home"/>, but never the car right behind the engine (that gap's the cab's own, and cutting it is every car),
    /// so cutting the coupling ahead of it takes the hound with it wherever its patrol has it. −1 off the train.
    /// </summary>
    public int FrontCar(TrainOnLine train, HoundTuning t)
    {
        if (Attached < 0)
            return -1;
        int car = Home;
        for (int i = 0; i < t.Patrol.CarsAhead; i++)
        {
            int ahead = train.VehicleAhead(car);
            if (ahead < 0 || train.Vehicles[ahead].IsEngine || train.VehicleAhead(ahead) is var beyond && (beyond < 0 || train.Vehicles[beyond].IsEngine))
                break;
            car = ahead;
        }
        return car;
    }

    int _dir, _rolls, _leapTo = -1, _leapFromCar = -1;
    double _nextSniff = -1, _outAt = -1, _leapZ;
    Double3 _leapFrom;

    /// <summary>This hound's own dice: the same on every run of the same night.</summary>
    double Roll()
    {
        ulong x = (ulong)Id * 0x9E3779B97F4A7C15UL ^ (ulong)(++_rolls) * 0xBF58476D1CE4E5B9UL;
        x ^= x >> 31;
        x *= 0x94D049BB133111EBUL;
        x ^= x >> 29;
        return (x >> 11) * (1.0 / (1UL << 53));
    }

    double Range(double[] r) => r.Length < 2 ? (r.Length == 1 ? r[0] : 0) : r[0] + (r[1] - r[0]) * Roll();

    // How far in from a roof's ends it turns or leaps (m), and from a room's ends inside.
    const double RoofEnd = 0.8, RoomEnd = 0.6;

    /// <summary>
    /// Aboard with nobody it can reach in reach (note 472): along the roofs, over the gaps, in and out at open doors; after
    /// <paramref name="prey"/> if it has someone it can reach on the train further off.
    /// </summary>
    void Patrol(EnemyContext ctx, HoundTuning t, PlayerState? prey)
    {
        var p = t.Patrol;
        if (!p.On)
            return;
        var train = ctx.Train;
        double dt = SimConstants.TickSeconds, secs = ModeSeconds;
        if (_nextSniff < 0)
            _nextSniff = PhaseSeconds + Range(p.SniffEvery);
        switch (Aboard)
        {
            case HoundMode.Leap:
                Leaping(ctx, p);
                return;
            case HoundMode.Drop:
                if (secs >= p.DropSeconds)
                {
                    SetMode(HoundMode.Patrol, Roll() < 0.5 ? 0 : 1);
                    _outAt = PhaseSeconds + Range(p.InsideSeconds);
                }
                return;
            case HoundMode.Climb:
                if (secs >= p.ClimbSeconds)
                {
                    // Up over the edge above the door it climbed out of, onto the roof.
                    var shape = train.Frames[Attached].Shape;
                    Local = new Double3(Math.Sign(Local.X) * Math.Min(0.6, shape.HalfWidth - 0.4), shape.RoofHeight, Local.Z);
                    SetMode(HoundMode.Patrol, Roll() < 0.5 ? 0 : 1);
                }
                return;
            case HoundMode.Sniff:
                if (secs < p.SniffSeconds && prey is null)
                    return;
                _nextSniff = PhaseSeconds + Range(p.SniffEvery);
                SetMode(HoundMode.Patrol, Facing);
                break;
        }
        if (_dir == 0)
            _dir = Facing == 1 ? 1 : -1;
        if (prey is null && PhaseSeconds >= _nextSniff)
        {
            SetMode(HoundMode.Sniff, Facing);
            return;
        }
        var frame = train.Frames[Attached];
        var here = frame.Shape;
        bool inside = Inside(train);
        double speed = prey is null ? p.Walk : p.Chase;
        // Which way along: after its prey (on this car, toward them; on another, toward that car), or on the way it was going.
        if (prey is { } q)
        {
            int at = train.Dynamics.Consist.IndexOf(Attached), theirs = q.Parent >= 0 ? train.Dynamics.Consist.IndexOf(q.Parent) : -1;
            double toward = theirs == at || theirs < 0 ? frame.ToLocal(q.Parent >= 0 && q.Parent < train.Frames.Count ? train.Frames[q.Parent].ToWorld(q.Position) : q.Position).Z - Local.Z
                : theirs < at ? -1 : 1;
            if (Math.Abs(toward) > 0.05)
                _dir = Math.Sign(toward);
        }
        if (inside)
        {
            var room = here.Interior!.Value;
            if (_outAt < 0)
                _outAt = PhaseSeconds + Range(p.InsideSeconds);
            // Time it was out, at an open side door it's next to: up and out.
            if (PhaseSeconds >= _outAt && prey is null && SideDoorOpen(train, Attached, Local.Z, out int side))
            {
                Local = Local with { X = side * (room.HalfSize.X - 0.4) };
                _outAt = -1;
                SetMode(HoundMode.Climb, side > 0 ? 2 : 3);
                return;
            }
            double lo = room.Min.Z + RoomEnd, hi = room.Max.Z - RoomEnd;
            // Out time: to the nearest open side door it can get out at.
            if (PhaseSeconds >= _outAt && prey is null && OpenDoorZ(train, Attached) is { } dz)
                _dir = Math.Sign(dz - Local.Z) is var d && d != 0 ? d : _dir;
            Walk(ctx, speed, lo, hi);
            return;
        }
        double end = here.HalfLength - RoofEnd;
        if (_dir * Local.Z >= end - 1e-6)
        {
            // At the roof's end: over the gap to the next car if there's one to land on (not the hooded engine), else back.
            int next = _dir < 0 ? (Attached == FrontCar(train, t) ? -1 : train.VehicleAhead(Attached)) : train.VehicleBehind(Attached);
            if (next >= 0 && !train.Vehicles[next].IsEngine)
            {
                // Not onto another hound: one there, or on its way there, and it turns back instead.
                double landZ = -_dir * (train.Frames[next].Shape.HalfLength - RoofEnd);
                var land = new Double3(Local.X, train.Frames[next].Shape.RoofHeight, landZ);
                bool taken = ctx.World.ActiveEnemies.Any(e => e is CinderHound h && h != this && !h.Gone
                    && (h.Attached == next && !h.Inside(train) && (h.Local - land).Length < SpotApart || h._leapTo == next && Math.Abs(h._leapZ - landZ) < SpotApart));
                if (!taken)
                {
                    _leapTo = next;
                    _leapFromCar = Attached;
                    _leapZ = landZ;
                    _leapFrom = Local;
                    SetMode(HoundMode.Leap, _dir < 0 ? 0 : 1);
                    return;
                }
            }
            // After someone past the end of its ground: it stands at the end, facing them.
            if (prey is not null)
            {
                SetMode(HoundMode.Still, _dir < 0 ? 0 : 1);
                return;
            }
            _dir = -_dir;
        }
        // Over a car's open side door, now and then down and in.
        if (prey is null && Math.Abs(Local.Z) < 0.4 && SideDoorOpen(train, Attached, Local.Z, out int doorSide) && here.Interior is { } into
            && Roll() < p.InChance * dt / 0.5)
        {
            Local = new Double3(doorSide * (into.HalfSize.X - 0.4), into.Min.Y, Math.Clamp(Local.Z, into.Min.Z + RoomEnd, into.Max.Z - RoomEnd));
            SetMode(HoundMode.Drop, doorSide > 0 ? 3 : 2);
            return;
        }
        Walk(ctx, speed, -end, end);
    }

    /// <summary>Along its car (the roof or the room) at <paramref name="speed"/>, between <paramref name="lo"/> and <paramref name="hi"/>, turning at them and short of another hound.</summary>
    void Walk(EnemyContext ctx, double speed, double lo, double hi)
    {
        double z = Math.Clamp(Local.Z + _dir * speed * SimConstants.TickSeconds, lo, hi);
        var next = Local with { Z = z };
        bool blocked = ctx.World.ActiveEnemies.Any(e => e is CinderHound h && h != this && !h.Gone && h.Attached == Attached
            && Math.Abs(h.Local.Y - Local.Y) < 1 && (h.Local - next).Length < SpotApart * 0.8 && Math.Sign(h.Local.Z - Local.Z) == _dir);
        if (blocked || (z <= lo + 1e-9 && _dir < 0 || z >= hi - 1e-9 && _dir > 0) && Inside(ctx.Train))
        {
            _dir = -_dir;
            SetMode(HoundMode.Patrol, _dir < 0 ? 0 : 1);
            if (blocked)
                return;
        }
        Local = next;
        SetMode(HoundMode.Patrol, _dir < 0 ? 0 : 1);
    }

    /// <summary>
    /// Over the coupling gap: E1's leap clip is the arc and the reach; across it, it goes from where it took off to where it
    /// lands between <see cref="HoundPatrolTuning.LeapFrom"/> and <see cref="HoundPatrolTuning.LeapTo"/> of the clip, on its
    /// new car from halfway over.
    /// </summary>
    void Leaping(EnemyContext ctx, HoundPatrolTuning p)
    {
        var train = ctx.Train;
        if (_leapTo < 0 || _leapTo >= train.Frames.Count || _leapFromCar < 0 || _leapFromCar >= train.Frames.Count)
        {
            _leapTo = -1;
            SetMode(HoundMode.Patrol, Facing);
            return;
        }
        var land = new Double3(_leapFrom.X, train.Frames[_leapTo].Shape.RoofHeight, _leapZ);
        if (ModeSeconds >= p.LeapSeconds)
        {
            Attached = _leapTo;
            Local = land;
            _leapTo = -1;
            SetMode(HoundMode.Patrol, Facing);
            return;
        }
        double f = Math.Clamp((ModeSeconds / p.LeapSeconds - p.LeapFrom) / Math.Max(1e-6, p.LeapTo - p.LeapFrom), 0, 1);
        var at = Double3.Lerp(train.Frames[_leapFromCar].ToWorld(_leapFrom), train.Frames[_leapTo].ToWorld(land), f);
        Attached = f >= 0.5 ? _leapTo : _leapFromCar;
        var frame = train.Frames[Attached];
        Local = frame.ToLocal(at) with { Y = frame.Shape.RoofHeight };
    }

    /// <summary>Whether <paramref name="car"/> has a side door open, and which side (+1 / −1).</summary>
    static bool SideDoorOpen(TrainOnLine train, int car, double z, out int side)
    {
        side = 0;
        var v = train.Vehicles[car];
        foreach (var d in train.Frames[car].Shape.DoorList)
            if (Math.Abs(d.Box.Centre.Z) < 1 && Math.Abs(d.Box.Centre.X) > 0.5 && v.DoorOpen(d.Index) && Math.Abs(d.Box.Centre.Z - z) < 1)
            {
                side = Math.Sign(d.Box.Centre.X);
                return true;
            }
        return false;
    }

    /// <summary>Where along <paramref name="car"/> its nearest open side door is, or null.</summary>
    static double? OpenDoorZ(TrainOnLine train, int car)
    {
        var v = train.Vehicles[car];
        foreach (var d in train.Frames[car].Shape.DoorList)
            if (Math.Abs(d.Box.Centre.Z) < 1 && Math.Abs(d.Box.Centre.X) > 0.5 && v.DoorOpen(d.Index))
                return d.Box.Centre.Z;
        return null;
    }

    void Maul(EnemyContext ctx, HoundTuning t)
    {
        // Put aboard some other way than Board, Meet or RunIn (a test, a staging): home is where it is now.
        if (_home < 0)
            _home = Attached;
        var at = WorldPosition(ctx.Train);
        (int Id, double Distance, Double3 World, PlayerState State)? victim = null;
        double chaseFrom = t.Patrol.On ? Math.Max(12, t.Patrol.ChaseFrom) : 12;
        foreach (var (player, world) in ctx.LivingCrew())
        {
            double d = (world - at).Length;
            if (d <= chaseFrom && Reaches(ctx.Train, player.State) && (victim is null || d < victim.Value.Distance))
                victim = (player.Id, d, world, player.State);
        }
        // Nobody near (12 m, as ever): it eats the car and lights it, and patrols (note 472).
        if (victim is not { } v || v.Distance > 12)
        {
            _boredTimer += SimConstants.TickSeconds;
            if (t.StayAboard)
                Feed(ctx, t);
            else if (_boredTimer >= t.BoredSeconds)
            {
                Enter(ctx, SpinePhase.BreakOff);
                Enter(ctx, SpinePhase.Gone);
                return;
            }
            if (t.StayAboard)
                Patrol(ctx, t, victim is { } far ? far.State : null);
            return;
        }
        _boredTimer = 0;
        if (v.Distance > t.Reach)
        {
            // Out of its reach on the train: after them (note 472), over the gaps.
            Patrol(ctx, t, v.State);
            return;
        }
        if (Aboard is HoundMode.Patrol or HoundMode.Sniff)
            SetMode(HoundMode.Still, Facing);
        _biteTimer += SimConstants.TickSeconds;
        if (_biteTimer >= t.BiteEverySeconds)
        {
            _biteTimer = 0;
            // Bitten down to the last: it pins them (App. A.1, kills go through GRAB). Punish to Commit first: the spine's.
            var held = ctx.Crew.First(c => c.Player.Id == v.Id).Player.State;
            if (held.Health <= t.BiteDamage && Grab(ctx, v.Id, t.MaulSeconds))
                return;
            ctx.Bite(v.Id, t.BiteDamage, DeathCause.Mauled);
        }
    }

    /// <summary>
    /// Nobody near: it eats the car's supplies, and keeps setting the car alight (GDD App. F, 6 Oct 2026: "they keep setting
    /// the car alight while they eat the supplies, which forces the crew to confront them"; note 269). The fire is App. C.5's,
    /// one to a car; put out, it starts another once it's been left alone that long again.
    /// </summary>
    void Feed(EnemyContext ctx, HoundTuning t)
    {
        var train = ctx.Train;
        var car = train.Vehicles[Attached];
        car.CargoIntegrity = Math.Max(0, car.CargoIntegrity - t.CargoPerSecond * SimConstants.TickSeconds);
        if (_boredTimer < t.IgniteEverySeconds)
            return;
        _boredTimer = 0;
        if (ctx.World.ActiveEnemies.Any(e => e is CarFire f && !f.Gone && f.Attached == Attached))
            return;
        int into = Attached;
        double along = Local.Z;
        // No C.9 record: its table names no actor for a fire the hounds set (the burn's own deaths are recorded as ever).
        ctx.World.AddEnemy(i => CarFire.In(i, train, into, along, ctx.Tuning.CarFire));
        ctx.World.PackFires++;
    }

    /// <summary>A runner killed on the line (note 328: a ball on it) is counted for the run's report.</summary>
    public override void Struck(EnemyContext ctx, int by, double damage)
    {
        bool running = Runner && Attached < 0 && !Gone;
        base.Struck(ctx, by, damage);
        if (running && Gone)
            ctx.World.Director?.RunnerEnded(Pack, 1);
    }

    /// <summary>Mauled: it takes its kill and leaves the fight.</summary>
    protected override void Punish(EnemyContext ctx, int victim)
    {
        Kill(ctx, victim, DeathCause.Mauled);
        Enter(ctx, SpinePhase.BreakOff);
        Enter(ctx, SpinePhase.Gone);
    }

    /// <summary>Clubbed off a crewmate it holds: back to the fight.</summary>
    protected override void Rescued(EnemyContext ctx, int by) => ReturnToFight(ctx);

    void ReturnToFight(EnemyContext ctx)
    {
        base.Rescued(ctx, -1);
        // BreakOff then back into the fight: Telegraph (a snarl) and Commit again once the window's passed.
        Enter(ctx, SpinePhase.Telegraph);
    }
}

/// <summary>
/// CAR HUGGER · vibration · rear (GDD v1.1 §21, App. A.3). It lurks on low ground beside the track, and clamps onto the
/// rear car as it passes: a heavy grinding from the rear, and the train's top speed capped while it's on (the cap stacks with
/// grades: a capped long train may not crest a hill). It eats the car's shell and loot steadily; eaten through, the car
/// drops away with it. Anyone in front of its mouth is swallowed (a grab: friends pull them free or hit it). Rule: cut the
/// caboose or kill it. Uncoupled, it leaves with the car; bludgeoned from the rear platform, it dies fast to a group and
/// slowly to one (it heals between one player's blows).
/// </summary>
/// <remarks>Lurking it's free on the line; latched, <see cref="Enemy.Attached"/> is its car, <see cref="Enemy.Local"/> its mouth at the car's rear end.</remarks>
public sealed class CarHugger(int id) : Enemy(id)
{
    public override EnemyKind Kind => EnemyKind.CarHugger;
    public override PressureZone Zone => PressureZone.Rear;
    public override Sense Sense => Sense.Vibration;
    public override Want Want => Want.Cargo;
    public override double MeleeRadius => Attached >= 0 ? 1.2 : 0;
    /// <summary>Lurking by the line ahead or clamped on a car, it's a great body in the open (note 290).</summary>
    public override bool Exposed => !Gone;
    public override bool PullsFree => true;

    /// <summary>Clamped on a car (the telegraph onwards).</summary>
    public bool Latched => Attached >= 0 && Phase is SpinePhase.Telegraph or SpinePhase.Commit or SpinePhase.Grab or SpinePhase.Punish;
    public override int Drags => Latched ? Attached : -1;
    double _cap = 9, _factor = 1.6;
    public override double DragAbove => _cap;
    public override double DragFactor => _factor;

    /// <summary>Beside the track at a low spot ahead of the train.</summary>
    public static CarHugger Lurking(int id, double along, int side, CarHuggerTuning t) =>
        new(id) { LineDistance = along, Lateral = side * 2.6, Height = -0.3, Health = t.Health, _cap = t.SpeedCap, _factor = t.DragFactor };

    /// <summary>
    /// Where it lurks (App. B.3 "marsh, water crossings, low ground"; "cannot spawn on grades"): a marsh or a bridge this far
    /// ahead, level there. Null if there's none.
    /// </summary>
    public static double? Spot(World world, CarHuggerTuning t)
    {
        if (world.Route is not { } route)
            return null;
        var train = world.Train;
        double front = train.Dynamics.Distance;
        foreach (var f in route.Features.Where(f => f.Kind is Route.FeatureKind.Marsh or Route.FeatureKind.Bridge))
        {
            double at = (f.Start + f.End) * 0.5;
            if (at < front + t.LurkAheadMin || at > front + t.LurkAheadMax)
                continue;
            if (Math.Abs(train.Line.Sample(train.Dynamics.Path, at).GradePercent) <= t.MaxGrade)
                return at;
        }
        return null;
    }

    protected override void Tick(EnemyContext ctx)
    {
        var t = ctx.Tuning.CarHugger;
        var train = ctx.Train;
        _cap = t.SpeedCap;
        _factor = t.DragFactor;
        if (Phase == SpinePhase.Dormant)
        {
            // LURK until the rear car passes, then LATCH: the grinding, and the cap.
            if (train.Dynamics.RearDistance > LineDistance + 2 || train.Dynamics.Distance < LineDistance)
                return;
            int rear = train.Dynamics.Consist.Vehicles[^1].Id;
            if (rear == 0)
                return;
            var shape = train.Frames[rear].Shape;
            Attached = rear;
            Local = new Double3(0, 1.0, shape.HalfLength + 0.4);
            Enter(ctx, SpinePhase.Telegraph);
            return;
        }
        if (Attached < 0)
            return;
        // Cut loose, it goes with its car into the dark.
        if (Attached >= train.Frames.Count || train.Dynamics.Consist.IndexOf(Attached) < 0)
        {
            Enter(ctx, SpinePhase.BreakOff);
            Enter(ctx, SpinePhase.Gone);
            return;
        }
        // Healing between blows: fast to a group, slow alone.
        Health = Math.Min(t.Health, Health + t.RegenPerSecond * SimConstants.TickSeconds);
        // FEED: shell and loot, steadily.
        var car = train.Vehicles[Attached];
        double eaten = car.Eaten;
        // An armoured car's plate is slower eating (note 184).
        car.Eaten += car.Batter(t.ShellPerSecond * SimConstants.TickSeconds, train.Dynamics.Tuning);
        // Through the end wall (the breach, decided 1 Oct): every breachEaten of shell, boarded up or not, it's through again.
        if (t.BreachEaten > 0 && Math.Floor(car.Eaten / t.BreachEaten) > Math.Floor(eaten / t.BreachEaten)
            && Breaches.EndWall(train.Frames[Attached].Shape) is { } wall)
            car.Breach(wall);
        car.CargoIntegrity = Math.Max(0, car.CargoIntegrity - t.LootPerSecond * SimConstants.TickSeconds);
        if (car.Integrity <= 0)
        {
            // FINISH: eaten through, the car drops away with it (and whoever was in it goes with the car).
            int ahead = train.VehicleAhead(Attached);
            if (ahead >= 0)
                train.Uncouple(ahead);
            car.Taken = true;
            Enter(ctx, SpinePhase.BreakOff);
            Enter(ctx, SpinePhase.Gone);
            return;
        }
        if (Phase == SpinePhase.Telegraph && PhaseSeconds >= ctx.Tuning.MinReactionSeconds)
            Enter(ctx, SpinePhase.Commit);
        if (Phase != SpinePhase.Commit)
            return;
        // SWALLOW: in front of its mouth, the rear platform's end.
        var mouth = WorldPosition(train);
        var victim = ctx.LivingCrew().Where(c => (c.World - mouth).Length <= t.MouthReach && !c.Player.State.Has(PlayerFlags.Held))
            .OrderBy(c => (c.World - mouth).Length).Select(c => (int?)c.Player.Id).FirstOrDefault();
        if (victim is { } v)
            Grab(ctx, v, t.SwallowSeconds);
    }

    /// <summary>Eaten, then back to the car.</summary>
    protected override void Punish(EnemyContext ctx, int victim)
    {
        Kill(ctx, victim, DeathCause.Eaten);
        Enter(ctx, SpinePhase.BreakOff);
        Enter(ctx, SpinePhase.Telegraph);
    }

    /// <summary>Pulled free or hit off: it's still on the car, grinding.</summary>
    protected override void Rescued(EnemyContext ctx, int by)
    {
        base.Rescued(ctx, by);
        Enter(ctx, SpinePhase.Telegraph);
    }
}

/// <summary>What a Cinder Hound aboard is doing (note 472), replicated in its Lateral for E1's clips.</summary>
public enum HoundMode : byte { Still, Patrol, Leap, Drop, Climb, Sniff }
