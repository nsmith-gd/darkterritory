using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace Ballast.Render;

public sealed class GpuUnavailableException(string message) : Exception(message);

/// <summary>
/// What the renderer fits its textures to (note 1017: a Linux player's screen had black squares where the textured quads
/// were, on a GPU and driver nobody knew; past a limit, a driver may say yes and sample zeros). The material arrays are
/// checked against these before they're made, and scaled down or cut short, loudly, rather than handed to the driver.
/// </summary>
/// <param name="ArrayLayers">How many layers a 2D array image may have (<c>maxImageArrayLayers</c>; the spec's floor is 256).</param>
/// <param name="Image2D">How wide and tall a 2D image may be (<c>maxImageDimension2D</c>).</param>
/// <param name="ImageBytes">The most one sampled RGBA8 array may hold: the format's <c>maxResourceSize</c> and the device's
/// <c>maxMemoryAllocationSize</c>, whichever is less.</param>
/// <param name="DeviceLocalBytes">The largest device-local memory heap: a small integrated GPU's carve-out, or the card's VRAM.</param>
public sealed record GpuLimits(int ArrayLayers, int Image2D, ulong ImageBytes, ulong DeviceLocalBytes)
{
    /// <summary>Each of this device's limits held to <paramref name="by"/>'s where that's lower (a developer's small GPU, --gpu-limits).</summary>
    public GpuLimits Within(GpuLimits by) => new(Math.Min(ArrayLayers, by.ArrayLayers), Math.Min(Image2D, by.Image2D), Math.Min(ImageBytes, by.ImageBytes),
        Math.Min(DeviceLocalBytes, by.DeviceLocalBytes));

    public override string ToString() =>
        $"{ArrayLayers} array layers, {Image2D} px images, {ImageBytes >> 20} MiB an image, {DeviceLocalBytes >> 20} MiB device-local";
}

/// <summary>
/// Makes the Vulkan instance and device in place of <see cref="GpuContext"/>. OpenXR's XR_KHR_vulkan_enable2 does
/// this: the runtime adds the extensions its compositor needs and picks the GPU the headset is on.
/// </summary>
public unsafe interface IVulkanFactory
{
    VkInstance CreateInstance(VkInstanceCreateInfo* info);
    VkPhysicalDevice PhysicalDevice(VkInstance instance);
    VkDevice CreateDevice(VkPhysicalDevice physical, VkDeviceCreateInfo* info);
}

/// <summary>
/// Vulkan 1.3 instance, device and graphics queue. Headless by default: on a machine with no GPU
/// (CI, cloud agents) it runs on Mesa's lavapipe software rasteriser, which is what lets an agent
/// take screenshots without a display (ARCHITECTURE §2 rule 3).
/// </summary>
public sealed unsafe class GpuContext : IDisposable
{
    public VkInstance Instance { get; }
    public VkInstanceApi InstanceApi { get; }
    public VkPhysicalDevice PhysicalDevice { get; }
    public VkDevice Device { get; }
    public VkDeviceApi Api { get; }
    public VkQueue Queue { get; }
    public uint QueueFamily { get; }
    public VkCommandPool CommandPool { get; }
    /// <summary>The anisotropy the samplers may use (16 at most), or 0 when the device has none.</summary>
    public float MaxAnisotropy { get; private set; }

    public string DeviceName { get; }
    /// <summary>
    /// The device as a bug report wants it (note 850: a joiner's Linux build drew black squares on a GPU and driver nobody
    /// knew): its name and kind, the driver's name, version and info string (Vulkan 1.2's driver properties), and the Vulkan
    /// version it offers. The driver version is the vendor's own encoding, given raw beside the Vulkan reading of it.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Whether the device draws both of a headset's eyes in one pass (VK_KHR_multiview, core since Vulkan 1.1, but an
    /// optional feature: enabled here wherever the device has it, with room for two views). The stereo renderer picks
    /// its path from this (<see cref="StereoPath"/>, ARCHITECTURE §8 note 221).
    /// </summary>
    public bool Multiview { get; private set; }

    /// <summary>The limits the renderer fits its textures to (<see cref="GpuLimits"/>), already held to <see cref="LimitsOverride"/>.</summary>
    public GpuLimits Limits { get; }

    /// <summary>
    /// A small GPU's limits to hold every device made after to (a developer's <c>--gpu-limits</c>, Ballast.Dev; note 1017):
    /// what a 2 GB card or an old integrated GPU would make of the textures, on any machine. Null in a player's game.
    /// </summary>
    public static GpuLimits? LimitsOverride { get; set; }

    /// <summary>
    /// The safe path (<c>--gpu-safe</c>, the SAFE GRAPHICS setting; note 1017): the post effects that read the frame back at
    /// lower resolution (bloom, the occlusion) off, and the material textures at half size without anisotropy, for a driver
    /// that draws the full path wrong. Read when a renderer loads its textures and every frame.
    /// </summary>
    public bool Safe { get; set; } = SafeByDefault;

    /// <summary>What <see cref="Safe"/> starts as on every device made after (a developer's <c>--gpu-safe</c> to `dt`, Ballast.Dev).</summary>
    public static bool SafeByDefault { get; set; }

    /// <summary>Nanoseconds per GPU timestamp tick, or 0 when the graphics queue can't time its work.</summary>
    public double TimestampPeriod { get; private set; }

    readonly VkPhysicalDeviceMemoryProperties _memory;

    /// <summary>Surface to present to, when created with a window; null when headless.</summary>
    public VkSurfaceKHR Surface { get; }
    public bool CanPresent => Surface.IsNotNull;

    /// <param name="instanceExtensions">Extra instance extensions, e.g. what SDL needs for a window surface.</param>
    /// <param name="createSurface">Creates the window surface once the instance exists; enables presentation.</param>
    /// <param name="factory">Makes the instance and device instead (a VR runtime); null for plain Vulkan.</param>
    /// <param name="limits">This context held to these limits too, as <see cref="LimitsOverride"/> holds every context (the visual
    /// tell audit's smaller texture budget, note 1043, without holding the rest of the process to it).</param>
    public GpuContext(string appName = "Ballast", IReadOnlyList<string>? instanceExtensions = null, Func<VkInstance, VkSurfaceKHR>? createSurface = null,
        IVulkanFactory? factory = null, GpuLimits? limits = null)
    {
        if (vkInitialize() != VkResult.Success)
            throw new GpuUnavailableException("Vulkan loader not found (install a GPU driver, or mesa-vulkan-drivers for software rendering)");

        var name = System.Text.Encoding.UTF8.GetBytes(appName + "\0");
        fixed (byte* pName = name)
        {
            var app = new VkApplicationInfo
            {
                pApplicationName = pName,
                pEngineName = pName,
                apiVersion = VkVersion.Version_1_3,
            };
            using var extensions = new Utf8Array(instanceExtensions ?? []);
            var info = new VkInstanceCreateInfo
            {
                pApplicationInfo = &app,
                enabledExtensionCount = (uint)extensions.Count,
                ppEnabledExtensionNames = extensions.Pointers,
            };
            if (factory is not null)
            {
                Instance = factory.CreateInstance(&info);
            }
            else
            {
                VkInstance instance;
                var created = vkCreateInstance(&info, null, &instance);
                // A loader with no installed driver (e.g. a GPU-less Windows machine) reports this.
                if (created is VkResult.ErrorIncompatibleDriver or VkResult.ErrorInitializationFailed)
                    throw new GpuUnavailableException($"no Vulkan driver installed ({created})");
                Check(created, "vkCreateInstance");
                Instance = instance;
            }
        }
        InstanceApi = GetApi(Instance);
        if (createSurface is not null)
            Surface = createSurface(Instance);

        (PhysicalDevice, QueueFamily) = factory is null ? PickDevice() : (factory.PhysicalDevice(Instance), GraphicsFamily(factory.PhysicalDevice(Instance)));
        VkPhysicalDeviceProperties props;
        InstanceApi.vkGetPhysicalDeviceProperties(PhysicalDevice, &props);
        DeviceName = new string((sbyte*)props.deviceName);
        Description = Describe(InstanceApi, PhysicalDevice, DeviceName, props);
        VkPhysicalDeviceMemoryProperties mem;
        InstanceApi.vkGetPhysicalDeviceMemoryProperties(PhysicalDevice, &mem);
        _memory = mem;
        Limits = ReadLimits(InstanceApi, PhysicalDevice, props, mem);
        if (LimitsOverride is { } small)
            Limits = Limits.Within(small);
        if (limits is not null)
            Limits = Limits.Within(limits);

        float priority = 1;
        var queueInfo = new VkDeviceQueueCreateInfo { queueFamilyIndex = QueueFamily, queueCount = 1, pQueuePriorities = &priority };
        // Multiview where the device has it, and room for both eyes in it.
        var has11 = new VkPhysicalDeviceVulkan11Features();
        var has = new VkPhysicalDeviceFeatures2 { pNext = &has11 };
        InstanceApi.vkGetPhysicalDeviceFeatures2(PhysicalDevice, &has);
        var multiviewProps = new VkPhysicalDeviceMultiviewProperties();
        var props2 = new VkPhysicalDeviceProperties2 { pNext = &multiviewProps };
        InstanceApi.vkGetPhysicalDeviceProperties2(PhysicalDevice, &props2);
        Multiview = has11.multiview && multiviewProps.maxMultiviewViewCount >= 2;
        var features11 = new VkPhysicalDeviceVulkan11Features { multiview = Multiview };
        var features13 = new VkPhysicalDeviceVulkan13Features { pNext = &features11, dynamicRendering = true, synchronization2 = true };
        // Anisotropic filtering where the device has it (every desktop GPU, and lavapipe): textures stay sharp at a
        // glancing angle, the track and the roofs running away from you.
        VkPhysicalDeviceFeatures supported;
        InstanceApi.vkGetPhysicalDeviceFeatures(PhysicalDevice, &supported);
        var enabled = new VkPhysicalDeviceFeatures { samplerAnisotropy = supported.samplerAnisotropy };
        MaxAnisotropy = supported.samplerAnisotropy ? Math.Min(16f, props.limits.maxSamplerAnisotropy) : 0;
        using var deviceExtensions = new Utf8Array(CanPresent ? ["VK_KHR_swapchain"] : []);
        var deviceInfo = new VkDeviceCreateInfo
        {
            pNext = &features13,
            pEnabledFeatures = &enabled,
            queueCreateInfoCount = 1,
            pQueueCreateInfos = &queueInfo,
            enabledExtensionCount = (uint)deviceExtensions.Count,
            ppEnabledExtensionNames = deviceExtensions.Pointers,
        };
        if (factory is not null)
        {
            Device = factory.CreateDevice(PhysicalDevice, &deviceInfo);
        }
        else
        {
            VkDevice device;
            Check(InstanceApi.vkCreateDevice(PhysicalDevice, &deviceInfo, null, &device), "vkCreateDevice");
            Device = device;
        }
        Api = GetApi(Instance, Device);

        // GPU timing (the renderer's per-pass times, `dt perf`): where the queue keeps timestamps at all.
        uint familyCount = 0;
        InstanceApi.vkGetPhysicalDeviceQueueFamilyProperties(PhysicalDevice, &familyCount, null);
        var families = new VkQueueFamilyProperties[familyCount];
        fixed (VkQueueFamilyProperties* f = families)
            InstanceApi.vkGetPhysicalDeviceQueueFamilyProperties(PhysicalDevice, &familyCount, f);
        TimestampPeriod = families[QueueFamily].timestampValidBits > 0 ? props.limits.timestampPeriod : 0;

        VkQueue queue;
        Api.vkGetDeviceQueue(QueueFamily, 0, &queue);
        Queue = queue;

        var poolInfo = new VkCommandPoolCreateInfo { flags = VkCommandPoolCreateFlags.ResetCommandBuffer, queueFamilyIndex = QueueFamily };
        VkCommandPool pool;
        Check(Api.vkCreateCommandPool(&poolInfo, null, &pool), "vkCreateCommandPool");
        CommandPool = pool;
    }

    /// <summary>The device's name, kind, driver and Vulkan version in one line (<see cref="Description"/>).</summary>
    static string Describe(VkInstanceApi api, VkPhysicalDevice device, string deviceName, in VkPhysicalDeviceProperties props)
    {
        var driver = new VkPhysicalDeviceDriverProperties();
        var props2 = new VkPhysicalDeviceProperties2 { pNext = &driver };
        api.vkGetPhysicalDeviceProperties2(device, &props2);
        string kind = props.deviceType switch
        {
            VkPhysicalDeviceType.DiscreteGpu => "discrete GPU",
            VkPhysicalDeviceType.IntegratedGpu => "integrated GPU",
            VkPhysicalDeviceType.VirtualGpu => "virtual GPU",
            VkPhysicalDeviceType.Cpu => "CPU (software rendering)",
            _ => "other",
        };
        var apiVersion = new VkVersion(props.apiVersion);
        var driverVersion = new VkVersion(props.driverVersion);
        string driverName = new string((sbyte*)driver.driverName), driverInfo = new string((sbyte*)driver.driverInfo);
        return $"{deviceName} ({kind}); driver {driverName} {driverInfo}".TrimEnd()
            + $" ({driver.driverID}), driver version {driverVersion.Major}.{driverVersion.Minor}.{driverVersion.Patch} (0x{props.driverVersion:x8}), "
            + $"Vulkan {apiVersion.Major}.{apiVersion.Minor}.{apiVersion.Patch}, vendor 0x{props.vendorID:x4} device 0x{props.deviceID:x4}";
    }

    static GpuLimits ReadLimits(VkInstanceApi api, VkPhysicalDevice device, in VkPhysicalDeviceProperties props, in VkPhysicalDeviceMemoryProperties mem)
    {
        var maintenance3 = new VkPhysicalDeviceMaintenance3Properties();
        var props2 = new VkPhysicalDeviceProperties2 { pNext = &maintenance3 };
        api.vkGetPhysicalDeviceProperties2(device, &props2);
        ulong image = maintenance3.maxMemoryAllocationSize;
        VkImageFormatProperties format;
        if (api.vkGetPhysicalDeviceImageFormatProperties(device, VkFormat.R8G8B8A8Srgb, VkImageType.Image2D, VkImageTiling.Optimal,
                VkImageUsageFlags.Sampled | VkImageUsageFlags.TransferDst, 0, &format) == VkResult.Success)
            image = Math.Min(image, format.maxResourceSize);
        ulong local = 0;
        for (int i = 0; i < mem.memoryHeapCount; i++)
            if ((mem.memoryHeaps[i].flags & VkMemoryHeapFlags.DeviceLocal) != 0)
                local = Math.Max(local, mem.memoryHeaps[i].size);
        return new GpuLimits((int)props.limits.maxImageArrayLayers, (int)props.limits.maxImageDimension2D, image, local);
    }

    (VkPhysicalDevice, uint) PickDevice()
    {
        uint count = 0;
        InstanceApi.vkEnumeratePhysicalDevices(&count, null);
        if (count == 0)
            throw new GpuUnavailableException("no Vulkan devices");
        var devices = new VkPhysicalDevice[count];
        fixed (VkPhysicalDevice* p = devices)
            InstanceApi.vkEnumeratePhysicalDevices(&count, p);

        // Prefer a real GPU; fall back to CPU (lavapipe) so headless machines still work.
        var ranked = devices.Select(d =>
        {
            VkPhysicalDeviceProperties props;
            InstanceApi.vkGetPhysicalDeviceProperties(d, &props);
            int score = props.deviceType switch
            {
                VkPhysicalDeviceType.DiscreteGpu => 3,
                VkPhysicalDeviceType.IntegratedGpu => 2,
                VkPhysicalDeviceType.Cpu => 1,
                _ => 0,
            };
            return (device: d, score, apiOk: props.apiVersion >= VkVersion.Version_1_3);
        }).Where(x => x.apiOk).OrderByDescending(x => x.score);

        foreach (var (device, _, _) in ranked)
        {
            uint families = 0;
            InstanceApi.vkGetPhysicalDeviceQueueFamilyProperties(device, &families, null);
            var props = new VkQueueFamilyProperties[families];
            fixed (VkQueueFamilyProperties* p = props)
                InstanceApi.vkGetPhysicalDeviceQueueFamilyProperties(device, &families, p);
            for (uint i = 0; i < families; i++)
            {
                if ((props[i].queueFlags & VkQueueFlags.Graphics) == 0)
                    continue;
                if (Surface.IsNotNull)
                {
                    VkBool32 present;
                    InstanceApi.vkGetPhysicalDeviceSurfaceSupportKHR(device, i, Surface, &present);
                    if (!present)
                        continue;
                }
                return (device, i);
            }
        }
        throw new GpuUnavailableException("no Vulkan 1.3 device with a graphics queue");
    }

    uint GraphicsFamily(VkPhysicalDevice device)
    {
        uint families = 0;
        InstanceApi.vkGetPhysicalDeviceQueueFamilyProperties(device, &families, null);
        var props = new VkQueueFamilyProperties[families];
        fixed (VkQueueFamilyProperties* p = props)
            InstanceApi.vkGetPhysicalDeviceQueueFamilyProperties(device, &families, p);
        for (uint i = 0; i < families; i++)
            if ((props[i].queueFlags & VkQueueFlags.Graphics) != 0)
                return i;
        throw new GpuUnavailableException("the headset's GPU has no graphics queue");
    }

    public uint FindMemoryType(uint typeBits, VkMemoryPropertyFlags flags)
    {
        for (int i = 0; i < _memory.memoryTypeCount; i++)
            if ((typeBits & (1u << i)) != 0 && (_memory.memoryTypes[i].propertyFlags & flags) == flags)
                return (uint)i;
        throw new InvalidOperationException($"no memory type for {flags}");
    }

    public VkDeviceMemory Allocate(VkMemoryRequirements req, VkMemoryPropertyFlags flags)
    {
        var info = new VkMemoryAllocateInfo { allocationSize = req.size, memoryTypeIndex = FindMemoryType(req.memoryTypeBits, flags) };
        VkDeviceMemory memory;
        Check(Api.vkAllocateMemory(&info, null, &memory), "vkAllocateMemory");
        return memory;
    }

    /// <summary>Records and submits a one-shot command buffer, then waits for it.</summary>
    public void Submit(Action<VkCommandBuffer> record)
    {
        var alloc = new VkCommandBufferAllocateInfo { commandPool = CommandPool, level = VkCommandBufferLevel.Primary, commandBufferCount = 1 };
        VkCommandBuffer cmd;
        Check(Api.vkAllocateCommandBuffers(&alloc, &cmd), "vkAllocateCommandBuffers");
        var begin = new VkCommandBufferBeginInfo { flags = VkCommandBufferUsageFlags.OneTimeSubmit };
        Check(Api.vkBeginCommandBuffer(cmd, &begin), "vkBeginCommandBuffer");
        record(cmd);
        Check(Api.vkEndCommandBuffer(cmd), "vkEndCommandBuffer");
        var submit = new VkSubmitInfo { commandBufferCount = 1, pCommandBuffers = &cmd };
        Check(Api.vkQueueSubmit(Queue, 1, &submit, VkFence.Null), "vkQueueSubmit");
        Check(Api.vkQueueWaitIdle(Queue), "vkQueueWaitIdle");
        Api.vkFreeCommandBuffers(CommandPool, 1, &cmd);
    }

    public static void Check(VkResult result, string what)
    {
        if (result != VkResult.Success)
            throw new InvalidOperationException($"{what} failed: {result}");
    }

    /// <summary>Waits for the GPU to finish everything it's been given (before freeing what it might still be using).</summary>
    public void WaitIdle() => Api.vkDeviceWaitIdle();

    public void Dispose()
    {
        Api.vkDeviceWaitIdle();
        Api.vkDestroyCommandPool(CommandPool, null);
        Api.vkDestroyDevice(null);
        if (Surface.IsNotNull)
            InstanceApi.vkDestroySurfaceKHR(Surface, null);
        InstanceApi.vkDestroyInstance(null);
    }
}

/// <summary>Null-terminated UTF-8 strings pinned in unmanaged memory, for Vulkan name arrays.</summary>
sealed unsafe class Utf8Array : IDisposable
{
    readonly nint[] _strings;
    readonly byte** _pointers;

    public Utf8Array(IReadOnlyList<string> values)
    {
        _strings = values.Select(v => (nint)System.Runtime.InteropServices.Marshal.StringToCoTaskMemUTF8(v)).ToArray();
        _pointers = (byte**)System.Runtime.InteropServices.NativeMemory.Alloc((nuint)Math.Max(1, _strings.Length), (nuint)sizeof(byte*));
        for (int i = 0; i < _strings.Length; i++)
            _pointers[i] = (byte*)_strings[i];
    }

    public int Count => _strings.Length;
    public byte** Pointers => _pointers;

    public void Dispose()
    {
        foreach (var p in _strings)
            System.Runtime.InteropServices.Marshal.FreeCoTaskMem(p);
        System.Runtime.InteropServices.NativeMemory.Free(_pointers);
    }
}
