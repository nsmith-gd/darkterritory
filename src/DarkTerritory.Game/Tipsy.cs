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
    public void Observe(World world, int me, double now, TipsyTuning t) =>
        Watch(world.Bodies.CarriedBy(me) is { Kind: BodyKind.Drink } glass ? glass.Id : -1, world.Bodies.All, now, t);

    /// <summary>
    /// Watches a drinker's hands at <paramref name="now"/>: <paramref name="glass"/> the drink in them (its body's id, or -1),
    /// and the one they held last time drunk if it's gone from <paramref name="bodies"/> (not put down).
    /// </summary>
    public void Watch(int glass, IReadOnlyList<Body> bodies, double now, TipsyTuning t)
    {
        if (glass >= 0)
        {
            _held = glass;
            return;
        }
        if (_held >= 0 && !Present(bodies, _held))
            Drank(now, t);
        _held = -1;
    }

    static bool Present(IReadOnlyList<Body> bodies, int id)
    {
        for (int i = 0; i < bodies.Count; i++)
            if (bodies[i].Id == id)
                return true;
        return false;
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
    /// How far their head lolls now as the rest of the crew see it (note 974; radians at most, 0 sober): <c>lollDegrees</c>,
    /// <c>lollEach</c> more a drink after the first, easing off with the drinks.
    /// </summary>
    public double Loll(double now, TipsyTuning t) =>
        Drinks(now) is var n and > 0 ? Strength(now, t) * (t.LollDegrees + t.LollEach * (n - 1)) * Math.PI / 180 : 0;

    /// <summary>
    /// A drinker's head as the crew see it, <paramref name="loll"/> (<see cref="Loll"/>) at <paramref name="at"/> (s, their own
    /// clock): rolled over to one side and the other over <c>lollPeriod</c>, a slower drift under it, and dropped forward
    /// (radians) the more it's over.
    /// </summary>
    public static (float Roll, float Nod) LollPose(double loll, double at, TipsyTuning t)
    {
        if (loll <= 0)
            return (0, 0);
        double w = 2 * Math.PI / Math.Max(0.5, t.LollPeriod);
        double roll = loll * (0.8 * Math.Sin(w * at) + 0.2 * Math.Sin(w * 0.41 * at + 2.1));
        return ((float)roll, (float)(loll * t.LollNod * (0.6 + 0.4 * Math.Abs(Math.Sin(w * at)))));
    }

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

/// <summary>
/// The crew's drinks as everyone else sees them (upgrades.md §8.5: "the crew see the drinker's head loll"; note 974): a
/// <see cref="Tipsy"/> a crewmate, each watching their own hands in the bodies this machine has. Presentation only.
/// </summary>
public sealed class Drinkers
{
    readonly Dictionary<int, Tipsy> _by = new();

    /// <summary>Watches each of <paramref name="crew"/>'s hands at <paramref name="now"/> (the scene's clock, s).</summary>
    public void Observe(IReadOnlyList<Body> bodies, IEnumerable<int> crew, double now, TipsyTuning t)
    {
        foreach (int id in crew)
        {
            int glass = -1;
            for (int i = 0; i < bodies.Count; i++)
                if (bodies[i].Carrier == id && bodies[i].Kind == BodyKind.Drink)
                {
                    glass = bodies[i].Id;
                    break;
                }
            if (!_by.TryGetValue(id, out var tipsy))
            {
                if (glass < 0)
                    continue;
                _by[id] = tipsy = new Tipsy();
            }
            tipsy.Watch(glass, bodies, now, t);
        }
    }

    /// <summary>How far <paramref name="id"/>'s head lolls at <paramref name="now"/> (<see cref="Tipsy.Loll"/>).</summary>
    public double Loll(int id, double now, TipsyTuning t) => _by.TryGetValue(id, out var tipsy) ? tipsy.Loll(now, t) : 0;
}
