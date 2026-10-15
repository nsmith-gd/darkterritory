using System.Numerics;
using Ballast;
using Ballast.Assets;
using Ballast.Render;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Game.Art;

public sealed partial class CreatureArt
{
    // How dark a watcher's shape is (its looks scaled by this: a shape against the fog, no more), the shade it's being drawn
    // in now, and how far it backs off as its sign ends, over how long. The art's, not design numbers.
    public const float WatcherShade = 0.1f;
    const float WatcherBackM = 1.6f, WatcherBackSeconds = 1.4f;
    float _shade = 1;
    AfootTuning? _afoot;

    /// <summary>
    /// How a watcher of each kind is drawn (note 761): its model, the clip it's held in, the bone its eyes are on, half the gap
    /// between them, how far in front of that bone and over it they are, and its scale as the game draws it. No eye bone: it has no eyes
    /// (the Whistler, GDD §21: "no eyes and no face"). No model: just the eyes, where the sim put them.
    /// </summary>
    static (string Model, string Clip, string? Eyes, float Apart, float Forward, float Up, float Scale)? WatcherLook(EnemyKind kind) => kind switch
    {
        // Sat low in the grass, its eyes up on top of its head and wide apart (A.6).
        EnemyKind.Ribbit => ("ribbit", "sit", "head", 0.07f, 0.16f, 0.1f, RibbitScale),
        // Stood still, its head cocked listening (App. A.8).
        EnemyKind.Gaunt => ("gaunt", "listen", "head", 0.045f, 0.1f, 0, 1),
        // Down on its haunches over something it's gnawing, its eyes come up off it.
        EnemyKind.Grumbler => ("grumbler", "gnaw", "head", 0.06f, 0.12f, 0, 1),
        // A child stood calling, black with soot (note 309's call).
        EnemyKind.SootChildren => ("soot_child", "call", "head", 0.032f, 0.09f, 0, 1),
        // At the mouth of its gap, its front lifted, turning in jerks: no eyes to catch the light, only its pale front.
        EnemyKind.Whistler => ("whistler", "watch", null, 0, 0, 0, 1),
        _ => null,
    };

    /// <summary>
    /// A sign shown a crewmate afoot (World.Watcher, note 327; note 761; the director, 14 Oct, `docs/playtest/2026-10-14.md`
    /// line 22: "The floating eyes that are watching me in the dark, they don't really make much sense. I don't really feel
    /// a sense of foreboding"): not eyes floating on their own, but the thing that lives here, out at the lamp's edge where the
    /// sim put its eyes, its feet on the ground under them. It's barely a shape against the fog, all of it dark
    /// (<see cref="WatcherShade"/>), turned to whoever it was shown to and held still in its kind's watching pose, with only its
    /// eyes caught in the light (<see cref="Effects.EyePair"/>, their own size). They open, it watches, blinks, and as the sign
    /// ends it backs off into the dark and its eyes go out. Worked out from the sign's time left, so a screenshot repeats.
    /// </summary>
    /// <param name="at">Where the sim put its eyes (World.Watcher.At: the ground there plus its kind's signHeight).</param>
    /// <param name="left">Seconds the sign still shows; <paramref name="show"/> its whole length.</param>
    public void Watching(MeshBuilder mesh, Effects fx, EnemyKind kind, Double3 at, Double3 eye, double left, double show)
    {
        var rel = at - eye;
        if (left <= 0 || rel.Length > 260)
            return;
        if (WatcherLook(kind) is not { } look || !_models.ContainsKey(look.Model))
        {
            fx.Eyes(mesh, at, eye, left, show);
            return;
        }
        var afoot = _afoot ??= DataFile.Load<EnemyTuning>(Path.Combine(ContentRoot, EnemyTuning.File)).Director.Afoot;
        float height = (float)afoot.SignHeight.GetValueOrDefault(Director.Key(kind), afoot.SignHeightDefault);
        var spot = ToF(rel);
        var flat = new Vector3(-spot.X, 0, -spot.Z);
        var toViewer = flat.LengthSquared() > 1e-6f ? Vector3.Normalize(flat) : Vector3.UnitZ;
        // Backing off as the sign ends, away from whoever it watched, into the dark.
        float back = SmoothStep(WatcherBackSeconds, 0, (float)left) * WatcherBackM;
        var feet = spot - Vector3.UnitY * height - toViewer * back;
        // (CreateRotationY(a) takes the model's forward, -Z, to (-sin a, -cos a): turned to face them.)
        var model = Matrix4x4.CreateScale(look.Scale) * Matrix4x4.CreateRotationY(MathF.Atan2(-toViewer.X, -toViewer.Z) + (kind == EnemyKind.Ribbit ? MathF.PI : 0))
            * Matrix4x4.CreateTranslation(feet);
        _shade = WatcherShade;
        try
        {
            if (!Draw(mesh, look.Model, look.Clip, show - left, true, model, null, seed: 61))
                return;
        }
        finally
        {
            _shade = 1;
        }
        if (look.Eyes is not { } bone)
            return;
        var head = BoneAt(look.Model, bone, model) + (toViewer * look.Forward + Vector3.UnitY * look.Up) * look.Scale;
        var across = Vector3.Normalize(new Vector3(-toViewer.Z, 0, toViewer.X)) * (look.Apart * look.Scale);
        fx.EyePair(mesh, head - across, head + across, Effects.Open(left, show));
    }
}
