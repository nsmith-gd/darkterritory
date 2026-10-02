using System.Numerics;
using Ballast.Render;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Art;

/// <summary>
/// The crew lockers (ARCHITECTURE §8 note 173): a row of tall iron lockers along the kit car's left wall, painted the guard
/// van's olive, each with its grade on an enamel plate on its door in the HUD's own pixel font (as the lineside boards
/// have theirs, <see cref="SignKit"/>). The row's cabinets are one piece in the car's frame, open-fronted, with their shelves;
/// each door is its own piece, hung on its hinge at the cabinet's front edge, shut across it or swung out into the aisle.
/// </summary>
public static class LockerKit
{
    /// <summary>How far an open door swings out on its hinge: well past square, back towards the row (an iron door left open hangs on its hinge).</summary>
    public const float OpenSwing = MathF.PI * 150 / 180;

    /// <summary>How thick a door is (it stands this far proud of the cabinet's face when shut).</summary>
    public const float DoorThickness = 0.02f;

    static Vector3 F(Ballast.Double3 d) => new((float)d.X, (float)d.Y, (float)d.Z);

    /// <summary>The row's cabinets, in the car's frame: back, sides, top and floor of each, its shelves, and a plinth.</summary>
    public static MeshAsset Row(Look? look, IReadOnlyList<LockerBay> bays, int slots)
    {
        var k = new Kit(look, 151);
        const float Sheet = 0.012f;
        foreach (var bay in bays)
        {
            var (min, max) = (F(bay.Box.Min), F(bay.Box.Max));
            k.Reseed(bay.Index * 3.7f);
            // Outside: the olive sheet steel, the sides shared down the row (each draws its own, a sheet thick).
            k.Use("paint_olive", Palette.MuddyOlive, 0.8f, 0.25f);
            k.Box(min, new Vector3(min.X + Sheet, max.Y, max.Z));                                   // the back, at the wall
            k.Box(min, new Vector3(max.X, max.Y, min.Z + Sheet));                                   // the front side
            k.Box(new Vector3(min.X, min.Y, max.Z - Sheet), max);                                   // the back side
            k.Box(new Vector3(min.X, max.Y - Sheet, min.Z), max);                                   // the top
            // A cap along the top of the row, and the plinth it stands on (black, scuffed by boots).
            k.Use("paint_black", Palette.SootBlack, 0.9f, 0.1f, tile: 1);
            k.Box(new Vector3(min.X, min.Y, min.Z), new Vector3(max.X - 0.03f, min.Y + 0.05f, max.Z));
            // Inside: darker, the shelves (the locker's floor, then up evenly, Lockers.SlotAt), a coat hook under the top.
            k.Use("iron_plate", Palette.Charcoal, 0.9f, 0.2f);
            k.Tint = Palette.Charcoal * 1.4f;
            for (int s = 0; s < Math.Max(1, slots); s++)
            {
                float y = (float)Lockers.SlotAt(bay, s, slots, 0).Y;
                k.Box(new Vector3(min.X + Sheet, y - 0.015f, min.Z + Sheet), new Vector3(max.X - 0.01f, y, max.Z - Sheet));
            }
            k.Use("rust_heavy", Palette.IronGrey, 0.8f, 0.4f);
            k.Rod(new Vector3(min.X + 0.03f, max.Y - 0.12f, (min.Z + max.Z) / 2), new Vector3(min.X + 0.12f, max.Y - 0.16f, (min.Z + max.Z) / 2), 0.008f);
        }
        return k.Build($"lockers-{bays.Count}");
    }

    /// <summary>
    /// A locker's door, hung from its hinge (the origin) and running along +Z for <paramref name="width"/>, its face to +X:
    /// louvres top and bottom, a handle at its free edge, and the grade on an enamel plate at eye height, lettered
    /// <paramref name="px"/> to the font's pixel (the same on every door in the row, so the longest name fits).
    /// </summary>
    public static MeshAsset Door(Look? look, string name, float width, float height, float px)
    {
        var k = new Kit(look, 1510 + name.Length);
        float t = DoorThickness, w = width - 0.004f;
        k.Use("paint_olive", Palette.MuddyOlive, 0.75f, 0.25f);
        k.Box(new Vector3(0, 0.05f, 0.002f), new Vector3(t, height - 0.01f, w));
        // The louvres: dark slots pressed into the sheet, three at the top and three at the foot.
        k.Use("paint_black", Palette.SootBlack, 0.9f, 0.05f, tile: 1);
        foreach (float y0 in new[] { height - 0.2f, 0.2f })
            for (int i = 0; i < 3; i++)
            {
                float slot = y0 + (i - 1) * 0.045f;
                k.Box(new Vector3(t, slot - 0.01f, w * 0.25f), new Vector3(t + 0.004f, slot + 0.01f, w * 0.75f));
            }
        // The handle: a turned bar at the free edge, and the hasp under it.
        k.Use("rust_heavy", Palette.IronGrey, 0.6f, 0.5f);
        k.Rod(new Vector3(t + 0.03f, height * 0.5f - 0.08f, w - 0.05f), new Vector3(t + 0.03f, height * 0.5f + 0.08f, w - 0.05f), 0.01f);
        k.Box(new Vector3(t, height * 0.5f - 0.1f, w - 0.06f), new Vector3(t + 0.03f, height * 0.5f - 0.085f, w - 0.04f));
        // The plate: cream enamel, a black rim, and the grade in black on it, a hair proud. A card the same on the door's
        // inside, so an open locker's still known by name from the aisle.
        var font = BitmapFont.Default;
        void Plate()
        {
            float textW = font.Measure(name) * px, plateW = MathF.Min(w - 0.03f, MathF.Max(textW + 0.04f, w * 0.6f));
            float plateH = font.Height * px + 0.035f, cy = height - 0.42f, cz = w / 2;
            float face = t + 0.004f;
            k.Use("paint_black", Palette.SootBlack, 0.4f, 0.2f, tile: 1);
            k.Box(new Vector3(t, cy - plateH / 2 - 0.008f, cz - plateW / 2 - 0.008f), new Vector3(face, cy + plateH / 2 + 0.008f, cz + plateW / 2 + 0.008f));
            // (Flat enamel, untextured: a texture's grain under the pixel letters muddies them.)
            k.Use("enamel_plate", new Vector3(0.9f, 0.86f, 0.74f), 0.15f, 0.5f, tile: 1);
            k.Emissive = 0.16f;
            k.Box(new Vector3(face, cy - plateH / 2, cz - plateW / 2), new Vector3(face + 0.003f, cy + plateH / 2, cz + plateW / 2));
            k.Use("enamel_ink", new Vector3(0.05f, 0.05f, 0.06f), 0.1f, 0.1f, tile: 1);
            k.Emissive = 0;
            float x = face + 0.0045f, y = cy + font.Height * px / 2;
            // Read from the aisle, facing the wall: the train's front (−Z) is on your right, so the lines run from +Z to −Z.
            float z = cz + textW / 2;
            foreach (char ch in name)
            {
                var g = font.Glyph(ch);
                // Each row's run of lit pixels is one quad, not one per pixel: the door is a small prop (800 triangles).
                for (int gy = 0; gy < g.GetLength(0); gy++)
                    for (int gx = 0; gx < g.GetLength(1); gx++)
                    {
                        if (!g[gy, gx]) continue;
                        int run = gx;
                        while (run + 1 < g.GetLength(1) && g[gy, run + 1]) run++;
                        float z0 = z - gx * px, z1 = z - (run + 1) * px, y0 = y - gy * px;
                        k.Quad(new Vector3(x, y0, z0), new Vector3(x, y0, z1), new Vector3(x, y0 - px, z1), new Vector3(x, y0 - px, z0));
                        gx = run;
                    }
                z -= font.Advance * px;
            }
        }
        Plate();
        k.With(Matrix4x4.CreateTranslation(-t / 2, 0, -w / 2) * Matrix4x4.CreateRotationY(MathF.PI) * Matrix4x4.CreateTranslation(t / 2, 0, w / 2), Plate);
        return k.Build($"locker-door-{name}");
    }

    /// <summary>The lettering's pixel for a row of lockers: the longest name across most of a door.</summary>
    public static float LetterPixel(IReadOnlyList<LockerBay> bays, float width) =>
        MathF.Min(0.011f, (width - 0.07f) / Math.Max(1, bays.Max(b => BitmapFont.Default.Measure(b.Name))));

    /// <summary>Where a door hangs, in its car's frame: on its hinge at the cabinet's front edge, shut across it or swung open.</summary>
    public static Matrix4x4 DoorAt(LockerBay bay, bool open)
    {
        var box = bay.Box;
        // A left-wall locker's door faces +X and hinges at its front end (−Z); one on the right is the same turned about.
        bool left = bay.Facing > 0;
        var hinge = new Vector3((float)(left ? box.Max.X : box.Min.X), (float)box.Min.Y, (float)(left ? box.Min.Z : box.Max.Z));
        var turn = left ? Matrix4x4.Identity : Matrix4x4.CreateRotationY(MathF.PI);
        var swing = open ? Matrix4x4.CreateRotationY(OpenSwing) : Matrix4x4.Identity;
        return swing * turn * Matrix4x4.CreateTranslation(hinge);
    }
}
