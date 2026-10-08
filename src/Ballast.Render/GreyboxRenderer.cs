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
    public const int MaxRooms = 16;
    public Matrix4x4 ViewProj;
    public Matrix4x4 InvViewProj;
    public Matrix4x4 LampViewProj;
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
    public fixed float Rooms[MaxRooms * 12];
    public Vector4 Counts;
    public Matrix4x4 MoonViewProj;
    public fixed float HeroOf[256];
    /// <summary>xyz the glow low on the dawn's horizon, w how far it's up (0..1).</summary>
    public Vector4 Dawn;
    /// <summary>xyz the wind (m/s, world axes), w how gusty.</summary>
    public Vector4 Wind;
    /// <summary>Per layer, how it moves (<see cref="GreyboxRenderer.Motion"/>): 1 bends in the wind, 2 is water, else 0.</summary>
    public fixed float MotionOf[256];
    /// <summary>The right eye's, when one pass draws both (<see cref="GreyboxRenderer.Views"/> 2; Shaders/view.glsl).</summary>
    public Matrix4x4 ViewProj1;
    public Matrix4x4 InvViewProj1;
    /// <summary>The shadowed hand lamp (<see cref="MeshBuilder.ShadowLight"/>): xyz camera-relative, w its range (0: none).</summary>
    public Vector4 HandPos;
    /// <summary>rgb its colour, a 1 when its cube shadow is drawn.</summary>
    public Vector4 HandColour;
    /// <summary>Its cube's six faces (+X, −X, +Y, −Y, +Z, −Z), camera-relative, one layer each of its shadow map.</summary>
    public fixed float HandViewProj[6 * 16];
}

[StructLayout(LayoutKind.Sequential)]
struct DrawConstants
{
    public Matrix4x4 Model;
    public Vector4 Tint;
    /// <summary>x how scarred (0..1), y the scar pattern's seed (see <see cref="MeshInstance.Scar"/>).</summary>
    public Vector4 Scar;
    /// <summary>xyz added to the surface's texel coordinates (<see cref="MeshInstance.SurfaceOffset"/>), w the bone palette's base.</summary>
    public Vector4 Skin;
    /// <summary><see cref="MeshInstance.Bite"/> (Shaders/bite.glsl); its floor rides in <see cref="Scar"/>.z. 128 bytes in all: the push constant size every device has.</summary>
    public Vector4 Bite;
}

[StructLayout(LayoutKind.Sequential)]
struct PostConstants
{
    public Vector4 A;
    public Vector4 B;
    public Vector4 C;
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
/// <summary>
/// What the last frame drew: the scene pass (with the sky), and each shadow pass (zero while its light is off). With
/// <see cref="Views"/> 2 the scene pass's draws are recorded once and drawn into both eyes (multiview), so the GPU sets up
/// <see cref="Triangles"/> twice.
/// </summary>
public readonly record struct FrameStats(int Triangles, int Draws, int Lights, int LampTriangles, int LampDraws, int MoonTriangles, int MoonDraws, int Views = 1,
    int HandTriangles = 0, int HandDraws = 0);

public sealed unsafe class GreyboxRenderer : IDisposable
{
    readonly VkFormat _colorFormat;
    const VkFormat DepthFormat = VkFormat.D32Sfloat;
    const VkFormat SceneFormat = VkFormat.R16G16B16A16Sfloat;

    readonly GpuContext _gpu;
    VkDeviceApi Api => _gpu.Api;

    readonly VkSampler _crunchy;
    readonly Target _color, _scene, _depth, _bloomA, _bloomB, _shadow;
    // The wide bloom's quarter-resolution pair, and the tonemapped frame FXAA reads (luma in alpha).
    readonly Target _bloomC, _bloomD, _ldr;
    // The screen-space occlusion, at half resolution (ssao.frag), from the scene's depth.
    readonly Target _ao;
    const VkFormat LdrFormat = VkFormat.R8G8B8A8Unorm;
    const int ShadowSize = 1024;
    // The moon's shadow: orthographic, over MoonShadowReach metres either way of a point ahead of the camera, at
    // MoonShadowSize texels (2048 by default; a headset's eyes, each drawing its own, take less).
    readonly int MoonShadowSize;
    const float MoonShadowReach = 55;
    readonly Target _moonShadow;
    readonly VkPipeline _moonShadowPipeline;
    bool _moonOn;
    readonly VkSampler _shadowSampler;
    readonly VkPipeline _shadowPipeline;
    bool _lampOn;
    // The hand lamp's cube shadow (MeshBuilder.ShadowLight, GDD §31): six layers, one a face, drawn in one multiview pass
    // where the device can draw six views at once (HandShadows); cleared, and the lamp unshadowed, where it can't.
    const int HandShadowSize = 512;
    // A little over 90 degrees a face, so the filter's taps at a face's edge stay on it.
    const float HandFaceDegrees = 96;
    // Its near plane: the lantern's own cage and cap, round the flame, cast nothing.
    const float HandNear = 0.1f;
    readonly Target _handShadow;
    readonly VkPipeline _handShadowPipeline, _handShadowSkinPipeline;
    PointLight? _hand;
    bool _handOn;
    readonly Matrix4x4[] _handFaces = new Matrix4x4[6];
    readonly VkBuffer _readback;
    readonly VkDeviceMemory _readbackMemory;
    readonly VkBuffer _frame;
    readonly VkDeviceMemory _frameMemory;
    readonly FrameData* _frameMapped;

    readonly VkDescriptorPool _pool;
    readonly VkDescriptorSetLayout _sceneSetLayout, _postSetLayout, _compositeSetLayout;
    readonly VkDescriptorSet _sceneSet, _brightSet, _blurHSet, _blurVSet, _compositeSet, _blurH2Set, _blurV2Set, _fxaaSet, _aoSet;
    readonly VkPipelineLayout _sceneLayout, _postLayout, _compositeLayout, _overlayLayout;
    readonly VkPipeline _fxAlphaPipeline, _fxAddPipeline;
    VkBuffer _fxVertices;
    VkDeviceMemory _fxMemory;
    ulong _fxCapacity;
    int _fxAlphaCount, _fxAddCount;
    readonly List<FxVertex> _fx = new();
    readonly VkPipeline _skyPipeline, _scenePipeline, _brightPipeline, _blurPipeline, _compositePipeline, _overlayPipeline, _fxaaPipeline, _aoPipeline;
    Matrix4x4 _projection;
    readonly VkSampler _nearest, _linear;

    GpuTexture _diffuse, _spec, _backdrop, _lut, _normal;
    // The hero layers at full size, and which slot each layer has in them (-1: none).
    GpuTexture _heroDiffuse, _heroSpec, _heroNormal;
    // ...and the big ones (authored over HeroSize: the largest creatures', RenderAssets.BigHeroSize), in arrays of their
    // own so the rest don't grow with them. A layer's slot there is BigHero + its index.
    GpuTexture _bigDiffuse, _bigSpec, _bigNormal;
    int[] _heroSlot = [];

    /// <summary>A hero slot at or over this is in the big arrays (scene.frag's heroSlot).</summary>
    const int BigHero = 64;
    // Which layers bend in the wind: the foliage's cards and boughs (by name, *_card and *_bough).
    float[] _motion = [];
    RenderAssets? _assets;

    VkBuffer _vertices;
    VkDeviceMemory _vertexMemory;
    ulong _vertexCapacity;
    int _vertexCount;

    // Cooked meshes on the GPU, for as long as their asset lives (MeshInstance.Asset).
    readonly ConditionalWeakTable<MeshAsset, GpuMesh> _meshes = new();
    readonly List<(WeakReference<MeshAsset> Asset, GpuMesh Mesh)> _allMeshes = new();
    int _prepares;
    // The kit's instances this frame, each with its bounding sphere placed (camera-relative) for culling.
    readonly List<(GpuMesh Mesh, DrawConstants Draw, Vector4 Sphere, bool Shadowless)> _draws = new();
    readonly List<PointLight> _lights = new();
    readonly List<Room> _rooms = new();

    // The 2D pass over the frame: HUD, prompts, menus (Overlay).
    VkBuffer _overlayVertices;
    VkDeviceMemory _overlayMemory;
    ulong _overlayCapacity;
    int _overlayCount;
    // Drawing both eyes at once: where the right eye's overlay starts in the buffer (overlay.vert).
    int _overlaySplit;
    readonly List<OverlayVertex> _overlayBoth = new();

    sealed class GpuMesh(VkBuffer buffer, VkDeviceMemory memory, int count)
    {
        public VkBuffer Buffer = buffer;
        public VkDeviceMemory Memory = memory;
        public int Count = count;
        // A skinned asset's bones and weights, a second vertex stream (skin.glsl).
        public VkBuffer Skin;
        public VkDeviceMemory SkinMemory;
    }

    // GPU skinning (MeshBuilder.Skinned): the skinned pieces drawn after the rest, by pipelines that pose them from the
    // frame's bone palettes (scene set binding 10).
    readonly VkPipeline _sceneSkinPipeline, _shadowSkinPipeline, _moonShadowSkinPipeline;
    readonly List<(GpuMesh Mesh, DrawConstants Draw, Vector4 Sphere)> _skinDraws = new();
    VkBuffer _bones;
    VkDeviceMemory _bonesMemory;
    Matrix4x4* _bonesMapped;
    int _bonesCapacity;

    // Layers: 1, or a multiview renderer's per-eye targets, one layer an eye (an array view over them all).
    readonly record struct Target(VkImage Image, VkDeviceMemory Memory, VkImageView View, int Width, int Height, int Layers = 1);

    // GPU timing: a timestamp at each pass boundary of the last recorded frame (none where the queue can't time).
    static readonly string[] PassNames = ["lampShadow", "moonShadow", "handShadow", "scene", "occlusion", "bloom", "composite", "final"];
    readonly VkQueryPool _timestamps;
    bool _timed;
    // A slot a mark, and a spare one for each view past the first: lavapipe (Mesa 25) writes a timestamp taken after a
    // multiview pass into a query per view, as if still inside it, and the last mark's second write ran off the pool's
    // end (a crash). Each mark's spare is overwritten by the next mark, so every read is still the right one. (Note 213.)
    uint TimestampSlots => (uint)(PassNames.Length + Views);

    /// <param name="colorFormat">The frame's format: UNORM, holding display-ready (gamma-encoded) values. A headset renderer
    /// matches the channel order of its sRGB swapchain so the frame copies across bit for bit.</param>
    /// <param name="moonShadowSize">The moon's shadow map's size, texels square.</param>
    /// <param name="views">2: a headset's both eyes in one pass (multiview, ARCHITECTURE §8 note 221): every per-eye target
    /// is a two-layer array, every scene and post pass draws both layers at once, and the shaders pick each eye's view by
    /// gl_ViewIndex. The shadow maps are the body's, not an eye's, so they're drawn once either way. Needs
    /// <see cref="GpuContext.Multiview"/>.</param>
    public GreyboxRenderer(GpuContext gpu, int width, int height, VkFormat colorFormat = VkFormat.R8G8B8A8Unorm, int moonShadowSize = 2048, int views = 1)
    {
        if (views is < 1 or > 2)
            throw new ArgumentOutOfRangeException(nameof(views), views, "one view, or a headset's two");
        if (views > 1 && !gpu.Multiview)
            throw new NotSupportedException($"{gpu.DeviceName} has no multiview: draw each eye with its own renderer");
        Views = views;
        MoonShadowSize = moonShadowSize;
        _colorFormat = colorFormat;
        _gpu = gpu;
        Width = width;
        Height = height;
        int hw = Math.Max(1, width / 2), hh = Math.Max(1, height / 2);

        _color = CreateTarget(_colorFormat, width, height, VkImageUsageFlags.ColorAttachment | VkImageUsageFlags.TransferSrc, VkImageAspectFlags.Color, views);
        _scene = CreateTarget(SceneFormat, width, height, VkImageUsageFlags.ColorAttachment | VkImageUsageFlags.Sampled, VkImageAspectFlags.Color, views);
        _depth = CreateTarget(DepthFormat, width, height, VkImageUsageFlags.DepthStencilAttachment | VkImageUsageFlags.Sampled, VkImageAspectFlags.Depth, views);
        _ao = CreateTarget(LdrFormat, hw, hh, VkImageUsageFlags.ColorAttachment | VkImageUsageFlags.Sampled, VkImageAspectFlags.Color, views);
        _bloomA = CreateTarget(SceneFormat, hw, hh, VkImageUsageFlags.ColorAttachment | VkImageUsageFlags.Sampled, VkImageAspectFlags.Color, views);
        _bloomB = CreateTarget(SceneFormat, hw, hh, VkImageUsageFlags.ColorAttachment | VkImageUsageFlags.Sampled, VkImageAspectFlags.Color, views);
        int qw = Math.Max(1, width / 4), qh = Math.Max(1, height / 4);
        _bloomC = CreateTarget(SceneFormat, qw, qh, VkImageUsageFlags.ColorAttachment | VkImageUsageFlags.Sampled, VkImageAspectFlags.Color, views);
        _bloomD = CreateTarget(SceneFormat, qw, qh, VkImageUsageFlags.ColorAttachment | VkImageUsageFlags.Sampled, VkImageAspectFlags.Color, views);
        _ldr = CreateTarget(LdrFormat, width, height, VkImageUsageFlags.ColorAttachment | VkImageUsageFlags.Sampled, VkImageAspectFlags.Color, views);
        _shadow = CreateTarget(DepthFormat, ShadowSize, ShadowSize, VkImageUsageFlags.DepthStencilAttachment | VkImageUsageFlags.Sampled, VkImageAspectFlags.Depth);
        _moonShadow = CreateTarget(DepthFormat, MoonShadowSize, MoonShadowSize, VkImageUsageFlags.DepthStencilAttachment | VkImageUsageFlags.Sampled, VkImageAspectFlags.Depth);
        HandShadows = gpu.MultiviewViews >= 6;
        _handShadow = CreateTarget(DepthFormat, HandShadowSize, HandShadowSize, VkImageUsageFlags.DepthStencilAttachment | VkImageUsageFlags.Sampled, VkImageAspectFlags.Depth, 6);
        {
            var info = new VkSamplerCreateInfo
            {
                magFilter = VkFilter.Linear,
                minFilter = VkFilter.Linear,
                addressModeU = VkSamplerAddressMode.ClampToEdge,
                addressModeV = VkSamplerAddressMode.ClampToEdge,
                addressModeW = VkSamplerAddressMode.ClampToEdge,
                compareEnable = true,
                compareOp = VkCompareOp.LessOrEqual,
                maxLod = 1,
            };
            VkSampler sampler;
            Check(Api.vkCreateSampler(&info, null, &sampler), "vkCreateSampler");
            _shadowSampler = sampler;
        }
        (_readback, _readbackMemory) = CreateBuffer((ulong)(width * height * 4 * views), VkBufferUsageFlags.TransferDst,
            VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
        (_frame, _frameMemory) = CreateBuffer((ulong)sizeof(FrameData), VkBufferUsageFlags.UniformBuffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
        void* mapped;
        Check(Api.vkMapMemory(_frameMemory, 0, (ulong)sizeof(FrameData), 0, &mapped), "vkMapMemory");
        _frameMapped = (FrameData*)mapped;
        if (gpu.TimestampPeriod > 0)
        {
            var info = new VkQueryPoolCreateInfo { queryType = VkQueryType.Timestamp, queryCount = TimestampSlots };
            VkQueryPool queries;
            Check(Api.vkCreateQueryPool(&info, null, &queries), "vkCreateQueryPool");
            _timestamps = queries;
        }

        _nearest = GpuTexture.CreateSampler(gpu, VkFilter.Nearest, VkFilter.Nearest, VkSamplerMipmapMode.Nearest, VkSamplerAddressMode.ClampToEdge, VkSamplerAddressMode.ClampToEdge);
        _linear = GpuTexture.CreateSampler(gpu, VkFilter.Linear, VkFilter.Linear, VkSamplerMipmapMode.Nearest, VkSamplerAddressMode.ClampToEdge, VkSamplerAddressMode.ClampToEdge);
        // The PS2 comparison mode's material sampler: point-sampled, mips biased up so distance shimmers.
        _crunchy = GpuTexture.CreateSampler(gpu, VkFilter.Nearest, VkFilter.Nearest, VkSamplerMipmapMode.Linear, VkSamplerAddressMode.Repeat,
            VkSamplerAddressMode.Repeat, 0.4f, 16);

        // Descriptor sets: the scene's (frame constants, the material maps, the backdrop), and the post passes'.
        _sceneSetLayout = SetLayout([(VkDescriptorType.UniformBuffer, VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment),
            (VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.Fragment), (VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.Fragment),
            (VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.Fragment), (VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.Fragment),
            (VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.Fragment), (VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.Fragment),
            (VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.Fragment), (VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.Fragment),
            (VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.Fragment), (VkDescriptorType.StorageBuffer, VkShaderStageFlags.Vertex),
            (VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.Fragment), (VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.Fragment),
            (VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.Fragment), (VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.Fragment)]);
        _postSetLayout = SetLayout([(VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.Fragment)]);
        _compositeSetLayout = SetLayout([(VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.Fragment),
            (VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.Fragment), (VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.Fragment),
            (VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.Fragment), (VkDescriptorType.CombinedImageSampler, VkShaderStageFlags.Fragment)]);
        var sizes = stackalloc VkDescriptorPoolSize[3];
        sizes[0] = new VkDescriptorPoolSize { type = VkDescriptorType.UniformBuffer, descriptorCount = 2 };
        sizes[1] = new VkDescriptorPoolSize { type = VkDescriptorType.CombinedImageSampler, descriptorCount = 32 };
        sizes[2] = new VkDescriptorPoolSize { type = VkDescriptorType.StorageBuffer, descriptorCount = 2 };
        var poolInfo = new VkDescriptorPoolCreateInfo { maxSets = 12, poolSizeCount = 3, pPoolSizes = sizes };
        VkDescriptorPool pool;
        Check(Api.vkCreateDescriptorPool(&poolInfo, null, &pool), "vkCreateDescriptorPool");
        _pool = pool;
        _sceneSet = Allocate(_sceneSetLayout);
        _brightSet = Allocate(_postSetLayout);
        _blurHSet = Allocate(_postSetLayout);
        _blurVSet = Allocate(_postSetLayout);
        _compositeSet = Allocate(_compositeSetLayout);
        _blurH2Set = Allocate(_postSetLayout);
        _blurV2Set = Allocate(_postSetLayout);
        _fxaaSet = Allocate(_postSetLayout);
        _aoSet = Allocate(_postSetLayout);

        _sceneLayout = PipelineLayout(_sceneSetLayout, (uint)sizeof(DrawConstants), VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment);
        _postLayout = PipelineLayout(_postSetLayout, (uint)sizeof(PostConstants), VkShaderStageFlags.Fragment);
        _compositeLayout = PipelineLayout(_compositeSetLayout, (uint)sizeof(PostConstants), VkShaderStageFlags.Fragment);
        _overlayLayout = PipelineLayout(null, (uint)sizeof(Vector4), VkShaderStageFlags.Vertex);

        _skyPipeline = Pipeline(_sceneLayout, "fullscreen.vert", "sky.frag", SceneFormat, PipelineKind.Fullscreen, depth: true);
        _scenePipeline = Pipeline(_sceneLayout, "scene.vert", "scene.frag", SceneFormat, PipelineKind.Scene, depth: true);
        _shadowPipeline = Pipeline(_sceneLayout, "shadow.vert", "shadow.frag", VkFormat.Undefined, PipelineKind.Shadow, depth: true);
        _moonShadowPipeline = Pipeline(_sceneLayout, "shadow_moon.vert", "shadow.frag", VkFormat.Undefined, PipelineKind.Shadow, depth: true);
        _sceneSkinPipeline = Pipeline(_sceneLayout, "scene.vert", "scene.frag", SceneFormat, PipelineKind.Scene, depth: true, skinned: true);
        _shadowSkinPipeline = Pipeline(_sceneLayout, "shadow.vert", "shadow.frag", VkFormat.Undefined, PipelineKind.Shadow, depth: true, skinned: true);
        _moonShadowSkinPipeline = Pipeline(_sceneLayout, "shadow_moon.vert", "shadow.frag", VkFormat.Undefined, PipelineKind.Shadow, depth: true, skinned: true);
        if (HandShadows)
        {
            _handShadowPipeline = Pipeline(_sceneLayout, "shadow_hand.vert", "shadow.frag", VkFormat.Undefined, PipelineKind.Shadow, depth: true, cube: true);
            _handShadowSkinPipeline = Pipeline(_sceneLayout, "shadow_hand.vert", "shadow.frag", VkFormat.Undefined, PipelineKind.Shadow, depth: true, skinned: true, cube: true);
        }
        _fxAlphaPipeline = Pipeline(_sceneLayout, "fx.vert", "fx.frag", SceneFormat, PipelineKind.FxAlpha, depth: true);
        _fxAddPipeline = Pipeline(_sceneLayout, "fx.vert", "fx.frag", SceneFormat, PipelineKind.FxAdditive, depth: true);
        _brightPipeline = Pipeline(_postLayout, "fullscreen.vert", "bright.frag", SceneFormat, PipelineKind.Fullscreen, depth: false);
        _blurPipeline = Pipeline(_postLayout, "fullscreen.vert", "blur.frag", SceneFormat, PipelineKind.Fullscreen, depth: false);
        _compositePipeline = Pipeline(_compositeLayout, "fullscreen.vert", "composite.frag", LdrFormat, PipelineKind.Fullscreen, depth: false);
        _fxaaPipeline = Pipeline(_postLayout, "fullscreen.vert", "fxaa.frag", _colorFormat, PipelineKind.Fullscreen, depth: false);
        _aoPipeline = Pipeline(_postLayout, "fullscreen.vert", "ssao.frag", LdrFormat, PipelineKind.Fullscreen, depth: false);
        _overlayPipeline = Pipeline(_overlayLayout, "overlay.vert", "overlay.frag", _colorFormat, PipelineKind.Overlay, depth: false);

        // Until there are assets: one plain white layer (the greybox's flat colour), no backdrop, no grade.
        (_diffuse, _spec, _backdrop, _lut) = Upload(new RenderAssets());
        _normal = UploadNormals(new RenderAssets());
        (_heroDiffuse, _heroSpec, _heroNormal, _bigDiffuse, _bigSpec, _bigNormal) = UploadHeroes(new RenderAssets());
        GrowBones(1024);
        WriteSets();
    }

    /// <summary>Room for <paramref name="count"/> bone matrices a frame (the buffer only grows), bound at binding 10.</summary>
    void GrowBones(int count)
    {
        if (count <= _bonesCapacity)
            return;
        if (_bonesCapacity > 0)
        {
            Api.vkUnmapMemory(_bonesMemory);
            Api.vkDestroyBuffer(_bones, null);
            Api.vkFreeMemory(_bonesMemory, null);
        }
        _bonesCapacity = Math.Max(count, _bonesCapacity * 2);
        ulong size = (ulong)(_bonesCapacity * sizeof(Matrix4x4));
        (_bones, _bonesMemory) = CreateBuffer(size, VkBufferUsageFlags.StorageBuffer, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
        void* mapped;
        Check(Api.vkMapMemory(_bonesMemory, 0, size, 0, &mapped), "vkMapMemory");
        _bonesMapped = (Matrix4x4*)mapped;
        var info = new VkDescriptorBufferInfo { buffer = _bones, offset = 0, range = size };
        var write = new VkWriteDescriptorSet { dstSet = _sceneSet, dstBinding = 10, descriptorCount = 1, descriptorType = VkDescriptorType.StorageBuffer, pBufferInfo = &info };
        Api.vkUpdateDescriptorSets(1, &write, 0, null);
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>How many views a frame draws: 1, or 2 for both of a headset's eyes in one pass (multiview).</summary>
    public int Views { get; }

    /// <summary>
    /// Whether <see cref="MeshBuilder.ShadowLight"/> casts shadows here: its cube's six faces are drawn in one multiview
    /// pass, which needs a device that draws six views at once (<see cref="GpuContext.MultiviewViews"/>).
    /// </summary>
    public bool HandShadows { get; }

    /// <summary>
    /// The rendered frame. After <see cref="Record"/> it is in TransferSrcOptimal layout. With <see cref="Views"/> 2 it's
    /// an array image, the left eye in layer 0 and the right in layer 1.
    /// </summary>
    public VkImage ColorImage => _color.Image;

    /// <summary>The post stack's settings: from the loaded assets, or the defaults. Set outside command recording.</summary>
    public PostSettings Post
    {
        get => _post;
        set
        {
            bool rebind = value.Ps2 != _post.Ps2;
            _post = value;
            if (rebind && _diffuse is not null)
                WriteSets();
        }
    }
    PostSettings _post = new();

    /// <summary>Counts from the last frame recorded, for budgets (pipeline "frame-level ceilings").</summary>
    public FrameStats Stats { get; private set; }

    /// <summary>
    /// Another renderer whose shadow maps this one samples instead of drawing its own (a headset's second eye: the lamp's
    /// and the moon's views are the body's, not an eye's, so both eyes' maps would be the same). That one must record its
    /// frame first. Set outside command recording.
    /// </summary>
    public GreyboxRenderer? ShadowsFrom
    {
        get => _shadowsFrom;
        set
        {
            if (value == this)
                throw new ArgumentException("a renderer can't take its shadows from itself");
            _shadowsFrom = value;
            WriteSets();
        }
    }
    GreyboxRenderer? _shadowsFrom;

    /// <summary>
    /// How long each pass of the last frame took on the GPU, in milliseconds, once that frame has finished (every submit
    /// here waits for it). Empty where the device can't time its queue.
    /// </summary>
    public IReadOnlyList<(string Pass, double Ms)> PassTimes()
    {
        if (!_timed)
            return [];
        var ticks = new ulong[PassNames.Length + 1];
        fixed (ulong* t = ticks)
            if (Api.vkGetQueryPoolResults(_timestamps, 0, (uint)ticks.Length, (nuint)(ticks.Length * sizeof(ulong)), t, sizeof(ulong),
                    VkQueryResultFlags.Bit64 | VkQueryResultFlags.Wait) != VkResult.Success)
                return [];
        return [.. PassNames.Select((name, i) => (name, (ticks[i + 1] - ticks[i]) * _gpu.TimestampPeriod * 1e-6))];
    }

    void Mark(VkCommandBuffer cmd, int index)
    {
        if (_timestamps.IsNotNull)
            Api.vkCmdWriteTimestamp2(cmd, VkPipelineStageFlags2.AllCommands, _timestamps, (uint)index);
    }

    /// <summary>Uploads the materials, the backdrop and the grade, replacing what was there. Call outside command recording.</summary>
    public void Load(RenderAssets assets)
    {
        Api.vkDeviceWaitIdle();
        _diffuse.Dispose();
        _spec.Dispose();
        _backdrop.Dispose();
        _lut.Dispose();
        _normal.Dispose();
        _heroDiffuse.Dispose();
        _heroSpec.Dispose();
        _heroNormal.Dispose();
        _bigDiffuse.Dispose();
        _bigSpec.Dispose();
        _bigNormal.Dispose();
        (_diffuse, _spec, _backdrop, _lut) = Upload(assets);
        _normal = UploadNormals(assets);
        (_heroDiffuse, _heroSpec, _heroNormal, _bigDiffuse, _bigSpec, _bigNormal) = UploadHeroes(assets);
        _assets = assets;
        Post = assets.Post;
        WriteSets();
    }

    /// <summary>The loaded material layers kept at full size in the hero arrays (<see cref="RenderAssets.HeroSize"/>).</summary>
    public IEnumerable<string> HeroLayers => _assets is null ? [] : _heroSlot.Select((s, i) => (s, i)).Where(x => x.s >= 0).Select(x => _assets.Layers[x.i].Name);

    /// <summary>The loaded material layer called <paramref name="name"/>, or −1.</summary>
    public int LayerOf(string name) => _assets?.IndexOf(name) ?? -1;

    (GpuTexture, GpuTexture, GpuTexture, GpuTexture) Upload(RenderAssets assets)
    {
        int size = assets.LayerSize;
        var layers = assets.Layers.Count > 0 ? assets.Layers : [new MaterialLayer("white", Image.Solid(size, 255, 255, 255), Image.Solid(size, 0, 0, 0))];
        var diffuse = layers.Select(l => (IReadOnlyList<byte[]>)GpuTexture.MipChain(l.Diffuse.Resized(size, size))).ToList();
        var spec = layers.Select(l => (IReadOnlyList<byte[]>)GpuTexture.MipChain(l.Spec.Resized(size, size))).ToList();
        float bias = assets.Post.MipBias;
        // Trilinear and anisotropic: the 2008-2012 look the benchmarks set (BioShock 2, Dead Space). The PS2 comparison
        // mode swaps in the point-sampled, positively biased sampler instead (_crunchy).
        var d = new GpuTexture(_gpu, GpuTexture.Kind.Array2D, VkFormat.R8G8B8A8Srgb, size, size, diffuse, VkFilter.Linear,
            VkSamplerAddressMode.Repeat, VkSamplerAddressMode.Repeat, bias, anisotropy: _gpu.MaxAnisotropy);
        var s = new GpuTexture(_gpu, GpuTexture.Kind.Array2D, VkFormat.R8G8B8A8Unorm, size, size, spec, VkFilter.Linear,
            VkSamplerAddressMode.Repeat, VkSamplerAddressMode.Repeat, bias, anisotropy: _gpu.MaxAnisotropy);
        var sky = assets.Backdrop ?? Image.Solid(4, 0, 0, 0, 0);
        var b = new GpuTexture(_gpu, GpuTexture.Kind.Image2D, VkFormat.R8G8B8A8Srgb, sky.Width, sky.Height, [GpuTexture.MipChain(sky)], VkFilter.Linear,
            VkSamplerAddressMode.Repeat, VkSamplerAddressMode.ClampToEdge, bias);
        var lut = new GpuTexture(_gpu, GpuTexture.Kind.Volume, VkFormat.R8G8B8A8Unorm, ColourGrade.Size, ColourGrade.Size, [[assets.Lut ?? ColourGrade.Identity()]],
            VkFilter.Linear, VkSamplerAddressMode.ClampToEdge, VkSamplerAddressMode.ClampToEdge, depth: ColourGrade.Size);
        return (d, s, b, lut);
    }

    /// <summary>
    /// How a layer moves, by its name (the frame's per-layer table; Shaders/frame.glsl <c>motionOf</c>): 1 for the
    /// foliage's cards and boughs, which bend in the wind from their root (scene.vert); 2 for water, whose ripples and
    /// swell run with the wind and its current (scene.frag; ARCHITECTURE §8 note 424); 0 for everything that stands still.
    /// </summary>
    public static float Motion(string layer) =>
        layer.EndsWith("_card", StringComparison.Ordinal) || layer.EndsWith("_bough", StringComparison.Ordinal) ? 1
        : layer.StartsWith("water_", StringComparison.Ordinal) ? 2
        : 0;

    /// <summary>
    /// The hero layers (authored larger than the arrays' size: the baked atlases of what's seen closest) again at up to
    /// <see cref="RenderAssets.HeroSize"/>, in arrays of their own; <see cref="_heroSlot"/> says which layer is where. With
    /// none, one flat layer each, so the bindings are always there.
    /// </summary>
    (GpuTexture, GpuTexture, GpuTexture, GpuTexture, GpuTexture, GpuTexture) UploadHeroes(RenderAssets assets)
    {
        int size = Math.Max(assets.LayerSize, assets.HeroSize);
        int bigSize = Math.Max(size, assets.BigHeroSize);
        var heroes = new List<MaterialLayer>();
        var bigs = new List<MaterialLayer>();
        _heroSlot = new int[assets.Layers.Count];
        _motion = [.. assets.Layers.Select(l => Motion(l.Name))];
        for (int i = 0; i < assets.Layers.Count; i++)
        {
            var l = assets.Layers[i];
            _heroSlot[i] = -1;
            if (l.Diffuse.Width > size && bigSize > size && i < 256 && bigs.Count < BigHero)
            {
                _heroSlot[i] = BigHero + bigs.Count;
                bigs.Add(l);
            }
            else if (l.Diffuse.Width > assets.LayerSize && i < 256 && heroes.Count < BigHero)
            {
                _heroSlot[i] = heroes.Count;
                heroes.Add(l);
            }
        }
        (GpuTexture, GpuTexture, GpuTexture) Arrays(List<MaterialLayer> layers, int at)
        {
            if (layers.Count == 0)
                at = 4;
            var flat = Image.Solid(at, 128, 128, 255);
            var list = layers.Count > 0 ? layers : [new MaterialLayer("none", Image.Solid(at, 255, 255, 255), Image.Solid(at, 0, 0, 0))];
            GpuTexture Array(Func<MaterialLayer, Image> map, VkFormat format) => new(_gpu, GpuTexture.Kind.Array2D, format, at, at,
                list.Select(l => (IReadOnlyList<byte[]>)GpuTexture.MipChain(map(l).Resized(at, at))).ToList(), VkFilter.Linear,
                VkSamplerAddressMode.Repeat, VkSamplerAddressMode.Repeat, assets.Post.MipBias, anisotropy: _gpu.MaxAnisotropy);
            return (Array(l => l.Diffuse, VkFormat.R8G8B8A8Srgb), Array(l => l.Spec, VkFormat.R8G8B8A8Unorm), Array(l => l.Normal ?? flat, VkFormat.R8G8B8A8Unorm));
        }
        var (d, s, n) = Arrays(heroes, size);
        var (bd, bs, bn) = Arrays(bigs, bigSize);
        return (d, s, n, bd, bs, bn);
    }

    /// <summary>Every layer's normal map (flat where a layer has none), filtered like the diffuse, linear (not sRGB).</summary>
    GpuTexture UploadNormals(RenderAssets assets)
    {
        int size = assets.LayerSize;
        var flat = Image.Solid(size, 128, 128, 255);
        var layers = assets.Layers.Count > 0 ? assets.Layers : [new MaterialLayer("white", Image.Solid(size, 255, 255, 255), Image.Solid(size, 0, 0, 0))];
        var normals = layers.Select(l => (IReadOnlyList<byte[]>)GpuTexture.MipChain((l.Normal ?? flat).Resized(size, size))).ToList();
        return new GpuTexture(_gpu, GpuTexture.Kind.Array2D, VkFormat.R8G8B8A8Unorm, size, size, normals, VkFilter.Linear,
            VkSamplerAddressMode.Repeat, VkSamplerAddressMode.Repeat, assets.Post.MipBias, anisotropy: _gpu.MaxAnisotropy);
    }

    /// <summary>Draws the mesh (camera-relative positions) and returns the frame as RGBA8, top row first.</summary>
    /// <param name="clearColor">Linear colour at the horizon (the fog's, usually).</param>
    /// <summary>
    /// The overlay's canvas, when it isn't the frame's own pixels: the HUD and menus lay out on a small fixed canvas (their
    /// pixel font's), scaled to however large the frame renders.
    /// </summary>
    public Vector2? OverlaySize { get; set; }

    /// <param name="overlay">2D drawing over the frame (the HUD), or null.</param>
    public byte[] Render(MeshBuilder mesh, in Camera camera, in FrameLighting lighting, Vector3 clearColor, Overlay? overlay = null)
    {
        if (Views != 1)
            throw new InvalidOperationException("a multiview renderer draws both eyes: RenderEyes");
        return RenderViews(mesh, [camera], lighting, clearColor, [overlay])[0];
    }

    /// <summary>
    /// Both eyes in one pass (<see cref="Views"/> 2) and each read back as RGBA8, top row first: the left eye's, then the
    /// right's. Each eye's overlay is its own (the HUD's panel projected for it, its vignette).
    /// </summary>
    public byte[][] RenderEyes(MeshBuilder mesh, Camera left, Camera right, in FrameLighting lighting, Vector3 clearColor, Overlay? leftOverlay = null, Overlay? rightOverlay = null)
    {
        if (Views != 2)
            throw new InvalidOperationException("a single-view renderer draws one eye: Render");
        return RenderViews(mesh, [left, right], lighting, clearColor, [leftOverlay, rightOverlay]);
    }

    byte[][] RenderViews(MeshBuilder mesh, Camera[] cameras, in FrameLighting lighting, Vector3 clearColor, Overlay?[] overlays)
    {
        if (overlays.Length > 1)
            Prepare(mesh, overlays[0], overlays[1]);
        else
            Prepare(mesh, overlays[0]);
        var light = lighting;
        _gpu.Submit(cmd =>
        {
            Record(cmd, cameras, light, clearColor);
            var region = new VkBufferImageCopy
            {
                imageSubresource = new VkImageSubresourceLayers(VkImageAspectFlags.Color, 0, 0, (uint)Views),
                imageExtent = new VkExtent3D(Width, Height, 1),
            };
            Api.vkCmdCopyImageToBuffer(cmd, _color.Image, VkImageLayout.TransferSrcOptimal, _readback, 1, &region);
        });

        int size = Width * Height * 4;
        var pixels = new byte[Views][];
        void* mapped;
        Check(Api.vkMapMemory(_readbackMemory, 0, (ulong)(size * Views), 0, &mapped), "vkMapMemory");
        for (int v = 0; v < Views; v++)
            pixels[v] = new ReadOnlySpan<byte>((byte*)mapped + v * size, size).ToArray();
        Api.vkUnmapMemory(_readbackMemory);
        return pixels;
    }

    /// <summary>
    /// <see cref="Prepare(MeshBuilder, Overlay?)"/> for both eyes at once (<see cref="Views"/> 2), each with its own overlay.
    /// </summary>
    public void Prepare(MeshBuilder mesh, Overlay? left, Overlay? right)
    {
        if (Views != 2)
            throw new InvalidOperationException("one overlay per view: this renderer draws one");
        Prepare(mesh, (Overlay?)null);
        // One buffer, the left eye's first: overlay.vert keeps each eye to its own.
        _overlayBoth.Clear();
        if (left is not null)
            _overlayBoth.AddRange(left.Vertices);
        _overlaySplit = _overlayBoth.Count;
        if (right is not null)
            _overlayBoth.AddRange(right.Vertices);
        _overlayCount = _overlayBoth.Count;
        if (_overlayCount > 0)
            Upload(CollectionsMarshal.AsSpan(_overlayBoth), OverlayVertex.Stride, ref _overlayVertices, ref _overlayMemory, ref _overlayCapacity);
    }

    /// <summary>
    /// Uploads geometry, kit instances and lights (and the overlay, if any) for the next <see cref="Record"/>. Call outside
    /// command recording. A multiview renderer given one overlay shows it to both eyes.
    /// </summary>
    public void Prepare(MeshBuilder mesh, Overlay? overlay = null)
    {
        Upload(mesh.Vertices, (uint)Vertex.Stride, ref _vertices, ref _vertexMemory, ref _vertexCapacity);
        // Now and then, free the GPU copies of pieces nobody holds any more (the line's cells behind the train). Frames
        // are submitted and waited for, so nothing in flight still uses them.
        if (++_prepares % 120 == 0)
            for (int i = _allMeshes.Count - 1; i >= 0; i--)
                if (!_allMeshes[i].Asset.TryGetTarget(out _))
                {
                    Free(_allMeshes[i].Mesh);
                    _allMeshes.RemoveAt(i);
                }
        _vertexCount = mesh.Count;
        _draws.Clear();
        _skinDraws.Clear();
        // Every submit waits for the GPU, so this frame's palettes can go straight over the last frame's.
        GrowBones(mesh.Bones.Count);
        CollectionsMarshal.AsSpan(mesh.Bones).CopyTo(new Span<Matrix4x4>(_bonesMapped, _bonesCapacity));
        foreach (var instance in mesh.Instances)
        {
            if (instance.Asset.Vertices.Length == 0)
                continue;
            if (!_meshes.TryGetValue(instance.Asset, out var gpuMesh))
            {
                var (buffer, memory) = Resident<Vertex>(instance.Asset.Vertices);
                gpuMesh = new GpuMesh(buffer, memory, instance.Asset.Vertices.Length);
                if (instance.Asset.Skin is { } skin)
                    (gpuMesh.Skin, gpuMesh.SkinMemory) = Resident<SkinWeights>(skin);
                _meshes.Add(instance.Asset, gpuMesh);
                _allMeshes.Add((new WeakReference<MeshAsset>(instance.Asset), gpuMesh));
            }
            var tint = instance.Tint == default ? Vector3.One : instance.Tint;
            var draw = new DrawConstants
            {
                Model = instance.Model,
                Tint = new Vector4(tint, instance.Glow),
                Scar = new Vector4(instance.Scar, instance.BiteFloor, 0),
                Skin = new Vector4(instance.SurfaceOffset, instance.Bones),
                Bite = instance.Bite,
            };
            var (centre, radius) = instance.Asset.Bounds;
            var m = instance.Model;
            float scale = MathF.Sqrt(MathF.Max(new Vector3(m.M11, m.M12, m.M13).LengthSquared(),
                MathF.Max(new Vector3(m.M21, m.M22, m.M23).LengthSquared(), new Vector3(m.M31, m.M32, m.M33).LengthSquared())));
            var sphere = new Vector4(Vector3.Transform(centre, m), radius * scale);
            if (instance.Bones >= 0 && gpuMesh.Skin.IsNotNull)
                // (Its bind pose's sphere; posed, a limb can reach past it: the hand lamp's cube culls it loosely, SkinSlack.)
                _skinDraws.Add((gpuMesh, draw, sphere));
            else
                _draws.Add((gpuMesh, draw, sphere, instance.Shadowless));
        }
        CopyStaged();
        _lights.Clear();
        _lights.AddRange(mesh.PointLights);
        _hand = mesh.ShadowLight;
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
        _rooms.Clear();
        _rooms.AddRange(mesh.Rooms);
        if (_rooms.Count > FrameData.MaxRooms)
        {
            _rooms.Sort((a, b) => a.Centre.LengthSquared().CompareTo(b.Centre.LengthSquared()));
            _rooms.RemoveRange(FrameData.MaxRooms, _rooms.Count - FrameData.MaxRooms);
        }
        _overlayCount = overlay?.Count ?? 0;
        _overlaySplit = -1;
        if (overlay is { Count: > 0 })
            Upload(CollectionsMarshal.AsSpan(overlay.Vertices), OverlayVertex.Stride, ref _overlayVertices, ref _overlayMemory, ref _overlayCapacity);
    }

    // Kit meshes live in the GPU's own memory (a discrete card reads host-visible memory over the bus, every pass): each
    // is written to a staging buffer, and a frame's new ones are copied across in one submit (CopyStaged).
    readonly List<(VkBuffer Staging, VkDeviceMemory StagingMemory, VkBuffer Into, ulong Size)> _staged = new();

    (VkBuffer, VkDeviceMemory) Resident<T>(ReadOnlySpan<T> data) where T : unmanaged
    {
        ulong size = (ulong)Math.Max(1, data.Length) * (ulong)sizeof(T);
        var (staging, stagingMemory) = CreateBuffer(size, VkBufferUsageFlags.TransferSrc, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent);
        void* mapped;
        Check(Api.vkMapMemory(stagingMemory, 0, size, 0, &mapped), "vkMapMemory");
        MemoryMarshal.AsBytes(data).CopyTo(new Span<byte>(mapped, (int)size));
        Api.vkUnmapMemory(stagingMemory);
        var (buffer, memory) = CreateBuffer(size, VkBufferUsageFlags.VertexBuffer | VkBufferUsageFlags.TransferDst, VkMemoryPropertyFlags.DeviceLocal);
        _staged.Add((staging, stagingMemory, buffer, size));
        return (buffer, memory);
    }

    void CopyStaged()
    {
        if (_staged.Count == 0)
            return;
        _gpu.Submit(cmd =>
        {
            foreach (var (staging, _, into, size) in _staged)
            {
                var region = new VkBufferCopy { size = size };
                Api.vkCmdCopyBuffer(cmd, staging, into, 1, &region);
            }
            // The copies land before any later pass reads them as vertices.
            var barrier = new VkMemoryBarrier2
            {
                srcStageMask = VkPipelineStageFlags2.Transfer,
                srcAccessMask = VkAccessFlags2.TransferWrite,
                dstStageMask = VkPipelineStageFlags2.VertexAttributeInput,
                dstAccessMask = VkAccessFlags2.VertexAttributeRead,
            };
            var dependency = new VkDependencyInfo { memoryBarrierCount = 1, pMemoryBarriers = &barrier };
            Api.vkCmdPipelineBarrier2(cmd, &dependency);
        });
        foreach (var (staging, stagingMemory, _, _) in _staged)
        {
            Api.vkDestroyBuffer(staging, null);
            Api.vkFreeMemory(stagingMemory, null);
        }
        _staged.Clear();
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

    /// <param name="cameras">Each view's camera (one, or both eyes): they share a position (the scene's origin) and differ
    /// by their eye offsets and orientations. The first's places the shadows, as the left eye's does drawing each eye alone.</param>
    void WriteFrame(ReadOnlySpan<Camera> cameras, in FrameLighting lighting)
    {
        ref readonly var camera = ref cameras[0];
        var f = _frameMapped;
        for (int v = 0; v < cameras.Length; v++)
        {
            _viewProj[v] = cameras[v].ViewProjection((float)Width / Height);
            Matrix4x4.Invert(_viewProj[v], out var inverse);
            if (v == 0)
                (f->ViewProj, f->InvViewProj) = (_viewProj[v], inverse);
            else
                (f->ViewProj1, f->InvViewProj1) = (_viewProj[v], inverse);
        }
        float fogBase = double.IsNaN(lighting.FogBase) ? -1.7f : (float)(lighting.FogBase - camera.Position.Y);
        f->LampViewProj = LampViewProjection(camera, lighting);
        _moonOn = lighting.MoonStrength > 0.01f && lighting.MoonDirection.Y > 0.05f && !Post.Ps2;
        f->MoonViewProj = MoonViewProjection(camera, lighting, (float)Width / Height);
        _handOn = _hand is { Range: > 0.5f } && HandShadows;
        if (_hand is { } hand)
            HandFaces(hand, _handFaces);
        // Sampling another's shadow maps: its views, exactly as it drew them.
        if (_shadowsFrom is { } from)
        {
            f->LampViewProj = from._frameMapped->LampViewProj;
            f->MoonViewProj = from._frameMapped->MoonViewProj;
            _moonOn = from._moonOn;
            // (Its hand lamp is this one's too: the eyes draw the same scene.)
            from._handFaces.CopyTo(_handFaces, 0);
            _handOn = from._handOn;
        }
        for (int i = 0; i < 256; i++)
            f->HeroOf[i] = i < _heroSlot.Length ? _heroSlot[i] : -1;
        f->Fog = new Vector4(lighting.FogColor, lighting.FogDensity);
        f->FogHeight = new Vector4(fogBase, lighting.FogHeightFalloff, lighting.FogFloor, (float)(lighting.Time % 10000));
        f->Moon = new Vector4(lighting.MoonDirection, lighting.Ambient);
        f->MoonColour = new Vector4(lighting.MoonColour, lighting.MoonStrength);
        f->LampPos = new Vector4(lighting.LampPosition.RelativeTo(camera.Position), lighting.LampRange);
        f->LampDir = new Vector4(lighting.LampDirection, MathF.Cos(lighting.LampConeDegrees * MathF.PI / 180));
        f->LampColour = new Vector4(lighting.LampColour, lighting.LampIntensity);
        f->Sky = new Vector4(Post.SkyZenith, Post.BackdropFog);
        f->Sky2 = new Vector4(Post.BackdropDegrees * MathF.PI / 180, MathF.Max(0.2f, lighting.FogCurve), Post.HorizonGlow, lighting.Wetness);
        f->Params = new Vector4(_lights.Count, Post.Ps2 ? 1 : 0, Post.TexturedWear, _assets?.Layers.Count ?? 0);
        f->HandPos = _hand is { } h ? new Vector4(h.Position, h.Range) : default;
        f->HandColour = _hand is { } c ? new Vector4(c.Colour, _handOn ? 1 : 0) : default;
        for (int face = 0; face < 6; face++)
        {
            var m = _handFaces[face];
            new ReadOnlySpan<float>(&m, 16).CopyTo(new Span<float>(f->HandViewProj + face * 16, 16));
        }
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
        for (int i = 0; i < _rooms.Count; i++)
        {
            var r = _rooms[i];
            float* p = f->Rooms + i * 12;
            (p[0], p[1], p[2], p[3]) = (r.Centre.X, r.Centre.Y, r.Centre.Z, r.Half.X);
            (p[4], p[5], p[6], p[7]) = (r.Right.X, r.Right.Y, r.Right.Z, r.Half.Y);
            (p[8], p[9], p[10], p[11]) = (r.Back.X, r.Back.Y, r.Back.Z, r.Half.Z);
        }
        f->Counts = new Vector4(_rooms.Count, _moonOn ? 1 : 0, 1f / (_shadowsFrom ?? this).MoonShadowSize, lighting.Frost);
        f->Dawn = new Vector4(lighting.DawnGlow, lighting.Dawn);
        f->Wind = new Vector4(lighting.Wind, lighting.Gusts);
        for (int i = 0; i < 256; i++)
            f->MotionOf[i] = i < _motion.Length ? _motion[i] : 0;
    }

    // Each view's view-projection this frame (the culling's, and the frame constants').
    readonly Matrix4x4[] _viewProj = new Matrix4x4[2];

    /// <summary>
    /// The headlamp's view for its shadow map: from the lamp (camera-relative) along its beam, a little wider than its
    /// cone, out to its range. Vulkan's clip space, as the camera's.
    /// </summary>
    static Matrix4x4 LampViewProjection(in Camera camera, in FrameLighting lighting)
    {
        var at = lighting.LampPosition.RelativeTo(camera.Position);
        var dir = Vector3.Normalize(lighting.LampDirection);
        var up = MathF.Abs(dir.Y) > 0.95f ? Vector3.UnitZ : Vector3.UnitY;
        var view = Matrix4x4.CreateLookAt(at, at + dir, up);
        float fov = Math.Clamp(lighting.LampConeDegrees * 2.6f, 10, 120) * MathF.PI / 180;
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(fov, 1, 0.3f, MathF.Max(1, lighting.LampRange));
        proj.M22 *= -1;
        return view * proj;
    }

    /// <summary>
    /// The hand lamp's cube, a view a face (+X, −X, +Y, −Y, +Z, −Z; scene.frag's handShadowAt picks one by the axis a point
    /// lies furthest along from it): from the flame (camera-relative) out to its range, each a little wider than its
    /// 90 degrees. Vulkan's clip space, as the camera's.
    /// </summary>
    static void HandFaces(in PointLight hand, Span<Matrix4x4> faces)
    {
        ReadOnlySpan<Vector3> dirs = [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ];
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(HandFaceDegrees * MathF.PI / 180, 1, HandNear, MathF.Max(HandNear * 2, hand.Range));
        proj.M22 *= -1;
        for (int i = 0; i < 6; i++)
        {
            var up = MathF.Abs(dirs[i].Y) > 0.5f ? Vector3.UnitZ : Vector3.UnitY;
            faces[i] = Matrix4x4.CreateLookAt(hand.Position, hand.Position + dirs[i], up) * proj;
        }
    }

    /// <summary>
    /// The moon's view for its shadow map: orthographic along the moonlight, over the ground round a point a little ahead of
    /// the camera (where most of what's seen is), camera-relative. The box is snapped to its own texels in world space, so
    /// its edges don't crawl as the train moves.
    /// </summary>
    Matrix4x4 MoonViewProjection(in Camera camera, in FrameLighting lighting, float aspect)
    {
        var dir = Vector3.Normalize(lighting.MoonDirection);
        var fwd = camera.Forward with { Y = 0 };
        fwd = fwd.LengthSquared() > 1e-6f ? Vector3.Normalize(fwd) : -Vector3.UnitZ;
        var centre = fwd * (MoonShadowReach * 0.55f);
        var up = MathF.Abs(dir.Y) > 0.95f ? Vector3.UnitZ : Vector3.UnitY;
        var right = Vector3.Normalize(Vector3.Cross(up, dir));
        var top = Vector3.Cross(dir, right);
        // Snap: the world position of the centre, in the light's axes, to a whole number of texels.
        float texel = 2 * MoonShadowReach / MoonShadowSize;
        var world = new Vector3((float)(camera.Position.X % 4096.0), (float)(camera.Position.Y % 4096.0), (float)(camera.Position.Z % 4096.0)) + centre;
        float sx = Vector3.Dot(world, right), sy = Vector3.Dot(world, top);
        centre -= right * (sx - MathF.Floor(sx / texel) * texel) + top * (sy - MathF.Floor(sy / texel) * texel);
        const float Depth = 160;
        var view = Matrix4x4.CreateLookAt(centre + dir * (Depth * 0.5f), centre, top);
        var proj = Matrix4x4.CreateOrthographic(2 * MoonShadowReach, 2 * MoonShadowReach, 0.1f, Depth);
        proj.M22 *= -1;
        return view * proj;
    }

    /// <summary>Records the frame into <see cref="ColorImage"/>, leaving it ready to copy or blit.</summary>
    /// <param name="clearColor">The horizon's colour (the sky fades from it to the zenith).</param>
    public void Record(VkCommandBuffer cmd, in Camera camera, in FrameLighting lighting, Vector3 clearColor)
    {
        if (Views != 1)
            throw new InvalidOperationException("a multiview renderer records both eyes at once");
        Record(cmd, new ReadOnlySpan<Camera>(in camera), lighting, clearColor);
    }

    /// <summary>
    /// Records the frame for every view at once (<see cref="Views"/> cameras: a headset's left eye then its right) into
    /// <see cref="ColorImage"/>'s layers, leaving it ready to copy or blit.
    /// </summary>
    public void Record(VkCommandBuffer cmd, ReadOnlySpan<Camera> cameras, in FrameLighting lighting, Vector3 clearColor)
    {
        if (cameras.Length != Views)
            throw new ArgumentException($"{Views} view(s) to draw, {cameras.Length} camera(s) given", nameof(cameras));
        ref readonly var camera = ref cameras[0];
        var light = lighting;
        light.FogColor = clearColor;
        WriteFrame(cameras, light);
        _lampOn = lighting.LampRange > 1;
        (int Triangles, int Draws) lampDrawn = default, moonDrawn = default, handDrawn = default;
        if (_timestamps.IsNotNull)
            Api.vkCmdResetQueryPool(cmd, _timestamps, 0, TimestampSlots);
        _timed = _timestamps.IsNotNull;
        Mark(cmd, 0);

        // 0, 0b: the shadow maps, unless they're another's (ShadowsFrom).
        if (_shadowsFrom is null)
        {
            // 0: the headlamp's shadow map (cleared to "nothing in the way" with the lamp dark).
            Transition(cmd, _shadow.Image, VkImageAspectFlags.Depth, VkImageLayout.Undefined, VkImageLayout.DepthAttachmentOptimal);
            {
                var depthAttachment = new VkRenderingAttachmentInfo
                {
                    imageView = _shadow.View,
                    imageLayout = VkImageLayout.DepthAttachmentOptimal,
                    loadOp = VkAttachmentLoadOp.Clear,
                    storeOp = VkAttachmentStoreOp.Store,
                    clearValue = new VkClearValue { depthStencil = new VkClearDepthStencilValue(1, 0) },
                };
                var rendering = new VkRenderingInfo { renderArea = new VkRect2D(0, 0, ShadowSize, ShadowSize), layerCount = 1, pDepthAttachment = &depthAttachment };
                Api.vkCmdBeginRendering(cmd, &rendering);
                if (_lampOn)
                {
                    var viewport = new VkViewport(0, 0, ShadowSize, ShadowSize, 0, 1);
                    Api.vkCmdSetViewport(cmd, 0, 1, &viewport);
                    var scissor = new VkRect2D(0, 0, ShadowSize, ShadowSize);
                    Api.vkCmdSetScissor(cmd, 0, 1, &scissor);
                    var set = _sceneSet;
                    Api.vkCmdBindDescriptorSets(cmd, VkPipelineBindPoint.Graphics, _sceneLayout, 0, 1, &set, 0, null);
                    Api.vkCmdBindPipeline(cmd, VkPipelineBindPoint.Graphics, _shadowPipeline);
                    lampDrawn = DrawGeometry(cmd, _shadowSkinPipeline, [_frameMapped->LampViewProj], shadow: true);
                }
                Api.vkCmdEndRendering(cmd);
            }
            Transition(cmd, _shadow.Image, VkImageAspectFlags.Depth, VkImageLayout.DepthAttachmentOptimal, VkImageLayout.ShaderReadOnlyOptimal);
            Mark(cmd, 1);

            // 0b: the moon's.
            Transition(cmd, _moonShadow.Image, VkImageAspectFlags.Depth, VkImageLayout.Undefined, VkImageLayout.DepthAttachmentOptimal);
            {
                var depthAttachment = new VkRenderingAttachmentInfo
                {
                    imageView = _moonShadow.View,
                    imageLayout = VkImageLayout.DepthAttachmentOptimal,
                    loadOp = VkAttachmentLoadOp.Clear,
                    storeOp = VkAttachmentStoreOp.Store,
                    clearValue = new VkClearValue { depthStencil = new VkClearDepthStencilValue(1, 0) },
                };
                var rendering = new VkRenderingInfo { renderArea = new VkRect2D(0, 0, (uint)MoonShadowSize, (uint)MoonShadowSize), layerCount = 1, pDepthAttachment = &depthAttachment };
                Api.vkCmdBeginRendering(cmd, &rendering);
                if (_moonOn)
                {
                    var viewport = new VkViewport(0, 0, MoonShadowSize, MoonShadowSize, 0, 1);
                    Api.vkCmdSetViewport(cmd, 0, 1, &viewport);
                    var scissor = new VkRect2D(0, 0, (uint)MoonShadowSize, (uint)MoonShadowSize);
                    Api.vkCmdSetScissor(cmd, 0, 1, &scissor);
                    var set = _sceneSet;
                    Api.vkCmdBindDescriptorSets(cmd, VkPipelineBindPoint.Graphics, _sceneLayout, 0, 1, &set, 0, null);
                    Api.vkCmdBindPipeline(cmd, VkPipelineBindPoint.Graphics, _moonShadowPipeline);
                    moonDrawn = DrawGeometry(cmd, _moonShadowSkinPipeline, [_frameMapped->MoonViewProj], shadow: true);
                }
                Api.vkCmdEndRendering(cmd);
            }
            Transition(cmd, _moonShadow.Image, VkImageAspectFlags.Depth, VkImageLayout.DepthAttachmentOptimal, VkImageLayout.ShaderReadOnlyOptimal);
            Mark(cmd, 2);

            // 0c: the hand lamp's cube, its six faces in one pass (each a view, into its own layer; culled to what any
            // face sees, which is what's within its reach). Cleared to "nothing in the way" with no lamp.
            Transition(cmd, _handShadow.Image, VkImageAspectFlags.Depth, VkImageLayout.Undefined, VkImageLayout.DepthAttachmentOptimal);
            {
                var depthAttachment = new VkRenderingAttachmentInfo
                {
                    imageView = _handShadow.View,
                    imageLayout = VkImageLayout.DepthAttachmentOptimal,
                    loadOp = VkAttachmentLoadOp.Clear,
                    storeOp = VkAttachmentStoreOp.Store,
                    clearValue = new VkClearValue { depthStencil = new VkClearDepthStencilValue(1, 0) },
                };
                var rendering = new VkRenderingInfo
                {
                    renderArea = new VkRect2D(0, 0, HandShadowSize, HandShadowSize),
                    layerCount = 6,
                    viewMask = HandShadows ? ViewMask(6) : 0,
                    pDepthAttachment = &depthAttachment,
                };
                Api.vkCmdBeginRendering(cmd, &rendering);
                if (_handOn)
                {
                    var viewport = new VkViewport(0, 0, HandShadowSize, HandShadowSize, 0, 1);
                    Api.vkCmdSetViewport(cmd, 0, 1, &viewport);
                    var scissor = new VkRect2D(0, 0, HandShadowSize, HandShadowSize);
                    Api.vkCmdSetScissor(cmd, 0, 1, &scissor);
                    var set = _sceneSet;
                    Api.vkCmdBindDescriptorSets(cmd, VkPipelineBindPoint.Graphics, _sceneLayout, 0, 1, &set, 0, null);
                    Api.vkCmdBindPipeline(cmd, VkPipelineBindPoint.Graphics, _handShadowPipeline);
                    handDrawn = DrawGeometry(cmd, _handShadowSkinPipeline, _handFaces, shadow: true, reach: _hand!.Value.Range);
                }
                Api.vkCmdEndRendering(cmd);
            }
            Transition(cmd, _handShadow.Image, VkImageAspectFlags.Depth, VkImageLayout.DepthAttachmentOptimal, VkImageLayout.ShaderReadOnlyOptimal);
            Mark(cmd, 3);
        }
        else
        {
            Mark(cmd, 1);
            Mark(cmd, 2);
            Mark(cmd, 3);
        }

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
        // Culled against every eye's view: what either sees is drawn (to both, with multiview).
        var sceneDrawn = DrawGeometry(cmd, _sceneSkinPipeline, _viewProj.AsSpan(0, Views));
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
        Transition(cmd, _depth.Image, VkImageAspectFlags.Depth, VkImageLayout.DepthAttachmentOptimal, VkImageLayout.ShaderReadOnlyOptimal);
        Mark(cmd, 4);
        Stats = new FrameStats(sceneDrawn.Triangles, sceneDrawn.Draws + 1, _lights.Count, lampDrawn.Triangles, lampDrawn.Draws, moonDrawn.Triangles, moonDrawn.Draws, Views,
            handDrawn.Triangles, handDrawn.Draws);

        // 2b: the occlusion from the depth, at half resolution (each eye by its own projection: c.yz the right's).
        _projection = camera.Projection((float)Width / Height);
        var right = Views > 1 ? cameras[1].Projection((float)Width / Height) : _projection;
        PostPass(cmd, _ao, _aoPipeline, _postLayout, _aoSet, new PostConstants
        {
            A = new Vector4(_projection.M33, _projection.M43, _projection.M11, _projection.M22),
            B = new Vector4(Post.OcclusionRadius, Post.OcclusionIntensity, 1f / Width, 1f / Height),
            C = new Vector4(Post.OcclusionFar, right.M11, right.M22, 0),
        });
        Mark(cmd, 5);

        // 3: bloom at half resolution.
        var bloomStep = new Vector2(1f / _bloomA.Width, 1f / _bloomA.Height);
        PostPass(cmd, _bloomA, _brightPipeline, _postLayout, _brightSet, new PostConstants { A = new Vector4(Post.BloomThreshold, 0, 0, 0) });
        PostPass(cmd, _bloomB, _blurPipeline, _postLayout, _blurHSet, new PostConstants { A = new Vector4(bloomStep.X, 0, 0, 0) });
        PostPass(cmd, _bloomA, _blurPipeline, _postLayout, _blurVSet, new PostConstants { A = new Vector4(0, bloomStep.Y, 0, 0) });
        // The wide bloom: the half-res glow again at quarter res, blurred twice as far, so a lamp in fog has a halo you
        // feel as much as see (the benchmarks' lanterns and emergency lights).
        var wideStep = new Vector2(2f / _bloomC.Width, 2f / _bloomC.Height);
        PostPass(cmd, _bloomC, _brightPipeline, _postLayout, _blurHSet, new PostConstants { A = Vector4.Zero });
        PostPass(cmd, _bloomD, _blurPipeline, _postLayout, _blurH2Set, new PostConstants { A = new Vector4(wideStep.X, 0, 0, 0) });
        PostPass(cmd, _bloomC, _blurPipeline, _postLayout, _blurV2Set, new PostConstants { A = new Vector4(0, wideStep.Y, 0, 0) });
        Mark(cmd, 6);

        // 4: the composite (tonemapped, graded) into the LDR frame; FXAA and the overlay into the final one.
        var composite = new PostConstants
        {
            A = new Vector4(Post.BloomStrength, Post.Vignette, Post.Grain, Post.ColourLevels),
            B = new Vector4((float)(lighting.Time * 30 % 997), (float)Width / Height, Post.Ps2 ? 1 : 0, Post.Exposure),
            C = new Vector4(Post.WideBloom, Post.LensFringe, Post.Ps2 ? 0 : Post.Occlusion, 1f / _ao.Width),
        };
        Transition(cmd, _ldr.Image, VkImageAspectFlags.Color, VkImageLayout.Undefined, VkImageLayout.ColorAttachmentOptimal);
        BeginRendering(cmd, _ldr, withDepth: false);
        var compositeSet = _compositeSet;
        Api.vkCmdBindPipeline(cmd, VkPipelineBindPoint.Graphics, _compositePipeline);
        Api.vkCmdBindDescriptorSets(cmd, VkPipelineBindPoint.Graphics, _compositeLayout, 0, 1, &compositeSet, 0, null);
        Api.vkCmdPushConstants(cmd, _compositeLayout, VkShaderStageFlags.Fragment, 0, (uint)sizeof(PostConstants), &composite);
        Api.vkCmdDraw(cmd, 3, 1, 0, 0);
        Api.vkCmdEndRendering(cmd);
        Transition(cmd, _ldr.Image, VkImageAspectFlags.Color, VkImageLayout.ColorAttachmentOptimal, VkImageLayout.ShaderReadOnlyOptimal);
        Mark(cmd, 7);

        Transition(cmd, _color.Image, VkImageAspectFlags.Color, VkImageLayout.Undefined, VkImageLayout.ColorAttachmentOptimal);
        BeginRendering(cmd, _color, withDepth: false);
        var fxaa = new PostConstants { A = new Vector4(1f / Width, 1f / Height, Post.Ps2 ? 0 : 1, 0) };
        var fxaaSet = _fxaaSet;
        Api.vkCmdBindPipeline(cmd, VkPipelineBindPoint.Graphics, _fxaaPipeline);
        Api.vkCmdBindDescriptorSets(cmd, VkPipelineBindPoint.Graphics, _postLayout, 0, 1, &fxaaSet, 0, null);
        Api.vkCmdPushConstants(cmd, _postLayout, VkShaderStageFlags.Fragment, 0, (uint)sizeof(PostConstants), &fxaa);
        Api.vkCmdDraw(cmd, 3, 1, 0, 0);
        if (_overlayCount > 0)
        {
            Api.vkCmdBindPipeline(cmd, VkPipelineBindPoint.Graphics, _overlayPipeline);
            var size = new Vector4(OverlaySize ?? new Vector2(Width, Height), _overlaySplit, 0);
            Api.vkCmdPushConstants(cmd, _overlayLayout, VkShaderStageFlags.Vertex, 0, (uint)sizeof(Vector4), &size);
            var ob = _overlayVertices;
            ulong zero = 0;
            Api.vkCmdBindVertexBuffers(cmd, 0, 1, &ob, &zero);
            Api.vkCmdDraw(cmd, (uint)_overlayCount, 1, 0, 0);
        }
        Api.vkCmdEndRendering(cmd);
        Transition(cmd, _color.Image, VkImageAspectFlags.Color, VkImageLayout.ColorAttachmentOptimal, VkImageLayout.TransferSrcOptimal);
        Mark(cmd, 8);
    }

    /// <summary>
    /// The frame's soup and every kit instance, with whatever pipeline is bound; then the skinned ones with
    /// <paramref name="skinned"/>, the same pass's skinning twin (it stays bound after). Returns what it drew.
    /// </summary>
    /// <param name="views">The pass's views (one, or both eyes'): instances wholly outside all of them aren't drawn.</param>
    /// <param name="reach">A point light's reach (the hand lamp's cube): the land's cells aren't drawn, and the skinned pieces
    /// are culled too, loosely (0: neither).</param>
    (int Triangles, int Draws) DrawGeometry(VkCommandBuffer cmd, VkPipeline skinned, ReadOnlySpan<Matrix4x4> views, bool shadow = false, float reach = 0)
    {
        Span<Vector4> planes = stackalloc Vector4[6 * views.Length];
        for (int v = 0; v < views.Length; v++)
            FrustumPlanes(views[v], planes.Slice(v * 6, 6));
        int triangles = _vertexCount / 3, draws = _vertexCount > 0 ? 1 : 0;
        var identity = new DrawConstants { Model = Matrix4x4.Identity, Tint = Vector4.One };
        if (_vertexCount > 0)
        {
            Api.vkCmdPushConstants(cmd, _sceneLayout, VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment, 0, (uint)sizeof(DrawConstants), &identity);
            var vb = _vertices;
            ulong offset = 0;
            Api.vkCmdBindVertexBuffers(cmd, 0, 1, &vb, &offset);
            Api.vkCmdDraw(cmd, (uint)_vertexCount, 1, 0, 0);
        }
        foreach (var (mesh, draw, sphere, shadowless) in _draws)
        {
            if (shadow && shadowless)
                continue;
            // A piece far bigger than a light's reach (a cell of the land, hundreds of metres across) always overlaps it, and
            // would cost the hand lamp's cube everything in it for ground that hardly shades itself so close.
            if (reach > 0 && sphere.W > reach * 4)
                continue;
            if (!Seen(sphere, planes, views.Length))
                continue;
            var d = draw;
            Api.vkCmdPushConstants(cmd, _sceneLayout, VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment, 0, (uint)sizeof(DrawConstants), &d);
            var vb = mesh.Buffer;
            ulong offset = 0;
            Api.vkCmdBindVertexBuffers(cmd, 0, 1, &vb, &offset);
            Api.vkCmdDraw(cmd, (uint)mesh.Count, 1, 0, 0);
            triangles += mesh.Count / 3;
            draws++;
        }
        if (_skinDraws.Count > 0)
            Api.vkCmdBindPipeline(cmd, VkPipelineBindPoint.Graphics, skinned);
        var buffers = stackalloc VkBuffer[2];
        var offsets = stackalloc ulong[2] { 0, 0 };
        foreach (var (mesh, draw, sphere) in _skinDraws)
        {
            if (reach > 0 && !Seen(sphere with { W = sphere.W * SkinSlack + 0.5f }, planes, views.Length))
                continue;
            var d = draw;
            Api.vkCmdPushConstants(cmd, _sceneLayout, VkShaderStageFlags.Vertex | VkShaderStageFlags.Fragment, 0, (uint)sizeof(DrawConstants), &d);
            (buffers[0], buffers[1]) = (mesh.Buffer, mesh.Skin);
            Api.vkCmdBindVertexBuffers(cmd, 0, 2, buffers, offsets);
            Api.vkCmdDraw(cmd, (uint)mesh.Count, 1, 0, 0);
            triangles += mesh.Count / 3;
            draws++;
        }
        return (triangles, draws);
    }

    /// <summary>
    /// The six planes of a view (System.Numerics row vectors: clip = v × M; Vulkan's depth, 0 to 1), each (n, d) with n·p + d
    /// ≥ 0 inside, n unit length.
    /// </summary>
    public static void FrustumPlanes(in Matrix4x4 m, Span<Vector4> planes)
    {
        var c1 = new Vector4(m.M11, m.M21, m.M31, m.M41);
        var c2 = new Vector4(m.M12, m.M22, m.M32, m.M42);
        var c3 = new Vector4(m.M13, m.M23, m.M33, m.M43);
        var c4 = new Vector4(m.M14, m.M24, m.M34, m.M44);
        planes[0] = c4 + c1;
        planes[1] = c4 - c1;
        planes[2] = c4 + c2;
        planes[3] = c4 - c2;
        planes[4] = c3;
        planes[5] = c4 - c3;
        for (int i = 0; i < 6; i++)
        {
            float len = new Vector3(planes[i].X, planes[i].Y, planes[i].Z).Length();
            planes[i] = len > 0 ? planes[i] / len : new Vector4(0, 0, 0, 1);
        }
    }

    // How far past its bind pose's sphere a skinned piece's pose may reach (an arm out, a Whistler's legs), for culling it.
    const float SkinSlack = 2;

    static bool Seen(Vector4 sphere, ReadOnlySpan<Vector4> planes, int views)
    {
        for (int v = 0; v < views; v++)
            if (Visible(planes.Slice(v * 6, 6), sphere))
                return true;
        return false;
    }

    /// <summary>Whether a sphere (xyz centre, w radius) is at least partly inside the planes.</summary>
    public static bool Visible(ReadOnlySpan<Vector4> planes, Vector4 sphere)
    {
        foreach (var p in planes)
            if (p.X * sphere.X + p.Y * sphere.Y + p.Z * sphere.Z + p.W < -sphere.W)
                return false;
        return true;
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
            storeOp = VkAttachmentStoreOp.Store,
            clearValue = new VkClearValue { depthStencil = new VkClearDepthStencilValue(1, 0) },
        };
        var rendering = new VkRenderingInfo
        {
            renderArea = new VkRect2D(0, 0, (uint)target.Width, (uint)target.Height),
            layerCount = 1,
            // Multiview: each view (eye) into its own layer, from the one set of draws.
            viewMask = ViewMask(target.Layers),
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

    static uint ViewMask(int layers) => layers > 1 ? (1u << layers) - 1 : 0;

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
            // Every layer: a multiview target's eyes move together.
            subresourceRange = new VkImageSubresourceRange(aspect, 0, 1, 0, VK_REMAINING_ARRAY_LAYERS),
        };
        var dep = new VkDependencyInfo { imageMemoryBarrierCount = 1, pImageMemoryBarriers = &barrier };
        Api.vkCmdPipelineBarrier2(cmd, &dep);
    }

    const uint VK_QUEUE_FAMILY_IGNORED = ~0u;
    const uint VK_REMAINING_ARRAY_LAYERS = ~0u;

    Target CreateTarget(VkFormat format, int width, int height, VkImageUsageFlags usage, VkImageAspectFlags aspect, int layers = 1)
    {
        var info = new VkImageCreateInfo
        {
            imageType = VkImageType.Image2D,
            format = format,
            extent = new VkExtent3D(width, height, 1),
            mipLevels = 1,
            arrayLayers = (uint)layers,
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
            viewType = layers > 1 ? VkImageViewType.Image2DArray : VkImageViewType.Image2D,
            format = format,
            subresourceRange = new VkImageSubresourceRange(aspect, 0, 1, 0, (uint)layers),
        };
        VkImageView view;
        Check(Api.vkCreateImageView(&viewInfo, null, &view), "vkCreateImageView");
        return new Target(image, memory, view, width, height, layers);
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
        var images = stackalloc VkDescriptorImageInfo[24];
        images[0] = new VkDescriptorImageInfo { sampler = Post.Ps2 ? _crunchy : _diffuse.Sampler, imageView = _diffuse.View, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        images[1] = new VkDescriptorImageInfo { sampler = Post.Ps2 ? _crunchy : _spec.Sampler, imageView = _spec.View, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        images[2] = new VkDescriptorImageInfo { sampler = _backdrop.Sampler, imageView = _backdrop.View, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        images[3] = new VkDescriptorImageInfo { sampler = _linear, imageView = _scene.View, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        images[4] = new VkDescriptorImageInfo { sampler = _linear, imageView = _bloomA.View, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        images[5] = new VkDescriptorImageInfo { sampler = _linear, imageView = _bloomB.View, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        images[6] = new VkDescriptorImageInfo { sampler = _nearest, imageView = _scene.View, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        images[7] = new VkDescriptorImageInfo { sampler = _linear, imageView = _bloomA.View, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        images[8] = new VkDescriptorImageInfo { sampler = _lut.Sampler, imageView = _lut.View, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        var shadows = _shadowsFrom ?? this;
        images[9] = new VkDescriptorImageInfo { sampler = _shadowSampler, imageView = shadows._shadow.View, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        var buffer = new VkDescriptorBufferInfo { buffer = _frame, offset = 0, range = (ulong)sizeof(FrameData) };
        images[10] = new VkDescriptorImageInfo { sampler = _linear, imageView = _bloomC.View, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        images[11] = new VkDescriptorImageInfo { sampler = _linear, imageView = _bloomD.View, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        images[12] = new VkDescriptorImageInfo { sampler = _linear, imageView = _ldr.View, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        images[13] = new VkDescriptorImageInfo { sampler = Post.Ps2 ? _crunchy : _normal.Sampler, imageView = _normal.View, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        images[14] = new VkDescriptorImageInfo { sampler = _nearest, imageView = _depth.View, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        images[15] = new VkDescriptorImageInfo { sampler = _linear, imageView = _ao.View, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        images[16] = new VkDescriptorImageInfo { sampler = _shadowSampler, imageView = shadows._moonShadow.View, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        images[17] = new VkDescriptorImageInfo { sampler = _heroDiffuse.Sampler, imageView = _heroDiffuse.View, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        images[18] = new VkDescriptorImageInfo { sampler = _heroSpec.Sampler, imageView = _heroSpec.View, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        images[19] = new VkDescriptorImageInfo { sampler = _heroNormal.Sampler, imageView = _heroNormal.View, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        images[20] = new VkDescriptorImageInfo { sampler = _bigDiffuse.Sampler, imageView = _bigDiffuse.View, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        images[21] = new VkDescriptorImageInfo { sampler = _bigSpec.Sampler, imageView = _bigSpec.View, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        images[22] = new VkDescriptorImageInfo { sampler = _bigNormal.Sampler, imageView = _bigNormal.View, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        var writes = stackalloc VkWriteDescriptorSet[24];
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
        writes[10] = Image(_sceneSet, 4, 9);
        writes[11] = Image(_compositeSet, 3, 10);
        writes[12] = Image(_blurH2Set, 0, 10);
        writes[13] = Image(_blurV2Set, 0, 11);
        writes[14] = Image(_fxaaSet, 0, 12);
        writes[15] = Image(_sceneSet, 5, 13);
        writes[16] = Image(_aoSet, 0, 14);
        writes[17] = Image(_compositeSet, 4, 15);
        writes[18] = Image(_sceneSet, 6, 16);
        writes[19] = Image(_sceneSet, 7, 17);
        writes[20] = Image(_sceneSet, 8, 18);
        writes[21] = Image(_sceneSet, 9, 19);
        writes[22] = Image(_sceneSet, 11, 20);
        writes[23] = Image(_sceneSet, 12, 21);
        images[23] = new VkDescriptorImageInfo { sampler = _shadowSampler, imageView = shadows._handShadow.View, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        var more = stackalloc VkWriteDescriptorSet[2];
        more[0] = Image(_sceneSet, 13, 22);
        more[1] = Image(_sceneSet, 14, 23);
        Api.vkUpdateDescriptorSets(24, writes, 0, null);
        Api.vkUpdateDescriptorSets(2, more, 0, null);
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

    enum PipelineKind { Scene, Fullscreen, Overlay, FxAlpha, FxAdditive, Shadow }

    /// <param name="skinned">A scene or shadow pipeline's GPU-skinning twin: SKINNED defined in its vertex shader, and the
    /// bones and weights as a second vertex stream (skin.glsl).</param>
    VkPipeline Pipeline(VkPipelineLayout layout, string vertName, string fragName, VkFormat colorFormat, PipelineKind kind, bool depth, bool skinned = false, bool cube = false)
    {
        // Everything but the shadow maps (the body's, drawn once) draws each eye's view, both at once with multiview.
        bool multiview = Views > 1 && kind != PipelineKind.Shadow;
        string? mv = multiview ? "MULTIVIEW" : null;
        // (The hand lamp's cube: its six faces as six views, gl_ViewIndex the face: shadow_hand.vert.)
        var vert = CreateShader(vertName, ShaderKind.VertexShader, skinned ? "SKINNED" : null, cube ? "CUBE" : mv);
        var frag = CreateShader(fragName, ShaderKind.FragmentShader, view: mv);
        var entry = "main\0"u8;
        fixed (byte* pEntry = entry)
        {
            var stages = stackalloc VkPipelineShaderStageCreateInfo[2];
            stages[0] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Vertex, module = vert, pName = pEntry };
            stages[1] = new VkPipelineShaderStageCreateInfo { stage = VkShaderStageFlags.Fragment, module = frag, pName = pEntry };

            var attributes = stackalloc VkVertexInputAttributeDescription[13];
            var bindings = stackalloc VkVertexInputBindingDescription[2];
            ref var binding = ref bindings[0];
            binding = new VkVertexInputBindingDescription { binding = 0, inputRate = VkVertexInputRate.Vertex };
            bindings[1] = new VkVertexInputBindingDescription { binding = 1, inputRate = VkVertexInputRate.Vertex, stride = SkinWeights.Stride };
            uint attributeCount = 0;
            if (kind is PipelineKind.Scene or PipelineKind.Shadow)
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
                if (skinned)
                {
                    attributes[attributeCount++] = new VkVertexInputAttributeDescription { location = 11, binding = 1, format = VkFormat.R32G32B32A32Sfloat, offset = 0 };
                    attributes[attributeCount++] = new VkVertexInputAttributeDescription { location = 12, binding = 1, format = VkFormat.R32G32B32A32Sfloat, offset = 16 };
                }
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
                vertexBindingDescriptionCount = attributeCount == 0 ? 0u : skinned ? 2u : 1u,
                pVertexBindingDescriptions = bindings,
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
                // The shadow map renders both faces (cards and open-backed pieces cast from either side), biased.
                cullMode = kind == PipelineKind.Scene ? VkCullModeFlags.Back : VkCullModeFlags.None,
                frontFace = VkFrontFace.CounterClockwise,
                lineWidth = 1,
                depthBiasEnable = kind == PipelineKind.Shadow,
                depthBiasConstantFactor = 1.5f,
                depthBiasSlopeFactor = 2.0f,
            };
            var multisample = new VkPipelineMultisampleStateCreateInfo { rasterizationSamples = VkSampleCountFlags.Count1 };
            // The sky sits at the far plane under everything, and writes no depth.
            var depthState = new VkPipelineDepthStencilStateCreateInfo
            {
                depthTestEnable = depth,
                depthWriteEnable = depth && kind is PipelineKind.Scene or PipelineKind.Shadow,
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
            var blend = new VkPipelineColorBlendStateCreateInfo { attachmentCount = kind == PipelineKind.Shadow ? 0u : 1u, pAttachments = &blendAttachment };
            var dynamicStates = stackalloc VkDynamicState[2] { VkDynamicState.Viewport, VkDynamicState.Scissor };
            var dynamic = new VkPipelineDynamicStateCreateInfo { dynamicStateCount = 2, pDynamicStates = dynamicStates };
            var renderingInfo = new VkPipelineRenderingCreateInfo
            {
                viewMask = cube ? ViewMask(6) : multiview ? ViewMask(Views) : 0,
                colorAttachmentCount = kind == PipelineKind.Shadow ? 0u : 1u,
                pColorAttachmentFormats = kind == PipelineKind.Shadow ? null : &colorFormat,
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

    // Compiled once a process: a headset's two eyes and the mirror each make every pipeline, and the skinned twins share
    // their fragment shaders.
    static readonly System.Collections.Concurrent.ConcurrentDictionary<(string, string?, string?), byte[]> Compiled = new();

    /// <param name="define">A macro defined before the rest of the source (after its #version).</param>
    /// <param name="view">MULTIVIEW, for a pipeline that draws both eyes at once: defined, with GL_EXT_multiview enabled
    /// (Shaders/view.glsl).</param>
    VkShaderModule CreateShader(string name, ShaderKind kind, string? define = null, string? view = null)
    {
        var bytecode = Compiled.GetOrAdd((name, define, view), key =>
        {
            var source = ShaderSource(name);
            int line = source.IndexOf('\n');
            string head = (define is null ? "" : $"#define {define}\n") + (view is null ? "" : $"#define {view}\n#extension GL_EXT_multiview : require\n");
            source = source[..(line + 1)] + head + source[(line + 1)..];
            using var compiler = new Compiler();
            var result = compiler.Compile(source, name, new CompilerOptions { ShaderStage = kind, TargetEnv = TargetEnvironmentVersion.Vulkan_1_3 });
            if (result.Status != CompilationStatus.Success)
                throw new InvalidOperationException($"{name}{(define is null ? "" : $" ({define})")}{(view is null ? "" : $" ({view})")}: {result.ErrorMessage}");
            return result.Bytecode.ToArray();
        });
        fixed (byte* code = bytecode)
        {
            var info = new VkShaderModuleCreateInfo { codeSize = (nuint)bytecode.Length, pCode = (uint*)code };
            VkShaderModule module;
            Check(Api.vkCreateShaderModule(&info, null, &module), "vkCreateShaderModule");
            return module;
        }
    }

    void Free(GpuMesh m)
    {
        Api.vkDestroyBuffer(m.Buffer, null);
        Api.vkFreeMemory(m.Memory, null);
        if (m.Skin.IsNotNull)
        {
            Api.vkDestroyBuffer(m.Skin, null);
            Api.vkFreeMemory(m.SkinMemory, null);
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
        if (_timestamps.IsNotNull)
            Api.vkDestroyQueryPool(_timestamps, null);
        if (_fxCapacity > 0)
        {
            Api.vkDestroyBuffer(_fxVertices, null);
            Api.vkFreeMemory(_fxMemory, null);
        }
        Api.vkDestroySampler(_shadowSampler, null);
        foreach (var p in new[] { _shadowPipeline, _fxAlphaPipeline, _fxAddPipeline, _skyPipeline, _scenePipeline, _brightPipeline, _blurPipeline, _compositePipeline, _overlayPipeline, _fxaaPipeline, _aoPipeline, _moonShadowPipeline,
            _sceneSkinPipeline, _shadowSkinPipeline, _moonShadowSkinPipeline })
            Api.vkDestroyPipeline(p, null);
        if (HandShadows)
        {
            Api.vkDestroyPipeline(_handShadowPipeline, null);
            Api.vkDestroyPipeline(_handShadowSkinPipeline, null);
        }
        foreach (var l in new[] { _sceneLayout, _postLayout, _compositeLayout, _overlayLayout })
            Api.vkDestroyPipelineLayout(l, null);
        Api.vkDestroyDescriptorPool(_pool, null);
        foreach (var l in new[] { _sceneSetLayout, _postSetLayout, _compositeSetLayout })
            Api.vkDestroyDescriptorSetLayout(l, null);
        _diffuse.Dispose();
        _normal.Dispose();
        _heroDiffuse.Dispose();
        _heroSpec.Dispose();
        _heroNormal.Dispose();
        _bigDiffuse.Dispose();
        _bigSpec.Dispose();
        _bigNormal.Dispose();
        _spec.Dispose();
        _backdrop.Dispose();
        _lut.Dispose();
        Api.vkDestroySampler(_nearest, null);
        Api.vkDestroySampler(_linear, null);
        Api.vkDestroySampler(_crunchy, null);
        foreach (var (_, m) in _allMeshes)
            Free(m);
        Api.vkUnmapMemory(_bonesMemory);
        Api.vkDestroyBuffer(_bones, null);
        Api.vkFreeMemory(_bonesMemory, null);
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
        foreach (var t in new[] { _color, _scene, _depth, _bloomA, _bloomB, _shadow, _bloomC, _bloomD, _ldr, _ao, _moonShadow, _handShadow })
            DestroyTarget(t);
    }
}
