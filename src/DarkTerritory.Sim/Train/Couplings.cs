using Ballast;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Train;

/// <summary>
/// U2, a coupling working loose (note 356), from a cause the crew can see and name (note 640; the director, the inbox, 12 Oct:
/// "There should always be a clear identifiable cause of a decouple").
/// </summary>
public sealed record LooseTuning
{
    public bool Enabled { get; init; } = true;
    /// <summary>A run-in: the engine's rake slowing at this share of its whole brake or more (an emergency application).</summary>
    public double RunInShare { get; init; } = 0.8;
    /// <summary>m/s: a run-in from under this is no harder than a stop.</summary>
    public double RunInAbove { get; init; } = 5;
    /// <summary>
    /// s the brake's held so hard for a run-in (the slack has to close up): a tap of the whole brake to hold a speed doesn't
    /// (note 640: the bot driver's cruise taps it, a tick or two at a time, and every tap was counted a run-in).
    /// </summary>
    public double RunInSeconds { get; init; } = 1.5;
    /// <summary>
    /// m/s: rakes meeting faster than this knock the pin at the coupling they met at loose (coupling up too hard): over the
    /// stop driver's crawl onto a cut (up to about 1, note 630), under the couplers' <c>coupleMaxSpeed</c>.
    /// </summary>
    public double KnockAbove { get; init; } = 1.2;
    /// <summary>
    /// s the engine's rake has lurched on a posted bend taken too fast (sight.json <c>lurchOver</c> over its board, the cargo
    /// thrown about) before one gap's pin works loose: a third cause the crew can name (queue #466, note 732; the director's
    /// playtest of 14 Oct, line 12). Once a bend; 0 never.
    /// </summary>
    public double BendSeconds { get; init; } = 2;
    /// <summary>A pin holds while the train stands (the director, the inbox, 9 Oct) and while a hound pack is aboard (12 Oct).</summary>
    public bool Holds { get; init; } = true;
    public double PartAfter { get; init; } = 90;
    public double TightenSeconds { get; init; } = 4;
    public double Reach { get; init; } = 2.2;
}

/// <summary>
/// What worked a pin loose (note 640): the clerk names it, and (note 732) it's on the car's record, so every client hears the
/// moment it jumped as what jumped it.
/// </summary>
public enum LooseCause : byte { None, RunIn, Knock, Bend }

/// <summary>
/// A coupling working loose (orchestrator.md §5.1 U2, notes 356 and 640). A pin works loose only from something the crew can
/// see and name: a run-in (the driver's emergency brake at speed: the cars behind run in on the engine, and one gap's pin
/// jumps), a knock (rakes meeting harder than <see cref="LooseTuning.KnockAbove"/>, the pin at the coupling they met at) or a
/// bend taken too fast (the rake lurching over its board for <see cref="LooseTuning.BendSeconds"/>, note 732). What did it is
/// on the car's record (<see cref="Vehicle.LooseCause"/>), so the moment it jumped is heard as what jumped it.
/// Never while a hound pack is aboard (the director, the inbox, 12 Oct). It knocks, faster as it goes
/// (<see cref="Vehicle.Loose"/> on the car ahead of the gap, the seconds it's been at it), and it's called out as every break
/// is. The wrench in the gap, Use held <see cref="LooseTuning.TightenSeconds"/>, tightens it (<see cref="CrewActions"/>,
/// predicted like any of the hands' work). Left <see cref="LooseTuning.PartAfter"/> seconds of running, the pin drops and the
/// rake parts behind it (§24's cut-car rules: what's behind rolls free); it holds while the train stands or a pack is aboard.
/// Never the engine's own coupling: that would be the whole train. All on the host; the clients are sent it.
/// </summary>
public sealed class Couplings(LooseTuning tuning, ulong seed)
{
    readonly Pcg32 _rng = new(seed, 0xC0091);
    /// <summary>The engine's rake's speed over the last <see cref="LooseTuning.RunInSeconds"/>, a tick at a time (a ring).</summary>
    double[]? _speeds;
    int _tick;
    bool _runningIn, _bent;
    double _lurched;

    public LooseTuning Tuning => tuning;
    /// <summary>How many worked loose, and how many parted, so far tonight (host; for the tests).</summary>
    public int Came { get; private set; }
    public int Parted { get; private set; }
    /// <summary>The pin worked loose this tick, what did it and the train's speed then (host; the clerk's), or null.</summary>
    public (int Car, LooseCause Cause, double Speed)? Loosened { get; private set; }
    /// <summary>The first car of the rake a dropped pin parted from the train this tick (host), or −1.</summary>
    public int PartedFrom { get; private set; } = -1;

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

    /// <summary>Host, after the train's step: the loose pins work on (one left too long drops); a run-in or a knock looses another.</summary>
    /// <param name="open">How many may be loose at once (one per active player).</param>
    /// <param name="quiet">The yard or a fort: none comes loose.</param>
    /// <param name="packAboard">A hound pack aboard: none comes loose, and a loose one holds.</param>
    /// <returns>The car whose coupling parted behind it this tick, or −1.</returns>
    public int Step(TrainOnLine train, int open, bool quiet, bool packAboard = false)
    {
        (Loosened, PartedFrom) = (null, -1);
        var engine = train.RakeOf(0);
        double speed = Math.Abs(engine.Velocity);
        bool holding = tuning.Holds && (packAboard || speed < 0.05);
        int parted = -1, loose = 0;
        var tight = new List<int>();
        for (int i = 0; i < train.Vehicles.Count && i < train.Frames.Count; i++)
        {
            var v = train.Vehicles[i];
            if (v.Loose > 0 && !Runs(train, i))
                v.Loose = 0;
            // Tight again (the wrench, or the pin dropped): nothing's working it.
            if (v.Loose <= 0)
                v.LooseCause = LooseCause.None;
            if (v.Loose > 0 && !holding && (v.Loose += SimConstants.TickSeconds) >= tuning.PartAfter)
            {
                v.Loose = 0;
                int behind = train.VehicleBehind(i);
                if (parted < 0 && train.Uncouple(i, Parting.Pin))
                {
                    (parted, PartedFrom) = (i, behind);
                    Parted++;
                }
            }
            else if (v.Loose > 0)
                loose++;
            else if (Runs(train, i))
                tight.Add(i);
        }

        // A run-in: the engine's rake slowing at an emergency's rate over RunInSeconds, from speed (once each time it's braked so).
        _speeds ??= new double[Math.Max(1, (int)Math.Round(tuning.RunInSeconds * SimConstants.TickRate))];
        int slot = _tick++ % _speeds.Length;
        double before = _tick > _speeds.Length ? _speeds[slot] : double.NaN;
        _speeds[slot] = speed;
        double rated = engine.MaxBrakeForce * engine.BrakeEfficiency / Math.Max(1, engine.Consist.MassTonnes);
        bool runIn = before >= tuning.RunInAbove && (before - speed) / tuning.RunInSeconds >= tuning.RunInShare * rated;
        bool runInNow = runIn && !_runningIn;
        _runningIn = runIn;

        // A bend taken too fast (note 732): the rake lurching over its board long enough jumps one gap's pin, once a bend.
        _lurched = train.Lurching ? _lurched + SimConstants.TickSeconds : 0;
        bool bendNow = tuning.BendSeconds > 0 && _lurched >= tuning.BendSeconds && !_bent;
        _bent = train.Lurching && (_bent || bendNow);

        // A knock: rakes meeting too hard, the pin at the coupling they met at if it's the engine's rake's and can work.
        int knocked = -1;
        double knockSpeed = 0;
        foreach (var c in train.ContactsThisTick)
            if (c.ClosingSpeed > tuning.KnockAbove && c.Front >= 0 && c.Front < train.Vehicles.Count && train.Vehicles[c.Front].Loose <= 0
                && Runs(train, c.Front) && c.ClosingSpeed > knockSpeed)
                (knocked, knockSpeed) = (c.Front, c.ClosingSpeed);

        if (quiet || packAboard || loose >= open || tight.Count == 0)
            return parted;
        if (knocked >= 0)
            Loose(train, knocked, LooseCause.Knock, knockSpeed);
        else if (runInNow)
            Loose(train, tight[(int)(_rng.NextDouble() * tight.Count) % tight.Count], LooseCause.RunIn, before);
        else if (bendNow)
            Loose(train, tight[(int)(_rng.NextDouble() * tight.Count) % tight.Count], LooseCause.Bend, speed);
        return parted;
    }

    void Loose(TrainOnLine train, int car, LooseCause cause, double speed)
    {
        train.Vehicles[car].Loose = SimConstants.TickSeconds;
        train.Vehicles[car].LooseCause = cause;
        Loosened = (car, cause, speed);
        Came++;
    }

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
