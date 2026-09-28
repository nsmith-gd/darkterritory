using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace Ballast.Render;

public sealed class GpuUnavailableException(string message) : Exception(message);

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
    public string DeviceName { get; }

    readonly VkPhysicalDeviceMemoryProperties _memory;

    public GpuContext(string appName = "Ballast")
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
            var info = new VkInstanceCreateInfo { pApplicationInfo = &app };
            VkInstance instance;
            Check(vkCreateInstance(&info, null, &instance), "vkCreateInstance");
            Instance = instance;
        }
        InstanceApi = GetApi(Instance);

        (PhysicalDevice, QueueFamily) = PickDevice();
        VkPhysicalDeviceProperties props;
        InstanceApi.vkGetPhysicalDeviceProperties(PhysicalDevice, &props);
        DeviceName = new string((sbyte*)props.deviceName);
        VkPhysicalDeviceMemoryProperties mem;
        InstanceApi.vkGetPhysicalDeviceMemoryProperties(PhysicalDevice, &mem);
        _memory = mem;

        float priority = 1;
        var queueInfo = new VkDeviceQueueCreateInfo { queueFamilyIndex = QueueFamily, queueCount = 1, pQueuePriorities = &priority };
        var features13 = new VkPhysicalDeviceVulkan13Features { dynamicRendering = true, synchronization2 = true };
        var deviceInfo = new VkDeviceCreateInfo { pNext = &features13, queueCreateInfoCount = 1, pQueueCreateInfos = &queueInfo };
        VkDevice device;
        Check(InstanceApi.vkCreateDevice(PhysicalDevice, &deviceInfo, null, &device), "vkCreateDevice");
        Device = device;
        Api = GetApi(Instance, Device);

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
                if ((props[i].queueFlags & VkQueueFlags.Graphics) != 0)
                    return (device, i);
        }
        throw new GpuUnavailableException("no Vulkan 1.3 device with a graphics queue");
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

    public void Dispose()
    {
        Api.vkDeviceWaitIdle();
        Api.vkDestroyCommandPool(CommandPool, null);
        Api.vkDestroyDevice(null);
        InstanceApi.vkDestroyInstance(null);
    }
}
