namespace Ballast;

/// <summary>
/// Fixed-timestep accumulator. The simulation always advances in whole ticks of
/// <see cref="TickSeconds"/>; rendering interpolates using <see cref="Alpha"/>.
/// </summary>
public sealed class FixedStepClock
{
    public FixedStepClock(int tickRate, int maxTicksPerFrame = 8)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tickRate, 1);
        TickRate = tickRate;
        TickSeconds = 1.0 / tickRate;
        MaxTicksPerFrame = maxTicksPerFrame;
    }

    public int TickRate { get; }
    public double TickSeconds { get; }
    public int MaxTicksPerFrame { get; }
    public long Tick { get; private set; }

    /// <summary>Fraction of a tick elapsed since the last simulated tick, in [0, 1).</summary>
    public double Alpha => _accumulator / TickSeconds;

    double _accumulator;

    /// <summary>Adds real elapsed time and returns how many ticks to simulate now.</summary>
    public int Advance(double elapsedSeconds)
    {
        _accumulator += Math.Max(0, elapsedSeconds);
        int ticks = 0;
        while (_accumulator >= TickSeconds && ticks < MaxTicksPerFrame)
        {
            _accumulator -= TickSeconds;
            ticks++;
        }
        // Spiral-of-death guard: drop time we could not simulate.
        if (ticks == MaxTicksPerFrame)
            _accumulator = Math.Min(_accumulator, TickSeconds);
        Tick += ticks;
        return ticks;
    }
}
