using System.Numerics;

namespace DarkTerritory.Game;

/// <summary>Colours from the art sheet's palette (GDD §28), linear-ish for the greybox shader.</summary>
public static class Palette
{
    static Vector3 Hex(uint rgb)
    {
        static float Lin(uint c) => MathF.Pow(c / 255f, 2.2f);
        return new(Lin(rgb >> 16 & 0xFF), Lin(rgb >> 8 & 0xFF), Lin(rgb & 0xFF));
    }

    public static readonly Vector3 Charcoal = Hex(0x2B2B2B);
    public static readonly Vector3 SootBlack = Hex(0x1A1A1A);
    public static readonly Vector3 IronGrey = Hex(0x4A4D52);
    public static readonly Vector3 RustRed = Hex(0x6B3A26);
    public static readonly Vector3 DeepBrown = Hex(0x3A2E26);
    public static readonly Vector3 MuddyOlive = Hex(0x3C3F2E);
    public static readonly Vector3 TarnishedBrass = Hex(0x7A6440);
    public static readonly Vector3 BlueGrey = Hex(0x4A5563);
    public static readonly Vector3 Ballast = Hex(0x4B4640);
    public static readonly Vector3 PineDark = Hex(0x1F2620);
    public static readonly Vector3 LampAmber = Hex(0xE0A050);
    public static readonly Vector3 FurnaceOrange = Hex(0xD06020);
    /// <summary>GDD §28 corruption accents: dead ivory and bruised flesh, never neon.</summary>
    public static readonly Vector3 Corrupted = Hex(0x6E5A52);
    public static readonly Vector3 GreaseSheen = Hex(0x3A4452);
    /// <summary>A switch stand's target lamp: pale green set for the main line, red set for the branch.</summary>
    public static readonly Vector3 SignalGreen = Hex(0x8FB894);
    public static readonly Vector3 SignalRed = Hex(0xB8402A);
}
