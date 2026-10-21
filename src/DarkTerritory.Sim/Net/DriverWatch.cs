using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Net;

/// <summary>
/// Note 935 (the director's playtest of 20 Oct evening, lines 1, 14, 18): what the bot at the controls did with the gauge,
/// the stops and the crew, for a night's report. Seconds the gauge was in the red (boiler.json redline) and over the working
/// band, its highest, the ruptures; each stop's run in and how far past its hold the engine went; and each crewmate left on
/// the ground by a train going faster than anyone runs, further than <see cref="LeftBehindAt"/> from it.
/// </summary>
public sealed record DriverReport(double RedSeconds, double OverBandSeconds, double MaxPressure, int Ruptures, int Stops, int Overshoots,
    double MaxOvershoot, int LeftBehind, IReadOnlyList<string> Left);

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

    void Ran()
    {
        _maxOvershoot = Math.Max(_maxOvershoot, _past);
        if (_past > OverBy)
            _overshoots++;
    }

    public DriverReport Report()
    {
        if (_hold is not null)
            Ran();
        return new DriverReport(Math.Round(_redTicks * SimConstants.TickSeconds, 1), Math.Round(_overBandTicks * SimConstants.TickSeconds, 1),
            Math.Round(_maxPressure, 1), _ruptures, _stops, _overshoots, Math.Round(_maxOvershoot, 1), _left.Count, _left);
    }
}
