using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Run;

/// <summary>Where a <see cref="SpurDrill"/> is in GDD §17's sequence.</summary>
public enum DrillStep : byte
{
    /// <summary>Running up to stop short of the spur's points.</summary>
    Approach,
    /// <summary>Stopped: the cars that won't fit are cut off to wait on the main line, and the switch is set for the spur.</summary>
    Cut,
    /// <summary>The engine takes the empties in to the buffer stop.</summary>
    SpurIn,
    /// <summary>Stopped at the facility while the crew loads (the caller says when it's done).</summary>
    Loading,
    /// <summary>Backing out through the points onto the cars left waiting, and coupling on.</summary>
    BackOut,
    /// <summary>The whole train back behind the points; the switch goes back to the main line.</summary>
    Reset,
    /// <summary>Away up the main line past the facility.</summary>
    Depart,
    Done,
}

/// <summary>
/// The facility set piece driven by script (GDD §17): "decouple the engine; take empty cars into the facility; switches
/// are thrown by hand; start the loading machinery; reverse out and recouple". The host does it the way a crew would,
/// with the regulator, the brake, the coupler and the switch, and nothing else. It verifies the track, the rakes and the
/// run headless (`dt facility drill`, <c>SpurDrillTests</c>); crew bots doing it through intent come after (T31).
/// </summary>
public sealed class SpurDrill
{
    readonly World _world;
    readonly int _facility, _spur;
    readonly double _pointsLength;
    readonly List<(DrillStep Step, double Seconds)> _log = [];
    int _waiting = -1; // the first vehicle left on the main line, or −1 if the whole train fits
    double _seconds, _stillFor;

    /// <param name="facility">Index of a facility with a spur.</param>
    public SpurDrill(World world, int facility)
    {
        _world = world;
        _facility = facility;
        _spur = world.Run?.SpurOf(facility) ?? RailLine.MainPath;
        if (_spur < 0)
            throw new ArgumentException($"facility {facility} has no spur", nameof(facility));
        _pointsLength = world.Switches?.Tuning.PointsLength ?? 12;
        Log(DrillStep.Approach);
    }

    public DrillStep Step { get; private set; }
    /// <summary>Each step and when it began, in seconds since the drill started.</summary>
    public IReadOnlyList<(DrillStep Step, double Seconds)> Timeline => _log;
    /// <summary>Cars (by vehicle id) that went down the spur.</summary>
    public IReadOnlyList<int> TookIn { get; private set; } = [];
    /// <summary>Cars left on the main line while the empties went in.</summary>
    public IReadOnlyList<int> LeftWaiting { get; private set; } = [];
    /// <summary>Set by the caller once the crew has finished loading; until then the train waits at the facility.</summary>
    public bool Loaded { get; set; }

    TrainOnLine Train => _world.Train;
    Branch Spur => Train.Line.Branches[_spur];
    RouteFeature Zone => _world.Run!.Route.Of(FeatureKind.Facility).ElementAt(_facility);

    void Log(DrillStep step)
    {
        Step = step;
        _stillFor = 0;
        _log.Add((step, Math.Round(_seconds, 1)));
    }

    /// <summary>The controls for this tick: call before stepping the world, with the world's tick seconds.</summary>
    public TrainControls Tick(double dt)
    {
        _seconds += dt;
        var engine = Train.Dynamics;
        bool stopped = engine.Speed < 0.02;
        _stillFor = stopped ? _stillFor + dt : 0;
        var g = engine.Tuning.Geometry;
        switch (Step)
        {
            case DrillStep.Approach:
                // Engine's front two metres short of the points' reach.
                if (Stopped(stopped))
                    Log(DrillStep.Cut);
                return Toward(Spur.Toe - _pointsLength - 2, 1);

            case DrillStep.Cut:
                {
                    // Engine and as many cars behind it as the spur takes clear of its points; the rest wait here.
                    int fit = Capacity(g, Spur, _pointsLength);
                    var vehicles = engine.Consist.Vehicles;
                    TookIn = [.. vehicles.Skip(1).Take(fit).Select(v => v.Id)];
                    LeftWaiting = [.. vehicles.Skip(1 + fit).Select(v => v.Id)];
                    if (LeftWaiting.Count > 0)
                    {
                        _waiting = LeftWaiting[0];
                        Train.Uncouple(vehicles[fit].Id);
                    }
                    _world.SetSwitch(_spur, true);
                    Log(DrillStep.SpurIn);
                    return Hold();
                }

            case DrillStep.SpurIn:
                if (engine.Path == _spur && Stopped(stopped))
                    Log(DrillStep.Loading);
                return Toward(Spur.End - 1, 1);

            case DrillStep.Loading:
                if (Loaded)
                    Log(DrillStep.BackOut);
                return Hold();

            case DrillStep.BackOut:
                {
                    // Back until the whole train (the waiting cars coupled on) has its front clear of the points.
                    bool together = _waiting < 0 || engine.Consist.IndexOf(_waiting) >= 0;
                    // Aim a little into the waiting cars, so the rakes touch (at a crawl) rather than stop just short.
                    double target = together ? Spur.Toe - _pointsLength - 2 : WaitingFront() + g.CouplingGap - 0.5;
                    if (together && engine.Path == RailLine.MainPath && Stopped(stopped))
                        Log(DrillStep.Reset);
                    // The last stretch onto the waiting cars at a crawl: they couple below the coupling speed (buckeyes), not collide.
                    bool close = !together && engine.RearDistance - target < 15;
                    return Toward(target, -1, close ? 0.8 : 3, rear: !together);
                }

            case DrillStep.Reset:
                _world.SetSwitch(_spur, false);
                Log(DrillStep.Depart);
                return Hold();

            case DrillStep.Depart:
                if (engine.Distance - engine.Consist.LengthMetres > Zone.End + 20)
                    Log(DrillStep.Done);
                return Toward(Zone.End + 100 + engine.Consist.LengthMetres, 1, 8);

            default:
                return Hold();
        }
    }

    bool Stopped(bool stopped) => stopped && _stillFor >= 1;

    /// <summary>How many cars a spur takes behind the engine, clear of its points with a metre spare at each end.</summary>
    public static int Capacity(GeometryTuning g, Branch spur, double pointsLength)
    {
        double room = spur.Local.Length - 1 - (pointsLength + 1) - g.EngineLength;
        return Math.Max(0, (int)(room / (g.CarLength + g.CouplingGap)));
    }

    double WaitingFront() => Train.RakeOf(_waiting).Distance;

    TrainControls Hold() => new() { Brake = 1, Reverser = Train.Dynamics.Velocity < 0 ? -1 : 1 };

    /// <summary>
    /// Drives the engine's rake to put its front (or with <paramref name="rear"/>, its back) at a distance, at up to
    /// <paramref name="top"/> m/s and gently at the end, like someone on the regulator watching the ground.
    /// </summary>
    TrainControls Toward(double target, int direction, double top = 3, bool rear = false)
    {
        var engine = Train.Dynamics;
        double at = rear ? engine.RearDistance : engine.Distance;
        double left = (target - at) * direction;
        if (left <= 0.05)
            return Hold();
        double brakeRate = Math.Max(0.1, engine.MaxBrakeForce / engine.Consist.MassTonnes * 0.5);
        double wanted = Math.Min(top, Math.Sqrt(2 * brakeRate * left));
        double speed = engine.Velocity * direction;
        return speed < wanted - 0.15
            ? new TrainControls { Throttle = 0.5, Reverser = direction }
            : speed > wanted + 0.15
                ? new TrainControls { Brake = Math.Clamp((speed - wanted) / 2, 0.2, 1), Reverser = direction }
                : new TrainControls { Reverser = direction };
    }
}
