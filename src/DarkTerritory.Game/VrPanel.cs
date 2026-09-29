using System.Numerics;
using Ballast.Render;
using Ballast.Xr;

namespace DarkTerritory.Game;

/// <summary>Where a panel floats in the headset (vr.json <c>hud</c>, <c>menu</c>).</summary>
public sealed record VrPanelTuning
{
    public double Distance { get; init; } = 1.6;
    public double Width { get; init; } = 1.8;
    public double Drop { get; init; } = 0.2;
    public double FollowDegrees { get; init; } = 25;
    public double FollowSeconds { get; init; } = 0.4;
}

/// <summary>
/// A flat panel floating ahead of the head (T36, roadmap M4): the HUD in a night, the menus in the front end. What's
/// on it is the flat screen's own <see cref="Overlay"/>, drawn as it always is, then carried onto the panel and
/// projected into each eye. So it sits at a real depth in both eyes, over the scene, and nothing about the HUD or the
/// menus has to know about headsets.
/// <para>
/// It follows the head lazily: its place moves with the head at once (leaning doesn't slide it away), but it only turns
/// once the head's looked more than <see cref="VrPanelTuning.FollowDegrees"/> off it, and then eases round. A panel
/// pinned to the face is the classic way to make people sick; one that waits can be glanced at.
/// </para>
/// </summary>
public sealed class VrPanel(VrPanelTuning tuning)
{
    Vector3 _head;
    double _yaw;
    bool _placed, _turning;

    public VrPanelTuning Tuning { get; } = tuning;

    /// <summary>Radians: which way the panel faces the head, in the tracking space (0 is its −Z).</summary>
    public double Yaw => _yaw;

    /// <summary>Once a frame: the head's place and facing in the tracking space.</summary>
    public void Follow(Vector3 head, Quaternion orientation, double dt)
    {
        _head = head;
        var (yaw, _) = VrLocomotion.Angles(orientation);
        if (!_placed)
        {
            (_yaw, _placed) = (yaw, true);
            return;
        }
        double off = Wrap(yaw - _yaw);
        if (Math.Abs(off) > Tuning.FollowDegrees * Math.PI / 180)
            _turning = true;
        if (!_turning)
            return;
        _yaw = Wrap(_yaw + off * (1 - Math.Exp(-dt / Math.Max(1e-3, Tuning.FollowSeconds))));
        // Round in front again: it stops, until the head goes off it once more.
        if (Math.Abs(Wrap(yaw - _yaw)) < 2 * Math.PI / 180)
            _turning = false;
    }

    /// <summary>Puts the panel straight ahead of the head again (a new screen, a respawn).</summary>
    public void Recentre() => _placed = false;

    /// <summary>
    /// Draws <paramref name="source"/> (a <paramref name="width"/> × <paramref name="height"/> overlay: the HUD, a menu)
    /// on the panel as <paramref name="eye"/> sees it, into <paramref name="into"/> in that eye's pixels. Triangles that
    /// fall behind the eye are left out.
    /// </summary>
    /// <param name="bodyYaw">The body's world yaw (<see cref="Camera.Yaw"/> of the body the eyes hang from).</param>
    public void Project(Overlay source, float width, float height, in Camera eye, double bodyYaw, float eyeWidth, float eyeHeight, Overlay into)
    {
        var facing = Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)_yaw);
        var right = Vector3.Transform(Vector3.UnitX, facing);
        var ahead = Vector3.Transform(-Vector3.UnitZ, facing);
        float w = (float)Tuning.Width, h = w * height / width;
        var centre = _head + ahead * (float)Tuning.Distance - Vector3.UnitY * (float)Tuning.Drop;
        var body = Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)bodyYaw);
        var viewProjection = eye.ViewProjection(eyeWidth / eyeHeight);
        var verts = source.Vertices;
        Span<OverlayVertex> tri = stackalloc OverlayVertex[3];
        for (int i = 0; i + 2 < verts.Count; i += 3)
        {
            bool visible = true;
            for (int k = 0; k < 3 && visible; k++)
            {
                var v = verts[i + k];
                var onPanel = centre + right * (w * (v.Position.X / width - 0.5f)) - Vector3.UnitY * (h * (v.Position.Y / height - 0.5f));
                var clip = Vector4.Transform(new Vector4(Vector3.Transform(onPanel, body), 1), viewProjection);
                if (clip.W < 0.05f)
                {
                    visible = false;
                    break;
                }
                // Vulkan's clip space: y is down, so the top of the image is −1.
                tri[k] = new OverlayVertex(new Vector2((clip.X / clip.W + 1) / 2 * eyeWidth, (clip.Y / clip.W + 1) / 2 * eyeHeight), v.Colour);
            }
            if (visible)
                into.Vertices.AddRange(tri);
        }
    }

    static double Wrap(double a) => Math.IEEERemainder(a, 2 * Math.PI);
}

/// <summary>The front end's keys, from controllers (T36): each press once, on the edge.</summary>
[Flags]
public enum VrMenuPress : byte { None = 0, Up = 1, Down = 2, Left = 4, Right = 8, Select = 16, Back = 32 }

/// <summary>
/// Menus from a headset's controllers (T36): the left stick moves through them (a push is one step; come back to the
/// middle for another), the trigger or A chooses, B goes back.
/// </summary>
public sealed class VrMenuInput
{
    const float Push = 0.6f, Centre = 0.3f;
    VrMenuPress _held;

    public VrMenuPress Read(in XrControllerState c)
    {
        var now = VrMenuPress.None;
        var stick = c.Move;
        if (stick.Y > Push) now |= VrMenuPress.Up;
        if (stick.Y < -Push) now |= VrMenuPress.Down;
        if (stick.X < -Push) now |= VrMenuPress.Left;
        if (stick.X > Push) now |= VrMenuPress.Right;
        if (c.Trigger || c.Primary) now |= VrMenuPress.Select;
        if (c.Secondary) now |= VrMenuPress.Back;
        var pressed = now & ~_held;
        // A stick direction stays held until the stick's back near the middle (no chatter at the threshold).
        var sticks = VrMenuPress.Up | VrMenuPress.Down | VrMenuPress.Left | VrMenuPress.Right;
        var still = MathF.Abs(stick.X) > Centre || MathF.Abs(stick.Y) > Centre ? _held & sticks : VrMenuPress.None;
        _held = now | still;
        return pressed;
    }

    /// <summary>Applies a press to the front end; returns what was chosen, if anything.</summary>
    public static Launch? Apply(VrMenuPress press, FrontEnd menu)
    {
        if (press.HasFlag(VrMenuPress.Up)) menu.Up();
        if (press.HasFlag(VrMenuPress.Down)) menu.Down();
        if (press.HasFlag(VrMenuPress.Left)) menu.Left();
        if (press.HasFlag(VrMenuPress.Right)) menu.Right();
        if (press.HasFlag(VrMenuPress.Back)) menu.Back();
        return press.HasFlag(VrMenuPress.Select) ? menu.Select() : null;
    }
}
