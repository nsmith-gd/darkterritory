using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Game;

/// <summary>
/// The stir before dawn, felt (docs/design/creatures/wakers.md §2; ARCHITECTURE §8 note 588; the director, 9 Oct 2026: "the
/// tension before it is really, really important"): far-off thuds through the train, nothing at first, then one every few
/// seconds over the last <see cref="WakersTuning.StirSeconds"/> before dawn, and after it as close and as quick as the
/// nearest Waker's stride, till it has the train. Presentation only: it moves the eye, never the player (as
/// <see cref="BoilerShake"/>), and the settings' CAMERA SHAKE scales it down to none.
/// </summary>
public static class DawnStir
{
    /// <summary>How near it is: 0 before the stir, rising to 1 at dawn; after, 1 for one close behind (or holding the train).</summary>
    public static double Strength(World world)
    {
        if (world.Enemies?.Wakers is not { Enabled: true } t || world.Run is not { } run || run.Over)
            return 0;
        if (run.DawnIn > 0)
            return t.StirSeconds <= 0 ? 0 : Math.Clamp(1 - run.DawnIn / t.StirSeconds, 0, 1) * 0.6;
        if (world.ActiveEnemies.OfType<Waker>().FirstOrDefault(w => w.Lead) is not { } waker)
            return 0.6;
        if (waker.Holds)
            return 1;
        double behind = Math.Max(0, world.Train.RearDistance - waker.LineDistance);
        return Math.Clamp(1 - behind / (t.RiseBehind * 1.5), 0.6, 1);
    }

    /// <summary>
    /// The stir's sky (note 599): how far the cold line behind the train has come up, 0 before the stir to 1 at dawn and
    /// after (the dawn's own light takes over from there).
    /// </summary>
    public static float Sky(World world)
    {
        if (world.Enemies?.Wakers is not { Enabled: true } t || world.Run is not { } run || t.StirSeconds <= 0)
            return 0;
        return (float)Math.Clamp(1 - run.DawnIn / t.StirSeconds, 0, 1);
    }

    /// <summary>
    /// Where the stir's line lies (world, flat): behind the train, off its last car; once one's up, toward the lead Waker,
    /// so the light is where they rise.
    /// </summary>
    public static Double3 SkyFrom(World world, IReadOnlyList<Sim.Train.CarFrame> frames)
    {
        var rear = frames[world.Train.Dynamics.Consist.Vehicles[^1].Id];
        if (world.ActiveEnemies.OfType<Waker>().FirstOrDefault(w => w.Lead) is { } waker
            && (waker.WorldPosition(world.Train) - rear.Origin) with { Y = 0 } is { Length: > 1 } toward)
            return toward.Normalized;
        return rear.Back with { Y = 0 };
    }

    /// <summary>The last thud: how strong (0: none, so no stir) and how many seconds ago, its seconds apart shortening as it nears.</summary>
    public static (double Strength, double Since) Thud(World world, double seconds)
    {
        double s = Strength(world);
        return s <= 0 ? (0, double.PositiveInfinity) : (s, seconds % (7 - 6 * s));
    }

    /// <summary>
    /// How far the cars' lanterns are swung on their chains (radians, about the car's length; note 599: "the lamps swinging"):
    /// knocked by each thud and swinging back and forth, dying away before the next. <paramref name="car"/> gives each car its
    /// own little lag.
    /// </summary>
    public static float LampSway(World world, double seconds, int car = 0)
    {
        var (s, since) = Thud(world, seconds - car * 0.06);
        if (s <= 0)
            return 0;
        return (float)((0.05 + 0.16 * s * s) * Math.Sin(since * Math.PI * 2 / 1.5) * Math.Exp(-since * 0.9));
    }

    /// <summary>The eye's offset this frame (m): a thud, its seconds apart shortening as it nears, each dying away.</summary>
    public static Double3 Offset(World world, double seconds)
    {
        var (s, since) = Thud(world, seconds);
        if (s <= 0)
            return default;
        double a = (0.003 + 0.035 * s * s) * Math.Exp(-since * 5);
        return new Double3(a * 0.4 * Math.Sin(seconds * 31.3), -a * Math.Abs(Math.Sin(since * 40)), a * 0.3 * Math.Sin(seconds * 23.9 + 1.1));
    }
}
