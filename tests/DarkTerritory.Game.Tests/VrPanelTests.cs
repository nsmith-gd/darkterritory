using System.Numerics;
using Ballast;
using Ballast.Render;
using Ballast.Xr;
using DarkTerritory.Game;

namespace DarkTerritory.Game.Tests;

/// <summary>The HUD and menus in the headset (T36): a panel ahead of the head, projected into each eye, and the menus from the controllers.</summary>
public class VrPanelTests
{
    static readonly VrTuning Vr = DataFile.Load<VrTuning>(Path.Combine(DataFile.FindContentRoot(), VrTuning.File));
    static readonly EyeFov Fov = new(-0.8f, 0.8f, 0.8f, -0.8f);
    const float EyeSize = 400;

    static Quaternion Yawed(double degrees) => Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)(degrees * Math.PI / 180));

    static Camera Eye(float x, Quaternion? head = null, double bodyYaw = 0) =>
        new XrEye(0, new Vector3(x, 0, 0), head ?? Quaternion.Identity, Fov).From(new Camera { Yaw = bodyYaw, Near = 0.05f, Far = 2000 });

    /// <summary>A dot at the middle of a 100×100 overlay, as it lands in an eye: its centre in that eye's pixels.</summary>
    static Vector2? Middle(VrPanel panel, in Camera eye, double bodyYaw = 0)
    {
        var dot = new Overlay();
        dot.Rect(49, 49, 2, 2, Vector4.One);
        var into = new Overlay();
        panel.Project(dot, 100, 100, eye, bodyYaw, EyeSize, EyeSize, into);
        if (into.Count == 0)
            return null;
        var sum = Vector2.Zero;
        foreach (var v in into.Vertices)
            sum += v.Position;
        return sum / into.Count;
    }

    [Fact]
    public void ThePanelHangsAheadAndBelowTheEyesAtItsDistance()
    {
        var tuning = Vr.Hud;
        var panel = new VrPanel(tuning);
        panel.Follow(Vector3.Zero, Quaternion.Identity, 0.01);
        var seen = Middle(panel, Eye(0))!.Value;
        // Straight ahead in x; below the middle by the drop at that distance (image y is down).
        Assert.Equal(EyeSize / 2, seen.X, 1);
        double below = tuning.Drop / tuning.Distance / Math.Tan(0.8) * EyeSize / 2;
        Assert.Equal(EyeSize / 2 + below, seen.Y, 1);
    }

    [Fact]
    public void EachEyeSeesItFromItsOwnSideSoItHasDepth()
    {
        var panel = new VrPanel(Vr.Hud);
        panel.Follow(Vector3.Zero, Quaternion.Identity, 0.01);
        const float halfIpd = 0.032f;
        float left = Middle(panel, Eye(-halfIpd))!.Value.X, right = Middle(panel, Eye(halfIpd))!.Value.X;
        // The left eye sees it right of its middle, the right eye left: converged on something at the panel's distance.
        Assert.True(left > EyeSize / 2 && right < EyeSize / 2);
        double disparity = 2 * halfIpd / Vr.Hud.Distance / Math.Tan(0.8) * EyeSize / 2;
        Assert.Equal(disparity, left - right, 1);
    }

    [Fact]
    public void ItWaitsForAGlanceButFollowsATurn()
    {
        var panel = new VrPanel(Vr.Hud);
        panel.Follow(Vector3.Zero, Quaternion.Identity, 0.01);
        // A glance aside: it stays put.
        for (int i = 0; i < 90; i++)
            panel.Follow(Vector3.Zero, Yawed(Vr.Hud.FollowDegrees - 5), 1 / 90.0);
        Assert.Equal(0, panel.Yaw, 6);
        // A real turn: it comes round, and is in front again (to within a couple of degrees, where it stops) in two seconds.
        for (int i = 0; i < 180; i++)
            panel.Follow(Vector3.Zero, Yawed(70), 1 / 90.0);
        Assert.InRange(panel.Yaw * 180 / Math.PI, 67.9, 70);
        // Looking at it there, it's in the middle of the view, near enough.
        var seen = Middle(panel, Eye(0, Yawed(70)))!.Value;
        Assert.InRange(seen.X, EyeSize / 2 - 8, EyeSize / 2 + 8);
        // And it stays there: no creeping after the head.
        double settled = panel.Yaw;
        for (int i = 0; i < 90; i++)
            panel.Follow(Vector3.Zero, Yawed(70), 1 / 90.0);
        Assert.Equal(settled, panel.Yaw);
    }

    [Fact]
    public void ItTurnsWithTheBodyAndIsNeverDrawnFromBehind()
    {
        var panel = new VrPanel(Vr.Hud);
        panel.Follow(Vector3.Zero, Quaternion.Identity, 0.01);
        // The body turned in the world (a snap, a curve): the panel's in the room, so it turns with it.
        Assert.Equal(EyeSize / 2, Middle(panel, Eye(0, bodyYaw: 1.2), bodyYaw: 1.2)!.Value.X, 1);
        // The head turned right round, away from it: nothing to draw (and nothing mirrored onto the view).
        Assert.Null(Middle(panel, Eye(0, Yawed(180))));
    }

    [Fact]
    public void TheSticksWorkTheMenusOnePushAtATime()
    {
        var keys = new VrMenuInput();
        var up = new XrControllerState { Move = new Vector2(0, 1) };
        Assert.Equal(VrMenuPress.Up, keys.Read(up));
        // Held, or wavering at the threshold, it's still the one push.
        Assert.Equal(VrMenuPress.None, keys.Read(up));
        Assert.Equal(VrMenuPress.None, keys.Read(new XrControllerState { Move = new Vector2(0, 0.5f) }));
        Assert.Equal(VrMenuPress.None, keys.Read(up));
        // Back to the middle, and again.
        Assert.Equal(VrMenuPress.None, keys.Read(default));
        Assert.Equal(VrMenuPress.Up, keys.Read(up));
        Assert.Equal(VrMenuPress.None, keys.Read(default));
        Assert.Equal(VrMenuPress.Select, keys.Read(new XrControllerState { Trigger = true }));
        Assert.Equal(VrMenuPress.None, keys.Read(new XrControllerState { Trigger = true }));
        Assert.Equal(VrMenuPress.Back, keys.Read(new XrControllerState { Secondary = true }));
    }
}
