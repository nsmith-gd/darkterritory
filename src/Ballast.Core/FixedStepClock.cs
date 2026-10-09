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
    public double Alpha => _accumulator / Step;

    /// <summary>
    /// How much real time a tick takes, as a multiple of <see cref="TickSeconds"/>: 1 normally; a little over it slows the
    /// simulation against the wall clock (note 532: a client pacing itself to a host that's behind). Never under 1.
    /// </summary>
    public double Stretch
    {
        get => _stretch;
        set => _stretch = Math.Max(1, value);
    }

    /// <summary>Real time the guard below threw away because the frames couldn't simulate it, in seconds, since the start.</summary>
    public double DroppedSeconds { get; private set; }

    double _accumulator, _stretch = 1;
    double Step => TickSeconds * _stretch;

    /// <summary>Adds real elapsed time and returns how many ticks to simulate now.</summary>
    public int Advance(double elapsedSeconds)
    {
        _accumulator += Math.Max(0, elapsedSeconds);
        int ticks = 0;
        while (_accumulator >= Step && ticks < MaxTicksPerFrame)
        {
            _accumulator -= Step;
            ticks++;
        }
        // Spiral-of-death guard: drop time we could not simulate.
        if (ticks == MaxTicksPerFrame && _accumulator > Step)
        {
            DroppedSeconds += _accumulator - Step;
            _accumulator = Step;
        }
        Tick += ticks;
        return ticks;
    }
}
