using Vortice.Vulkan;
using static Ballast.Render.GpuContext;

namespace Ballast.Render;

/// <summary>
/// A sampled image on the GPU: a 2D array of material layers with their mip chains, a 2D image, or a 3D table. Uploaded
/// once through a staging buffer and left in shader-read layout.
/// </summary>
public sealed unsafe class GpuTexture : IDisposable
{
    readonly GpuContext _gpu;
    VkDeviceApi Api => _gpu.Api;

    public VkImage Image { get; }
    public VkImageView View { get; }
    public VkSampler Sampler { get; }
    readonly VkDeviceMemory _memory;

    public int Width { get; }
    public int Height { get; }
    public int Layers { get; }

    public enum Kind { Array2D, Image2D, Volume }

    /// <param name="levels">Per layer, its mip chain top level first (all layers the same size). For a volume, one
    /// "layer" holding the whole table and depth = <paramref name="depth"/>.</param>
    /// <param name="filter">Nearest for the crunchy look; linear for smooth things (the grade table).</param>
    public GpuTexture(GpuContext gpu, Kind kind, VkFormat format, int width, int height, IReadOnlyList<IReadOnlyList<byte[]>> levels,
        VkFilter filter, VkSamplerAddressMode addressU, VkSamplerAddressMode addressV, float mipBias = 0, int depth = 1)
    {
        _gpu = gpu;
        Width = width;
        Height = height;
        Layers = kind == Kind.Volume ? 1 : levels.Count;
        int mips = levels[0].Count;
        var info = new VkImageCreateInfo
        {
            imageType = kind == Kind.Volume ? VkImageType.Image3D : VkImageType.Image2D,
            format = format,
            extent = new VkExtent3D(width, height, depth),
            mipLevels = (uint)mips,
            arrayLayers = (uint)Layers,
            samples = VkSampleCountFlags.Count1,
            tiling = VkImageTiling.Optimal,
            usage = VkImageUsageFlags.Sampled | VkImageUsageFlags.TransferDst,
            initialLayout = VkImageLayout.Undefined,
        };
        VkImage created;
        Check(Api.vkCreateImage(&info, null, &created), "vkCreateImage");
        var image = created;
        Image = image;
        VkMemoryRequirements req;
        Api.vkGetImageMemoryRequirements(image, &req);
        _memory = gpu.Allocate(req, VkMemoryPropertyFlags.DeviceLocal);
        Check(Api.vkBindImageMemory(image, _memory, 0), "vkBindImageMemory");

        var range = new VkImageSubresourceRange(VkImageAspectFlags.Color, 0, (uint)mips, 0, (uint)Layers);
        var viewInfo = new VkImageViewCreateInfo
        {
            image = image,
            viewType = kind switch { Kind.Array2D => VkImageViewType.Image2DArray, Kind.Volume => VkImageViewType.Image3D, _ => VkImageViewType.Image2D },
            format = format,
            subresourceRange = range,
        };
        VkImageView view;
        Check(Api.vkCreateImageView(&viewInfo, null, &view), "vkCreateImageView");
        View = view;

        // Everything into one staging buffer, then one copy per layer and level.
        ulong total = 0;
        foreach (var layer in levels)
            foreach (var level in layer)
                total += (ulong)level.Length;
        var bufferInfo = new VkBufferCreateInfo { size = total, usage = VkBufferUsageFlags.TransferSrc, sharingMode = VkSharingMode.Exclusive };
        VkBuffer stagingBuffer;
        Check(Api.vkCreateBuffer(&bufferInfo, null, &stagingBuffer), "vkCreateBuffer");
        var staging = stagingBuffer;
        VkMemoryRequirements breq;
        Api.vkGetBufferMemoryRequirements(staging, &breq);
        var stagingMemory = gpu.Allocate(breq, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
        Check(Api.vkBindBufferMemory(staging, stagingMemory, 0), "vkBindBufferMemory");
        void* mapped;
        Check(Api.vkMapMemory(stagingMemory, 0, total, 0, &mapped), "vkMapMemory");
        var regions = new List<VkBufferImageCopy>();
        ulong offset = 0;
        for (int l = 0; l < levels.Count; l++)
            for (int m = 0; m < mips; m++)
            {
                var data = levels[l][m];
                data.CopyTo(new Span<byte>((byte*)mapped + (long)offset, data.Length));
                regions.Add(new VkBufferImageCopy
                {
                    bufferOffset = offset,
                    imageSubresource = new VkImageSubresourceLayers(VkImageAspectFlags.Color, (uint)m, (uint)(kind == Kind.Volume ? 0 : l), 1),
                    imageExtent = new VkExtent3D(Math.Max(1, width >> m), Math.Max(1, height >> m), Math.Max(1, depth >> m)),
                });
                offset += (ulong)data.Length;
            }
        Api.vkUnmapMemory(stagingMemory);
        var regionArray = regions.ToArray();
        gpu.Submit(cmd =>
        {
            Barrier(cmd, image, range, VkImageLayout.Undefined, VkImageLayout.TransferDstOptimal);
            fixed (VkBufferImageCopy* r = regionArray)
                Api.vkCmdCopyBufferToImage(cmd, staging, image, VkImageLayout.TransferDstOptimal, (uint)regionArray.Length, r);
            Barrier(cmd, image, range, VkImageLayout.TransferDstOptimal, VkImageLayout.ShaderReadOnlyOptimal);
        });
        Api.vkDestroyBuffer(staging, null);
        Api.vkFreeMemory(stagingMemory, null);

        Sampler = CreateSampler(gpu, filter, filter, mips > 1 ? VkSamplerMipmapMode.Linear : VkSamplerMipmapMode.Nearest, addressU, addressV, mipBias, mips);
    }

    public static VkSampler CreateSampler(GpuContext gpu, VkFilter mag, VkFilter min, VkSamplerMipmapMode mip, VkSamplerAddressMode u,
        VkSamplerAddressMode v, float bias = 0, int levels = 1)
    {
        var info = new VkSamplerCreateInfo
        {
            magFilter = mag,
            minFilter = min,
            mipmapMode = mip,
            addressModeU = u,
            addressModeV = v,
            addressModeW = VkSamplerAddressMode.ClampToEdge,
            mipLodBias = bias,
            maxLod = levels,
        };
        VkSampler sampler;
        Check(gpu.Api.vkCreateSampler(&info, null, &sampler), "vkCreateSampler");
        return sampler;
    }

    void Barrier(VkCommandBuffer cmd, VkImage image, VkImageSubresourceRange range, VkImageLayout from, VkImageLayout to)
    {
        var barrier = new VkImageMemoryBarrier2
        {
            srcStageMask = VkPipelineStageFlags2.AllCommands,
            srcAccessMask = VkAccessFlags2.MemoryWrite,
            dstStageMask = VkPipelineStageFlags2.AllCommands,
            dstAccessMask = VkAccessFlags2.MemoryRead | VkAccessFlags2.MemoryWrite,
            oldLayout = from,
            newLayout = to,
            srcQueueFamilyIndex = ~0u,
            dstQueueFamilyIndex = ~0u,
            image = image,
            subresourceRange = range,
        };
        var dep = new VkDependencyInfo { imageMemoryBarrierCount = 1, pImageMemoryBarriers = &barrier };
        Api.vkCmdPipelineBarrier2(cmd, &dep);
    }

    /// <summary>
    /// An image's mip chain by 2×2 box filter, down to 1×1. Alpha-tested maps keep their coverage roughly by
    /// averaging alpha too; the cutout's threshold is 0.5.
    /// </summary>
    public static List<byte[]> MipChain(Image image)
    {
        var chain = new List<byte[]> { image.Rgba };
        int w = image.Width, h = image.Height;
        var level = image.Rgba;
        while (w > 1 || h > 1)
        {
            int nw = Math.Max(1, w / 2), nh = Math.Max(1, h / 2);
            var next = new byte[nw * nh * 4];
            for (int y = 0; y < nh; y++)
                for (int x = 0; x < nw; x++)
                    for (int c = 0; c < 4; c++)
                    {
                        int x0 = Math.Min(x * 2, w - 1), x1 = Math.Min(x * 2 + 1, w - 1), y0 = Math.Min(y * 2, h - 1), y1 = Math.Min(y * 2 + 1, h - 1);
                        int sum = level[(y0 * w + x0) * 4 + c] + level[(y0 * w + x1) * 4 + c] + level[(y1 * w + x0) * 4 + c] + level[(y1 * w + x1) * 4 + c];
                        next[(y * nw + x) * 4 + c] = (byte)((sum + 2) / 4);
                    }
            chain.Add(next);
            (w, h, level) = (nw, nh, next);
        }
        return chain;
    }

    public void Dispose()
    {
        Api.vkDestroySampler(Sampler, null);
        Api.vkDestroyImageView(View, null);
        Api.vkDestroyImage(Image, null);
        Api.vkFreeMemory(_memory, null);
    }
}
