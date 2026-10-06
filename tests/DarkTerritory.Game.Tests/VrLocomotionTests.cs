using System.Numerics;
using Ballast;
using Ballast.Render;
using Ballast.Xr;
using DarkTerritory.Game;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Game.Tests;

/// <summary>Headset controls and comfort (T26): controllers and head to intent, turning, and the vignette.</summary>
public class VrLocomotionTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly VrTuning Tuning = DataFile.Load<VrTuning>(Path.Combine(Content, VrTuning.File));
    const double Frame = 1 / 90.0;

    static Quaternion Yawed(double degrees) => Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)(degrees * Math.PI / 180));
    static Quaternion Pitched(double degrees) => Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)(degrees * Math.PI / 180));
    static double Deg(double radians) => radians * 180 / Math.PI;

    static void Hold(VrLocomotion v, XrControllerState c, double seconds, Quaternion? head = null)
    {
        for (double t = 0; t < seconds - 1e-9; t += Frame)
            v.Frame(c, head ?? v.Head, Frame);
    }

    [Fact]
    public void TheTuningLoadsAsWritten()
    {
        Assert.Equal(VrTurn.Snap, Tuning.Turn);
        Assert.Equal(45, Tuning.SnapDegrees);
        Assert.True(Tuning.Vignette.Enabled);
    }

    [Fact]
    public void TheHeadsHeightGoesWithTheHands()
    {
        // T82: crouched 0.6 m under where the session began (the eye point), the head's height over the feet goes out with
        // the hands; with no hand tracked, nothing does.
        var v = new VrLocomotion(Tuning);
        var c = new XrControllerState { Right = new XrHand(true, new Vector3(0.2f, -0.9f, -0.3f), Quaternion.Identity) };
        v.Frame(c, Quaternion.Identity, Frame, new Vector3(0, -0.6f, 0));
        var intent = v.Intent(new PlayerState(), c);
        Assert.True(intent.Has(PlayerButtons.Hand));
        Assert.Equal(PlayerIntent.Centimetres(Eyes.Height - 0.6), intent.Head);
        Assert.Equal(0, v.Intent(new PlayerState(), default).Head);
    }

    [Fact]
    public void ASnapTurnsOncePerPush()
    {
        var v = new VrLocomotion(Tuning);
        var right = new XrControllerState { Turn = new Vector2(1, 0) };
        Hold(v, right, 0.5);
        Assert.Equal(-45, Deg(v.BodyYaw), 6);
        // Held over: nothing more until it comes back to the middle.
        Hold(v, right with { Turn = new Vector2(0.5f, 0) }, 0.2);
        Hold(v, right, 0.2);
        Assert.Equal(-45, Deg(v.BodyYaw), 6);
        Hold(v, default, 0.1);
        Hold(v, right, 0.1);
        Assert.Equal(-90, Deg(v.BodyYaw), 6);
        Hold(v, default, 0.1);
        Hold(v, new XrControllerState { Turn = new Vector2(-0.9f, 0) }, 0.1);
        Assert.Equal(-45, Deg(v.BodyYaw), 6);
        Assert.Equal(3, v.Snaps);
    }

    [Fact]
    public void ASmoothTurnGoesAtTheStick()
    {
        var v = new VrLocomotion(Tuning with { Turn = VrTurn.Smooth });
        Hold(v, new XrControllerState { Turn = new Vector2(1, 0) }, 0.5);
        Assert.Equal(-Tuning.SmoothDegreesPerSecond / 2, Deg(v.BodyYaw), 1);
        // Drift inside the deadzone turns nothing.
        double before = v.BodyYaw;
        Hold(v, new XrControllerState { Turn = new Vector2(Tuning.StickDeadzone * 0.9f, 0) }, 1);
        Assert.Equal(before, v.BodyYaw, 9);
    }

    [Fact]
    public void TheSimLooksWhereTheHeadLooks()
    {
        var v = new VrLocomotion(Tuning);
        var self = new PlayerState { Parent = 2, Yaw = 0.3, Pitch = 0.1 };
        v.Follow(self, heading: 0.7);
        // The room starts facing the way the player did.
        Assert.Equal(0.3, v.BodyYaw, 9);

        v.Frame(default, Quaternion.Concatenate(Pitched(-20), Yawed(40)), Frame);
        var intent = v.Intent(self, default);
        Assert.Equal(40, Deg(intent.LookYaw), 3);
        Assert.Equal(-20 - Deg(0.1), Deg(intent.LookPitch), 3);

        // Once the sim has turned the player, the head asks for nothing more: no drift, no overshoot.
        self.Yaw += intent.LookYaw;
        self.Pitch += intent.LookPitch;
        var again = v.Intent(self, default);
        Assert.Equal(0, again.LookYaw, 5);
        Assert.Equal(0, again.LookPitch, 5);
        // The eyes hang off the room, not the sim: turning the head doesn't turn the body.
        var body = v.Body(new Camera { Yaw = 99, Pitch = 1 }, heading: 0.7);
        Assert.Equal(0.3 + 0.7, body.Yaw, 9);
        Assert.Equal(0, body.Pitch);
    }

    [Fact]
    public void TheSticksAndButtonsBecomeTheKeyboardsIntent()
    {
        var v = new VrLocomotion(Tuning);
        var self = new PlayerState();
        v.Follow(self, 0);
        var walk = v.Intent(self, new XrControllerState { Move = new Vector2(0, 1) });
        Assert.Equal(0, walk.MoveX, 5);
        Assert.Equal(1, walk.MoveZ, 5);
        // Diagonal is no faster than straight.
        var diagonal = v.Intent(self, new XrControllerState { Move = new Vector2(1, 1) });
        Assert.Equal(1, new Vector2(diagonal.MoveX, diagonal.MoveZ).Length(), 4);
        Assert.Equal(PlayerButtons.None, v.Intent(self, new XrControllerState { Move = new Vector2(0.1f, 0.12f) }).Buttons);
        Assert.Equal(0, v.Intent(self, new XrControllerState { Move = new Vector2(0.1f, 0.12f) }).MoveZ);

        var all = v.Intent(self, new XrControllerState { Grip = true, Trigger = true, Primary = true, Secondary = true });
        Assert.Equal(PlayerButtons.Use | PlayerButtons.Fire | PlayerButtons.Jump | PlayerButtons.Throw, all.Buttons);
    }

    [Fact]
    public void AStickClickRunsUntilTheStickIsLetGo()
    {
        var v = new VrLocomotion(Tuning);
        var self = new PlayerState();
        var forward = new XrControllerState { Move = new Vector2(0, 1) };
        Assert.False(v.Intent(self, forward).Has(PlayerButtons.Run));
        Assert.True(v.Intent(self, forward with { Run = true }).Has(PlayerButtons.Run));
        Assert.True(v.Intent(self, forward).Has(PlayerButtons.Run));
        Assert.False(v.Intent(self, default).Has(PlayerButtons.Run));
        Assert.False(v.Intent(self, forward).Has(PlayerButtons.Run));
    }

    [Fact]
    public void TheRoomStaysPutWhenThePlayerChangesFrame()
    {
        var v = new VrLocomotion(Tuning);
        var self = new PlayerState { Parent = 3, Yaw = 0.2 };
        v.Follow(self, heading: 1.1);
        double world = v.BodyYaw + 1.1;
        // Off the car onto the ground: the sim re-expresses the player's yaw; the room keeps its world facing.
        v.Follow(self with { Parent = PlayerState.World, Yaw = 1.3 }, heading: 0);
        Assert.Equal(world, v.BodyYaw, 9);
    }

    [Fact]
    public void APlacementTakesTheSimsFacing()
    {
        var v = new VrLocomotion(Tuning);
        var self = new PlayerState { Yaw = 0.2 };
        v.Follow(self, 0);
        v.Frame(default, Yawed(30), Frame);
        // A respawn faces the player down the car; the room turns so the head (still 30° off it) faces that way.
        v.Follow(self with { Yaw = 2.0, Placed = 1 }, 0);
        Assert.Equal(2.0 - 30 * Math.PI / 180, v.BodyYaw, 6);
        Assert.Equal(0, v.Intent(self with { Yaw = 2.0 }, default).LookYaw, 5);
    }

    [Fact]
    public void TheVignetteClosesWhileTheStickMovesYouAndOpensAfter()
    {
        var v = new VrLocomotion(Tuning);
        Hold(v, new XrControllerState { Move = new Vector2(0, 1) }, 1);
        Assert.Equal(Tuning.Vignette.Moving, v.Vignette, 2);
        Hold(v, default, 2);
        Assert.True(v.Vignette < 0.01f, $"still {v.Vignette} after stopping");
        // A snap blinks it shut for a moment.
        v.Frame(new XrControllerState { Turn = new Vector2(1, 0) }, Quaternion.Identity, Frame);
        Assert.True(v.Vignette > Tuning.Vignette.Snap * 0.9f, $"only {v.Vignette} on a snap");

        var off = new VrLocomotion(Tuning with { Vignette = Tuning.Vignette with { Enabled = false } });
        Hold(off, new XrControllerState { Move = new Vector2(0, 1), Turn = new Vector2(1, 0) }, 1);
        Assert.Equal(0, off.Vignette);
    }

    [Fact]
    public void TheVignetteIsClearInTheMiddleAndDarkAtTheEdge()
    {
        var o = new Overlay();
        o.Vignette(200, 100, 100, 50, inner: 0.5f, strength: 0.8f);
        Assert.True(o.Count > 0);
        // Every vertex nearer the middle than the clear ellipse is clear; the frame's edge is at full strength.
        foreach (var v in o.Vertices)
        {
            float r = new Vector2((v.Position.X - 100) / 100, (v.Position.Y - 50) / 50).Length();
            if (r < 0.49f)
                Assert.Equal(0, v.Colour.W);
            if (r > 0.99f)
                Assert.Equal(0.8f, v.Colour.W, 4);
        }
        // Reaching past every corner, even centred off to one side as a headset eye is.
        o.Clear();
        o.Vignette(200, 100, 130, 50, 0.5f, 0.8f);
        Assert.Contains(o.Vertices, v => v.Position.X < 0 && v.Position.Y < 0);
        Assert.Contains(o.Vertices, v => v.Position.X > 200 && v.Position.Y > 100);
        o.Clear();
        o.Vignette(200, 100, 100, 50, 0.5f, 0);
        Assert.Equal(0, o.Count);
    }

    [Fact]
    public void TurnAroundAndWalkDownTheCar()
    {
        // End to end through the same path the keyboard takes: four snaps and the stick walk the player the other way.
        var s = new PrototypeSession(Content, "test-loop", 4);
        s.Respawn(2);
        var v = new VrLocomotion(Tuning);
        var start = s.Player;
        v.Follow(start, Eyes.Heading(start, s.Train.Frames));
        var right = new XrControllerState { Turn = new Vector2(1, 0) };
        for (int i = 0; i < 4; i++)
        {
            v.Frame(right, Quaternion.Identity, Frame);
            v.Frame(default, Quaternion.Identity, Frame);
        }
        for (int tick = 0; tick < 45; tick++)
        {
            v.Follow(s.Player, Eyes.Heading(s.Player, s.Train.Frames));
            s.Step(v.Intent(s.Player, new XrControllerState { Move = new Vector2(0, 1) }));
        }
        Assert.Equal(start.Parent, s.Player.Parent);
        Assert.Equal(Math.PI, Math.Abs(Math.IEEERemainder(s.Player.Yaw - start.Yaw, 2 * Math.PI)), 3);
        // Facing −Z of the car to start with, so turned round they walk towards +Z.
        Assert.True(s.Player.Position.Z - start.Position.Z > 1, $"walked {s.Player.Position.Z - start.Position.Z:0.00} m down the car");
        Assert.Equal(start.Position.X, s.Player.Position.X, 1);
    }
}
