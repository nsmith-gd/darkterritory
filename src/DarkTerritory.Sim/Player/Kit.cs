namespace DarkTerritory.Sim.Player;

/// <summary>
/// The tools a crewmate carries (GDD §1322: "the tools already on the train are the weapons: shovel, wrench, crowbar").
/// T108 playtest: "I don't have a bludgeoning weapon"; everyone starts the night, and every respawn, with one.
/// </summary>
public enum Tool : byte { None, Crowbar, Shovel, Wrench }

/// <summary>
/// A player's hotbar: <see cref="Slots"/> slots packed a byte each into <see cref="PlayerState.Kit"/>, and the one in hand
/// (<see cref="PlayerState.HeldSlot"/>). Packed so it replicates as two numbers and compares by value: a predicting client
/// selects from the same intent the host does.
/// </summary>
public static class Kit
{
    public const int Slots = 8;

    public static Tool At(ulong kit, int slot) => slot is >= 0 and < Slots ? (Tool)((kit >> (slot * 8)) & 0xFF) : Tool.None;

    public static ulong With(ulong kit, int slot, Tool tool) =>
        (kit & ~(0xFFUL << (slot * 8))) | ((ulong)tool << (slot * 8));

    /// <summary>A kit of these tools, from the first slot on.</summary>
    public static ulong Of(IEnumerable<Tool> tools)
    {
        ulong kit = 0;
        int slot = 0;
        foreach (var t in tools)
            if (t != Tool.None && slot < Slots)
                kit = With(kit, slot++, t);
        return kit;
    }

    /// <summary>Into the first empty slot; false if the bar's full.</summary>
    public static bool TryAdd(ref ulong kit, Tool tool)
    {
        for (int i = 0; i < Slots; i++)
            if (At(kit, i) == Tool.None)
            {
                kit = With(kit, i, tool);
                return true;
            }
        return false;
    }

    public static bool Has(ulong kit, Tool tool)
    {
        for (int i = 0; i < Slots; i++)
            if (At(kit, i) == tool)
                return true;
        return false;
    }

    /// <summary>What's in hand.</summary>
    public static Tool Held(in PlayerState s) => At(s.Kit, s.HeldSlot);

    /// <summary>
    /// This tick's choice of slot: a number key picks that slot (empty or not, so you can put your hands free), the wheel
    /// steps to the next slot with something in it.
    /// </summary>
    public static void Select(ref PlayerState s, in PlayerIntent intent)
    {
        if (intent.Select is >= 1 and <= Slots)
            s.HeldSlot = (byte)(intent.Select - 1);
        if (intent.Cycle == 0)
            return;
        int step = Math.Sign(intent.Cycle);
        for (int i = 1; i <= Slots; i++)
        {
            int slot = ((s.HeldSlot + step * i) % Slots + Slots) % Slots;
            if (At(s.Kit, slot) != Tool.None)
            {
                s.HeldSlot = (byte)slot;
                return;
            }
        }
    }
}
