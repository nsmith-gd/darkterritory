using Vortice.Vulkan;
using static Ballast.Render.GpuContext;

namespace Ballast.Render;

/// <summary>
/// Presents the renderer's low-resolution frame to a window by nearest-filter blit. Rendering
/// low and scaling up with nearest is the pixelated look (art sheet principle 2), and it keeps
/// the swapchain path trivially thin: one blit, one present.
/// </summary>
public sealed unsafe class Swapchain : IDisposable
{
    readonly GpuContext _gpu;
    VkDeviceApi Api => _gpu.Api;
    VkSwapchainKHR _swapchain;
    VkImage[] _images = [];
    readonly VkFence _acquired;

    public Swapchain(GpuContext gpu, int width, int height, bool vsync = true)
    {
        if (!gpu.CanPresent)
            throw new InvalidOperationException("GpuContext was created without a surface");
        _gpu = gpu;
        VSync = vsync;
        var fenceInfo = new VkFenceCreateInfo();
        VkFence fence;
        Check(Api.vkCreateFence(&fenceInfo, null, &fence), "vkCreateFence");
        _acquired = fence;
        Recreate(width, height);
    }

    /// <summary>Waits for the monitor's refresh (FIFO) or not; a change takes at the next <see cref="Recreate"/>.</summary>
    public bool VSync { get; set; }
    public VkExtent2D Extent { get; private set; }
    public VkFormat Format { get; private set; }

    public void Recreate(int width, int height)
    {
        Api.vkDeviceWaitIdle();
        var inst = _gpu.InstanceApi;
        VkSurfaceCapabilitiesKHR caps;
        Check(inst.vkGetPhysicalDeviceSurfaceCapabilitiesKHR(_gpu.PhysicalDevice, _gpu.Surface, &caps), "surface capabilities");

        uint formatCount = 0;
        inst.vkGetPhysicalDeviceSurfaceFormatsKHR(_gpu.PhysicalDevice, _gpu.Surface, &formatCount, null);
        var formats = new VkSurfaceFormatKHR[formatCount];
        fixed (VkSurfaceFormatKHR* p = formats)
            inst.vkGetPhysicalDeviceSurfaceFormatsKHR(_gpu.PhysicalDevice, _gpu.Surface, &formatCount, p);
        // The renderer writes gamma-encoded colour, so present through a UNORM format as-is.
        var format = formats.FirstOrDefault(f => f.format is VkFormat.B8G8R8A8Unorm or VkFormat.R8G8B8A8Unorm, formats[0]);
        Format = format.format;

        var extent = caps.currentExtent.width != uint.MaxValue
            ? caps.currentExtent
            : new VkExtent2D(
                Math.Clamp((uint)width, caps.minImageExtent.width, caps.maxImageExtent.width),
                Math.Clamp((uint)height, caps.minImageExtent.height, caps.maxImageExtent.height));
        Extent = extent;

        uint imageCount = caps.maxImageCount == 0 ? caps.minImageCount + 1 : Math.Min(caps.minImageCount + 1, caps.maxImageCount);
        var old = _swapchain;
        var info = new VkSwapchainCreateInfoKHR
        {
            surface = _gpu.Surface,
            minImageCount = imageCount,
            imageFormat = format.format,
            imageColorSpace = format.colorSpace,
            imageExtent = extent,
            imageArrayLayers = 1,
            imageUsage = VkImageUsageFlags.TransferDst | VkImageUsageFlags.ColorAttachment,
            imageSharingMode = VkSharingMode.Exclusive,
            preTransform = caps.currentTransform,
            compositeAlpha = VkCompositeAlphaFlagsKHR.Opaque,
            presentMode = VSync ? VkPresentModeKHR.Fifo : VkPresentModeKHR.Immediate,
            clipped = true,
            oldSwapchain = old,
        };
        VkSwapchainKHR swapchain;
        Check(Api.vkCreateSwapchainKHR(&info, null, &swapchain), "vkCreateSwapchainKHR");
        if (old.IsNotNull)
            Api.vkDestroySwapchainKHR(old, null);
        _swapchain = swapchain;

        uint count = 0;
        Api.vkGetSwapchainImagesKHR(_swapchain, &count, null);
        _images = new VkImage[count];
        fixed (VkImage* p = _images)
            Api.vkGetSwapchainImagesKHR(_swapchain, &count, p);
    }

    /// <summary>
    /// Records the frame via <paramref name="render"/>, blits the renderer's image to the window and presents.
    /// Returns false if the swapchain is out of date and must be recreated at the new window size. The image keeps its shape
    /// (note 459: <see cref="Letterbox.Fit"/>), black bars where the window's is another (a 16:10 screen, an ultrawide).
    /// </summary>
    /// <param name="source">The part of the renderer's image to show (x, y, width, height); all of it by default.</param>
    public bool Present(GreyboxRenderer renderer, Action<VkCommandBuffer> render, (int X, int Y, int Width, int Height)? source = null)
    {
        uint index;
        var result = Api.vkAcquireNextImageKHR(_swapchain, ulong.MaxValue, VkSemaphore.Null, _acquired, &index);
        if (result == VkResult.ErrorOutOfDateKHR)
            return false;
        if (result != VkResult.Success && result != VkResult.SuboptimalKHR)
            Check(result, "vkAcquireNextImageKHR");
        var fence = _acquired;
        Api.vkWaitForFences(1, &fence, true, ulong.MaxValue);
        Api.vkResetFences(1, &fence);

        var image = _images[index];
        var extent = Extent;
        _gpu.Submit(cmd =>
        {
            render(cmd);
            renderer.Transition(cmd, image, VkImageAspectFlags.Color, VkImageLayout.Undefined, VkImageLayout.TransferDstOptimal);
            var blit = new VkImageBlit
            {
                srcSubresource = new VkImageSubresourceLayers(VkImageAspectFlags.Color, 0, 0, 1),
                dstSubresource = new VkImageSubresourceLayers(VkImageAspectFlags.Color, 0, 0, 1),
            };
            var (sx, sy, sw, sh) = source ?? (0, 0, renderer.Width, renderer.Height);
            var (dx, dy, dw, dh) = Letterbox.Fit(sw, sh, (int)extent.width, (int)extent.height);
            if (dw < extent.width || dh < extent.height)
            {
                // The bars: the swapchain's image comes undefined, so whatever isn't the picture is cleared to black.
                var black = new VkClearColorValue(0f, 0f, 0f, 1f);
                var all = new VkImageSubresourceRange(VkImageAspectFlags.Color, 0, 1, 0, 1);
                Api.vkCmdClearColorImage(cmd, image, VkImageLayout.TransferDstOptimal, &black, 1, &all);
            }
            blit.srcOffsets[0] = new VkOffset3D(sx, sy, 0);
            blit.srcOffsets[1] = new VkOffset3D(sx + sw, sy + sh, 1);
            blit.dstOffsets[0] = new VkOffset3D(dx, dy, 0);
            blit.dstOffsets[1] = new VkOffset3D(dx + dw, dy + dh, 1);
            Api.vkCmdBlitImage(cmd, renderer.ColorImage, VkImageLayout.TransferSrcOptimal, image, VkImageLayout.TransferDstOptimal, 1, &blit, VkFilter.Linear);
            renderer.Transition(cmd, image, VkImageAspectFlags.Color, VkImageLayout.TransferDstOptimal, VkImageLayout.PresentSrcKHR);
        });

        var swapchain = _swapchain;
        var present = new VkPresentInfoKHR { swapchainCount = 1, pSwapchains = &swapchain, pImageIndices = &index };
        result = Api.vkQueuePresentKHR(_gpu.Queue, &present);
        return result is VkResult.Success;
    }

    public void Dispose()
    {
        Api.vkDeviceWaitIdle();
        Api.vkDestroyFence(_acquired, null);
        if (_swapchain.IsNotNull)
            Api.vkDestroySwapchainKHR(_swapchain, null);
    }
}
