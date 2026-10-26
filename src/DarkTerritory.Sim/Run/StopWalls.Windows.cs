using Ballast;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Stops;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Run;

/// <summary>A window in an open house's outside wall, in its own frame (note 1053): the run of wall it's in and its middle along that run.</summary>
public readonly record struct HouseWindowAt(WallRun Run, double Along)
{
    /// <summary>Its middle on the wall's outside face, in the house's frame.</summary>
    public (double X, double Y) Outside => Run.AlongX ? (Along, Run.At) : (Run.At, Along);
}

/// <summary>
/// An opening cut through an open house's outside wall, in its own frame: the wall's middle line it's in (along x at y =
/// <see cref="At"/>, or along y at x = <see cref="At"/>), from <see cref="A"/> to <see cref="B"/> along it, and its bottom and
/// top over the house's ground; what the wall under it is (a doorway's: the plinth its threshold's on).
/// </summary>
public readonly record struct Opening(bool AlongX, double At, double A, double B, double Lo, double Hi, ShellPart Under = ShellPart.Wall);

/// <summary>
/// The open houses' windows (queue #751, ARCHITECTURE §8 note 1053; GDD App. F.5: "The village's houses have real doors and
/// windows, which open and shut. In the Hollow House, a door slamming shut is what's coming, and a crewmate can still get out
/// through a door or a window while they slam"). The ground floor's windows are openings through the wall, each with a plank
/// shutter: standing open as the ransack left it, as the doors do, or shut (a crewmate holds Use at it, as at a door, and the
/// Hollow House slams them after its doors). Shut, it's a wall in the opening (<see cref="Near"/>, as a door's leaf is); open,
/// anyone climbs through it by walking into it, inside or out, the way a cellar window's crates are climbed (note 1005): a
/// world ladder up to the sill each side, over it and out (<see cref="Ladders"/>). A jackpot house's (note 719) are shut and
/// barred till its lock's picked. The upstairs windows, and a storeyed house's ground floor's back end, stay boarded (art).
/// </summary>
public sealed partial class StopWalls
{
    /// <summary>
    /// A window's half width (m), and its sill and head over its floor's boards: the art's boarded windows' (TownKit), as they
    /// always stood. Not design numbers: a cottage's sash, its sill at a crewmate's hip.
    /// </summary>
    public const double WindowHalf = 0.44, HouseWindowSill = 0.8, HouseWindowHead = 1.9;

    // The working windows, as doors (their keys negative), and their ladders, inside and out; shared by every copy (Clear).
    List<HouseDoor> _windows = [];
    List<(int Owner, int Key, WorldLadder Ladder)> _windowLadders = [];
    Dictionary<(int, int), List<int>> _windowLadderCells = [];

    /// <summary>
    /// A window's key: its stop's place in the route's features, its building and which of the building's working windows
    /// (<see cref="GroundWindows"/>' order). Negative, so never a door's (<see cref="DoorKey"/>); alike on every machine. It's
    /// shut as a door is (<see cref="Shut"/>, <see cref="SetShut"/>), on the doors' record.
    /// </summary>
    public static int WindowKey(int feature, int building, int window) => -((feature << 18 | building << 8 | window) + 1);

    /// <summary>A window's stop (its place in the route's features), building and which window it is, from its key (<see cref="WindowKey"/>).</summary>
    public static (int Feature, int Building, int Window) WindowOf(int key)
    {
        int code = -key - 1;
        return (code >> 18, code >> 8 & 1023, code & 255);
    }

    /// <summary>Whether a key's a window's (<see cref="WindowKey"/>), not a door's.</summary>
    public static bool IsWindow(int key) => key < 0;

    /// <summary>The working windows (note 1053), as doors: <see cref="HouseDoor.At"/> the middle of the wall under the opening at the floor inside, <see cref="HouseDoor.Out"/> its way out.</summary>
    public IReadOnlyList<HouseDoor> HouseWindows => _windows;

    /// <summary>
    /// Every window along an open house's outside walls, in its own frame, as each floor has them: along each run with no
    /// door, one or two, as fit a window's width and a pier of about a metre either side and between, spread evenly.
    /// </summary>
    public static IEnumerable<HouseWindowAt> WindowsOf(StopBuilding b)
    {
        var o = Outline(b);
        for (int i = 0; i < o.Runs.Count; i++)
        {
            var r = o.Runs[i];
            if (r.Inner || o.Doors.Contains(i))
                continue;
            int count = Math.Min(2, (int)((r.Length - 1.0) / (2 * WindowHalf + 1.6)));
            for (int w = 0; w < count; w++)
                yield return new HouseWindowAt(r, r.A + r.Length * (w + 1) / (count + 1));
        }
    }

    /// <summary>
    /// The windows that open, shut and are climbed through: the ground floor's (<see cref="WindowsOf"/>), but for a storeyed
    /// house's back end, whose wall has its back door in it.
    /// </summary>
    public static IEnumerable<HouseWindowAt> GroundWindows(StopBuilding b) =>
        WindowsOf(b).Where(w => !Storeyed(b) || w.Run.AlongX || Math.Sign(w.Run.At) != BackEnd(b));

    /// <summary>The working windows' openings through the wall (<see cref="GroundWindows"/>), sill and head over the house's ground plus <paramref name="lift"/>.</summary>
    public static IEnumerable<Opening> WindowOpenings(StopBuilding b, double lift = 0)
    {
        double floor = LevelAt(b, 0) + lift, t = WallThickness;
        foreach (var w in GroundWindows(b))
            yield return new Opening(w.Run.AlongX, w.Run.At - w.Run.Out * t / 2, w.Along - WindowHalf, w.Along + WindowHalf,
                floor + HouseWindowSill, floor + HouseWindowHead);
    }

    /// <summary>
    /// A wall's box (an open house's frame: middle and half sizes; from <paramref name="low"/> to <paramref name="top"/>) with
    /// the openings in it cut out: the wall either side of and between them whole, and under, between and over each: what
    /// each piece is (under an opening, its <see cref="Opening.Under"/>; over the top one, a lintel). The same pieces, in the same
    /// order, the sim stands and the art draws. A box no opening's in comes back whole.
    /// </summary>
    public static IEnumerable<ShellBox> Cut((double X, double Y, double HalfX, double HalfY) box, IReadOnlyList<Opening> holes, double low, double top)
    {
        var (x, y, wx, wy) = box;
        bool alongX = wx > wy;
        double at = alongX ? y : x, mid = alongX ? x : y, half = alongX ? wx : wy, across = alongX ? wy : wx;
        double from = mid - half, to = mid + half;
        var cuts = holes.Where(h => h.AlongX == alongX && Math.Abs(h.At - at) < 1e-6 && h.B > from && h.A < to).ToList();
        if (cuts.Count == 0)
        {
            yield return new ShellBox(ShellPart.Wall, x, y, wx, wy, low, top);
            yield break;
        }
        // Along the wall from edge to edge of every opening in it: a stretch no opening's over stands whole; one under one or
        // more (a cellar window under a ground floor's) stands under, between and over them.
        var edges = cuts.SelectMany(h => new[] { Math.Max(from, h.A), Math.Min(to, h.B) }).Append(from).Append(to).Distinct().Order().ToList();
        for (int i = 0; i + 1 < edges.Count; i++)
        {
            double a = edges[i], z = edges[i + 1];
            var over = cuts.Where(h => h.A <= a + 1e-9 && h.B >= z - 1e-9).OrderBy(h => h.Lo).ToList();
            if (over.Count == 0)
            {
                if (z - a > 0.01)
                    yield return Tag(ShellPart.Wall, Along(alongX, at, a, z, across, low, top));
                continue;
            }
            double up = low;
            for (int k = 0; k < over.Count; k++)
            {
                if (over[k].Lo - up > 1e-6 || k == 0)
                    yield return Tag(k == 0 ? over[k].Under : ShellPart.Wall, Along(alongX, at, a, z, across, up, over[k].Lo));
                up = Math.Max(up, over[k].Hi);
            }
            yield return Tag(ShellPart.Lintel, Along(alongX, at, a, z, across, up, top));
        }
    }

    /// <summary>The window a crewmate standing at <paramref name="at"/> can work: the nearest within <paramref name="reach"/> (flat), or null.</summary>
    public HouseDoor? WindowInReach(Double3 at, double reach)
    {
        HouseDoor? best = null;
        double nearest = reach;
        foreach (var d in _windows)
        {
            var v = at - d.At;
            double flat = (v with { Y = 0 }).Length;
            if (flat <= nearest && Math.Abs(v.Y) < 2)
                (best, nearest) = (d, flat);
        }
        return best;
    }

    /// <summary>
    /// Whether a window's barred (note 719): a jackpot house's, while its door's locked. Shut, and nobody opens it: the wrench
    /// is the way in.
    /// </summary>
    public bool Barred(int key) => IsWindow(key) && _locked.Count > 0 && _windows.FirstOrDefault(w => w.Key == key) is { Key: not 0 } w && LockedHouse(w.House);

    /// <summary>
    /// An open house's working windows (from <see cref="Of"/>, after its doors, <see cref="AddHouse"/>): a wall in each opening that
    /// stands only while it's shut, the ladders up to its sill inside and out, and the window as a door of the house its inside's
    /// in (a pair's cottage's). <paramref name="place"/> stands a box in the house's frame in the world (bottom and top over its
    /// ground); <paramref name="world"/> is a point in it at a height over its ground.
    /// </summary>
    void AddWindows(int feature, int building, StopBuilding b, int owner, int firstHouse, Func<double, double, double, double, double, double, Wall> place,
        Func<double, double, double, Double3> world)
    {
        double t = WallThickness, floor = LevelAt(b, 0);
        int k = 0;
        foreach (var w in GroundWindows(b))
        {
            int key = WindowKey(feature, building, k++);
            var (ox, oy) = w.Run.Normal;
            var (sx, sy) = w.Outside;
            double cx = sx - ox * t / 2, cy = sy - oy * t / 2;
            // The shutter fills the opening, the wall's thickness deep.
            Add(place(cx, cy, w.Run.AlongX ? WindowHalf : t / 2, w.Run.AlongX ? t / 2 : WindowHalf, floor + HouseWindowSill, floor + HouseWindowHead) with { Door = key });
            var at = world(cx, cy, floor);
            var way = (world(cx + ox, cy + oy, floor) - at) with { Y = 0 };
            // (Which house: the one its inside's in, of this building's: a pair's cottage's own.)
            var inside = world(cx - ox * (t / 2 + 0.5), cy - oy * (t / 2 + 0.5), floor + 0.5);
            int house = Enumerable.Range(firstHouse, _houses.Count - firstHouse).FirstOrDefault(h => _houses[h].Holds(inside), _houses.Count - 1);
            _windows.Add(new HouseDoor(key, at, way.Normalized, house));
            // Up to the sill from the floor inside, and from the ground outside; over it and out (or in) a step past the wall, as
            // a cellar window's crates are climbed (note 1005).
            double sill = floor + HouseWindowSill + 0.05, off = t / 2 + 0.15;
            var footIn = world(cx - ox * off, cy - oy * off, floor);
            var footOut = world(cx + ox * off, cy + oy * off, 0);
            // (Not where the house's own furniture stands against it inside: climbed in, you'd be set down in a cupboard. Its
            // shutter's worked as any.)
            double past = WindowOnto - off;
            // (The foot's against the wall, the landing a crewmate's width clear of it: neither finds the wall itself.)
            if (Solid(owner, footIn + Double3.Up * 0.5, 0.05) || Solid(owner, world(cx - ox * past, cy - oy * past, floor + 0.5), 0.25))
                continue;
            AddWindowLadder(owner, key, new WorldLadder(footIn, world(cx - ox * off, cy - oy * off, sill), way.Normalized, WindowOnto));
            AddWindowLadder(owner, key, new WorldLadder(footOut, world(cx + ox * off, cy + oy * off, sill), way.Normalized * -1, WindowOnto));
        }
    }

    /// <summary>Whether a world point's in one of a building's own solids, or within <paramref name="margin"/> of one (not a door's or a window's).</summary>
    bool Solid(int owner, Double3 p, double margin) =>
        _cells.TryGetValue(CellOf(p.X, p.Z), out var list) && list.Any(i => _walls[i] is { Door: 0 } w && w.Owner == owner
            && w.ToLocal(p) is var l && Math.Abs(l.X) <= w.HalfLength + margin && Math.Abs(l.Z) <= w.HalfWidth + margin && p.Y >= w.Bottom && p.Y <= w.Top);

    void AddWindowLadder(int owner, int key, WorldLadder ladder)
    {
        int index = _windowLadders.Count;
        _windowLadders.Add((owner, key, ladder));
        // In every cell within reach of it and a little more (the over-the-top step's past the wall).
        const double R = 3;
        var (x0, z0) = CellOf(ladder.Foot.X - R, ladder.Foot.Z - R);
        var (x1, z1) = CellOf(ladder.Foot.X + R, ladder.Foot.Z + R);
        for (int x = x0; x <= x1; x++)
            for (int z = z0; z <= z1; z++)
            {
                if (!_windowLadderCells.TryGetValue((x, z), out var list))
                    _windowLadderCells[(x, z)] = list = [];
                list.Add(index);
            }
    }

    /// <summary>The open windows' ladders (note 1053): a shut one's are gone, and whoever was on one falls back.</summary>
    IEnumerable<WorldLadder> WindowLadders => _windowLadders.Where(l => !_shut.Contains(l.Key)).Select(l => l.Ladder);
}
