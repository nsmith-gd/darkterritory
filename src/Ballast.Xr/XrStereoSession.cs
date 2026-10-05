using System.Numerics;
using Ballast.Render;
using Silk.NET.OpenXR;
using Vortice.Vulkan;

namespace Ballast.Xr;

/// <summary>One eye for one frame: where it is in the tracking space, and its frustum.</summary>
/// <param name="Position">Metres from the tracking origin (the head where the session started), +Y up, −Z forward.</param>
public readonly record struct XrEye(int Index, Vector3 Position, Quaternion Orientation, EyeFov Fov)
{
    /// <summary>
    /// This eye in the game: the tracking origin sits at the flat camera's eye point, turned to its yaw (the player's
    /// body). The head's own pitch and roll replace the mouse's pitch.
    /// </summary>
    public Camera From(in Camera body)
    {
        var yaw = Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)body.Yaw);
        var eye = body;
        eye.Orientation = Quaternion.Concatenate(Orientation, yaw);
        eye.Fov = Fov;
        eye.EyeOffset = Vector3.Transform(Position, yaw);
        return eye;
    }
}

public enum XrFrameResult : byte
{
    /// <summary>The session isn't running yet (or any more): nothing was asked for. Keep calling.</summary>
    Idle,
    /// <summary>The runtime took a frame but didn't want it drawn (headset off, app hidden).</summary>
    Skipped,
    /// <summary>Both eyes were drawn and submitted.</summary>
    Rendered,
    /// <summary>The runtime is closing the session: stop.</summary>
    Exiting,
}

/// <summary>
/// A running OpenXR session: the stereo swapchains, the frame loop, and the session's lifecycle. The game draws both
/// eyes in one pass into a two-layer <see cref="GreyboxRenderer"/> (multiview: <see cref="FrameBoth"/>), or, where the
/// device can't, each eye with its own (<see cref="Frame(Func{XrEye, VkCommandBuffer, GreyboxRenderer}, Action{XrControllerState}?)"/>),
/// and each eye's image is copied into its swapchain bit for bit:
/// the renderer's UNORM target holds display-ready values, and the swapchain is the sRGB twin of that format.
/// Pixels are copied, not scaled: the eye is drawn at <c>renderScale</c> of the runtime's recommendation into the
/// top-left of the image, and the compositor stretches it. The low-res look survives, without nearest-filter shimmer
/// in a headset.
/// </summary>
public sealed unsafe class XrStereoSession : IDisposable
{
    readonly XrHeadset _headset;
    readonly GpuContext _gpu;
    readonly XR _xr;
    readonly Session _session;
    readonly Space _space;
    readonly Silk.NET.OpenXR.Swapchain[] _swapchains = new Silk.NET.OpenXR.Swapchain[2];
    readonly VkImage[][] _images = new VkImage[2][];
    readonly int _swapchainWidth, _swapchainHeight;
    bool _running, _disposed;

    internal XrStereoSession(XrHeadset headset, GpuContext gpu, double renderScale)
    {
        _headset = headset;
        _gpu = gpu;
        _xr = headset.Xr;

        var binding = new GraphicsBindingVulkanKHR
        {
            Type = StructureType.GraphicsBindingVulkanKhr,
            Instance = new(gpu.Instance.Handle),
            PhysicalDevice = new(gpu.PhysicalDevice.Handle),
            Device = new(gpu.Device.Handle),
            QueueFamilyIndex = gpu.QueueFamily,
            QueueIndex = 0,
        };
        var create = new SessionCreateInfo { Type = StructureType.SessionCreateInfo, Next = &binding, SystemId = headset.SystemId };
        Session session;
        XrHeadset.Check(_xr.CreateSession(headset.Instance, &create, &session), "xrCreateSession");
        _session = session;

        // LOCAL: the origin is the head where the session began. The game puts that origin at the player's eye,
        // turned to the player's body yaw, so a seated player on a moving car sees the car's frame, not the room's.
        var spaceInfo = new ReferenceSpaceCreateInfo
        {
            Type = StructureType.ReferenceSpaceCreateInfo,
            ReferenceSpaceType = ReferenceSpaceType.Local,
            PoseInReferenceSpace = new Posef { Orientation = new Quaternionf { W = 1 } },
        };
        Space space;
        XrHeadset.Check(_xr.CreateReferenceSpace(_session, &spaceInfo, &space), "xrCreateReferenceSpace");
        _space = space;

        Controls = new XrControls(_xr, headset.Instance, _session);
        (SwapchainFormat, EyeFormat) = PickFormat();
        _swapchainWidth = headset.EyeWidth;
        _swapchainHeight = headset.EyeHeight;
        EyeWidth = Math.Max(16, (int)Math.Round(headset.EyeWidth * Math.Clamp(renderScale, 0.1, 1)));
        EyeHeight = Math.Max(16, (int)Math.Round(headset.EyeHeight * Math.Clamp(renderScale, 0.1, 1)));
        for (int eye = 0; eye < 2; eye++)
            (_swapchains[eye], _images[eye]) = CreateSwapchain();
    }

    /// <summary>What each eye's renderer draws at, and in: pass these to its <see cref="GreyboxRenderer"/>.</summary>
    public int EyeWidth { get; }
    public int EyeHeight { get; }
    public VkFormat EyeFormat { get; }
    public VkFormat SwapchainFormat { get; }
    public SessionState State { get; private set; } = SessionState.Idle;
    public bool Running => _running;
    /// <summary>Every state the session has been through, oldest first (for the headless check).</summary>
    public List<SessionState> States { get; } = [];
    public int FramesRendered { get; private set; }
    public XrControls Controls { get; }
    /// <summary>The controllers as of the last frame (read at its predicted display time).</summary>
    public XrControllerState Controllers { get; private set; }
    /// <summary>The head's orientation in the tracking space, as of the last frame drawn.</summary>
    public Quaternion Head { get; private set; } = Quaternion.Identity;
    /// <summary>Where the head is in the tracking space (between the eyes), as of the last frame drawn.</summary>
    public Vector3 HeadPosition => (_eyes[0].Position + _eyes[1].Position) / 2;
    /// <summary>Both eyes, left then right, as of the last frame drawn (this frame's, from the synced callback on).</summary>
    public IReadOnlyList<XrEye> Eyes => _eyes;
    readonly XrEye[] _eyes = new XrEye[2];

    (VkFormat Swapchain, VkFormat Eye) PickFormat()
    {
        uint count = 0;
        XrHeadset.Check(_xr.EnumerateSwapchainFormats(_session, 0, &count, null), "xrEnumerateSwapchainFormats");
        var formats = new long[count];
        fixed (long* p = formats)
            XrHeadset.Check(_xr.EnumerateSwapchainFormats(_session, count, &count, p), "xrEnumerateSwapchainFormats");
        // The sRGB twin of a UNORM format the renderer can draw, so the copy needs no conversion. A runtime that offers
        // neither gets UNORM, which it may show a little washed out (it reads UNORM as linear).
        (VkFormat, VkFormat)[] wanted =
        [
            (VkFormat.R8G8B8A8Srgb, VkFormat.R8G8B8A8Unorm),
            (VkFormat.B8G8R8A8Srgb, VkFormat.B8G8R8A8Unorm),
            (VkFormat.R8G8B8A8Unorm, VkFormat.R8G8B8A8Unorm),
            (VkFormat.B8G8R8A8Unorm, VkFormat.B8G8R8A8Unorm),
        ];
        foreach (var (swapchain, eye) in wanted)
            if (formats.Contains((long)swapchain))
                return (swapchain, eye);
        throw new XrUnavailableException($"the runtime offers no 8-bit colour swapchain (it has {string.Join(", ", formats.Select(f => (VkFormat)f))})");
    }

    (Silk.NET.OpenXR.Swapchain, VkImage[]) CreateSwapchain()
    {
        var info = new SwapchainCreateInfo
        {
            Type = StructureType.SwapchainCreateInfo,
            UsageFlags = SwapchainUsageFlags.ColorAttachmentBit | SwapchainUsageFlags.TransferDstBit,
            Format = (long)SwapchainFormat,
            SampleCount = 1,
            Width = (uint)_swapchainWidth,
            Height = (uint)_swapchainHeight,
            FaceCount = 1,
            ArraySize = 1,
            MipCount = 1,
        };
        Silk.NET.OpenXR.Swapchain swapchain;
        XrHeadset.Check(_xr.CreateSwapchain(_session, &info, &swapchain), "xrCreateSwapchain");
        uint count = 0;
        XrHeadset.Check(_xr.EnumerateSwapchainImages(swapchain, 0, &count, null), "xrEnumerateSwapchainImages");
        var images = new SwapchainImageVulkanKHR[count];
        for (int i = 0; i < images.Length; i++)
            images[i].Type = StructureType.SwapchainImageVulkanKhr;
        fixed (SwapchainImageVulkanKHR* p = images)
            XrHeadset.Check(_xr.EnumerateSwapchainImages(swapchain, count, &count, (SwapchainImageBaseHeader*)p), "xrEnumerateSwapchainImages");
        return (swapchain, [.. images.Select(i => new VkImage(i.Image))]);
    }

    /// <summary>Handles the runtime's events: starts the session when it's ready, ends it when it's told to.</summary>
    /// <returns>False once the session is exiting.</returns>
    public bool PollEvents()
    {
        var buffer = new EventDataBuffer { Type = StructureType.EventDataBuffer };
        while (_xr.PollEvent(_headset.Instance, &buffer) == Result.Success)
        {
            if (buffer.Type == StructureType.EventDataSessionStateChanged)
            {
                var changed = *(EventDataSessionStateChanged*)&buffer;
                State = changed.State;
                States.Add(changed.State);
                switch (changed.State)
                {
                    case SessionState.Ready:
                        var begin = new SessionBeginInfo { Type = StructureType.SessionBeginInfo, PrimaryViewConfigurationType = ViewConfigurationType.PrimaryStereo };
                        XrHeadset.Check(_xr.BeginSession(_session, &begin), "xrBeginSession");
                        _running = true;
                        break;
                    case SessionState.Stopping:
                        XrHeadset.Check(_xr.EndSession(_session), "xrEndSession");
                        _running = false;
                        break;
                }
            }
            else if (buffer.Type == StructureType.EventDataInteractionProfileChanged)
                Controls.ProfileChanged();
            buffer = new EventDataBuffer { Type = StructureType.EventDataBuffer };
        }
        return State is not (SessionState.Exiting or SessionState.LossPending);
    }

    /// <summary>
    /// One frame at the runtime's pace (this blocks until the headset wants the next one). <paramref name="drawEye"/>
    /// records an eye into its renderer and returns it; it's called for eye 0 (left) then eye 1 (right).
    /// <paramref name="synced"/>, if given, runs first, as soon as the controllers are read for the frame: whatever
    /// follows the hands (the hands themselves) is placed there, not a frame late. The eyes are located by then too
    /// (<see cref="Eyes"/>), so what's placed by the head (a HUD panel projected into each eye) isn't a frame late either.
    /// </summary>
    public XrFrameResult Frame(Func<XrEye, VkCommandBuffer, GreyboxRenderer> drawEye, Action<XrControllerState>? synced = null) =>
        Frame(drawEye, null, synced);

    /// <summary>
    /// <see cref="Frame(Func{XrEye, VkCommandBuffer, GreyboxRenderer}, Action{XrControllerState}?)"/> with both eyes drawn
    /// at once (multiview, ARCHITECTURE §8 note 219): <paramref name="drawBoth"/> records both into one renderer's
    /// two-layer image (left, right) and returns it, and each layer is copied to its eye's swapchain in the same submit.
    /// </summary>
    public XrFrameResult FrameBoth(Func<IReadOnlyList<XrEye>, VkCommandBuffer, GreyboxRenderer> drawBoth, Action<XrControllerState>? synced = null) =>
        Frame(null, drawBoth, synced);

    /// <summary>GPU submits for the eyes so far: one an eye drawing each alone, one a frame drawing both at once.</summary>
    public int EyeSubmits { get; private set; }

    XrFrameResult Frame(Func<XrEye, VkCommandBuffer, GreyboxRenderer>? drawEye, Func<IReadOnlyList<XrEye>, VkCommandBuffer, GreyboxRenderer>? drawBoth,
        Action<XrControllerState>? synced)
    {
        if (!PollEvents())
            return XrFrameResult.Exiting;
        if (!_running)
            return XrFrameResult.Idle;

        var wait = new FrameWaitInfo { Type = StructureType.FrameWaitInfo };
        var state = new FrameState { Type = StructureType.FrameState };
        XrHeadset.Check(_xr.WaitFrame(_session, &wait, &state), "xrWaitFrame");
        var beginFrame = new FrameBeginInfo { Type = StructureType.FrameBeginInfo };
        XrHeadset.Check(_xr.BeginFrame(_session, &beginFrame), "xrBeginFrame");

        var views = stackalloc View[2];
        views[0] = new View { Type = StructureType.View };
        views[1] = new View { Type = StructureType.View };
        var projection = stackalloc CompositionLayerProjectionView[2];
        bool draw = state.ShouldRender != 0;
        if (draw)
        {
            var locate = new ViewLocateInfo
            {
                Type = StructureType.ViewLocateInfo,
                ViewConfigurationType = ViewConfigurationType.PrimaryStereo,
                DisplayTime = state.PredictedDisplayTime,
                Space = _space,
            };
            var viewState = new ViewState { Type = StructureType.ViewState };
            uint located;
            XrHeadset.Check(_xr.LocateView(_session, &locate, &viewState, 2, &located, views), "xrLocateViews");
            // No tracking this frame (the headset lost its bearings): submit nothing rather than a wrong view.
            draw = located == 2 && (viewState.ViewStateFlags & ViewStateFlags.OrientationValidBit) != 0;
            if (draw)
            {
                Head = Quaternion.Slerp(Orientation(views[0].Pose), Orientation(views[1].Pose), 0.5f);
                for (int eye = 0; eye < 2; eye++)
                {
                    var pose = views[eye].Pose;
                    var fov = views[eye].Fov;
                    _eyes[eye] = new XrEye(eye, new Vector3(pose.Position.X, pose.Position.Y, pose.Position.Z), Orientation(pose),
                        new EyeFov(fov.AngleLeft, fov.AngleRight, fov.AngleUp, fov.AngleDown));
                }
            }
        }
        Controllers = Controls.Sync(state.PredictedDisplayTime, _space);
        synced?.Invoke(Controllers);
        if (draw)
        {
            var both = drawBoth is null ? default : DrawBoth(drawBoth);
            for (int eye = 0; eye < 2; eye++)
            {
                var pose = views[eye].Pose;
                var fov = views[eye].Fov;
                var (width, height) = drawBoth is null ? DrawEye(eye, _eyes[eye], drawEye!) : both;
                projection[eye] = new CompositionLayerProjectionView
                {
                    Type = StructureType.CompositionLayerProjectionView,
                    Pose = pose,
                    Fov = fov,
                    SubImage = new SwapchainSubImage
                    {
                        Swapchain = _swapchains[eye],
                        ImageRect = new Rect2Di { Offset = new Offset2Di(0, 0), Extent = new Extent2Di(width, height) },
                        ImageArrayIndex = 0,
                    },
                };
            }
            FramesRendered++;
        }

        var layer = new CompositionLayerProjection
        {
            Type = StructureType.CompositionLayerProjection,
            Space = _space,
            ViewCount = 2,
            Views = projection,
        };
        var layers = stackalloc CompositionLayerBaseHeader*[1];
        layers[0] = (CompositionLayerBaseHeader*)&layer;
        var end = new FrameEndInfo
        {
            Type = StructureType.FrameEndInfo,
            DisplayTime = state.PredictedDisplayTime,
            EnvironmentBlendMode = EnvironmentBlendMode.Opaque,
            LayerCount = draw ? 1u : 0u,
            Layers = layers,
        };
        XrHeadset.Check(_xr.EndFrame(_session, &end), "xrEndFrame");
        return draw ? XrFrameResult.Rendered : XrFrameResult.Skipped;
    }

    static Quaternion Orientation(in Posef pose) => new(pose.Orientation.X, pose.Orientation.Y, pose.Orientation.Z, pose.Orientation.W);

    (int Width, int Height) DrawEye(int eye, XrEye view, Func<XrEye, VkCommandBuffer, GreyboxRenderer> drawEye)
    {
        var swapchain = _swapchains[eye];
        var acquire = new SwapchainImageAcquireInfo { Type = StructureType.SwapchainImageAcquireInfo };
        uint index;
        XrHeadset.Check(_xr.AcquireSwapchainImage(swapchain, &acquire, &index), "xrAcquireSwapchainImage");
        var wait = new SwapchainImageWaitInfo { Type = StructureType.SwapchainImageWaitInfo, Timeout = long.MaxValue };
        XrHeadset.Check(_xr.WaitSwapchainImage(swapchain, &wait), "xrWaitSwapchainImage");
        var target = _images[eye][index];
        int width = 0, height = 0;
        EyeSubmits++;
        _gpu.Submit(cmd =>
        {
            var renderer = drawEye(view, cmd);
            width = Math.Min(renderer.Width, _swapchainWidth);
            height = Math.Min(renderer.Height, _swapchainHeight);
            // The runtime hands images over in COLOR_ATTACHMENT_OPTIMAL and wants them back that way.
            renderer.Transition(cmd, target, VkImageAspectFlags.Color, VkImageLayout.ColorAttachmentOptimal, VkImageLayout.TransferDstOptimal);
            var region = new VkImageCopy
            {
                srcSubresource = new VkImageSubresourceLayers(VkImageAspectFlags.Color, 0, 0, 1),
                dstSubresource = new VkImageSubresourceLayers(VkImageAspectFlags.Color, 0, 0, 1),
                extent = new VkExtent3D((uint)width, (uint)height, 1),
            };
            _gpu.Api.vkCmdCopyImage(cmd, renderer.ColorImage, VkImageLayout.TransferSrcOptimal, target, VkImageLayout.TransferDstOptimal, 1, &region);
            renderer.Transition(cmd, target, VkImageAspectFlags.Color, VkImageLayout.TransferDstOptimal, VkImageLayout.ColorAttachmentOptimal);
        });
        var release = new SwapchainImageReleaseInfo { Type = StructureType.SwapchainImageReleaseInfo };
        XrHeadset.Check(_xr.ReleaseSwapchainImage(swapchain, &release), "xrReleaseSwapchainImage");
        return (width, height);
    }

    /// <summary>Both eyes from one renderer's layers, in one submit: each swapchain's image acquired, filled from its layer, released.</summary>
    (int Width, int Height) DrawBoth(Func<IReadOnlyList<XrEye>, VkCommandBuffer, GreyboxRenderer> drawBoth)
    {
        var targets = new VkImage[2];
        for (int eye = 0; eye < 2; eye++)
        {
            var acquire = new SwapchainImageAcquireInfo { Type = StructureType.SwapchainImageAcquireInfo };
            uint index;
            XrHeadset.Check(_xr.AcquireSwapchainImage(_swapchains[eye], &acquire, &index), "xrAcquireSwapchainImage");
            var wait = new SwapchainImageWaitInfo { Type = StructureType.SwapchainImageWaitInfo, Timeout = long.MaxValue };
            XrHeadset.Check(_xr.WaitSwapchainImage(_swapchains[eye], &wait), "xrWaitSwapchainImage");
            targets[eye] = _images[eye][index];
        }
        int width = 0, height = 0;
        EyeSubmits++;
        _gpu.Submit(cmd =>
        {
            var renderer = drawBoth(_eyes, cmd);
            if (renderer.Views != 2)
                throw new InvalidOperationException("drawing both eyes at once wants a two-view renderer");
            width = Math.Min(renderer.Width, _swapchainWidth);
            height = Math.Min(renderer.Height, _swapchainHeight);
            for (int eye = 0; eye < 2; eye++)
            {
                renderer.Transition(cmd, targets[eye], VkImageAspectFlags.Color, VkImageLayout.ColorAttachmentOptimal, VkImageLayout.TransferDstOptimal);
                var region = new VkImageCopy
                {
                    srcSubresource = new VkImageSubresourceLayers(VkImageAspectFlags.Color, 0, (uint)eye, 1),
                    dstSubresource = new VkImageSubresourceLayers(VkImageAspectFlags.Color, 0, 0, 1),
                    extent = new VkExtent3D((uint)width, (uint)height, 1),
                };
                _gpu.Api.vkCmdCopyImage(cmd, renderer.ColorImage, VkImageLayout.TransferSrcOptimal, targets[eye], VkImageLayout.TransferDstOptimal, 1, &region);
                renderer.Transition(cmd, targets[eye], VkImageAspectFlags.Color, VkImageLayout.TransferDstOptimal, VkImageLayout.ColorAttachmentOptimal);
            }
        });
        for (int eye = 0; eye < 2; eye++)
        {
            var release = new SwapchainImageReleaseInfo { Type = StructureType.SwapchainImageReleaseInfo };
            XrHeadset.Check(_xr.ReleaseSwapchainImage(_swapchains[eye], &release), "xrReleaseSwapchainImage");
        }
        return (width, height);
    }

    /// <summary>Asks the runtime to end the session; keep calling <see cref="Frame"/> until it says Exiting.</summary>
    public void RequestExit()
    {
        if (_running)
            _xr.RequestExitSession(_session);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _gpu.Api.vkDeviceWaitIdle();
        Controls.Dispose();
        foreach (var s in _swapchains)
            _xr.DestroySwapchain(s);
        _xr.DestroySpace(_space);
        _xr.DestroySession(_session);
    }
}
