using System.Numerics;
using Ballast;
using Ballast.Render;
using Ballast.Xr;
using DarkTerritory.Game;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>Stereo views (T21) and controllers (T26). The session test needs an OpenXR runtime: CI runs Monado's simulated headset.</summary>
public class VrTests
{
    static readonly string Content = DataFile.FindContentRoot();

    static Vector4 Clip(in Camera c, Vector3 p) => Vector4.Transform(new Vector4(p, 1), c.ViewProjection(1));

    [Fact]
    public void ACentredEyeSeesWhatTheFlatCameraSees()
    {
        var flat = new Camera { FovYDegrees = 70, Near = 0.1f, Far = 2000 };
        float half = 35 * MathF.PI / 180;
        var eye = new XrEye(0, Vector3.Zero, Quaternion.Identity, new EyeFov(-half, half, half, -half)).From(flat);
        foreach (var p in new[] { new Vector3(0.3f, 0.2f, -2), new Vector3(-5, -1, -40), new Vector3(0, 3, -7) })
        {
            var a = Clip(flat, p);
            var b = Clip(eye, p);
            Assert.Equal(a.X / a.W, b.X / b.W, 4);
            Assert.Equal(a.Y / a.W, b.Y / b.W, 4);
            Assert.Equal(a.Z / a.W, b.Z / b.W, 4);
        }
    }

    [Fact]
    public void TheEyesTurnWithTheBody()
    {
        // Turned 90° left, facing −X: the head's right (+X) now points along −Z.
        var body = new Camera { Yaw = Math.PI / 2, Near = 0.1f, Far = 2000 };
        var right = new XrEye(1, new Vector3(0.032f, 0, 0), Quaternion.Identity, new EyeFov(-0.8f, 0.8f, 0.8f, -0.8f)).From(body);
        Assert.Equal(0, right.EyeOffset.X, 5);
        Assert.Equal(-0.032f, right.EyeOffset.Z, 5);
        Assert.Equal(-1, right.Forward.X, 5);
        // Something straight ahead of the body is dead centre for a centred eye there.
        var ahead = Clip(right, right.EyeOffset + new Vector3(-10, 0, 0));
        Assert.Equal(0, ahead.X / ahead.W, 4);
    }

    [Fact]
    public void AnOffCentreFrustumPutsStraightAheadTowardsTheNose()
    {
        // A left eye sees further left than right: straight ahead is right of its image centre. Up is up (Vulkan's Y is down).
        var eye = new XrEye(0, Vector3.Zero, Quaternion.Identity, new EyeFov(-0.9f, 0.75f, 0.8f, -0.8f)).From(new Camera { Near = 0.1f, Far = 2000 });
        var ahead = Clip(eye, new Vector3(0, 0, -5));
        Assert.True(ahead.X / ahead.W > 0.05f);
        var above = Clip(eye, new Vector3(0, 1, -5));
        Assert.True(above.Y / above.W < 0);
    }

    [Fact]
    public void AHeadsetSessionDrawsBothEyes()
    {
        VrView vr;
        try
        {
            vr = VrView.Start("DarkTerritory.Game.Tests", renderScale: 0.25);
        }
        catch (XrUnavailableException e)
        {
            Assert.Skip($"no OpenXR headset here ({e.Message}); CI runs this against Monado's simulated one");
            return;
        }
        using (vr)
        {
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File)), 6, 1)), RailLine.Load(Path.Combine(Content, "lines/test-loop.json")), 1200);
            var body = Views.Get("roof", train, 2);
            var mesh = new MeshBuilder();
            new GreyboxScene { Time = 0.37 }.Build(mesh, train, body.Position);
            var lighting = Views.Lighting(train);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (vr.Session.FramesRendered < 5 && clock.Elapsed.TotalSeconds < 20)
                if (vr.Frame(mesh, body, lighting, lighting.FogColor) == XrFrameResult.Idle)
                    Thread.Sleep(5);
            Assert.Equal(5, vr.Session.FramesRendered);
            Assert.Contains(Silk.NET.OpenXR.SessionState.Focused, vr.Session.States);

            // Monado's simulated driver brings two Khronos simple controllers (tools/xr-sim.sh): the bindings take, and
            // the hands are drawn where the eyes can see them.
            if (vr.Headset.Runtime.Contains("Monado", StringComparison.OrdinalIgnoreCase))
            {
                var pads = vr.Session.Controllers;
                Assert.Equal("/interaction_profiles/khr/simple_controller", pads.Profile);
                Assert.True(pads.Left.Tracked && pads.Right.Tracked, "both simulated hands should be tracked");
                var yaw = Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)body.Yaw);
                foreach (var hand in new[] { pads.Left, pads.Right })
                {
                    var c = Clip(vr.LastEye(0), VrHands.Place(yaw, hand));
                    Assert.True(c.W > 0, "a hand behind the eye");
                    Assert.InRange(c.X / c.W, -1, 1);
                    Assert.InRange(c.Y / c.W, -1, 1);
                }
            }

            // Two eyes, two viewpoints a head's width apart, and two different pictures of the same train.
            double ipd = (vr.LastEye(1).EyeOffset - vr.LastEye(0).EyeOffset).Length();
            Assert.InRange(ipd, 0.05, 0.08);
            var (pixels, w, h) = vr.SideBySide(mesh, lighting, lighting.FogColor);
            int eye = w / 2;
            int differ = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < eye; x++)
                    if (Math.Abs(pixels[(y * w + x) * 4] - pixels[(y * w + x + eye) * 4]) > 8)
                        differ++;
            Assert.True(differ > eye * h / 100, $"the eyes should see the scene from different places ({differ} pixels differ)");

            vr.Session.RequestExit();
            for (clock.Restart(); clock.Elapsed.TotalSeconds < 5;)
                if (vr.Frame(mesh, body, lighting, lighting.FogColor) is var r && r == XrFrameResult.Exiting)
                    break;
                else if (r == XrFrameResult.Idle)
                    Thread.Sleep(5);
            Assert.Equal(Silk.NET.OpenXR.SessionState.Exiting, vr.Session.State);
        }
    }
}
