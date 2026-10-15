using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>
/// The views the dead watch the crew by (GDD App. D.10; the director, 14 Oct 2026: "a third and fourth camera option ... a
/// third person wide shot of the train, and ... a third person above and behind a little bit of ... a character"; note 743),
/// the view key (Tab) going round them in this order.
/// </summary>
public enum DeadView : byte
{
    /// <summary>The crewmate's own eyes (a body's watched over the shoulder: it has none).</summary>
    Eyes,
    /// <summary>The chase: up and back off the last car (<see cref="Views.Chase"/>).</summary>
    Chase,
    /// <summary>The whole train side on, from out beside the line.</summary>
    Wide,
    /// <summary>Above and a little behind whoever's watched; a body, from over it.</summary>
    Shoulder,
}

/// <summary>
/// The dead's cameras (note 743). Presentation only, each hung off a crewmate or the train (D.10: no free camera, no seeing
/// through walls): over a shoulder indoors stays inside the car's room. Whom you're watching is the session's
/// (<see cref="IPlaySession.Watching"/>), the living and, since note 743, the bodies of the dead.
/// </summary>
public static class DeadViews
{
    /// <summary>The third-person views' field of view (degrees).</summary>
    public const float Fov = 60;

    /// <summary>
    /// The night's fog in the wide, as a share of what's on the line: the whole train is 60 to 120 m off, where the line's
    /// fog would leave a grey smear of it; the dead are watching the train, not driving it.
    /// </summary>
    public const float WideFog = 0.35f;

    public static DeadView Next(DeadView view) => (DeadView)(((int)view + 1) % 4);

    public static string Name(DeadView view) => view switch
    {
        DeadView.Eyes => "EYES",
        DeadView.Chase => "CHASE",
        DeadView.Wide => "WIDE",
        _ => "OVER THE SHOULDER",
    };

    /// <summary>The body of the dead crewmate being watched (note 743), or null: watching the living, or nobody.</summary>
    public static Body? BodyOf(IPlaySession s) =>
        s.Watching < 0 || s.Viewpoint.Alive ? null
            : s.World.Bodies.All.FirstOrDefault(b => b.Kind == BodyKind.Ragdoll && b.Owner == s.Watching);

    /// <summary>
    /// The dead player's camera in <paramref name="view"/>, or null for the watched crewmate's own eyes (the session's
    /// <see cref="IPlaySession.EyeCamera"/>, <paramref name="eyes"/>): only the living have them.
    /// </summary>
    public static Camera? For(DeadView view, IPlaySession s, IReadOnlyList<CarFrame> frames, Camera eyes)
    {
        if (s.Watching < 0)
            return null;
        var body = BodyOf(s);
        if (view == DeadView.Eyes && s.Viewpoint.Alive)
            return null;
        double hint = s.Train.Dynamics.Distance;
        var line = s.Train.Line;
        double Ground(double x, double z) => PlayerMotor.GroundAt(new Double3(x, 0, z), line, ref hint);
        var train = Views.Train(frames, s.Train.StandingCar);
        Camera camera;
        if (view == DeadView.Chase)
            camera = Views.Chase(train);
        else if (view == DeadView.Wide)
            camera = Wide(train, Ground);
        else if (body is not null)
        {
            var room = body.Parent >= 0 && body.Parent < frames.Count ? frames[body.Parent] : (CarFrame?)null;
            camera = OverBody(Bodies.WorldCentre(body, s.Train), train[0].Back * -1, room, Ground);
        }
        else
        {
            var v = s.Viewpoint;
            var (feet, yaw) = Eyes.World(v, frames);
            var room = v.Parent >= 0 && v.Parent < frames.Count && PlayerMotor.Indoors(v, s.Train) ? frames[v.Parent] : (CarFrame?)null;
            camera = Shoulder(feet, yaw, room, Ground);
        }
        return camera with { Near = eyes.Near, Far = Math.Max(eyes.Far, camera.Far) };
    }

    /// <summary>
    /// The whole train side on: from out on its left, far enough off that it's all in frame (a 16:9 frame at
    /// <see cref="Fov"/> is about as wide as it is far off), and high over it, looking down at its middle.
    /// </summary>
    public static Camera Wide(IReadOnlyList<CarFrame> train, Func<double, double, double>? ground = null)
    {
        var front = train[0];
        var back = train[^1];
        var head = front.ToWorld(new Double3(0, 0, -front.Shape.HalfLength));
        var tail = back.ToWorld(new Double3(0, 0, back.Shape.HalfLength));
        var along = (head - tail) with { Y = 0 };
        double length = along.Length;
        along = length > 1 ? along * (1 / length) : (front.Back * -1) with { Y = 0 };
        var left = Double3.Cross(Double3.Up, along);
        left = left * (1 / Math.Max(1e-6, left.Length));
        var middle = (head + tail) * 0.5 + Double3.Up * 2;
        // Near as it can be with the ends in frame, and high: over the lineside trees, and less of the night's fog between.
        double off = Math.Max(24, length * 0.48 + 8);
        var at = middle + left * off + Double3.Up * (off * 0.5);
        if (ground is not null)
            at = at with { Y = Math.Max(at.Y, ground(at.X, at.Z) + 3) };
        return Camera.LookAt(at, middle, Fov) with { Far = (float)Math.Max(2000, off * 6) };
    }

    /// <summary>Above and a little behind a crewmate: the way they face ahead of them, them low in the frame.</summary>
    public static Camera Shoulder(Double3 feet, double yaw, CarFrame? room = null, Func<double, double, double>? ground = null)
    {
        var forward = new Double3(-Math.Sin(yaw), 0, -Math.Cos(yaw));
        var at = feet + Double3.Up * 2.5 - forward * 2.8;
        var target = feet + Double3.Up * 1.2 + forward * 3;
        return Placed(at, target, room, ground, feet + Double3.Up * 1.1);
    }

    /// <summary>Over a body where it lies, from up and behind it along the train's way, looking down on it.</summary>
    public static Camera OverBody(Double3 centre, Double3 ahead, CarFrame? room = null, Func<double, double, double>? ground = null)
    {
        var forward = ahead with { Y = 0 };
        forward = forward * (1 / Math.Max(1e-6, forward.Length));
        var at = centre + Double3.Up * 2.4 - forward * 2.4;
        return Placed(at, centre, room, ground, centre);
    }

    // Indoors, inside the car's room (a crewmate in a car is shot from in it, not through its roof); outdoors, off the ground.
    // Pushed in off a wall or the ceiling, the camera's nearer its subject than it framed for: it looks at the subject.
    static Camera Placed(Double3 at, Double3 target, CarFrame? room, Func<double, double, double>? ground, Double3 subject)
    {
        if (room is { Shape.Interior: { } box } frame && Inside(frame, box, subject))
        {
            var kept = HeldShot.InRoom(frame, box, at);
            if ((kept - at).Length > 0.05)
                target = subject;
            at = kept;
        }
        else if (ground is not null)
            at = at with { Y = Math.Max(at.Y, ground(at.X, at.Z) + 1.2) };
        return Camera.LookAt(at, target, Fov);
    }

    static bool Inside(CarFrame frame, Box box, Double3 world)
    {
        var l = frame.ToLocal(world);
        return l.X >= box.Min.X && l.X <= box.Max.X && l.Y >= box.Min.Y - 0.5 && l.Y <= box.Max.Y && l.Z >= box.Min.Z && l.Z <= box.Max.Z;
    }
}
