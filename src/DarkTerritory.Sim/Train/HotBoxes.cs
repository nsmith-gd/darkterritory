using Ballast;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Train;

/// <summary>
/// Mirror of content/tuning/upkeep.json: the small jobs the train makes as it runs (docs/design/orchestrator.md §5.1; GDD
/// App. F.3, the director, 7 Oct 2026: "on the train, still relatively boring from point A to point B"). Field docs live in
/// that file. Null on a world without it: nothing runs hot.
/// </summary>
public sealed record UpkeepTuning
{
    public const string File = "tuning/upkeep.json";
    public HotBoxTuning HotBox { get; init; } = new();
}

/// <summary>U1, the hot box (note 331): an axle box running dry on a car as the train runs.</summary>
public sealed record HotBoxTuning
{
    public bool Enabled { get; init; } = true;
    public double EveryMetres { get; init; } = 2500;
    public double Jitter { get; init; } = 0.5;
    public double FastAbove { get; init; } = 18;
    public double FastFactor { get; init; } = 1.5;
    public double FirstAfterMetres { get; init; } = 2000;
    public double DragAfter { get; init; } = 60;
    public double SlowBy { get; init; } = 2;
    public double HoldFactor { get; init; } = 1.1;
    public double FireAfter { get; init; } = 120;
    public double GreaseSeconds { get; init; } = 3;
    public double Reach { get; init; } = 3.0;
    public double BogieInset { get; init; } = 1.6;
}

/// <summary>
/// The hot box (orchestrator.md §5.1 U1, note 331). Every so many metres of the train's running (more above a speed), a
/// car's rear axle box runs dry; hot, it squeals, then smokes. Greased (Use held at it, from the coupling gap behind the car or the ground
/// beside it), it's cool again. Left alone it drags (the train loses top speed), then it catches: a car fire (App. C.5).
/// The host starts one and sets the fire; the seconds a box has run hot count on every machine alike (prediction drags as
/// the host does), and greasing is predicted like any of the hands' work.
/// </summary>
public sealed class HotBoxes(HotBoxTuning tuning, ulong seed)
{
    readonly Pcg32 _rng = new(seed, 0x407B0);
    double _toGo = -1;

    public HotBoxTuning Tuning => tuning;
    /// <summary>How many came on, and how many caught, so far tonight (host; for the harness and the tests).</summary>
    public int Came { get; private set; }
    public int Caught { get; private set; }

    /// <summary>Where a car's hot box is (car frame): its rear bogie, at the axle's height, on the right, outside the wheel.</summary>
    public static Double3 Box(CarShape shape, HotBoxTuning t) =>
        new(shape.HalfWidth + 0.05, 0.5, shape.HalfLength - t.BogieInset);

    /// <summary>The cars that can run hot: every one but the engine, in the engine's rake, not standing on a siding.</summary>
    static bool Runs(TrainOnLine train, int car) =>
        !train.Vehicles[car].IsEngine && !train.StandingCar(car) && train.RakeOf(car) == train.RakeOf(0);

    /// <summary>Host, after the train's step: the train's running brings a box on; one left too long sets its car alight.</summary>
    /// <param name="open">How many may be open at once (orchestrator.md §5.1: one per active player).</param>
    /// <param name="quiet">The yard or a fort: nothing comes on (none in the grace).</param>
    /// <returns>A car whose box caught this tick, or −1.</returns>
    public int Step(TrainOnLine train, int open, bool quiet)
    {
        if (_toGo < 0)
            _toGo = tuning.FirstAfterMetres + Draw();
        double speed = Math.Abs(train.RakeOf(0).Velocity);
        _toGo = Math.Max(0, _toGo - speed * SimConstants.TickSeconds * (speed > tuning.FastAbove ? tuning.FastFactor : 1));
        int caught = -1, hot = 0;
        var cool = new List<int>();
        for (int i = 0; i < train.Vehicles.Count; i++)
        {
            var v = train.Vehicles[i];
            if (v.HotBox >= tuning.FireAfter)
            {
                v.HotBox = 0;
                caught = i;
                Caught++;
            }
            else if (v.HotBox > 0)
                hot++;
            else if (Runs(train, i))
                cool.Add(i);
        }
        // Its running done, one comes on, on a car drawn from those running cool; or it waits for one to be free.
        if (_toGo > 0 || quiet || hot >= open || cool.Count == 0)
            return caught;
        train.Vehicles[cool[(int)(_rng.NextDouble() * cool.Count) % cool.Count]].HotBox = SimConstants.TickSeconds;
        _toGo = Draw();
        Came++;
        return caught;
    }

    double Draw() => tuning.EveryMetres * (1 + tuning.Jitter * (2 * _rng.NextDouble() - 1));

    /// <summary>
    /// The hot box this player can grease now, if any: alive, off the car's roof and out of its room, within reach of the box
    /// (from the coupling gap behind it, its end ladder, or the ground beside it).
    /// </summary>
    public static int? Within(in PlayerState s, TrainOnLine train, HotBoxTuning t)
    {
        if (!s.Alive || s.Surface is Surface.Roof || PlayerMotor.Indoors(s, train))
            return null;
        var at = PlayerMotor.WorldPosition(s, train);
        int? best = null;
        double nearest = t.Reach;
        for (int i = 0; i < train.Vehicles.Count && i < train.Frames.Count; i++)
        {
            if (train.Vehicles[i].HotBox <= 0)
                continue;
            var f = train.Frames[i];
            // From the hands (a metre up from the feet); either side of the car, as there's a box on each rail.
            var local = f.ToLocal(at) + new Double3(0, 0.9, 0);
            local = local with { X = Math.Abs(local.X) };
            double d = (local - Box(f.Shape, t)).Length;
            if (d <= nearest)
                (best, nearest) = (i, d);
        }
        return best;
    }

    /// <summary>
    /// A rake's drag from its hot boxes run past <see cref="HotBoxTuning.DragAfter"/>: each takes <see cref="HotBoxTuning.SlowBy"/>
    /// off the top speed, held there as the Car Hugger holds its cap (App. A.3): over it, a drag of
    /// <see cref="HotBoxTuning.HoldFactor"/> times what the engine can pull (<paramref name="pull"/>, m/s² on this rake).
    /// </summary>
    public static double Drag(HotBoxTuning t, TrainDynamics rake, double topSpeed, double pull)
    {
        int dragging = 0;
        foreach (var v in rake.Consist.Vehicles)
            if (v.HotBox >= t.DragAfter)
                dragging++;
        return dragging > 0 && rake.Speed > topSpeed - t.SlowBy * dragging ? t.HoldFactor * pull : 0;
    }
}
