using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Bots;

public sealed partial class ConductorBot
{
    /// <summary>Stood the train for one of the six (note 367), and not yet away again.</summary>
    bool _stoodForSix;
    /// <summary>Seconds stood so, for <c>crewBots.standGiveUp</c>.</summary>
    double _standingForSix;
    /// <summary>The car behind a Knotter's joint (note 365): what's left standing there once it's killed, to couple up to.</summary>
    int _knotRear = -1;
    /// <summary>
    /// The Knotter holding that joint (note 545), and whether it was slack when last seen: gone from a stand, with the cars
    /// left just its gap behind, it was killed. A client's copy of it is dropped, not ended, so its own state can't say.
    /// </summary>
    int _knotId = -1;
    bool _knotSlack;
    /// <summary>A Knotter or a Hotbox stood for <c>standGiveUp</c> and not dealt with (nobody to): gone on from, for good.</summary>
    readonly HashSet<int> _gaveUpOn = [];

    /// <summary>What the driver's doing about the six just now (for tests and the harness's trace), or null.</summary>
    public string? SixStep { get; private set; }

    /// <summary>
    /// The driver's part in answering the six creatures of 8 Oct (note 367). Null when there's nothing of theirs to stand for.
    /// <list type="bullet">
    /// <item>Tower Jaw's wreck across the line ahead (note 363): stopped short of it as for the Track Doll, on the braking curve
    /// to <c>crewBots.wreckStopShort</c> short of its near edge and on the brake from <c>wreckHoldWithin</c> beyond that,
    /// and held there while the crew clears it.</item>
    /// <item>Hotbox glowing or seized (note 367: "hear the knock, find the wheel, stop to pull it"): brought to a stand and held
    /// there, so it unfolds for the crew to prise out, and while the axle it left seized is freed (no longer than
    /// <c>standGiveUp</c>).</item>
    /// <item>The Knotter, once it's forced its gap (note 365: "stop, kill it, couple up"): to a stand, where it slackens and
    /// the crew can kill it; and once it's dead, back onto the cars it left standing, at a crawl so they couple.</item>
    /// </list>
    /// Then everyone aboard (as at a stop) before it goes on. The express driver (note 376) stops only for the wreck, which it
    /// can't run through; nor does a driver mid-way through a stop's moves (its own standing is the stand the crew needs).
    /// </summary>
    PlayerIntent? ForTheSix(in PlayerState self, World world, ref double cruise)
    {
        SixStep = null;
        var train = world.Train;
        var d = train.Dynamics;
        if (!self.Alive || !PlayerMotor.InCab(self, train) || world.Enemies is not { } et)
            return null;
        var b = et.CrewBots;
        var hold = new PlayerIntent { Buttons = PlayerButtons.Brake, ThrottleNotch = -4 };
        bool standing = Math.Abs(d.Velocity) < 0.05;
        if (_stoodForSix && standing)
            _standingForSix += SimConstants.TickSeconds;
        bool givenUp = _standingForSix > b.StandGiveUp;

        // The wreck across the line ahead: short of it, and stood there till it's cleared.
        if (world.Controls.Reverser >= 0 && d.Path == Rail.RailLine.MainPath)
            foreach (var jaw in world.ActiveEnemies.OfType<TowerJaw>().Where(j => !j.Gone).OrderBy(j => j.Id))
            {
                if (!Heed.WreckOnTheLine(train, jaw, et.TowerJaw))
                    continue;
                double ahead = jaw.LineDistance - et.TowerJaw.WreckHalf - d.Distance;
                if (ahead < -et.TowerJaw.WreckHalf)
                    continue;
                cruise = Math.Min(cruise, Math.Sqrt(2 * DollBraking * Math.Max(0, ahead - b.WreckStopShort)));
                if (ahead > b.WreckStopShort + b.WreckHoldWithin)
                    continue;
                _stoodForSix = true;
                SixStep = "short of the wreck";
                return hold;
            }

        // The coupling a Knotter's forced, wherever the driver's got to (note 545: one forced its gap as the train backed off
        // frontier:7's dead line, seed 8, was killed slack at that stand, and the train ran on without the eight cars behind it,
        // the stop driver's moves never having let this see it).
        if (world.ActiveEnemies.OfType<Knotter>().FirstOrDefault(k => !k.Gone && k.Mode is KnotterMode.Force or KnotterMode.Taut
                or KnotterMode.Coil or KnotterMode.Slack && k.Attached > 0 && k.Attached < train.Frames.Count) is { } forced
            && train.VehicleBehind(forced.Attached) is var behind and >= 0)
            (_knotRear, _knotId, _knotSlack) = (behind, forced.Id, forced.Mode == KnotterMode.Slack);

        if (!Express && Stops is not { Doing: not StopDriver.Leg.Cruise })
        {
            var knot = world.ActiveEnemies.OfType<Knotter>().FirstOrDefault(k => !k.Gone && k.Mode is KnotterMode.Force or KnotterMode.Taut
                or KnotterMode.Coil or KnotterMode.Slack && k.Attached > 0 && k.Attached < train.Frames.Count && !_gaveUpOn.Contains(k.Id));
            var box = world.ActiveEnemies.OfType<Hotbox>().FirstOrDefault(h => !h.Gone && h.Mode is HotboxMode.Glow or HotboxMode.Seized
                or HotboxMode.Unfolded && !_gaveUpOn.Contains(h.Id));
            // Nobody's dealt with it in all that time (nobody to: a driver alone): on without it, and not stood for again.
            if (givenUp)
            {
                if (knot is not null)
                    _gaveUpOn.Add(knot.Id);
                if (box is not null)
                    _gaveUpOn.Add(box.Id);
                (knot, box) = (null, null);
            }
            if (knot is not null && train.VehicleBehind(knot.Attached) is var rear and >= 0)
                _knotRear = rear;
            bool hotbox = box is not null;
            // Its axle left seized once it's out: stood for the wrench, a while.
            bool seized = _stoodForSix && !givenUp && d.Consist.Vehicles.Any(v => v.Seized);
            if (knot is not null || hotbox || seized)
            {
                _stoodForSix = true;
                SixStep = knot is not null ? "for the Knotter" : hotbox ? "for the Hotbox" : "for the axle";
                return hold;
            }
        }
        if (!_stoodForSix)
        {
            // Killed while the stop's moves had the train (its stand was theirs): coupled up all the same, then theirs again.
            // Killed, not cut loose: slack at a stand, gone, and the cars still its gap behind with nothing aboard them (a
            // coupling cut for a pack, a Car Hugger or a fire stays cut, and the driver's own cut, note 343, is never undone).
            if (_knotRear >= 0 && !world.ActiveEnemies.Any(e => e.Id == _knotId && !e.Gone))
            {
                var left = train.Rakes.FirstOrDefault(r => r != d && r.Consist.IndexOf(_knotRear) >= 0);
                // Still its gap behind (a stand's jolt as it lets go is waited out); gone further, or coupled, it's done with.
                double apart = left is null ? double.MaxValue : d.RearDistance - left.Distance;
                // Never at a facility's stop: its plan takes the cars left as its cut, and couples back onto them after
                // (StopDriver's BackOut); coupled up under it, its cut is gone and it stands Held with the points set all night.
                bool facility = Stops is
                {
                    Doing: not (StopDriver.Leg.Cruise or StopDriver.Leg.ToSwitch or StopDriver.Leg.OffDeadLine
                    or StopDriver.Leg.SetBack or StopDriver.Leg.Forward)
                };
                // Coupled up onto them (they're the train's again): the reverser forward first, then it's done with.
                if (left is null && _knotSlack && !facility && world.Controls.Reverser < 0 && Recouple(world) is { } forward)
                {
                    SixStep = "coupling up";
                    return forward;
                }
                if (!_knotSlack || _outToCut || facility || left is null || left.Path != d.Path || apart < 0 || apart > et.Knotter.Gap + KnotReach)
                {
                    (_knotRear, _knotId, _knotSlack) = (-1, -1, false);
                    return null;
                }
                // (Not only from a stand: backing onto them is moving, and that's still this.)
                if (!world.ActiveEnemies.Any(e => !e.Gone && e.Attached >= 0 && left.Consist.IndexOf(e.Attached) >= 0)
                    && Recouple(world) is { } backOnto)
                {
                    SixStep = "coupling up";
                    return backOnto;
                }
            }
            return null;
        }
        // Dealt with: everyone aboard first (nobody's left down between the cars it's about to back onto).
        if (Crewmates?.Any(c => c.Alive && (c.Parent == PlayerState.World || c.Surface == Surface.Ladder)) == true
            && _standingForSix <= b.StandGiveUp + AllAboardSeconds)
        {
            SixStep = "all aboard";
            return hold;
        }
        if (_knotRear >= 0 && Recouple(world) is { } coupling)
        {
            SixStep = "coupling up";
            return coupling;
        }
        _stoodForSix = false;
        _standingForSix = 0;
        (_knotRear, _knotId, _knotSlack) = (-1, -1, false);
        return null;
    }

    /// <summary>
    /// Back onto the cars a Knotter's death left standing behind the train (note 365: the knot's metres behind it, their
    /// brakes on), at half the couplers' <c>coupleMaxSpeed</c>, aimed a little into them so the rakes touch and couple (as
    /// the stop's set-back onto its cut, <see cref="StopDriver"/>); coupled, the reverser forward again. Null once it's
    /// running forward with them, or if they're gone (rolled off, or on another path).
    /// </summary>
    PlayerIntent? Recouple(World world)
    {
        var d = world.Train.Dynamics;
        if (LeftStanding(world) is not { } left)
            return world.Controls.Reverser < 0 ? StopDriver.Toward(world, d.Distance + 1000, +1, SetBackSpeed) : null;
        return StopDriver.Toward(world, left.Distance + d.Tuning.Geometry.CouplingGap - 0.5, -1, d.Tuning.Couplings.CoupleMaxSpeed / 2, rear: true);
    }

    /// <summary>m past a Knotter's gap the cars it left can stand and still be its (they settle a little as it lets go).</summary>
    const double KnotReach = 1;

    /// <summary>The cars a Knotter's death left standing behind the train, on its path, to back onto; null if there are none.</summary>
    Train.TrainDynamics? LeftStanding(World world)
    {
        var d = world.Train.Dynamics;
        var left = world.Train.Rakes.FirstOrDefault(r => r != d && r.Consist.IndexOf(_knotRear) >= 0);
        return left is null || Math.Abs(left.Velocity) > 0.05 || left.Path != d.Path || left.Distance > d.RearDistance ? null : left;
    }
}
