using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>
/// The train in a Waker's hands, as drawn (docs/design/creatures/wakers.md §5; ARCHITECTURE §8 note 588; the director, 9 Oct
/// 2026: "the picking up of the train, especially with you in it, you should experience that if you're still playing"). From
/// the rear, a car every <see cref="WakersTuning.LiftSeconds"/>, each goes up rear end first and the next is hauled up after
/// it, the lifted cars a ramp up to its mouth; an eaten car is gone. Presentation only, as <see cref="TippleTilt"/>: the sim's frames never leave
/// the rails, so whoever's in a car stays stood where they were in it and their eyes (drawn from these frames) go up with
/// it. Every machine draws it alike from the Waker's replicated state.
/// </summary>
public static class WakerLift
{
    /// <summary>How much higher each car's rear end is than its front once it's all the way up (m): the lifted cars a ramp up to its mouth.</summary>
    public const double Step = 6;

    /// <summary>Lifts the held cars of <paramref name="frames"/> (the train's, by vehicle id) in place.</summary>
    public static void Apply(List<CarFrame> frames, TrainOnLine train, World world) =>
        Apply(frames, train, world.ActiveEnemies, world.Enemies?.Wakers);

    /// <summary>The same, from <paramref name="enemies"/> (a staged scene's).</summary>
    public static void Apply(List<CarFrame> frames, TrainOnLine train, IEnumerable<Enemy> enemies, WakersTuning? t)
    {
        if (t is null || enemies.OfType<Waker>().FirstOrDefault(w => w.Lead && w.Holds) is not { } waker)
            return;
        var cars = train.Dynamics.Consist.Vehicles;
        int eaten = waker.EatenCars(t, cars.Count);
        // How far up each car's going, from the rear (k = 0); its rear end's height is the sum of its own and every car's in
        // front of it, so each hangs from the one behind it, and the frontmost of them still has its front on the rail.
        var p = new double[cars.Count];
        for (int k = 0; k < cars.Count; k++)
            p[k] = Ease(Math.Clamp((waker.Extra - k * t.LiftSeconds) / Math.Max(1e-6, t.LiftSeconds), 0, 1));
        double Height(int junction)
        {
            double h = 0;
            for (int m = junction; m < cars.Count; m++)
                h += p[m] * Step;
            return h;
        }
        for (int k = 0; k < cars.Count; k++)
        {
            int id = cars[cars.Count - 1 - k].Id;
            if (id < 0 || id >= frames.Count)
                continue;
            if (k < eaten)
            {
                // In its mouth: out of sight.
                frames[id] = frames[id] with { Origin = frames[id].Origin - Double3.Up * 500 };
                continue;
            }
            if (p[k] > 0)
                frames[id] = Lift(frames[id], Height(k), Height(k + 1));
        }
    }

    static double Ease(double p) => p * p * (3 - 2 * p);

    /// <summary><paramref name="f"/> with its rear end raised <paramref name="rear"/> m and its front end <paramref name="front"/> m, tipped to match.</summary>
    public static CarFrame Lift(in CarFrame f, double rear, double front)
    {
        double a = Math.Atan2(rear - front, 2 * f.Shape.HalfLength), c = Math.Cos(a), s = Math.Sin(a);
        var back = f.Back * c + f.Up * s;
        var up = f.Up * c - f.Back * s;
        return f with { Origin = f.Origin + Double3.Up * ((rear + front) / 2), Back = back, Up = up };
    }
}
