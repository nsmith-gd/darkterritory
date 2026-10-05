using Ballast;
using Ballast.Physics;
using DarkTerritory.Sim.Physics;

namespace DarkTerritory.Sim.Train;

/// <summary>GDD v1.4 App. E.3 and E.10: the derailment film's physics and the shots (wreck.json "film").</summary>
/// <param name="Seconds">E.10 pre-sim length: the wreck run to rest, or this long, whichever's first.</param>
/// <param name="Fling">E.3 comic fling: each player's velocity at the derail tick times this.</param>
/// <param name="KickMin">... plus an upward kick between this and <paramref name="KickMax"/> (m/s), from the run seed.</param>
/// <param name="Spin">... and a tumble of up to this (rad/s).</param>
/// <param name="EjectCap">... capped at this speed (m/s), so bodies stay in frame and in the sim.</param>
/// <param name="MinKickSpeed">Below this derail speed (m/s), a slow tip-over still launches somebody: the kick's given in full regardless, sideways too.</param>
/// <param name="Bystander">A player further than this (m) from every car gets no impulse: they turn to watch, then go limp.</param>
/// <param name="BaseSlow">E.5's base slow motion, and <paramref name="PeakSlow"/> at a player's peak.</param>
/// <param name="FreezeSeconds">E.5's beat lengths (real seconds): the held frame, the establishing wide, each player, their cap, the settle, the cause card.</param>
/// <param name="ShotCap">The player-shot block's cap; past it each player's shot shrinks to fit (never under <paramref name="ShotMin"/>).</param>
public sealed record FilmTuning(
    double Seconds = 6, int Substeps = 4, double Fling = 1.3, double KickMin = 2, double KickMax = 4, double Spin = 4, double EjectCap = 30,
    double MinKickSpeed = 6, double Bystander = 25, double BaseSlow = 0.25, double PeakSlow = 0.1,
    double FreezeSeconds = 0.4, double EstablishingSeconds = 3, double ShotSeconds = 2.5, double ShotMin = 2, double ShotCap = 16,
    double SettleSeconds = 3, double CauseSeconds = 2, double CameraRadius = 0.3, double MinDistance = 2.5, double LensClearance = 1.5,
    double SimRadius = 300);

/// <summary>A car at the derail tick, exactly (E.2's snapshot).</summary>
/// <param name="Floor">Its floor over the box's foot (the rails), in its own frame: a crewmate inside stands on it, not on the
/// track under the car (note 232: they fell through to the rails and lay under the cut-away car).</param>
public sealed record FilmCar(int Vehicle, Double3 Origin, Double3 Right, Double3 Up, Double3 Back, Double3 Velocity, Double3 Spin,
    double Mass, double HalfWidth, double Height, double HalfLength, double Railed, double Floor = 0);

/// <summary>A crewmate at the derail tick: where, how fast, which car they were in (−1 out of one), and what they were doing.</summary>
/// <param name="Role">For the name card: "on the throttle", "at the firebox", "on the roof".</param>
public sealed record FilmPlayer(int Id, string Name, string Role, Double3 Position, Double3 Velocity, double Yaw, int Inside);

/// <summary>
/// Everything the film is shot from (E.2 step 2): the cars, the couplings, the crew, the seed, and the cause card's words.
/// Small, and exact over JSON, so the host sends it and every client shoots the same film from it (ARCHITECTURE note 165).
/// </summary>
public sealed record FilmLink(int A, int B, double Length);

/// <param name="Along">Where on the line it came off: every machine looks the ground up from here, so it finds the same heights.</param>
public sealed record FilmStart(ulong Seed, IReadOnlyList<FilmCar> Cars, IReadOnlyList<FilmLink> Links,
    IReadOnlyList<FilmPlayer> Players, string Cause, double Speed, double Along = 0);

/// <summary>One recorded instant: every car's pose, every ragdoll's 11 joints, and the hits that made a sound.</summary>
public sealed record FilmFrame(IReadOnlyList<(Double3 Origin, Double3 Right, Double3 Up, Double3 Back)> Cars, IReadOnlyList<Double3[]> Ragdolls,
    IReadOnlyList<WreckImpact> Impacts);

public enum ShotKind : byte { Freeze, Establishing, Player, Settle, Cause }

/// <summary>
/// One shot of the edited film (E.5): its real length, the stretch of recorded time it shows (it may revisit time another
/// shot showed), the slow motion it eases through, and the camera, held or dollying slowly (E.4 O13).
/// </summary>
public sealed record FilmShot(ShotKind Kind, int Subject, double Real, double From, double To, double SlowAtPeak, Double3 Camera, Double3 CameraTo,
    Double3 Look, Double3 LookTo, double Fov, string Card)
{
    /// <summary>Recorded time at <paramref name="real"/> seconds into the shot: easing from the base speed to the peak's and back.</summary>
    public double At(double real)
    {
        if (Real <= 0 || To <= From)
            return From;
        double u = Math.Clamp(real / Real, 0, 1);
        // The slow motion is a bell over the shot (slowest in the middle); time is its integral, normalised to the stretch.
        return From + (To - From) * Eased(u, SlowAtPeak) / Eased(1, SlowAtPeak);
    }

    static double Eased(double u, double peakShare)
    {
        // ∫ (1 - (1 - peakShare) sin²(πu)) du, in closed form with DMath so every client cuts the same frames.
        double k = 1 - peakShare;
        return u - k * (u / 2 - DMath.Sin(2 * DMath.PI * u) / (4 * DMath.PI));
    }
}

/// <summary>
/// The derailment as a film (GDD v1.4 App. E): the wreck pre-simulated to rest from its start, the crew ragdolled and flung
/// by it, recorded at 30 Hz (E.2 step 3), then cut into shots by a director that can see the future (E.4, E.5). The game is
/// the witness: everybody gets a shot, the biggest flight last, then the cause.
/// </summary>
public sealed class WreckFilm
{
    public const int Rate = 30;
    const double Dt = 1.0 / Rate;

    public FilmStart Start { get; }
    public IReadOnlyList<FilmFrame> Frames { get; }
    public IReadOnlyList<FilmShot> Shots { get; }
    /// <summary>Each player's peak (recorded seconds) and its score, for the order and for their auto-bookmark (D.12).</summary>
    public IReadOnlyDictionary<int, (double At, double Score)> Peaks { get; }
    /// <summary>The whole film's real length.</summary>
    public double Length => Shots.Sum(s => s.Real);

    /// <summary>
    /// The film as played after the first person and the replay (T121 playtest; ARCHITECTURE note 177): from the first
    /// player's shot on. E.5's freeze and establishing wide are what those two beats already show.
    /// </summary>
    public IReadOnlyList<FilmShot> Cut => [.. Shots.SkipWhile(s => s.Kind is ShotKind.Freeze or ShotKind.Establishing)];

    public double CutLength => Cut.Sum(s => s.Real);

    /// <summary>Real seconds into the cut where the cause card begins (a skip lands here).</summary>
    public double CauseAt => Cut.TakeWhile(s => s.Kind != ShotKind.Cause).Sum(s => s.Real);

    /// <summary>Real seconds into the cut where the first player's shot ends: from here anyone can vote to skip (E.5).</summary>
    public double SkippableFrom => Cut.FirstOrDefault() is { Kind: ShotKind.Player } first ? first.Real : 0;

    /// <summary>Which shot of the cut plays at <paramref name="real"/> seconds into it, and how far in; null past its end.</summary>
    public (FilmShot Shot, double Into)? CutAt(double real)
    {
        foreach (var s in Cut)
        {
            if (real < s.Real)
                return (s, real);
            real -= s.Real;
        }
        return null;
    }
    public double Recorded => (Frames.Count - 1) * Dt;

    WreckFilm(FilmStart start, List<FilmFrame> frames, List<FilmShot> shots, Dictionary<int, (double, double)> peaks)
    {
        Start = start;
        Frames = frames;
        Shots = shots;
        Peaks = peaks;
    }

    /// <summary>The start, from the wreck just begun and the crew as they were at the derail tick (host).</summary>
    /// <param name="floor">Each vehicle's floor over its box's foot (<see cref="FilmCar.Floor"/>); none, the foot.</param>
    public static FilmStart StartOf(Wreck wreck, IEnumerable<FilmPlayer> crew, string cause, double speed, double along = 0, Func<int, double>? floor = null) =>
        new(wreck.Seed, [.. wreck.Bodies.Select(b => new FilmCar(b.Vehicle, b.Origin, b.Right, b.Up, b.Back, b.Velocity, b.Spin,
                b.Mass, b.HalfWidth, b.Height, b.HalfLength, b.Railed, floor?.Invoke(b.Vehicle) ?? 0))],
            [.. wreck.Links.Select(l => new FilmLink(l.A, l.B, l.Length))], [.. crew], cause, speed, along);

    /// <summary>Shoots and edits the film: the same on every machine with the same start and ground.</summary>
    public static WreckFilm Shoot(WreckTuning wt, FilmStart start, Func<double, double, double> ground)
    {
        var t = wt.Film;
        var cars = start.Cars.Select(c => new WreckBody
        {
            Vehicle = c.Vehicle,
            Origin = c.Origin,
            Right = c.Right,
            Up = c.Up,
            Back = c.Back,
            Velocity = c.Velocity,
            Spin = c.Spin,
            Mass = c.Mass,
            HalfWidth = c.HalfWidth,
            Height = c.Height,
            HalfLength = c.HalfLength,
            Railed = c.Railed,
            PrevOrigin = c.Origin,
            PrevRight = c.Right,
            PrevUp = c.Up,
            PrevBack = c.Back,
        }).ToList();
        var wreck = Wreck.FromStart(wt with { Substeps = t.Substeps }, cars, start.Links.Select(l => (l.A, l.B, l.Length)), ground, start.Seed);
        var rng = new Pcg32(start.Seed, 0xF11);
        var engine = cars.Count > 0 ? cars[0].Centre : Double3.Zero;
        var dolls = start.Players.Select(p => Ragdoll(p, cars, rng, t, start.Speed)).ToList();
        var frames = new List<FilmFrame> { Frame(cars, dolls, []) };
        double still = 0;
        for (int i = 1; i <= t.Seconds * Rate; i++)
        {
            wreck.Step(Dt);
            var impacts = wreck.Impacts.ToList();
            double h = Dt / t.Substeps;
            for (int s = 0; s < t.Substeps; s++)
            {
                foreach (var d in dolls)
                {
                    if (d.Hold > 0)
                    {
                        d.Hold -= h; // a bystander turning to watch: stood still a moment, then limp
                        continue;
                    }
                    var home = d.Inside >= 0 ? cars.FirstOrDefault(c => c.Vehicle == d.Inside) : null;
                    double floor = home is null ? 0 : start.Cars.FirstOrDefault(c => c.Vehicle == home.Vehicle)?.Floor ?? 0;
                    d.Body.Step(h, new Double3(0, -wt.Gravity, 0), (p, r) => Touch(p, r, cars, home, floor, ground));
                }
                Pile(dolls);
            }
            // Out of the sim's radius (E.3): frozen where it got to.
            foreach (var d in dolls)
                if ((d.Body.Centre - engine).Length > t.SimRadius)
                    d.Body.Sleep();
            frames.Add(Frame(cars, dolls, impacts));
            bool resting = wreck.Settled && dolls.All(d => d.Body.Asleep || d.Body.Particles.All(p => (p.Position - p.Previous).Length < 0.05 * h));
            still = resting ? still + Dt : 0;
            if (still >= 0.5)
                break;
        }
        var peaks = start.Players.Select((p, i) => (p.Id, Peak(frames, start.Cars, i, ground))).ToDictionary(x => x.Id, x => x.Item2);
        var shots = Director.Plan(t, start, frames, peaks, ground);
        return new WreckFilm(start, frames, shots, peaks);
    }

    sealed class Doll(PbdBody body, int inside, double hold)
    {
        public PbdBody Body { get; } = body;
        public int Inside { get; } = inside;
        public double Hold { get; set; } = hold;
    }

    /// <summary>
    /// E.3: each living player becomes their ragdoll (the 11 joints of D.9), launched with their velocity times the fling, an
    /// upward kick and a tumble from the seed, capped; a bystander far from every car gets nothing, and a beat to watch.
    /// </summary>
    static Doll Ragdoll(FilmPlayer p, List<WreckBody> cars, Pcg32 rng, FilmTuning t, double speed)
    {
        double c = DMath.Cos(p.Yaw), s = DMath.Sin(p.Yaw);
        var particles = Bodies.Skeleton.Select(j => new Particle(p.Position + new Double3(j.At.X * c + j.At.Z * s, j.At.Y, -j.At.X * s + j.At.Z * c), 1, j.Radius)).ToArray();
        var bones = Bodies.Bones.Select(b => new DistanceConstraint(b.A, b.B, (Bodies.Skeleton[b.A].At - Bodies.Skeleton[b.B].At).Length, b.Stiffness)).ToArray();
        var body = new PbdBody(particles, bones) { Friction = 0.3, Bounce = 0.2, Iterations = 6 };
        bool bystander = cars.All(car => Outside(car, p.Position) > t.Bystander);
        double kick = rng.Range(t.KickMin, t.KickMax), spin = rng.Range(0, t.Spin);
        var axis = new Double3(rng.Range(-1, 1), rng.Range(-0.3, 0.3), rng.Range(-1, 1));
        axis = axis.Length > 1e-6 ? axis.Normalized : Double3.Up;
        var v = p.Velocity * t.Fling + Double3.Up * kick;
        if (speed < t.MinKickSpeed)
        {
            // A slow tip-over still throws someone: sideways, off the way the train was going.
            var side = Double3.Cross(Double3.Up, p.Velocity.Length > 0.1 ? p.Velocity.Normalized : new Double3(c, 0, -s));
            v += (side.Length > 1e-6 ? side.Normalized : Double3.Zero) * (rng.Chance(0.5) ? 1 : -1) * (t.MinKickSpeed - speed);
        }
        if (v.Length > t.EjectCap)
            v = v.Normalized * t.EjectCap;
        var centre = body.Centre;
        for (int i = 0; i < particles.Length; i++)
        {
            var tumble = Double3.Cross(axis * spin, particles[i].Position - centre);
            particles[i].SetVelocity(bystander ? Double3.Zero : v + tumble, Dt / t.Substeps);
        }
        return new Doll(body, p.Inside, bystander ? 0.8 : 0);
    }

    /// <summary>How far a point is outside a car's box (0 inside it).</summary>
    static double Outside(WreckBody car, Double3 world)
    {
        var d = world - car.Origin;
        double x = Math.Abs(Double3.Dot(d, car.Right)) - car.HalfWidth, y = Double3.Dot(d, car.Up), z = Math.Abs(Double3.Dot(d, car.Back)) - car.HalfLength;
        double dy = y < 0 ? -y : y > car.Height ? y - car.Height : 0;
        return new Double3(Math.Max(0, x), dy, Math.Max(0, z)).Length;
    }

    /// <summary>
    /// E.3's colliders for one joint: the ground (and never more than 0.5 m under it), and the cars as hollow boxes, so a
    /// player inside tumbles around inside theirs and one outside is pushed off the rest (the deepest wins).
    /// </summary>
    static Contact? Touch(Double3 p, double r, List<WreckBody> cars, WreckBody? home, double floor, Func<double, double, double> ground)
    {
        Contact? best = null;
        double deepest = 0;
        double g = ground(p.X, p.Z);
        if (p.Y - r < g)
        {
            var n = new Double3(ground(p.X - 0.3, p.Z) - ground(p.X + 0.3, p.Z), 0.6, ground(p.X, p.Z - 0.3) - ground(p.X, p.Z + 0.3)).Normalized;
            (best, deepest) = (new Contact(p with { Y = g + r }, n), g + r - p.Y);
        }
        foreach (var car in cars)
        {
            var d = p - car.Origin;
            var local = new Double3(Double3.Dot(d, car.Right), Double3.Dot(d, car.Up), Double3.Dot(d, car.Back));
            Contact? hit;
            if (car == home)
            {
                // Inside its walls: kept in (doorways and windows aren't gaps yet).
                double hw = car.HalfWidth - 0.05, hl = car.HalfLength - 0.05, top = car.Height - 0.05;
                var kept = new Double3(Math.Clamp(local.X, -hw + r, hw - r), Math.Clamp(local.Y, floor + 0.05 + r, top - r), Math.Clamp(local.Z, -hl + r, hl - r));
                if ((kept - local).Length < 1e-9)
                    continue;
                var push = kept - local;
                hit = new Contact(kept, push.Normalized);
            }
            else
            {
                if (Math.Abs(local.X) > car.HalfWidth + r || Math.Abs(local.Z) > car.HalfLength + r || local.Y < -r || local.Y > car.Height + r)
                    continue;
                hit = Collide.SphereBox(local, r, new Double3(-car.HalfWidth, 0, -car.HalfLength), new Double3(car.HalfWidth, car.Height, car.HalfLength));
            }
            if (hit is not { } h)
                continue;
            double depth = (h.Position - local).Length;
            if (depth <= deepest)
                continue;
            var world = car.ToWorld(h.Position);
            var normal = car.Right * h.Normal.X + car.Up * h.Normal.Y + car.Back * h.Normal.Z;
            (best, deepest) = (new Contact(world, normal), depth);
        }
        return best;
    }

    /// <summary>Bodies pile up (E.3): every joint against every other doll's, as 0.12 m spheres.</summary>
    static void Pile(List<Doll> dolls)
    {
        const double R = 0.12;
        for (int a = 0; a < dolls.Count; a++)
            for (int b = a + 1; b < dolls.Count; b++)
            {
                var pa = dolls[a].Body.Particles;
                var pb = dolls[b].Body.Particles;
                if ((dolls[a].Body.Centre - dolls[b].Body.Centre).Length > 3)
                    continue;
                for (int i = 0; i < pa.Length; i++)
                    for (int k = 0; k < pb.Length; k++)
                    {
                        var d = pb[k].Position - pa[i].Position;
                        double len = d.Length;
                        if (len >= 2 * R || len < 1e-9)
                            continue;
                        var push = d * ((2 * R - len) / len * 0.5);
                        pa[i].Position -= push;
                        pb[k].Position += push;
                    }
            }
    }

    static FilmFrame Frame(List<WreckBody> cars, List<Doll> dolls, List<WreckImpact> impacts) =>
        new([.. cars.Select(c => (c.Origin, c.Right, c.Up, c.Back))], [.. dolls.Select(d => d.Body.Particles.Select(p => p.Position).ToArray())], impacts);

    /// <summary>
    /// E.5: a player's peak moment: the highest apex, the longest airtime or the hardest landing, whichever scores highest.
    /// Returns when (recorded seconds) and the score. "In the air" is off whatever's under them (<see cref="Clearance"/>):
    /// stood up, or lying on a roof, isn't flying (note 232: the pelvis over the ground had a roof rider "airborne" all
    /// film and anyone stood up "in the air", so the peak was rarely a flight).
    /// </summary>
    static (double At, double Score) Peak(List<FilmFrame> frames, IReadOnlyList<FilmCar> cars, int doll, Func<double, double, double> ground)
    {
        double bestApex = 0, apexAt = 0, air = 0, bestAir = 0, airAt = 0, bestLanding = 0, landingAt = 0;
        Double3 last = frames[0].Ragdolls[doll][2];
        double lastV = 0;
        for (int f = 0; f < frames.Count; f++)
        {
            var pelvis = frames[f].Ragdolls[doll][2];
            double height = Clearance(frames[f], cars, doll, ground);
            if (height > bestApex)
                (bestApex, apexAt) = (height, f * Dt);
            air = height > AirborneAbove ? air + Dt : 0;
            if (air > bestAir)
                (bestAir, airAt) = (air, (f - air * Rate / 2) * Dt);
            double v = f == 0 ? 0 : (pelvis - last).Length / Dt;
            if (f > 1 && lastV - v > bestLanding)
                (bestLanding, landingAt) = (lastV - v, f * Dt);
            (last, lastV) = (pelvis, v);
        }
        // Each in metres-ish: a metre of clear air under them, a third of a second in the air, or 3 m/s lost on landing.
        (double At, double Score)[] candidates = [(apexAt, bestApex), (airAt, bestAir * 3), (landingAt, bestLanding / 3)];
        return candidates.MaxBy(c => c.Score);
    }

    /// <summary>A body this far off what's under it (m, its lowest joint's centre) is in the air.</summary>
    public const double AirborneAbove = 0.3;

    /// <summary>
    /// How far a ragdoll is off whatever's under it in a recorded frame: its lowest joint over the ground, or over the floor
    /// or roof of a car it's over or inside (in the car's own frame, so a car on its side has its side for a floor).
    /// </summary>
    public double Clearance(int frame, int doll, Func<double, double, double> ground) => Clearance(Frames[frame], Start.Cars, doll, ground);

    static double Clearance(FilmFrame frame, IReadOnlyList<FilmCar> cars, int doll, Func<double, double, double> ground)
    {
        double lowest = double.PositiveInfinity;
        foreach (var j in frame.Ragdolls[doll])
        {
            double clear = j.Y - ground(j.X, j.Z);
            for (int c = 0; c < cars.Count && c < frame.Cars.Count; c++)
            {
                var (o, right, up, back) = frame.Cars[c];
                var d = j - o;
                double x = Double3.Dot(d, right), y = Double3.Dot(d, up), z = Double3.Dot(d, back);
                if (Math.Abs(x) > cars[c].HalfWidth || Math.Abs(z) > cars[c].HalfLength || y < 0)
                    continue;
                clear = Math.Min(clear, y >= cars[c].Height ? y - cars[c].Height : y >= cars[c].Floor ? y - cars[c].Floor : y);
            }
            lowest = Math.Min(lowest, clear);
        }
        return lowest;
    }

    /// <summary>The recorded frame at <paramref name="seconds"/>, with the next and the share between (for playback).</summary>
    public (FilmFrame A, FilmFrame B, double T) At(double seconds)
    {
        double f = Math.Clamp(seconds * Rate, 0, Frames.Count - 1);
        int i = (int)Math.Floor(f);
        int j = Math.Min(i + 1, Frames.Count - 1);
        return (Frames[i], Frames[j], f - i);
    }

    /// <summary>Which shot plays at <paramref name="real"/> seconds into the film, and how far into it.</summary>
    public (FilmShot Shot, double Into)? ShotAt(double real)
    {
        foreach (var s in Shots)
        {
            if (real < s.Real)
                return (s, real);
            real -= s.Real;
        }
        return null;
    }

    /// <summary>
    /// E.4 and E.5, the director: the shot list (freeze, establishing wide, one shot per player in rising order of their peak,
    /// the settle, the cause card), and a camera for each from 24 candidates on a shell round the subject's peak, scored only
    /// if it passes the clearance rules against the whole recording.
    /// </summary>
    static class Director
    {
        public static List<FilmShot> Plan(FilmTuning t, FilmStart start, List<FilmFrame> frames, Dictionary<int, (double At, double Score)> peaks,
            Func<double, double, double> ground)
        {
            double length = (frames.Count - 1) * Dt;
            var shots = new List<FilmShot>();
            var (wideEye, wideLook) = Wide(frames[Math.Min(frames.Count - 1, Rate / 2)], ground);
            shots.Add(new FilmShot(ShotKind.Freeze, -1, t.FreezeSeconds, 0, 0, 1, wideEye, wideEye, wideLook, wideLook, 55, ""));
            double estTo = Math.Min(length, t.EstablishingSeconds * t.BaseSlow);
            shots.Add(new FilmShot(ShotKind.Establishing, -1, t.EstablishingSeconds, 0, estTo, 1, wideEye, wideEye + (wideLook - wideEye) * 0.08, wideLook, wideLook, 55, ""));
            var order = start.Players.Select((p, i) => (p, i)).OrderBy(x => peaks[x.p.Id].Score).ThenBy(x => x.p.Id).ToList();
            double each = order.Count == 0 ? 0 : Math.Max(t.ShotMin, Math.Min(t.ShotSeconds, t.ShotCap / order.Count));
            foreach (var (p, i) in order)
            {
                double peak = peaks[p.Id].At;
                // At the base speed easing to the peak's: the stretch of recorded time the shot covers, centred on the peak.
                double span = each * (t.BaseSlow + t.PeakSlow) / 2;
                double from = Math.Clamp(peak - span / 2, 0, Math.Max(0, length - span)), to = Math.Min(length, from + span);
                var (eye, look) = Camera(t, frames, i, from, to, ground);
                shots.Add(new FilmShot(ShotKind.Player, p.Id, each, from, to, t.PeakSlow / t.BaseSlow, eye, eye, look, look, 50,
                    $"{p.Name.ToUpperInvariant()} - {p.Role.ToUpperInvariant()}"));
            }
            var (endEye, endLook) = Wide(frames[^1], ground);
            double settleFrom = Math.Max(0, length - t.SettleSeconds * (t.BaseSlow + 1) / 2);
            shots.Add(new FilmShot(ShotKind.Settle, -1, t.SettleSeconds, settleFrom, length, 1, endEye, endEye + (endLook - endEye) * -0.1, endLook, endLook, 55, ""));
            shots.Add(new FilmShot(ShotKind.Cause, -1, t.CauseSeconds, length, length, 1, endEye, endEye, endLook, endLook, 55, start.Cause));
            return shots;
        }

        /// <summary>E.4 O14: a wide of every ragdoll and the engine, from high on the side away from the consist's middle.</summary>
        static (Double3 Eye, Double3 Look) Wide(FilmFrame frame, Func<double, double, double> ground)
        {
            var points = frame.Ragdolls.Select(r => r[2]).Append(frame.Cars[0].Origin).ToList();
            var centre = points.Aggregate(Double3.Zero, (a, p) => a + p) * (1.0 / points.Count);
            double extent = Math.Max(8, points.Max(p => (p - centre).Length));
            var train = frame.Cars.Aggregate(Double3.Zero, (a, c) => a + c.Origin) * (1.0 / frame.Cars.Count);
            var away = (centre - train) with { Y = 0 };
            var along = frame.Cars[0].Back with { Y = 0 };
            var side = Double3.Cross(Double3.Up, along.Length > 1e-6 ? along.Normalized : new Double3(0, 0, 1));
            var dir = away.Length > 1 ? away.Normalized : side;
            double d = Math.Min(150, extent * 2.2);
            var eye = centre + dir * d + Double3.Up * (d * 0.6);
            eye = eye with { Y = Math.Max(eye.Y, ground(eye.X, eye.Z) + 3) };
            return (eye, centre);
        }

        /// <summary>E.4 O1-O10 for one player's shot: the best clear candidate on the shell round their peak, or overhead.</summary>
        static (Double3 Eye, Double3 Look) Camera(FilmTuning t, List<FilmFrame> frames, int doll, double from, double to, Func<double, double, double> ground)
        {
            int f0 = (int)(from * Rate), f1 = Math.Min(frames.Count - 1, (int)Math.Ceiling(to * Rate));
            var mid = frames[(f0 + f1) / 2].Ragdolls[doll];
            var subject = (mid[0] + mid[1] + mid[2]) * (1.0 / 3);
            var vel = (frames[f1].Ragdolls[doll][2] - frames[f0].Ragdolls[doll][2]) * (1.0 / Math.Max(Dt, to - from));
            var train = frames[(f0 + f1) / 2].Cars.Aggregate(Double3.Zero, (a, c) => a + c.Origin) * (1.0 / frames[0].Cars.Count);
            var away = (subject - train) with { Y = 0 };
            away = away.Length > 1e-6 ? away.Normalized : new Double3(1, 0, 0);
            (Double3 Eye, double Score)? best = null;
            foreach (double r in new[] { 4.0, 7, 11 })
                foreach (double el in new[] { 10.0, 25, 45 })
                    for (int a = 0; a < 8; a++)
                    {
                        double az = a * DMath.PI / 4, e = el * DMath.PI / 180;
                        var dir = new Double3(DMath.Cos(az) * DMath.Cos(e), DMath.Sin(e), DMath.Sin(az) * DMath.Cos(e));
                        var eye = subject + dir * r;
                        if (!Clear(t, frames, doll, f0, f1, eye, ground))
                            continue;
                        var flat = (dir with { Y = 0 }).Normalized;
                        double across = vel.Length > 0.5 ? 1 - Math.Abs(Double3.Dot(vel.Normalized, dir * -1)) : 0.5;
                        double score = Double3.Dot(flat, away) * 1.0 + across * 0.8 - Math.Abs(r - 7) * 0.05 - Math.Abs(el - 25) * 0.005;
                        if (best is null || score > best.Value.Score)
                            best = (eye, score);
                    }
            // O10: nothing passed: from 70 degrees overhead at 12 m.
            var overhead = subject + new Double3(DMath.Cos(70 * DMath.PI / 180) * 12 * away.X, DMath.Sin(70 * DMath.PI / 180) * 12, DMath.Cos(70 * DMath.PI / 180) * 12 * away.Z);
            return (best?.Eye ?? overhead, subject);
        }

        /// <summary>
        /// A held camera's clearance over the shot (O1, O3, O4, O5): above the ground and out of every car by its radius, nothing
        /// within the lens clearance, the subject at least the minimum away and seen (two of head, chest and pelvis by
        /// rays the ground doesn't cut; cars fade, O2).
        /// </summary>
        static bool Clear(FilmTuning t, List<FilmFrame> frames, int doll, int f0, int f1, Double3 eye, Func<double, double, double> ground)
        {
            if (eye.Y < ground(eye.X, eye.Z) + 1)
                return false;
            for (int f = f0; f <= f1; f += 3)
            {
                var frame = frames[f];
                var joints = frame.Ragdolls[doll];
                if ((joints[1] - eye).Length < t.MinDistance)
                    return false;
                foreach (var (o, right, up, back) in frame.Cars)
                {
                    var d = eye - o;
                    double x = Math.Abs(Double3.Dot(d, right)), y = Double3.Dot(d, up), z = Math.Abs(Double3.Dot(d, back));
                    var outside = new Double3(Math.Max(0, x - 1.6), y < 0 ? -y : Math.Max(0, y - 4.2), Math.Max(0, z - 7.5));
                    if (outside.Length < t.LensClearance + t.CameraRadius)
                        return false;
                }
                for (int other = 0; other < frame.Ragdolls.Count; other++)
                    if (other != doll && (frame.Ragdolls[other][1] - eye).Length < t.LensClearance)
                        return false;
                int seen = 0;
                foreach (int j in new[] { 0, 1, 2 })
                    if (Sightline(eye, joints[j], ground))
                        seen++;
                if (seen < 2)
                    return false;
            }
            return true;
        }

        static bool Sightline(Double3 eye, Double3 at, Func<double, double, double> ground)
        {
            for (int k = 1; k < 12; k++)
            {
                var p = Double3.Lerp(eye, at, k / 12.0);
                if (p.Y < ground(p.X, p.Z) - 0.05)
                    return false;
            }
            return true;
        }
    }
}
