using System.Runtime.InteropServices;
using System.Text;
using Ballast.Render;
using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.OpenXR;
using Silk.NET.OpenXR.Extensions.KHR;
using Vortice.Vulkan;

namespace Ballast.Xr;

public sealed class XrUnavailableException(string message) : Exception(message);

/// <summary>
/// An OpenXR runtime and the headset on it (ARCHITECTURE §8 note 25): Quest over Link or Air Link, SteamVR, WMR,
/// Monado. Owns the XrInstance and XrSystem, and makes the game's Vulkan instance and device through
/// XR_KHR_vulkan_enable2, so the runtime gets the extensions its compositor needs on the GPU the headset is on.
/// <code>
/// using var headset = XrHeadset.Start("Dark Territory");
/// using var gpu = new GpuContext("Dark Territory", factory: headset);
/// using var session = headset.Begin(gpu);
/// </code>
/// </summary>
public sealed unsafe class XrHeadset : IVulkanFactory, IDisposable
{
    const string VulkanEnable2 = "XR_KHR_vulkan_enable2";

    internal readonly XR Xr;
    internal readonly Instance Instance;
    internal readonly ulong SystemId;
    readonly KhrVulkanEnable2 _vulkan;
    readonly nint _getInstanceProcAddr;
    bool _disposed;

    XrHeadset(XR xr, Instance instance, ulong systemId, KhrVulkanEnable2 vulkan, nint getInstanceProcAddr)
    {
        Xr = xr;
        Instance = instance;
        SystemId = systemId;
        _vulkan = vulkan;
        _getInstanceProcAddr = getInstanceProcAddr;

        var instanceProps = new InstanceProperties { Type = StructureType.InstanceProperties };
        Check(xr.GetInstanceProperties(instance, &instanceProps), "xrGetInstanceProperties");
        Runtime = Text(instanceProps.RuntimeName);
        var systemProps = new SystemProperties { Type = StructureType.SystemProperties };
        Check(xr.GetSystemProperties(instance, systemId, &systemProps), "xrGetSystemProperties");
        System = Text(systemProps.SystemName);

        uint count = 0;
        Check(xr.EnumerateViewConfigurationView(instance, systemId, ViewConfigurationType.PrimaryStereo, 0, &count, null), "xrEnumerateViewConfigurationViews");
        var views = new ViewConfigurationView[count];
        for (int i = 0; i < views.Length; i++)
            views[i].Type = StructureType.ViewConfigurationView;
        fixed (ViewConfigurationView* p = views)
            Check(xr.EnumerateViewConfigurationView(instance, systemId, ViewConfigurationType.PrimaryStereo, count, &count, p), "xrEnumerateViewConfigurationViews");
        if (count != 2)
            throw new XrUnavailableException($"the headset has {count} views, not a stereo pair");
        EyeWidth = (int)views[0].RecommendedImageRectWidth;
        EyeHeight = (int)views[0].RecommendedImageRectHeight;

        // Required before the device is made (the spec's order).
        var requirements = new GraphicsRequirementsVulkanKHR { Type = StructureType.GraphicsRequirementsVulkanKhr };
        Check(_vulkan.GetVulkanGraphicsRequirements2(instance, systemId, &requirements), "xrGetVulkanGraphicsRequirements2KHR");
    }

    /// <summary>The runtime's name ("Monado", "Oculus", "SteamVR/OpenXR").</summary>
    public string Runtime { get; }
    /// <summary>The headset's name as the runtime reports it.</summary>
    public string System { get; }
    /// <summary>The runtime's recommended render size per eye, in pixels.</summary>
    public int EyeWidth { get; }
    public int EyeHeight { get; }

    /// <summary>Finds the runtime and the headset, or says plainly which is missing.</summary>
    public static XrHeadset Start(string appName)
    {
        XR xr;
        try
        {
            xr = XR.GetApi();
        }
        catch (Exception e)
        {
            // All GetApi does is load the native loader, and Silk.NET reports a missing one in more than one way.
            throw new XrUnavailableException($"no OpenXR loader (openxr_loader.dll next to the game, or libopenxr-loader1 on Linux): {e.Message}");
        }

        if (!Extensions(xr).Contains(VulkanEnable2))
            throw new XrUnavailableException($"the OpenXR runtime has no {VulkanEnable2}");

        var app = new ApplicationInfo { ApiVersion = 1UL << 48 }; // OpenXR 1.0: every runtime speaks it
        Copy(appName, app.ApplicationName, 128);
        Copy("Ballast", app.EngineName, 128);
        var extensions = (byte**)SilkMarshal.StringArrayToPtr([VulkanEnable2]);
        Instance instance;
        try
        {
            var info = new InstanceCreateInfo
            {
                Type = StructureType.InstanceCreateInfo,
                ApplicationInfo = app,
                EnabledExtensionCount = 1,
                EnabledExtensionNames = extensions,
            };
            var created = xr.CreateInstance(&info, &instance);
            if (created == Result.ErrorRuntimeUnavailable)
                throw new XrUnavailableException("no OpenXR runtime is installed (Meta Quest Link, SteamVR, or Monado)");
            // What a runtime says when it's installed but its service isn't up (Monado without monado-service).
            if (created == Result.ErrorRuntimeFailure)
                throw new XrUnavailableException("the OpenXR runtime is installed but not running (start SteamVR, the Meta app, or monado-service)");
            if (created != Result.Success)
                throw new XrUnavailableException($"the OpenXR runtime wouldn't start a session ({created})");
        }
        finally
        {
            SilkMarshal.Free((nint)extensions);
        }

        var get = new SystemGetInfo { Type = StructureType.SystemGetInfo, FormFactor = FormFactor.HeadMountedDisplay };
        ulong systemId;
        var found = xr.GetSystem(instance, &get, &systemId);
        if (found != Result.Success)
        {
            xr.DestroyInstance(instance);
            throw new XrUnavailableException(found == Result.ErrorFormFactorUnavailable
                ? "the OpenXR runtime is there but no headset is connected"
                : $"no headset ({found})");
        }
        if (!xr.TryGetInstanceExtension<KhrVulkanEnable2>(null, instance, out var vulkan))
        {
            xr.DestroyInstance(instance);
            throw new XrUnavailableException($"couldn't load {VulkanEnable2}");
        }
        return new XrHeadset(xr, instance, systemId, vulkan, VulkanGetInstanceProcAddr());
    }

    static HashSet<string> Extensions(XR xr)
    {
        uint count = 0;
        var names = new HashSet<string>();
        if (xr.EnumerateInstanceExtensionProperties((byte*)null, 0, &count, null) != Result.Success)
            return names;
        var props = new ExtensionProperties[count];
        for (int i = 0; i < props.Length; i++)
            props[i].Type = StructureType.ExtensionProperties;
        fixed (ExtensionProperties* p = props)
        {
            xr.EnumerateInstanceExtensionProperties((byte*)null, count, &count, p);
            for (int i = 0; i < count; i++)
                names.Add(Text(p[i].ExtensionName));
        }
        return names;
    }

    /// <summary>The Vulkan loader's own entry point, which the runtime uses to make the instance and device.</summary>
    static nint VulkanGetInstanceProcAddr()
    {
        string[] names = OperatingSystem.IsWindows() ? ["vulkan-1.dll"]
            : OperatingSystem.IsMacOS() ? ["libvulkan.1.dylib", "libvulkan.dylib", "libMoltenVK.dylib"]
            : ["libvulkan.so.1", "libvulkan.so"];
        foreach (var name in names)
            if (NativeLibrary.TryLoad(name, out var lib) && NativeLibrary.TryGetExport(lib, "vkGetInstanceProcAddr", out var proc))
                return proc;
        throw new XrUnavailableException("no Vulkan loader");
    }

    public VkInstance CreateInstance(VkInstanceCreateInfo* info)
    {
        var create = new VulkanInstanceCreateInfoKHR
        {
            Type = StructureType.VulkanInstanceCreateInfoKhr,
            SystemId = SystemId,
            PfnGetInstanceProcAddr = new PfnVoidFunction((delegate* unmanaged[Cdecl]<void>)_getInstanceProcAddr),
            VulkanCreateInfo = info,
        };
        VkHandle instance;
        uint vkResult;
        Check(_vulkan.CreateVulkanInstance(Instance, &create, &instance, &vkResult), "xrCreateVulkanInstanceKHR");
        if (vkResult != 0)
            throw new XrUnavailableException($"the runtime couldn't make a Vulkan instance ({(VkResult)(int)vkResult})");
        return new VkInstance(instance.Handle);
    }

    public VkPhysicalDevice PhysicalDevice(VkInstance instance)
    {
        var get = new VulkanGraphicsDeviceGetInfoKHR
        {
            Type = StructureType.VulkanGraphicsDeviceGetInfoKhr,
            SystemId = SystemId,
            VulkanInstance = new VkHandle(instance.Handle),
        };
        VkHandle device;
        Check(_vulkan.GetVulkanGraphicsDevice2(Instance, &get, &device), "xrGetVulkanGraphicsDevice2KHR");
        return new VkPhysicalDevice(device.Handle);
    }

    public VkDevice CreateDevice(VkPhysicalDevice physical, VkDeviceCreateInfo* info)
    {
        var create = new VulkanDeviceCreateInfoKHR
        {
            Type = StructureType.VulkanDeviceCreateInfoKhr,
            SystemId = SystemId,
            PfnGetInstanceProcAddr = new PfnVoidFunction((delegate* unmanaged[Cdecl]<void>)_getInstanceProcAddr),
            VulkanPhysicalDevice = new VkHandle(physical.Handle),
            VulkanCreateInfo = info,
        };
        VkHandle device;
        uint vkResult;
        Check(_vulkan.CreateVulkanDevice(Instance, &create, &device, &vkResult), "xrCreateVulkanDeviceKHR");
        if (vkResult != 0)
            throw new XrUnavailableException($"the runtime couldn't make a Vulkan device ({(VkResult)(int)vkResult})");
        return new VkDevice(device.Handle);
    }

    /// <summary>Starts a stereo session on a <see cref="GpuContext"/> this headset made.</summary>
    /// <param name="renderScale">Per-eye render size against the runtime's recommendation. The look is low-res by design (GDD §32).</param>
    public XrStereoSession Begin(GpuContext gpu, double renderScale = 0.5) => new(this, gpu, renderScale);

    internal static void Check(Result result, string what)
    {
        if (result < 0)
            throw new InvalidOperationException($"{what} failed: {result}");
    }

    static string Text(byte* p) => Marshal.PtrToStringUTF8((nint)p) ?? "";

    static void Copy(string s, byte* into, int capacity)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        int n = Math.Min(bytes.Length, capacity - 1);
        for (int i = 0; i < n; i++)
            into[i] = bytes[i];
        into[n] = 0;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Xr.DestroyInstance(Instance);
        Xr.Dispose();
    }
}
