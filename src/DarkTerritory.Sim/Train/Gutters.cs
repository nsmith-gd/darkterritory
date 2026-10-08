using Ballast;

namespace DarkTerritory.Sim.Train;

/// <summary>U3, a lamp guttering (note 346): a car's lamp running low on oil as the train runs.</summary>
public sealed record GutterTuning
{
    public bool Enabled { get; init; } = true;
    public double EveryMetres { get; init; } = 3000;
    public double Jitter { get; init; } = 0.5;
    public double FirstAfterMetres { get; init; } = 3000;
    public double OutAfter { get; init; } = 45;
}

/// <summary>
/// A lamp guttering (orchestrator.md §5.1 U3, note 346). Every so many metres of the train's running, a lit car's lamp starts
/// to gutter: it flickers and dims, worse as it goes (<see cref="Vehicle.Gutter"/>, the seconds it's been at it). Whoever's
/// in the car trims it with the lamp key (<see cref="World"/>'s lamp press: guttering, the press trims it rather than putting
/// it out). Left <see cref="GutterTuning.OutAfter"/> seconds, it goes out, and the car is dark: Climbers make for an unlit car
/// (App. A.4), and someone lights it again by hand. All on the host; the clients are sent it.
/// </summary>
public sealed class Gutters(GutterTuning tuning, ulong seed)
{
    readonly Pcg32 _rng = new(seed, 0x6077);
    double _toGo = -1;

    public GutterTuning Tuning => tuning;
    /// <summary>How many started, and how many went out, so far tonight (host; for the tests).</summary>
    public int Came { get; private set; }
    public int WentOut { get; private set; }

    /// <summary>The cars whose lamp can gutter: lit, with a room for it to light, in the engine's rake.</summary>
    static bool Lit(TrainOnLine train, int car) =>
        !train.Vehicles[car].IsEngine && train.Vehicles[car].LampLit && train.Frames[car].Shape.Interior is not null
        && !train.StandingCar(car) && train.RakeOf(car) == train.RakeOf(0);

    /// <summary>Host, after the train's step: the guttering lamps burn down (one left too long goes out); the running brings another.</summary>
    /// <param name="open">How many may gutter at once (one per active player).</param>
    /// <param name="quiet">The yard or a fort: none starts.</param>
    public void Step(TrainOnLine train, int open, bool quiet)
    {
        if (_toGo < 0)
            _toGo = tuning.FirstAfterMetres + Draw();
        _toGo = Math.Max(0, _toGo - Math.Abs(train.RakeOf(0).Velocity) * SimConstants.TickSeconds);
        int guttering = 0;
        var lit = new List<int>();
        for (int i = 0; i < train.Vehicles.Count && i < train.Frames.Count; i++)
        {
            var v = train.Vehicles[i];
            if (v.Gutter > 0 && !v.LampLit)
                v.Gutter = 0;
            if (v.Gutter > 0 && (v.Gutter += SimConstants.TickSeconds) >= tuning.OutAfter)
            {
                (v.Gutter, v.LampLit) = (0, false);
                WentOut++;
            }
            else if (v.Gutter > 0)
                guttering++;
            else if (Lit(train, i))
                lit.Add(i);
        }
        if (_toGo > 0 || quiet || guttering >= open || lit.Count == 0)
            return;
        train.Vehicles[lit[(int)(_rng.NextDouble() * lit.Count) % lit.Count]].Gutter = SimConstants.TickSeconds;
        _toGo = Draw();
        Came++;
    }

    double Draw() => tuning.EveryMetres * (1 + tuning.Jitter * (2 * _rng.NextDouble() - 1));
}
