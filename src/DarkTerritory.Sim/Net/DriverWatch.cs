using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Net;

/// <summary>
/// Note 935 (the director's playtest of 20 Oct evening, lines 1, 14, 18): what the bot at the controls did with the gauge,
/// the stops and the crew, for a night's report. Seconds the gauge was in the red (boiler.json redline) and over the working
/// band, its highest, the ruptures; each stop's run in and how far past its hold the engine went; and each crewmate left on
/// the ground by a train going faster than anyone runs, further than <see cref="LeftBehindAt"/> from it. Note 1009 (the
/// director's playtest of 23 Oct, line 11: "Why did we just stop? We just stopped on a bend"): every stand of the night's run
/// outside a stop's legs (<see cref="DriverWatch.StandAfter"/> or longer, the train under way), with what the driver stood it
/// for (<see cref="ConductorBot.Why"/>), how long, and the bend it stood on; and the seconds of them.
/// </summary>
public sealed record DriverReport(double RedSeconds, double OverBandSeconds, double MaxPressure, int Ruptures, int Stops, int Overshoots,
    double MaxOvershoot, int LeftBehind, IReadOnlyList<string> Left, int Stands = 0, double StandSeconds = 0, IReadOnlyList<string>? StandsWhy = null);

/// <summary>Watches the host's world each tick for a <see cref="DriverReport"/> (note 935).</summary>
public sealed class DriverWatch
{
    /// <summary>A crewmate on the ground this far (m) from the train's nearest end, with it going faster than a run, is left behind.</summary>
    public const double LeftBehindAt = 30;
    /// <summary>Past a stop's hold by more than this (m), a run in has overshot it (StopDriver's own margin).</summary>
    const double OverBy = StopDriver.HoldOver;

    int _redTicks, _overBandTicks, _ruptures, _stops, _overshoots;
    double _maxPressure, _maxOvershoot;
    bool _wasRuptured;
    double? _hold;
    double _past;
    readonly HashSet<int> _behind = [];
    readonly List<string> _left = [];
    /// <summary>A stand this long (s) or longer counts (note 1009): not a stop's own legs, nor a moment's stand reversing.</summary>
    public const double StandAfter = 5;
    int _standTicks;
    double _standAt, _standBend;
    readonly Dictionary<string, int> _standWhy = [];
    readonly List<string> _stands = [];
    double _standSeconds;

    /// <param name="crew">Everyone aboard by player id (bots and people playing alike).</param>
    /// <param name="driver">The bot at the controls, if one is (its stops).</param>
    public void Watch(World world, IEnumerable<(int Id, PlayerState State)> crew, ConductorBot? driver)
    {
        var train = world.Train;
        var d = train.Dynamics;
        if (train.BoilerTuning is { } bt)
        {
            double p = train.Boiler.Pressure;
            _maxPressure = Math.Max(_maxPressure, p);
            if (p >= bt.Redline)
                _redTicks++;
            if (p > bt.WorkingBandMax)
                _overBandTicks++;
            if (train.Boiler.Ruptured && !_wasRuptured)
                _ruptures++;
            _wasRuptured = train.Boiler.Ruptured;
        }
        // The run in to a stop: from the approach to its stand, how far past the hold it went.
        double? hold = driver?.Stops is { } s && s.Doing == StopDriver.Leg.Approach && s.Plan is { } plan ? plan.Hold : null;
        if (hold is { } h)
        {
            if (_hold != h)
            {
                _stops++;
                _past = 0;
            }
            if (d.Path == Rail.RailLine.MainPath)
                _past = Math.Max(_past, d.Distance - h);
        }
        else if (_hold is not null)
            Ran();
        _hold = hold;
        // Note 1009: stood outside a stop's legs, the run under way, and why.
        if (world.Run is { Phase: Run.RunPhase.Underway } && driver is { Driving: true } && d.Speed < 0.05
            && driver.Stops is not { Doing: not StopDriver.Leg.Cruise })
        {
            if (_standTicks++ == 0)
            {
                _standAt = d.Distance;
                _standBend = d.Path == Rail.RailLine.MainPath ? Math.Abs(train.Line.Sample(d.Path, d.Distance).Curvature) : 0;
            }
            _standWhy[driver.Why] = _standWhy.GetValueOrDefault(driver.Why) + 1;
        }
        else
            Stood(world);
        bool fast = !SpeedBands.CanBeCaughtOnFoot(d.Tuning, d.Speed);
        foreach (var (id, c) in crew)
        {
            if (!c.Alive || c.Parent != PlayerState.World || train.OnHandcar(c.Parent))
            {
                _behind.Remove(id);
                continue;
            }
            if (!fast || _behind.Contains(id))
                continue;
            double along = ConductorBot.AlongTheTrain(train, PlayerMotor.WorldPosition(c, train));
            double off = along < d.RearDistance ? d.RearDistance - along : Math.Max(0, along - d.Distance);
            if (off > LeftBehindAt)
            {
                _behind.Add(id);
                _left.Add($"#{id} at {world.Tick * SimConstants.TickSeconds:0}s, {d.Distance / 1000:0.00} km, {off:0} m off");
            }
        }
    }

    void Stood(World world)
    {
        double seconds = _standTicks * SimConstants.TickSeconds;
        if (seconds >= StandAfter)
        {
            string why = string.Join(", ", _standWhy.OrderByDescending(w => w.Value).ThenBy(w => w.Key, StringComparer.Ordinal)
                .Select(w => $"{(w.Key.Length > 0 ? w.Key : "?")} {w.Value * SimConstants.TickSeconds:0}s"));
            string bend = _standBend > 1e-6 ? $", on a bend of R {1 / _standBend:0} m" : "";
            _stands.Add($"{world.Tick * SimConstants.TickSeconds - seconds:0}s at {_standAt / 1000:0.00} km, {seconds:0} s{bend}: {why}");
            _standSeconds += seconds;
        }
        _standTicks = 0;
        _standWhy.Clear();
    }

    void Ran()
    {
        _maxOvershoot = Math.Max(_maxOvershoot, _past);
        if (_past > OverBy)
            _overshoots++;
    }

    public DriverReport Report(World? world = null)
    {
        if (_hold is not null)
            Ran();
        if (world is not null)
            Stood(world);
        return new DriverReport(Math.Round(_redTicks * SimConstants.TickSeconds, 1), Math.Round(_overBandTicks * SimConstants.TickSeconds, 1),
            Math.Round(_maxPressure, 1), _ruptures, _stops, _overshoots, Math.Round(_maxOvershoot, 1), _left.Count, _left,
            _stands.Count, Math.Round(_standSeconds, 1), [.. _stands]);
    }
}
