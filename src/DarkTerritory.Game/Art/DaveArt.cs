using System.Numerics;
using Ballast.Assets;
using Ballast.Render;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Game.Art;

/// <summary>
/// Dave modelled (ARCHITECTURE §8 note 491; the director, 8 Oct: "a unique model so he's recognizable from afar ... a bit
/// tubby on the belly and has glasses ... wears Birks sandals ... loves vests and cool hats"), dressed as
/// <see cref="DaveKit"/> has him after the director's photographs: his own figure on the crew's rig and clips
/// (tools/models/recipes/dave.py: a black tee over the belly, his arms bare, light washed jeans, cork-soled sandals; his
/// grey hair, black rectangular glasses, the hoop in his left ear and his silver chain), his six waistcoats its variants;
/// one of his six hats on his head and, at his canvas, his brush in his right fist and his palette under his left hand
/// (tools/models/recipes/dave_kit.py), the brush's tip on the canvas where crew_clips' <c>paint</c> puts it. The hats and
/// the waistcoats are in DaveKit's order, so the night's outfit (<see cref="DaveKit.Outfit"/>) is the same whichever
/// figure draws him; DaveKit's dressed figure stands in where this one isn't built.
/// </summary>
public static class DaveArt
{
    /// <summary>The figure (content/art/models/dave.glb, DaveKit's name for him), and its clip at his canvas.</summary>
    public const string Figure = DaveKit.Figure, Painting = "paint";

    /// <summary>His hats (props dave_hat_0..5) and his waistcoats (the figure's variants 0..5): DaveKit's.</summary>
    public const int Hats = DaveKit.Hats, Vests = DaveKit.Vests;

    /// <summary>
    /// The canvas's painted face, ahead of his feet in his frame (he faces −Z), where <c>paint</c>'s brush reaches: its
    /// middle across, its foot and its top (m). crew_clips.py's CANVAS_Y; DaveKit's easel's picture is its width and height.
    /// </summary>
    public const float CanvasOut = 0.737f, CanvasHalfWidth = 0.42f, CanvasFoot = 1.0f, CanvasTop = 1.62f;

    /// <summary>The brush's tip in the bind pose (on the right fist's grip, along +Y in Blender: −Z here), and the palette's
    /// painted face's middle (on the left hand, under its palm-down bind): crew_clips' BRUSH_TIP and PALETTE_FACE.</summary>
    public static readonly Vector3 BrushTip = new(0.79f, 1.43f, -0.235f), PaletteMiddle = new(-0.85f, 1.414f, 0);

    /// <summary>Whether his modelled figure's built (else DaveKit's dressed figure draws him).</summary>
    public static bool Built(CreatureArt creatures) => creatures.Modelled(Figure);

    /// <summary>
    /// What he's doing in <paramref name="phase"/>: at his canvas he paints, brush and palette in hand; turned on someone
    /// he's set them down (DaveKit's clips: still, then both hands out).
    /// </summary>
    public static (string Clip, bool Painting) Act(SpinePhase phase) => phase switch
    {
        SpinePhase.Telegraph or SpinePhase.Commit or SpinePhase.Grab or SpinePhase.Punish => (DaveKit.Clip(phase), false),
        _ => (Painting, true),
    };

    /// <summary>
    /// Where he stands to paint, from <paramref name="feet"/> (his frame, facing the easel DaveKit stands there): a step's
    /// fraction nearer, so the brush that <c>paint</c> reaches out <see cref="CanvasOut"/> touches DaveKit's picture.
    /// </summary>
    public static Matrix4x4 AtCanvas(in Matrix4x4 feet) =>
        Matrix4x4.CreateTranslation(0, 0, CanvasOut - DaveKit.Picture) * feet;

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
