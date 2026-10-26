using System.Numerics;
using Ballast.Render;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Art;

/// <summary>
/// A cargo car's load read from outside it (GDD Part Eleven question 7; note 949 rank 14; note 1025): the placards and
/// stencils on its sides for what's dangerous or precious in it (powder, chemicals, medicine, the comet), and the stain
/// down its side once what's in it has spoiled. Painted on the walls' outer faces, a hair proud, both sides; the car's
/// frame (+X its right, −Z forward), as <see cref="TrainKit.CarNumber"/> is. The doors cracked on the load, the breath and
/// the heads at the crack, and the hanging door are the scene's (SceneArt.Car).
/// </summary>
public static class CargoKit
{
    /// <summary>The cargoes a car is placarded for: dangerous or precious enough that the yard paints it on the side.</summary>
    public static bool Placarded(CargoKind cargo) => cargo is CargoKind.Ammunition or CargoKind.Chemicals or CargoKind.Medicine or CargoKind.Comet;

    /// <summary>What's stencilled under the placard: the cargo, and the yard's warning.</summary>
    public static (string Word, string Warning) Stencil(CargoKind cargo) => cargo switch
    {
        CargoKind.Ammunition => ("POWDER", "NO LIGHTS"),
        CargoKind.Chemicals => ("CHEMICALS", "THIS WAY UP"),
        CargoKind.Medicine => ("MEDICINE", "FRAGILE"),
        CargoKind.Comet => ("COMET", "DO NOT OPEN"),
        _ => ("", ""),
    };

    /// <summary>The placard's ground and its emblem's colour.</summary>
    public static (Vector3 Ground, Vector3 Emblem) Colours(CargoKind cargo) => cargo switch
    {
        CargoKind.Ammunition => (new Vector3(0.72f, 0.22f, 0.07f), Palette.SootBlack),
        CargoKind.Chemicals => (new Vector3(0.74f, 0.58f, 0.1f), Palette.SootBlack),
        CargoKind.Medicine => (new Vector3(0.62f, 0.6f, 0.54f), new Vector3(0.62f, 0.07f, 0.05f)),
        _ => (new Vector3(0.05f, 0.06f, 0.06f), new Vector3(0.35f, 0.95f, 0.45f)),
    };

    // The placard board's middle up the side, the diamond's half-diagonal; the stencil's pixel and the warning's, under it.
    const float PlacardY = 2.5f, Diamond = 0.5f, WordPixel = 0.03f, WarnPixel = 0.022f;
    // The emblems are drawn for a 0.42 m diamond, and scaled to this one.
    const float EmblemScale = Diamond / 0.42f;

    /// <summary>
    /// Where along the car its placard board hangs: between the seventh and eighth of the eight bays of posts and braces
    /// down its side (TrainKit.Car's outside framing), behind the side door, clear of the door's slide and the posts.
    /// </summary>
    public static float PlacardZ(CarShape shape)
    {
        float l = (float)shape.HalfLength;
        return -l + 0.06f + 6.5f * (2 * l - 0.12f) / 8;
    }

    // The roof's word: out from the walk's centreline (the walk is 0.45 m either side of it), its pixel.
    const float RoofWordX = 0.98f, RoofPixel = 0.055f;

    /// <summary>Where along the roof the cargo's name is painted: a third of the way back from the front, clear of the
    /// number at the front end and the hatch behind the middle.</summary>
    public static float RoofWordZ(CarShape shape) => -(float)shape.HalfLength / 2.3f;

    /// <summary>
    /// The placards and stencils of <paramref name="cargo"/> on both sides of a car shaped <paramref name="shape"/>: a board
    /// bolted over the bay's bracing, an enamelled diamond on it with its emblem (the powder's burst, the chemicals' flask,
    /// the medicine's cross, the comet's ring and tail, lit a little as the comet's green is), and the cargo's name
    /// stencilled under it with the yard's warning. Empty for a cargo that isn't placarded.
    /// </summary>
    public static MeshAsset Placards(Look? look, CarShape shape, CargoKind cargo)
    {
        var k = new Kit(look, 71 + (int)cargo);
        if (!Placarded(cargo))
            return k.Build("cargo-placard-none");
        float w = (float)shape.HalfWidth, z = PlacardZ(shape);
        float bay = (2 * (float)shape.HalfLength - 0.12f) / 8;
        var (ground, emblem) = Colours(cargo);
        var (word, warning) = Stencil(cargo);
        var font = BitmapFont.Default;
        float wordY = PlacardY - Diamond - 0.18f, warnY = wordY - font.LineHeight * WordPixel * 0.85f;
        float top = PlacardY + Diamond + 0.1f, bottom = warnY - font.Height * WarnPixel / 2 - 0.1f;
        foreach (int side in new[] { -1, 1 })
        {
            var n = new Vector3(side, 0, 0);
            var up = Vector3.UnitY;
            var right = Vector3.Normalize(Vector3.Cross(up, n));
            // The board, standing off the plate on the posts' faces, the bay's width less the posts.
            float face = w + 0.06f;
            k.Use("wood_grey", new Vector3(0.16f, 0.14f, 0.12f), 0.85f, 0.05f, tile: 1.2f);
            float x0 = MathF.Min(side * (w + 0.035f), side * face), x1 = MathF.Max(side * (w + 0.035f), side * face);
            k.Box(new Vector3(x0, bottom, z - bay / 2 + 0.08f), new Vector3(x1, top, z + bay / 2 - 0.08f),
                Kit.Faces.All & ~(side > 0 ? Kit.Faces.NegX : Kit.Faces.PosX));
            var centre = new Vector3(side * (face + 0.002f), PlacardY, z);
            var diamondUp = Vector3.Normalize(up + right);
            // The diamond's iron rim, the enamel on it, the emblem on that, each a few millimetres proud of the last.
            k.Use("enamel", Palette.IronGrey, 0.6f, 0.25f);
            k.Tint = Palette.IronGrey * 0.7f;
            k.Panel(centre, n, diamondUp, Diamond * 1.5f, Diamond * 1.5f);
            k.Use("enamel", ground, 0.35f, 0.15f);
            k.Tint = ground;
            k.Panel(centre + n * 0.004f, n, diamondUp, Diamond * 1.3f, Diamond * 1.3f);
            k.Use("enamel", emblem, 0.3f, 0.1f);
            k.Tint = emblem;
            k.Emissive = cargo == CargoKind.Comet ? 0.8f : 0;
            Emblem(k, cargo, centre + n * 0.008f, n, up, right, EmblemScale);
            k.Emissive = 0;
            // The stencil: the yard's paint straight on the board, pale, as the car's number is.
            var paint = new Vector3(0.7f, 0.67f, 0.6f);
            k.Use("enamel", paint, 0.7f, 0.05f);
            k.Tint = paint;
            Letter(k, font, word, new Vector3(centre.X, wordY, z), n, up, right, WordPixel);
            Letter(k, font, warning, new Vector3(centre.X, warnY, z), n, up, right, WarnPixel);
        }
        // On the roof too, for a crew walking the train's roofs: the cargo's name painted along the car either side of the
        // walk, in its placard's colour (the comet's lit), its foot toward the walk. Ahead of the hatch, behind the number.
        var roofPaint = cargo == CargoKind.Comet ? emblem : ground;
        k.Use("enamel", roofPaint, 0.6f, 0.05f);
        k.Tint = roofPaint;
        k.Emissive = cargo == CargoKind.Comet ? 0.6f : 0;
        float h = (float)shape.RoofHeight;
        // Laid on the roof's arch (TrainKit.Car's profile: down 0.035 m at the eaves, by the square of the way out).
        Vector3 OnRoof(Vector3 p) => p with { Y = h - 0.005f - 0.035f * MathF.Pow(MathF.Min(1, MathF.Abs(p.X) / (w + 0.02f)), 2) + 0.01f };
        foreach (int side in new[] { -1, 1 })
        {
            var across = new Vector3(side, 0, 0);
            Letter(k, font, word, new Vector3(side * RoofWordX, h, RoofWordZ(shape)), Vector3.UnitY, across,
                Vector3.Normalize(Vector3.Cross(across, Vector3.UnitY)), RoofPixel, OnRoof);
        }
        k.Emissive = 0;
        return k.Build($"cargo-placard-{Cargoes.Key(cargo)}");
    }

    /// <summary>Each cargo's mark in the diamond, about its middle, on the face whose normal is <paramref name="n"/>, <paramref name="s"/> times its size.</summary>
    static void Emblem(Kit k, CargoKind cargo, Vector3 c, Vector3 n, Vector3 up, Vector3 right, float s)
    {
        switch (cargo)
        {
            case CargoKind.Ammunition:
                // A bursting ball: the ball, and eight rays out of it.
                k.Disc(c, n, 0.1f * s, 10);
                for (int i = 0; i < 8; i++)
                {
                    float a = i * MathF.Tau / 8 + 0.2f;
                    var dir = up * MathF.Cos(a) + right * MathF.Sin(a);
                    k.Panel(c + dir * 0.19f * s, n, dir, 0.035f * s, 0.11f * s);
                }
                break;
            case CargoKind.Chemicals:
                // A flask: its round belly, its neck, its lip.
                k.Disc(c - up * 0.05f * s, n, 0.12f * s, 12);
                k.Panel(c + up * 0.11f * s, n, up, 0.06f * s, 0.14f * s);
                k.Panel(c + up * 0.19f * s, n, up, 0.1f * s, 0.03f * s);
                break;
            case CargoKind.Medicine:
                k.Panel(c, n, up, 0.09f * s, 0.3f * s);
                k.Panel(c, n, up, 0.3f * s, 0.09f * s);
                break;
            default:
                // The comet: a ring and the tail streaming off it down and back.
                var head = c + (up + right) * 0.06f * s;
                k.Disc(head, n, 0.08f * s, 10);
                var tail = Vector3.Normalize(up + right);
                var across = Vector3.Normalize(right - up);
                foreach (var (off, len) in new[] { (-0.04f, 0.26f), (0f, 0.3f), (0.04f, 0.22f) })
                    k.Panel(head - tail * (0.08f + len / 2) * s + across * off * s, n, tail, 0.022f * s, len * s);
                break;
        }
    }

    /// <summary>A line of the HUD's pixel font painted on the face, centred at <paramref name="centre"/>, each row's run of pixels one quad.</summary>
    static void Letter(Kit k, BitmapFont font, string line, Vector3 centre, Vector3 n, Vector3 up, Vector3 right, float px,
        Func<Vector3, Vector3>? onto = null)
    {
        float x = -font.Measure(line) * px / 2, y0 = font.Height * px / 2;
        foreach (char ch in line)
        {
            var g = font.Glyph(ch);
            for (int gy = 0; gy < g.GetLength(0); gy++)
                for (int gx = 0; gx < g.GetLength(1); gx++)
                {
                    if (!g[gy, gx] || gx > 0 && g[gy, gx - 1])
                        continue;
                    int run = 1;
                    while (gx + run < g.GetLength(1) && g[gy, gx + run])
                        run++;
                    float xm = x + (gx + run / 2f) * px, ym = y0 - (gy + 0.5f) * px;
                    var at = centre + right * xm + up * ym;
                    k.Panel(onto?.Invoke(at) ?? at, n, up, run * px, px);
                }
            x += font.Advance * px;
        }
    }

    /// <summary>The stain's colour for what spoiled: the livestock's muck, the chemicals' sick yellow, the comet's green, else wet rot.</summary>
    public static Vector3 StainOf(CargoKind cargo) => cargo switch
    {
        CargoKind.Livestock => new Vector3(0.16f, 0.11f, 0.06f),
        CargoKind.Chemicals => new Vector3(0.3f, 0.32f, 0.08f),
        CargoKind.Comet => new Vector3(0.12f, 0.3f, 0.14f),
        CargoKind.Food or CargoKind.Grain => new Vector3(0.14f, 0.12f, 0.08f),
        _ => new Vector3(0.09f, 0.08f, 0.07f),
    };

    /// <summary>
    /// What spoiled in it run out through its floor and down its side: a dark tide-mark along the floor's seam either side of
    /// each side door (the door's own landing hides what's under it) and streaks from it down the boards to the frame, the
    /// same on every car of the same <paramref name="seed"/>. The comet's glows faintly.
    /// </summary>
    public static MeshAsset Stain(Look? look, CarShape shape, CargoKind cargo, int seed)
    {
        var k = new Kit(look, 90 + seed % 17);
        var colour = StainOf(cargo);
        k.Use("enamel", colour, 0.9f, 0.35f);
        k.Tint = colour;
        k.Emissive = cargo == CargoKind.Comet ? 0.35f : 0;
        float w = (float)shape.HalfWidth;
        float floor = (float)(shape.Interior?.Min.Y ?? 1.1);
        int streak = 0;
        foreach (var door in shape.DoorList)
        {
            if (door.Box.Max.Z - door.Box.Min.Z <= door.Box.Max.X - door.Box.Min.X)
                continue;
            int side = door.Box.Min.X < 0 ? -1 : 1;
            var n = new Vector3(side, 0, 0);
            float x = side * (w + 0.025f);
            // Either side of the door: ahead of it, and behind it past where its leaf stands cracked.
            foreach (var (z0, z1) in new[] { ((float)door.Box.Min.Z - StainRun, (float)door.Box.Min.Z - 0.1f),
                ((float)door.Box.Max.Z + 0.45f, (float)door.Box.Max.Z + 0.45f + StainRun) })
            {
                // The tide-mark along the floor's seam.
                k.Panel(new Vector3(x, floor + 0.06f, (z0 + z1) / 2), n, Vector3.UnitY, z1 - z0, 0.12f);
                // The streaks: each its own width and length, hashed, longest nearest the door.
                const int count = 6;
                for (int i = 0; i < count; i++, streak++)
                {
                    float h = Hash(seed * 31 + streak), h2 = Hash(seed * 17 + streak * 7 + 3);
                    float z = z0 + (z1 - z0) * (i + 0.2f + 0.6f * h) / count;
                    float near = 1 - MathF.Min(MathF.Abs(z - (float)door.Box.Min.Z), MathF.Abs(z - (float)door.Box.Max.Z)) / (StainRun + 0.5f);
                    float len = 0.2f + (floor - 0.3f) * (0.35f + 0.65f * near) * (0.5f + 0.5f * h2);
                    float wide = 0.04f + 0.08f * h;
                    k.Panel(new Vector3(x + side * 0.002f, floor + 0.02f - len / 2, z), n, Vector3.UnitY, wide, len);
                }
            }
        }
        k.Emissive = 0;
        return k.Build($"cargo-stain-{Cargoes.Key(cargo)}-{seed}");
    }

    // How far along the side either side of a door the stain runs (m).
    const float StainRun = 1.3f;

    static float Hash(int i)
    {
        uint x = (uint)i * 2654435761u;
        x ^= x >> 15;
        x *= 0x2c1b3c6d;
        x ^= x >> 12;
        return (x & 0xffff) / 65535f;
    }
}
