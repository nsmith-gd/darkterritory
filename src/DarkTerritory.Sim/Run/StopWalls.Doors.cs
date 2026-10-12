using Ballast;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Stops;

namespace DarkTerritory.Sim.Run;

/// <summary>
/// An open house's door in the world (note 401): its key (<see cref="StopWalls.DoorKey"/>), the middle of its doorway on
/// the house's outside edge at the floor, its way out (level, a unit vector), and which house it's a door of (a pair's
/// cottages are a house each, note 453).
/// </summary>
public readonly record struct HouseDoor(int Key, Double3 At, Double3 Out, int House);

/// <summary>
/// The village houses' doors (queue #137, ARCHITECTURE §8 note 401; note 326's "not yet": doors that shut). Every open house's
/// doorway has a door, hanging open as the ransack left it. A crewmate on foot holds Use at it to shut it, or to open it
/// again (<see cref="World"/>'s crew act, the host's; the doors shut replicate). Shut, it's a wall in the doorway like any
/// other (<see cref="Near"/>): the crew, what they carry and drop, a cannon ball and whatever hunts them stop at it, and the
/// bots' way round the village (<see cref="Bots.FootPath"/>) goes round. A house with its door shut is an enclosed space
/// (<see cref="PlayerMotor.Space"/>), as a car with its doors shut is (a pair of cottages is two, each behind its own door,
/// note 453): the Choir passes over whoever's in it (GDD §21:
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

    /// <summary>
    /// An open house as a space: its plan, its frame in the world (origin, axis, across; level), its doors' keys, and which of
    /// its parts it is when that part's a home of its own (a pair's cottage, note 453), or −1 for all of them.
    /// </summary>
    sealed record HouseSpace(StopBuilding B, Double3 Origin, Double3 Ex, Double3 Ey, int[] Doors, int Part = -1)
    {
        /// <summary>Whether a world point stands inside it (within 3 m of its floor).</summary>
        public bool Holds(Double3 p)
        {
            var d = p - Origin;
            if (Math.Abs(d.Y) >= 3)
                return false;
            double x = d.X * Ex.X + d.Z * Ex.Z, y = d.X * Ey.X + d.Z * Ey.Z;
            return Part < 0 ? InParts(B, x, y) : B.Parts[Part] is var q && Math.Abs(x - q.X) <= q.Length / 2 + 1e-9 && Math.Abs(y - q.Y) <= q.Width / 2 + 1e-9;
        }
    }

    /// <summary>
    /// A door's key: its stop's place in the route's features, its building and which of the building's doorways (a pair of
    /// cottages has two). Alike on every machine; never 0.
    /// </summary>
    public static int DoorKey(int feature, int building, int doorway) => (feature << 12 | building << 2 | doorway) + 1;

    /// <summary>
    /// An open house as the creatures that live in houses see it (G1's house creatures, notes 583–586): its index (as
    /// <see cref="HouseAt"/> gives it), its stop's place in the route's features and its building there (from its first
    /// door's key), its plan, its frame in the world (origin, axis, across; level) and its doors' keys.
    /// </summary>
    public readonly record struct OpenHouse(int Index, int Feature, int Building, StopBuilding B, Double3 Origin, Double3 Ex, Double3 Ey, IReadOnlyList<int> Doors, int Part)
    {
        /// <summary>A point in the building's own plan (metres along and across it from its middle), in the world, on its floor.</summary>
        public Double3 World(double x, double y) => Origin + Ex * x + Ey * y;

        /// <summary>
        /// The home's own plan: its middle in the building's plan and its half-extents along and across. A pair's cottage is
        /// its own part (note 453), whose middle isn't the building's; a house of several parts, its biggest.
        /// </summary>
        public (double X, double Y, double HalfX, double HalfY) Plan
        {
            get
            {
                // A pair's cottage is its own part; a house of several parts (an L) is its biggest (the middle of the whole
                // can be out in the L's notch).
                var part = Part >= 0 && Part < B.Parts.Count ? (FootprintPart?)B.Parts[Part]
                    : B.Parts.Count > 0 ? B.Parts.OrderByDescending(q => q.Length * q.Width).ThenBy(q => q.X).ThenBy(q => q.Y).First() : (FootprintPart?)null;
                return part is { } q2 ? (q2.X, q2.Y, q2.Length / 2, q2.Width / 2) : (0, 0, B.Length / 2, B.Width / 2);
            }
        }

        /// <summary>The home's middle, in the world, on its floor.</summary>
        public Double3 Middle => World(Plan.X, Plan.Y);
    }

    /// <summary>Every open house, in index order (alike on every machine).</summary>
    public IEnumerable<OpenHouse> OpenHouses => _houses.Select((h, i) => new OpenHouse(i, h.Doors.Length > 0 ? (h.Doors[0] - 1) >> 12 : -1,
        h.Doors.Length > 0 ? ((h.Doors[0] - 1) >> 2) & 0x3FF : -1, h.B, h.Origin, h.Ex, h.Ey, h.Doors, h.Part));

    /// <summary>Whether a world point stands inside open house <paramref name="house"/>.</summary>
    public bool InHouse(int house, Double3 p) => house >= 0 && house < _houses.Count && _houses[house].Holds(p);

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
            if (_houses[h].Holds(p))
                return h;
        return -1;
    }

    /// <summary>
    /// Which open house a world point stands inside, shut up or not (an index alike on every machine, <see cref="HouseDoor.House"/>),
    /// or −1: the bots keep out of one with the Gaunt in it (note 413).
    /// </summary>
    public int HouseAt(Double3 p)
    {
        for (int h = 0; h < _houses.Count; h++)
            if (_houses[h].Holds(p))
                return h;
        return -1;
    }

    /// <summary>
    /// An open house's doors (from <see cref="Of"/>): a wall in each doorway, that stands only while it's shut, and the house as
    /// a space; a pair of cottages as two, each its own door's (note 453: they're 0.4 m apart, no way between them, and one
    /// shut up was outside while the other's door stood open). <paramref name="place"/> stands a box in the house's frame in
    /// the world, with its door's key.
    /// </summary>
    void AddHouse(int feature, int building, StopBuilding b, RailLine line, RouteFeature f, Func<double, double, double, double, int, Wall> place)
    {
        // The stop's zone is straight and level: the house's frame to the world's is a turn and a shift.
        var origin = Run.StopWorld(line, f, InHouse(b, 0, 0));
        var ex = (Run.StopWorld(line, f, InHouse(b, 1, 0)) - origin) with { Y = 0 };
        var ey = (Run.StopWorld(line, f, InHouse(b, 0, 1)) - origin) with { Y = 0 };
        double t = WallThickness, half = DoorWidth / 2;
        var keys = new List<int>();
        var doorways = Doorways(b).ToList();
        // A home of its own behind each door when there's more than one, each in a part of its own (a pair's cottages): the
        // part its doorway's in.
        var parts = doorways.Select(w => b.Parts.ToList().FindIndex(q => Math.Abs(w.In.X - q.X) <= q.Length / 2 && Math.Abs(w.In.Y - q.Y) <= q.Width / 2)).ToList();
        bool apart = doorways.Count > 1 && parts.All(q => q >= 0) && parts.Distinct().Count() == parts.Count;
        int k = 0;
        foreach (var (outside, inside) in doorways)
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
            if (apart)
                _houses.Add(new HouseSpace(b, origin, ex.Normalized, ey.Normalized, [key], parts[k - 1]));
            else
                keys.Add(key);
        }
        if (!apart)
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
