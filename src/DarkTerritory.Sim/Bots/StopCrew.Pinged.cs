using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Bots;

/// <summary>
/// Note 936 (the director's playtest of 20 Oct, one bot crewmate, the driver: "I wish I could just click middle mouse button
/// and like the bot would get what I want just based on what I'm pointing at"): a pinged job worked by a hand that isn't
/// one of the stop's own at it: the driver, the crew's only pair of hands, sent out of the cab by a ping.
/// </summary>
public sealed partial class StopHand
{
    /// <summary>
    /// The stop the train's standing at as a plan of its own, for a hand sent to work it by a ping (the driver needn't have
    /// made it for its loading): the site and its spur, the hold short of the points. Kept while it's the same stop, as the
    /// village's searching keys on it.
    /// </summary>
    StopPlan? _pingedAt;

    /// <summary>Working a ping at a stop the train stands at anywhere (not only at the end of its spur): <see cref="Carry"/>'s.</summary>
    bool _orderedHere;

    /// <summary>The plan of the stop the train's standing at, with a spur (<see cref="_pingedAt"/>); null away from one.</summary>
    StopPlan? PingedAt(World world)
    {
        var train = world.Train;
        if (world.Run?.CurrentSite is not { Spur: >= 0 } site || site.Spur >= train.Line.Branches.Count)
            return _pingedAt = null;
        if (_pingedAt?.Site != site)
        {
            var spur = train.Line.Branches[site.Spur];
            _pingedAt = new StopPlan(site.Index, site, spur, spur.Toe - (world.Switches?.Tuning.PointsLength ?? 12) - 2, 0, -1);
        }
        return _pingedAt;
    }

    /// <summary>
    /// This tick's intent for a pinged job (<see cref="CrewCalls.OrderOf"/>), the train standing: the points' lever thrown;
    /// the crane's controls (the power restarted first) or its rigging; the town's houses searched, a find fetched, or the
    /// stop's cargo carried in (to the car pinged, if one was), all by the crate hands' own way (<see cref="Carry"/>), the
    /// finds brought aboard as theirs are. Null when there's nothing for it to do (the job's done, or not one it can do here).
    /// </summary>
    public PlayerIntent? Ordered(in PlayerState self, World world, PingKind kind, int target)
    {
        var train = world.Train;
        Doing = "";
        if (!self.Alive)
            return null;
        if (self.Surface == Surface.Air)
            return new PlayerIntent();
        if (kind == PingKind.Points)
        {
            if (target < 0 || target >= train.Line.Branches.Count)
                return null;
            var throwing = Throw(self, train, train.Line.Branches[target]);
            Doing = "throwing the points";
            return throwing;
        }
        if (kind is not (PingKind.Crane or PingKind.Town or PingKind.Loot or PingKind.Cargo) || PingedAt(world) is not { } p)
            return null;
        _plan = p;
        _wentIn = _reachedEnd = true;
        if (kind == PingKind.Crane)
            return OrderedCrane(world, p) is ({ } crane, var part)
                ? part == StopJob.Winch0 ? PowerUp(self, world, p) ?? Operate(self, world, p, crane) : RigCasting(self, world, p, crane)
                : null;
        _orderedHere = true;
        try
        {
            return Lend(self, world, p, StopJob.Crates);
        }
        finally
        {
            _orderedHere = false;
        }
    }

    /// <summary>
    /// Note 936: the cargo car this hand's been pinged to load (the director: "load that cargo into the car that's open"), with
    /// room and a door on the stop's side; null with none.
    /// </summary>
    int? OrderedCar(World world, StopPlan p)
    {
        var train = world.Train;
        if (PlayerId is not { } me || calls.OrderOf(me) is not { Kind: PingKind.Cargo, Target: > 0 and var car } || car >= train.Frames.Count
            || train.Vehicles[car] is not { Kind: VehicleKind.Cargo } v || v.Load >= 1 - 1e-6 || SideDoor(train.Frames[car].Shape, p.Site.Side) is null)
            return null;
        return car;
    }
}
