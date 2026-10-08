using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Bots;

/// <summary>
/// A crate hand's errand to the village (GDD App. F.3: "crews decide whether to split up for more loot, or work the yard
/// together, or the village together, which takes more time"; ARCHITECTURE §8 note 326). Its crates done and the dawn far
/// enough off, a hand goes to the nearest open house's hiding spot nobody else has claimed, by a way round the walls and the
/// train (<see cref="FootPath"/>), searches it (Use held, as anyone does), picks up what comes out and carries it back to a
/// cargo car to stow, as a crate. Up to <c>crew.villageShare</c> of the crate hands go at once; the rest shut the doors.
/// The driver doesn't call the loading done while anyone's out (<see cref="CrewCalls.Out"/>), and anyone out comes back the
/// moment it's called (late, or given up). All through intent: it walks, holds Use, presses Use.
/// </summary>
public sealed partial class StopHand
{
    HidingSpot? _spot;
    List<Double3>? _path;
    int _pathAt, _stuckTicks, _stopIndex = -1;
    Double3 _lastAt, _pathGoal;
    readonly HashSet<int> _cantReach = [];
    StopPlan? _villageOf;

    /// <summary>What it's searching (for tests and traces), if it's out at a house.</summary>
    public HidingSpot? Searching => _spot;

    /// <summary>The village's numbers (facilities.json <c>crew</c>).</summary>
    static StopCrewTuning Crew(World world) => world.Run?.FacilityTuning?.Crew ?? new StopCrewTuning();

    /// <summary>
    /// Out to the village, its crates done (from <see cref="Carry"/>): the next spot to search, the way there, the search,
    /// and the find picked up. Null when there's nothing for it there (the doors and aboard, as before).
    /// </summary>
    PlayerIntent? Village(in PlayerState self, World world, StopPlan p)
    {
        if (world.Run is not { } run || run.HidingSpots.Count == 0 || calls.Leaving || PlayerId is null)
            return GiveUpVillage(self, world, p);
        var t = Crew(world);
        if (!ReferenceEquals(_villageOf, p))
        {
            _villageOf = p;
            _cantReach.Clear();
            _stopIndex = run.Stops.ToList().IndexOf(p.Site.Feature);
        }
        if (_stopIndex < 0 || t.VillageShare <= 0)
            return GiveUpVillage(self, world, p);
        var train = world.Train;
        var here = PlayerMotor.WorldPosition(self, train);
        // Off the car it's just stowed a find in (or anywhere on the train) before it goes: out of the car by its door, or
        // down off the roof.
        if (self.Parent != PlayerState.World)
            return _spot is not null || Worth(world, run, here) ? OffTheCar(self, world, p) ?? GetDown(self, train, p.Site.Side) : null;
        if (_spot is null)
        {
            // Not with the dawn close, and not more than our share of the hands at once.
            if (run.DawnIn < t.VillageDawnSpare || calls.OutCount(member) >= Math.Max(1, (int)Math.Ceiling(calls.CrateHands * t.VillageShare)))
                return GiveUpVillage(self, world, p);
            var next = run.HidingSpots
                .Where(h => h.Stop == _stopIndex && !run.Searched(h.Stop, h.Container.Index) && !_cantReach.Contains(h.Key) && !calls.SpotClaimed(h.Key, member))
                .OrderBy(h => (Flat(h.At) - Flat(here)).Length).ThenBy(h => h.Key).FirstOrDefault();
            if (next.Seconds <= 0)
                return GiveUpVillage(self, world, p);
            if ((Flat(next.At) - Flat(here)).Length > t.VillageReach)
                return GiveUpVillage(self, world, p);
            var path = FootPath.Plan(train, here, Front(next));
            if (path is null || FootPath.Length(here, path) > t.VillageReach * 1.4)
            {
                // Nowhere it can walk to from here (round the far side of a lake, say): try the next one next tick.
                _cantReach.Add(next.Key);
                Doing = "looking for a way to the village";
                return new PlayerIntent();
            }
            _spot = next;
            _path = path;
            _pathAt = 0;
            calls.ClaimSpot(member, next.Key);
            calls.Out(member, true);
        }
        var spot = _spot.Value;
        // Searched (by us or anyone): pick up what came out, if anything did.
        if (run.Searched(spot.Stop, spot.Container.Index))
        {
            var find = world.Bodies.All.FirstOrDefault(b => b.Kind == Physics.BodyKind.Loot && b.Owner == spot.Key && b.Carrier < 0);
            if (find is null || (Flat(Physics.Bodies.WorldCentre(find, train)) - Flat(here)).Length > 1.6)
            {
                // Nothing there (or someone's taken it): on to the next.
                ReleaseSpot();
                return new PlayerIntent();
            }
            var at = Physics.Bodies.WorldCentre(find, train);
            double yaw = DMath.Atan2(-(at.X - here.X), -(at.Z - here.Z));
            if (!Aligned(self, yaw))
                return new PlayerIntent { LookYaw = Turn(self, yaw) };
            Doing = "picking up the find";
            return Press();
        }
        // There: face the spot and hold Use until it's searched.
        var front = Front(spot);
        if ((Flat(here) - Flat(front)).Length < 0.45)
        {
            double yaw = DMath.Atan2(-(spot.At.X - here.X), -(spot.At.Z - here.Z));
            if (!Aligned(self, yaw))
                return new PlayerIntent { LookYaw = Turn(self, yaw) };
            Doing = $"searching the {spot.Container.Kind.ToString().ToLowerInvariant()}";
            _pressed = false;
            return new PlayerIntent { Buttons = PlayerButtons.Use };
        }
        Doing = "to a house";
        return Follow(self, train, front);
    }

    /// <summary>Whether there's a spot at this stop still to search, within reach, that nobody's taken and it hasn't failed to reach.</summary>
    bool Worth(World world, Run.Run run, Double3 here)
    {
        var t = Crew(world);
        return run.DawnIn >= t.VillageDawnSpare && calls.OutCount(member) < Math.Max(1, (int)Math.Ceiling(calls.CrateHands * t.VillageShare))
            && run.HidingSpots.Any(h => h.Stop == _stopIndex && !run.Searched(h.Stop, h.Container.Index) && !_cantReach.Contains(h.Key)
                && !calls.SpotClaimed(h.Key, member) && (Flat(h.At) - Flat(here)).Length <= t.VillageReach);
    }

    /// <summary>
    /// Back to the train from the houses (<see cref="Carry"/>, with a find in our arms; or called back empty-handed): by a way
    /// round them, to the foot of a cargo car's steps, where the crate hand's own way takes over. Null once it's near enough.
    /// </summary>
    PlayerIntent? FindHome(in PlayerState self, World world, StopPlan p)
    {
        var train = world.Train;
        if (_car < 0 || _car >= train.Frames.Count)
            _car = NearestCargo(train, PlayerMotor.WorldPosition(self, train));
        if (_car < 0)
            return null;
        var frame = train.Frames[_car];
        var layout = train.Dynamics.Tuning.Geometry.Interior!;
        int side = p.Site.Side;
        double w = frame.Shape.Bounds.Max.X, sd = layout.SideDoorWidth / 2;
        var foot = frame.ToWorld(new Double3(side * (w + layout.StepWidth / 2), 0, -sd - 4 * layout.StepDepth - 0.4));
        var here = PlayerMotor.WorldPosition(self, train);
        // Whatever it searched is done with: the next hand can have the next spot.
        if (_spot is { } searched)
        {
            calls.ReleaseSpot(member, searched.Key);
            _spot = null;
        }
        // Near enough: the crate hand's own way up the steps and in. Still out till it's stowed and there's nothing more
        // to search (the driver waits).
        if ((Flat(here) - Flat(foot)).Length < 8)
        {
            _path = null;
            return null;
        }
        calls.Out(member, true);
        Doing = "bringing the find back";
        return Follow(self, train, foot);
    }

    /// <summary>The cargo car nearest a point (a find's stowed in any car: it takes no room).</summary>
    static int NearestCargo(TrainOnLine train, Double3 at) =>
        train.Vehicles.Where(v => v.Kind == VehicleKind.Cargo && v.Id < train.Frames.Count)
            .OrderBy(v => (Flat(train.Frames[v.Id].Origin) - Flat(at)).Length).Select(v => v.Id).DefaultIfEmpty(-1).First();

    /// <summary>Where a crewmate stands to search a spot: just out in front of it, in the room.</summary>
    static Double3 Front(in HidingSpot h) => h.At + h.Facing * 0.45;

    /// <summary>Along the planned way, a point at a time; a new way if it's stuck (a crewmate in the door, a car moved).</summary>
    PlayerIntent Follow(in PlayerState self, TrainOnLine train, Double3 goal)
    {
        var here = PlayerMotor.WorldPosition(self, train);
        if ((Flat(here) - Flat(_lastAt)).Length < 0.02)
            _stuckTicks++;
        else
            _stuckTicks = 0;
        _lastAt = here;
        // A new way for a new goal (the house, then back to the train), or when it's stuck.
        if (_path is null || (Flat(_pathGoal) - Flat(goal)).Length > 1 || _stuckTicks > SimConstants.TickRate * 2)
        {
            _pathGoal = goal;
            _path = FootPath.Plan(train, here, goal);
            _pathAt = 0;
            _stuckTicks = 0;
            if (_path is null)
                return Head(self, goal, Doing); // no way found: straight at it, as the crate hands go
        }
        while (_pathAt < _path.Count - 1 && (Flat(_path[_pathAt]) - Flat(here)).Length < 0.4)
            _pathAt++;
        var target = _path[Math.Min(_pathAt, _path.Count - 1)];
        var to = Flat(target) - Flat(here);
        double turn = Wrap(DMath.Atan2(-to.X, -to.Z) - self.Yaw);
        bool far = (Flat(goal) - Flat(here)).Length > 4;
        return new PlayerIntent
        {
            LookYaw = (float)Math.Clamp(turn, -0.5, 0.5),
            MoveZ = Math.Abs(turn) < 0.35 ? (float)Math.Clamp(to.Length * 1.5, 0.3, 1) : 0,
            Buttons = far && Math.Abs(turn) < 0.2 ? PlayerButtons.Run : PlayerButtons.None,
        };
    }

    void ReleaseSpot()
    {
        if (_spot is { } s)
            calls.ReleaseSpot(member, s.Key);
        _spot = null;
        _path = null;
    }

    /// <summary>
    /// Nothing (more) for it in the village: back from the houses if it's out among them (still out till then, so nobody
    /// leaves it), and then it's not out any more.
    /// </summary>
    PlayerIntent? GiveUpVillage(in PlayerState self, World world, StopPlan p)
    {
        bool wasOut = _spot is not null || calls.IsOut(member);
        ReleaseSpot();
        if (wasOut && self.Parent == PlayerState.World && FindHome(self, world, p) is { } home)
        {
            Doing = "back from the village";
            return home;
        }
        calls.Out(member, false);
        return null;
    }
}
