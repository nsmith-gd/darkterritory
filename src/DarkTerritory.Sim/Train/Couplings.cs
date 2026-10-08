using Ballast;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Train;

/// <summary>U2, a coupling working loose (note 356): a gap's pin worked loose by the running, on bends most.</summary>
public sealed record LooseTuning
{
    public bool Enabled { get; init; } = true;
    public double EveryMetres { get; init; } = 6000;
    public double Jitter { get; init; } = 0.5;
    public double RoughRadius { get; init; } = 800;
    public double RoughFactor { get; init; } = 3;
    public double FirstAfterMetres { get; init; } = 4000;
    public double PartAfter { get; init; } = 90;
    public double TightenSeconds { get; init; } = 4;
    public double Reach { get; init; } = 2.2;
}

/// <summary>
/// A coupling working loose (orchestrator.md §5.1 U2, note 356). Every so many metres of the train's running, the metres
/// counting more on a bend (rough track), the pin in one of its gaps works loose: it knocks, faster as it goes
/// (<see cref="Vehicle.Loose"/> on the car ahead of the gap, the seconds it's been at it), and it's called out as every break
/// is. The wrench in the gap, Use held <see cref="LooseTuning.TightenSeconds"/>, tightens it (<see cref="CrewActions"/>,
/// predicted like any of the hands' work). Left <see cref="LooseTuning.PartAfter"/> seconds, the pin drops and the rake
/// parts behind it (§24's cut-car rules: what's behind rolls free). Never the engine's own coupling: that would be the
/// whole train. The worst job, so the rarest. All on the host; the clients are sent it.
/// </summary>
public sealed class Couplings(LooseTuning tuning, ulong seed)
{
    readonly Pcg32 _rng = new(seed, 0xC0091);
    double _toGo = -1;

    public LooseTuning Tuning => tuning;
    /// <summary>How many worked loose, and how many parted, so far tonight (host; for the tests).</summary>
    public int Came { get; private set; }
    public int Parted { get; private set; }

    /// <summary>Where the coupling behind a car is (that car's frame): the middle of the gap, at the coupler's height.</summary>
    public static Double3 Pin(CarShape shape, TrainTuning t) =>
        new(t.Geometry.PlateX, t.Geometry.CouplerHeight, shape.HalfLength + t.Geometry.CouplingGap / 2);

    /// <summary>The couplings that can work loose: behind a car (never the engine) of the engine's rake, with a car behind it.</summary>
    static bool Runs(TrainOnLine train, int car)
    {
        if (train.Vehicles[car].IsEngine || train.Vehicles[car].Taken || train.StandingCar(car))
            return false;
        var rake = train.RakeOf(0);
        int at = rake.Consist.IndexOf(car);
        return at >= 0 && at + 1 < rake.Consist.Vehicles.Count;
    }

    /// <summary>Host, after the train's step: the loose pins work on (one left too long drops); the running looses another.</summary>
    /// <param name="open">How many may be loose at once (one per active player).</param>
    /// <param name="quiet">The yard or a fort: none comes loose.</param>
    /// <returns>The car whose coupling parted behind it this tick, or −1.</returns>
    public int Step(TrainOnLine train, int open, bool quiet)
    {
        if (_toGo < 0)
            _toGo = tuning.FirstAfterMetres + Draw();
        var engine = train.RakeOf(0);
        double speed = Math.Abs(engine.Velocity);
        bool rough = Math.Abs(train.Line.Sample(engine.Path, engine.Distance).Curvature) * tuning.RoughRadius >= 1;
        _toGo = Math.Max(0, _toGo - speed * SimConstants.TickSeconds * (rough ? tuning.RoughFactor : 1));
        int parted = -1, loose = 0;
        var tight = new List<int>();
        for (int i = 0; i < train.Vehicles.Count && i < train.Frames.Count; i++)
        {
            var v = train.Vehicles[i];
            if (v.Loose > 0 && !Runs(train, i))
                v.Loose = 0;
            if (v.Loose > 0 && (v.Loose += SimConstants.TickSeconds) >= tuning.PartAfter)
            {
                v.Loose = 0;
                if (parted < 0 && train.Uncouple(i))
                {
                    parted = i;
                    Parted++;
                }
            }
            else if (v.Loose > 0)
                loose++;
            else if (Runs(train, i))
                tight.Add(i);
        }
        if (_toGo > 0 || quiet || loose >= open || tight.Count == 0)
            return parted;
        train.Vehicles[tight[(int)(_rng.NextDouble() * tight.Count) % tight.Count]].Loose = SimConstants.TickSeconds;
        _toGo = Draw();
        Came++;
        return parted;
    }

    double Draw() => tuning.EveryMetres * (1 + tuning.Jitter * (2 * _rng.NextDouble() - 1));

    /// <summary>
    /// The loose coupling this player can tighten now, if any (the car ahead of its gap): alive, off the roofs and out of the
    /// rooms, the hands within reach of its pin (from the plate in the gap, an end ladder over it, or the ground beside it).
    /// Whether the wrench is in hand is <see cref="Tightens"/>'.
    /// </summary>
    public static int? Within(in PlayerState s, TrainOnLine train, LooseTuning t)
    {
        if (!s.Alive || s.Surface is Surface.Roof || PlayerMotor.Indoors(s, train))
            return null;
        var at = PlayerMotor.WorldPosition(s, train) + Double3.Up * 0.9;
        int? best = null;
        double nearest = t.Reach;
        for (int i = 0; i < train.Vehicles.Count && i < train.Frames.Count; i++)
        {
            if (train.Vehicles[i].Loose <= 0)
                continue;
            var f = train.Frames[i];
            double d = (f.ToWorld(Pin(f.Shape, train.Dynamics.Tuning)) - at).Length;
            if (d <= nearest)
                (best, nearest) = (i, d);
        }
        return best;
    }

    /// <summary>
    /// The loose couplings, called out as every break is (note 301's one language: "mend me here"), in the gap behind their
    /// car, added to <paramref name="breaks"/> (<see cref="RepairCallouts.Of"/>'s); with <paramref name="crew"/>, the indices
    /// of those someone's tightening now are added to <paramref name="mending"/>.
    /// </summary>
    public static void Callouts(TrainOnLine train, List<BreakCallout> breaks, IEnumerable<PlayerState>? crew = null, ISet<int>? mending = null)
    {
        if (train.Loose is not { Enabled: true } t)
            return;
        for (int i = 0; i < train.Vehicles.Count && i < train.Frames.Count; i++)
        {
            if (train.Vehicles[i].Loose <= 0)
                continue;
            if (crew is not null && mending is not null && crew.Any(s => s.ActionProgress > 0 && Tightens(s, train) && Within(s, train, t) == i))
                mending.Add(breaks.Count);
            breaks.Add(new BreakCallout(BreakKind.Coupling, i, Pin(train.Frames[i].Shape, train.Dynamics.Tuning)));
        }
    }

    /// <summary>Whether these hands tighten a pin: the wrench in hand where it's the tool (note 301), bare hands where it isn't.</summary>
    public static bool Tightens(in PlayerState s, TrainOnLine train) => !Repairs.ByWrench(train) || Repairs.WrenchInHand(s);
}
