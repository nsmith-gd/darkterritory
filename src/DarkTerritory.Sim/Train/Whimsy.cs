using Ballast;

namespace DarkTerritory.Sim.Train;

/// <summary>The canary's state (note 715): singing while all's well, quiet while something's aboard near it, dead in fumes.</summary>
public enum CanaryMood : byte { Singing, Silent, Dead }

/// <summary>
/// The whimsies' state for the night (ARCHITECTURE §8 note 715), the host's, in the world record: which of Gerald and the
/// gnomes the Gannet's taken (<see cref="Taken"/>, bit <see cref="Decoy.Index"/>), the canary's mood, the calliope's note (a
/// count of the cord's pulls: each the tune's next), and the gramophone's record and the tick it was wound.
/// </summary>
public readonly record struct WhimsyState(int Taken, CanaryMood Canary, int CalliopeNote, bool Playing, int Record, uint WoundAt)
{
    public bool IsTaken(int index) => (Taken & 1 << index) != 0;
}

/// <summary>
/// A decoy the Gannet can dive on (note 715): Gerald (index 0), the straw dummy in a crew coat sat on the guard van's roof,
/// or a garden gnome (index 1 on) bolted to a car's roof; in its car's frame, at its feet on the roof.
/// </summary>
public readonly record struct Decoy(int Index, int Car, Double3 Local)
{
    public bool Gerald => Index == 0;
}

/// <summary>
/// Where the whimsies are on the train (note 715; docs/design/upgrades.md §8.2): worked out alike on every machine from the
/// train's shape, so a client draws them where the host has the Gannet dive on them.
/// </summary>
public static class Whimsy
{
    /// <summary>The fitted decoys, taken or not: Gerald if <paramref name="gerald"/>, then <paramref name="gnomes"/> gnomes.</summary>
    public static List<Decoy> Decoys(TrainOnLine train, bool gerald, int gnomes) => Decoys(train.Frames, gerald, gnomes);

    /// <summary><see cref="Decoys(TrainOnLine, bool, int)"/> from the cars' frames (the art's).</summary>
    public static List<Decoy> Decoys(IReadOnlyList<CarFrame> frames, bool gerald, int gnomes)
    {
        var all = new List<Decoy>();
        int cars = frames.Count - 1;
        if (cars < 1)
            return all;
        if (gerald)
        {
            // Stood on the guard van's roof at its front end by the edge, out of the rear gun's way, looking back down the line.
            var van = frames[^1].Shape;
            all.Add(new Decoy(0, frames.Count - 1, new Double3(0.95, van.RoofHeight, -van.HalfLength + 1.6)));
        }
        for (int i = 0; i < Math.Min(gnomes, 30); i++)
        {
            // Along the cars behind the engine, one a car before doubling up, off the rail on alternate sides.
            int car = 1 + i % cars;
            var shape = frames[car].Shape;
            double span = Math.Max(1, 2 * shape.HalfLength - 4);
            double z = -shape.HalfLength + 2 + (1.7 + 2.9 * i) % span;
            all.Add(new Decoy(i + 1, car, new Double3(i % 2 == 0 ? -0.75 : 0.75, shape.RoofHeight, z)));
        }
        return all;
    }

    /// <summary>The canary's car: car one, behind the engine (the engine's, on a train of one).</summary>
    public static int CanaryCar(TrainOnLine train) => CanaryCar(train.Frames);

    public static int CanaryCar(IReadOnlyList<CarFrame> frames) => Math.Min(1, frames.Count - 1);

    /// <summary>The canary's cage, hung inside car one at its front left (or on the engine's cab wall), in that car's frame.</summary>
    public static Double3 Cage(TrainOnLine train) => Cage(train.Frames);

    public static Double3 Cage(IReadOnlyList<CarFrame> frames)
    {
        var shape = frames[CanaryCar(frames)].Shape;
        if (shape.Interior is { } room)
            return new Double3(room.Min.X + 0.3, room.Max.Y - 0.55, room.Min.Z + 0.7);
        return shape.Cab is { } cab ? new Double3(cab.Min.X + 0.3, cab.Max.Y - 0.6, cab.Max.Z - 0.4) : new Double3(0, shape.RoofHeight - 0.6, 0);
    }

    /// <summary>The gramophone's car: the last, the guard van.</summary>
    public static int GramophoneCar(TrainOnLine train) => GramophoneCar(train.Frames);

    public static int GramophoneCar(IReadOnlyList<CarFrame> frames) => frames.Count - 1;

    /// <summary>
    /// The gramophone on its little table against the guard van's left wall, past the tool storage (GDD §10), in the van's
    /// frame: the table's foot on the floor (its top <see cref="Table"/> over it).
    /// </summary>
    public static Double3 Gramophone(CarShape van) =>
        van.Interior is { } room ? new Double3(room.Min.X + 0.3, room.Min.Y + 0.1, room.Min.Z + 4.9) : new Double3(0, van.RoofHeight, 0);

    /// <summary>The gramophone's table top over the floor (m).</summary>
    public const double Table = 0.8;
}
