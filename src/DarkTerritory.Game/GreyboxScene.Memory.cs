using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>
/// Crew memory in the scene (GDD §9, §11; the director, 14 Oct 2026; ARCHITECTURE §8 note 721): the crew photo's props (the
/// crate with the dead's caps, the slate on its easel with the night's tally chalked on it, the photographer's flash) while
/// the photo's drawn, and the logbook on its shelf in car one.
/// </summary>
public sealed partial class GreyboxScene
{
    /// <summary>The crew photo's props and flash (note 721), while it's drawn; null otherwise.</summary>
    public PhotoProps? Photo { get; set; }

    /// <summary>The logbook on its shelf (note 721, <see cref="LogbookReader.Shelf"/>): its car, where in it, which way it faces. Null: not drawn.</summary>
    public (int Car, Double3 Local, double Yaw)? Logbook { get; set; }

    MeshAsset? _crate, _cap, _easel, _book;
    (string Key, MeshAsset Mesh)? _chalk;

    void Memory(MeshBuilder mesh, IReadOnlyList<CarFrame> frames, Double3 eye)
    {
        if (Logbook is { } book && book.Car >= 0 && book.Car < frames.Count && (frames[book.Car].Origin - eye).Length < DrawDistance)
        {
            _book ??= Book(Look);
            mesh.Append(_book, Matrix4x4.CreateRotationY((float)book.Yaw) * Matrix4x4.CreateTranslation(book.Local.RelativeTo(default)) * SceneArt.FrameMatrix(frames[book.Car], eye));
        }
        if (Photo is not { } p)
            return;
        // The flash: the photo is lit by it, the night round it as dark as ever.
        mesh.PointLights.Add(new PointLight(V(p.Flash, eye), new Vector3(1, 0.97f, 0.9f) * p.FlashStrength, p.FlashRange));
        _crate ??= Crate(Look);
        var crate = Matrix4x4.CreateRotationY((float)p.CrateYaw) * Matrix4x4.CreateTranslation(V(p.Crate, eye));
        mesh.Append(_crate, crate);
        // The dead's caps on its lid, in a row of three a side to side, the next row behind; each in its crewmate's colours.
        _cap ??= Cap(Look);
        for (int i = 0; i < p.Caps.Count; i++)
        {
            int row = i / 3, inRow = Math.Min(3, p.Caps.Count - row * 3);
            float x = ((i % 3) - (inRow - 1) / 2f) * 0.29f, z = -0.13f + row * 0.26f;
            var tint = Look?.Tuning.CrewColour(p.Caps[i]) ?? Vector3.One;
            mesh.Append(_cap, Matrix4x4.CreateScale(CapScale) * Matrix4x4.CreateRotationY(0.4f * ((i * 7 % 5) - 2) / 2) * Matrix4x4.CreateTranslation(x, CrateHeight + 0.002f, z) * crate, tint);
        }
        _easel ??= Easel(Look);
        var easel = Matrix4x4.CreateRotationY((float)p.SlateYaw) * Matrix4x4.CreateTranslation(V(p.Slate, eye));
        mesh.Append(_easel, easel);
        string key = string.Join("\n", p.Chalk);
        if (_chalk is not { } chalk || chalk.Key != key)
            _chalk = chalk = (key, Chalk(Look, p.Chalk));
        mesh.Append(chalk.Mesh, easel);
    }

    const float CrateHeight = 0.62f;
    /// <summary>The caps a little larger than life: a photo's small, and they're what the crate's for.</summary>
    const float CapScale = 1.3f;
    static readonly Vector3 SlateCentre = new(0, 1.12f, -0.03f);
    const float SlateW = 0.86f, SlateH = 0.62f;

    /// <summary>A packing crate, planked: what the dead's caps are set on.</summary>
    static MeshAsset Crate(Look? look)
    {
        var k = new Kit(look, 4410);
        SquareKit.Timber(k);
        k.Tint *= 1.25f;
        k.Box(new Vector3(-0.46f, 0, -0.3f), new Vector3(0.46f, CrateHeight, 0.3f));
        // The battens across its face and the dark seams between its planks.
        k.Tint *= 0.7f;
        foreach (float y in new[] { 0.1f, 0.5f })
            k.Box(new Vector3(-0.47f, y, -0.315f), new Vector3(0.47f, y + 0.07f, -0.3f));
        return k.Build("photo-crate");
    }

    /// <summary>A flat cap with its peak, as the crew wear them, small enough to sit three on a crate (its peak to −Z).</summary>
    static MeshAsset Cap(Look? look)
    {
        var k = new Kit(look, 4420);
        // White, the crewmate's colour multiplied in as it's drawn.
        k.Use("cap", new Vector3(0.75f), 0.6f, 0.05f, tile: 1);
        k.Tint = new Vector3(0.8f);
        k.Cylinder(new Vector3(0, 0, 0.01f), new Vector3(0, 0.07f, 0.02f), 0.105f, 14, radiusB: 0.095f);
        k.Quad(new Vector3(-0.085f, 0.012f, -0.06f), new Vector3(0.085f, 0.012f, -0.06f), new Vector3(0.07f, 0.004f, -0.16f), new Vector3(-0.07f, 0.004f, -0.16f), twoSided: true);
        return k.Build("photo-cap");
    }

    /// <summary>The photographer's easel and its slate, framed in timber, a chalk ledge under it; the slate faces −Z.</summary>
    static MeshAsset Easel(Look? look)
    {
        var k = new Kit(look, 4430);
        SquareKit.Timber(k);
        k.Tint *= 1.2f;
        foreach (float side in new[] { -1f, 1f })
            k.Cylinder(new Vector3(side * 0.36f, 0, -0.06f), new Vector3(side * 0.24f, 1.62f, 0.08f), 0.022f, 6);
        k.Cylinder(new Vector3(0, 0, 0.58f), new Vector3(0, 1.55f, 0.04f), 0.022f, 6);
        var c = SlateCentre;
        float hw = SlateW / 2, hh = SlateH / 2;
        k.Box(new Vector3(-hw - 0.05f, c.Y - hh - 0.06f, c.Z), new Vector3(hw + 0.05f, c.Y - hh, c.Z + 0.03f));
        k.Box(new Vector3(-hw - 0.05f, c.Y + hh, c.Z), new Vector3(hw + 0.05f, c.Y + hh + 0.05f, c.Z + 0.03f));
        foreach (float side in new[] { -1f, 1f })
            k.Box(new Vector3(side * hw - (side > 0 ? 0 : 0.05f), c.Y - hh, c.Z), new Vector3(side * hw + (side > 0 ? 0.05f : 0), c.Y + hh, c.Z + 0.03f));
        // The chalk ledge, out in front at the slate's foot.
        k.Box(new Vector3(-hw, c.Y - hh - 0.08f, c.Z - 0.07f), new Vector3(hw, c.Y - hh - 0.05f, c.Z));
        // The slate: blue-black, a little sheen.
        k.Use("slate_board", new Vector3(0.05f, 0.055f, 0.06f), 0.4f, 0.15f, tile: 1);
        k.Tint = new Vector3(0.05f, 0.055f, 0.06f);
        k.Panel(c, -Vector3.UnitZ, Vector3.UnitY, SlateW, SlateH);
        return k.Build("photo-easel");
    }

    /// <summary>The night's tally chalked on the slate (note 721), each line as large as fits it, centred.</summary>
    static MeshAsset Chalk(Look? look, IReadOnlyList<string> lines)
    {
        var k = new Kit(look, 4440);
        k.Use("chalk", new Vector3(0.92f, 0.92f, 0.88f), 0.9f, 0, tile: 1);
        k.Tint = new Vector3(0.92f, 0.92f, 0.88f);
        // A little glow of its own: chalk on slate reads in any light the flash leaves.
        k.Emissive = 0.25f;
        var font = BitmapFont.Default;
        float inner = SlateW - 0.08f, room = SlateH - 0.08f;
        int rows = Math.Max(1, lines.Count);
        float widest = Math.Max(1, lines.Max(l => font.Measure(l)));
        float px = MathF.Min(inner / widest, room / (rows * font.LineHeight));
        float top = SlateCentre.Y + rows * font.LineHeight * px / 2;
        for (int i = 0; i < lines.Count; i++)
        {
            float x = font.Measure(lines[i]) * px / 2;
            k.With(Matrix4x4.CreateTranslation(0, 0, SlateCentre.Z), () => SquareKit.Ink(k, font, lines[i], x, top - i * font.LineHeight * px, px));
        }
        k.Emissive = 0;
        return k.Build("photo-chalk");
    }

    /// <summary>
    /// The logbook (note 721): a fat ledger bound in oxblood cloth, its pages' edge showing, lying shut on its little shelf,
    /// the shelf's back to the wall (+Z) and its front to the aisle (−Z), two iron brackets under it.
    /// </summary>
    static MeshAsset Book(Look? look)
    {
        var k = new Kit(look, 4450);
        SquareKit.Timber(k);
        k.Tint *= 1.2f;
        float back = (float)LogbookReader.ShelfDepth;
        k.Box(new Vector3(-0.26f, -0.035f, -0.14f), new Vector3(0.26f, 0, back));
        k.Use("rust_heavy", Palette.IronGrey, 0.8f, 0.3f);
        foreach (float x in new[] { -0.19f, 0.19f })
            k.Box(new Vector3(x - 0.012f, -0.2f, back - 0.25f), new Vector3(x + 0.012f, -0.035f, back));
        k.Use("leather", new Vector3(0.36f, 0.1f, 0.08f), 0.7f, 0.1f, tile: 1);
        k.Tint = new Vector3(0.36f, 0.1f, 0.08f);
        k.Box(new Vector3(-0.17f, 0, -0.12f), new Vector3(0.17f, 0.012f, 0.12f));
        k.Box(new Vector3(-0.17f, 0.058f, -0.12f), new Vector3(0.17f, 0.07f, 0.12f));
        k.Box(new Vector3(-0.17f, 0.012f, 0.105f), new Vector3(0.17f, 0.058f, 0.12f));
        k.Use("paper_form", new Vector3(0.85f, 0.8f, 0.68f), 0.9f, 0, tile: 1);
        k.Tint = new Vector3(0.85f, 0.8f, 0.68f);
        k.Box(new Vector3(-0.162f, 0.012f, -0.112f), new Vector3(0.162f, 0.058f, 0.105f));
        return k.Build("logbook");
    }
}
