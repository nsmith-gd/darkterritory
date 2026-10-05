using System.Numerics;
using Ballast.Render;
using Ballast.Xr;
using Vortice.Vulkan;

namespace DarkTerritory.Game;

/// <summary>
/// The game in a headset (T21, roadmap M4): an OpenXR session, a renderer per eye, and the flat camera as the
/// player's body. The eyes sit where the flat camera's eye point is, turned to its yaw; the head supplies the rest.
/// Both eyes draw the same mesh (it's built around the body's eye point), each from its own few centimetres off it:
/// in one pass where the GPU has multiview (one renderer, a layer an eye), or a renderer an eye where it hasn't
/// (<see cref="StereoPath"/>, tuning/vr.json; ARCHITECTURE §8 note 219).
/// A panel (the HUD, the menus: <see cref="VrPanel"/>) is projected into each eye's overlay, under the vignette.
/// </summary>
public sealed class VrView : IDisposable
{
    readonly GreyboxRenderer[] _eyes;
    readonly Camera[] _last = new Camera[2];
    readonly Overlay[] _overlays = [new(), new()];
    readonly Overlay?[] _lastOverlay = new Overlay?[2];
    Camera _lastBody;
    XrControllerState _lastControllers;
    readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    double _lastFrame, _lastPanel;

    VrView(XrHeadset headset, GpuContext gpu, XrStereoSession session, StereoPath stereo)
    {
        Headset = headset;
        Gpu = gpu;
        Session = session;
        Stereo = Choose(stereo, gpu);
        // At an eye's resolution the moon's shadow map needs no more than 1024, a quarter of the flat view's 2048 to fill.
        if (Stereo == StereoPath.Multiview)
            // Both eyes in one renderer: the shadow maps are drawn once, for the body.
            _eyes = [new(gpu, session.EyeWidth, session.EyeHeight, session.EyeFormat, moonShadowSize: 1024, views: 2)];
        else
        {
            // The left eye draws the shadow maps and the right samples them (the lamp's and the moon's views are the body's,
            // so they're the same for both: tuning/perf.json's budget, ARCHITECTURE §8 note 86).
            _eyes = [new(gpu, session.EyeWidth, session.EyeHeight, session.EyeFormat, moonShadowSize: 1024),
                new(gpu, session.EyeWidth, session.EyeHeight, session.EyeFormat, moonShadowSize: 1024)];
            _eyes[1].ShadowsFrom = _eyes[0];
        }
    }

    /// <summary>The path the eyes are drawn by: the one asked for, unless that's multiview and the device hasn't got it.</summary>
    public static StereoPath Choose(StereoPath wanted, GpuContext gpu) =>
        wanted == StereoPath.Multiview && gpu.Multiview ? StereoPath.Multiview : StereoPath.PerEye;

    /// <summary>How the eyes are being drawn (<see cref="Choose"/>).</summary>
    public StereoPath Stereo { get; }

    public XrHeadset Headset { get; }
    public GpuContext Gpu { get; }
    public XrStereoSession Session { get; }

    /// <summary>Finds the headset and starts a session. Throws <see cref="XrUnavailableException"/> saying what's missing.</summary>
    /// <param name="instanceExtensions">With <paramref name="createSurface"/>: a desktop window to mirror to as well.</param>
    /// <param name="stereo">How the eyes are wanted drawn (tuning/vr.json): multiview falls back to a pass an eye on a device without it.</param>
    public static VrView Start(string appName, double renderScale = 0.5, IReadOnlyList<string>? instanceExtensions = null, Func<VkInstance, VkSurfaceKHR>? createSurface = null,
        StereoPath stereo = StereoPath.Multiview)
    {
        var headset = XrHeadset.Start(appName);
        GpuContext? gpu = null;
        try
        {
            gpu = new GpuContext(appName, instanceExtensions, createSurface, factory: headset);
            return new VrView(headset, gpu, headset.Begin(gpu, renderScale), stereo);
        }
        catch
        {
            gpu?.Dispose();
            headset.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The player's own hands, drawn into the mesh round the eye point for the eyes (CreatureArt.HeadsetHands: the crew's
    /// gloves on IK'd arms); false, or unset, and the box fists (VrHands) are drawn instead.
    /// </summary>
    public Func<MeshBuilder, Camera, XrControllerState, bool>? Hands { get; set; }

    /// <summary>Draws a frame to the headset, at its pace (this blocks until it wants one).</summary>
    /// <param name="mesh">The scene, built round <paramref name="body"/>'s eye point. The hands are added to it for the
    /// eyes and taken off again, so it comes back as it went in.</param>
    /// <param name="comfort">The player's turning and comfort vignette: stepped with this frame's controllers and head.</param>
    /// <param name="panel">What's on the floating panel this frame (the HUD, a menu), if anything.</param>
    public XrFrameResult Frame(MeshBuilder mesh, in Camera body, in FrameLighting lighting, Vector3 clear, VrLocomotion? comfort = null, VrPanelContent? panel = null)
    {
        var b = body;
        var light = lighting;
        void Synced(XrControllerState controllers)
        {
            _lastBody = b;
            _lastControllers = controllers;
            int scene = mesh.Count;
            if (Hands?.Invoke(mesh, b, controllers) != true)
                VrHands.Build(mesh, b, controllers);
            // The eyes are this frame's by now, so the panel sits still in the world while the head moves.
            double now = _clock.Elapsed.TotalSeconds;
            panel?.Panel.Follow(Session.HeadPosition, Session.Head, Math.Clamp(now - _lastPanel, 0, 0.1));
            _lastPanel = now;
            for (int i = 0; i < 2; i++)
            {
                var overlay = _overlays[i];
                overlay.Clear();
                if (panel is { } p)
                    p.Panel.Project(p.Overlay, p.Width, p.Height, Session.Eyes[i].From(b), b.Yaw, Session.EyeWidth, Session.EyeHeight, overlay);
                Vignette(i, comfort, overlay);
                _lastOverlay[i] = overlay.Count > 0 ? overlay : null;
                if (Stereo == StereoPath.PerEye)
                    _eyes[i].Prepare(mesh, _lastOverlay[i]);
            }
            if (Stereo == StereoPath.Multiview)
                _eyes[0].Prepare(mesh, _lastOverlay[0], _lastOverlay[1]);
            mesh.Truncate(scene);
        }
        var result = Stereo == StereoPath.Multiview
            ? Session.FrameBoth((eyes, cmd) =>
            {
                Camera[] cameras = [eyes[0].From(b), eyes[1].From(b)];
                (_last[0], _last[1]) = (cameras[0], cameras[1]);
                _eyes[0].Record(cmd, cameras, light, clear);
                return _eyes[0];
            }, Synced)
            : Session.Frame((eye, cmd) =>
            {
                var camera = eye.From(b);
                _last[eye.Index] = camera;
                var renderer = _eyes[eye.Index];
                renderer.Record(cmd, camera, light, clear);
                return renderer;
            }, Synced);
        if (result is XrFrameResult.Rendered or XrFrameResult.Skipped)
        {
            double now = _clock.Elapsed.TotalSeconds;
            comfort?.Frame(Session.Controllers, Session.Head, Math.Clamp(now - _lastFrame, 0, 0.1), Session.HeadPosition);
            _lastFrame = now;
        }
        return result;
    }

    /// <summary>
    /// Shows the left eye's last frame (a multiview renderer's layer 0) in the desktop window (its middle, cropped to the window's shape), in place of drawing
    /// the flat view a third time a frame. Returns false if the swapchain needs recreating, as <see cref="Swapchain.Present"/>.
    /// </summary>
    public bool Mirror(Swapchain swapchain)
    {
        if (Session.FramesRendered == 0)
            return true;
        var eye = _eyes[0];
        var extent = swapchain.Extent;
        double aspect = extent.height > 0 ? (double)extent.width / extent.height : 16.0 / 9;
        int w = eye.Width, h = (int)Math.Round(w / aspect);
        if (h > eye.Height)
            (w, h) = ((int)Math.Round(eye.Height * aspect), eye.Height);
        return swapchain.Present(eye, _ => { }, ((eye.Width - w) / 2, (eye.Height - h) / 2, w, h));
    }

    /// <summary>The comfort vignette for an eye, centred where it looks straight ahead (towards the nose, not mid-image).</summary>
    void Vignette(int eye, VrLocomotion? comfort, Overlay into)
    {
        if (comfort is not { Vignette: > 0.01f })
            return;
        float w = Session.EyeWidth, h = Session.EyeHeight;
        var f = Session.Eyes[eye].Fov;
        float l = MathF.Tan(f.Left), r = MathF.Tan(f.Right), u = MathF.Tan(f.Up), d = MathF.Tan(f.Down);
        float cx = r > l ? w * -l / (r - l) : w / 2, cy = u > d ? h * u / (u - d) : h / 2;
        into.Vignette(w, h, cx, cy, comfort.Tuning.Vignette.Inner, comfort.Vignette);
    }

    /// <summary>Both eyes wear the look's textures, backdrop and grade.</summary>
    public void Dress(Look look)
    {
        foreach (var eye in _eyes)
            look.Dress(eye);
    }

    /// <summary>The camera each eye last drew with.</summary>
    public Camera LastEye(int eye) => _last[eye];

    /// <summary>Overlay vertices each eye last drew (the panel and the vignette), for the headless check.</summary>
    public int OverlayVertices(int eye) => _lastOverlay[eye]?.Count ?? 0;

    /// <summary>
    /// Both eyes side by side, as RGBA, drawn again as they last were, hands and vignette too (for screenshots and tests).
    /// </summary>
    public (byte[] Pixels, int Width, int Height) SideBySide(MeshBuilder mesh, in FrameLighting lighting, Vector3 clear)
    {
        int w = Session.EyeWidth, h = Session.EyeHeight;
        int scene = mesh.Count;
        if (Hands?.Invoke(mesh, _lastBody, _lastControllers) != true)
            VrHands.Build(mesh, _lastBody, _lastControllers);
        var eyes = Stereo == StereoPath.Multiview
            ? _eyes[0].RenderEyes(mesh, _last[0], _last[1], lighting, clear, _lastOverlay[0], _lastOverlay[1])
            : [_eyes[0].Render(mesh, _last[0], lighting, clear, _lastOverlay[0]), _eyes[1].Render(mesh, _last[1], lighting, clear, _lastOverlay[1])];
        var (left, right) = (eyes[0], eyes[1]);
        mesh.Truncate(scene);
        var both = new byte[w * 2 * h * 4];
        for (int y = 0; y < h; y++)
        {
            left.AsSpan(y * w * 4, w * 4).CopyTo(both.AsSpan(y * w * 8));
            right.AsSpan(y * w * 4, w * 4).CopyTo(both.AsSpan(y * w * 8 + w * 4));
        }
        if (Session.EyeFormat == VkFormat.B8G8R8A8Unorm)
            for (int i = 0; i < both.Length; i += 4)
                (both[i], both[i + 2]) = (both[i + 2], both[i]);
        return (both, w * 2, h);
    }

    public void Dispose()
    {
        Session.Dispose();
        foreach (var eye in _eyes)
            eye.Dispose();
        Gpu.Dispose();
        Headset.Dispose();
    }
}

/// <summary>What's on a headset panel this frame: an overlay as drawn for the flat screen, the size it was drawn at, and the panel.</summary>
public readonly record struct VrPanelContent(VrPanel Panel, Overlay Overlay, float Width, float Height);
