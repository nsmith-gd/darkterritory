using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Game;

/// <summary>
/// The Waker's stride (GDD §8 "A titan"; ARCHITECTURE §8 note 679; the director, 13 Oct 2026: "the train should be struggling to
/// outrun it because it has such a large gait, not because it itself is fast"). He's drawn <see cref="Scale"/> times his
/// modelled size (tools/blender/waker.py), so his walk and his run cover the ground they do in the Sim's time: below
/// <see cref="WalkUpTo"/> m/s he walks, from <see cref="RunFrom"/> he runs, and between the two the art blends them. Each is
/// played at his speed (<see cref="Speed"/>), so his feet stay where they come down. How far through his strides he is, the
/// art, the dust his feet raise and the thuds of his footfalls through the train (<see cref="DawnStir"/>) all read off the one
/// clock, the seconds since he began to run: a pure function of the Sim's, the same on every machine.
/// </summary>
public static class WakerGait
{
    /// <summary>How many times his modelled size he's drawn (waker.py's SCALE): 2.07 m modelled, 62 m stood.</summary>
    public const float Scale = 30;

    /// <summary>His walk's cycle (two steps): the ground it covers (m, drawn) and its seconds as authored (waker.py: 12 m/s).</summary>
    public const double WalkCycleMetres = 2 * 0.8 * Scale, WalkCycleSeconds = 4.0;

    /// <summary>His run's cycle (two steps), as the walk's (waker.py: 26 m/s, wakers.topSpeed).</summary>
    public const double RunCycleMetres = 2 * 1.473 * Scale, RunCycleSeconds = 3.4;

    /// <summary>Up to this speed (m/s) he walks, from <see cref="RunFrom"/> he runs; between, the two blended.</summary>
    public const double WalkUpTo = 12, RunFrom = 22;

    /// <summary>How far ahead of where he is (m, drawn) a foot comes down: the walk's and the run's (waker.py's stance × step).</summary>
    public const double WalkReach = 0.6 * 0.8 * Scale, RunReach = 0.353 * 1.473 * Scale;

    /// <summary>How far out to each side his feet come down (m, drawn).</summary>
    public const double FootOut = 0.125 * Scale;

    /// <summary>His speed (m/s) <paramref name="seconds"/> into his run (<see cref="Waker.Covered"/>'s rate).</summary>
    public static double Speed(double seconds, WakersTuning t) =>
        t.StartSpeed + (t.TopSpeed - t.StartSpeed) * Math.Clamp(seconds / Math.Max(1e-6, t.RampSeconds), 0, 1);

    /// <summary>How much of the run is in his stride at <paramref name="speed"/> (0 his walk .. 1 his run).</summary>
    public static double RunWeight(double speed)
    {
        double k = Math.Clamp((speed - WalkUpTo) / (RunFrom - WalkUpTo), 0, 1);
        return k * k * (3 - 2 * k);
    }

    /// <summary>The ground a cycle of his stride covers at <paramref name="speed"/> (m).</summary>
    public static double CycleMetres(double speed) => WalkCycleMetres + (RunCycleMetres - WalkCycleMetres) * RunWeight(speed);

    /// <summary>How long a cycle of his stride takes at <paramref name="speed"/> (s).</summary>
    public static double CycleSeconds(double speed) => CycleMetres(speed) / Math.Max(0.5, speed);

    /// <summary>How far ahead of him a foot comes down at <paramref name="speed"/> (m).</summary>
    public static double Reach(double speed) => WalkReach + (RunReach - WalkReach) * RunWeight(speed);

    /// <summary>
    /// The cycles his feet have been through <paramref name="seconds"/> into his run (the right foot comes down on each whole
    /// one, the left on each half): ∫ v / <see cref="CycleMetres"/>(v) dt over his speed's ramp (Simpson's rule), then
    /// steady at his top speed.
    /// </summary>
    public static double Cycles(double seconds, WakersTuning t)
    {
        if (seconds <= 0)
            return 0;
        double Rate(double s)
        {
            double v = Speed(s, t);
            return v / CycleMetres(v);
        }
        double ramp = Math.Min(seconds, Math.Max(0, t.RampSeconds));
        double sum = 0;
        if (ramp > 0)
        {
            const int N = 64;
            double h = ramp / N;
            sum = Rate(0) + Rate(ramp);
            for (int i = 1; i < N; i++)
                sum += (i % 2 == 1 ? 4 : 2) * Rate(i * h);
            sum *= h / 3;
        }
        return sum + Math.Max(0, seconds - ramp) * Rate(seconds);
    }

    /// <summary>How far through his stride's cycle he is (0..1: 0 the right foot down, 0.5 the left).</summary>
    public static double Phase(double seconds, WakersTuning t)
    {
        double c = Cycles(seconds, t);
        return c - Math.Floor(c);
    }

    /// <summary>Seconds since a foot of his last came down, <paramref name="seconds"/> into his run.</summary>
    public static double SinceFootfall(double seconds, WakersTuning t)
    {
        double half = 2 * Cycles(seconds, t);
        return (half - Math.Floor(half)) * CycleSeconds(Speed(seconds, t)) / 2;
    }
}
