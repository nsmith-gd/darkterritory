using Ballast;

namespace DarkTerritory.Sim.Train;

/// <summary>Mirror of content/tuning/wreck.json. Field docs live in that file.</summary>
/// <param name="RackSeconds">E.9 "the rack": on the empty engineering-kit rack, in the cab.</param>
/// <param name="PullBackSeconds">E.9 "the pull-back": up and back over the stopped consist to a high wide, the lamps going out from the last car forward, the engine last.</param>
/// <param name="HeightM">How high the wide ends, and <paramref name="BackM"/> how far out behind and beside the train's middle.</param>
/// <param name="SkipAfterSeconds">E.10's stranded skip delay: it can be skipped from here (<see cref="SkipTuning"/>).</param>
public sealed record StrandedOutroTuning(double RackSeconds = 1.5, double PullBackSeconds = 6, double HeightM = 42, double BackM = 55, double SkipAfterSeconds = 3)
{
    public double Seconds => RackSeconds + PullBackSeconds;
}

/// <summary>
/// GDD v1.4 App. E.5 "Skipping", after the director's answer to E.12 question 2 (note 315).
/// </summary>
/// <param name="Own">Each player skips the film (and the Stranded outro) on their own screen, whenever they want; false: the old majority-or-host vote.</param>
/// <param name="HoldSeconds">How long the key's held to skip your own.</param>
public sealed record SkipTuning(bool Own = true, double HoldSeconds = 0.5);

public sealed record WreckTuning
{
    public const string File = "tuning/wreck.json";
    /// <summary>GDD v1.4 App. E.9, the Stranded outro: on the empty rack, then the pull-back as the lamps go out.</summary>
    public StrandedOutroTuning Stranded { get; init; } = new();
    /// <summary>GDD v1.4 App. E.6, the derailment's opera: the draw's speed weighting and the fade at the end.</summary>
    public Music.MusicTuning Music { get; init; } = new();
    /// <summary>GDD v1.4 App. E: the derailment film's physics and shots.</summary>
    public FilmTuning Film { get; init; } = new();
    /// <summary>GDD v1.4 App. E.5 "Skipping": each player's own, or the crew's vote (note 315).</summary>
    public SkipTuning Skip { get; init; } = new();
    public int Substeps { get; init; } = 6;
    public double Gravity { get; init; } = 9.81;
    public double Restitution { get; init; } = 0.12;
    public double Friction { get; init; } = 0.55;
    public double CarRestitution { get; init; } = 0.08;
    public double CarFriction { get; init; } = 0.35;
    public double CouplingSlack { get; init; } = 0.4;
    public double CouplingBreak { get; init; } = 90;
    public double KickLateral { get; init; } = 0.22;
    public double KickYaw { get; init; } = 0.55;
    public double KickRoll { get; init; } = 0.9;
    /// <summary>Note 330 (#69): the first car's sideways throw is never under this (m/s), however slow it came off.</summary>
    public double KickMin { get; init; }
    /// <summary>... and, that slow, it's popped up this much (m/s) as its flange climbs the rail.</summary>
    public double KickUp { get; init; }
    /// <summary>The rest: each is thrown sideways at least this (m/s) as its wheels drop, ...</summary>
    public double JostleMin { get; init; }
    /// <summary>... hops up this much (m/s), and tips this much (rad/s) the way it's thrown.</summary>
    public double JostleUp { get; init; }
    public double JostleRoll { get; init; }
    /// <summary>
    /// Note 579 (the director, 9 Oct 2026: "cars basically being blasted off the tracks in a comedic way"): every car, the first
    /// included, is popped up about this (m/s) as its wheels come off, each by its own draw within <see cref="BlastSpread"/>
    /// of it, ...
    /// </summary>
    public double BlastUp { get; init; }
    public double BlastSpread { get; init; }
    /// <summary>... and set tumbling end over end and about its length up to this (rad/s), each its own way.</summary>
    public double BlastRoll { get; init; }
    public double Jostle { get; init; } = 0.06;
    public double JostleYaw { get; init; } = 0.35;
    public double DigIn { get; init; } = 2.0;
    public double DropInterval { get; init; } = 0.3;
    public double RollingFriction { get; init; } = 0.03;
    public double SlowSeconds { get; init; } = 2.5;
    public double SlowRate { get; init; } = 0.35;
    public double SettleSpeed { get; init; } = 0.25;
    public double SettleHold { get; init; } = 1.5;
    public double MaxSeconds { get; init; } = 30;
    public double CinematicSeconds { get; init; } = 12;
    /// <summary>T121 playtest ("let people experience it first hand, then replay the moment from the third person train view"): seconds in your own eyes, riding the wreck.</summary>
    public double FirstPersonSeconds { get; init; } = 4;
    /// <summary>
    /// GDD v1.4 App. D.12, E.5: how far into the first-person beat each crew member's derailment bookmark is taken (every
    /// client takes them all, from each one's eye in the car they rode). The build's per-player beat is that one.
    /// </summary>
    public double BookmarkSeconds { get; init; } = 2;
    /// <summary>Then the replay from the chase view, this long, starting this far before the train came off.</summary>
    public double ReplaySeconds { get; init; } = 9;
    public double ReplayLeadSeconds { get; init; } = 3;
    /// <summary>The whole derailment sequence: first person, the replay, then the orbit (<see cref="CinematicSeconds"/>).</summary>
    public double SequenceSeconds => FirstPersonSeconds + ReplaySeconds + CinematicSeconds;
}

/// <summary>
/// One car in a wreck: a rigid box (its shape's bounds) with mass, momentum and spin. The pose is kept as the car frame's
/// basis (right, up, back), so it draws and carries whoever's aboard the way a car on the rails does.
/// </summary>
public sealed class WreckBody
{
    public required int Vehicle { get; init; }
    /// <summary>The frame's origin: the floor of the car at its middle (rail height, on the rails).</summary>
    public Double3 Origin;
    public Double3 Right, Up, Back;
    public Double3 Velocity, Spin;
    public double Mass;
    /// <summary>Half the box: across, up (its full height, from the origin), along.</summary>
    public double HalfWidth, Height, HalfLength;
    /// <summary>Where it was a tick ago, for drawing between ticks.</summary>
    public Double3 PrevOrigin, PrevRight, PrevUp, PrevBack;
    /// <summary>Wreck seconds it stays on the rails yet: it rolls on, held to them, until its wheels drop.</summary>
    public double Railed;

    public Double3 Centre => Origin + Up * (Height / 2);
    public Double3 ToWorld(Double3 local) => Origin + Right * local.X + Up * local.Y + Back * local.Z;

    /// <summary>The inverse inertia applied to a world vector (a box's, about its centre).</summary>
    public Double3 InverseInertia(Double3 v)
    {
        double a = 2 * HalfWidth, b = Height, c = 2 * HalfLength;
        double ix = Mass / 12 * (b * b + c * c), iy = Mass / 12 * (a * a + c * c), iz = Mass / 12 * (a * a + b * b);
        double x = Double3.Dot(v, Right) / ix, y = Double3.Dot(v, Up) / iy, z = Double3.Dot(v, Back) / iz;
        return Right * x + Up * y + Back * z;
    }

    public Double3 PointVelocity(Double3 world) => Velocity + Double3.Cross(Spin, world - Centre);

    public void ApplyImpulse(Double3 impulse, Double3 at)
    {
        Velocity += impulse * (1 / Mass);
        Spin += InverseInertia(Double3.Cross(at - Centre, impulse));
    }
}

/// <summary>A big hit in the wreck this tick, for the sparks, the dust and the crash (presentation; host and clients alike).</summary>
public readonly record struct WreckImpact(Double3 At, double Speed, bool Ground);

/// <summary>
/// The derailment as physics (T117 playtest: "a full on dramatic cinematic derailment ... the full physics carnage of a
/// derailment simulation"). From the tick the train comes off, each car is a <see cref="WreckBody"/>: gravity, contact
/// with the ground under it (the line's terrain), friction, the couplings as slack chains that pull and snap, and the cars
/// shouldering into each other as capsules. The first car off is thrown outward off its curve; the rest follow on their own
/// momentum, so the train concertinas and jackknifes behind it. The host steps it; clients are sent the poses
/// (<see cref="Puppet"/>). Seeded, but no client re-simulates it, so it needn't be bit-exact anywhere.
/// </summary>
public sealed class Wreck
{
    readonly WreckTuning _t;
    readonly Func<double, double, double> _ground;
    readonly List<(int A, int B, bool Intact, double Length)> _links = [];
    readonly Pcg32 _rng;
    double _still;

    public IReadOnlyList<WreckBody> Bodies { get; }
    /// <summary>Seconds of wreck time since the train came off.</summary>
    public double Seconds { get; private set; }
    /// <summary>Real seconds since it came off (the slow beat counts at its slowed rate in <see cref="Seconds"/>).</summary>
    public double RealSeconds { get; private set; }
    /// <summary>Everything's come to rest (or it's run its time).</summary>
    public bool Settled { get; private set; }
    /// <summary>Couplings snapped so far.</summary>
    public int Snapped => _links.Count(l => !l.Intact);
    /// <summary>This tick's big hits.</summary>
    public List<WreckImpact> Impacts { get; } = [];

    Wreck(WreckTuning t, IReadOnlyList<WreckBody> bodies, Func<double, double, double> ground, ulong seed)
    {
        _t = t;
        Bodies = bodies;
        _ground = ground;
        _rng = new Pcg32(seed, 0x57EC);
        Seed = seed;
    }

    /// <summary>
    /// A wreck from a film's start (GDD v1.4 App. E.2): the bodies and couplings exactly as recorded at the derail tick,
    /// run at real time (the film does its own slow motion), so every machine that has the start shoots the same film.
    /// </summary>
    public static Wreck FromStart(WreckTuning t, IReadOnlyList<WreckBody> bodies, IEnumerable<(int A, int B, double Length)> links,
        Func<double, double, double> ground, ulong seed)
    {
        var w = new Wreck(t, bodies, ground, seed) { SlowMotion = false };
        foreach (var (a, b, length) in links)
            w._links.Add((a, b, true, length));
        return w;
    }

    /// <summary>Its couplings as made at the start, for a film's start (E.2).</summary>
    public IEnumerable<(int A, int B, double Length)> Links => _links.Select(l => (l.A, l.B, l.Length));

    /// <summary>The first seconds play slowed (the live wreck's beat); a film's wreck runs in real time.</summary>
    public bool SlowMotion { get; private init; } = true;

    /// <summary>The seed its jostles draw from.</summary>
    public ulong Seed { get; private init; }

    /// <summary>A wreck that's only drawn: the poses come from the host (<see cref="Pose"/>).</summary>
    public static Wreck PuppetOf(WreckTuning t, IReadOnlyList<WreckBody> bodies) => new(t, bodies, (_, _) => double.NegativeInfinity, 0) { Puppet = true };

    /// <summary>Drawn from the host's poses, not simulated here.</summary>
    public bool Puppet { get; private init; }

    /// <summary>
    /// The train coming off the rails now. <paramref name="ground"/> is the land's height at (x, z); <paramref name="first"/>
    /// the vehicle that goes first (the engine, as a rule); <paramref name="outward"/> which way it's thrown (+1 to its right).
    /// </summary>
    public static Wreck Begin(WreckTuning t, TrainOnLine train, Func<double, double, double> ground, ulong seed, int first = 0, int outward = 0)
    {
        var bodies = new List<WreckBody>();
        foreach (var f in train.Frames)
        {
            // A switchyard's cars standing on their siding (note 187) aren't the train: they stay where they stand.
            if (train.StandingCar(f.Index))
                continue;
            var v = train.Vehicles[f.Index];
            // Its rake's speed along its own length (the frame's may be a tick stale).
            double along = train.RakeOf(f.Index).Velocity;
            var b = new WreckBody
            {
                Vehicle = f.Index,
                Origin = f.Origin,
                Right = f.Right,
                Up = f.Up,
                Back = f.Back,
                Velocity = f.Back * -along,
                Mass = Math.Max(5, v.MassTonnes(train.Dynamics.Tuning)),
                HalfWidth = f.Shape.HalfWidth,
                Height = Math.Max(1.5, f.Shape.RoofHeight),
                HalfLength = f.Shape.HalfLength,
            };
            (b.PrevOrigin, b.PrevRight, b.PrevUp, b.PrevBack) = (b.Origin, b.Right, b.Up, b.Back);
            bodies.Add(b);
        }
        var w = new Wreck(t, bodies, ground, seed);
        foreach (var rake in train.Rakes)
        {
            if (train.Standing(rake))
                continue;
            var vs = rake.Consist.Vehicles;
            for (int i = 0; i + 1 < vs.Count; i++)
            {
                var a = bodies.First(b => b.Vehicle == vs[i].Id);
                var c = bodies.First(b => b.Vehicle == vs[i + 1].Id);
                w._links.Add((a.Vehicle, c.Vehicle, true, (Coupler(c, front: true) - Coupler(a, front: false)).Length));
            }
        }
        // The first off is thrown outward: sideways, slewing and rolling; the rest jostle as their wheels drop.
        int side = outward != 0 ? Math.Sign(outward) : w._rng.Chance(0.5) ? 1 : -1;
        foreach (var b in bodies)
        {
            double speed = b.Velocity.Length;
            if (b.Vehicle == first)
            {
                // A slow derail's share is nothing (note 330): thrown at kickMin instead, and popped up off the rail.
                bool slow = t.KickLateral * speed < t.KickMin;
                double up = Math.Max(slow ? t.KickUp : 0, w.Blast(out var tumble));
                b.Velocity += b.Right * (side * Math.Max(t.KickLateral * speed, t.KickMin)) + b.Up * up;
                // Rolling over the way it slides (about +Back a car's top swings to its left, so the roll is the other sign). Its
                // blast only rolls it the harder: tumbling end over end too, a slow one (note 330's 30 km/h) came down on its
                // wheels and slid on upright (the end-over-end spin taken by its ends in the ground before it went over).
                b.Spin += b.Up * (-side * t.KickYaw) + b.Back * (-side * (t.KickRoll + Math.Abs(tumble.Roll)));
            }
            else
            {
                // The rest come off one after another, down the train from the first: until then each rolls on, held to
                // the rails, and shoves into the cars ploughing ahead of it, which is what folds the train (it buckles).
                int index = train.Frames.ToList().FindIndex(f => f.Index == b.Vehicle);
                int firstIndex = train.Frames.ToList().FindIndex(f => f.Index == first);
                b.Railed = Math.Abs(index - firstIndex) * t.DropInterval;
            }
        }
        return w;
    }

    /// <summary>A host's pose for a body (clients).</summary>
    public void Pose(int vehicle, Double3 origin, Double3 right, Double3 up, Double3 back, Double3 velocity)
    {
        foreach (var b in Bodies)
            if (b.Vehicle == vehicle)
            {
                (b.PrevOrigin, b.PrevRight, b.PrevUp, b.PrevBack) = (b.Origin, b.Right, b.Up, b.Back);
                (b.Origin, b.Right, b.Up, b.Back, b.Velocity) = (origin, right, up, back, velocity);
            }
    }

    /// <summary>One sim tick of real time (slowed at first).</summary>
    public void Step(double dt)
    {
        Impacts.Clear();
        foreach (var b in Bodies)
            (b.PrevOrigin, b.PrevRight, b.PrevUp, b.PrevBack) = (b.Origin, b.Right, b.Up, b.Back);
        if (Settled)
            return;
        double rate = SlowMotion && RealSeconds < _t.SlowSeconds ? _t.SlowRate : 1;
        RealSeconds += dt;
        double h = dt * rate / _t.Substeps;
        for (int s = 0; s < _t.Substeps; s++)
            Substep(h);
        Seconds += dt * rate;
        bool still = Bodies.All(b => b.Velocity.Length < _t.SettleSpeed && b.Spin.Length < _t.SettleSpeed);
        _still = still ? _still + dt : 0;
        if (_still >= _t.SettleHold || Seconds >= _t.MaxSeconds)
        {
            Settled = true;
            foreach (var b in Bodies)
                (b.Velocity, b.Spin) = (Double3.Zero, Double3.Zero);
        }
    }

    void Substep(double h)
    {
        foreach (var b in Bodies)
            if (b.Railed > 0 && (b.Railed -= h) <= 0)
                Drop(b);
        foreach (var b in Bodies)
            b.Velocity -= Double3.Up * (_t.Gravity * h);
        Couplings();
        Cars();
        foreach (var b in Bodies)
            Ground(b);
        foreach (var b in Bodies)
            Integrate(b, h);
    }

    /// <summary>A car's wheels come off: it slews and slides a little sideways as they drop.</summary>
    void Drop(WreckBody b)
    {
        double speed = b.Velocity.Length;
        // Note 330 (#69): however slow, it's thrown, not set down: at least jostleMin sideways, a hop, and a tip that way.
        double draw = _rng.NextDouble() * 2 - 1;
        double sideways = draw * _t.Jostle * speed;
        int way = draw < 0 ? -1 : 1;
        if (Math.Abs(sideways) < _t.JostleMin)
            sideways = way * _t.JostleMin;
        double up = Math.Max(_t.JostleUp, Blast(out var tumble));
        b.Velocity += b.Right * sideways + b.Up * up;
        b.Spin += b.Up * ((_rng.NextDouble() * 2 - 1) * _t.JostleYaw) + b.Back * (-way * (_t.JostleRoll + Math.Abs(tumble.Roll))) + b.Right * tumble.Pitch;
    }

    /// <summary>
    /// A car's blast off the rails (note 579): how hard it's popped up (m/s), and its tumble, end over end and about its
    /// length (rad/s), each drawn for it. Nothing drawn with no blast, so a wreck without one is the wreck it was.
    /// </summary>
    double Blast(out (double Pitch, double Roll) tumble)
    {
        tumble = default;
        if (_t.BlastUp <= 0 && _t.BlastRoll <= 0)
            return 0;
        double up = _t.BlastUp * (1 + (_rng.NextDouble() * 2 - 1) * _t.BlastSpread);
        tumble = ((_rng.NextDouble() * 2 - 1) * _t.BlastRoll, (_rng.NextDouble() * 2 - 1) * _t.BlastRoll);
        return up;
    }

    /// <summary>The points a box meets the ground by: its bottom and top edges, every quarter of its length.</summary>
    static IEnumerable<Double3> Hull(WreckBody b)
    {
        for (int k = 0; k <= 4; k++)
        {
            double z = -b.HalfLength + k * b.HalfLength / 2;
            for (int side = -1; side <= 1; side += 2)
            {
                yield return new Double3(side * b.HalfWidth, 0, z);
                yield return new Double3(side * b.HalfWidth, b.Height, z);
            }
        }
    }

    void Ground(WreckBody b)
    {
        double deepest = 0;
        Double3 normal = Double3.Up;
        foreach (var local in Hull(b))
        {
            var p = b.ToWorld(local);
            double g = _ground(p.X, p.Z);
            double pen = g - p.Y;
            if (pen <= 0)
                continue;
            // The slope under it, by a finite difference: a bank throws a car down it.
            var n = new Double3(_ground(p.X - 0.5, p.Z) - _ground(p.X + 0.5, p.Z), 1, _ground(p.X, p.Z - 0.5) - _ground(p.X, p.Z + 0.5)).Normalized;
            var r = p - b.Centre;
            var vRel = b.PointVelocity(p);
            double vn = Double3.Dot(vRel, n);
            if (vn < 0)
            {
                double k = 1 / b.Mass + Double3.Dot(n, Double3.Cross(b.InverseInertia(Double3.Cross(r, n)), r));
                double jn = -(1 + _t.Restitution) * vn / k;
                b.ApplyImpulse(n * jn, p);
                if (-vn > 4)
                    Impacts.Add(new WreckImpact(p, -vn, true));
                // Friction: the slide along the ground, up to mu of the push. Upright, a car dragged sideways digs its bogies
                // into the ballast and trips over them (it rolls), so across its length it's held harder than along it.
                var vt = b.PointVelocity(p);
                vt -= n * Double3.Dot(vt, n);
                double speed = vt.Length;
                if (speed > 1e-6)
                {
                    double upright = Math.Max(0, Double3.Dot(b.Up, n));
                    var across = b.Right - n * Double3.Dot(b.Right, n);
                    across = across.Length > 1e-6 ? across.Normalized : Double3.Zero;
                    double vAcross = Double3.Dot(vt, across);
                    var along = vt - across * vAcross;
                    // Still on the rails: rolling along them, held across them.
                    bool railed = b.Railed > 0;
                    Slide(b, p, r, across, vAcross, (railed ? _t.DigIn : _t.Friction + (_t.DigIn - _t.Friction) * upright) * jn);
                    double vAlong = along.Length;
                    if (vAlong > 1e-6)
                        Slide(b, p, r, along * (1 / vAlong), vAlong, (railed ? _t.RollingFriction : _t.Friction) * jn);
                    if (speed > 3 && _rng.Chance(0.02))
                        Impacts.Add(new WreckImpact(p, speed, true));
                }
            }
            if (pen > deepest)
                (deepest, normal) = (pen, n);
        }
        // Out of the ground: the deepest point back to its surface, a share at a time (steady, not a jump).
        if (deepest > 0)
            b.Origin += normal * (deepest * 0.5);
    }

    /// <summary>Friction along one direction at a contact: stop the slide, or take <paramref name="most"/> off it.</summary>
    static void Slide(WreckBody b, Double3 p, Double3 r, Double3 dir, double v, double most)
    {
        if (Math.Abs(v) < 1e-6 || dir == Double3.Zero)
            return;
        double k = 1 / b.Mass + Double3.Dot(dir, Double3.Cross(b.InverseInertia(Double3.Cross(r, dir)), r));
        double j = Math.Min(Math.Abs(v) / k, most);
        b.ApplyImpulse(dir * (-Math.Sign(v) * j), p);
    }

    void Couplings()
    {
        for (int i = 0; i < _links.Count; i++)
        {
            var (a, c, intact, rest) = _links[i];
            if (!intact)
                continue;
            var A = Bodies.First(x => x.Vehicle == a);
            var B = Bodies.First(x => x.Vehicle == c);
            var pa = Coupler(A, front: false);
            var pb = Coupler(B, front: true);
            var d = pb - pa;
            double len = d.Length;
            double slack = rest + _t.CouplingSlack;
            if (len <= slack || len < 1e-9)
                continue;
            var n = d * (1 / len);
            double vSep = Double3.Dot(B.PointVelocity(pb) - A.PointVelocity(pa), n);
            double bias = (len - slack) * 0.2 * _t.Substeps * 30;
            double ka = 1 / A.Mass + Double3.Dot(n, Double3.Cross(A.InverseInertia(Double3.Cross(pa - A.Centre, n)), pa - A.Centre));
            double kb = 1 / B.Mass + Double3.Dot(n, Double3.Cross(B.InverseInertia(Double3.Cross(pb - B.Centre, n)), pb - B.Centre));
            double j = (vSep + bias) / (ka + kb);
            if (j <= 0)
                continue;
            if (j > _t.CouplingBreak)
            {
                _links[i] = (a, c, false, rest);
                Impacts.Add(new WreckImpact((pa + pb) * 0.5, j / Math.Max(A.Mass, B.Mass), false));
                continue;
            }
            A.ApplyImpulse(n * j, pa);
            B.ApplyImpulse(n * -j, pb);
        }
    }

    /// <summary>A car's coupler, its front (−Z, the way it runs) or its back, at buffer height.</summary>
    static Double3 Coupler(WreckBody b, bool front) => b.ToWorld(new Double3(0, 1.0, front ? -b.HalfLength : b.HalfLength));

    /// <summary>Car into car: each a capsule along its length; pushed apart where they overlap.</summary>
    void Cars()
    {
        for (int i = 0; i < Bodies.Count; i++)
            for (int k = i + 1; k < Bodies.Count; k++)
            {
                var A = Bodies[i];
                var B = Bodies[k];
                if ((A.Centre - B.Centre).Length > A.HalfLength + B.HalfLength + 3)
                    continue;
                double ra = Math.Max(A.HalfWidth, A.Height / 2) * 0.9, rb = Math.Max(B.HalfWidth, B.Height / 2) * 0.9;
                var (pa, pb) = Closest(A.Centre - A.Back * (A.HalfLength - ra), A.Centre + A.Back * (A.HalfLength - ra),
                    B.Centre - B.Back * (B.HalfLength - rb), B.Centre + B.Back * (B.HalfLength - rb));
                var d = pb - pa;
                double dist = d.Length;
                if (dist >= ra + rb || dist < 1e-9)
                    continue;
                var n = d * (1 / dist);
                var at = pa + n * ra;
                double vn = Double3.Dot(B.PointVelocity(at) - A.PointVelocity(at), n);
                double pen = ra + rb - dist;
                if (vn < 0)
                {
                    double ka = 1 / A.Mass + Double3.Dot(n, Double3.Cross(A.InverseInertia(Double3.Cross(at - A.Centre, n)), at - A.Centre));
                    double kb = 1 / B.Mass + Double3.Dot(n, Double3.Cross(B.InverseInertia(Double3.Cross(at - B.Centre, n)), at - B.Centre));
                    double j = -(1 + _t.CarRestitution) * vn / (ka + kb);
                    A.ApplyImpulse(n * -j, at);
                    B.ApplyImpulse(n * j, at);
                    if (-vn > 3)
                        Impacts.Add(new WreckImpact(at, -vn, false));
                    // Grinding along each other.
                    var vt = B.PointVelocity(at) - A.PointVelocity(at);
                    vt -= n * Double3.Dot(vt, n);
                    double speed = vt.Length;
                    if (speed > 1e-6)
                    {
                        var tdir = vt * (1 / speed);
                        double jt = Math.Min(speed / (ka + kb), _t.CarFriction * j);
                        A.ApplyImpulse(tdir * jt, at);
                        B.ApplyImpulse(tdir * -jt, at);
                    }
                }
                double total = A.Mass + B.Mass;
                A.Origin -= n * (pen * 0.5 * B.Mass / total);
                B.Origin += n * (pen * 0.5 * A.Mass / total);
            }
    }

    static (Double3, Double3) Closest(Double3 p1, Double3 q1, Double3 p2, Double3 q2)
    {
        var d1 = q1 - p1;
        var d2 = q2 - p2;
        var r = p1 - p2;
        double a = Double3.Dot(d1, d1), e = Double3.Dot(d2, d2), f = Double3.Dot(d2, r);
        double s, t;
        double c = Double3.Dot(d1, r), b = Double3.Dot(d1, d2);
        double denom = a * e - b * b;
        s = denom > 1e-12 ? Math.Clamp((b * f - c * e) / denom, 0, 1) : 0;
        t = e > 1e-12 ? (b * s + f) / e : 0;
        if (t < 0)
        {
            t = 0;
            s = a > 1e-12 ? Math.Clamp(-c / a, 0, 1) : 0;
        }
        else if (t > 1)
        {
            t = 1;
            s = a > 1e-12 ? Math.Clamp((b - c) / a, 0, 1) : 0;
        }
        return (p1 + d1 * s, p2 + d2 * t);
    }

    static void Integrate(WreckBody b, double h)
    {
        // Integrate about the centre: the origin is the floor, half the height under it.
        var centre = b.Centre + b.Velocity * h;
        double angle = b.Spin.Length * h;
        if (angle > 1e-9)
        {
            var axis = b.Spin.Normalized;
            b.Right = Rotate(b.Right, axis, angle);
            b.Up = Rotate(b.Up, axis, angle);
            b.Back = Rotate(b.Back, axis, angle);
            // Keep the basis square.
            b.Back = b.Back.Normalized;
            b.Right = Double3.Cross(b.Up, b.Back).Normalized;
            b.Up = Double3.Cross(b.Back, b.Right).Normalized;
        }
        b.Origin = centre - b.Up * (b.Height / 2);
        // A little air and ground drag on the spin, so a car lying still stops rocking.
        b.Spin *= 1 - 0.4 * h;
    }

    static Double3 Rotate(Double3 v, Double3 axis, double angle)
    {
        double c = DMath.Cos(angle), s = DMath.Sin(angle);
        return v * c + Double3.Cross(axis, v) * s + axis * (Double3.Dot(axis, v) * (1 - c));
    }
}
