using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Bots;

/// <summary>
/// A stop hand on foot when the Choir comes (queue #149, ARCHITECTURE §8 note 413; GDD §21: it takes "anyone ... not behind a
/// closed door", App. A.7). The walkers shelter from a roof (<see cref="RoofWalkerBot.Look"/>, <see cref="WarmUp"/>), and a hand
/// down at a stop worked on through it in the open. Now, with the Choir gathering (facilities.json <c>crew.shelterAt</c>) or
/// here, a hand on the ground nearer an open house than the train goes into the nearest one with a single door that nobody
/// else has taken and no Gaunt in it, by its way round the walls (<see cref="FootPath"/>), puts down what it carries, shuts
/// the door behind it (Use held, as anyone does: note 401) and waits, still out (the driver waits for it). Quiet again
/// (<c>crew.shelterOutAt</c>), it opens the door, takes up what it put down and goes back to what it was doing. Nearer the
/// train, it puts down what it carries and leaves the stop to the walker, who gets aboard and into a shut car.
/// All through intent: it walks, presses Use, holds Use.
/// </summary>
public sealed partial class StopHand
{
    enum Hiding : byte { Off, Aboard, ToHouse, Shut, In, Open, TakeUp }

    Hiding _hiding;
    HouseDoor _door;
    bool _wasOut;
    int? _putDown;
    int _hidingTicks;
    bool _houseSought;
    /// <summary>In a house another of us took, behind its door: the door's theirs to shut and open (note 406).</summary>
    bool _guest;
    readonly HashSet<int> _noHouse = [];

    /// <summary>Where it is in getting behind a door from the Choir (for tests and traces): "" when it isn't.</summary>
    public string Sheltering => _hiding == Hiding.Off ? "" : _hiding.ToString();

    /// <summary>The house it's taken against the Choir, if it has (its door).</summary>
    public HouseDoor? ShelterDoor => _hiding is Hiding.ToHouse or Hiding.Shut or Hiding.In or Hiding.Open ? _door : null;

    /// <summary>A house taken for shelter, on the crew's calls: apart from the spots' keys (positive) and the crates' (small negatives).</summary>
    static int HouseKey(int house) => -(1 << 24) - house;

    /// <summary>Given up getting into a house, and the train's way after all.</summary>
    const int HouseGiveUpTicks = SimConstants.TickRate * 40;

    /// <summary>
    /// Behind a door from the Choir (from <see cref="Decide"/>, at a stop): this tick's intent, or null when there's nothing in
    /// it for us (no Choir; or the walker's to get us aboard, then null till it's gone).
    /// </summary>
    PlayerIntent? Shelter(in PlayerState self, World world, StopPlan p)
    {
        var t = Crew(world);
        var choir = world.Choir;
        bool gathering = t.ShelterAt > 0 && !world.SafeYard && !world.TrainInFort && (choir.Present || choir.Build >= t.ShelterAt);
        bool quiet = !choir.Present && choir.Build < t.ShelterOutAt;
        var train = world.Train;
        var walls = train.Walls;
        if (_hiding == Hiding.Off)
        {
            if (!gathering || self.Parent != PlayerState.World || self.Surface != Surface.Ground || PlayerId is null)
                return null;
            _wasOut = calls.IsOut(member);
            _putDown = null;
            _hidingTicks = 0;
            _noHouse.Clear();
            _houseSought = false;
            _guest = false;
            _hiding = Hiding.Aboard;
        }
        _hidingTicks++;
        var here = PlayerMotor.WorldPosition(self, train);
        var carried = world.Bodies.CarriedBy(PlayerId!.Value);
        switch (_hiding)
        {
            case Hiding.Aboard:
                {
                    // Note 406: already inside a house another of us has taken (two hands searching it, one took it for
                    // shelter and shut the door on the pair): behind its door too. It stays, and leaves the door to them; it
                    // went for the train before, from inside the shut house, and stood at the wall till dawn.
                    if (walls is not null && walls.HouseAt(here) is var inHouse and >= 0 && calls.SpotClaimed(HouseKey(inHouse), member)
                        && walls.HouseDoors.Any(d => d.House == inHouse))
                    {
                        _door = walls.HouseDoors.First(d => d.House == inHouse);
                        _guest = true;
                        _hiding = Hiding.In;
                        calls.Out(member, true);
                        return new PlayerIntent();
                    }
                    // A house to go into, if there's one nearer than the train (and walkable); else the walker's way aboard.
                    // Looked for once (and again only when one's turned out no good): a way's planned to each one tried.
                    if (!_houseSought && self.Parent == PlayerState.World && walls is not null && _hidingTicks < HouseGiveUpTicks
                        && (_houseSought = true) && House(world, walls, here, t) is { } door)
                    {
                        _door = door;
                        _hiding = Hiding.ToHouse;
                        _path = null;
                        calls.ClaimSpot(member, HouseKey(door.House));
                        calls.Out(member, true);
                        return new PlayerIntent();
                    }
                    if (quiet)
                        return Done(world, self);
                    // Nobody climbs aboard with freight in their arms: it's put down here, and taken up again after.
                    if (carried is not null && self.Parent == PlayerState.World)
                    {
                        _putDown = carried.Id;
                        Doing = "putting it down for the Choir";
                        return Press();
                    }
                    Doing = "aboard from the Choir";
                    return null;
                }
            case Hiding.ToHouse:
                {
                    // Someone shut it before we got there (another of us in it, or someone playing): another, or aboard.
                    if (walls!.Shut(_door.Key) || _hidingTicks > HouseGiveUpTicks)
                        return Elsewhere();
                    if (quiet)
                        return Done(world, self);
                    // A two-handed crate isn't walked anywhere alone: down it goes.
                    if (carried is { Kind: Physics.BodyKind.Heavy })
                        return Press();
                    var stand = Inside(_door);
                    if ((Flat(here) - Flat(stand)).Length < 0.35)
                    {
                        _hiding = Hiding.Shut;
                        return new PlayerIntent();
                    }
                    Doing = "into a house from the Choir";
                    var going = Follow(self, train, stand);
                    if (_path is null && (Flat(here) - Flat(stand)).Length > 3)
                    {
                        _noHouse.Add(_door.House);
                        return Elsewhere();
                    }
                    return going;
                }
            case Hiding.Shut:
                {
                    if (walls!.Shut(_door.Key))
                    {
                        _hiding = Hiding.In;
                        return new PlayerIntent();
                    }
                    if (quiet)
                    {
                        _hiding = Hiding.Open;
                        return new PlayerIntent();
                    }
                    // What it carried, down in the room first (facing into it, so the hand on the door doesn't take it up again).
                    double intoRoom = DMath.Atan2(_door.Out.X, _door.Out.Z), toDoor = intoRoom + Math.PI;
                    if (carried is not null)
                    {
                        if (!Aligned(self, intoRoom))
                            return new PlayerIntent { LookYaw = Turn(self, intoRoom) };
                        _putDown = carried.Id;
                        Doing = "putting it down in the house";
                        return Press();
                    }
                    if (!Aligned(self, toDoor))
                        return new PlayerIntent { LookYaw = Turn(self, toDoor) };
                    // The door in reach (a hiding spot by it would take the hold instead: up to the doorway).
                    if (world.DoorInReach(self) is not { } at || at.Key != _door.Key)
                        return Walk(self, _door.At - _door.Out * 0.45, toDoor, "to the door");
                    Doing = "shutting the door on the Choir";
                    _pressed = false;
                    return new PlayerIntent { Buttons = PlayerButtons.Use };
                }
            case Hiding.In:
                if (_guest)
                {
                    if (!quiet)
                    {
                        _hidingTicks = 0;
                        Doing = "behind a door from the Choir";
                        return new PlayerIntent();
                    }
                    return Done(world, self);
                }
                // Someone opened it (someone playing, going out): shut it again while it's about.
                if (!walls!.Shut(_door.Key))
                {
                    _hiding = quiet ? Hiding.Open : Hiding.Shut;
                    return new PlayerIntent();
                }
                if (!quiet)
                {
                    _hidingTicks = 0; // waiting's not being stuck
                    Doing = "behind a door from the Choir";
                    return new PlayerIntent();
                }
                _hiding = Hiding.Open;
                return new PlayerIntent();
            case Hiding.Open:
                {
                    // Quiet: the door open again (or the Choir's back, and it stays shut), then what we put down up again.
                    if (gathering && walls!.Shut(_door.Key))
                    {
                        _hiding = Hiding.In;
                        return new PlayerIntent();
                    }
                    double toDoor = DMath.Atan2(-_door.Out.X, -_door.Out.Z);
                    if (walls!.Shut(_door.Key))
                    {
                        if (!Aligned(self, toDoor))
                            return new PlayerIntent { LookYaw = Turn(self, toDoor) };
                        if (world.DoorInReach(self) is not { } at || at.Key != _door.Key)
                            return Walk(self, _door.At - _door.Out * 0.45, toDoor, "to the door");
                        Doing = "opening the door";
                        _pressed = false;
                        return new PlayerIntent { Buttons = PlayerButtons.Use };
                    }
                    if (gathering)
                    {
                        _hiding = Hiding.Shut;
                        return new PlayerIntent();
                    }
                    return Done(world, self);
                }
            case Hiding.TakeUp:
                {
                    // Up again, or gone (someone else took it), or a few seconds at it: back to work either way.
                    if (carried is not null || _hidingTicks > SimConstants.TickRate * 4 || Lying(world, here) is not { } lies)
                        return Off();
                    double toward = DMath.Atan2(-(lies.X - here.X), -(lies.Z - here.Z));
                    if (!Aligned(self, toward))
                        return new PlayerIntent { LookYaw = Turn(self, toward) };
                    Doing = "picking it up again";
                    return Press();
                }
        }
        return null;

        // Not this house: another, if there's one, or the train.
        PlayerIntent Elsewhere()
        {
            _noHouse.Add(_door.House);
            calls.ReleaseSpot(member, HouseKey(_door.House));
            _path = null;
            _houseSought = false;
            _hiding = Hiding.Aboard;
            return new PlayerIntent();
        }
    }

    /// <summary>
    /// The Choir gone: the house given back, out again only if it was, and what it put down for it taken up again if it's
    /// lying in reach (else it's left where it is, for the crates' way or the next hand).
    /// </summary>
    PlayerIntent? Done(World world, in PlayerState self)
    {
        if (!_guest && _hiding is Hiding.ToHouse or Hiding.Shut or Hiding.In or Hiding.Open)
            calls.ReleaseSpot(member, HouseKey(_door.House));
        _guest = false;
        calls.Out(member, _wasOut);
        _path = null;
        _hidingTicks = 0;
        if (self.Parent == PlayerState.World && Lying(world, PlayerMotor.WorldPosition(self, world.Train)) is not null)
        {
            _hiding = Hiding.TakeUp;
            return new PlayerIntent();
        }
        return Off();
    }

    PlayerIntent? Off()
    {
        _hiding = Hiding.Off;
        _putDown = null;
        return null;
    }

    /// <summary>Where what it put down for the Choir lies, if it's still there and in reach, nobody holding it.</summary>
    Double3? Lying(World world, Double3 here)
    {
        if (_putDown is not int id || world.Bodies.All.FirstOrDefault(b => b.Id == id && b.Carrier < 0 && b.Parent == PlayerState.World) is not { } body)
            return null;
        var at = Physics.Bodies.WorldCentre(body, world.Train);
        return (Flat(at) - Flat(here)).Length < 2.5 ? at : null;
    }

    /// <summary>
    /// The nearest open house to get into from <paramref name="here"/>: a single door (one to shut, and it's shut in), not shut
    /// (someone's in it), not taken by another of us, no Gaunt in it; its door no further than <c>crew.shelterOverTrain</c> times
    /// the train (a car's a climb, the roofs and an end door away), and walkable to within <c>crew.shelterReach</c>.
    /// </summary>
    HouseDoor? House(World world, StopWalls walls, Double3 here, StopCrewTuning t)
    {
        var train = world.Train;
        double toTrain = FromTrain(train, here);
        var gaunts = world.ActiveEnemies.Where(e => e is Enemies.Gaunt && !e.Gone).Select(e => walls.HouseAt(e.WorldPosition(train))).ToHashSet();
        foreach (var door in walls.HouseDoors.GroupBy(d => d.House).Where(g => g.Count() == 1).Select(g => g.First())
            .Where(d => !walls.Shut(d.Key) && !_noHouse.Contains(d.House) && !calls.SpotClaimed(HouseKey(d.House), member) && !gaunts.Contains(d.House))
            .Select(d => (Door: d, Far: (Flat(d.At) - Flat(here)).Length)).Where(d => d.Far <= t.ShelterReach && d.Far < toTrain * t.ShelterOverTrain)
            .OrderBy(d => d.Far).ThenBy(d => d.Door.Key).Select(d => d.Door).Take(3))
        {
            var path = FootPath.Plan(train, here, Inside(door));
            if (path is not null && FootPath.Length(here, path) <= t.ShelterReach * 1.4)
                return door;
            _noHouse.Add(door.House);
        }
        return null;
    }

    /// <summary>Where a hand stands to shut a house's door on itself: inside, a stride in from the doorway.</summary>
    static Double3 Inside(in HouseDoor d) => d.At - d.Out * 0.75;

    /// <summary>How far a point is from the nearest car's side or end (flat).</summary>
    static double FromTrain(TrainOnLine train, Double3 at)
    {
        double best = double.MaxValue;
        foreach (var frame in train.Frames)
        {
            var local = frame.ToLocal(at);
            double dx = Math.Max(0, Math.Abs(local.X) - frame.Shape.HalfWidth), dz = Math.Max(0, Math.Abs(local.Z) - frame.Shape.HalfLength);
            best = Math.Min(best, Math.Sqrt(dx * dx + dz * dz));
        }
        return best;
    }
}
