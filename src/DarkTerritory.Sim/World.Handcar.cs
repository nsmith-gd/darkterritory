using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim;

/// <summary>
/// The handcar, the crew's side (GDD §24.1; the director, 14 Oct 2026, App. F.6; ARCHITECTURE §8 note 729): taking its handle,
/// pumping it in time, its brake lever, and lifting it off the van onto the rails and back. The trolley's own running is
/// <see cref="TrainOnLine"/>'s (<see cref="Handcar"/>).
/// </summary>
public sealed partial class World
{
    // This tick's, gathered on the host as each crewmate acts and spent by StepHandcar before the train's step.
    readonly List<double> _handcarEfforts = new();
    bool _handcarBrake;
    int _handcarLifters;

    /// <summary>The end of the handcar's handle a rider stands at to take it (note 729): −1 the front's, +1 the back's, or null.</summary>
    public int? AtHandle(in PlayerState s)
    {
        if (Train.Handcar is null || !Train.OnHandcar(s.Parent) || !s.Grounded)
            return null;
        var t = Train.Dynamics.Tuning.Handcar;
        int end = Handcar.EndAt(s.Position.Z);
        var place = Handcar.Place(t, end);
        double dx = s.Position.X - place.X, dz = s.Position.Z - place.Z;
        return dx * dx + dz * dz <= t.HandleReach * t.HandleReach ? end : null;
    }

    /// <summary>A crewmate near enough the handcar to put their hands to lifting it (note 729), and not stood on it.</summary>
    public bool CanLiftHandcar(in PlayerState s)
    {
        if (Train.Handcar is not { } h || !s.Alive || s.Has(PlayerFlags.Held) || s.Parent == h.Id)
            return false;
        var at = PlayerMotor.WorldPosition(s, Train);
        var car = Train.Frames[h.Id].Origin;
        double dx = at.X - car.X, dz = at.Z - car.Z;
        double reach = Train.Dynamics.Tuning.Handcar.LiftReach + (h.Carried ? 0 : Train.Dynamics.Tuning.Handcar.Length / 2);
        return dx * dx + dz * dz <= reach * reach;
    }

    /// <summary>
    /// One crewmate's hands on the handcar this tick (note 729), from <see cref="CrewAct"/>. At an end of the handle, a press of
    /// Use (the seat's press, as at the crane) takes it and the next lets it go; stood there, the stick's forward and back are
    /// the stroke. Worked out alike everywhere, so a client predicts standing at the handle. The host gathers the strokes, the
    /// brake lever (the brake key, anyone aboard it) and the lifters (Use held beside it, not on it, hands free).
    /// </summary>
    void HandcarAct(ref PlayerState s, in PlayerIntent intent, int playerId, bool handsTookIt)
    {
        if (Train.Handcar is not { } h)
        {
            s.Flags &= ~PlayerFlags.Pumping;
            return;
        }
        var t = Train.Dynamics.Tuning.Handcar;
        bool riding = s.Alive && Train.OnHandcar(s.Parent) && s.Grounded && !s.Has(PlayerFlags.Held);
        bool pumping = riding && s.Has(PlayerFlags.Pumping);
        if (riding && intent.Has(PlayerActions.Seat))
        {
            if (pumping)
                pumping = false;
            else if (AtHandle(s) is { } end)
            {
                // Stood at the end of the handle, facing it (the front end's pumper faces back down the car).
                pumping = true;
                s.Position = Handcar.Place(t, end);
                s.Velocity = default;
                s.Yaw = end < 0 ? Math.PI : 0;
            }
        }
        s.Flags = pumping ? s.Flags | PlayerFlags.Pumping : s.Flags & ~PlayerFlags.Pumping;
        if (!Authority)
            return;
        if (pumping)
            _handcarEfforts.Add(Handcar.Effort(t, h.Phase, Handcar.EndAt(s.Position.Z), intent.MoveZ));
        if (riding && intent.Has(PlayerButtons.Brake))
            _handcarBrake = true;
        if (!handsTookIt && intent.Has(PlayerButtons.Use) && CanLiftHandcar(s))
            _handcarLifters++;
    }

    /// <summary>
    /// The host's handcar for this tick (note 729), before the train's step: the pumpers' drive and the brake lever as the crew
    /// left them, and a lift's progress. Two lifters set it on the rails (or put it back) in handcar.liftSeconds, one alone in
    /// soloLiftScale times that; a lift that can't go (the train moving, no room, someone aboard it) makes no headway.
    /// </summary>
    void StepHandcar()
    {
        if (Train.Handcar is not { } h)
            return;
        var t = Train.Dynamics.Tuning.Handcar;
        double speed = Train.HandcarRake?.Velocity ?? 0;
        h.Pumpers = _handcarEfforts.Count;
        h.Drive = h.Carried ? 0 : Handcar.DriveFrom(t, speed, _handcarEfforts);
        bool aboard = _actors.Any(a => a.State.Alive && a.State.Parent == h.Id);
        // The lever's on while someone's on it, and latched on when it's left (handcar.parked): nobody's trolley rolls off
        // down a bank on its own.
        h.Brake = h.Carried ? 0 : _handcarBrake || !aboard && t.Parked ? 1 : 0;
        if (_handcarLifters > 0 && !aboard && Train.HandcarCanGo())
        {
            h.Lift += SimConstants.TickSeconds / Math.Max(0.1, t.LiftSeconds * (_handcarLifters >= 2 ? 1 : Math.Max(1, t.SoloLiftScale)));
            if (h.Lift >= 1)
            {
                h.Lift = 0;
                if (h.Carried)
                    Train.SetHandcarOnRails();
                else
                    Train.StowHandcar();
            }
        }
        else
            h.Lift = 0;
        _handcarEfforts.Clear();
        _handcarBrake = false;
        _handcarLifters = 0;
    }
}
