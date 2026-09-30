namespace DarkTerritory.Sim.Combat;

/// <summary>
/// The crew loudness meter's levels (combat.json <c>loudness</c>, GDD App. C.7): how loud each named level is, as Choir
/// aggro a second, and the window the meter reads over.
/// </summary>
public sealed record LoudnessTuning
{
    public Dictionary<string, double> Levels { get; init; } = new() { ["cannon"] = 4.5, ["machinery"] = 1.5 };
    public double WindowSeconds { get; init; } = 3;
}

/// <summary>
/// The crew loudness meter (GDD App. C.7), as far as this build has it: combined loudness from what the living do, measured
/// over a few seconds so a single moment doesn't count. It drives the Choir. Gunfire has always fed the Choir by the round
/// (<see cref="ChoirState.RoundFired"/>) and is only counted here; the rest (a breach, App. D.7) feeds it through
/// <see cref="Step"/>. Voices, the whistle and machinery aren't on it yet (ARCHITECTURE §8 note 92). Nothing the dead or
/// lobbied do ever reaches it (App. D.1).
/// </summary>
public sealed class CrewLoudness
{
    readonly Dictionary<string, double> _thisTick = new();
    readonly Queue<double> _window = new();
    double _windowSum;

    /// <summary>Everything that's been loud this run, by source (seconds × level): what tests and the harness read.</summary>
    public Dictionary<string, double> Totals { get; } = new();
    /// <summary>The meter: combined loudness a second, over the window.</summary>
    public double Level { get; private set; }

    /// <summary>Something loud this tick, at a named level (combat.json): "cannon", "machinery".</summary>
    /// <param name="counted">Already fed to the Choir its own way (a round fired): counted here, not fed again.</param>
    public void Add(string source, string level, LoudnessTuning t, bool counted = false)
    {
        double v = t.Levels.GetValueOrDefault(level);
        if (v <= 0)
            return;
        _thisTick[source] = Math.Max(_thisTick.GetValueOrDefault(source), v);
        if (counted)
            _counted.Add(source);
    }

    readonly HashSet<string> _counted = new();

    /// <summary>Once a tick: the meter moves, and what wasn't already the Choir's goes to it.</summary>
    public void Step(ref ChoirState choir, ChoirTuning c, LoudnessTuning t, double dt)
    {
        double sum = 0, feed = 0;
        foreach (var (source, v) in _thisTick)
        {
            sum += v;
            Totals[source] = Totals.GetValueOrDefault(source) + v * dt;
            if (!_counted.Contains(source))
                feed += v;
        }
        _thisTick.Clear();
        _counted.Clear();
        _window.Enqueue(sum);
        _windowSum += sum;
        int ticks = Math.Max(1, (int)Math.Round(t.WindowSeconds / dt));
        while (_window.Count > ticks)
            _windowSum -= _window.Dequeue();
        Level = _windowSum / ticks;
        if (feed > 0)
            choir.Noise(c, feed * dt);
    }
}
