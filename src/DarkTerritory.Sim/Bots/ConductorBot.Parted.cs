using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Bots;

public sealed partial class ConductorBot
{
    /// <summary>A car of the rake a dropped pin last parted from the train (note 653), to go for; −1 when there's none.</summary>
    int _pinCut = -1;
    /// <summary>The engine's rake as last seen, front to back, and which of its cars had the pin behind them loose then.</summary>
    int[] _rakeWas = [];
    readonly HashSet<int> _looseWas = [];
    /// <summary>Backing onto a cut left behind: once it's coupled, the reverser forward again.</summary>
    bool _pinBacking;

    /// <summary>How far the engine draws clear of cars a pin's parted from it, still touching, before closing in to couple (m).</summary>
    const double DropBack = 2;

    /// <summary>What the driver's doing about a parted pin just now (for tests and traces), or null.</summary>
    public string? PinStep { get; private set; }

    /// <summary>
    /// Note 653 (queue #383, D1's ask after #371): the cars a dropped pin parted from the train (note 356) are gone for and
    /// coupled back on. On note 634's seed 7 the pin behind a switchyard's pair, carried ahead of the engine (note 187),
    /// dropped at 14.5 m/s; the pair ran on ahead and nobody went after them, and they were lost at the night's end.
    /// <list type="bullet">
    /// <item>Ahead of the engine: after them, at the night's cruise, closing on them at what stops the engine at them over
    /// their own speed (<see cref="StopDriver.Toward"/>), aimed a little into them so they couple.</item>
    /// <item>Behind: to a stand, back onto them at the set-back speed as for the Knotter's (note 365), and the reverser
    /// forward again. Not if they're rolling back faster than a cut that's lost (note 261), further back than the driver sets
    /// back for anyone, or with dawn too near for the run back and in again (note 615).</item>
    /// </list>
    /// Only on the open line: a stop's moves have the train till they're done, and it's gone for after. Not the express
    /// driver (note 376), who takes no stops.
    /// </summary>
    /// <summary>
    /// A car of a rake a dropped pin has parted from the engine's since last seen, or −1. Told from what every client has
    /// (the host's own record of it isn't the clients', and the bots drive from theirs): the train come apart behind a car whose
    /// pin was loose (note 356; the host lets a pin go loose till it drops). Not a cut the crew made (the knife, note 343) nor
    /// a Knotter's (its own stand and couple-up, note 365): their couplings weren't loose.
    /// </summary>
    int PinDropped(TrainOnLine train)
    {
        var d = train.Dynamics;
        int cut = -1;
        for (int k = 0; k + 1 < _rakeWas.Length && cut < 0; k++)
        {
            int ahead = _rakeWas[k], behind = _rakeWas[k + 1];
            if (_looseWas.Contains(ahead) && train.RakeOf(ahead) != train.RakeOf(behind))
                cut = train.RakeOf(ahead) == d ? behind : ahead;
        }
        _rakeWas = [.. d.Consist.Vehicles.Select(v => v.Id)];
        _looseWas.Clear();
        foreach (var v in d.Consist.Vehicles)
            if (v.Loose > 0)
                _looseWas.Add(v.Id);
        return cut;
    }

    PlayerIntent? ForTheParted(in PlayerState self, World world, double cruise)
    {
        PinStep = null;
        var train = world.Train;
        var d = train.Dynamics;
        if (PinDropped(train) is var dropped and >= 0)
            (_pinCut, _pinBacking) = (dropped, false);
        if (_pinCut < 0 || Express || !self.Alive)
            return null;
        if (!PlayerMotor.InCab(self, train) || Stops is { Doing: not StopDriver.Leg.Cruise })
            return null;
        var cut = train.Rakes.FirstOrDefault(r => r != d && r.Consist.IndexOf(_pinCut) >= 0);
        if (cut is null)
        {
            // Coupled up (they're the train's again): forward again after backing onto them, then done with.
            if (_pinBacking && world.Controls.Reverser < 0)
            {
                PinStep = "coupling up";
                return StopDriver.Toward(world, d.Distance + 1000, +1, SetBackSpeed);
            }
            (_pinCut, _pinBacking) = (-1, false);
            return null;
        }
        var g = d.Tuning.Geometry;
        if (cut.Path != d.Path)
            return GiveUp();
        if (cut.Distance > d.Distance)
        {
            PinStep = "after the cars ahead";
            // Parted, the engine's coupler won't take them till it's been clear of them (TrainOnLine's lock on a cut): run
            // up on them still locked and it only pushes them along. So it drops back a little first, then closes in.
            double onto = d.FrontCouplerLocked ? cut.RearDistance - g.CouplingGap - DropBack : cut.RearDistance - g.CouplingGap + 0.5;
            return StopDriver.Toward(world, onto, +1, Math.Max(cruise, SetBackSpeed), away: cut.Velocity);
        }
        double apart = d.RearDistance - cut.Distance;
        double pace = world.LampShining ? CruiseSpeed : DarkCruiseSpeed;
        if (cut.Velocity < -StopDriver.SetBackTop || apart > SetBackFor
            || world.Enemies is { } et && DawnInHand(world, et, 2 * Math.Max(0, apart) / SetBackSpeed, pace) <= 0)
            return GiveUp();
        PinStep = "back for the cars behind";
        // Still rolling on after the train: stood till they've stopped or caught it up, never backed into them on the move.
        if (cut.Velocity > 0.05)
            return new PlayerIntent { Buttons = PlayerButtons.Brake, ThrottleNotch = -4 };
        // Run up against the train's tail as it stood, they're touching but not coupled (their coupler locked as they
        // parted): drawn forward clear of them first, or backing only shoves them away down the line.
        if (cut.FrontCouplerLocked)
            return StopDriver.Toward(world, cut.Distance + g.CouplingGap + DropBack, +1, SetBackSpeed, rear: true);
        _pinBacking = true;
        double rolling = Math.Max(0, -cut.Velocity);
        return StopDriver.Toward(world, cut.Distance + g.CouplingGap - 0.5, -1,
            d.Tuning.Couplings.CoupleMaxSpeed / 2 + rolling, rear: true, away: rolling);

        PlayerIntent? GiveUp()
        {
            bool backing = _pinBacking && world.Controls.Reverser < 0;
            (_pinCut, _pinBacking) = (-1, false);
            if (!backing)
                return null;
            // Gone on without them: forward again first.
            PinStep = "on without them";
            return StopDriver.Toward(world, d.Distance + 1000, +1, SetBackSpeed);
        }
    }
}
