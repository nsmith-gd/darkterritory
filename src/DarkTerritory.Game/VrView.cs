using System.Numerics;
using Ballast.Render;
using Ballast.Xr;
using Vortice.Vulkan;

namespace DarkTerritory.Game;

/// <summary>
/// The game in a headset (T21, roadmap M4): an OpenXR session, a renderer per eye, and the flat camera as the
/// player's body. The eyes sit where the flat camera's eye point is, turned to its yaw; the head supplies the rest.
/// Both eyes draw the same mesh (it's built around the body's eye point), each from its own few centimetres off it.
/// </summary>
public sealed class VrView : IDisposable
{
    readonly GreyboxRenderer[] _eyes;
    readonly Camera[] _last = new Camera[2];

    VrView(XrHeadset headset, GpuContext gpu, XrStereoSession session)
    {
        Headset = headset;
        Gpu = gpu;
        Session = session;
        _eyes = [new(gpu, session.EyeWidth, session.EyeHeight, session.EyeFormat), new(gpu, session.EyeWidth, session.EyeHeight, session.EyeFormat)];
    }

    public XrHeadset Headset { get; }
    public GpuContext Gpu { get; }
    public XrStereoSession Session { get; }

    /// <summary>Finds the headset and starts a session. Throws <see cref="XrUnavailableException"/> saying what's missing.</summary>
    /// <param name="instanceExtensions">With <paramref name="createSurface"/>: a desktop window to mirror to as well.</param>
    public static VrView Start(string appName, double renderScale = 0.5, IReadOnlyList<string>? instanceExtensions = null, Func<VkInstance, VkSurfaceKHR>? createSurface = null)
    {
        var headset = XrHeadset.Start(appName);
        GpuContext? gpu = null;
        try
        {
            gpu = new GpuContext(appName, instanceExtensions, createSurface, factory: headset);
            return new VrView(headset, gpu, headset.Begin(gpu, renderScale));
        }
        catch
        {
            gpu?.Dispose();
            headset.Dispose();
            throw;
        }
    }

    /// <summary>Draws a frame to the headset, at its pace (this blocks until it wants one).</summary>
    public XrFrameResult Frame(MeshBuilder mesh, in Camera body, in FrameLighting lighting, Vector3 clear)
    {
        foreach (var eye in _eyes)
            eye.Prepare(mesh);
        var b = body;
        var light = lighting;
        return Session.Frame((eye, cmd) =>
        {
            var camera = eye.From(b);
            _last[eye.Index] = camera;
            var renderer = _eyes[eye.Index];
            renderer.Record(cmd, camera, light, clear);
            return renderer;
        });
    }

    /// <summary>The camera each eye last drew with.</summary>
    public Camera LastEye(int eye) => _last[eye];

    /// <summary>Both eyes side by side, as RGBA, drawn again from where they last were (for screenshots and tests).</summary>
    public (byte[] Pixels, int Width, int Height) SideBySide(MeshBuilder mesh, in FrameLighting lighting, Vector3 clear)
    {
        int w = Session.EyeWidth, h = Session.EyeHeight;
        var left = _eyes[0].Render(mesh, _last[0], lighting, clear);
        var right = _eyes[1].Render(mesh, _last[1], lighting, clear);
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
