using System.Numerics;
using Ballast.Assets;
using Ballast.Render;

namespace DarkTerritory.Game.Art;

/// <summary>
/// Dave modelled (ARCHITECTURE §8 note 491; the director, 8 Oct: "a unique model so he's recognizable from afar ... a bit
/// tubby on the belly and has glasses ... wears Birks sandals ... loves vests and cool hats"): his own figure on the crew's
/// rig and clips (tools/models/recipes/dave.py: a linen shirt over the belly, the sleeves rolled, round wire spectacles,
/// cork-soled sandals), his five waistcoats its variants; one of his five hats on his head and, at his canvas, his brush
/// in his right fist and his palette under his left hand (tools/models/recipes/dave_kit.py), the brush's tip on the canvas
/// where crew_clips' <c>paint</c> puts it. The hats and the waistcoats are in DaveKit's order (Hats, Vests), so the night's
/// outfit is the same whichever figure draws him; DaveKit's code-built figure stands in where this one isn't built.
/// </summary>
public static class DaveArt
{
    /// <summary>The figure (content/art/models/dave.glb), and its clip at his canvas.</summary>
    public const string Figure = "dave", Painting = "paint";

    /// <summary>His hats (props dave_hat_0..4) and his waistcoats (the figure's variants 0..4).</summary>
    public const int Hats = 5, Vests = 5;

    /// <summary>
    /// The canvas's painted face, ahead of his feet in his frame (he faces −Z), where <c>paint</c>'s brush reaches: its
    /// middle across, its foot and its top (m). crew_clips.py's CANVAS_Y; the easel stands it there.
    /// </summary>
    public const float CanvasOut = 0.737f, CanvasHalfWidth = 0.42f, CanvasFoot = 1.0f, CanvasTop = 1.62f;

    /// <summary>The brush's tip in the bind pose (on the right fist's grip, along +Y in Blender: −Z here), and the palette's
    /// painted face's middle (on the left hand, under its palm-down bind): crew_clips' BRUSH_TIP and PALETTE_FACE.</summary>
    public static readonly Vector3 BrushTip = new(0.79f, 1.43f, -0.235f), PaletteMiddle = new(-0.85f, 1.414f, 0);

    /// <summary>Whether the modelled figure's built (else DaveKit's code figure draws him).</summary>
    public static bool Built(CreatureArt creatures) => creatures.Get(Figure) is not null;

    /// <summary>
    /// Draws him at <paramref name="at"/> (his feet, facing its −Z) in <paramref name="clip"/> at <paramref name="time"/>, in
    /// hat <paramref name="hat"/> (−1: bare-headed) and waistcoat <paramref name="vest"/>; <paramref name="painting"/> puts
    /// his brush and palette in his hands (he sets them down to turn on someone). False when the figure or the clip isn't
    /// built.
    /// </summary>
    public static bool Draw(Look look, MeshBuilder mesh, in Matrix4x4 at, string clip, double time, int hat, int vest, bool painting,
        Func<ModelMaterial, MaterialLook, MaterialLook>? adjust = null)
    {
        var creatures = look.Art.Creatures;
        if (!creatures.Draw(mesh, Figure, clip, time, true, at, Math.Abs(vest) % Vests, seed: 47, adjust: adjust))
            return false;
        var props = PropArt.Of(look);
        if (hat >= 0 && props.Get("dave_hat_" + hat % Hats) is { } worn)
            creatures.Wear(mesh, worn, "head", at, Figure);
        if (painting)
        {
            if (props.Get("dave_brush") is { } brush)
                creatures.Wear(mesh, brush, "hand_r", at, Figure);
            if (props.Get("dave_palette") is { } palette)
                creatures.Wear(mesh, palette, "hand_l", at, Figure);
        }
        return true;
    }

    /// <summary>Where the brush's tip is on the figure last drawn (world), and the palette's middle.</summary>
    public static (Vector3 Brush, Vector3 Palette) Hands(CreatureArt creatures, in Matrix4x4 at) =>
        (creatures.Posed("hand_r", BrushTip, at, Figure), creatures.Posed("hand_l", PaletteMiddle, at, Figure));
}
