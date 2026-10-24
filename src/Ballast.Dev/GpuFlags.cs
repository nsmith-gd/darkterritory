using Ballast.Render;

namespace Ballast.Dev;

/// <summary>
/// The GPU's developer flags (note 1017: a Linux player's black squares, on a GPU nobody here has): <c>--gpu-limits</c> holds
/// every device made after to a small GPU's limits, so `dt screenshot`, `dt perf` or a dev build shows on any machine what
/// the renderer makes of a 2 GB card or an old integrated GPU; <c>--gpu-safe</c> takes the safe path as a player's
/// <c>--gpu-safe</c> does in the app.
/// </summary>
public static class GpuFlags
{
    /// <summary>A small integrated GPU's: the spec's floor of array layers, 4096 px images, a 512 MiB image, a 1 GiB heap.</summary>
    public static readonly GpuLimits Small = new(256, 4096, 512UL << 20, 1UL << 30);

    /// <summary>
    /// Takes <c>--gpu-limits [small | layers=N,image=PX,bytes=MIB,heap=MIB]</c> (any of the four; the rest the device's own) and
    /// <c>--gpu-safe</c> out of <paramref name="args"/>, setting <see cref="GpuContext.LimitsOverride"/> and
    /// <see cref="GpuContext.SafeByDefault"/>; returns the rest.
    /// </summary>
    public static string[] Take(string[] args)
    {
        var (rest, limits, safe) = Read(args);
        if (safe)
            GpuContext.SafeByDefault = true;
        if (limits is not null)
        {
            GpuContext.LimitsOverride = limits;
            Console.Error.WriteLine($"gpu: held to {limits} (--gpu-limits)");
        }
        return rest;
    }

    /// <summary>The flags read out of <paramref name="args"/>, and the rest, without setting anything.</summary>
    public static (string[] Others, GpuLimits? Limits, bool Safe) Read(string[] args)
    {
        var rest = new List<string>();
        GpuLimits? limits = null;
        bool safe = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--gpu-safe")
                safe = true;
            else if (args[i] == "--gpu-limits")
                limits = Parse(i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : "small");
            else
                rest.Add(args[i]);
        }
        return ([.. rest], limits, safe);
    }

    public static GpuLimits Parse(string spec)
    {
        if (spec == "small")
            return Small;
        var limits = new GpuLimits(int.MaxValue, int.MaxValue, ulong.MaxValue, ulong.MaxValue);
        foreach (var part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2 || !long.TryParse(kv[1], out long n) || n <= 0)
                throw new ArgumentException($"--gpu-limits: '{part}' isn't name=number (layers, image, bytes, heap)");
            limits = kv[0] switch
            {
                "layers" => limits with { ArrayLayers = (int)n },
                "image" => limits with { Image2D = (int)n },
                "bytes" => limits with { ImageBytes = (ulong)n << 20 },
                "heap" => limits with { DeviceLocalBytes = (ulong)n << 20 },
                _ => throw new ArgumentException($"--gpu-limits: no limit called '{kv[0]}' (layers, image, bytes, heap)"),
            };
        }
        return limits;
    }
}
