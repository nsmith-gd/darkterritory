using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Game.Art;

/// <summary>
/// The Hanger (GDD §21 OUTSIDE; the director, 9 Oct 2026; note 586; tools/blender/hanger.py): the pale bloated thing in a
/// house's roof space. GreyboxScene draws it in its house (GreyboxScene.Houses: under the ceiling, its strands); this is
/// its clips by its mode, and its body drawn where the sim has it for a draw that has nothing else (its death).
/// </summary>
public sealed partial class CreatureArt
{
    HangerTuning? _hanger;

    /// <summary>The Hanger's tuning, as the content has it (how many strands it hangs, how high it hauls).</summary>
    public HangerTuning HangerTuning => _hanger ??= DataFile.Load<EnemyTuning>(Path.Combine(ContentRoot, EnemyTuning.File)).Hanger;

    /// <summary>
    /// How far under what it hangs from its body is (m): the model's origin is the underside of the beams it grips
    /// (tools/blender/hanger.py), the sim's <see cref="Enemy.Local"/> its body.
    /// </summary>
    public const float HangerBody = 0.3f;

    /// <summary>Where its mouth is in its own frame (tools/blender/hanger.py MOUTH): under its head, its strands come out of it.</summary>
    public static readonly Vector3 HangerMouth = new(0, -0.37f, -0.3f);

    /// <summary>Its clips' lengths (tools/blender/hanger.py): the snap of a strand taken, and the recoil from a blow.</summary>
    public const double HangerGrabSeconds = 0.5, HangerHitSeconds = 0.4;

    /// <summary>
    /// The clip the Hanger plays (tools/blender/hanger.py), how far into it, and whether it loops, by its mode (the sim's) and
    /// how long it's been in it: hidden, folded up still; grabbing, the snap, then holding the line; hauling hand over hand;
    /// holding; hit, the recoil, then holding; retreating, scuttling off along the beams.
    /// </summary>
    public static (string Clip, double Time, bool Loop) HangerClip(HangerMode mode, double modeSeconds) => mode switch
    {
        HangerMode.Grab when modeSeconds < HangerGrabSeconds => ("grab", modeSeconds, false),
        HangerMode.Hit when modeSeconds < HangerHitSeconds => ("hit", modeSeconds, false),
        HangerMode.Grab or HangerMode.Hold or HangerMode.Hit => ("hold", modeSeconds, true),
        HangerMode.Haul => ("haul", modeSeconds, true),
        HangerMode.Retreat => ("retreat", modeSeconds, true),
        _ => ("hidden", modeSeconds, true),
    };

    /// <summary>The Hanger at <paramref name="model"/> (its origin where it grips, facing −Z), by its mode; false without its model.</summary>
    public bool Hanger(MeshBuilder mesh, in Matrix4x4 model, HangerMode mode, double modeSeconds, int id)
    {
        // (Folded up still, each its own breath.)
        var (clip, at, loop) = HangerClip(mode, modeSeconds + (mode == HangerMode.Hidden ? id * 0.73 : 0));
        return Draw(mesh, "hanger", clip, at, loop, model, seed: 586 + id);
    }

    /// <summary>
    /// The house creatures' draw where there's nothing but the sim's enemy to go by (a death, the greybox's, one going): the
    /// Hanger hung over where its body is (going, scuttling back up into its roof, note 645); the Hollow House is its house
    /// (GreyboxScene.Houses draws it), so nothing. Null for any other kind.
    /// </summary>
    bool? Houses(MeshBuilder mesh, in Matrix4x4 model, EnemyKind kind, SpinePhase phase, double t)
    {
        switch (kind)
        {
            case EnemyKind.Hanger when _models.ContainsKey("hanger"):
                return Draw(mesh, "hanger", phase == SpinePhase.BreakOff ? "retreat" : "hold", t, true, Matrix4x4.CreateTranslation(0, HangerBody, 0) * model, seed: 586);
            case EnemyKind.HollowHouse:
                return true;
        }
        return null;
    }
}
