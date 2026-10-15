namespace DarkTerritory.Sim.Player;

/// <summary>
/// An outfit as the wire has it (note 298, and the hat rack's, note 714): one byte, a look's colour and a hat. Under 9, a
/// colour alone (look.json crewColours; 8 the wearer's own); from 9, <c>hat × 9 + colour</c>, the hat counted from 1 (look.json
/// crewHats). Under the host's 64, so the Hello and the outfits message carry it as they did.
/// </summary>
public static class Outfit
{
    /// <summary>A colour's code for the wearer's own look (their id's).</summary>
    public const int Own = 8;

    /// <summary>The byte for <paramref name="colour"/> (−1: their own) under <paramref name="hat"/> (0: none).</summary>
    public static byte Encode(int colour, int hat) => (byte)(Math.Max(0, hat) * 9 + (colour is >= 0 and < Own ? colour : Own));

    /// <summary>Its colour, or null for the wearer's own.</summary>
    public static int? ColourOf(byte outfit) => outfit % 9 is var c && c == Own ? null : c;

    /// <summary>Its hat (0 none).</summary>
    public static int HatOf(byte outfit) => outfit / 9;
}
