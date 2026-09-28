using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Vortice.ShaderCompiler;
using Vortice.Vulkan;
using static Ballast.Render.GpuContext;

namespace Ballast.Render;

[StructLayout(LayoutKind.Sequential)]
struct FrameConstants
{
    public Matrix4x4 ViewProj;
    public Vector4 Fog;
    public Vector4 Moon;
    public Vector4 LampPos;
    public Vector4 LampDir;
}

/// <summary>
/// Forward renderer for greybox scenes into an offscreen colour target, with CPU readback.
/// The same pipeline will draw into swapchain and OpenXR images; offscreen is the path agents,
/// tests and golden screenshots use.
/// </summary>
public sealed unsafe class GreyboxRenderer : IDisposable
{
    const VkFormat ColorFormat = VkFormat.R8G8B8A8Unorm;
    const VkFormat DepthFormat = VkFormat.D32Sfloat;

    readonly GpuContext _gpu;
    VkDeviceApi Api => _gpu.Api;

    readonly VkImage _color, _depth;
    readonly VkDeviceMemory _colorMemory, _depthMemory;
    readonly VkImageView _colorView, _depthView;
    readonly VkBuffer _readback;
    readonly VkDeviceMemory _readbackMemory;
    readonly VkPipelineLayout _layout;
    readonly VkPipeline _pipeline;

    VkBuffer _vertices;
    VkDeviceMemory _vertexMemory;
    ulong _vertexCapacity;

    public GreyboxRenderer(GpuContext gpu, int width, int height)
    {
        _gpu = gpu;
        Width = width;
        Height = height;

        (_color, _colorMemory, _colorView) = CreateImage(ColorFormat, VkImageUsageFlags.ColorAttachment | VkImageUsageFlags.TransferSrc, VkImageAspectFlags.Color);
        (_depth, _depthMemory, _depthView) = CreateImage(DepthFormat, VkImageUsageFlags.DepthStencilAttachment, VkImageAspectFlags.Depth);
        (_readback, _readbackMemory) = CreateBuffer((ulong)(width * height * 4), VkBufferUsageFlags.TransferDst,
            VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
        (_layout, _pipeline) = CreatePipeline();
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>The rendered frame. After <see cref="Record"/> it is in TransferSrcOptimal layout.</summary>
    public VkImage ColorImage => _color;

    /// <summary>Draws the mesh (camera-relative positions) and returns the frame as RGBA8, top row first.</summary>
    /// <param name="clearColor">Linear colour; gamma-encoded here so the sky matches fogged geometry.</param>
    public byte[] Render(MeshBuilder mesh, in Camera camera, in FrameLighting lighting, Vector3 clearColor)
    {
        Prepare(mesh);
        var cam = camera;
        var light = lighting;
        _gpu.Submit(cmd =>
        {
            Record(cmd, cam, light, clearColor);
            var region = new VkBufferImageCopy
            {
                imageSubresource = new VkImageSubresourceLayers(VkImageAspectFlags.Color, 0, 0, 1),
                imageExtent = new VkExtent3D(Width, Height, 1),
            };
            Api.vkCmdCopyImageToBuffer(cmd, _color, VkImageLayout.TransferSrcOptimal, _readback, 1, &region);
        });

        var pixels = new byte[Width * Height * 4];
        void* mapped;
        Check(Api.vkMapMemory(_readbackMemory, 0, (ulong)pixels.Length, 0, &mapped), "vkMapMemory");
        new ReadOnlySpan<byte>(mapped, pixels.Length).CopyTo(pixels);
        Api.vkUnmapMemory(_readbackMemory);
        return pixels;
    }

    int _vertexCount;

    /// <summary>Uploads geometry for the next <see cref="Record"/>. Call outside command recording.</summary>
    public void Prepare(MeshBuilder mesh)
    {
        UploadVertices(mesh.Vertices);
        _vertexCount = mesh.Count;
    }

    /// <summary>Records the frame into <see cref="ColorImage"/>, leaving it ready to copy or blit.</summary>
    public void Record(VkCommandBuffer cmd, in Camera camera, in FrameLighting lighting, Vector3 clearColor)
    {
        clearColor = new Vector3(MathF.Pow(clearColor.X, 1 / 2.2f), MathF.Pow(clearColor.Y, 1 / 2.2f), MathF.Pow(clearColor.Z, 1 / 2.2f));
        var constants = new FrameConstants
        {
            ViewProj = camera.ViewProjection((float)Width / Height),
            Fog = new Vector4(lighting.FogColor, lighting.FogDensity),
            Moon = new Vector4(lighting.MoonDirection, lighting.Ambient),
            LampPos = new Vector4(lighting.LampPosition.RelativeTo(camera.Position), lighting.LampRange),
            LampDir = new Vector4(lighting.LampDirection, MathF.Cos(lighting.LampConeDegrees * MathF.PI / 180)),
        };
        int vertexCount = _vertexCount;
        Transition(cmd, _color, VkImageAspectFlags.Color, VkImageLayout.Undefined, VkImageLayout.ColorAttachmentOptimal);
        Transition(cmd, _depth, VkImageAspectFlags.Depth, VkImageLayout.Undefined, VkImageLayout.DepthAttachmentOptimal);

        var colorAttachment = new VkRenderingAttachmentInfo
        {
            imageView = _colorView,
            imageLayout = VkImageLayout.ColorAttachmentOptimal,
            loadOp = VkAttachmentLoadOp.Clear,
            storeOp = VkAttachmentStoreOp.Store,
            clearValue = new VkClearValue { color = new VkClearColorValue(clearColor.X, clearColor.Y, clearColor.Z, 1) },
        };
        var depthAttachment = new VkRenderingAttachmentInfo
        {
            imageView = _depthView,
            imageLayout = VkImageLayout.DepthAttachmentOptimal,
            loadOp = VkAttachmentLoadOp.Clear,
            storeOp = VkAttachmentStoreOp.DontCare,
            clearValue = new VkClearValue { depthStencil = new VkClearDepthStencilValue(1, 0) },
        };
        var rendering = new VkRenderingInfo
        {
            renderArea = new VkRect2D(0, 0, (uint)Width, (uint)Height),
            layerCount = 1,
            colorAttachmentCount = 1,
            pColorAttachments = &colorAttachment,
            pDepthAttachment = &depthAttachment,
        };
        Api.vkCmdBeginRendering(cmd, &rendering);
        var viewport = new VkViewport(0, 0, Width, Height, 0, 1);
        Api.vkCmdSetViewport(cmd, 0, 1, &viewport);
        var scissor = new VkRect2D(0, 0, (uint)Width, (uint)Height);
        Api.vkCmdSetScissor(cmd, 0, 1, &scissor);
        if (vertexCount > 0)
        {
            Api.vkCmdBindPipeline(cmd, VkPipelineBindPoint.Graphics, _pipeline);
            var c = constants;
            Api.vkCmdPushConstants(cmd, _layout, VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment, 0, (uint)sizeof(FrameConstants), &c);
            var vb = _vertices;
            ulong offset = 0;
            Api.vkCmdBindVertexBuffers(cmd, 0, 1, &vb, &offset);
            Api.vkCmdDraw(cmd, (uint)vertexCount, 1, 0, 0);
        }
        Api.vkCmdEndRendering(cmd);

        Transition(cmd, _color, VkImageAspectFlags.Color, VkImageLayout.ColorAttachmentOptimal, VkImageLayout.TransferSrcOptimal);
    }

    void UploadVertices(ReadOnlySpan<Vertex> vertices)
    {
        ulong size = (ulong)Math.Max(1, vertices.Length) * Vertex.Stride;
        if (size > _vertexCapacity)
        {
            if (_vertexCapacity > 0)
            {
                Api.vkDestroyBuffer(_vertices, null);
                Api.vkFreeMemory(_vertexMemory, null);
            }
            _vertexCapacity = Math.Max(size, _vertexCapacity * 2);
            (_vertices, _vertexMemory) = CreateBuffer(_vertexCapacity, VkBufferUsageFlags.VertexBuffer,
                VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
        }
        if (vertices.IsEmpty)
            return;
        void* mapped;
        Check(Api.vkMapMemory(_vertexMemory, 0, size, 0, &mapped), "vkMapMemory");
        MemoryMarshal.AsBytes(vertices).CopyTo(new Span<byte>(mapped, (int)size));
        Api.vkUnmapMemory(_vertexMemory);
    }

    internal void Transition(VkCommandBuffer cmd, VkImage image, VkImageAspectFlags aspect, VkImageLayout from, VkImageLayout to)
    {
        var barrier = new VkImageMemoryBarrier2
        {
            srcStageMask = VkPipelineStageFlags2.AllCommands,
            srcAccessMask = VkAccessFlags2.MemoryWrite,
            dstStageMask = VkPipelineStageFlags2.AllCommands,
            dstAccessMask = VkAccessFlags2.MemoryRead | VkAccessFlags2.MemoryWrite,
            oldLayout = from,
            newLayout = to,
            srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
            dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
            image = image,
            subresourceRange = new VkImageSubresourceRange(aspect, 0, 1, 0, 1),
        };
        var dep = new VkDependencyInfo { imageMemoryBarrierCount = 1, pImageMemoryBarriers = &barrier };
        Api.vkCmdPipelineBarrier2(cmd, &dep);
    }

    const uint VK_QUEUE_FAMILY_IGNORED = ~0u;

    (VkImage, VkDeviceMemory, VkImageView) CreateImage(VkFormat format, VkImageUsageFlags usage, VkImageAspectFlags aspect)
    {
        var info = new VkImageCreateInfo
        {
            imageType = VkImageType.Image2D,
            format = format,
            extent = new VkExtent3D(Width, Height, 1),
            mipLevels = 1,
            arrayLayers = 1,
            samples = VkSampleCountFlags.Count1,
            tiling = VkImageTiling.Optimal,
            usage = usage,
            initialLayout = VkImageLayout.Undefined,
        };
        VkImage image;
        Check(Api.vkCreateImage(&info, null, &image), "vkCreateImage");
        VkMemoryRequirements req;
        Api.vkGetImageMemoryRequirements(image, &req);
        var memory = _gpu.Allocate(req, VkMemoryPropertyFlags.DeviceLocal);
        Check(Api.vkBindImageMemory(image, memory, 0), "vkBindImageMemory");
        var viewInfo = new VkImageViewCreateInfo
        {
            image = image,
            viewType = VkImageViewType.Image2D,
            format = format,
            subresourceRange = new VkImageSubresourceRange(aspect, 0, 1, 0, 1),
        };
        VkImageView view;
        Check(Api.vkCreateImageView(&viewInfo, null, &view), "vkCreateImageView");
        return (image, memory, view);
    }

    (VkBuffer, VkDeviceMemory) CreateBuffer(ulong size, VkBufferUsageFlags usage, VkMemoryPropertyFlags flags)
    {
        var info = new VkBufferCreateInfo { size = size, usage = usage, sharingMode = VkSharingMode.Exclusive };
        VkBuffer buffer;
        Check(Api.vkCreateBuffer(&info, null, &buffer), "vkCreateBuffer");
        VkMemoryRequirements req;
        Api.vkGetBufferMemoryRequirements(buffer, &req);
        var memory = _gpu.Allocate(req, flags);
        Check(Api.vkBindBufferMemory(buffer, memory, 0), "vkBindBufferMemory");
        return (buffer, memory);
    }

    (VkPipelineLayout, VkPipeline) CreatePipeline()
    {
        var push = new VkPushConstantRange { stageFlags = VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment, size = (uint)sizeof(FrameConstants) };
        var layoutInfo = new VkPipelineLayoutCreateInfo { pushConstantRangeCount = 1, pPushConstantRanges = &push };
        VkPipelineLayout layout;
        Check(Api.vkCreatePipelineLayout(&layoutInfo, null, &layout), "vkCreatePipelineLayout");

        var vert = CreateShader("greybox.vert", ShaderKind.VertexShader);
        var frag = CreateShader("greybox.frag", ShaderKind.FragmentShader);
        var entry = "main\0"u8;
        fixed (byte* pEntry = entry)
        {
            var stages = stackalloc VkPipelineShaderStageCreateInfo[2];
            stages[0] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Vertex, module = vert, pName = pEntry };
            stages[1] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Fragment, module = frag, pName = pEntry };

            var binding = new VkVertexInputBindingDescription { binding = 0, stride = Vertex.Stride, inputRate = VkVertexInputRate.Vertex };
            var attributes = stackalloc VkVertexInputAttributeDescription[4];
            attributes[0] = new VkVertexInputAttributeDescription { location = 0, binding = 0, format = VkFormat.R32G32B32Sfloat, offset = 0 };
            attributes[1] = new VkVertexInputAttributeDescription { location = 1, binding = 0, format = VkFormat.R32G32B32Sfloat, offset = 12 };
            attributes[2] = new VkVertexInputAttributeDescription { location = 2, binding = 0, format = VkFormat.R32G32B32Sfloat, offset = 24 };
            attributes[3] = new VkVertexInputAttributeDescription { location = 3, binding = 0, format = VkFormat.R32Sfloat, offset = 36 };
            var vertexInput = new VkPipelineVertexInputStateCreateInfo
            {
                vertexBindingDescriptionCount = 1,
                pVertexBindingDescriptions = &binding,
                vertexAttributeDescriptionCount = 4,
                pVertexAttributeDescriptions = attributes,
            };
            var inputAssembly = new VkPipelineInputAssemblyStateCreateInfo { topology = VkPrimitiveTopology.TriangleList };
            var viewportState = new VkPipelineViewportStateCreateInfo { viewportCount = 1, scissorCount = 1 };
            // Geometry is counter-clockwise from outside. The projection's Y flip and Vulkan's
            // Y-down framebuffer cancel out, so front faces stay counter-clockwise.
            var raster = new VkPipelineRasterizationStateCreateInfo
            {
                polygonMode = VkPolygonMode.Fill,
                cullMode = VkCullModeFlags.Back,
                frontFace = VkFrontFace.CounterClockwise,
                lineWidth = 1,
            };
            var multisample = new VkPipelineMultisampleStateCreateInfo { rasterizationSamples = VkSampleCountFlags.Count1 };
            var depth = new VkPipelineDepthStencilStateCreateInfo { depthTestEnable = true, depthWriteEnable = true, depthCompareOp = VkCompareOp.LessOrEqual };
            var blendAttachment = new VkPipelineColorBlendAttachmentState { colorWriteMask = VkColorComponentFlags.All };
            var blend = new VkPipelineColorBlendStateCreateInfo { attachmentCount = 1, pAttachments = &blendAttachment };
            var dynamicStates = stackalloc VkDynamicState[2] { VkDynamicState.Viewport, VkDynamicState.Scissor };
            var dynamic = new VkPipelineDynamicStateCreateInfo { dynamicStateCount = 2, pDynamicStates = dynamicStates };
            var colorFormat = ColorFormat;
            var renderingInfo = new VkPipelineRenderingCreateInfo
            {
                colorAttachmentCount = 1,
                pColorAttachmentFormats = &colorFormat,
                depthAttachmentFormat = DepthFormat,
            };
            var info = new VkGraphicsPipelineCreateInfo
            {
                pNext = &renderingInfo,
                stageCount = 2,
                pStages = stages,
                pVertexInputState = &vertexInput,
                pInputAssemblyState = &inputAssembly,
                pViewportState = &viewportState,
                pRasterizationState = &raster,
                pMultisampleState = &multisample,
                pDepthStencilState = &depth,
                pColorBlendState = &blend,
                pDynamicState = &dynamic,
                layout = layout,
            };
            VkPipeline pipeline;
            Check(Api.vkCreateGraphicsPipelines(VkPipelineCache.Null, 1, &info, null, &pipeline), "vkCreateGraphicsPipelines");
            Api.vkDestroyShaderModule(vert, null);
            Api.vkDestroyShaderModule(frag, null);
            return (layout, pipeline);
        }
    }

    VkShaderModule CreateShader(string name, ShaderKind kind)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Shaders/" + name)
            ?? throw new FileNotFoundException("embedded shader missing", name);
        var source = new StreamReader(stream).ReadToEnd();
        using var compiler = new Compiler();
        var result = compiler.Compile(source, name, new CompilerOptions { ShaderStage = kind, TargetEnv = TargetEnvironmentVersion.Vulkan_1_3 });
        if (result.Status != CompilationStatus.Success)
            throw new InvalidOperationException($"{name}: {result.ErrorMessage}");
        fixed (byte* code = result.Bytecode)
        {
            var info = new VkShaderModuleCreateInfo { codeSize = (nuint)result.Bytecode.Length, pCode = (uint*)code };
            VkShaderModule module;
            Check(Api.vkCreateShaderModule(&info, null, &module), "vkCreateShaderModule");
            return module;
        }
    }

    public void Dispose()
    {
        Api.vkDeviceWaitIdle();
        Api.vkDestroyPipeline(_pipeline, null);
        Api.vkDestroyPipelineLayout(_layout, null);
        if (_vertexCapacity > 0)
        {
            Api.vkDestroyBuffer(_vertices, null);
            Api.vkFreeMemory(_vertexMemory, null);
        }
        Api.vkDestroyBuffer(_readback, null);
        Api.vkFreeMemory(_readbackMemory, null);
        Api.vkDestroyImageView(_colorView, null);
        Api.vkDestroyImage(_color, null);
        Api.vkFreeMemory(_colorMemory, null);
        Api.vkDestroyImageView(_depthView, null);
        Api.vkDestroyImage(_depth, null);
        Api.vkFreeMemory(_depthMemory, null);
    }
}
