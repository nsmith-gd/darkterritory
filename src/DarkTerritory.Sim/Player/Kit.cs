namespace DarkTerritory.Sim.Player;

/// <summary>
/// The tools a crewmate carries (GDD §1322: "the tools already on the train are the weapons: shovel, wrench, crowbar").
/// T108 playtest: "I don't have a bludgeoning weapon"; everyone starts the night, and every respawn, with one.
/// </summary>
public enum Tool : byte { None, Crowbar, Shovel, Wrench }

/// <summary>
/// A player's hotbar: <see cref="Slots"/> slots packed a byte each into <see cref="PlayerState.Kit"/>, and the one in hand
/// (<see cref="PlayerState.HeldSlot"/>). Packed so it replicates as two numbers and compares by value: a predicting client
/// selects from the same intent the host does. Note 594 (the director, the inbox, 12 Oct: "Lethal Company's four shared
/// slots"): a slot holds a tool or a hand-sized thing (a find, a toy, a lamp: <see cref="PlayerState.Pockets"/> marks which
/// hold a thing, the thing itself a body with its <see cref="Physics.Body.Pocket"/>), so the tools carried are slots a find
/// can't have. Four: the number keys and the wire are built round it, so it's the game's shape, not a tuning number.
/// </summary>
public static class Kit
{
    public const int Slots = 4;

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

    /// <summary>Into the first empty slot (no tool, and no thing in it: <paramref name="pockets"/>); false if the bar's full.</summary>
    public static bool TryAdd(ref ulong kit, Tool tool, byte pockets = 0)
    {
        for (int i = 0; i < Slots; i++)
            if (At(kit, i) == Tool.None && (pockets & (1 << i)) == 0)
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

    /// <summary>
    /// The number key that puts a tool in empty hands (the first slot with one), or 0: there's one in hand already, or none
    /// to take. What a bot presses before a melee tool's work (D.7's breach; note 275).
    /// </summary>
    public static byte ToolToHand(in PlayerState s)
    {
        // A thing in the slot in hand (note 594) is hands full, as anything carried is.
        if (Held(s) != Tool.None || (s.Pockets & (1 << s.HeldSlot)) != 0)
            return 0;
        for (int i = 0; i < Slots; i++)
            if (At(s.Kit, i) != Tool.None)
                return (byte)(i + 1);
        return 0;
    }

    /// <summary>What's in hand.</summary>
    public static Tool Held(in PlayerState s) => At(s.Kit, s.HeldSlot);

    /// <summary>Note 594: a slot with neither a tool nor a thing in it.</summary>
    public static bool Empty(in PlayerState s, int slot) => At(s.Kit, slot) == Tool.None && (s.Pockets & (1 << slot)) == 0;

    /// <summary>Note 594: where a thing picked up goes: the slot in hand if it's empty, else the first empty one; −1 if all are full.</summary>
    public static int SlotFor(in PlayerState s)
    {
        if (s.HeldSlot < Slots && Empty(s, s.HeldSlot))
            return s.HeldSlot;
        for (int i = 0; i < Slots; i++)
            if (Empty(s, i))
                return i;
        return -1;
    }

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
            if (!Empty(s, slot))
            {
                s.HeldSlot = (byte)slot;
                return;
            }
        }
    }
}
