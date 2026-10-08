using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Run;

/// <summary>
/// A gantry crane at a facility (spec D.2, T48): "one player in an elevated cab drives X/Y/Z; ground crew rigs the load and
/// calls position. 2 mandatory. Crane operator cannot see the ground crew. Dropped loads kill." A gantry astride the track,
/// its castings stacked on the far side. The operator holds Use at the control stand at the near leg and is up in the cab:
/// the bridge runs along the track, the trolley across it, the hook up and down. Someone on the ground rigs a casting to
/// the hook. Set down on a cargo car's roof, it's lashed there and loaded; let go of high, it falls, and kills whoever's
/// under it.
/// <para>
/// Laid out from the route like the rest of a site, so every machine agrees where it stands; its state is the host's and
/// replicates in a Crane record.
/// </para>
/// </summary>
public sealed class Crane
{
    readonly CraneTuning _t;
    readonly Func<double, double, double, Double3> _at;

    /// <param name="at">A point beside the site's track: (along from the layout point, lateral on the site's side, up).</param>
    public Crane(CraneTuning t, Func<double, double, double, Double3> at)
    {
        _t = t;
        _at = at;
        Bridge = t.Along;
        Trolley = t.Span[1] - 1;
        Hook = t.Height - 1;
        Castings = [.. Enumerable.Range(0, t.Castings).Select(i => new Casting { At = Stacked(i) })];
    }

    public CraneTuning Tuning => _t;

    /// <summary>A casting: where it lies (world), what it's on, and whether it's on the hook, lashed on a car, or lost.</summary>
    public sealed class Casting
    {
        public CastingState State { get; set; }
        /// <summary>On the ground: where (world, its base). Lashed: where on its car (the car's frame).</summary>
        public Double3 At { get; set; }
        public int Car { get; set; } = -1;
    }

    public Casting[] Castings { get; }
    /// <summary>The bridge's place along the track, the trolley's across it (both from the layout point), the hook's height.</summary>
    public double Bridge { get; set; }
    public double Trolley { get; set; }
    public double Hook { get; set; }
    /// <summary>Who's at the controls this tick, and who's rigging (−1 for nobody). Host only, but the rig's progress replicates.</summary>
    public int Operator { get; internal set; } = -1;
    /// <summary>How fast it runs, of its full speed: its yard's power (level-design D.2), 0 when the power's dead.</summary>
    public double SpeedScale { get; internal set; } = 1;
    /// <summary>Its gantry brought down (Tower Jaw, note 363): dead for the night, whatever powers it.</summary>
    public bool Wrecked { get; private set; }

    /// <summary>Brought down: it moves no more.</summary>
    public void Wreck()
    {
        Wrecked = true;
        SpeedScale = 0;
    }
    public double Rigging { get; internal set; }
    internal int Rigger = -1;
    bool _releaseWas;

    public Casting? Hooked => Castings.FirstOrDefault(c => c.State == CastingState.Hooked);
    public int Left => Castings.Count(c => c.State is CastingState.Stacked or CastingState.Hooked);

    Double3 Stacked(int i) => _at(_t.Along + (i - (_t.Castings - 1) * 0.5) * _t.Spacing, _t.StackLateral, 0);

    /// <summary>The gantry's feet and top, for drawing: its four corners at the ground.</summary>
    public Double3 Corner(int end, int side) => _at(_t.Along + (end == 0 ? -0.5 : 0.5) * _t.Length, _t.Span[side], 0);
    public double Top => _t.Height + 0.6;
    /// <summary>The ends of the bridge where it is now, at the top.</summary>
    public Double3 BridgeEnd(int side) => _at(Bridge, _t.Span[side], Top);
    /// <summary>Where the hook is (world).</summary>
    public Double3 HookAt => _at(Bridge, Trolley, Hook);
    /// <summary>The control stand, at the foot of the near leg at the gantry's first end; the cab's up over it.</summary>
    public Double3 Controls => _at(_t.Along - 0.5 * _t.Length + 0.8, _t.Span[0] - 0.8, 0);
    public Double3 Cab => _at(_t.Along - 0.5 * _t.Length + 0.8, _t.Span[0], Top - 1.2);

    /// <summary>The casting's half-height (it hangs this far under the hook, its middle).</summary>
    public double CastingHalf => _t.CastingSize[1] * 0.5;

    /// <summary>A player's hands on the controls: standing at the stand, holding Use. Deterministic, so a client predicts it too.</summary>
    public bool AtControls(in PlayerState s, in PlayerIntent intent, TrainOnLine train) =>
        s.Alive && s.Parent == PlayerState.World && intent.Has(PlayerButtons.Use)
        && ((PlayerMotor.WorldPosition(s, train) - Controls) with { Y = 0 }).Length <= _t.ControlsReach;

    /// <summary>
    /// What's under the hook: a car it's over (and that car's roof height, or, down through its open roof hatch, what's
    /// under the hatch inside: T99), or the ground.
    /// </summary>
    public (int Car, double Y) Under(TrainOnLine train)
    {
        var hook = HookAt;
        foreach (var frame in train.Frames)
        {
            var local = frame.ToLocal(hook);
            if (Math.Abs(local.X) <= frame.Shape.HalfWidth && Math.Abs(local.Z) <= frame.Shape.HalfLength)
            {
                double top = Hatch(train, frame.Index, local) is { Fits: true } h ? h.Floor : frame.Shape.RoofHeight;
                return (frame.Index, frame.ToWorld(new Double3(local.X, top, local.Z)).Y);
            }
        }
        return (-1, _at(Bridge, Trolley, 0).Y);
    }

    /// <summary>
    /// A casting hung over a car's open roof hatch at <paramref name="local"/> (the hook, in the car's frame): whether it goes
    /// in through the opening and comes to rest under the roof line, and on what (the floor, the load, or the castings
    /// already in). One that won't clear the opening, or would stand up through it when set down, doesn't go in (T99
    /// playtest: "crates cannot be lowered to a point where they block the roof from closing"), so the hook stops over the
    /// roof. Null when it isn't over an open hatch at all.
    /// </summary>
    public (bool Fits, double Floor)? Hatch(TrainOnLine train, int car, Double3 local)
    {
        var shape = train.Frames[car].Shape;
        if (shape.Hatch is not { } hatch || !train.Vehicles[car].DoorOpen(CarShape.HatchBit))
            return null;
        var (hx, hz) = Footprint(train.Frames[car]);
        bool over = Math.Abs(local.X - hatch.Centre.X) < hatch.HalfSize.X + hx && Math.Abs(local.Z - hatch.Centre.Z) < hatch.HalfSize.Z + hz;
        if (!over)
            return null;
        bool clears = local.X - hx >= hatch.Min.X && local.X + hx <= hatch.Max.X && local.Z - hz >= hatch.Min.Z && local.Z + hz <= hatch.Max.Z;
        if (!clears)
            return (false, 0);
        // What it comes down on: the highest thing under its footprint below the roof, a casting already in included.
        double floor = 0;
        foreach (var (x, z) in new[] { (0.0, 0.0), (-hx, -hz), (hx, -hz), (-hx, hz), (hx, hz) })
            floor = Math.Max(floor, shape.TopAt(local.X + x, local.Z + z, hatch.Min.Y - 0.01)?.Top ?? 0);
        foreach (var c in Castings)
            if (c.State == CastingState.Loaded && c.Car == car && c.At.Y < hatch.Min.Y
                && Math.Abs(c.At.X - local.X) < 2 * hx && Math.Abs(c.At.Z - local.Z) < 2 * hz)
                floor = Math.Max(floor, c.At.Y + 2 * CastingHalf);
        return (floor + 2 * CastingHalf <= hatch.Min.Y, floor);
    }

    /// <summary>A casting's half-extents across and along a car (it hangs square to the world, the car's on a curve).</summary>
    (double X, double Z) Footprint(CarFrame frame)
    {
        var x = frame.DirToLocal(new Double3(1, 0, 0));
        double c = Math.Abs(x.X), s = Math.Abs(x.Z), a = _t.CastingSize[0] / 2, b = _t.CastingSize[2] / 2;
        return (c * a + s * b, s * a + c * b);
    }

    /// <summary>
    /// The casting on the hook is down in a car's hatch opening (T99): its top below the roof's top and its base below the
    /// roof's underside, so the lid can't come down on it.
    /// </summary>
    public bool InHatch(TrainOnLine train, int car)
    {
        if (Hooked is null || car < 0 || car >= train.Frames.Count || train.Frames[car].Shape.Hatch is not { } hatch)
            return false;
        var frame = train.Frames[car];
        var local = frame.ToLocal(HookAt);
        var (hx, hz) = Footprint(frame);
        bool over = Math.Abs(local.X - hatch.Centre.X) < hatch.HalfSize.X + hx && Math.Abs(local.Z - hatch.Centre.Z) < hatch.HalfSize.Z + hz;
        double bottom = local.Y - 2 * CastingHalf;
        return over && bottom < hatch.Max.Y;
    }

    /// <summary>
    /// The operator's hands on the controls this tick (host and client alike: the replicated state is the host's). The stick
    /// runs the bridge (forward is along the track, away from the stand) and the trolley (right is out across); Jump
    /// raises the hook, Brake lowers it, Fire lets go.
    /// </summary>
    public void Drive(in PlayerIntent intent, TrainOnLine train, double dt)
    {
        dt *= SpeedScale;
        Bridge = Math.Clamp(Bridge + intent.MoveZ * _t.BridgeSpeed * dt, _t.Along - 0.5 * _t.Length, _t.Along + 0.5 * _t.Length);
        Trolley = Math.Clamp(Trolley + intent.MoveX * _t.TrolleySpeed * dt, _t.Span[0] + 0.5, _t.Span[1] - 0.5);
        double lift = (intent.Has(PlayerButtons.Jump) ? 1 : 0) - (intent.Has(PlayerButtons.Brake) ? 1 : 0);
        Hook = Math.Clamp(Hook + lift * _t.HoistSpeed * dt, Lowest(train), _t.Height);
    }

    /// <summary>
    /// Where the bridge and trolley put the hook over a point (world): what an operator's eye does from the cab, by search
    /// (coarse, then fine) since the gantry's laid along a curve. The distance left across the ground is how near it gets.
    /// </summary>
    public (double Bridge, double Trolley, double Off) Over(Double3 world)
    {
        // The gantry doesn't move, so the answer for a point doesn't either: remembered to the centimetre (the crew ask
        // about the same roofs and castings every tick).
        var key = ((long)Math.Round(world.X * 100), (long)Math.Round(world.Z * 100));
        if (_over.TryGetValue(key, out var known))
            return known;
        return _over[key] = Search(world);
    }

    readonly Dictionary<(long, long), (double, double, double)> _over = new();

    (double Bridge, double Trolley, double Off) Search(Double3 world)
    {
        double lo = _t.Along - 0.5 * _t.Length, hi = _t.Along + 0.5 * _t.Length, x0 = _t.Span[0] + 0.5, x1 = _t.Span[1] - 0.5;
        double Off(double b, double x) => ((_at(b, x, 0) - world) with { Y = 0 }).Length;
        (double B, double X, double Off) best = (lo, x0, double.MaxValue);
        for (double b = lo; b <= hi + 1e-9; b += 0.5)
            for (double x = x0; x <= x1 + 1e-9; x += 0.5)
                if (Off(b, x) is var o && o < best.Off)
                    best = (b, x, o);
        var (cb, cx) = (best.B, best.X);
        for (double b = cb - 0.5; b <= cb + 0.5 + 1e-9; b += 0.05)
            for (double x = cx - 0.5; x <= cx + 0.5 + 1e-9; x += 0.05)
                if (b >= lo && b <= hi && x >= x0 && x <= x1 && Off(b, x) is var o && o < best.Off)
                    best = (b, x, o);
        return best;
    }

    /// <summary>The lowest the hook goes over what's under it now (with a casting on it, the casting's base just above).</summary>
    public double Lowest(TrainOnLine train)
    {
        var (_, floor) = Under(train);
        return floor - _at(Bridge, Trolley, 0).Y + (Hooked is null ? 0.3 : 2 * CastingHalf + 0.05);
    }

    /// <summary>The ground point under a casting on the hook.</summary>
    public Double3 HookedBase => HookAt - Double3.Up * (2 * CastingHalf);

    /// <summary>
    /// Fire, pressed: the hook lets go. Set down (its base near what's under it) over a cargo car with room, it's lashed and
    /// loaded; on the ground, it lies there to be rigged again. Let go of higher, it falls, and it's lost: the point it
    /// lands at is returned, for whoever's under it.
    /// </summary>
    public (Double3 Landed, bool Fell)? Release(TrainOnLine train)
    {
        if (Hooked is not { } c)
            return null;
        var (car, floor) = Under(train);
        // Hung over an open hatch it won't go in through, it's kept on the hook: set down there, it would stand in the
        // opening (T99).
        if (car >= 0 && Hatch(train, car, train.Frames[car].ToLocal(HookAt)) is { Fits: false })
            return null;
        var bottom = HookedBase;
        double above = bottom.Y - floor;
        var landed = bottom with { Y = floor };
        if (above > _t.DropAbove)
        {
            c.State = CastingState.Lost;
            return (landed, true);
        }
        if (car >= 0 && train.Vehicles[car] is { Kind: VehicleKind.Cargo } v && v.Load < 1)
        {
            v.Load = Math.Min(1, v.Load + _t.LoadPerCasting);
            c.State = CastingState.Loaded;
            c.Car = car;
            c.At = train.Frames[car].ToLocal(landed);
            return (landed, false);
        }
        c.State = CastingState.Stacked;
        c.At = car >= 0 ? landed : _at(Bridge, Trolley, 0);
        return (landed, false);
    }

    /// <summary>A casting a rigger at the hook could hook on: lying within reach of it, the hook low enough to reach.</summary>
    public Casting? Riggable(Double3 rigger)
    {
        var hook = HookAt;
        if (Hooked is not null || hook.Y - _at(Bridge, Trolley, 0).Y > _t.RigHeight)
            return null;
        if (((rigger - hook) with { Y = 0 }).Length > _t.RigReach)
            return null;
        return Castings.Where(c => c.State == CastingState.Stacked && ((c.At - hook) with { Y = 0 }).Length <= _t.RigReach)
            .OrderBy(c => ((c.At - hook) with { Y = 0 }).Length).FirstOrDefault();
    }

    /// <summary>
    /// One tick of rigging: someone on the ground holding Use at the hook beside a casting, for long enough, and it's hooked.
    /// </summary>
    public void Rig(int player, Double3 at, bool holding, double dt)
    {
        if (!holding || Riggable(at) is not { } c)
        {
            if (Rigger == player)
            {
                Rigger = -1;
                Rigging = 0;
            }
            return;
        }
        if (Rigger >= 0 && Rigger != player)
            return;
        Rigger = player;
        Rigging += dt / _t.RigSeconds;
        if (Rigging < 1)
            return;
        c.State = CastingState.Hooked;
        Rigger = -1;
        Rigging = 0;
    }

    /// <summary>The operator's Fire, as a press (held down it lets go once).</summary>
    internal bool Pressed(bool fire)
    {
        bool pressed = fire && !_releaseWas;
        _releaseWas = fire;
        return pressed;
    }

    /// <summary>Client side: adopts the host's crane (the Crane record).</summary>
    public void Mirror(double bridge, double trolley, double hook, double rigging, IReadOnlyList<(CastingState State, int Car, Double3 At)> castings)
    {
        Bridge = bridge;
        Trolley = trolley;
        Hook = hook;
        Rigging = rigging;
        for (int i = 0; i < Math.Min(castings.Count, Castings.Length); i++)
            (Castings[i].State, Castings[i].Car, Castings[i].At) = castings[i];
    }

    /// <summary>Where a casting is in the world (a lashed one rides its car).</summary>
    public Double3? Where(Casting c, IReadOnlyList<CarFrame> frames) => c.State switch
    {
        CastingState.Hooked => HookedBase,
        CastingState.Loaded when c.Car >= 0 && c.Car < frames.Count => frames[c.Car].ToWorld(c.At),
        CastingState.Stacked => c.At,
        _ => null,
    };
}
