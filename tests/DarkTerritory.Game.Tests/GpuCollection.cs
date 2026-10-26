namespace DarkTerritory.Game.Tests;

/// <summary>
/// The test classes that open a Vulkan device and dress a renderer with the look: one at a time. Each device uploads the
/// look's textures on its own (note 1047 shares them only within a device), about 7 GB in software Vulkan, so two at once
/// (PerfBudgetTests' second eye beside InteriorLightTests' house and barn) ran a 16 GB CI runner out of memory with the
/// assembly on its own (26 Oct: main's Linux test step killed, exit 137). The rest of the assembly still runs beside them.
/// </summary>
[CollectionDefinition(Name)]
public sealed class GpuCollection
{
    public const string Name = "Gpu";
}
