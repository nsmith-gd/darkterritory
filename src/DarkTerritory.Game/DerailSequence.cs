using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>The derailment's beats (T121 playtest): in your own eyes, the replay from the chase view, then the orbit.</summary>
public enum DerailBeat : byte { None, FirstPerson, Replay, Orbit }

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

    public static DerailBeat Beat(WreckTuning t, double seconds) =>
        seconds < 0 ? DerailBeat.None
        : seconds < t.FirstPersonSeconds ? DerailBeat.FirstPerson
        : seconds < t.FirstPersonSeconds + t.ReplaySeconds ? DerailBeat.Replay
        : seconds < t.SequenceSeconds ? DerailBeat.Orbit
        : DerailBeat.None;

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
    public Camera ReplayCamera(CarFrame[] now)
    {
        var at = _shots.FindLast(s => s.At < _derailedAt) is { Frames.Length: > 0 } before ? before.Frames : now;
        var held = Views.Chase(at);
        var mid = now[now.Length / 2].ToWorld(new Double3(0, 2, 0));
        var shot = Views.Chase(now);
        return Camera.LookAt(ReferenceEquals(at, now) ? shot.Position : held.Position, mid, 60);
    }
}
