using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace Ballast.Render;

public sealed class GpuUnavailableException(string message) : Exception(message);

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
    /// Whether the device draws both of a headset's eyes in one pass (VK_KHR_multiview, core since Vulkan 1.1, but an
    /// optional feature: enabled here wherever the device has it, with room for two views). The stereo renderer picks
    /// its path from this (<see cref="StereoPath"/>, ARCHITECTURE §8 note 221).
    /// </summary>
    public bool Multiview { get; private set; }

    /// <summary>Nanoseconds per GPU timestamp tick, or 0 when the graphics queue can't time its work.</summary>
    public double TimestampPeriod { get; private set; }

    readonly VkPhysicalDeviceMemoryProperties _memory;

    /// <summary>Surface to present to, when created with a window; null when headless.</summary>
    public VkSurfaceKHR Surface { get; }
    public bool CanPresent => Surface.IsNotNull;

    /// <param name="instanceExtensions">Extra instance extensions, e.g. what SDL needs for a window surface.</param>
    /// <param name="createSurface">Creates the window surface once the instance exists; enables presentation.</param>
    /// <param name="factory">Makes the instance and device instead (a VR runtime); null for plain Vulkan.</param>
    public GpuContext(string appName = "Ballast", IReadOnlyList<string>? instanceExtensions = null, Func<VkInstance, VkSurfaceKHR>? createSurface = null,
        IVulkanFactory? factory = null)
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
        VkPhysicalDeviceMemoryProperties mem;
        InstanceApi.vkGetPhysicalDeviceMemoryProperties(PhysicalDevice, &mem);
        _memory = mem;

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
