using System.Numerics;
using Ballast.Render;
using Ballast.Xr;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Game;

public enum VrTurn : byte { Snap, Smooth }

/// <summary>Mirror of content/tuning/vr.json.</summary>
public sealed record VrTuning
{
    public const string File = "tuning/vr.json";
    public VrTurn Turn { get; init; } = VrTurn.Snap;
    public double SnapDegrees { get; init; } = 45;
    public double SmoothDegreesPerSecond { get; init; } = 120;
    public float SnapEngage { get; init; } = 0.7f;
    public float SnapRelease { get; init; } = 0.35f;
    public float StickDeadzone { get; init; } = 0.2f;
    public VignetteTuning Vignette { get; init; } = new();
}

public sealed record VignetteTuning
{
    public bool Enabled { get; init; } = true;
    public float Moving { get; init; } = 0.55f;
    public float Turning { get; init; } = 0.8f;
    public float Snap { get; init; } = 0.9f;
    public float Inner { get; init; } = 0.5f;
    public double RiseSeconds { get; init; } = 0.12;
    public double FallSeconds { get; init; } = 0.35;
}

/// <summary>
/// The headset player's side of "clients send intent" (roadmap M4): controllers and head become the same
/// <see cref="PlayerIntent"/> a keyboard makes, so the host can't tell and nothing in the sim knows about VR.
/// <para>
/// There are two yaws. The sim's is where the player looks: it follows the head, so guns, reach and what the crew see
/// all agree with the headset. The tracking space's (<see cref="BodyYaw"/>) is where the room points on the car: only
/// the stick turns it, on this machine, at the display's rate. The eyes are placed from that one, never from the sim's
/// (which moves at the tick rate and would make the world swim). Walking is head-relative for free: the stick moves
/// the sim player along its look.
/// </para>
/// </summary>
public sealed class VrLocomotion(VrTuning tuning)
{
    int _parent;
    double _heading;
    byte _placed;
    bool _known, _snapHeld, _running;

    public VrTuning Tuning { get; } = tuning;

    /// <summary>Radians: the tracking space's facing in the player's parent frame (0 faces its −Z, positive is left).</summary>
    public double BodyYaw { get; private set; }

    /// <summary>The head in the tracking space, as of the last display frame.</summary>
    public Quaternion Head { get; private set; } = Quaternion.Identity;

    /// <summary>How far the comfort vignette has closed in, 0..1.</summary>
    public float Vignette { get; private set; }

    /// <summary>Snap turns so far (for the headless check).</summary>
    public int Snaps { get; private set; }

    /// <summary>Once per display frame: the stick turns the body, and the vignette follows what the sticks are doing.</summary>
    /// <param name="head">The head (either eye will do: they share an orientation) in the tracking space.</param>
    public void Frame(in XrControllerState c, Quaternion head, double dt)
    {
        Head = head;
        float turn = Deadzone(c.Turn.X);
        float target = 0;
        if (Tuning.Turn == VrTurn.Smooth)
        {
            // Stick right turns right, which is negative yaw (the mouse's sign).
            BodyYaw = Wrap(BodyYaw - turn * Tuning.SmoothDegreesPerSecond * Math.PI / 180 * dt);
            target = Tuning.Vignette.Turning * MathF.Abs(turn);
        }
        else if (!_snapHeld && MathF.Abs(c.Turn.X) >= Tuning.SnapEngage)
        {
            BodyYaw = Wrap(BodyYaw - MathF.Sign(c.Turn.X) * Tuning.SnapDegrees * Math.PI / 180);
            _snapHeld = true;
            Snaps++;
            // A snap is a cut, not a motion: the vignette blinks shut on it and opens again.
            if (Tuning.Vignette.Enabled)
                Vignette = MathF.Max(Vignette, Tuning.Vignette.Snap);
        }
        else if (_snapHeld && MathF.Abs(c.Turn.X) <= Tuning.SnapRelease)
            _snapHeld = false;

        var move = Deadzone(c.Move);
        target = MathF.Max(target, Tuning.Vignette.Moving * MathF.Min(1, move.Length()));
        if (!Tuning.Vignette.Enabled)
        {
            Vignette = 0;
            return;
        }
        double tau = target > Vignette ? Tuning.Vignette.RiseSeconds : Tuning.Vignette.FallSeconds;
        Vignette += (float)((target - Vignette) * (1 - Math.Exp(-dt / Math.Max(1e-3, tau))));
    }

    /// <summary>Turns the room by <paramref name="radians"/> (the mouse, alongside a headset).</summary>
    public void Turn(double radians) => BodyYaw = Wrap(BodyYaw + radians);

    /// <summary>
    /// Keeps the room where it was when the sim moves the player between frames (off a car onto the ground, across a
    /// curve's couplers), and takes the sim's facing when it places them (a respawn, dropping in at a POI).
    /// </summary>
    /// <param name="heading">The world heading of <paramref name="self"/>'s parent frame (0 for the ground).</param>
    public void Follow(in PlayerState self, double heading)
    {
        if (!_known || self.Placed != _placed)
            BodyYaw = Wrap(self.Yaw - Angles(Head).Yaw);
        else if (self.Parent != _parent)
            BodyYaw = Wrap(BodyYaw + _heading - heading);
        _known = true;
        _parent = self.Parent;
        _heading = heading;
        _placed = self.Placed;
    }

    /// <summary>This tick's intent: look where the head looks, walk where the stick points, act with the buttons.</summary>
    /// <remarks>
    /// Look is still a delta (the host clamps it and bots use it the same way): the one that turns the player, as
    /// this machine last predicted them, to face where the head is.
    /// </remarks>
    public PlayerIntent Intent(in PlayerState self, in XrControllerState c)
    {
        var (yaw, pitch) = Angles(Head);
        var move = Deadzone(c.Move);
        if (move.LengthSquared() > 1)
            move = Vector2.Normalize(move);
        // Clicking the stick runs until it's let go back to the centre: holding a click down while steering is a cramp.
        if (move == Vector2.Zero)
            _running = false;
        else if (c.Run)
            _running = true;

        var buttons = PlayerButtons.None;
        if (_running) buttons |= PlayerButtons.Run;
        if (c.Primary) buttons |= PlayerButtons.Jump;
        if (c.Grip) buttons |= PlayerButtons.Use;
        if (c.Trigger) buttons |= PlayerButtons.Fire;
        if (c.Secondary) buttons |= PlayerButtons.Throw;
        return new PlayerIntent
        {
            MoveX = move.X,
            MoveZ = move.Y,
            LookYaw = (float)Wrap(BodyYaw + yaw - self.Yaw),
            LookPitch = (float)(pitch - self.Pitch),
            Buttons = buttons,
        };
    }

    /// <summary>The body the eyes hang off: the eye point, turned to the tracking space's facing in the world.</summary>
    /// <param name="heading">The world heading of the player's parent frame, as passed to <see cref="Follow"/>.</param>
    public Camera Body(in Camera eye, double heading) => eye with { Yaw = BodyYaw + heading, Pitch = 0 };

    /// <summary>A pose's yaw and pitch in the frame it's given in, by where its −Z points.</summary>
    public static (double Yaw, double Pitch) Angles(Quaternion q)
    {
        var f = Vector3.Transform(-Vector3.UnitZ, q);
        return (Math.Atan2(-f.X, -f.Z), Math.Asin(Math.Clamp(f.Y, -1, 1)));
    }

    float Deadzone(float v) => MathF.Abs(v) <= Tuning.StickDeadzone ? 0 : MathF.Sign(v) * (MathF.Abs(v) - Tuning.StickDeadzone) / (1 - Tuning.StickDeadzone);

    /// <summary>A radial deadzone, rescaled so the stick still reaches full travel just past it.</summary>
    Vector2 Deadzone(Vector2 v)
    {
        float length = v.Length();
        return length <= Tuning.StickDeadzone ? Vector2.Zero : v / length * ((length - Tuning.StickDeadzone) / (1 - Tuning.StickDeadzone));
    }

    static double Wrap(double a) => Math.IEEERemainder(a, 2 * Math.PI);
}
