using Ballast;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Stops;

namespace DarkTerritory.Sim.Run;

/// <summary>
/// An open house's door in the world (note 401): its key (<see cref="StopWalls.DoorKey"/>), the middle of its doorway on
/// the house's outside edge at the floor, its way out (level, a unit vector), and which house it's a door of.
/// </summary>
public readonly record struct HouseDoor(int Key, Double3 At, Double3 Out, int House);

/// <summary>
/// The village houses' doors (queue #137, ARCHITECTURE §8 note 401; note 326's "not yet": doors that shut). Every open house's
/// doorway has a door, hanging open as the ransack left it. A crewmate on foot holds Use at it to shut it, or to open it
/// again (<see cref="World"/>'s crew act, the host's; the doors shut replicate). Shut, it's a wall in the doorway like any
/// other (<see cref="Near"/>): the crew, what they carry and drop, a cannon ball and whatever hunts them stop at it, and the
/// bots' way round the village (<see cref="Bots.FootPath"/>) goes round. A house with every door shut is an enclosed space
/// (<see cref="PlayerMotor.Space"/>), as a car with its doors shut is: the Choir passes over whoever's in it (GDD §21:
/// "anyone ... not behind a closed door"), and voices through its walls are muffled.
/// </summary>
public sealed partial class StopWalls
{
    // Shared by every copy of these walls (Clear makes one): the doors, the houses they're doors of, which are shut, and the
    // houses with every door shut (worked out when one changes).
    HashSet<int> _shut = [];
    List<HouseDoor> _doors = [];
    List<HouseSpace> _houses = [];
    List<int> _sealed = [];

    /// <summary>An open house as a space: its plan, its frame in the world (origin, axis, across; level), its doors' keys.</summary>
    sealed record HouseSpace(StopBuilding B, Double3 Origin, Double3 Ex, Double3 Ey, int[] Doors);

    /// <summary>
    /// A door's key: its stop's place in the route's features, its building and which of the building's doorways (a pair of
    /// cottages has two). Alike on every machine; never 0.
    /// </summary>
    public static int DoorKey(int feature, int building, int doorway) => (feature << 12 | building << 2 | doorway) + 1;

    /// <summary>Every open house's door.</summary>
    public IReadOnlyList<HouseDoor> HouseDoors => _doors;

    /// <summary>Whether a door's shut.</summary>
    public bool Shut(int key) => _shut.Contains(key);

    /// <summary>The doors shut, in key order (for the wire).</summary>
    public IEnumerable<int> ShutDoors => _shut.Order();

    /// <summary>Shuts or opens a door (the host's crew act). True if that changed it.</summary>
    public bool SetShut(int key, bool shut)
    {
        if (!_doors.Any(d => d.Key == key) || (shut ? !_shut.Add(key) : !_shut.Remove(key)))
            return false;
        Seal();
        return true;
    }

    /// <summary>Client side: the host's doors shut.</summary>
    public void MirrorShut(IEnumerable<int> shut)
    {
        var now = shut.Where(k => _doors.Any(d => d.Key == k)).ToHashSet();
        if (now.SetEquals(_shut))
            return;
        _shut.Clear();
        _shut.UnionWith(now);
        Seal();
    }

    void Seal()
    {
        _sealed.Clear();
        for (int h = 0; h < _houses.Count; h++)
            if (_houses[h].Doors.Length > 0 && _houses[h].Doors.All(_shut.Contains))
                _sealed.Add(h);
    }

    /// <summary>The door a crewmate standing at <paramref name="at"/> can work: the nearest within <paramref name="reach"/> (flat), or null.</summary>
    public HouseDoor? DoorInReach(Double3 at, double reach)
    {
        HouseDoor? best = null;
        double nearest = reach;
        foreach (var d in _doors)
        {
            var v = at - d.At;
            double flat = (v with { Y = 0 }).Length;
            if (flat <= nearest && Math.Abs(v.Y) < 2)
                (best, nearest) = (d, flat);
        }
        return best;
    }

    /// <summary>
    /// Which house, shut up with every door shut, a world point stands inside (an index alike on every machine), or −1. Cheap
    /// while nothing's shut.
    /// </summary>
    public int ShutIn(Double3 p)
    {
        foreach (int h in _sealed)
        {
            var house = _houses[h];
            var d = p - house.Origin;
            if (Math.Abs(d.Y) < 3 && InParts(house.B, d.X * house.Ex.X + d.Z * house.Ex.Z, d.X * house.Ey.X + d.Z * house.Ey.Z))
                return h;
        }
        return -1;
    }

    /// <summary>
    /// An open house's doors (from <see cref="Of"/>): a wall in each doorway, that stands only while it's shut, and the house as
    /// a space. <paramref name="place"/> stands a box in the house's frame in the world, with its door's key.
    /// </summary>
    void AddHouse(int feature, int building, StopBuilding b, RailLine line, RouteFeature f, Func<double, double, double, double, int, Wall> place)
    {
        // The stop's zone is straight and level: the house's frame to the world's is a turn and a shift.
        var origin = Run.StopWorld(line, f, InHouse(b, 0, 0));
        var ex = (Run.StopWorld(line, f, InHouse(b, 1, 0)) - origin) with { Y = 0 };
        var ey = (Run.StopWorld(line, f, InHouse(b, 0, 1)) - origin) with { Y = 0 };
        double t = WallThickness, half = DoorWidth / 2;
        var keys = new List<int>();
        int k = 0;
        foreach (var (outside, inside) in Doorways(b))
        {
            // The doorway's middle on the house's outside edge, and its way out (along an axis of the house: its walls are).
            double nx = outside.X - inside.X, ny = outside.Y - inside.Y, len = Math.Sqrt(nx * nx + ny * ny);
            (nx, ny) = (nx / len, ny / len);
            double mx = inside.X + nx * 1.2, my = inside.Y + ny * 1.2;
            bool acrossX = Math.Abs(nx) > 0.5;
            int key = DoorKey(feature, building, k++);
            // The leaf fills the doorway, the wall's thickness deep.
            Add(place(mx - nx * t / 2, my - ny * t / 2, acrossX ? t / 2 : half, acrossX ? half : t / 2, key));
            var at = Run.StopWorld(line, f, InHouse(b, mx, my));
            var way = (Run.StopWorld(line, f, InHouse(b, mx + nx, my + ny)) - at) with { Y = 0 };
            _doors.Add(new HouseDoor(key, at, way.Normalized, _houses.Count));
            keys.Add(key);
        }
        _houses.Add(new HouseSpace(b, origin, ex.Normalized, ey.Normalized, [.. keys]));
    }

    /// <summary>A copy of these walls (<see cref="Clear"/>) shares their doors, and which are shut.</summary>
    void KeepDoors(StopWalls from)
    {
        _shut = from._shut;
        _doors = from._doors;
        _houses = from._houses;
        _sealed = from._sealed;
    }
}
