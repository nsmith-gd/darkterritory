using System.Numerics;
using Ballast.Render;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Art;

/// <summary>
/// The crew's tools on the train (queue #753, ARCHITECTURE §8 note 1052; docs/design/upgrades.md §8.1's first tell: "a thing you
/// can see and point at"): the communication cord run along every car's right-hand eave with a pull at each car's middle, its
/// gong high on the cab's right wall; and the end-of-train telltale's lamp at the end of the dials' row, lit while the train's
/// whole. Flat colours, so they read by lamplight.
/// </summary>
public static partial class TrainKit
{
    static readonly Vector3 CordHemp = new(0.72f, 0.6f, 0.4f), GongBrass = new(0.8f, 0.62f, 0.3f), BracketIron = new(0.18f, 0.17f, 0.16f);

    /// <summary>How far down from a car's roof edge the cord runs along its right-hand side (m), through its eyes.</summary>
    public const float CordBelowRoof = 0.22f;
    /// <summary>How far out from the car's side the cord stands on its eyes (m).</summary>
    public const float CordOut = 0.06f;

    /// <summary>The cord's run on a car, in its frame: along the right side under the eave, end to end.</summary>
    public static (Vector3 Front, Vector3 Back) CordRun(CarShape shape)
    {
        float x = (float)shape.HalfWidth + CordOut, y = (float)shape.RoofHeight - CordBelowRoof, z = (float)shape.HalfLength - 0.25f;
        return (new Vector3(x, y, -z), new Vector3(x, y, z));
    }

    /// <summary>
    /// The communication cord along a car <paramref name="length"/> long (m), centred on (0, 0, 0) and running along Z: the hemp
    /// line, an iron eye every couple of metres holding it off the side (−X is the car's side), and the pull hanging at the
    /// middle, a wooden T on a short drop, red-painted so it's told from anything else on the side.
    /// </summary>
    public static MeshAsset CordLine(Look? look, float length)
    {
        var k = new Kit(look, 1052);
        float half = length / 2;
        k.Use("dress_string", CordHemp, 0.9f, 0);
        k.Rod(new Vector3(0, 0, -half), new Vector3(0, 0, half), 0.012f);
        int eyes = Math.Max(2, (int)MathF.Round(length / 2.2f) + 1);
        k.Use("rust_heavy", BracketIron, 0.6f, 0.4f);
        for (int i = 0; i < eyes; i++)
        {
            float z = -half + length * i / (eyes - 1);
            k.Rod(new Vector3(-CordOut, 0, z), new Vector3(0.004f, 0, z), 0.006f);
        }
        // The pull: down a hand's length on its drop, the red-painted T grip across it.
        k.Use("dress_string", CordHemp, 0.9f, 0);
        k.Rod(Vector3.Zero, new Vector3(0, -0.32f, 0), 0.006f);
        k.Use("dress_paint", new Vector3(0.7f, 0.1f, 0.08f), 0.5f, 0.1f);
        k.Cylinder(new Vector3(0, -0.34f, -0.07f), new Vector3(0, -0.34f, 0.07f), 0.014f, 6);
        return k.Build($"cord-line-{length:0.0}");
    }

    /// <summary>A metre of the cord, from (0, 0, 0) along −Z: scaled along its run where it crosses a coupling to the next car.</summary>
    public static MeshAsset CordSpan(Look? look)
    {
        var k = new Kit(look, 1057);
        k.Use("dress_string", CordHemp, 0.9f, 0);
        k.Rod(Vector3.Zero, new Vector3(0, 0, -1), 0.012f);
        return k.Build("cord-span");
    }

    /// <summary>The cord's gong in the cab (note 1052), in the engine's frame: high on the right wall, back from the front glass.</summary>
    public static Vector3 CordGongAt(CarShape engine)
    {
        var cab = engine.Cab!.Value;
        float z = (float)cab.Min.Z + 0.75f;
        return new Vector3((float)cab.Max.X - 0.02f, CeilingAt(engine, z) - 0.24f, z);
    }

    /// <summary>
    /// The communication cord's gong (note 1052): a small brass bell dome on an iron bracket off the wall (its foot at (0, 0, 0),
    /// the wall +X behind it, the dome out along −X), the striker's arm over it and the cord coming in to it from above.
    /// </summary>
    public static MeshAsset CordGong(Look? look)
    {
        var k = new Kit(look, 1053);
        k.Use("rust_heavy", BracketIron, 0.6f, 0.4f);
        k.BoxAt(new Vector3(-0.005f, 0, 0), new Vector3(0.005f, 0.09f, 0.06f));
        k.Rod(new Vector3(0, 0, 0), new Vector3(-0.1f, 0, 0), 0.008f);
        // The dome, open side down, a lip round it.
        k.Use("dress_brass", GongBrass, 0.3f, 0.9f);
        var profile = new[] { new Vector2(0.095f, -0.07f), new Vector2(0.09f, -0.055f), new Vector2(0.07f, -0.01f), new Vector2(0.04f, 0.02f), new Vector2(0.0f, 0.028f) };
        k.Lathe(new Vector3(-0.1f, 0, 0), profile, 14);
        // The striker: a sprung arm from the bracket with its hammer at the lip; the cord in to its tail.
        k.Use("rust_heavy", BracketIron, 0.6f, 0.4f);
        k.Rod(new Vector3(-0.01f, 0.05f, 0.04f), new Vector3(-0.17f, -0.05f, 0.03f), 0.004f);
        k.BoxAt(new Vector3(-0.18f, -0.06f, 0.03f), new Vector3(0.012f, 0.012f, 0.012f));
        k.Use("dress_string", CordHemp, 0.9f, 0);
        k.Rod(new Vector3(-0.01f, 0.05f, 0.04f), new Vector3(-0.01f, 0.24f, 0.3f), 0.006f);
        return k.Build("cord-gong");
    }

    /// <summary>
    /// The end-of-train telltale's lamp (note 1052), in the engine's frame: at the end of the dials' row over the front glass, to
    /// the left of the pressure gauge, facing back into the cab as they do.
    /// </summary>
    public static Vector3 TelltaleLampAt(CarShape engine)
    {
        var g = DriverGauge(engine, 0);
        return g - new Vector3(2 * (DriverGaugeRadius + 0.035f), 0, 0);
    }

    /// <summary>
    /// The telltale's lamp (note 1052): a brass-bezelled bullseye on a small black plate lettered TRAIN, facing +Z, centred on
    /// (0, 0, 0). Its glass is drawn by the scene (lit, or out), over the lens here.
    /// </summary>
    public static MeshAsset TelltaleLamp(Look? look)
    {
        var k = new Kit(look, 1054);
        k.Use("rust_heavy", BracketIron * 0.6f, 0.5f, 0.2f);
        k.BoxAt(new Vector3(0, -0.01f, -0.006f), new Vector3(0.08f, 0.1f, 0.006f));
        k.Use("dress_brass", GongBrass, 0.3f, 0.9f);
        k.Cylinder(new Vector3(0, 0.02f, 0), new Vector3(0, 0.02f, 0.03f), 0.045f, 14);
        k.Use("dress_paint", new Vector3(0.9f, 0.86f, 0.76f), 0.6f, 0);
        DressKit.Letter(k, "TRAIN", new Vector3(-DressKit.Width("TRAIN", 0.008f) / 2, -0.055f, 0.001f), Vector3.UnitX, -Vector3.UnitY, 0.008f);
        return k.Build("telltale-lamp");
    }

    /// <summary>
    /// A roof gangplank's plate or tread (note 1052), over its solid <paramref name="size"/> (m) centred on (0, 0, 0): chequer
    /// plate on an angle-iron edge each side, a worn bright nosing at its far end.
    /// </summary>
    public static MeshAsset GangplankPlate(Look? look, Vector3 size)
    {
        var k = new Kit(look, 1058);
        var h = size / 2;
        k.Use("iron_plate", Palette.IronGrey * 0.8f, 0.9f, 0.45f, tile: 0.25f);
        k.Box(-h, h);
        k.Use("rust_heavy", BracketIron, 0.6f, 0.4f);
        foreach (float x in new[] { -h.X, h.X })
            k.Box(new Vector3(x - 0.02f, -h.Y - 0.05f, -h.Z), new Vector3(x + 0.02f, h.Y + 0.02f, h.Z));
        k.Use("dress_brass", new Vector3(0.72f, 0.7f, 0.66f), 0.2f, 0.9f);
        k.Box(new Vector3(-h.X, h.Y, -h.Z), new Vector3(h.X, h.Y + 0.004f, -h.Z + 0.04f));
        return k.Build($"gangplank-{size.X:0.00}-{size.Y:0.00}-{size.Z:0.00}");
    }

    /// <summary>
    /// The stretcher (note 1052) as it hangs stowed on the guard van's wall: two ash poles 2.2 m long along Y, the canvas between
    /// them, its spreader bars and webbing straps, centred on (0, 0, 0), its back to the wall at −X.
    /// </summary>
    public static MeshAsset Stretcher(Look? look)
    {
        var k = new Kit(look, 1059);
        k.Use("dress_paint", new Vector3(0.55f, 0.42f, 0.26f), 0.7f, 0.05f);
        foreach (float z in new[] { -0.28f, 0.28f })
            k.Cylinder(new Vector3(0.03f, -1.1f, z), new Vector3(0.03f, 1.1f, z), 0.02f, 6);
        k.Use("sac", new Vector3(0.74f, 0.7f, 0.54f), 0.9f, 0, tile: 1);
        k.Box(new Vector3(0.02f, -0.85f, -0.27f), new Vector3(0.035f, 0.85f, 0.27f));
        k.Use("rust_heavy", BracketIron, 0.6f, 0.4f);
        foreach (float y in new[] { -0.9f, 0.9f })
            k.Rod(new Vector3(0.05f, y, -0.3f), new Vector3(0.05f, y, 0.3f), 0.008f);
        k.Use("leather", new Vector3(0.2f, 0.16f, 0.12f), 0.7f, 0, tile: 1);
        foreach (float y in new[] { -0.3f, 0.3f })
            k.Box(new Vector3(0.035f, y - 0.025f, -0.3f), new Vector3(0.05f, y + 0.025f, 0.3f));
        // Its two hooks on the wall.
        k.Use("rust_heavy", BracketIron, 0.6f, 0.4f);
        foreach (float z in new[] { -0.28f, 0.28f })
            k.Rod(new Vector3(-0.01f, 1.0f, z), new Vector3(0.06f, 1.05f, z), 0.008f);
        return k.Build("stretcher");
    }

    /// <summary>Where the telltale's glass is over its lamp's centre (the bezel's face).</summary>
    public static readonly Vector3 TelltaleGlass = new(0, 0.02f, 0.032f);
}
