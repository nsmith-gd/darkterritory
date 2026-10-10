using Ballast;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>
/// Held (note 580; the director, 9 Oct 2026: "When you are being held by something the camera should cut to third person and
/// show the thing holding you"): while the host has you <see cref="PlayerFlags.Held"/>, the camera leaves your eyes for a
/// held shot of you and what has you, side on, a little behind you and over you, both in frame; your own figure is drawn
/// (<see cref="Figure"/>) and your arms aren't. Back in your eyes the tick you're let go. Presentation only: the sim never
/// sees it, and a client reads the hold from what replicates (the flag and the holder's <see cref="Enemy.Holding"/>).
/// </summary>
public static class HeldShot
{
    /// <summary>The camera's field of view in the shot (degrees).</summary>
    public const float Fov = 62;

    /// <summary>The creature holding <paramref name="playerId"/>, if one is (any kind whose grab names its victim).</summary>
    public static Enemy? Holder(World world, int playerId) =>
        playerId < 0 ? null : world.ActiveEnemies.FirstOrDefault(e => e.Holding == playerId);

    /// <summary>Your hold as the camera has it: held, alive, and what has you. Null when you're your own eyes.</summary>
    public static (Double3 You, Double3 Holder, double Size)? Subjects(in PlayerState s, int playerId, World world, IReadOnlyList<CarFrame> frames)
    {
        if (!s.Alive || !s.Has(PlayerFlags.Held) || Holder(world, playerId) is not { } holder)
            return null;
        var (feet, _) = Eyes.World(s, frames);
        return (feet, holder.WorldPosition(world.Train), Size(holder.Kind));
    }

    /// <summary>
    /// How far a holder of this kind reaches round its middle (m), for framing: the Gannet's spread wings and the Moose's rack
    /// want the camera further off than a man-sized thing does.
    /// </summary>
    public static double Size(EnemyKind kind) => kind switch
    {
        EnemyKind.Gannet => 3.5,
        EnemyKind.Moose => 2.2,
        EnemyKind.CarHugger or EnemyKind.Whistler => 1.5,
        _ => 1.0,
    };

    /// <summary>The shot for this frame, or null when you aren't held (or nothing in the world says by what).</summary>
    public static Camera? For(IPlaySession s, IReadOnlyList<CarFrame> frames, Camera eyes)
    {
        if (Subjects(s.Player, s.PlayerId, s.World, frames) is not { } subjects)
            return null;
        var p = s.Player;
        CarFrame? car = p.Parent != PlayerState.World && p.Parent >= 0 && p.Parent < frames.Count && PlayerMotor.Indoors(p, s.Train) ? frames[p.Parent] : null;
        double hint = s.Train.Dynamics.Distance;
        var line = s.Train.Line;
        return Frame(subjects.You, subjects.Holder, eyes, car, subjects.Size, (x, z) => PlayerMotor.GroundAt(new Double3(x, 0, z), line, ref hint));
    }

    /// <summary>
    /// The framing itself: side on to the line from you to what has you, the camera on the side you had at your back, pulled
    /// back far enough for both, and a little over you. Indoors (<paramref name="car"/>), kept inside the car's room so the
    /// shot isn't of its wall; outdoors, kept off the ground (<paramref name="ground"/>). <paramref name="size"/>: <see cref="Size"/>.
    /// </summary>
    public static Camera Frame(Double3 you, Double3 holder, Camera eyes, CarFrame? car = null, double size = 1.0, Func<double, double, double>? ground = null)
    {
        var apart = (holder - you) with { Y = 0 };
        double gap = apart.Length;
        var forward = new Double3(-Math.Sin(eyes.Yaw), 0, -Math.Cos(eyes.Yaw));
        // On top of you (a pin, a swallow): the line is the way you were looking.
        var axis = gap > 0.3 ? apart * (1 / gap) : forward;
        var side = Double3.Cross(Double3.Up, axis);
        if (Double3.Dot(side, forward) > 0)
            side = side * -1;
        var target = Double3.Lerp(you, holder, gap > 0.3 ? 0.5 : 0) + Double3.Up * 1.0;
        double back = Math.Clamp(gap * 0.9 + 1.4 + 1.6 * size, 2.8, 12);
        var at = target + side * back - axis * (back * 0.35) + Double3.Up * (0.6 + 0.5 * size);
        if (car is { Shape.Interior: { } room } frame)
        {
            // In the car with you: the room's inside, a hand's width off its walls, floor and ceiling.
            const double Margin = 0.2;
            var local = frame.ToLocal(at);
            local = new Double3(
                Math.Clamp(local.X, room.Min.X + Margin, room.Max.X - Margin),
                Math.Clamp(local.Y, room.Min.Y + 1.0, room.Max.Y - Margin),
                Math.Clamp(local.Z, room.Min.Z + Margin, room.Max.Z - Margin));
            var kept = frame.ToWorld(local);
            // Pushed in off a wall, the camera's nearer you than it framed for: look at you, and past you at it.
            if ((kept - at).Length > 0.05)
                target = you + Double3.Up * 0.9 + (holder - you) * 0.3;
            at = kept;
        }
        else if (ground is not null)
            at = at with { Y = Math.Max(at.Y, ground(at.X, at.Z) + 1.2) };
        return Camera.LookAt(at, target, Fov) with { Near = eyes.Near, Far = eyes.Far };
    }

    /// <summary>Your own figure, drawn while the shot's on you (it isn't round your eyes otherwise).</summary>
    public static Crewmate? Figure(IPlaySession s, IReadOnlyList<CarFrame> frames) =>
        Subjects(s.Player, s.PlayerId, s.World, frames) is null ? null
            : CrewActs.Crewmate((byte)s.PlayerId, s.Player, s.World, frames, [s.Player]);
}
