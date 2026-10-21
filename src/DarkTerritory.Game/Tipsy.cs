using DarkTerritory.Sim;
using DarkTerritory.Sim.Physics;

namespace DarkTerritory.Game;

/// <summary>
/// The dining car's drinks on the drinker's view (upgrades.md §8.5, note 959; the director, 13 Oct: "If you drink one of those
/// drinks, though, your vision should get blurry ... make you sort of tip over a bit. Of course, that wouldn't happen in VR").
/// Presentation only, this machine's player alone: a drink they held and finished (it's gone from their hands and from the
/// train, not put down) blurs the view and leans the horizon one way and the other, for a minute and more with each. The
/// sim never sees it (the walk is straight, so nothing is predicted or sent), and the app never applies it in a headset.
/// Mirror of look.json <c>tipsy</c>.
/// </summary>
public sealed class Tipsy
{
    int _held = -1;
    int _drinks;
    double _until = double.NegativeInfinity;

    /// <summary>How many drinks are counted now (0 sober).</summary>
    public int Drinks(double now) => now < _until ? _drinks : 0;

    /// <summary>
    /// Watches <paramref name="me"/>'s hands at <paramref name="now"/> (the presentation's clock, s): a drink in them that's
    /// gone from the train since the last look was drunk.
    /// </summary>
    public void Observe(World world, int me, double now, TipsyTuning t)
    {
        if (world.Bodies.CarriedBy(me) is { Kind: BodyKind.Drink } glass)
        {
            _held = glass.Id;
            return;
        }
        if (_held >= 0 && !world.Bodies.All.Any(b => b.Id == _held))
            Drank(now, t);
        _held = -1;
    }

    /// <summary>A drink had at <paramref name="now"/>: one more counted (to <c>mostDrinks</c>), and longer to go.</summary>
    public void Drank(double now, TipsyTuning t)
    {
        if (now >= _until)
            _drinks = 0;
        _drinks = Math.Min(Math.Max(1, (int)t.MostDrinks), _drinks + 1);
        _until = now + t.Seconds + t.MoreEach * (_drinks - 1);
    }

    /// <summary>How far in it is (0..1): all of it until its last <c>fadeSeconds</c>, then easing off.</summary>
    double Strength(double now, TipsyTuning t) =>
        now >= _until ? 0 : Math.Clamp((_until - now) / Math.Max(1e-6, t.FadeSeconds), 0, 1);

    /// <summary>The view's blur now (0..1, the renderer's <c>Post.Blur</c>).</summary>
    public float Blur(double now, TipsyTuning t) =>
        Drinks(now) is var n and > 0 ? (float)Math.Clamp(Strength(now, t) * (t.Blur + t.BlurEach * (n - 1)), 0, 1) : 0;

    /// <summary>
    /// The view's lean now (radians, the camera's roll): the horizon swaying one way and the other over <c>leanPeriod</c>,
    /// with a slower drift under it so it never quite repeats.
    /// </summary>
    public double Roll(double now, TipsyTuning t)
    {
        if (Drinks(now) is not (> 0 and var n))
            return 0;
        double most = (t.LeanDegrees + t.LeanEach * (n - 1)) * Math.PI / 180;
        double w = 2 * Math.PI / Math.Max(0.5, t.LeanPeriod);
        return Strength(now, t) * most * (0.75 * Math.Sin(w * now) + 0.25 * Math.Sin(w * 0.37 * now + 1.3));
    }
}
