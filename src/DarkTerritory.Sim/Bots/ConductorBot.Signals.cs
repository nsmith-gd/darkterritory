using Ballast;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Bots;

public sealed partial class ConductorBot
{
    /// <summary>The lamp signal the driver's working to (note 720), and how long it's been at it.</summary>
    LampSignal _obeying;
    double _obeyedFor;
    /// <summary>The last signal read (its record's id and tick), so one shown for three seconds is read once.</summary>
    int _signalRead = -1;
    uint _signalReadAt;
    /// <summary>Where the engine was when it began setting back for a BACK UP (NaN until the reverser's over).</summary>
    double _backFrom = double.NaN;

    /// <summary>The lamp signal the driver's working to now (for tests and the harness's trace).</summary>
    public LampSignal Obeying => _obeying;

    /// <summary>
    /// Lamp signals (GDD §12; the director's calls of 14 Oct 2026, friendslop.md queue #454; note 720): a crewmate's hand lamp
    /// swung across, raised and lowered, or circled, where the driver can see it, within <see cref="LampSignalTuning.SeenWithin"/>
    /// of the cab or of the van's rear. STOP: brake to a stand, and stand (up to <see cref="LampSignalTuning.StandSeconds"/>,
    /// or till GO). GO: off the brake and on, the reverser put forward first if it's back. BACK UP: to a stand, the reverser
    /// back, and set back <see cref="LampSignalTuning.BackMetres"/> at <see cref="LampSignalTuning.BackSpeed"/>, then stand.
    /// It also lets someone with no microphone drive the crew. Null when there's no signal being worked to.
    /// </summary>
    PlayerIntent? Signalled(World world)
    {
        var t = world.VoicesTuning.LampSignals;
        var d = world.Train.Dynamics;
        // The newest signal shown that it hasn't read, and can see.
        SignalEvent? seen = null;
        foreach (var e in world.Signals)
            if ((e.Tick > _signalReadAt || e.Tick == _signalReadAt && e.Id != _signalRead) && Sees(world, e, t)
                && (seen is null || e.Tick > seen.Value.Tick || e.Tick == seen.Value.Tick && e.Id > seen.Value.Id))
                seen = e;
        if (seen is { } read)
        {
            (_signalRead, _signalReadAt) = (read.Id, read.Tick);
            (_obeying, _obeyedFor, _backFrom) = (read.Kind, 0, double.NaN);
        }
        _obeyedFor += SimConstants.TickSeconds;
        var stand = new PlayerIntent { Buttons = PlayerButtons.Brake, ThrottleNotch = -4 };
        switch (_obeying)
        {
            case LampSignal.Stop:
                if (_obeyedFor <= t.StandSeconds)
                    return stand;
                _obeying = LampSignal.None;
                return null;
            case LampSignal.Back:
                // Stood long enough after it with nothing more said: forward again (as GO).
                if (_obeyedFor > t.StandSeconds)
                {
                    _obeying = LampSignal.Go;
                    return Signalled(world);
                }
                if (world.Controls.Reverser < 0 && double.IsNaN(_backFrom))
                    _backFrom = d.Distance;
                return StopDriver.Toward(world, (double.IsNaN(_backFrom) ? d.Distance : _backFrom) - t.BackMetres, -1, t.BackSpeed);
            case LampSignal.Go:
                // Set back, the reverser's put forward again at a stand; then the drive's own.
                if (world.Controls.Reverser < 0)
                    return StopDriver.Toward(world, d.Distance + 1000, +1, t.BackSpeed);
                _obeying = LampSignal.None;
                return null;
            default:
                return null;
        }
    }

    /// <summary>
    /// Whether the driver can see <paramref name="e"/>: its signaller (the crew this bot's client sees) within
    /// <see cref="LampSignalTuning.SeenWithin"/> of the cab or of the last car's rear end.
    /// </summary>
    bool Sees(World world, SignalEvent e, LampSignalTuning t)
    {
        var train = world.Train;
        int at = -1;
        for (int i = 0; i < Crew.Count; i++)
            if (Crew[i].Id == e.By)
                at = i;
        if (at < 0 || !Crew[at].State.Alive || train.Frames.Count == 0)
            return false;
        var where = PlayerMotor.WorldPosition(Crew[at].State, train);
        var engine = train.Frames[0];
        var cab = engine.ToWorld(engine.Shape.Cab?.Centre ?? default);
        int last = train.Dynamics.Consist.Vehicles[^1].Id;
        var rear = last < train.Frames.Count ? train.Frames[last].ToWorld(new Double3(0, 1.5, train.Frames[last].Shape.Bounds.Max.Z)) : cab;
        return (where - cab).Length <= t.SeenWithin || (where - rear).Length <= t.SeenWithin;
    }
}
