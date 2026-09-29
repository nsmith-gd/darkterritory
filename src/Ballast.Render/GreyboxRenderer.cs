using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Vortice.ShaderCompiler;
using Vortice.Vulkan;
using static Ballast.Render.GpuContext;

namespace Ballast.Render;

/// <summary>The frame's constants (std140; mirrors Shaders/frame.glsl).</summary>
[StructLayout(LayoutKind.Sequential)]
unsafe struct FrameData
{
    public const int MaxLights = 32;
    public Matrix4x4 ViewProj;
    public Matrix4x4 InvViewProj;
    public Vector4 Fog;
    public Vector4 FogHeight;
    public Vector4 Moon;
    public Vector4 MoonColour;
    public Vector4 LampPos;
    public Vector4 LampDir;
    public Vector4 LampColour;
    public Vector4 Sky;
    public Vector4 Params;
    public Vector4 Sky2;
    public fixed float Lights[MaxLights * 8];
}

[StructLayout(LayoutKind.Sequential)]
struct DrawConstants
{
    public Matrix4x4 Model;
    public Vector4 Tint;
}

[StructLayout(LayoutKind.Sequential)]
struct PostConstants
{
    public Vector4 A;
    public Vector4 B;
}

/// <summary>
/// The forward renderer (pipeline plan, "Lighting, VFX and post"), into an offscreen colour target with CPU readback.
/// The same passes draw into swapchain and OpenXR images; offscreen is the path agents, tests and golden screenshots use.
/// <list type="number">
/// <item>The sky: gradient, moon, clouds and the backdrop band of far silhouettes.</item>
/// <item>The scene into a float target: the per-frame soup and the cooked kit pieces, textured Blinn-Phong, per-pixel
/// practical lights, height fog.</item>
/// <item>Bloom at half resolution: a threshold and two blur passes.</item>
/// <item>The composite into the frame: grade by LUT, vignette, grain, ordered dither and reduced colour depth; then the
/// 2D overlay (HUD, menus) over it.</item>
/// </list>
/// Without <see cref="Load"/>ed assets it draws the untextured greybox, as it always has.
/// </summary>
public sealed unsafe class GreyboxRenderer : IDisposable
{
    readonly VkFormat _colorFormat;
    const VkFormat DepthFormat = VkFormat.D32Sfloat;
    const VkFormat SceneFormat = VkFormat.R16G16B16A16Sfloat;

    readonly GpuContext _gpu;
    VkDeviceApi Api => _gpu.Api;

    readonly Target _color, _scene, _depth, _bloomA, _bloomB;
    readonly VkBuffer _readback;
    readonly VkDeviceMemory _readbackMemory;
    readonly VkBuffer _frame;
    readonly VkDeviceMemory _frameMemory;
    readonly FrameData* _frameMapped;

    readonly VkDescriptorPool _pool;
    readonly VkDescriptorSetLayout _sceneSetLayout, _postSetLayout, _compositeSetLayout;
    readonly VkDescriptorSet _sceneSet, _brightSet, _blurHSet, _blurVSet, _compositeSet;
    readonly VkPipelineLayout _sceneLayout, _postLayout, _compositeLayout, _overlayLayout;
    readonly VkPipeline _fxAlphaPipeline, _fxAddPipeline;
    VkBuffer _fxVertices;
    VkDeviceMemory _fxMemory;
    ulong _fxCapacity;
    int _fxAlphaCount, _fxAddCount;
    readonly List<FxVertex> _fx = new();
    readonly VkPipeline _skyPipeline, _scenePipeline, _brightPipeline, _blurPipeline, _compositePipeline, _overlayPipeline;
    readonly VkSampler _nearest, _linear;

    GpuTexture _diffuse, _spec, _backdrop, _lut;
    RenderAssets? _assets;

    VkBuffer _vertices;
    VkDeviceMemory _vertexMemory;
    ulong _vertexCapacity;
    int _vertexCount;

    // Cooked meshes on the GPU, for as long as their asset lives (MeshInstance.Asset).
    readonly ConditionalWeakTable<MeshAsset, GpuMesh> _meshes = new();
    readonly List<GpuMesh> _allMeshes = new();
    readonly List<(GpuMesh Mesh, DrawConstants Draw)> _draws = new();
    readonly List<PointLight> _lights = new();

    // The 2D pass over the frame: HUD, prompts, menus (Overlay).
    VkBuffer _overlayVertices;
    VkDeviceMemory _overlayMemory;
    ulong _overlayCapacity;
    int _overlayCount;

    sealed class GpuMesh(VkBuffer buffer, VkDeviceMemory memory, int count)
    {
        public VkBuffer Buffer = buffer;
        public VkDeviceMemory Memory = memory;
        public int Count = count;
    }

    readonly record struct Target(VkImage Image, VkDeviceMemory Memory, VkImageView View, int Width, int Height);

    /// <param name="colorFormat">The frame's format: UNORM, holding display-ready (gamma-encoded) values. A headset renderer
    /// matches the channel order of its sRGB swapchain so the frame copies across bit for bit.</param>
    public GreyboxRenderer(GpuContext gpu, int width, int height, VkFormat colorFormat = VkFormat.R8G8B8A8Unorm)
    {
        _colorFormat = colorFormat;
        _gpu = gpu;
        Width = width;
        Height = height;
        int hw = Math.Max(1, width / 2), hh = Math.Max(1, height / 2);

        _color = CreateTarget(_colorFormat, width, height, VkImageUsageFlags.ColorAttachment | VkImageUsageFlags.TransferSrc, VkImageAspectFlags.Color);
        _scene = CreateTarget(SceneFormat, width, height, VkImageUsageFlags.ColorAttachment | VkImageUsageFlags.Sampled, VkImageAspectFlags.Color);
        _depth = CreateTarget(DepthFormat, width, height, VkImageUsageFlags.DepthStencilAttachment, VkImageAspectFlags.Depth);
        _bloomA = CreateTarget(SceneFormat, hw, hh, VkImageUsageFlags.ColorAttachment | VkImageUsageFlags.Sampled, VkImageAspectFlags.Color);
        _bloomB = CreateTarget(SceneFormat, hw, hh, VkImageUsageFlags.ColorAttachment | VkImageUsageFlags.Sampled, VkImageAspectFlags.Color);
        (_readback, _readbackMemory) = CreateBuffer((ulong)(width * height * 4), VkBufferUsageFlags.TransferDst,
            VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
        (_frame, _frameMemory) = CreateBuffer((ulong)sizeof(FrameData), VkBufferUsageFlags.UniformBuffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
        void* mapped;
        Check(Api.vkMapMemory(_frameMemory, 0, (ulong)sizeof(FrameData), 0, &mapped), "vkMapMemory");
        _frameMapped = (FrameData*)mapped;

        _nearest = GpuTexture.CreateSampler(gpu, VkFilter.Nearest, VkFilter.Nearest, VkSamplerMipmapMode.Nearest, VkSamplerAddressMode.ClampToEdge, VkSamplerAddressMode.ClampToEdge);
        _linear = GpuTexture.CreateSampler(gpu, VkFilter.Linear, VkFilter.Linear, VkSamplerMipmapMode.Nearest, VkSamplerAddressMode.ClampToEdge, VkSamplerAddressMode.ClampToEdge);

        // Descriptor sets: the scene's (frame constants, the material maps, the backdrop), and the post passes'.
        _sceneSetLayout = SetLayout([(VkDescriptorType.UniformBuffer, VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment),
            (VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.Fragment), (VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.Fragment),
            (VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.Fragment)]);
        _postSetLayout = SetLayout([(VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.Fragment)]);
        _compositeSetLayout = SetLayout([(VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.Fragment),
            (VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.Fragment), (VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.Fragment)]);
        var sizes = stackalloc VkDescriptorPoolSize[2];
        sizes[0] = new VkDescriptorPoolSize { type = VkDescriptorType.UniformBuffer, descriptorCount = 2 };
        sizes[1] = new VkDescriptorPoolSize { type = VkDescriptorType.CombinedImageSampler, descriptorCount = 16 };
        var poolInfo = new VkDescriptorPoolCreateInfo { maxSets = 8, poolSizeCount = 2, pPoolSizes = sizes };
        VkDescriptorPool pool;
        Check(Api.vkCreateDescriptorPool(&poolInfo, null, &pool), "vkCreateDescriptorPool");
        _pool = pool;
        _sceneSet = Allocate(_sceneSetLayout);
        _brightSet = Allocate(_postSetLayout);
        _blurHSet = Allocate(_postSetLayout);
        _blurVSet = Allocate(_postSetLayout);
        _compositeSet = Allocate(_compositeSetLayout);

        _sceneLayout = PipelineLayout(_sceneSetLayout, (uint)sizeof(DrawConstants), VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment);
        _postLayout = PipelineLayout(_postSetLayout, (uint)sizeof(PostConstants), VkShaderStageFlags.Fragment);
        _compositeLayout = PipelineLayout(_compositeSetLayout, (uint)sizeof(PostConstants), VkShaderStageFlags.Fragment);
        _overlayLayout = PipelineLayout(null, (uint)sizeof(Vector2), VkShaderStageFlags.Vertex);

        _skyPipeline = Pipeline(_sceneLayout, "fullscreen.vert", "sky.frag", SceneFormat, PipelineKind.Fullscreen, depth: true);
        _scenePipeline = Pipeline(_sceneLayout, "scene.vert", "scene.frag", SceneFormat, PipelineKind.Scene, depth: true);
        _fxAlphaPipeline = Pipeline(_sceneLayout, "fx.vert", "fx.frag", SceneFormat, PipelineKind.FxAlpha, depth: true);
        _fxAddPipeline = Pipeline(_sceneLayout, "fx.vert", "fx.frag", SceneFormat, PipelineKind.FxAdditive, depth: true);
        _brightPipeline = Pipeline(_postLayout, "fullscreen.vert", "bright.frag", SceneFormat, PipelineKind.Fullscreen, depth: false);
        _blurPipeline = Pipeline(_postLayout, "fullscreen.vert", "blur.frag", SceneFormat, PipelineKind.Fullscreen, depth: false);
        _compositePipeline = Pipeline(_compositeLayout, "fullscreen.vert", "composite.frag", _colorFormat, PipelineKind.Fullscreen, depth: false);
        _overlayPipeline = Pipeline(_overlayLayout, "overlay.vert", "overlay.frag", _colorFormat, PipelineKind.Overlay, depth: false);

        // Until there are assets: one plain white layer (the greybox's flat colour), no backdrop, no grade.
        (_diffuse, _spec, _backdrop, _lut) = Upload(new RenderAssets());
        WriteSets();
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>The rendered frame. After <see cref="Record"/> it is in TransferSrcOptimal layout.</summary>
    public VkImage ColorImage => _color.Image;

    /// <summary>The post stack's settings: from the loaded assets, or the defaults.</summary>
    public PostSettings Post { get; set; } = new();

    /// <summary>Counts from the last frame recorded, for budgets (pipeline "frame-level ceilings").</summary>
    public (int Triangles, int Draws, int Lights) Stats { get; private set; }

    /// <summary>Uploads the materials, the backdrop and the grade, replacing what was there. Call outside command recording.</summary>
    public void Load(RenderAssets assets)
    {
        Api.vkDeviceWaitIdle();
        _diffuse.Dispose();
        _spec.Dispose();
        _backdrop.Dispose();
        _lut.Dispose();
        (_diffuse, _spec, _backdrop, _lut) = Upload(assets);
        _assets = assets;
        Post = assets.Post;
        WriteSets();
    }

    /// <summary>The loaded material layer called <paramref name="name"/>, or −1.</summary>
    public int LayerOf(string name) => _assets?.IndexOf(name) ?? -1;

    (GpuTexture, GpuTexture, GpuTexture, GpuTexture) Upload(RenderAssets assets)
    {
        int size = assets.LayerSize;
        var layers = assets.Layers.Count > 0 ? assets.Layers : [new MaterialLayer("white", Image.Solid(size, 255, 255, 255), Image.Solid(size, 0, 0, 0))];
        var diffuse = layers.Select(l => (IReadOnlyList<byte[]>)GpuTexture.MipChain(l.Diffuse.Resized(size, size))).ToList();
        var spec = layers.Select(l => (IReadOnlyList<byte[]>)GpuTexture.MipChain(l.Spec.Resized(size, size))).ToList();
        float bias = assets.Post.MipBias;
        var d = new GpuTexture(_gpu, GpuTexture.Kind.Array2D, VkFormat.R8G8B8A8Srgb, size, size, diffuse, VkFilter.Nearest,
            VkSamplerAddressMode.Repeat, VkSamplerAddressMode.Repeat, bias);
        var s = new GpuTexture(_gpu, GpuTexture.Kind.Array2D, VkFormat.R8G8B8A8Unorm, size, size, spec, VkFilter.Nearest,
            VkSamplerAddressMode.Repeat, VkSamplerAddressMode.Repeat, bias);
        var sky = assets.Backdrop ?? Image.Solid(4, 0, 0, 0, 0);
        var b = new GpuTexture(_gpu, GpuTexture.Kind.Image2D, VkFormat.R8G8B8A8Srgb, sky.Width, sky.Height, [GpuTexture.MipChain(sky)], VkFilter.Nearest,
            VkSamplerAddressMode.Repeat, VkSamplerAddressMode.ClampToEdge, bias);
        var lut = new GpuTexture(_gpu, GpuTexture.Kind.Volume, VkFormat.R8G8B8A8Unorm, ColourGrade.Size, ColourGrade.Size, [[assets.Lut ?? ColourGrade.Identity()]],
            VkFilter.Linear, VkSamplerAddressMode.ClampToEdge, VkSamplerAddressMode.ClampToEdge, depth: ColourGrade.Size);
        return (d, s, b, lut);
    }

    /// <summary>Draws the mesh (camera-relative positions) and returns the frame as RGBA8, top row first.</summary>
    /// <param name="clearColor">Linear colour at the horizon (the fog's, usually).</param>
    /// <param name="overlay">2D drawing over the frame (the HUD), or null.</param>
    public byte[] Render(MeshBuilder mesh, in Camera camera, in FrameLighting lighting, Vector3 clearColor, Overlay? overlay = null)
    {
        Prepare(mesh, overlay);
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
            Api.vkCmdCopyImageToBuffer(cmd, _color.Image, VkImageLayout.TransferSrcOptimal, _readback, 1, &region);
        });

        var pixels = new byte[Width * Height * 4];
        void* mapped;
        Check(Api.vkMapMemory(_readbackMemory, 0, (ulong)pixels.Length, 0, &mapped), "vkMapMemory");
        new ReadOnlySpan<byte>(mapped, pixels.Length).CopyTo(pixels);
        Api.vkUnmapMemory(_readbackMemory);
        return pixels;
    }

    /// <summary>Uploads geometry, kit instances and lights (and the overlay, if any) for the next <see cref="Record"/>. Call outside command recording.</summary>
    public void Prepare(MeshBuilder mesh, Overlay? overlay = null)
    {
        Upload(mesh.Vertices, (uint)Vertex.Stride, ref _vertices, ref _vertexMemory, ref _vertexCapacity);
        _vertexCount = mesh.Count;
        _draws.Clear();
        foreach (var instance in mesh.Instances)
        {
            if (instance.Asset.Vertices.Length == 0)
                continue;
            if (!_meshes.TryGetValue(instance.Asset, out var gpuMesh))
            {
                ulong capacity = 0;
                VkBuffer buffer = default;
                VkDeviceMemory memory = default;
                Upload<Vertex>(instance.Asset.Vertices, (uint)Vertex.Stride, ref buffer, ref memory, ref capacity);
                gpuMesh = new GpuMesh(buffer, memory, instance.Asset.Vertices.Length);
                _meshes.Add(instance.Asset, gpuMesh);
                _allMeshes.Add(gpuMesh);
            }
            var tint = instance.Tint == default ? Vector3.One : instance.Tint;
            _draws.Add((gpuMesh, new DrawConstants { Model = instance.Model, Tint = new Vector4(tint, instance.Glow) }));
        }
        _lights.Clear();
        _lights.AddRange(mesh.PointLights);
        if (_lights.Count > FrameData.MaxLights)
        {
            // The nearest win: past the budget, a light far off lights little you can see.
            _lights.Sort((a, b) => (a.Position.LengthSquared() - a.Range * a.Range * 0.25f).CompareTo(b.Position.LengthSquared() - b.Range * b.Range * 0.25f));
            _lights.RemoveRange(FrameData.MaxLights, _lights.Count - FrameData.MaxLights);
        }
        // Effects: smoke and dust sorted far to near (they're blended), then the additive glows (order doesn't matter).
        _fx.Clear();
        var alpha = CollectionsMarshal.AsSpan(mesh.AlphaFx);
        int tris = alpha.Length / 3;
        if (tris > 0)
        {
            var order = new (float Distance, int Index)[tris];
            for (int i = 0; i < tris; i++)
                order[i] = (-(alpha[i * 3].Position + alpha[i * 3 + 1].Position + alpha[i * 3 + 2].Position).LengthSquared(), i);
            Array.Sort(order, (a, b) => a.Distance.CompareTo(b.Distance));
            foreach (var (_, i) in order)
            {
                _fx.Add(alpha[i * 3]);
                _fx.Add(alpha[i * 3 + 1]);
                _fx.Add(alpha[i * 3 + 2]);
            }
        }
        _fxAlphaCount = _fx.Count;
        _fx.AddRange(mesh.AdditiveFx);
        _fxAddCount = _fx.Count - _fxAlphaCount;
        if (_fx.Count > 0)
            Upload(CollectionsMarshal.AsSpan(_fx), (uint)FxVertex.Stride, ref _fxVertices, ref _fxMemory, ref _fxCapacity);
        _overlayCount = overlay?.Count ?? 0;
        if (overlay is { Count: > 0 })
            Upload(CollectionsMarshal.AsSpan(overlay.Vertices), OverlayVertex.Stride, ref _overlayVertices, ref _overlayMemory, ref _overlayCapacity);
    }

    void Upload<T>(ReadOnlySpan<T> data, uint stride, ref VkBuffer buffer, ref VkDeviceMemory memory, ref ulong capacity) where T : unmanaged
    {
        ulong size = (ulong)Math.Max(1, data.Length) * stride;
        if (size > capacity)
        {
            if (capacity > 0)
            {
                Api.vkDestroyBuffer(buffer, null);
                Api.vkFreeMemory(memory, null);
            }
            capacity = Math.Max(size, capacity * 2);
            (buffer, memory) = CreateBuffer(capacity, VkBufferUsageFlags.VertexBuffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
        }
        if (data.IsEmpty)
            return;
        void* mapped;
        Check(Api.vkMapMemory(memory, 0, size, 0, &mapped), "vkMapMemory");
        MemoryMarshal.AsBytes(data).CopyTo(new Span<byte>(mapped, (int)size));
        Api.vkUnmapMemory(memory);
    }

    void WriteFrame(in Camera camera, in FrameLighting lighting, Vector3 horizon)
    {
        var viewProj = camera.ViewProjection((float)Width / Height);
        Matrix4x4.Invert(viewProj, out var inverse);
        float fogBase = double.IsNaN(lighting.FogBase) ? -1.7f : (float)(lighting.FogBase - camera.Position.Y);
        var f = _frameMapped;
        f->ViewProj = viewProj;
        f->InvViewProj = inverse;
        f->Fog = new Vector4(lighting.FogColor, lighting.FogDensity);
        f->FogHeight = new Vector4(fogBase, lighting.FogHeightFalloff, lighting.FogFloor, (float)(lighting.Time % 10000));
        f->Moon = new Vector4(lighting.MoonDirection, lighting.Ambient);
        f->MoonColour = new Vector4(lighting.MoonColour, lighting.MoonStrength);
        f->LampPos = new Vector4(lighting.LampPosition.RelativeTo(camera.Position), lighting.LampRange);
        f->LampDir = new Vector4(lighting.LampDirection, MathF.Cos(lighting.LampConeDegrees * MathF.PI / 180));
        f->LampColour = new Vector4(lighting.LampColour, lighting.LampIntensity);
        f->Sky = new Vector4(Post.SkyZenith, Post.BackdropFog);
        f->Sky2 = new Vector4(Post.BackdropDegrees * MathF.PI / 180, MathF.Max(0.2f, lighting.FogCurve), Post.HorizonGlow, 0);
        f->Params = new Vector4(_lights.Count, Post.Ps2 ? 1 : 0, Post.TexturedWear, _assets?.Layers.Count ?? 0);
        for (int i = 0; i < _lights.Count; i++)
        {
            var l = _lights[i];
            // Positions are relative to the scene's origin, the camera's position; a headset eye sits a few
            // centimetres off it, but the eye offset is in the view matrix, not the geometry.
            f->Lights[i * 8] = l.Position.X;
            f->Lights[i * 8 + 1] = l.Position.Y;
            f->Lights[i * 8 + 2] = l.Position.Z;
            f->Lights[i * 8 + 3] = l.Range;
            f->Lights[i * 8 + 4] = l.Colour.X;
            f->Lights[i * 8 + 5] = l.Colour.Y;
            f->Lights[i * 8 + 6] = l.Colour.Z;
        }
        _ = horizon;
    }

    /// <summary>Records the frame into <see cref="ColorImage"/>, leaving it ready to copy or blit.</summary>
    /// <param name="clearColor">The horizon's colour (the sky fades from it to the zenith).</param>
    public void Record(VkCommandBuffer cmd, in Camera camera, in FrameLighting lighting, Vector3 clearColor)
    {
        var light = lighting;
        light.FogColor = clearColor;
        WriteFrame(camera, light, clearColor);
        int triangles = _vertexCount / 3;

        // 1-2: sky and scene into the float target.
        Transition(cmd, _scene.Image, VkImageAspectFlags.Color, VkImageLayout.Undefined, VkImageLayout.ColorAttachmentOptimal);
        Transition(cmd, _depth.Image, VkImageAspectFlags.Depth, VkImageLayout.Undefined, VkImageLayout.DepthAttachmentOptimal);
        BeginRendering(cmd, _scene, withDepth: true);
        var sceneSet = _sceneSet;
        Api.vkCmdBindDescriptorSets(cmd, VkPipelineBindPoint.Graphics, _sceneLayout, 0, 1, &sceneSet, 0, null);
        var identity = new DrawConstants { Model = Matrix4x4.Identity, Tint = Vector4.One };
        Api.vkCmdBindPipeline(cmd, VkPipelineBindPoint.Graphics, _skyPipeline);
        Api.vkCmdPushConstants(cmd, _sceneLayout, VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment, 0, (uint)sizeof(DrawConstants), &identity);
        Api.vkCmdDraw(cmd, 3, 1, 0, 0);
        Api.vkCmdBindPipeline(cmd, VkPipelineBindPoint.Graphics, _scenePipeline);
        if (_vertexCount > 0)
        {
            Api.vkCmdPushConstants(cmd, _sceneLayout, VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment, 0, (uint)sizeof(DrawConstants), &identity);
            var vb = _vertices;
            ulong offset = 0;
            Api.vkCmdBindVertexBuffers(cmd, 0, 1, &vb, &offset);
            Api.vkCmdDraw(cmd, (uint)_vertexCount, 1, 0, 0);
        }
        foreach (var (mesh, draw) in _draws)
        {
            var d = draw;
            Api.vkCmdPushConstants(cmd, _sceneLayout, VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment, 0, (uint)sizeof(DrawConstants), &d);
            var vb = mesh.Buffer;
            ulong offset = 0;
            Api.vkCmdBindVertexBuffers(cmd, 0, 1, &vb, &offset);
            Api.vkCmdDraw(cmd, (uint)mesh.Count, 1, 0, 0);
            triangles += mesh.Count / 3;
        }
        if (_fxAlphaCount + _fxAddCount > 0)
        {
            var fb = _fxVertices;
            ulong offset = 0;
            Api.vkCmdBindVertexBuffers(cmd, 0, 1, &fb, &offset);
            if (_fxAlphaCount > 0)
            {
                Api.vkCmdBindPipeline(cmd, VkPipelineBindPoint.Graphics, _fxAlphaPipeline);
                var d = new DrawConstants { Model = Matrix4x4.Identity, Tint = new Vector4(1, 1, 1, 0) };
                Api.vkCmdPushConstants(cmd, _sceneLayout, VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment, 0, (uint)sizeof(DrawConstants), &d);
                Api.vkCmdDraw(cmd, (uint)_fxAlphaCount, 1, 0, 0);
            }
            if (_fxAddCount > 0)
            {
                Api.vkCmdBindPipeline(cmd, VkPipelineBindPoint.Graphics, _fxAddPipeline);
                var d = new DrawConstants { Model = Matrix4x4.Identity, Tint = new Vector4(1, 1, 1, 1) };
                Api.vkCmdPushConstants(cmd, _sceneLayout, VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment, 0, (uint)sizeof(DrawConstants), &d);
                Api.vkCmdDraw(cmd, (uint)_fxAddCount, 1, (uint)_fxAlphaCount, 0);
            }
        }
        Api.vkCmdEndRendering(cmd);
        Transition(cmd, _scene.Image, VkImageAspectFlags.Color, VkImageLayout.ColorAttachmentOptimal, VkImageLayout.ShaderReadOnlyOptimal);
        Stats = (triangles, _draws.Count + 2, _lights.Count);

        // 3: bloom at half resolution.
        var bloomStep = new Vector2(1f / _bloomA.Width, 1f / _bloomA.Height);
        PostPass(cmd, _bloomA, _brightPipeline, _postLayout, _brightSet, new PostConstants { A = new Vector4(Post.BloomThreshold, 0, 0, 0) });
        PostPass(cmd, _bloomB, _blurPipeline, _postLayout, _blurHSet, new PostConstants { A = new Vector4(bloomStep.X, 0, 0, 0) });
        PostPass(cmd, _bloomA, _blurPipeline, _postLayout, _blurVSet, new PostConstants { A = new Vector4(0, bloomStep.Y, 0, 0) });

        // 4: the composite and the overlay, into the frame.
        Transition(cmd, _color.Image, VkImageAspectFlags.Color, VkImageLayout.Undefined, VkImageLayout.ColorAttachmentOptimal);
        BeginRendering(cmd, _color, withDepth: false);
        var composite = new PostConstants
        {
            A = new Vector4(Post.BloomStrength, Post.Vignette, Post.Grain, Post.ColourLevels),
            B = new Vector4((float)(lighting.Time * 30 % 997), (float)Width / Height, Post.Ps2 ? 1 : 0, 0),
        };
        var compositeSet = _compositeSet;
        Api.vkCmdBindPipeline(cmd, VkPipelineBindPoint.Graphics, _compositePipeline);
        Api.vkCmdBindDescriptorSets(cmd, VkPipelineBindPoint.Graphics, _compositeLayout, 0, 1, &compositeSet, 0, null);
        Api.vkCmdPushConstants(cmd, _compositeLayout, VkShaderStageFlags.Fragment, 0, (uint)sizeof(PostConstants), &composite);
        Api.vkCmdDraw(cmd, 3, 1, 0, 0);
        if (_overlayCount > 0)
        {
            Api.vkCmdBindPipeline(cmd, VkPipelineBindPoint.Graphics, _overlayPipeline);
            var size = new Vector2(Width, Height);
            Api.vkCmdPushConstants(cmd, _overlayLayout, VkShaderStageFlags.Vertex, 0, (uint)sizeof(Vector2), &size);
            var ob = _overlayVertices;
            ulong zero = 0;
            Api.vkCmdBindVertexBuffers(cmd, 0, 1, &ob, &zero);
            Api.vkCmdDraw(cmd, (uint)_overlayCount, 1, 0, 0);
        }
        Api.vkCmdEndRendering(cmd);
        Transition(cmd, _color.Image, VkImageAspectFlags.Color, VkImageLayout.ColorAttachmentOptimal, VkImageLayout.TransferSrcOptimal);
    }

    void PostPass(VkCommandBuffer cmd, Target into, VkPipeline pipeline, VkPipelineLayout layout, VkDescriptorSet set, PostConstants constants)
    {
        Transition(cmd, into.Image, VkImageAspectFlags.Color, VkImageLayout.Undefined, VkImageLayout.ColorAttachmentOptimal);
        BeginRendering(cmd, into, withDepth: false);
        Api.vkCmdBindPipeline(cmd, VkPipelineBindPoint.Graphics, pipeline);
        Api.vkCmdBindDescriptorSets(cmd, VkPipelineBindPoint.Graphics, layout, 0, 1, &set, 0, null);
        Api.vkCmdPushConstants(cmd, layout, VkShaderStageFlags.Fragment, 0, (uint)sizeof(PostConstants), &constants);
        Api.vkCmdDraw(cmd, 3, 1, 0, 0);
        Api.vkCmdEndRendering(cmd);
        Transition(cmd, into.Image, VkImageAspectFlags.Color, VkImageLayout.ColorAttachmentOptimal, VkImageLayout.ShaderReadOnlyOptimal);
    }

    void BeginRendering(VkCommandBuffer cmd, Target target, bool withDepth)
    {
        var colorAttachment = new VkRenderingAttachmentInfo
        {
            imageView = target.View,
            imageLayout = VkImageLayout.ColorAttachmentOptimal,
            loadOp = VkAttachmentLoadOp.DontCare,
            storeOp = VkAttachmentStoreOp.Store,
        };
        var depthAttachment = new VkRenderingAttachmentInfo
        {
            imageView = _depth.View,
            imageLayout = VkImageLayout.DepthAttachmentOptimal,
            loadOp = VkAttachmentLoadOp.Clear,
            storeOp = VkAttachmentStoreOp.DontCare,
            clearValue = new VkClearValue { depthStencil = new VkClearDepthStencilValue(1, 0) },
        };
        var rendering = new VkRenderingInfo
        {
            renderArea = new VkRect2D(0, 0, (uint)target.Width, (uint)target.Height),
            layerCount = 1,
            colorAttachmentCount = 1,
            pColorAttachments = &colorAttachment,
            pDepthAttachment = withDepth ? &depthAttachment : null,
        };
        Api.vkCmdBeginRendering(cmd, &rendering);
        var viewport = new VkViewport(0, 0, target.Width, target.Height, 0, 1);
        Api.vkCmdSetViewport(cmd, 0, 1, &viewport);
        var scissor = new VkRect2D(0, 0, (uint)target.Width, (uint)target.Height);
        Api.vkCmdSetScissor(cmd, 0, 1, &scissor);
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

    Target CreateTarget(VkFormat format, int width, int height, VkImageUsageFlags usage, VkImageAspectFlags aspect)
    {
        var info = new VkImageCreateInfo
        {
            imageType = VkImageType.Image2D,
            format = format,
            extent = new VkExtent3D(width, height, 1),
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
        return new Target(image, memory, view, width, height);
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

    VkDescriptorSetLayout SetLayout((VkDescriptorType Type, VkShaderStageFlags Stages)[] bindings)
    {
        var b = stackalloc VkDescriptorSetLayoutBinding[bindings.Length];
        for (int i = 0; i < bindings.Length; i++)
            b[i] = new VkDescriptorSetLayoutBinding { binding = (uint)i, descriptorType = bindings[i].Type, descriptorCount = 1, stageFlags = bindings[i].Stages };
        var info = new VkDescriptorSetLayoutCreateInfo { bindingCount = (uint)bindings.Length, pBindings = b };
        VkDescriptorSetLayout layout;
        Check(Api.vkCreateDescriptorSetLayout(&info, null, &layout), "vkCreateDescriptorSetLayout");
        return layout;
    }

    VkDescriptorSet Allocate(VkDescriptorSetLayout layout)
    {
        var info = new VkDescriptorSetAllocateInfo { descriptorPool = _pool, descriptorSetCount = 1, pSetLayouts = &layout };
        VkDescriptorSet set;
        Check(Api.vkAllocateDescriptorSets(&info, &set), "vkAllocateDescriptorSets");
        return set;
    }

    void WriteSets()
    {
        var images = stackalloc VkDescriptorImageInfo[10];
        images[0] = new VkDescriptorImageInfo { sampler = _diffuse.Sampler, imageView = _diffuse.View, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        images[1] = new VkDescriptorImageInfo { sampler = _spec.Sampler, imageView = _spec.View, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        images[2] = new VkDescriptorImageInfo { sampler = _backdrop.Sampler, imageView = _backdrop.View, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        images[3] = new VkDescriptorImageInfo { sampler = _linear, imageView = _scene.View, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        images[4] = new VkDescriptorImageInfo { sampler = _linear, imageView = _bloomA.View, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        images[5] = new VkDescriptorImageInfo { sampler = _linear, imageView = _bloomB.View, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        images[6] = new VkDescriptorImageInfo { sampler = _nearest, imageView = _scene.View, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        images[7] = new VkDescriptorImageInfo { sampler = _linear, imageView = _bloomA.View, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        images[8] = new VkDescriptorImageInfo { sampler = _lut.Sampler, imageView = _lut.View, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        var buffer = new VkDescriptorBufferInfo { buffer = _frame, offset = 0, range = (ulong)sizeof(FrameData) };
        var writes = stackalloc VkWriteDescriptorSet[10];
        VkWriteDescriptorSet Image(VkDescriptorSet set, uint binding, int image) => new()
        {
            dstSet = set,
            dstBinding = binding,
            descriptorCount = 1,
            descriptorType = VkDescriptorType.CombinedImageSampler,
            pImageInfo = &images[image],
        };
        writes[0] = new VkWriteDescriptorSet { dstSet = _sceneSet, dstBinding = 0, descriptorCount = 1, descriptorType = VkDescriptorType.UniformBuffer, pBufferInfo = &buffer };
        writes[1] = Image(_sceneSet, 1, 0);
        writes[2] = Image(_sceneSet, 2, 1);
        writes[3] = Image(_sceneSet, 3, 2);
        writes[4] = Image(_brightSet, 0, 3);
        writes[5] = Image(_blurHSet, 0, 4);
        writes[6] = Image(_blurVSet, 0, 5);
        writes[7] = Image(_compositeSet, 0, 6);
        writes[8] = Image(_compositeSet, 1, 7);
        writes[9] = Image(_compositeSet, 2, 8);
        Api.vkUpdateDescriptorSets(10, writes, 0, null);
    }

    VkPipelineLayout PipelineLayout(VkDescriptorSetLayout? set, uint pushSize, VkShaderStageFlags pushStages)
    {
        var push = new VkPushConstantRange { stageFlags = pushStages, size = pushSize };
        var setLayout = set ?? default;
        var info = new VkPipelineLayoutCreateInfo
        {
            setLayoutCount = set is null ? 0u : 1u,
            pSetLayouts = set is null ? null : &setLayout,
            pushConstantRangeCount = 1,
            pPushConstantRanges = &push,
        };
        VkPipelineLayout layout;
        Check(Api.vkCreatePipelineLayout(&info, null, &layout), "vkCreatePipelineLayout");
        return layout;
    }

    enum PipelineKind { Scene, Fullscreen, Overlay, FxAlpha, FxAdditive }

    VkPipeline Pipeline(VkPipelineLayout layout, string vertName, string fragName, VkFormat colorFormat, PipelineKind kind, bool depth)
    {
        var vert = CreateShader(vertName, ShaderKind.VertexShader);
        var frag = CreateShader(fragName, ShaderKind.FragmentShader);
        var entry = "main\0"u8;
        fixed (byte* pEntry = entry)
        {
            var stages = stackalloc VkPipelineShaderStageCreateInfo[2];
            stages[0] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Vertex, module = vert, pName = pEntry };
            stages[1] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Fragment, module = frag, pName = pEntry };

            var attributes = stackalloc VkVertexInputAttributeDescription[11];
            var binding = new VkVertexInputBindingDescription { binding = 0, inputRate = VkVertexInputRate.Vertex };
            uint attributeCount = 0;
            if (kind == PipelineKind.Scene)
            {
                binding.stride = Vertex.Stride;
                (VkFormat, uint)[] layoutOf =
                [
                    (VkFormat.R32G32B32Sfloat, 0), (VkFormat.R32G32B32Sfloat, 12), (VkFormat.R32G32B32Sfloat, 24), (VkFormat.R32Sfloat, 36),
                    // The surface treatment (T39): texel coordinates, wear, shine; then the texture's coordinates and layer.
                    (VkFormat.R32G32B32Sfloat, 40), (VkFormat.R32Sfloat, 52), (VkFormat.R32Sfloat, 56), (VkFormat.R32G32Sfloat, 60), (VkFormat.R32Sfloat, 68),
                    (VkFormat.R32Sfloat, 72), (VkFormat.R32Sfloat, 76),
                ];
                for (int i = 0; i < layoutOf.Length; i++)
                    attributes[i] = new VkVertexInputAttributeDescription { location = (uint)i, binding = 0, format = layoutOf[i].Item1, offset = layoutOf[i].Item2 };
                attributeCount = (uint)layoutOf.Length;
            }
            else if (kind is PipelineKind.FxAlpha or PipelineKind.FxAdditive)
            {
                binding.stride = FxVertex.Stride;
                attributes[0] = new VkVertexInputAttributeDescription { location = 0, binding = 0, format = VkFormat.R32G32B32Sfloat, offset = 0 };
                attributes[1] = new VkVertexInputAttributeDescription { location = 1, binding = 0, format = VkFormat.R32G32Sfloat, offset = 12 };
                attributes[2] = new VkVertexInputAttributeDescription { location = 2, binding = 0, format = VkFormat.R32G32B32A32Sfloat, offset = 20 };
                attributes[3] = new VkVertexInputAttributeDescription { location = 3, binding = 0, format = VkFormat.R32Sfloat, offset = 36 };
                attributeCount = 4;
            }
            else if (kind == PipelineKind.Overlay)
            {
                binding.stride = OverlayVertex.Stride;
                attributes[0] = new VkVertexInputAttributeDescription { location = 0, binding = 0, format = VkFormat.R32G32Sfloat, offset = 0 };
                attributes[1] = new VkVertexInputAttributeDescription { location = 1, binding = 0, format = VkFormat.R32G32B32A32Sfloat, offset = 8 };
                attributeCount = 2;
            }
            var vertexInput = new VkPipelineVertexInputStateCreateInfo
            {
                vertexBindingDescriptionCount = attributeCount > 0 ? 1u : 0u,
                pVertexBindingDescriptions = &binding,
                vertexAttributeDescriptionCount = attributeCount,
                pVertexAttributeDescriptions = attributes,
            };
            var inputAssembly = new VkPipelineInputAssemblyStateCreateInfo { topology = VkPrimitiveTopology.TriangleList };
            var viewportState = new VkPipelineViewportStateCreateInfo { viewportCount = 1, scissorCount = 1 };
            // Geometry is counter-clockwise from outside. The projection's Y flip and Vulkan's
            // Y-down framebuffer cancel out, so front faces stay counter-clockwise.
            var raster = new VkPipelineRasterizationStateCreateInfo
            {
                polygonMode = VkPolygonMode.Fill,
                cullMode = kind == PipelineKind.Scene ? VkCullModeFlags.Back : VkCullModeFlags.None,
                frontFace = VkFrontFace.CounterClockwise,
                lineWidth = 1,
            };
            var multisample = new VkPipelineMultisampleStateCreateInfo { rasterizationSamples = VkSampleCountFlags.Count1 };
            // The sky sits at the far plane under everything, and writes no depth.
            var depthState = new VkPipelineDepthStencilStateCreateInfo
            {
                depthTestEnable = depth,
                depthWriteEnable = depth && kind == PipelineKind.Scene,
                depthCompareOp = VkCompareOp.LessOrEqual,
            };
            var blendAttachment = kind == PipelineKind.FxAdditive
                ? new VkPipelineColorBlendAttachmentState
                {
                    blendEnable = true,
                    srcColorBlendFactor = VkBlendFactor.One,
                    dstColorBlendFactor = VkBlendFactor.One,
                    colorBlendOp = VkBlendOp.Add,
                    srcAlphaBlendFactor = VkBlendFactor.Zero,
                    dstAlphaBlendFactor = VkBlendFactor.One,
                    alphaBlendOp = VkBlendOp.Add,
                    colorWriteMask = VkColorComponentFlags.All,
                }
                : kind is PipelineKind.Overlay or PipelineKind.FxAlpha
                ? new VkPipelineColorBlendAttachmentState
                {
                    blendEnable = true,
                    srcColorBlendFactor = VkBlendFactor.SrcAlpha,
                    dstColorBlendFactor = VkBlendFactor.OneMinusSrcAlpha,
                    colorBlendOp = VkBlendOp.Add,
                    srcAlphaBlendFactor = VkBlendFactor.One,
                    dstAlphaBlendFactor = VkBlendFactor.OneMinusSrcAlpha,
                    alphaBlendOp = VkBlendOp.Add,
                    colorWriteMask = VkColorComponentFlags.All,
                }
                : new VkPipelineColorBlendAttachmentState { colorWriteMask = VkColorComponentFlags.All };
            var blend = new VkPipelineColorBlendStateCreateInfo { attachmentCount = 1, pAttachments = &blendAttachment };
            var dynamicStates = stackalloc VkDynamicState[2] { VkDynamicState.Viewport, VkDynamicState.Scissor };
            var dynamic = new VkPipelineDynamicStateCreateInfo { dynamicStateCount = 2, pDynamicStates = dynamicStates };
            var renderingInfo = new VkPipelineRenderingCreateInfo
            {
                colorAttachmentCount = 1,
                pColorAttachmentFormats = &colorFormat,
                depthAttachmentFormat = depth ? DepthFormat : VkFormat.Undefined,
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
                pDepthStencilState = &depthState,
                pColorBlendState = &blend,
                pDynamicState = &dynamic,
                layout = layout,
            };
            VkPipeline pipeline;
            Check(Api.vkCreateGraphicsPipelines(VkPipelineCache.Null, 1, &info, null, &pipeline), "vkCreateGraphicsPipelines");
            Api.vkDestroyShaderModule(vert, null);
            Api.vkDestroyShaderModule(frag, null);
            return pipeline;
        }
    }

    /// <summary>A shader's GLSL from the embedded resources, with <c>#include "x"</c> lines replaced by the named file.</summary>
    static string ShaderSource(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Shaders/" + name)
            ?? throw new FileNotFoundException("embedded shader missing", name);
        var lines = new StreamReader(stream).ReadToEnd().Split('\n');
        return string.Join('\n', lines.Select(l => l.TrimStart().StartsWith("#include \"") ? ShaderSource(l.Trim()[10..^1]) : l));
    }

    VkShaderModule CreateShader(string name, ShaderKind kind)
    {
        var source = ShaderSource(name);
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

    void DestroyTarget(Target t)
    {
        Api.vkDestroyImageView(t.View, null);
        Api.vkDestroyImage(t.Image, null);
        Api.vkFreeMemory(t.Memory, null);
    }

    public void Dispose()
    {
        Api.vkDeviceWaitIdle();
        if (_fxCapacity > 0)
        {
            Api.vkDestroyBuffer(_fxVertices, null);
            Api.vkFreeMemory(_fxMemory, null);
        }
        foreach (var p in new[] { _fxAlphaPipeline, _fxAddPipeline, _skyPipeline, _scenePipeline, _brightPipeline, _blurPipeline, _compositePipeline, _overlayPipeline })
            Api.vkDestroyPipeline(p, null);
        foreach (var l in new[] { _sceneLayout, _postLayout, _compositeLayout, _overlayLayout })
            Api.vkDestroyPipelineLayout(l, null);
        Api.vkDestroyDescriptorPool(_pool, null);
        foreach (var l in new[] { _sceneSetLayout, _postSetLayout, _compositeSetLayout })
            Api.vkDestroyDescriptorSetLayout(l, null);
        _diffuse.Dispose();
        _spec.Dispose();
        _backdrop.Dispose();
        _lut.Dispose();
        Api.vkDestroySampler(_nearest, null);
        Api.vkDestroySampler(_linear, null);
        foreach (var m in _allMeshes)
        {
            Api.vkDestroyBuffer(m.Buffer, null);
            Api.vkFreeMemory(m.Memory, null);
        }
        if (_overlayCapacity > 0)
        {
            Api.vkDestroyBuffer(_overlayVertices, null);
            Api.vkFreeMemory(_overlayMemory, null);
        }
        if (_vertexCapacity > 0)
        {
            Api.vkDestroyBuffer(_vertices, null);
            Api.vkFreeMemory(_vertexMemory, null);
        }
        Api.vkUnmapMemory(_frameMemory);
        Api.vkDestroyBuffer(_frame, null);
        Api.vkFreeMemory(_frameMemory, null);
        Api.vkDestroyBuffer(_readback, null);
        Api.vkFreeMemory(_readbackMemory, null);
        foreach (var t in new[] { _color, _scene, _depth, _bloomA, _bloomB })
            DestroyTarget(t);
    }
}
