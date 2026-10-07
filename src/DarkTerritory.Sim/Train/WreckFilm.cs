using Ballast;
using Ballast.Physics;
using DarkTerritory.Sim.Physics;

namespace DarkTerritory.Sim.Train;

/// <summary>GDD v1.4 App. E.3 and E.10: the derailment film's physics and the shots (wreck.json "film").</summary>
/// <param name="Seconds">E.10 pre-sim length: the wreck run to rest, or this long, whichever's first.</param>
/// <param name="Fling">E.3 comic fling: each player's velocity at the derail tick times this.</param>
/// <param name="KickMin">... plus an upward kick between this and <paramref name="KickMax"/> (m/s), from the run seed.</param>
/// <param name="Spin">... and a tumble of up to this (rad/s), and at least <paramref name="SpinMin"/>, so a body in the air goes over.</param>
/// <param name="EjectCap">... capped at this speed (m/s), so bodies stay in frame and in the sim.</param>
/// <param name="MinKickSpeed">Below this derail speed (m/s), a slow tip-over still launches somebody: the kick's given in full regardless, sideways too.</param>
/// <param name="Bystander">A player further than this (m) from every car gets no impulse: they turn to watch, then go limp.</param>
/// <param name="BaseSlow">E.5's base slow motion, and <paramref name="PeakSlow"/> at a player's peak.</param>
/// <param name="FreezeSeconds">E.5's beat lengths (real seconds): the held frame, the establishing wide, each player, their cap, the settle, the cause card.</param>
/// <param name="ShotCap">The player-shot block's cap; past it each player's shot shrinks to fit (never under <paramref name="ShotMin"/>).</param>
/// <param name="SeatKick">The gunner in the gun's seat (T112) is thrown out of it: their upward kick is this (m/s), not the
/// seeded one, and their ragdoll starts <paramref name="SeatClear"/> m further back from the gun than the seat, clear of it.</param>
/// <param name="SurviveHits">Alive, a body takes this many hits (landings, <paramref name="ThudSpeed"/> and up) before one of
/// <paramref name="ImpactSpeed"/> kills it (the director, take 3); one of <paramref name="CertainDeathSpeed"/> kills at once,
/// as being crushed does; past <paramref name="GiveUpSeconds"/> of the recording, with its hits survived, any hit kills, and
/// after <paramref name="MostHits"/> survived, the next hit kills whatever it is.</param>
/// <param name="Muscle">Alive, the body holds a brace (arms up and out, knees bent) with this stiffness, and its hands reach
/// <paramref name="Reach"/> of the way each substep to where it's going; dead, it goes limp (the director, take 3).</param>
/// <param name="ImpactSpeed">A body's hit this hard (m/s of velocity taken off it inside one recorded frame by the ground
/// or a car) is the hit that kills it (the director's decision of 5 Oct 2026, App. E.2 step 1), as is being crushed.</param>
/// <param name="ThudSpeed">A hit at least this hard is a landing, recorded for its thud; <paramref name="ThudGap"/> s apart at
/// least, for each body.</param>
/// <param name="FirstPersonRate">The first-person beat plays the recording this slowly (recorded seconds a real second), from
/// the derail to <paramref name="FirstPersonAfter"/> real seconds past your own death, within
/// <paramref name="FirstPersonMin"/>-<paramref name="FirstPersonMax"/> s.</param>
/// <param name="TaskHold">A body goes into the wreck as it was at its work (<see cref="FilmTask"/>, App. F.2 take 4): its muscles
/// hold that pose this long (recorded s) before they go over to the brace.</param>
/// <param name="ShotFill">A player's shot frames them this tall (a share of the frame's height; E.4 O6 asks 25-60%), by its
/// field of view, between <paramref name="FovMin"/> and <paramref name="FovMax"/> degrees.</param>
public sealed record FilmTuning(
    double Seconds = 6, int Substeps = 4, double Fling = 1.3, double KickMin = 2, double KickMax = 4, double Spin = 4, double EjectCap = 30,
    double MinKickSpeed = 6, double Bystander = 25, double BaseSlow = 0.25, double PeakSlow = 0.1,
    double FreezeSeconds = 0.4, double EstablishingSeconds = 3, double ShotSeconds = 2.5, double ShotMin = 2, double ShotCap = 16,
    double SettleSeconds = 3, double CauseSeconds = 2, double CameraRadius = 0.3, double MinDistance = 2.5, double LensClearance = 1.5,
    double SimRadius = 300, double SeatKick = 6, double SeatClear = 0.25, double ImpactSpeed = 6, double ThudSpeed = 2, double ThudGap = 0.25,
    double SpinMin = 0, double SurviveHits = 2, double MostHits = 3, double CertainDeathSpeed = 16, double GiveUpSeconds = 2.4, double Muscle = 0.25, double Reach = 0.06,
    double FirstPersonRate = 0.4, double FirstPersonAfter = 0.8, double FirstPersonMin = 1.5, double FirstPersonMax = 6,
    double ShotFill = 0.45, double FovMin = 22, double FovMax = 60, double TaskHold = 0.4)
{
    /// <summary>
    /// Real seconds into a player's first-person beat that their death lands, for a death <paramref name="recorded"/> seconds
    /// into the recording: when the host kills them, and where their own eyes stop. A bystander's death at the settle is
    /// past the beat's cap, so they die as it ends.
    /// </summary>
    public double DeathDelay(double recorded) => Math.Min(recorded / FirstPersonRate, FirstPersonMax - FirstPersonAfter);

    /// <summary>The whole first-person beat for a death <paramref name="recorded"/> seconds in: up to it, and a little past.</summary>
    public double FirstPersonLength(double recorded) => Math.Clamp(DeathDelay(recorded) + FirstPersonAfter, FirstPersonMin, FirstPersonMax);
}

/// <summary>A car at the derail tick, exactly (E.2's snapshot).</summary>
/// <param name="Floor">Its floor over the box's foot (the rails), in its own frame: a crewmate inside stands on it, not on the
/// track under the car (note 251: they fell through to the rails and lay under the cut-away car).</param>
/// <param name="Doorways">Its doorways (its frame), gaps in its walls (E.3): someone inside can be thrown out through one.</param>
/// <param name="Gun">It has a mounted gun (T93, T112) with its pivot at <paramref name="GunAt"/> (its own frame), laid at
/// <paramref name="GunYaw"/> (the player's yaw convention: 0 along −Z): a solid in the film, so nobody goes through it.</param>
public sealed record FilmCar(int Vehicle, Double3 Origin, Double3 Right, Double3 Up, Double3 Back, Double3 Velocity, Double3 Spin,
    double Mass, double HalfWidth, double Height, double HalfLength, double Railed, double Floor = 0, bool Gun = false, Double3 GunAt = default,
    double GunYaw = 0, IReadOnlyList<Box>? Doorways = null);

/// <summary>A crewmate at the derail tick: where, how fast, which car they were in (−1 out of one), and what they were doing.</summary>
/// <param name="Role">For the name card: "on the throttle", "at the firebox", "on the roof".</param>
/// <param name="Seated">In the gun's seat (T112): thrown out of it (<see cref="FilmTuning.SeatKick"/>).</param>
/// <param name="Task">What they were at (App. F.2 take 4: "people mid-task"): their body starts the film in its pose.</param>
public sealed record FilmPlayer(int Id, string Name, string Role, Double3 Position, Double3 Velocity, double Yaw, int Inside, bool Seated = false,
    FilmTask Task = FilmTask.None);

/// <summary>
/// What a crewmate was doing as the train came off (App. F.2, take 4: the third-person poses read "arms-up and awkward, not
/// people mid-task"): each goes into the wreck from it, its body in that pose, held a moment (FilmTuning.TaskHold) before
/// the brace takes over: on the throttle, at the shovel, in the gun's seat, carrying something in both arms.
/// </summary>
public enum FilmTask : byte { None, Driving, Firing, Gunning, Carrying }

/// <summary>
/// Everything the film is shot from (E.2 step 2): the cars, the couplings, the crew, the seed, and the cause card's words.
/// Small, and exact over JSON, so the host sends it and every client shoots the same film from it (ARCHITECTURE note 165).
/// </summary>
public sealed record FilmLink(int A, int B, double Length);

/// <param name="Along">Where on the line it came off: every machine looks the ground up from here, so it finds the same heights.</param>
public sealed record FilmStart(ulong Seed, IReadOnlyList<FilmCar> Cars, IReadOnlyList<FilmLink> Links,
    IReadOnlyList<FilmPlayer> Players, string Cause, double Speed, double Along = 0);

/// <summary>One recorded instant: every car's pose, every ragdoll's 11 joints, the cars' hits and the bodies' landings that made a sound.</summary>
public sealed record FilmFrame(IReadOnlyList<(Double3 Origin, Double3 Right, Double3 Up, Double3 Back)> Cars, IReadOnlyList<Double3[]> Ragdolls,
    IReadOnlyList<WreckImpact> Impacts, IReadOnlyList<FilmLanding> Landings);

/// <summary>
/// A body hitting something in the recording (the ground, a car, or a car hitting it): which ragdoll, where (its middle),
/// how hard (m/s taken off it) and whether it was a car (a hollow thud off planks and steel) or the ground.
/// </summary>
public readonly record struct FilmLanding(int Doll, Double3 At, double Speed, bool Car);

/// <summary>How a body died in the film: the hit that killed it, crushed under a car, or limp at the settle (a bystander, or never hit to death).</summary>
public enum FilmDeathKind : byte { Impact, Crushed, Settle }

/// <summary>A crewmate's death in the film (App. E.2 step 1, the director's decision of 5 Oct 2026): when (recorded seconds), how, how hard.</summary>
/// <param name="Survived">The hits (landings) it took alive before the one that killed it.</param>
public sealed record FilmDeath(double At, FilmDeathKind Kind, double Speed, int Survived = 0);

/// <summary>The film's physics without its shots: the frames and each crewmate's death (the host reads the deaths on the derail tick).</summary>
public sealed record FilmRecording(IReadOnlyList<FilmFrame> Frames, IReadOnlyDictionary<int, FilmDeath> Deaths);

public enum ShotKind : byte { Freeze, Establishing, Player, Settle, Cause }

/// <summary>
/// One shot of the edited film (E.5): its real length, the stretch of recorded time it shows (it may revisit time another
/// shot showed), the slow motion it eases through, and the camera, held or dollying slowly (E.4 O13).
/// </summary>
/// <param name="Rig">A player's shot of someone inside a car: the car (its place in the film's cars) the camera is rigged to,
/// held at <paramref name="RigAt"/> in its frame, so the car carrying them doesn't run out of the shot; −1 for a camera held
/// in the world (<paramref name="Camera"/> is where the rig is at the shot's middle either way).</param>
public sealed record FilmShot(ShotKind Kind, int Subject, double Real, double From, double To, double SlowAtPeak, Double3 Camera, Double3 CameraTo,
    Double3 Look, Double3 LookTo, double Fov, string Card, int Rig = -1, Double3 RigAt = default)
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

    /// <summary>
    /// The real seconds into the shot at which it shows <paramref name="recorded"/> (the inverse of <see cref="At"/>, which
    /// only ever moves forward): where a player's peak falls in their shot. By halving, so every client finds the same.
    /// </summary>
    public double RealAt(double recorded)
    {
        if (Real <= 0 || To <= From || recorded <= From)
            return 0;
        if (recorded >= To)
            return Real;
        double lo = 0, hi = Real;
        for (int i = 0; i < 40; i++)
        {
            double mid = (lo + hi) / 2;
            if (At(mid) < recorded)
                lo = mid;
            else
                hi = mid;
        }
        return (lo + hi) / 2;
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
    /// <summary>
    /// Each player's peak: the moment of their death (recorded seconds; <see cref="Deaths"/>) and E.5's score of their
    /// flight (the highest apex, longest airtime or hardest landing), for the order, their shot and their auto-bookmark (D.12).
    /// </summary>
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

    /// <summary>
    /// GDD v1.4 App. E.5-E.6: real seconds into the cut where the last player's shot shows their peak, the biggest flight of
    /// the night (the shots go in ascending order of peak), which the music's hit lands on. Null with no player's shot.
    /// </summary>
    public double? FinalApexAt
    {
        get
        {
            double at = 0;
            double? apex = null;
            foreach (var s in Cut)
            {
                if (s.Kind == ShotKind.Player && Peaks.TryGetValue(s.Subject, out var peak))
                    apex = at + s.RealAt(peak.At);
                at += s.Real;
            }
            return apex;
        }
    }

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

    /// <summary>
    /// Each crewmate's death (App. E.2 step 1, the director's decision of 5 Oct 2026): their own body's first hard hit (the
    /// ground, a car, a car hitting them) or being crushed, read off the physics. The host kills them as it lands in their
    /// own first-person beat (<see cref="FilmTuning.DeathDelay"/>); every client's first person ends on its own.
    /// </summary>
    public IReadOnlyDictionary<int, FilmDeath> Deaths { get; }

    /// <summary>The film's numbers it was shot with.</summary>
    public FilmTuning Tuning { get; }

    /// <summary>Real seconds of <paramref name="player"/>'s own first-person beat (up to and through their death); null if they're not in the film.</summary>
    public double? FirstPersonOf(int player) => Deaths.TryGetValue(player, out var d) ? Tuning.FirstPersonLength(d.At) : null;

    WreckFilm(FilmTuning tuning, FilmStart start, IReadOnlyList<FilmFrame> frames, IReadOnlyDictionary<int, FilmDeath> deaths, List<FilmShot> shots,
        Dictionary<int, (double, double)> peaks)
    {
        Tuning = tuning;
        Start = start;
        Frames = frames;
        Deaths = deaths;
        Shots = shots;
        Peaks = peaks;
    }

    /// <summary>The start, from the wreck just begun and the crew as they were at the derail tick (host).</summary>
    /// <param name="floor">Each vehicle's floor over its box's foot (<see cref="FilmCar.Floor"/>); none, the foot.</param>
    /// <param name="gun">Each vehicle's mounted gun, its pivot (its own frame) and the yaw it's laid at; null for none.</param>
    /// <param name="doorways">Each vehicle's doorways (<see cref="FilmCar.Doorways"/>); none, no gaps.</param>
    public static FilmStart StartOf(Wreck wreck, IEnumerable<FilmPlayer> crew, string cause, double speed, double along = 0, Func<int, double>? floor = null,
        Func<int, (Double3 At, double Yaw)?>? gun = null, Func<int, IReadOnlyList<Box>>? doorways = null) =>
        new(wreck.Seed, [.. wreck.Bodies.Select(b => gun?.Invoke(b.Vehicle) is { } g
                ? Car(b, floor, doorways) with { Gun = true, GunAt = g.At, GunYaw = g.Yaw }
                : Car(b, floor, doorways))],
            [.. wreck.Links.Select(l => new FilmLink(l.A, l.B, l.Length))], [.. crew], cause, speed, along);

    static FilmCar Car(WreckBody b, Func<int, double>? floor, Func<int, IReadOnlyList<Box>>? doorways) => new(b.Vehicle, b.Origin, b.Right, b.Up, b.Back,
        b.Velocity, b.Spin, b.Mass, b.HalfWidth, b.Height, b.HalfLength, b.Railed, floor?.Invoke(b.Vehicle) ?? 0,
        Doorways: doorways?.Invoke(b.Vehicle) is { Count: > 0 } open ? open : null);

    /// <summary>
    /// Shoots and edits the film: the same on every machine with the same start and ground. <paramref name="recorded"/> is
    /// the recording already made from this start (the host's, from the derail tick), or null to make it.
    /// </summary>
    public static WreckFilm Shoot(WreckTuning wt, FilmStart start, Func<double, double, double> ground, FilmRecording? recorded = null)
    {
        var t = wt.Film;
        var (frames, deaths) = recorded ?? Record(wt, start, ground);
        // E.5's peak, scored as before (the order: the biggest flight last), but its moment is the hit that kills them (App.
        // E.2 step 1, the director's decision of 5 Oct 2026): the shot, the bookmark and the opera's hit go where they die.
        var peaks = start.Players.Select((p, i) => (p.Id, (deaths[p.Id].At, Peak(frames, start.Cars, i, ground).Score))).ToDictionary(x => x.Id, x => x.Item2);
        var shots = Director.Plan(t, start, frames, peaks, ground);
        return new WreckFilm(t, start, frames, deaths, shots, peaks);
    }

    /// <summary>
    /// A throwaway recording of one car and one body for a few frames, so the code's compiled before a host first needs it
    /// on a derail tick. Nothing is kept.
    /// </summary>
    public static void Warm(WreckTuning wt)
    {
        var car = new FilmCar(0, Double3.Zero, new Double3(1, 0, 0), Double3.Up, new Double3(0, 0, 1), new Double3(0, 0, -10), Double3.Zero,
            30, 1.4, 3.5, 7, 1, 1, Gun: true, GunAt: new Double3(0, 4.4, -2), Doorways: [new Box(new Double3(1.3, 1, -0.9), new Double3(1.5, 3, 0.9))]);
        var start = new FilmStart(1, [car], [], [new FilmPlayer(0, "", "", new Double3(0, 1, 0), new Double3(0, 0, -10), 0, 0)], "", 10);
        Record(wt with { Film = wt.Film with { Seconds = 0.2 } }, start, (_, _) => 0);
    }

    /// <summary>
    /// E.2 step 3: the wreck pre-simulated to rest from its start with the crew ragdolled in it, at 30 Hz, and when each of
    /// them dies in it. Deterministic (DMath, seeded): the host reads the deaths from it on the derail tick, and every
    /// client that shoots the film from the same start records the same frames.
    /// </summary>
    public static FilmRecording Record(WreckTuning wt, FilmStart start, Func<double, double, double> ground)
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
        var dolls = start.Players.Select(p => Ragdoll(p, cars, start.Cars, rng, t, start.Speed)).ToList();
        var stage = new Stage(cars, start.Cars, ground);
        var frames = new List<FilmFrame> { Frame(cars, dolls, [], []) };
        var deaths = new Dictionary<int, FilmDeath>();
        var gravity = new Double3(0, -wt.Gravity, 0);
        double still = 0;
        for (int i = 1; i <= t.Seconds * Rate; i++)
        {
            wreck.Step(Dt);
            var impacts = wreck.Impacts.ToList();
            double h = Dt / t.Substeps;
            for (int s = 0; s < t.Substeps; s++)
            {
                // The cars where they are this substep, between where the wreck had them a frame ago and now: a car
                // ploughing into somebody shoves them a substep at a time, and carries or throws them, rather than jumping
                // a whole frame through them.
                stage.At((s + 1.0) / t.Substeps, s / (double)t.Substeps);
                foreach (var d in dolls)
                {
                    if (d.Hold > 0)
                    {
                        d.Hold -= h; // a bystander turning to watch: stood still a moment, then limp
                        continue;
                    }
                    if (d.Body.Asleep)
                        continue;
                    var ps = d.Body.Particles;
                    if (d.Alive && !d.Bystander && d.TaskUntil <= 0)
                        Reach(d, t, h);
                    Span<Double3> before = [ps[0].Position - ps[0].Previous, ps[1].Position - ps[1].Previous, ps[2].Position - ps[2].Previous];
                    var middle = Middle(ps);
                    d.Body.Step(h, gravity, (p, r) => stage.Touch(p, r, d));
                    // What hit the head, chest and pelvis: their velocity changed by more than gravity on a substep they touched
                    // something (the ground, a car, a car hitting them). Not the muscles bracing them (take 3), nor other bodies.
                    for (int j = 0; j < 3; j++)
                        if (ps[j].Contact)
                            d.Hit[j] += (ps[j].Position - ps[j].Previous - before[j]) * (1 / h) - gravity * h;
                    // ... and what hit the whole of it (its middle's velocity: what its contacts did, exactly; bones and
                    // muscles pull equal and opposite), so a hit taken on braced hands and feet counts too.
                    d.Whole += (Middle(ps) - middle) * (1 / h) - gravity * h;
                }
                Pile(dolls);
            }
            double now = i * Dt;
            var landings = new List<FilmLanding>();
            for (int k = 0; k < dolls.Count; k++)
            {
                var d = dolls[k];
                double speed = Math.Max(d.Whole.Length, Math.Max(d.Hit[0].Length, Math.Max(d.Hit[1].Length, d.Hit[2].Length)));
                bool car = d.OnCar, crushed = d.Squeezed;
                (d.Hit[0], d.Hit[1], d.Hit[2], d.Whole, d.OnCar, d.Squeezed) = (Double3.Zero, Double3.Zero, Double3.Zero, Double3.Zero, false, false);
                if (d.Hold > 0)
                    continue;
                // thudGap apart, but a hit hard enough to kill always thuds.
                bool landed = speed >= t.ThudSpeed && (now - d.Landed >= t.ThudGap || speed >= t.ImpactSpeed);
                if (landed)
                {
                    landings.Add(new FilmLanding(k, d.Body.Centre, speed, car));
                    d.Landed = now;
                }
                int id = start.Players[k].Id;
                if (!d.Alive)
                    continue;
                if (d.TaskUntil > 0 && now >= d.TaskUntil)
                    ToBrace(d);
                // Alive till the hit that kills them (the director, take 3): they take their first hits, braced, and the
                // next hard one kills; a crushing, or a hit past all surviving, kills whenever it comes.
                bool hardened = d.Survived >= t.SurviveHits;
                bool fatal = crushed || speed >= t.CertainDeathSpeed
                    || landed && hardened && (speed >= t.ImpactSpeed || now >= t.GiveUpSeconds || d.Survived >= t.MostHits);
                if (fatal)
                {
                    deaths[id] = new FilmDeath(now, crushed && speed < t.ImpactSpeed ? FilmDeathKind.Crushed : FilmDeathKind.Impact, speed, d.Survived);
                    Die(d);
                }
                else if (landed)
                {
                    d.Survived++;
                    d.LastHit = (now, speed);
                }
            }
            // Out through a doorway (its middle out of its car's box): outside from now on.
            foreach (var d in dolls)
                if (d.Inside >= 0 && cars.FindIndex(c => c.Vehicle == d.Inside) is >= 0 and var home && Out(cars[home], d.Body.Particles[2].Position))
                    d.Inside = -1;
            // Out of the sim's radius (E.3): frozen where it got to.
            foreach (var d in dolls)
                if ((d.Body.Centre - engine).Length > t.SimRadius)
                    d.Body.Sleep();
            frames.Add(Frame(cars, dolls, impacts, landings));
            bool resting = wreck.Settled && dolls.All(d => d.Body.Asleep || d.Body.Particles.All(p => (p.Position - p.Previous).Length < 0.05 * h));
            still = resting ? still + Dt : 0;
            if (still >= 0.5)
                break;
        }
        // Nobody walks away. Never hit to death, they died in their last hit (they've lain still since: going limp there shows
        // nothing); never hit at all (a bystander, E.3), at the settle.
        double end = (frames.Count - 1) * Dt;
        for (int k = 0; k < dolls.Count; k++)
        {
            int id = start.Players[k].Id;
            if (deaths.ContainsKey(id))
                continue;
            var d = dolls[k];
            deaths[id] = d.Survived > 0 && !d.Bystander ? new FilmDeath(d.LastHit.At, FilmDeathKind.Impact, d.LastHit.Speed, d.Survived - 1)
                : new FilmDeath(end, FilmDeathKind.Settle, 0, d.Survived);
        }
        return new FilmRecording(frames, deaths);
    }

    sealed class Doll(PbdBody body, int inside, double hold)
    {
        public PbdBody Body { get; } = body;
        /// <summary>The car it's inside (−1 out of one): out through a doorway, it's outside, and that car is a solid to it.</summary>
        public int Inside { get; set; } = inside;
        public double Hold { get; set; } = hold;
        public bool Bystander { get; } = hold > 0;
        /// <summary>
        /// This frame: how much the head, chest and pelvis had their velocity changed by hitting something (the hardest of
        /// the three is the hit), whether a car was in it, and whether a car pressed it into the ground.
        /// </summary>
        public readonly Double3[] Hit = new Double3[3];
        public Double3 Whole;
        public bool OnCar, Squeezed;
        /// <summary>When its last landing was recorded, and its hardest hit (when, how hard).</summary>
        public double Landed = double.NegativeInfinity;
        /// <summary>Alive (braced, its muscles on) till the hit that kills it, and the hits it's taken so far.</summary>
        public bool Alive = true;
        public int Survived;
        public (double At, double Speed) LastHit;
        /// <summary>Where its muscles start in its constraints (the bones before them).</summary>
        public int Muscles;
        /// <summary>Holding its work's pose till then (recorded s); then its muscles go over to the brace.</summary>
        public double TaskUntil;
    }

    /// <summary>
    /// The brace a body holds while it's alive (the director, take 3): elbows and hands up and out in front, knees bent, in
    /// its own frame as <see cref="Bodies.Skeleton"/>'s joints. Its muscles are the distances of this pose between joints
    /// the bones don't join, pulled toward at <see cref="FilmTuning.Muscle"/>; which way round is the physics' to find, so it
    /// flails into it.
    /// </summary>
    static readonly Double3[] Brace =
    [
        new(0, 1.6, 0), new(0, 1.33, 0), new(0, 0.95, 0),
        new(-0.36, 1.22, -0.22), new(-0.34, 1.32, -0.5), new(0.36, 1.22, -0.22), new(0.34, 1.32, -0.5),
        new(-0.14, 0.55, -0.16), new(-0.16, 0.1, 0.02), new(0.14, 0.55, -0.16), new(0.16, 0.1, 0.02),
    ];

    static readonly (int A, int B)[] MusclePairs =
    [
        (4, 0), (6, 0), (4, 2), (6, 2), (4, 6), (3, 2), (5, 2), (3, 5), (8, 1), (10, 1), (7, 1), (9, 1), (8, 10),
    ];

    /// <summary>A body's middle's implicit velocity times the step (the mean of its joints': equal masses).</summary>
    static Double3 Middle(Particle[] ps)
    {
        var sum = Double3.Zero;
        foreach (var p in ps)
            sum += p.Position - p.Previous;
        return sum * (1.0 / ps.Length);
    }

    /// <summary>Its work's pose given up: the muscles pull to the brace from here (and the hands go out, Reach).</summary>
    static void ToBrace(Doll d)
    {
        d.TaskUntil = 0;
        var cs = d.Body.Constraints;
        for (int i = d.Muscles; i < cs.Length; i++)
            cs[i] = cs[i] with { Length = (Brace[cs[i].A] - Brace[cs[i].B]).Length };
    }

    /// <summary>
    /// The 11 joints (<see cref="Bodies.Skeleton"/>'s order: head, chest, pelvis, left elbow and hand, right elbow and hand,
    /// left knee and foot, right knee and foot) of a body at its work, in its own frame (−Z ahead, feet at 0): laid bone by
    /// bone from the pelvis along each bone's way, at the skeleton's own bone lengths, so the bones hold it as it is.
    /// </summary>
    public static Double3[] TaskPose(FilmTask task)
    {
        if (task == FilmTask.None)
            return [.. Bodies.Skeleton.Select(j => j.At)];
        // Per task: the pelvis, then each bone's way (normalised here): spine, neck, left upper arm and forearm, right upper
        // arm and forearm, left thigh and shin, right thigh and shin.
        (Double3 Pelvis, Double3[] Ways) p = task switch
        {
            // On the throttle: stood at the console, the right hand forward on the regulator, the left up on the brake.
            FilmTask.Driving => (new(0, 0.95, 0.05),
            [
                new(0, 1, -0.15), new(0, 1, -0.1), new(-0.55, -0.4, -0.6), new(0.15, 0.25, -1), new(0.45, -0.55, -0.6), new(-0.05, 0.05, -1),
                new(-0.15, -1, -0.15), new(0, -1, 0.1), new(0.15, -1, 0.05), new(0, -1, 0),
            ]),
            // At the shovel: bent into the swing, the blade low and forward, the knees bent, feet apart.
            FilmTask.Firing => (new(0, 0.8, 0.15),
            [
                new(0, 0.6, -0.8), new(0, 0.5, -1), new(-0.3, -0.8, -0.5), new(0.25, -0.55, -0.8), new(0.4, -0.9, -0.2), new(-0.1, -0.5, -0.9),
                new(-0.35, -0.8, -0.5), new(0, -1, 0.3), new(0.3, -0.9, 0.3), new(0, -1, 0.2),
            ]),
            // In the gun's seat: sat, the knees apart either side of the breech, the hands out either side of the breech on its handles (clear of the gun:
            // the film holds it a solid).
            FilmTask.Gunning => (new(0, 0.62, 0.1),
            [
                new(0, 1, -0.25), new(0, 1, -0.2), new(-0.5, -0.6, -0.3), new(-0.8, 0, -0.4), new(0.5, -0.6, -0.3), new(0.8, 0, -0.4),
                new(-0.6, -0.3, -0.75), new(0, -1, 0.1), new(0.6, -0.3, -0.75), new(0, -1, 0.1),
            ]),
            // Carrying in both arms: leaned back against the load, the forearms under it in front.
            _ => (new(0, 0.95, 0),
            [
                new(0, 1, 0.12), new(0, 1, 0.02), new(-0.35, -0.75, -0.55), new(0.2, 0.15, -1), new(0.35, -0.75, -0.55), new(-0.2, 0.15, -1),
                new(-0.12, -1, -0.05), new(0, -1, 0), new(0.12, -1, 0.05), new(0, -1, 0),
            ]),
        };
        var sk = Bodies.Skeleton;
        double L(int a, int b) => (sk[a].At - sk[b].At).Length;
        var j = new Double3[11];
        var w = p.Ways;
        j[2] = p.Pelvis;
        j[1] = j[2] + w[0].Normalized * L(1, 2);
        j[0] = j[1] + w[1].Normalized * L(0, 1);
        j[3] = j[1] + w[2].Normalized * L(1, 3);
        j[4] = j[3] + w[3].Normalized * L(3, 4);
        j[5] = j[1] + w[4].Normalized * L(1, 5);
        j[6] = j[5] + w[5].Normalized * L(5, 6);
        j[7] = j[2] + w[6].Normalized * L(2, 7);
        j[8] = j[7] + w[7].Normalized * L(7, 8);
        j[9] = j[2] + w[8].Normalized * L(2, 9);
        j[10] = j[9] + w[9].Normalized * L(9, 10);
        return j;
    }

    /// <summary>Dead: its muscles let go, and it's a ragdoll (the bones hold, nothing else).</summary>
    static void Die(Doll d)
    {
        d.Alive = false;
        var cs = d.Body.Constraints;
        for (int i = d.Muscles; i < cs.Length; i++)
            cs[i] = cs[i] with { Stiffness = 0 };
    }

    /// <summary>
    /// Alive, its hands go out to meet what's coming: a share of the way each substep toward a point before its chest the
    /// way it's going (down, slow), either side of it. Deterministic: positions only, from its own state.
    /// </summary>
    static void Reach(Doll d, FilmTuning t, double h)
    {
        var ps = d.Body.Particles;
        var v = Double3.Zero;
        foreach (var p in ps)
            v += p.Position - p.Previous;
        v *= 1 / (ps.Length * h);
        var dir = v.Length > 1 ? v.Normalized : new Double3(0, -1, 0);
        var across = Double3.Cross(dir, Double3.Up);
        across = across.Length > 1e-6 ? across.Normalized : ps[5].Position - ps[3].Position;
        across = across.Length > 1e-6 ? across.Normalized : new Double3(1, 0, 0);
        var chest = ps[1].Position;
        foreach (var (hand, side) in new[] { (4, -1.0), (6, 1.0) })
        {
            var target = chest + dir * 0.55 + across * (side * 0.28);
            // Moved with its history, so it's a reach, not a shove: its velocity isn't changed, only where it's going.
            var move = (target - ps[hand].Position) * t.Reach;
            ps[hand].Position += move;
            ps[hand].Previous += move;
        }
    }

    /// <summary>
    /// E.3: each living player becomes their ragdoll (the 11 joints of D.9), launched with their velocity times the fling, an
    /// upward kick and a tumble from the seed, capped; a bystander far from every car gets nothing, and a beat to watch. The
    /// gunner in the gun's seat (T112) is thrown up out of it, from just clear of the gun, with the seat's own velocity.
    /// </summary>
    static Doll Ragdoll(FilmPlayer p, List<WreckBody> cars, IReadOnlyList<FilmCar> starts, Pcg32 rng, FilmTuning t, double speed)
    {
        double c = DMath.Cos(p.Yaw), s = DMath.Sin(p.Yaw);
        var at = p.Position;
        if (p.Seated && Gunned(p.Position, cars, starts) is { } gun)
        {
            // Back from the gun's breech along its laid line, so no joint starts inside the gun (the seat's just behind it).
            var behind = gun.Car.Right * DMath.Sin(gun.Start.GunYaw) + gun.Car.Back * DMath.Cos(gun.Start.GunYaw);
            at += behind * t.SeatClear;
        }
        var task = p.Seated ? FilmTask.Gunning : p.Task;
        var pose = TaskPose(task);
        var particles = Bodies.Skeleton.Select((j, i) => new Particle(at + new Double3(pose[i].X * c + pose[i].Z * s, pose[i].Y, -pose[i].X * s + pose[i].Z * c), 1, j.Radius)).ToArray();
        var bones = Bodies.Bones.Select(b => new DistanceConstraint(b.A, b.B, (Bodies.Skeleton[b.A].At - Bodies.Skeleton[b.B].At).Length, b.Stiffness)).ToArray();
        bool bystander = cars.All(car => Outside(car, p.Position) > t.Bystander);
        // Alive, braced: the muscles hold the brace's shape over the bones (a bystander stands, then goes limp with no muscles).
        // (At work, they hold the work's pose first: TaskHold, then the brace.)
        var held = task == FilmTask.None ? Brace : pose;
        var muscles = MusclePairs.Select(m => new DistanceConstraint(m.A, m.B, (held[m.A] - held[m.B]).Length, bystander ? 0 : t.Muscle)).ToArray();
        var body = new PbdBody(particles, [.. bones, .. muscles]) { Friction = 0.3, Bounce = 0.2, Iterations = 6 };
        double kick = rng.Range(t.KickMin, t.KickMax), spin = rng.Range(Math.Min(t.SpinMin, t.Spin), t.Spin);
        var axis = new Double3(rng.Range(-1, 1), rng.Range(-0.3, 0.3), rng.Range(-1, 1));
        axis = axis.Length > 1e-6 ? axis.Normalized : Double3.Up;
        // Thrown out of the gun's seat: up, hard (the seat's velocity is its car's, flung like everyone's).
        // Thrown out of the gun's seat: straight up off it with the seat's own velocity (its car's), not flung on into the gun
        // in front of them; the car slows under them in the air, so they go up and over it.
        if (p.Seated)
        {
            kick = t.SeatKick;
            // ... and over, head over heels: the seat's back pitches them forward about the seat's own axis, at the full spin.
            (axis, spin) = (new Double3(c, 0, -s), t.Spin);
        }
        var v = (p.Seated ? p.Velocity : p.Velocity * t.Fling) + Double3.Up * kick;
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
        return new Doll(body, p.Inside, bystander ? 0.8 : 0) { Muscles = bones.Length, TaskUntil = task == FilmTask.None ? 0 : t.TaskHold };
    }

    /// <summary>The car whose gun a point is at (within 2.5 m of its pivot), for the gunner's start.</summary>
    static (WreckBody Car, FilmCar Start)? Gunned(Double3 world, List<WreckBody> cars, IReadOnlyList<FilmCar> starts)
    {
        for (int i = 0; i < cars.Count && i < starts.Count; i++)
            if (starts[i].Gun && (cars[i].ToWorld(starts[i].GunAt) - world).Length < 2.5)
                return (cars[i], starts[i]);
        return null;
    }

    /// <summary>Whether a point is out of a car's box.</summary>
    static bool Out(WreckBody car, Double3 world)
    {
        var d = world - car.Origin;
        double y = Double3.Dot(d, car.Up);
        return Math.Abs(Double3.Dot(d, car.Right)) > car.HalfWidth || Math.Abs(Double3.Dot(d, car.Back)) > car.HalfLength || y < 0 || y > car.Height;
    }

    /// <summary>How far a point is outside a car's box (0 inside it).</summary>
    static double Outside(WreckBody car, Double3 world)
    {
        var d = world - car.Origin;
        double x = Math.Abs(Double3.Dot(d, car.Right)) - car.HalfWidth, y = Double3.Dot(d, car.Up), z = Math.Abs(Double3.Dot(d, car.Back)) - car.HalfLength;
        double dy = y < 0 ? -y : y > car.Height ? y - car.Height : 0;
        return new Double3(Math.Max(0, x), dy, Math.Max(0, z)).Length;
    }

    /// <summary>A car's pose: its origin and basis.</summary>
    readonly record struct Pose(Double3 Origin, Double3 Right, Double3 Up, Double3 Back)
    {
        public Double3 ToWorld(Double3 local) => Origin + Right * local.X + Up * local.Y + Back * local.Z;

        public Double3 ToLocal(Double3 world)
        {
            var d = world - Origin;
            return new Double3(Double3.Dot(d, Right), Double3.Dot(d, Up), Double3.Dot(d, Back));
        }

        public Double3 Direction(Double3 local) => Right * local.X + Up * local.Y + Back * local.Z;

        /// <summary>Between where a car was a frame ago and where it is now (the basis lerped and squared up, as playback draws it).</summary>
        public static Pose Between(WreckBody b, double u)
        {
            var back = Double3.Lerp(b.PrevBack, b.Back, u).Normalized;
            var up = Double3.Lerp(b.PrevUp, b.Up, u).Normalized;
            var right = Double3.Cross(up, back).Normalized;
            return new Pose(Double3.Lerp(b.PrevOrigin, b.Origin, u), right, Double3.Cross(back, right).Normalized, back);
        }
    }

    /// <summary>
    /// E.3's colliders for the ragdolls, as they are this substep: the ground, the cars as boxes (hollow for whoever's inside),
    /// and each car's mounted gun. The cars move: a contact carries the car's own motion, so a car that runs into a body
    /// throws it, and one that comes down on it presses it into the ground, which crushes it (the director's point 4 of 5 Oct
    /// 2026: nothing pushes it out to somewhere safe; it's pinned or run under as the physics has it).
    /// </summary>
    /// <summary>
    /// A mounted gun's barrel and breech as a box about its pivot (across, up, back along its laid line): the cannon's model
    /// runs from the breech 0.44 m behind the pivot to the muzzle 1.5 m ahead (TrainKit).
    /// </summary>
    public static readonly (Double3 Min, Double3 Max) GunBarrel = (new Double3(-0.3, -0.32, -1.55), new Double3(0.3, 0.32, 0.55));

    /// <summary>A mounted gun's pedestal under its pivot, down to the roof it stands on (0.9 m under the pivot: Guns.PivotHeight).</summary>
    public static readonly (Double3 Min, Double3 Max) GunPedestal = (new Double3(-0.35, -0.9, -0.35), new Double3(0.35, -0.3, 0.35));

    sealed class Stage(List<WreckBody> cars, IReadOnlyList<FilmCar> starts, Func<double, double, double> ground)
    {
        readonly Pose[] _now = new Pose[cars.Count], _before = new Pose[cars.Count];

        /// <summary>The cars at <paramref name="u"/> of the way through this frame, and where they were the substep before.</summary>
        public void At(double u, double u0)
        {
            for (int i = 0; i < cars.Count; i++)
            {
                _now[i] = Pose.Between(cars[i], u);
                _before[i] = Pose.Between(cars[i], u0);
            }
        }

        public Contact? Touch(Double3 p, double r, Doll d)
        {
            Contact? best = null;
            double deepest = 0;
            double g = ground(p.X, p.Z);
            bool onGround = p.Y - r < g;
            if (onGround)
            {
                var n = new Double3(ground(p.X - 0.3, p.Z) - ground(p.X + 0.3, p.Z), 0.6, ground(p.X, p.Z - 0.3) - ground(p.X, p.Z + 0.3)).Normalized;
                (best, deepest) = (new Contact(p with { Y = g + r }, n), g + r - p.Y);
            }
            bool fromAbove = false, byCar = false;
            for (int i = 0; i < cars.Count; i++)
            {
                var car = cars[i];
                var pose = _now[i];
                var local = pose.ToLocal(p);
                bool home = car.Vehicle == d.Inside && Math.Abs(local.X) <= car.HalfWidth && Math.Abs(local.Z) <= car.HalfLength
                    && local.Y >= 0 && local.Y <= car.Height;
                Contact? hit = null;
                if (home)
                {
                    // Inside its walls: kept in, but for its doorways (E.3: "a player inside tumbles around inside the car
                    // and can be thrown out through an opening, but never through a wall"). Out through one, it's outside.
                    double floor = starts[i].Floor;
                    double hw = car.HalfWidth - 0.05, hl = car.HalfLength - 0.05, top = car.Height - 0.05;
                    var kept = new Double3(Math.Clamp(local.X, -hw + r, hw - r), Math.Clamp(local.Y, floor + 0.05 + r, top - r), Math.Clamp(local.Z, -hl + r, hl - r));
                    if (kept.X != local.X && Doorway(starts[i], x: true, local, r))
                        kept = kept with { X = local.X };
                    if (kept.Z != local.Z && Doorway(starts[i], x: false, local, r))
                        kept = kept with { Z = local.Z };
                    if ((kept - local).Length >= 1e-9)
                        hit = new Contact(kept, (kept - local).Normalized);
                }
                else if (Math.Abs(local.X) <= car.HalfWidth + r && Math.Abs(local.Z) <= car.HalfLength + r && local.Y >= -r && local.Y <= car.Height + r)
                    hit = Collide.SphereBox(local, r, new Double3(-car.HalfWidth, 0, -car.HalfLength), new Double3(car.HalfWidth, car.Height, car.HalfLength));
                if (starts[i].Gun && Gun(local, r, starts[i]) is { } gun
                    && (hit is not { } other || (gun.Position - local).Length > (other.Position - local).Length))
                    hit = gun;
                if (hit is not { } h)
                    continue;
                double depth = (h.Position - local).Length;
                var world = pose.ToWorld(h.Position);
                var normal = pose.Direction(h.Normal);
                byCar = true;
                if (onGround && !home && normal.Y < -0.3)
                    fromAbove = true;
                if (depth <= deepest)
                    continue;
                // The surface's own motion this substep, at the contact: a moving car carries what it touches.
                (best, deepest) = (new Contact(world, normal, world - _before[i].ToWorld(h.Position)), depth);
            }
            d.OnCar |= byCar;
            d.Squeezed |= fromAbove;
            return best;
        }

        /// <summary>
        /// Whether a point inside a car is in one of its doorways (<see cref="FilmCar.Doorways"/>), in the side wall it's
        /// past (<paramref name="x"/>: a side door, across X; else an end door, along Z): floor to lintel, between its jambs.
        /// </summary>
        static bool Doorway(FilmCar car, bool x, Double3 local, double r)
        {
            foreach (var door in car.Doorways ?? [])
            {
                var size = door.Max - door.Min;
                bool side = size.X < size.Z;
                if (side != x)
                    continue;
                double centre = x ? (door.Min.X + door.Max.X) / 2 : (door.Min.Z + door.Max.Z) / 2;
                if (Math.Sign(centre) != Math.Sign(x ? local.X : local.Z))
                    continue;
                double along = x ? local.Z : local.X, lo = x ? door.Min.Z : door.Min.X, hi = x ? door.Max.Z : door.Max.X;
                if (along > lo + r * 0.5 && along < hi - r * 0.5 && local.Y > door.Min.Y - 0.1 && local.Y < door.Max.Y - r)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// A mounted gun as a solid, in its car's frame (relative to its pivot, along its laid line): the barrel and breech
        /// (<see cref="GunBarrel"/>) on its pedestal down to the roof (<see cref="GunPedestal"/>).
        /// </summary>
        static Contact? Gun(Double3 local, double r, FilmCar car)
        {
            double c = DMath.Cos(car.GunYaw), s = DMath.Sin(car.GunYaw);
            var right = new Double3(c, 0, -s);
            var back = new Double3(s, 0, c);
            var d = local - car.GunAt;
            var g = new Double3(Double3.Dot(d, right), d.Y, Double3.Dot(d, back));
            var barrel = Collide.SphereBox(g, r, GunBarrel.Min, GunBarrel.Max);
            var pedestal = Collide.SphereBox(g, r, GunPedestal.Min, GunPedestal.Max);
            if (barrel is null && pedestal is null)
                return null;
            var hit = barrel is not { } b ? pedestal!.Value : pedestal is not { } p || (b.Position - g).Length >= (p.Position - g).Length ? b : p;
            return new Contact(car.GunAt + right * hit.Position.X + Double3.Up * hit.Position.Y + back * hit.Position.Z,
                right * hit.Normal.X + Double3.Up * hit.Normal.Y + back * hit.Normal.Z);
        }
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

    static FilmFrame Frame(List<WreckBody> cars, List<Doll> dolls, List<WreckImpact> impacts, List<FilmLanding> landings) =>
        new([.. cars.Select(c => (c.Origin, c.Right, c.Up, c.Back))], [.. dolls.Select(d => d.Body.Particles.Select(p => p.Position).ToArray())], impacts, landings);
    /// <summary>
    /// E.5: a player's peak moment: the highest apex, the longest airtime or the hardest landing, whichever scores highest.
    /// Returns when (recorded seconds) and the score. "In the air" is off whatever's under them (<see cref="Clearance"/>):
    /// stood up, or lying on a roof, isn't flying (note 251: the pelvis over the ground had a roof rider "airborne" all
    /// film and anyone stood up "in the air", so the peak was rarely a flight).
    /// </summary>
    static (double At, double Score) Peak(IReadOnlyList<FilmFrame> frames, IReadOnlyList<FilmCar> cars, int doll, Func<double, double, double> ground)
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

    /// <summary>
    /// Where a player's shot looks at <paramref name="recorded"/>: the middle of their ragdoll's bounds, steadied over a
    /// tenth of a second either side, so the held camera turns to follow a flight (E.4 O13: it pans, it doesn't move) and the
    /// body stays in the middle of the frame (O6) however tight the lens.
    /// </summary>
    public Double3 Middle(int doll, double recorded) => Middle(Frames, doll, recorded);

    static Double3 Middle(IReadOnlyList<FilmFrame> frames, int doll, double recorded)
    {
        double f = Math.Clamp(recorded * Rate, 0, frames.Count - 1);
        var sum = Double3.Zero;
        double weight = 0;
        for (int k = -3; k <= 3; k++)
        {
            double at = Math.Clamp(f + k, 0, frames.Count - 1);
            int i = (int)Math.Floor(at), j = Math.Min(i + 1, frames.Count - 1);
            var c = Double3.Lerp(Bounds(frames[i].Ragdolls[doll]), Bounds(frames[j].Ragdolls[doll]), at - i);
            double w = 4 - Math.Abs(k);
            sum += c * w;
            weight += w;
        }
        return sum * (1 / weight);
    }

    static Double3 Bounds(Double3[] joints)
    {
        Double3 lo = joints[0], hi = joints[0];
        foreach (var j in joints)
        {
            lo = new Double3(Math.Min(lo.X, j.X), Math.Min(lo.Y, j.Y), Math.Min(lo.Z, j.Z));
            hi = new Double3(Math.Max(hi.X, j.X), Math.Max(hi.Y, j.Y), Math.Max(hi.Z, j.Z));
        }
        return (lo + hi) * 0.5;
    }

    static Double3 ToLocal((Double3 Origin, Double3 Right, Double3 Up, Double3 Back) pose, Double3 world)
    {
        var d = world - pose.Origin;
        return new Double3(Double3.Dot(d, pose.Right), Double3.Dot(d, pose.Up), Double3.Dot(d, pose.Back));
    }

    static Double3 ToWorld((Double3 Origin, Double3 Right, Double3 Up, Double3 Back) pose, Double3 local) =>
        pose.Origin + pose.Right * local.X + pose.Up * local.Y + pose.Back * local.Z;

    /// <summary>Where a shot's camera is at <paramref name="recorded"/>: rigged to its car (<see cref="FilmShot.Rig"/>), or held.</summary>
    public Double3 CameraAt(FilmShot shot, double recorded, Double3 held)
    {
        if (shot.Rig < 0)
            return held;
        var (a, b, u) = At(recorded);
        if (shot.Rig >= a.Cars.Count)
            return held;
        return Double3.Lerp(ToWorld(a.Cars[shot.Rig], shot.RigAt), ToWorld(b.Cars[shot.Rig], shot.RigAt), u);
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
        public static List<FilmShot> Plan(FilmTuning t, FilmStart start, IReadOnlyList<FilmFrame> frames, Dictionary<int, (double At, double Score)> peaks,
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
                // At the base speed easing to the peak's: the stretch of recorded time the shot covers, the throw into their
                // death (with the hits they took on the way) and then the limp aftermath; the slowest moment is at the hit.
                double span = each * (t.BaseSlow + t.PeakSlow) / 2;
                double from = Math.Clamp(peak - span * 0.55, 0, Math.Max(0, length - span)), to = Math.Min(length, from + span);
                var (eye, look, fov, rig, rigAt) = Camera(t, start, frames, i, from, to, ground);
                shots.Add(new FilmShot(ShotKind.Player, p.Id, each, from, to, t.PeakSlow / t.BaseSlow, eye, eye, look, look, fov,
                    $"{p.Name.ToUpperInvariant()} - {p.Role.ToUpperInvariant()}", rig, rigAt));
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
        static (Double3 Eye, Double3 Look, double Fov, int Rig, Double3 RigAt) Camera(FilmTuning t, FilmStart start, IReadOnlyList<FilmFrame> frames, int doll,
            double from, double to, Func<double, double, double> ground)
        {
            int f0 = (int)(from * Rate), f1 = Math.Min(frames.Count - 1, (int)Math.Ceiling(to * Rate)), mid = (f0 + f1) / 2;
            var subject = Middle(frames, doll, mid * Dt);
            // Inside a car through the middle of the shot: the camera's rigged to that car (it held still in the world, the
            // car ran off out of the shot at 20 m/s with them in it). Otherwise held in the world.
            int rig = -1;
            for (int c = 0; c < start.Cars.Count && c < frames[mid].Cars.Count; c++)
                if (InBox(frames[mid].Cars[c], start.Cars[c], frames[mid].Ragdolls[doll][2]))
                    rig = c;
            Double3 EyeAt(Double3 eye, int f) => rig < 0 ? eye : ToWorld(frames[f].Cars[rig], ToLocal(frames[mid].Cars[rig], eye));
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
                        if (!Clear(t, frames, doll, f0, f1, f => EyeAt(eye, f), ground))
                            continue;
                        var flat = (dir with { Y = 0 }).Normalized;
                        double across = vel.Length > 0.5 ? 1 - Math.Abs(Double3.Dot(vel.Normalized, dir * -1)) : 0.5;
                        // The close ring first (the director, 5 Oct 2026: subjects were 5-40% of the frame, under O6's 25%): a
                        // 7 m candidate has to be much the better side or angle to win, an 11 m one more so.
                        double score = Double3.Dot(flat, away) * 1.0 + across * 0.8 - (r - 4) * 0.12 - Math.Abs(el - 25) * 0.005;
                        if (best is null || score > best.Value.Score)
                            best = (eye, score);
                    }
            // O10: nothing passed: from 70 degrees overhead at 12 m.
            var overhead = subject + new Double3(DMath.Cos(70 * DMath.PI / 180) * 12 * away.X, DMath.Sin(70 * DMath.PI / 180) * 12, DMath.Cos(70 * DMath.PI / 180) * 12 * away.Z);
            var chosen = best?.Eye ?? overhead;
            if (best is null)
                rig = -1;
            return (chosen, subject, Fit(t, frames, doll, f0, f1, f => EyeAt(chosen, f)), rig, rig < 0 ? default : ToLocal(frames[mid].Cars[rig], chosen));
        }

        static bool InBox((Double3 Origin, Double3 Right, Double3 Up, Double3 Back) pose, FilmCar car, Double3 world)
        {
            var l = ToLocal(pose, world);
            return Math.Abs(l.X) <= car.HalfWidth && Math.Abs(l.Z) <= car.HalfLength && l.Y >= 0 && l.Y <= car.Height;
        }

        /// <summary>
        /// E.4 O6 by the lens: the field of view (degrees) that frames the subject at <see cref="FilmTuning.ShotFill"/> of the
        /// frame's height through the middle of the shot, never over 60% of it nor 70% of its width (16:9), seen from
        /// <paramref name="eye"/> as the camera follows their middle (<see cref="Middle"/>).
        /// </summary>
        static double Fit(FilmTuning t, IReadOnlyList<FilmFrame> frames, int doll, int f0, int f1, Func<int, Double3> eyeAt)
        {
            var tall = new List<double>();
            double tallest = 0, widest = 0;
            for (int f = f0; f <= f1; f++)
            {
                var centre = Middle(frames, doll, f * Dt);
                var eye = eyeAt(f);
                var dir = centre - eye;
                double dist = dir.Length;
                if (dist < 1e-6)
                    continue;
                dir *= 1 / dist;
                var up = Double3.Up - dir * Double3.Dot(Double3.Up, dir);
                up = up.Length > 1e-6 ? up.Normalized : new Double3(0, 0, 1);
                var right = Double3.Cross(dir, up);
                double v = 0, h = 0;
                foreach (var j in frames[f].Ragdolls[doll])
                {
                    var o = j - centre;
                    v = Math.Max(v, Math.Abs(Double3.Dot(o, up)));
                    h = Math.Max(h, Math.Abs(Double3.Dot(o, right)));
                }
                // Both ways from the middle the camera holds, as a share of the distance; the joints' own size beside.
                double q = (2 * v + 0.25) / dist;
                tall.Add(q);
                tallest = Math.Max(tallest, q);
                widest = Math.Max(widest, (2 * h + 0.25) / dist);
            }
            if (tall.Count == 0)
                return t.FovMax;
            tall.Sort();
            double half = Math.Max(tall[tall.Count / 2] / (2 * t.ShotFill), Math.Max(tallest / (2 * 0.6), widest / (2 * 0.7 * 16 / 9)));
            return Math.Clamp(2 * DMath.Atan(half) * 180 / DMath.PI, t.FovMin, t.FovMax);
        }

        /// <summary>
        /// A held camera's clearance over the shot (O1, O3, O4, O5): above the ground and out of every car by its radius, nothing
        /// within the lens clearance, the subject at least the minimum away and seen (two of head, chest and pelvis by
        /// rays the ground doesn't cut; cars fade, O2).
        /// </summary>
        static bool Clear(FilmTuning t, IReadOnlyList<FilmFrame> frames, int doll, int f0, int f1, Func<int, Double3> eyeAt, Func<double, double, double> ground)
        {
            for (int f = f0; f <= f1; f += 3)
            {
                var eye = eyeAt(f);
                if (eye.Y < ground(eye.X, eye.Z) + 1)
                    return false;
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
