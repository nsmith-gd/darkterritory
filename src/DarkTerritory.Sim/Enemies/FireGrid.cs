using Ballast;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Enemies;

/// <summary>Which of a car's inside surfaces a fire cell is on (GDD App. F.1, "Fire is a grid": never mid-air).</summary>
public enum FireFace : byte { Floor, Ceiling, Left, Right }

/// <summary>
/// A car's inside surfaces cut into cells for fire (the director's decision of 6 Oct 2026, GDD App. F.1: "Each car's surfaces
/// (floor, walls, roof; never mid-air) are cut into large cells of 1–2 m. Fire spreads cell to cell, the extinguisher puts out
/// the cell you aim at, and burnt cells char the textures"). Laid out from the car's interior box alone, so the host and every
/// client have the same cells in the same order, and only their heat and char go over the wire. The end walls aren't cells:
/// a fire's way out of a car is through its ends to the next car (<see cref="End"/>).
/// </summary>
public sealed class FireGrid
{
    /// <summary>Each cell's centre and the way its surface faces (into the car), in the car's frame.</summary>
    public readonly Double3[] Centre;
    public readonly FireFace[] Face;
    /// <summary>The cells a cell sets alight: those sharing an edge with it, on its own surface or round a corner onto the next.</summary>
    public readonly int[][] Next;
    /// <summary>How many cells along the car (Z), across it (X) and up its walls (Y).</summary>
    public readonly int Along, Across, Up;
    public readonly Box Room;
    public int Count => Centre.Length;

    static readonly Dictionary<(Box, double), FireGrid> Cache = new();

    /// <summary>The boards are this far above the interior box's floor (CarFrame pads it below them; TrainKit draws them here).</summary>
    public const double FloorPad = 0.1;

    /// <summary>The grid for a car with this interior, at <paramref name="cell"/> m cells (one per box and size, built once).</summary>
    public static FireGrid For(Box room, double cell)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue((room, cell), out var grid))
                Cache[(room, cell)] = grid = new FireGrid(room, cell);
            return grid;
        }
    }

    /// <summary>The grid of car <paramref name="car"/>, or null if it has no room inside (the engine, a flat).</summary>
    public static FireGrid? Of(TrainOnLine train, int car, double cell) =>
        car >= 0 && car < train.Frames.Count && train.Frames[car].Shape.Interior is { } room ? For(room, cell) : null;

    FireGrid(Box room, double cell)
    {
        Room = room;
        var size = room.Max - room.Min;
        Along = Math.Max(1, (int)Math.Round(size.Z / cell));
        Across = Math.Max(1, (int)Math.Round(size.X / cell));
        Up = Math.Max(1, (int)Math.Round(size.Y / cell));
        double dz = size.Z / Along, dx = size.X / Across, dy = size.Y / Up;
        double floor = room.Min.Y + FloorPad;
        var centre = new List<Double3>();
        var face = new List<FireFace>();
        var key = new List<(FireFace Face, int A, int B)>();
        // Floor and ceiling: along × across; the walls: along × up. Index order is the layout's, the same everywhere.
        foreach (var f in new[] { FireFace.Floor, FireFace.Ceiling })
            for (int z = 0; z < Along; z++)
                for (int x = 0; x < Across; x++)
                {
                    centre.Add(new Double3(room.Min.X + (x + 0.5) * dx, f == FireFace.Floor ? floor : room.Max.Y, room.Min.Z + (z + 0.5) * dz));
                    face.Add(f);
                    key.Add((f, z, x));
                }
        foreach (var f in new[] { FireFace.Left, FireFace.Right })
            for (int z = 0; z < Along; z++)
                for (int y = 0; y < Up; y++)
                {
                    centre.Add(new Double3(f == FireFace.Left ? room.Min.X : room.Max.X, room.Min.Y + (y + 0.5) * dy, room.Min.Z + (z + 0.5) * dz));
                    face.Add(f);
                    key.Add((f, z, y));
                }
        Centre = [.. centre];
        Face = [.. face];
        var index = new Dictionary<(FireFace, int, int), int>();
        for (int i = 0; i < key.Count; i++)
            index[key[i]] = i;
        Next = new int[Count][];
        for (int i = 0; i < Count; i++)
        {
            var (f, a, b) = key[i];
            var n = new List<int>();
            void Add(FireFace g, int p, int q)
            {
                if (index.TryGetValue((g, p, q), out int j))
                    n.Add(j);
            }
            // Along the car on every surface.
            Add(f, a - 1, b);
            Add(f, a + 1, b);
            if (f is FireFace.Floor or FireFace.Ceiling)
            {
                Add(f, a, b - 1);
                Add(f, a, b + 1);
                // Round the corner: the floor's and ceiling's edge columns meet the walls' bottom and top rows.
                int row = f == FireFace.Floor ? 0 : Up - 1;
                if (b == 0)
                    Add(FireFace.Left, a, row);
                if (b == Across - 1)
                    Add(FireFace.Right, a, row);
            }
            else
            {
                Add(f, a, b - 1);
                Add(f, a, b + 1);
                int column = f == FireFace.Left ? 0 : Across - 1;
                if (b == 0)
                    Add(FireFace.Floor, a, column);
                if (b == Up - 1)
                    Add(FireFace.Ceiling, a, column);
            }
            Next[i] = [.. n];
        }
    }

    /// <summary>The way cell <paramref name="i"/>'s surface faces, into the car.</summary>
    public Double3 Normal(int i) => Face[i] switch
    {
        FireFace.Floor => Double3.Up,
        FireFace.Ceiling => new Double3(0, -1, 0),
        FireFace.Left => new Double3(1, 0, 0),
        _ => new Double3(-1, 0, 0),
    };

    /// <summary>How many cells along the car from cell <paramref name="i"/>'s end (0: against an end wall), and which end (−1 front, +1 rear).</summary>
    public (int From, int Side) End(int i)
    {
        int a = Row(i);
        return a < Along - 1 - a ? (a, -1) : (Along - 1 - a, 1);
    }

    /// <summary>Cell <paramref name="i"/>'s row along the car (0 at the front, −Z).</summary>
    public int Row(int i) => Face[i] is FireFace.Floor or FireFace.Ceiling ? (i % (Along * Across)) / Across : ((i - 2 * Along * Across) % (Along * Up)) / Up;

    /// <summary>The floor cell under a point in the car (feet, a dropped match), clamped into the room.</summary>
    public int FloorAt(Double3 local)
    {
        var size = Room.Max - Room.Min;
        int z = Math.Clamp((int)Math.Floor((local.Z - Room.Min.Z) / size.Z * Along), 0, Along - 1);
        int x = Math.Clamp((int)Math.Floor((local.X - Room.Min.X) / size.X * Across), 0, Across - 1);
        return z * Across + x;
    }

    /// <summary>
    /// The cell a spray from <paramref name="eye"/> along <paramref name="dir"/> (the car's frame) lands on, within
    /// <paramref name="reach"/> m: the first of the room's floor, ceiling and side walls the line meets from inside. −1 if it
    /// meets none in reach (aimed out of an end, or too far).
    /// </summary>
    public int Hit(Double3 eye, Double3 dir, double reach)
    {
        double best = double.MaxValue;
        Double3 at = default;
        void Plane(double origin, double d, double wall)
        {
            if (Math.Abs(d) < 1e-9)
                return;
            double t = (wall - origin) / d;
            if (t > 1e-6 && t < best)
            {
                var p = eye + dir * t;
                if (p.X >= Room.Min.X - 1e-6 && p.X <= Room.Max.X + 1e-6 && p.Y >= Room.Min.Y + FloorPad - 1e-6 && p.Y <= Room.Max.Y + 1e-6
                    && p.Z >= Room.Min.Z - 1e-6 && p.Z <= Room.Max.Z + 1e-6)
                {
                    best = t;
                    at = p;
                }
            }
        }
        Plane(eye.Y, dir.Y, Room.Min.Y + FloorPad);
        Plane(eye.Y, dir.Y, Room.Max.Y);
        Plane(eye.X, dir.X, Room.Min.X);
        Plane(eye.X, dir.X, Room.Max.X);
        if (best > reach)
            return -1;
        return Nearest(at);
    }

    /// <summary>The cell nearest a point on (or near) the room's surfaces.</summary>
    public int Nearest(Double3 local)
    {
        int best = -1;
        double d = double.MaxValue;
        for (int i = 0; i < Count; i++)
        {
            double e = (Centre[i] - local).Length;
            if (e < d)
            {
                d = e;
                best = i;
            }
        }
        return best;
    }
}
