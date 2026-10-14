using System.Numerics;
using Ballast.Render;
using DarkTerritory.Sim.LineGen;

namespace DarkTerritory.Game.Art;

/// <summary>
/// The lineside signage kit (linegen plan §9.5, §18 "Signage"): km posts, speed and restricted boards, gradient posts,
/// whistle boards, junction and facility boards, bridge plates and limits, line-closed, yard limit, station names, tunnel
/// plates. Note 560 (the director, 9 Oct: "they need to be massive like billboards ... and be illuminated by the lamp for
/// a long while"): a board is as big as its words want (<see cref="BoardLayout"/>, signage.json), lettered in the HUD's
/// own pixel font, and its face and letters are reflective enamel: emissive paint, so it shines as bright as the lamp
/// makes it (the instance's glow, <see cref="Billboards"/>) and comes through the fog as a light does. Its posts are
/// their own piece (<see cref="Post"/>), stood to the land under each board. In the piece's frame: the face toward +Z (the
/// oncoming train), across X, its bottom edge's middle at the origin.
/// </summary>
public static class SignKit
{
    /// <summary>A board's face of <paramref name="type"/>, lettered as <paramref name="face"/> lays it out; <paramref name="emissive"/> its paint's.</summary>
    /// <param name="plain">How much of the reflective paint's shine the plain paint has (a red or green ground keeps its colour).</param>
    public static MeshAsset Board(Look? look, string type, BoardFace face, float emissive, float plain)
    {
        var k = new Kit(look, 1300 + face.Lines.Sum(l => l.Length));
        var font = BitmapFont.Default;
        float w = (float)face.Width, h = (float)face.Height, px = (float)face.Pixel;
        bool plate = type is "tunnelPlate";
        var (paint, letter) = Colours(type);
        // The paler of the two is the reflective paint (white figures, or a white ground); the dark one is plain paint, and
        // shines a little (plain): so the words stand out of the face however bright the lamp makes them.
        static float Luma(Vector3 c) => c.X * 0.3f + c.Y * 0.59f + c.Z * 0.11f;
        bool paleGround = Luma(paint) > Luma(letter);
        // A frame of iron round an enamelled plate, a backing of planks behind it (a plate: cast, into the stone).
        float depth = plate ? 0.25f : 0.12f, rim = MathF.Max(0.06f, px * 0.6f);
        k.Use("iron_plate", Palette.IronGrey, 0.6f, 0.25f, tile: 1);
        k.Box(new Vector3(-w / 2 - rim, -rim, -depth), new Vector3(w / 2 + rim, h + rim, -0.06f));
        if (!plate)
        {
            // The board's back bracing (two rails across), so it reads as built from behind and the side.
            k.Use("wood_grey", Palette.IronGrey, 0.7f, 0.05f, tile: 2);
            foreach (float y in (ReadOnlySpan<float>)[h * 0.25f, h * 0.75f])
                k.Box(new Vector3(-w / 2, y - 0.12f, -depth - 0.18f), new Vector3(w / 2, y + 0.12f, -depth));
        }
        // The enamel: flat paint (no texture: the lettering is what has to read), a little worn.
        k.Use("enamel", paint, 0.2f, 0.15f);
        k.Tint = paint;
        k.Emissive = paleGround ? emissive : emissive * plain;
        k.Quad(new Vector3(-w / 2, h, 0.01f), new Vector3(w / 2, h, 0.01f), new Vector3(w / 2, 0, 0.01f), new Vector3(-w / 2, 0, 0.01f));
        // The lettering, painted proud of the enamel a little (enough not to fight it in the depth buffer from 300 m), each
        // row's run of pixels one quad.
        k.Use("enamel", letter, 0.15f, 0.1f);
        k.Tint = letter;
        k.Emissive = paleGround ? emissive * plain : emissive;
        float z = 0.01f + MathF.Max(0.02f, px * 0.12f);
        float y0 = h - (float)face.Margin;
        foreach (var line in face.Lines)
        {
            float x = -font.Measure(line) * px / 2;
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
                        float xa = x + gx * px, xb = xa + run * px, ya = y0 - gy * px, yb = ya - px;
                        k.Quad(new Vector3(xa, ya, z), new Vector3(xb, ya, z), new Vector3(xb, yb, z), new Vector3(xa, yb, z));
                    }
                x += font.Advance * px;
            }
            y0 -= font.LineHeight * px;
        }
        return k.Build($"board-{type}-{string.Join('|', face.Lines)}-{px:0.000}");
    }

    /// <summary>A board's paint and its lettering's, as the line's boards have always been coloured.</summary>
    public static (Vector3 Paint, Vector3 Letter) Colours(string type) => type switch
    {
        // Speed boards: black figures on white, as ever; restricted: yellow; closed and limits: red.
        "restricted" or "endRestricted" => (new Vector3(0.56f, 0.44f, 0.11f), Palette.SootBlack),
        "lineClosed" or "bridgeLimit" or "limitReason" => (new Vector3(0.55f, 0.1f, 0.07f), Reflective),
        // The mail crane's warning board (sight.json drop): the mail's green, white letters.
        "mailDrop" => (new Vector3(0.12f, 0.3f, 0.16f), Reflective),
        // Names: dark enamel, cream letters, as the station boards are. A tunnel's plate is cast iron on the portal's
        // stone, cream letters (note 295): pale paint was lost in the stone.
        "stationName" or "facility" or "junction" or "bridge" or "tunnelPlate" => (new Vector3(0.07f, 0.09f, 0.12f), Reflective),
        _ => (Reflective, Palette.SootBlack),
    };

    /// <summary>
    /// The reflective paint's colour: a cream that's mid-grey as paint goes, since it shines back twice its colour times the
    /// lamp's glow (scene.frag's emissive); whiter, a lit board's words drowned in their own bloom close to.
    /// </summary>
    static readonly Vector3 Reflective = new(0.56f, 0.54f, 0.47f);

    /// <summary>A board's post: a stout square timber 1 m tall (scaled to each), foot at the origin.</summary>
    public static MeshAsset Post(Look? look)
    {
        var k = new Kit(look, 1290);
        k.Use("paint_black", Palette.SootBlack, 0.7f, 0.1f, tile: 1);
        k.Box(new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 1, 0.5f), Kit.Faces.All & ~Kit.Faces.NegY);
        return k.Build("sign-post");
    }

    /// <summary>A dead signal: a lattice mast, its arm dropped, its lamp long out.</summary>
    public static MeshAsset DeadSignal(Look? look) => WorldKit.Signal(look, false);
}
