using System.Numerics;
using Ballast;
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
    /// <summary>The HUD's panel in a night (T36).</summary>
    public VrPanelTuning Hud { get; init; } = new();
    /// <summary>The menus' panel in the front end (T36).</summary>
    public VrPanelTuning Menu { get; init; } = new() { Distance = 2.0, Width = 2.0, Drop = 0, FollowDegrees = 35, FollowSeconds = 0.6 };
    /// <summary>A headset player's body as the rest of the crew see it (T82).</summary>
    public VrBodyTuning Body { get; init; } = new();
    /// <summary>How the eyes are drawn: both in one pass where the GPU can (multiview), or each alone (note 219).</summary>
    public StereoPath Stereo { get; init; } = StereoPath.Multiview;
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
    bool _known, _snapHeld, _running, _leftReaches, _leftWas, _rightWas;
    float? _pulledFrom;

    public VrTuning Tuning { get; } = tuning;

    /// <summary>Radians: the tracking space's facing in the player's parent frame (0 faces its −Z, positive is left).</summary>
    public double BodyYaw { get; private set; }

    /// <summary>The head in the tracking space, as of the last display frame.</summary>
    public Quaternion Head { get; private set; } = Quaternion.Identity;

    /// <summary>Where the head is in the tracking space (its origin is the eye point, <see cref="Eyes.Height"/> over the feet).</summary>
    public Vector3 HeadPosition { get; private set; }

    /// <summary>How far the comfort vignette has closed in, 0..1.</summary>
    public float Vignette { get; private set; }

    /// <summary>Snap turns so far (for the headless check).</summary>
    public int Snaps { get; private set; }

    /// <summary>
    /// The hand that reaches for things (T29): the last one to grip, the right until then. Reach is tested from it,
    /// so it's the one the sim hears about.
    /// </summary>
    public bool LeftReaches => _leftReaches;

    /// <summary>Once per display frame: the stick turns the body, and the vignette follows what the sticks are doing.</summary>
    /// <param name="head">The head (either eye will do: they share an orientation) in the tracking space.</param>
    /// <param name="headPosition">And where it is there (T82: its height goes out with the hands).</param>
    public void Frame(in XrControllerState c, Quaternion head, double dt, Vector3 headPosition = default)
    {
        Head = head;
        HeadPosition = headPosition;
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
    /// <para>
    /// The reaching hand goes with it (T29), from the feet in the frame the player will face: the tracking space hangs
    /// from the sim's eye point, so that's the hand's place in the room turned by the room's facing, less the look.
    /// On a ladder, a gripping hand pulled down climbs, at up to the ladder's climbing speed
    /// (<paramref name="ladderClimb"/>; 0 leaves climbing to the stick).
    /// </para>
    /// </remarks>
    public PlayerIntent Intent(in PlayerState self, in XrControllerState c, double ladderClimb = 0)
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
        var intent = new PlayerIntent
        {
            MoveX = move.X,
            MoveZ = move.Y,
            LookYaw = (float)Wrap(BodyYaw + yaw - self.Yaw),
            LookPitch = (float)(pitch - self.Pitch),
            Buttons = buttons,
        };

        // Whichever hand grips last is the one reaching.
        if (c.Left.Grip && !_leftWas) _leftReaches = true;
        else if (c.Right.Grip && !_rightWas) _leftReaches = false;
        _leftWas = c.Left.Grip;
        _rightWas = c.Right.Grip;
        var hand = _leftReaches ? c.Left : c.Right;
        var other = _leftReaches ? c.Right : c.Left;
        if (hand.Tracked)
        {
            // The other hand goes too (T43): a heavy crate's end takes both.
            double turn = BodyYaw - (self.Yaw + intent.LookYaw);
            // And the head's height over the feet (T82), so the crew see the body under it lean and crouch.
            intent.Reach(Reach(hand.Position, turn), other.Tracked ? Reach(other.Position, turn) : null, HeadPosition.Y + Eyes.Height);
            if (self.Surface == Surface.Ladder && hand.Grip && ladderClimb > 0)
            {
                // Hand over hand: the hand stays on its rung while the body goes up past it. Pushing the hand up
                // only climbs down slowly, never as far as letting go (Use with the stick back does that).
                float pulled = _pulledFrom is { } from ? from - hand.Position.Y : 0;
                float climb = (float)Math.Clamp(pulled / (ladderClimb * Sim.SimConstants.TickSeconds), -0.45, 1);
                if (MathF.Abs(climb) > MathF.Abs(intent.MoveZ))
                    intent.MoveZ = climb;
                _pulledFrom = hand.Position.Y;
            }
            else
                _pulledFrom = null;
        }
        return intent;
    }

    /// <summary>
    /// A hand's place in the tracking space as the sim takes it: from the feet (the room hangs from the eye point,
    /// <see cref="Eyes.Height"/> over them), turned by <paramref name="turn"/> radians into the frame the player faces.
    /// </summary>
    public static Double3 Reach(Vector3 hand, double turn)
    {
        double c = Math.Cos(turn), s = Math.Sin(turn);
        return new Double3(hand.X * c + hand.Z * s, hand.Y + Eyes.Height, -hand.X * s + hand.Z * c);
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
