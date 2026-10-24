using Ballast.Dev;
using Ballast.Render;

namespace DarkTerritory.Dev.Tests;

/// <summary>
/// The GPU's developer flags (ARCHITECTURE §8 note 1017): <c>--gpu-limits</c> read as a small GPU's limits, taken out of the
/// arguments with <c>--gpu-safe</c>, and a device's own limits held to them.
/// </summary>
public class GpuFlagsTests
{
    [Fact]
    public void LimitsAreReadByNameAndHoldADeviceToTheLower()
    {
        var some = GpuFlags.Parse("layers=300,heap=2048");
        Assert.Equal(300, some.ArrayLayers);
        Assert.Equal(2048UL << 20, some.DeviceLocalBytes);
        Assert.Equal(int.MaxValue, some.Image2D);
        var desktop = new GpuLimits(2048, 16384, 4UL << 30, 8UL << 30);
        Assert.Equal(new GpuLimits(300, 16384, 4UL << 30, 2048UL << 20), desktop.Within(some));
        Assert.Equal(GpuFlags.Small, GpuFlags.Parse("small"));
        Assert.Throws<ArgumentException>(() => GpuFlags.Parse("layers=lots"));
        Assert.Throws<ArgumentException>(() => GpuFlags.Parse("vram=512"));
    }

    [Fact]
    public void TheFlagsAreTakenOutOfTheArguments()
    {
        // (Read, not Take: Take sets the statics every device made after reads, and other tests here make devices.)
        var (rest, limits, safe) = GpuFlags.Read(["screenshot", "--gpu-limits", "--view", "roof", "--gpu-safe"]);
        Assert.Equal(["screenshot", "--view", "roof"], rest);
        Assert.Equal(GpuFlags.Small, limits);
        Assert.True(safe);
        var none = GpuFlags.Read(["perf"]);
        Assert.Equal(["perf"], none.Others);
        Assert.Null(none.Limits);
        Assert.False(none.Safe);
    }
}
