using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>
/// The derailment's beats (T121 playtest): in your own eyes, the replay from the chase view, then the film (GDD v1.4 App.
/// E.5: everyone's own death, the settle, the cause card), or the orbit if there's no film to show.
/// </summary>
public enum DerailBeat : byte { None, FirstPerson, Replay, Orbit, Film }

/// <summary>A player's peak in the film (GDD v1.4 App. E.5): their shot, how far into it, and the recorded moment it shows.</summary>
public sealed record FilmPeak(WreckFilm Film, FilmShot Shot, double Into, double Recorded)
{
    /// <summary>The shot's camera at the peak.</summary>
    public Camera Camera => DerailSequence.FilmCamera(Shot, Into, Film);
}

/// <summary>
/// What the derailment shows on a frame (<see cref="DerailSequence.Show"/>): the beat, the cars as drawn (the replay's kept
/// frames, or the film's poses), the beat's camera (null with no derailment playing), and the replay or the film's shot
/// behind it. The app and `dt film` both draw from this, so the film on disk is the one the crew sees.
/// </summary>
/// <param name="FilmAt">The recorded instant the film's shot shows (0 outside the film).</param>
/// <param name="Distance">How far the camera is from what it frames, in the replay and the film (0 otherwise): E.4 O12's fog.</param>
/// <param name="Eye">In the first person with the film shot: which of its ragdolls is yours (you see through its eyes, at
/// <paramref name="FilmAt"/>); −1 otherwise.</param>
public sealed record DerailShot(DerailBeat Beat, IReadOnlyList<CarFrame> Frames, Camera? Camera, (CarFrame[] Frames, Crewmate[] Crew, bool Off)? Replay,
    WreckFilm? Film, (FilmShot Shot, double Into)? Filming, double FilmAt, double Distance = 0, int Eye = -1);

/// <summary>One beat of the whole sequence (<see cref="DerailSequence.Timeline"/>): the first person, the replay, then the cut shot by shot.</summary>
/// <param name="Shot">The film's shot, in the film's beat; null for the first person, the replay and the orbit.</param>
public sealed record SequenceBeat(DerailBeat Beat, double From, double To, FilmShot? Shot)
{
    public double Seconds => To - From;
}

/// <summary>
/// The derailment as the crew sees it (T121 playtest: "instead of cutting straight to that lets first ... let people
/// experience it first hand, then replay the moment from the third person train view that we already have"). First, the
/// eye stays where you were, in your car, riding it as it comes off and ploughs in (the cars follow the wreck's bodies, so
/// the cab rolls round you). Then the chase view replays the moment from a few seconds before, the train running into it
/// and over, the cause on the screen; then the camera circling the wreck (T117) while it settles. Presentation only: it
/// records what was drawn, and every client's beats run off its own count of the wreck's seconds.
/// </summary>
public sealed class DerailSequence
{
    readonly record struct Shot(double At, CarFrame[] Frames, Crewmate[] Crew);

    readonly List<Shot> _shots = [];
    double _derailedAt = double.NaN;
    int _car = -1;
    Double3 _eyeLocal, _lookLocal;
    Camera _worldEye;

    /// <summary>
    /// The sequence's timing for <paramref name="player"/>: the first person is their own, up to and through their death in
    /// the film (GDD v1.4 App. E.2 step 1, the director's decision of 5 Oct 2026), once it's shot; till then, and for anyone
    /// not in it, the tuning's.
    /// </summary>
    public static WreckTuning TuningFor(WreckTuning t, WreckFilm? film, int player) =>
        film?.FirstPersonOf(player) is { } own ? t with { FirstPersonSeconds = own } : t;

    public static DerailBeat Beat(WreckTuning t, double seconds, WreckFilm? film = null) =>
        seconds < 0 ? DerailBeat.None
        : seconds < t.FirstPersonSeconds ? DerailBeat.FirstPerson
        : seconds < t.FirstPersonSeconds + t.ReplaySeconds ? DerailBeat.Replay
        : seconds < Length(t, film) ? film is null ? DerailBeat.Orbit : DerailBeat.Film
        : DerailBeat.None;

    /// <summary>The whole sequence: the first person, the replay, then the film's cut (or the orbit without one).</summary>
    public static double Length(WreckTuning t, WreckFilm? film) =>
        film is null ? t.SequenceSeconds : t.FirstPersonSeconds + t.ReplaySeconds + film.CutLength;

    /// <summary>
    /// The sequence beat by beat, in sequence seconds: the first person, the replay, then each of the film's shots (or the
    /// orbit without a film). It ends at <see cref="Length"/>.
    /// </summary>
    public static IReadOnlyList<SequenceBeat> Timeline(WreckTuning t, WreckFilm? film)
    {
        double replayEnd = t.FirstPersonSeconds + t.ReplaySeconds;
        var beats = new List<SequenceBeat>
        {
            new(DerailBeat.FirstPerson, 0, t.FirstPersonSeconds, null),
            new(DerailBeat.Replay, t.FirstPersonSeconds, replayEnd, null),
        };
        if (film is null)
        {
            beats.Add(new(DerailBeat.Orbit, replayEnd, Length(t, null), null));
            return beats;
        }
        double at = replayEnd;
        foreach (var s in film.Cut)
        {
            beats.Add(new(DerailBeat.Film, at, at + s.Real, s));
            at += s.Real;
        }
        return beats;
    }

    /// <summary>
    /// What <paramref name="session"/>'s derailment shows this frame, over its live <paramref name="frames"/>: by the
    /// beat at its <see cref="IPlaySession.WreckSeconds"/>, the cars as drawn and the camera. <paramref name="ownEyes"/>
    /// false (a headset, whose eyes stay its own) skips the first person's camera for the orbit's.
    /// </summary>
    public DerailShot Show(IPlaySession session, IReadOnlyList<CarFrame> frames, bool ownEyes = true)
    {
        var film = session.Film;
        var t = session.SequenceTuning;
        double seconds = session.WreckSeconds;
        var wreck = session.Train.Wreck;
        bool wrecking = session.WreckCinematic && wreck is not null;
        var beat = wrecking ? Beat(t, seconds, film) : DerailBeat.None;
        // Your own eyes (App. E.2 step 1, the director's decision): once the film's shot, through your own ragdoll's, flung
        // with it to the hit that kills you and a moment past; till then, riding your car.
        int eye = beat == DerailBeat.FirstPerson && ownEyes && film is not null ? Doll(film, session.PlayerId) : -1;
        double ownAt = eye >= 0 ? OwnAt(film!, seconds) : 0;
        if (eye >= 0)
            frames = FilmFrames(film!, ownAt, frames);
        var replay = beat == DerailBeat.Replay ? ReplayAt(seconds, t) : null;
        if (replay is { } replaying)
            frames = replaying.Frames;
        // E.5: the film, everyone's own death: its cars, each shot's camera (note 177).
        var filming = beat == DerailBeat.Film && film is not null ? film.CutAt(FilmSeconds(t, seconds)) : null;
        double at = filming is { } fs ? fs.Shot.At(fs.Into) : 0;
        if (filming is not null)
            frames = FilmFrames(film!, at, frames);
        Camera? camera = !wrecking ? null
            : eye >= 0 ? FilmEye(film!, eye, ownAt, _worldEye.FovYDegrees > 0 ? _worldEye.FovYDegrees : 70)
            : beat == DerailBeat.FirstPerson && ownEyes ? FirstPerson(frames)
            : replay is { } shot ? ReplayCamera(shot.Frames, session.Train.StandingCar, shot.Off, Ground(session.Train))
            : filming is { } f ? FilmCamera(f.Shot, f.Into, film)
            : Views.Wreck(wreck!, OrbitSeconds(t, seconds));
        double distance = filming is { } framed ? (framed.Shot.Camera - framed.Shot.Look).Length
            : replay is { } r && camera is { } c && Views.Train(r.Frames, session.Train.StandingCar) is { Length: > 0 } own
                ? (own[own.Length / 2].Origin - c.Position).Length : 0;
        return new DerailShot(beat, frames, camera, replay, film, filming, eye >= 0 ? ownAt : at, distance, eye);
    }

    /// <summary>Which of the film's ragdolls is <paramref name="player"/>'s (−1 if they're not in it).</summary>
    public static int Doll(WreckFilm film, int player)
    {
        for (int i = 0; i < film.Start.Players.Count; i++)
            if (film.Start.Players[i].Id == player)
                return i;
        return -1;
    }

    /// <summary>The recorded moment the first person shows <paramref name="seconds"/> into it (<see cref="FilmTuning.FirstPersonRate"/>).</summary>
    public static double OwnAt(WreckFilm film, double seconds) => Math.Clamp(seconds * film.Tuning.FirstPersonRate, 0, film.Recorded);

    /// <summary>
    /// Through a ragdoll's eyes in the film at <paramref name="recorded"/>: at its head, facing the way its chest does, its
    /// shoulders the horizon (it tumbles, you tumble), steadied over a few recorded frames so it reads as a body flung, not
    /// a camera shaken.
    /// </summary>
    public static Camera FilmEye(WreckFilm film, int doll, double recorded, float fovY = 70)
    {
        Double3 up = Double3.Zero, right = Double3.Zero;
        for (int k = -2; k <= 2; k++)
        {
            var (a, b, u) = film.At(recorded + k / (double)WreckFilm.Rate);
            Double3 J(int j) => Double3.Lerp(a.Ragdolls[doll][j], b.Ragdolls[doll][j], u);
            double w = 3 - Math.Abs(k);
            up += (J(0) - J(2)) * w;
            right += (J(5) - J(3)) * w;
        }
        var (ha, hb, hu) = film.At(recorded);
        var head = Double3.Lerp(ha.Ragdolls[doll][0], hb.Ragdolls[doll][0], hu);
        up = up.Length > 1e-6 ? up.Normalized : Double3.Up;
        right -= up * Double3.Dot(right, up);
        right = right.Length > 1e-6 ? right.Normalized : Double3.Cross(up, new Double3(0, 0, 1)).Normalized;
        var fwd = Double3.Cross(up, right).Normalized;
        // From just in front of the face, looking a little down the body's front (where you'd see the ground coming), rolled
        // with the shoulders.
        var eye = head + fwd * 0.12 + up * 0.04;
        var back = (fwd * 0.94 - up * 0.34).Normalized * -1;
        var camUp = Double3.Cross(back, right).Normalized;
        var m = new Matrix4x4(
            (float)right.X, (float)right.Y, (float)right.Z, 0,
            (float)camUp.X, (float)camUp.Y, (float)camUp.Z, 0,
            (float)back.X, (float)back.Y, (float)back.Z, 0,
            0, 0, 0, 1);
        var cam = Camera.LookAt(eye, eye - back, fovY);
        cam.Orientation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m));
        return cam;
    }

    /// <summary>
    /// Sets what <paramref name="scene"/> draws for <paramref name="shot"/> seen from <paramref name="eye"/>: in the film,
    /// the crew as ragdolls and nobody else, the cutaway and the light rig; in the replay, the crew as they were; outside
    /// either, the live bodies and <paramref name="crew"/>. The live wreck's dust and sparks only once it's off and not in the film.
    /// </summary>
    public static void Dress(GreyboxScene scene, DerailShot shot, IPlaySession session, Double3 eye, IReadOnlyList<Crewmate> crew)
    {
        bool filming = shot.Filming is not null && shot.Film is not null;
        // In your own eyes through the film: its cars and the rest of the crew as its ragdolls, not your own (you're in its head).
        bool own = shot.Eye >= 0 && shot.Film is not null;
        scene.Bodies = filming ? FilmBodies(shot.Film!, shot.FilmAt)
            : own ? [.. FilmBodies(shot.Film!, shot.FilmAt).Where(b => b.Owner != shot.Film!.Start.Players[shot.Eye].Id)]
            : session.World.Bodies.All;
        scene.Crew = shot.Replay is { } replayed ? replayed.Crew : filming || own ? [] : crew;
        scene.CutAway = filming ? FilmCutAway(shot.Film!, shot.Filming!.Value.Shot, shot.FilmAt, shot.Frames, eye) : null;
        scene.Lights = filming ? FilmLights(shot.Film!, shot.Filming!.Value.Shot, shot.FilmAt, eye) : null;
        // Replaying the run-in, the train's still on the rails: no wreck yet, no sparks. The film draws its own wreck; the
        // live one's dust and sparks are somewhere else by then.
        scene.Wreck = shot.Replay is { Off: false } || filming || own ? null : session.Train.Wreck;
        scene.Derailed = shot.Replay is { } rerun ? rerun.Off : session.World.Derailed;
    }

    /// <summary>
    /// E.4 O12 for the film: the fog pushed out to at least twice the shot's distance (camera to what it frames), so a wide
    /// of the whole wreck reads through the night as a close shot does; and the same for the replay's chase view. Nothing
    /// in the first person.
    /// </summary>
    public static void Fog(ref FrameLighting light, DerailShot shot)
    {
        // The replay too: its chase view is 80 m off the middle of the train, which the night's fog had all but swallowed.
        if (shot.Distance > 0)
            Views.CinematicFog(ref light, shot.Distance);
    }

    /// <summary>Seconds into the film's cut.</summary>
    public static double FilmSeconds(WreckTuning t, double seconds) => Math.Max(0, seconds - t.FirstPersonSeconds - t.ReplaySeconds);

    /// <summary>
    /// The film's cars at <paramref name="recorded"/> seconds of its recording, as frames over the live train's shapes
    /// (<paramref name="live"/>, by vehicle), between its 30 Hz keyframes.
    /// </summary>
    public static CarFrame[] FilmFrames(WreckFilm film, double recorded, IReadOnlyList<CarFrame> live)
    {
        var (a, b, u) = film.At(recorded);
        var frames = live.ToArray();
        for (int i = 0; i < film.Start.Cars.Count && i < a.Cars.Count; i++)
        {
            int v = film.Start.Cars[i].Vehicle;
            if (v < 0 || v >= frames.Length)
                continue;
            var (oa, _, ua, ba) = a.Cars[i];
            var (ob, _, ub, bb) = b.Cars[i];
            var back = Double3.Lerp(ba, bb, u).Normalized;
            var up = Double3.Lerp(ua, ub, u).Normalized;
            var right = Double3.Cross(up, back).Normalized;
            frames[v] = new CarFrame(frames[v].Index, Double3.Lerp(oa, ob, u), right, Double3.Cross(back, right).Normalized, back, Double3.Zero, frames[v].Shape);
        }
        return frames;
    }

    /// <summary>The crew as the film has them at <paramref name="recorded"/>: ragdolls (D.9's 11 joints) in the world, each its owner's.</summary>
    public static List<Sim.Physics.Body> FilmBodies(WreckFilm film, double recorded)
    {
        var (a, b, u) = film.At(recorded);
        var bodies = new List<Sim.Physics.Body>(a.Ragdolls.Count);
        for (int d = 0; d < a.Ragdolls.Count && d < film.Start.Players.Count; d++)
        {
            var joints = a.Ragdolls[d].Select((p, j) => new Ballast.Physics.Particle(Double3.Lerp(p, b.Ragdolls[d][j], u), 1, 0.1)).ToArray();
            bodies.Add(new Sim.Physics.Body(-1 - d, Sim.Physics.BodyKind.Ragdoll, Sim.Player.PlayerState.World, new Ballast.Physics.PbdBody(joints))
            {
                Owner = film.Start.Players[d].Id,
            });
        }
        // What they were carrying (note 370): in their hands till they let go, then thrown free and tumbling.
        if (a.Loads is { } loads && b.Loads is { } next)
            foreach (var load in loads)
            {
                if (load.Doll >= film.Start.Players.Count || film.Start.Players[load.Doll] is not { Carried: { } kind } p)
                    continue;
                var to = next.FirstOrDefault(l => l.Doll == load.Doll, load);
                var at = Double3.Lerp(load.At, to.At, u);
                var up = Double3.Lerp(load.Up, to.Up, u);
                double turn = Math.IEEERemainder(to.Yaw - load.Yaw, Math.Tau);
                bodies.Add(new Sim.Physics.Body(-100 - load.Doll, kind, Sim.Player.PlayerState.World,
                    new Ballast.Physics.PbdBody([new Ballast.Physics.Particle(at, 1, 0.3)]))
                {
                    Owner = -1,
                    Cargo = p.CarriedCargo,
                    Yaw = load.Yaw + turn * u,
                    Tilt = up.Length > 1e-6 ? up.Normalized : Double3.Up,
                });
            }
        // The extras (App. E.3; note 373): the stowed dead, each in its owner's look, and the loose things aboard, tumbling.
        var extras = film.Start.Extras ?? [];
        if (a.Dead is { } dead && b.Dead is { } deadNext)
        {
            var bodiesOf = extras.Where(x => x.Kind == Sim.Physics.BodyKind.Ragdoll && x.Joints.Count > 2 && !x.Rides).ToList();
            for (int d = 0; d < dead.Count && d < deadNext.Count && d < bodiesOf.Count; d++)
            {
                var joints = dead[d].Select((p, j) => new Ballast.Physics.Particle(Double3.Lerp(p, deadNext[d][j], u), 1, 0.1)).ToArray();
                bodies.Add(new Sim.Physics.Body(-200 - d, Sim.Physics.BodyKind.Ragdoll, Sim.Player.PlayerState.World, new Ballast.Physics.PbdBody(joints))
                {
                    Owner = bodiesOf[d].Owner,
                });
            }
        }
        if (a.Items is { } items && b.Items is { } itemsNext)
            foreach (var item in items)
            {
                if (item.Doll < 0 || item.Doll >= extras.Count)
                    continue;
                var x = extras[item.Doll];
                var to = itemsNext.FirstOrDefault(l => l.Doll == item.Doll, item);
                var at = Double3.Lerp(item.At, to.At, u);
                var up = Double3.Lerp(item.Up, to.Up, u);
                double turn = Math.IEEERemainder(to.Yaw - item.Yaw, Math.Tau);
                bodies.Add(new Sim.Physics.Body(-1000 - item.Doll, x.Kind, Sim.Player.PlayerState.World,
                    new Ballast.Physics.PbdBody([new Ballast.Physics.Particle(at, 1, 0.3)]))
                {
                    Owner = x.Owner,
                    Cargo = x.Cargo,
                    Yaw = item.Yaw + turn * u,
                    Tilt = up.Length > 1e-6 ? up.Normalized : Double3.Up,
                });
            }
        // Past the budget (E.3: "freeze in place"; note 373): where it was in its car, wherever the car goes.
        for (int e = 0; e < extras.Count; e++)
        {
            if (!extras[e].Rides)
                continue;
            var x = extras[e];
            var from = WreckFilm.Riding(film.Start, a, x);
            var to = WreckFilm.Riding(film.Start, b, x);
            var joints = from.Select((p, j) => new Ballast.Physics.Particle(Double3.Lerp(p, to[j], u), 1, from.Length > 1 ? 0.1 : 0.3)).ToArray();
            int car = film.Start.Cars.ToList().FindIndex(c => c.Vehicle == x.Inside);
            var (_, _, carUp, back) = car >= 0 && car < a.Cars.Count ? a.Cars[car] : (default, default, Double3.Up, new Double3(0, 0, 1));
            bodies.Add(new Sim.Physics.Body(-3000 - e, x.Kind, Sim.Player.PlayerState.World, new Ballast.Physics.PbdBody(joints))
            {
                Owner = x.Owner,
                Cargo = x.Cargo,
                Yaw = Math.Atan2(-back.X, -back.Z),
                Tilt = carUp.Length > 1e-6 ? carUp.Normalized : Double3.Up,
            });
        }
        return bodies;
    }

    /// <summary>
    /// E.4 O2's fadeable occluders, as a cutaway: in a player's shot, every car whose shell stands between the camera and
    /// them (or that they're inside), so they're seen. Terrain never goes.
    /// </summary>
    public static HashSet<int> FilmCutAway(WreckFilm film, FilmShot shot, double recorded, IReadOnlyList<CarFrame> frames, Double3 eye)
    {
        var cut = new HashSet<int>();
        int doll = shot.Kind == ShotKind.Player ? film.Start.Players.ToList().FindIndex(p => p.Id == shot.Subject) : -1;
        if (doll < 0)
            return cut;
        var (a, b, u) = film.At(recorded);
        foreach (int joint in new[] { 0, 1, 2 })
        {
            var target = Double3.Lerp(a.Ragdolls[doll][joint], b.Ragdolls[doll][joint], u);
            foreach (var f in frames)
                if (Crosses(f, eye, target))
                    cut.Add(f.Index);
        }
        return cut;
    }

    /// <summary>Whether the segment from <paramref name="from"/> to <paramref name="to"/> passes through a car's box (a slab test in its frame).</summary>
    static bool Crosses(in CarFrame f, Double3 from, Double3 to)
    {
        var p = Local(f, from - f.Origin);
        var d = Local(f, to - f.Origin) - p;
        double[] lo = [-f.Shape.HalfWidth, 0, -f.Shape.HalfLength], hi = [f.Shape.HalfWidth, f.Shape.RoofHeight, f.Shape.HalfLength];
        double[] o = [p.X, p.Y, p.Z], v = [d.X, d.Y, d.Z];
        double t0 = 0, t1 = 1;
        for (int k = 0; k < 3; k++)
        {
            if (Math.Abs(v[k]) < 1e-9)
            {
                if (o[k] < lo[k] || o[k] > hi[k])
                    return false;
                continue;
            }
            double ta = (lo[k] - o[k]) / v[k], tb = (hi[k] - o[k]) / v[k];
            t0 = Math.Max(t0, Math.Min(ta, tb));
            t1 = Math.Min(t1, Math.Max(ta, tb));
            if (t0 > t1)
                return false;
        }
        return true;
    }

    /// <summary>
    /// E.4 O12: the lamps died in the wreck, so the film brings its own: a key light on the subject from the camera's side
    /// and a warm rim behind them in the forward lamp's colour; in a wide, one over the middle of the crew.
    /// </summary>
    public static List<(Double3 At, System.Numerics.Vector3 Colour, float Radius)> FilmLights(WreckFilm film, FilmShot shot, double recorded, Double3 eye)
    {
        var (a, b, u) = film.At(recorded);
        var lamp = new System.Numerics.Vector3(1.0f, 0.72f, 0.42f);
        var lights = new List<(Double3, System.Numerics.Vector3, float)>();
        int doll = shot.Kind == ShotKind.Player ? film.Start.Players.ToList().FindIndex(p => p.Id == shot.Subject) : -1;
        if (doll >= 0)
        {
            var chest = Double3.Lerp(a.Ragdolls[doll][1], b.Ragdolls[doll][1], u);
            var toEye = (eye - chest).Normalized;
            var side = Double3.Cross(Double3.Up, toEye);
            side = side.Length > 1e-6 ? side.Normalized : new Double3(1, 0, 0);
            lights.Add((chest + toEye * 2.5 + side * 1.5 + Double3.Up * 1.5, new System.Numerics.Vector3(0.75f, 0.78f, 0.9f) * 2.2f, 9f));
            lights.Add((chest - toEye * 2.2 + Double3.Up * 1.2, lamp * 3.2f, 7f));
        }
        else if (a.Ragdolls.Count > 0)
        {
            var mid = a.Ragdolls.Aggregate(Double3.Zero, (s, r) => s + r[1]) * (1.0 / a.Ragdolls.Count);
            lights.Add((mid + Double3.Up * 8, lamp * 2.5f, 40f));
        }
        return lights;
    }

    /// <summary>
    /// A shot's camera <paramref name="into"/> seconds in: held, or dollying slowly to its end (E.4 O13). A player's shot,
    /// given its <paramref name="film"/>, turns to follow the subject's middle (<see cref="WreckFilm.Middle"/>) through its
    /// tight lens, so they stay in the middle of the frame (O6).
    /// </summary>
    public static Camera FilmCamera(FilmShot shot, double into, WreckFilm? film = null)
    {
        double u = shot.Real > 0 ? Math.Clamp(into / shot.Real, 0, 1) : 0;
        var look = Double3.Lerp(shot.Look, shot.LookTo, u);
        var eye = Double3.Lerp(shot.Camera, shot.CameraTo, u);
        if (shot.Kind == ShotKind.Player && film is not null && Doll(film, shot.Subject) is >= 0 and var doll)
        {
            look = film.Middle(doll, shot.At(into));
            // Someone inside a car: the camera's rigged to it, so the car carrying them stays in the shot.
            eye = film.CameraAt(shot, shot.At(into), eye);
        }
        return Camera.LookAt(eye, look, (float)shot.Fov);
    }

    /// <summary>
    /// GDD v1.4 App. E.5 "each player's peak frame is captured as their auto-bookmark" (D.12): where in the cut
    /// <paramref name="player"/>'s own shot reaches their peak (<see cref="WreckFilm.Peaks"/>), as the film plays it. Null
    /// if the film has no shot of them.
    /// </summary>
    public static FilmPeak? PeakOf(WreckFilm film, int player)
    {
        if (film.Cut.FirstOrDefault(s => s.Kind == ShotKind.Player && s.Subject == player) is not { } shot
            || !film.Peaks.TryGetValue(player, out var peak))
            return null;
        // The shot's time eases (slowest at the peak) but only ever runs forward: halve towards the moment it shows the peak.
        double target = Math.Clamp(peak.At, shot.From, shot.To), lo = 0, hi = shot.Real;
        for (int i = 0; i < 40 && hi - lo > 1e-6; i++)
        {
            double mid = (lo + hi) / 2;
            if (shot.At(mid) < target)
                lo = mid;
            else
                hi = mid;
        }
        double into = (lo + hi) / 2;
        return new FilmPeak(film, shot, into, shot.At(into));
    }

    /// <summary>
    /// Sets <paramref name="scene"/> to draw the film at a peak as the film itself draws that frame: its cars (returned,
    /// over <paramref name="live"/>'s shapes), the crew as ragdolls and nobody else, the cutaway and the light rig for the
    /// shot's camera, and none of the live wreck's dust.
    /// </summary>
    public static CarFrame[] Stage(GreyboxScene scene, FilmPeak p, IReadOnlyList<CarFrame> live)
    {
        var frames = FilmFrames(p.Film, p.Recorded, live);
        var eye = p.Camera.Position;
        scene.Bodies = FilmBodies(p.Film, p.Recorded);
        scene.Crew = [];
        scene.CutAway = FilmCutAway(p.Film, p.Shot, p.Recorded, frames, eye);
        scene.Lights = FilmLights(p.Film, p.Shot, p.Recorded, eye);
        scene.Wreck = null;
        scene.Derailed = true;
        return frames;
    }

    /// <summary>Seconds into the orbit (T117's <see cref="Views.Wreck"/>), after the first person and the replay.</summary>
    public static double OrbitSeconds(WreckTuning t, double seconds) => Math.Max(0, seconds - t.FirstPersonSeconds - t.ReplaySeconds);

    /// <summary>
    /// Every frame: what's drawn now, at <paramref name="now"/> (the session's own seconds, smooth between ticks). Before
    /// the train's off, the last few seconds are kept; the frame it comes off, where the eye was is taken in the car it
    /// was in (<paramref name="car"/>, or −1 off the train), with the camera it had (<paramref name="eye"/>).
    /// </summary>
    public void Record(double now, IReadOnlyList<CarFrame> frames, IReadOnlyList<Crewmate>? crew, bool derailed, Camera eye, int car, WreckTuning t)
    {
        if (!derailed)
        {
            if (!double.IsNaN(_derailedAt))
                Reset();
            _shots.Add(new Shot(now, [.. frames], crew is null ? [] : [.. crew]));
            int old = _shots.FindIndex(s => s.At >= now - t.ReplayLeadSeconds - 1);
            if (old > 0)
                _shots.RemoveRange(0, old);
            return;
        }
        if (double.IsNaN(_derailedAt))
        {
            _derailedAt = now;
            _worldEye = eye;
            _car = car >= 0 && car < frames.Count ? car : -1;
            if (_car >= 0)
            {
                var f = frames[_car];
                _eyeLocal = Local(f, eye.Position - f.Origin);
                var fwd = eye.Forward;
                _lookLocal = Local(f, new Double3(fwd.X, fwd.Y, fwd.Z));
            }
        }
        // Kept until the replay's through with it: it runs this far behind.
        if (now - _derailedAt <= t.FirstPersonSeconds + t.ReplaySeconds)
            _shots.Add(new Shot(now, [.. frames], crew is null ? [] : [.. crew]));
    }

    public void Reset()
    {
        _shots.Clear();
        _derailedAt = double.NaN;
        _car = -1;
    }

    static Double3 Local(in CarFrame f, Double3 v) => new(Double3.Dot(v, f.Right), Double3.Dot(v, f.Up), Double3.Dot(v, f.Back));

    static Double3 World(in CarFrame f, Double3 v) => f.Right * v.X + f.Up * v.Y + f.Back * v.Z;

    /// <summary>
    /// The first beat's camera: your eye, carried by your car as the wreck throws it about, rolling with it (the camera's
    /// whole orientation, so a car going over takes the horizon with it). Off the train, where you stood, watching.
    /// </summary>
    public Camera FirstPerson(IReadOnlyList<CarFrame> frames)
    {
        if (_car < 0 || _car >= frames.Count)
            return _worldEye;
        return Riding(frames[_car], _eyeLocal, _lookLocal, _worldEye.FovYDegrees > 0 ? _worldEye.FovYDegrees : 70);
    }

    /// <summary>
    /// An eye at <paramref name="eyeLocal"/> in a car, looking along <paramref name="lookLocal"/> in it, carried and rolled
    /// with the car however it lies: the first beat's camera, and a derailment's bookmarks (GDD v1.4 App. D.12).
    /// </summary>
    public static Camera Riding(in CarFrame f, Double3 eyeLocal, Double3 lookLocal, float fovY = 70)
    {
        var eye = f.Origin + World(f, eyeLocal);
        var fwd = World(f, lookLocal).Normalized;
        var back = fwd * -1;
        var right = Double3.Cross(f.Up, back);
        right = right.Length > 1e-6 ? right.Normalized : f.Right;
        var up = Double3.Cross(back, right).Normalized;
        var m = new Matrix4x4(
            (float)right.X, (float)right.Y, (float)right.Z, 0,
            (float)up.X, (float)up.Y, (float)up.Z, 0,
            (float)back.X, (float)back.Y, (float)back.Z, 0,
            0, 0, 0, 1);
        var cam = Camera.LookAt(eye, eye + fwd, fovY);
        cam.Orientation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m));
        return cam;
    }

    /// <summary>
    /// The replay's frame at <paramref name="seconds"/> into the sequence: the train as it was drawn, from
    /// <see cref="WreckTuning.ReplayLeadSeconds"/> before it came off. Null if nothing was kept (a client that joined mid-wreck).
    /// </summary>
    public (CarFrame[] Frames, Crewmate[] Crew, bool Off)? ReplayAt(double seconds, WreckTuning t)
    {
        if (_shots.Count == 0 || double.IsNaN(_derailedAt))
            return null;
        double at = _derailedAt - t.ReplayLeadSeconds + (seconds - t.FirstPersonSeconds);
        int i = _shots.FindIndex(s => s.At >= at);
        var shot = i < 0 ? _shots[^1] : _shots[i];
        return (shot.Frames, shot.Crew, shot.At >= _derailedAt);
    }

    /// <summary>
    /// The replay's camera: the chase view (above and behind the last car, on the middle of the train) riding with the
    /// train until the moment it came off, and from there held where it was, turning to keep the middle of the train in
    /// frame as it ploughs on and piles up.
    /// </summary>
    /// <param name="standing">A switchyard's standing cars (TrainOnLine.StandingCar), which the shot leaves out.</param>
    /// <param name="off">The frame shown is from the moment it came off or after (<see cref="ReplayAt"/>'s Off); before it,
    /// the camera rides with the train. (It used to be held from the replay's first frame, so the train ran in from behind
    /// it and the whole run-in was lost in the fog: note 251.)</param>
    /// <param name="ground">The land's height at (x, z), to keep the wide off a hillside; null, not checked.</param>
    public Camera ReplayCamera(CarFrame[] now, Func<int, bool> standing, bool off = true, Func<double, double, double>? ground = null)
    {
        now = Views.Train(now, standing);
        var mid = now[now.Length / 2].ToWorld(new Double3(0, 2, 0));
        if (!off || _shots.FindLast(s => s.At < _derailedAt) is not { Frames.Length: > 0 } before)
            return Camera.LookAt(Views.Chase(now).Position, mid, 60);
        // Off the rails (note 579; the director, 9 Oct 2026: "show the derailment happening in slow motion ... cars basically
        // being blasted off the tracks"): cut in from the chase view, 80 m back in the fog, to a wide side on, out on the side
        // the train's thrown and a little ahead of where it came off, on the front of the train going up in the air. It's
        // carried along with the front's travel (not its tumbling), so the pile-up stays in frame.
        var then = Views.Train(before.Frames, standing);
        int front = Math.Max(1, then.Length / 3);
        Double3 Front(CarFrame[] f) => Enumerable.Range(0, Math.Min(front, f.Length)).Aggregate(Double3.Zero, (a, i) => a + f[i].Origin) * (1.0 / Math.Min(front, f.Length));
        var frontThen = Front(then);
        var frontNow = Front(now);
        var along = then[0].Back * -1;
        var across = then[0].Right;
        // The side it went: where the front's gone across its line since; on the right if it hasn't yet.
        double thrown = Double3.Dot(frontNow - frontThen, across);
        var outward = thrown < -0.5 ? across * -1 : across;
        var eye = frontThen + outward * ReplayWideOut + along * ReplayWideAhead + Double3.Up * ReplayWideUp + (frontNow - frontThen) with { Y = 0 };
        if (ground is not null)
            eye = eye with { Y = Math.Max(eye.Y, ground(eye.X, eye.Z) + 3) };
        var look = Double3.Lerp(frontNow, mid, 0.3) + Double3.Up * 2;
        return Camera.LookAt(eye, look, 55);
    }

    static Func<double, double, double> Ground(TrainOnLine train)
    {
        double hint = train.Dynamics.Distance;
        return (x, z) => Sim.Player.PlayerMotor.GroundAt(new Double3(x, 0, z), train.Line, ref hint);
    }

    /// <summary>The replay's wide once the train's off (note 579): this far out from the front of the train, ahead, and up (m).</summary>
    const double ReplayWideOut = 30, ReplayWideAhead = 12, ReplayWideUp = 9;
}
