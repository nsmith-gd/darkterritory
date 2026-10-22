using Ballast;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Train;

/// <summary>
/// One end of a speaking tube (note 720): the car it's in (by vehicle id, the frame's index), its mouthpiece's horn in that
/// car's frame, and which way the horn opens (into the room).
/// </summary>
public readonly record struct TubeEnd(int Car, Double3 Mouth, Double3 Facing);

/// <summary>
/// The speaking tube (GDD App. C.8; the director's calls of 14 Oct 2026, friendslop.md queue #454; ARCHITECTURE §8 note 720):
/// a brass pipe from the cab to the guard van, built in. Said into one mouthpiece, it's heard out of the other's horn,
/// however long the train is. The ends are numbered in <see cref="Ends"/> and joined in pairs by <see cref="Tubes"/>, so a
/// later upgrade (U1's speaking tubes between neighbouring cars, PR #967) adds ends and pairs and nothing else changes: the
/// host routes a voice by the end it went in at, and the clients place the horn it comes out of by the end's number.
/// </summary>
public static class SpeakingTube
{
    /// <summary>How high over the floor the horns are (m): a mouth's height, stooped a little to it.</summary>
    public const double MouthHeight = 1.42;
    /// <summary>How far out from its wall a horn's mouth stands (m): its throat and bell (the art's VoiceKit.Reach).</summary>
    public const double HornOut = 0.2;

    /// <summary>
    /// The cab's mouthpiece, in the engine's frame: on the cab's left wall a step behind the hatch ladder (the coal bunker's
    /// back, then the ladder; note 704), turned in to the cab. Measured from the bunker, the footplate's own (note 280), so it
    /// stays by the footplate however the cab behind it grows (#450, note 716).
    /// </summary>
    public static Double3? CabMouth(CarShape engine)
    {
        if (engine.Cab is not { } cab)
            return null;
        double bunkerBack = engine.Solids.Where(s => s.Part == PartKind.Tender).Select(s => s.Box.Max.Z).DefaultIfEmpty(cab.Min.Z + 2).Max();
        return new Double3(cab.Min.X + HornOut, cab.Min.Y + 0.1 + MouthHeight, Math.Min(bunkerBack + 1.0, cab.Max.Z - 0.6));
    }

    /// <summary>
    /// The guard van's, in its frame: the front right corner, on the wall over the stores' crates (the left wall's the lockers'
    /// and the powder's), turned in to the van.
    /// </summary>
    public static Double3? VanMouth(CarShape van) =>
        van.Interior is { } room ? new Double3(room.Max.X - HornOut, room.Min.Y + 0.1 + MouthHeight, room.Min.Z + 0.6) : null;

    /// <summary>
    /// A car's between the cab and the van (note 978), in its frame: the front left corner, on the wall the cargo isn't stacked
    /// against (a car's crates go up its right front first), turned in to the room.
    /// </summary>
    public static Double3? CarMouth(CarShape car) =>
        car.Interior is { } room ? new Double3(room.Min.X + HornOut, room.Min.Y + 0.1 + MouthHeight, room.Min.Z + 0.6) : null;

    /// <summary>
    /// The train's tube ends, in order: the cab's (0), then the guard van's (1), the last guard car on the engine's rake. A
    /// van cut off or taken has no end aboard: the tube's broken with it. Note 978 (upgrades.md §6's speaking tubes, the
    /// train's kit <see cref="KitTuning.SpeakingTubes"/>): fitted, every car between with a room has one too, after those two,
    /// in the train's order from the engine back (<see cref="CarMouth"/>).
    /// </summary>
    public static IReadOnlyList<TubeEnd> Ends(TrainOnLine train)
    {
        var ends = new List<TubeEnd>(2);
        if (train.Frames.Count == 0 || CabMouth(train.Frames[0].Shape) is not { } cab)
            return ends;
        var van = train.Dynamics.Consist.Vehicles.LastOrDefault(v => v.Kind == VehicleKind.Guard);
        if (van is null || van.Id >= train.Frames.Count || VanMouth(train.Frames[van.Id].Shape) is not { } mouth)
            return ends;
        ends.Add(new TubeEnd(0, cab, new Double3(1, 0, 0)));
        ends.Add(new TubeEnd(van.Id, mouth, new Double3(-1, 0, 0)));
        if (train.Dynamics.Tuning.Kit.SpeakingTubes)
            foreach (var v in train.Dynamics.Consist.Vehicles)
                if (v.Id != 0 && v.Id != van.Id && v.Id < train.Frames.Count && CarMouth(train.Frames[v.Id].Shape) is { } m)
                    ends.Add(new TubeEnd(v.Id, m, new Double3(1, 0, 0)));
        return ends;
    }

    /// <summary>
    /// Which ends are joined (by their numbers in <see cref="Ends"/>): the cab's to the van's, the pipe under the floors; and,
    /// with the speaking tubes fitted (note 978), each end to the next along the train (the cab's, the cars' in order, the
    /// van's), so a voice carries to the neighbouring cars either way.
    /// </summary>
    public static IReadOnlyList<(int A, int B)> Tubes(IReadOnlyList<TubeEnd> ends)
    {
        if (ends.Count < 2)
            return [];
        var tubes = new List<(int A, int B)> { (0, 1) };
        if (ends.Count > 2)
        {
            // The order along the train: the cab, the cars between (2..), the van.
            var chain = new List<int> { 0 };
            for (int i = 2; i < ends.Count; i++)
                chain.Add(i);
            chain.Add(1);
            for (int i = 0; i + 1 < chain.Count; i++)
                tubes.Add((chain[i], chain[i + 1]));
        }
        return tubes;
    }

    /// <summary>
    /// The end <paramref name="s"/> is speaking into: alive, stood in that end's car (not on its roof) within
    /// <paramref name="reach"/> of the mouthpiece across the floor, the nearest if more than one; null for none.
    /// </summary>
    public static int? Into(in PlayerState s, IReadOnlyList<TubeEnd> ends, double reach)
    {
        if (!s.Alive || s.Parent == PlayerState.World)
            return null;
        int? best = null;
        double bestD = double.MaxValue;
        for (int i = 0; i < ends.Count; i++)
        {
            var e = ends[i];
            if (e.Car != s.Parent || s.Position.Y > e.Mouth.Y || s.Position.Y < e.Mouth.Y - MouthHeight - 0.6)
                continue;
            double d = ((s.Position - e.Mouth) with { Y = 0 }).Length;
            if (d <= reach && d < bestD)
                (best, bestD) = (i, d);
        }
        return best;
    }

    /// <summary>The ends a voice said into <paramref name="end"/> comes out of: each joined to it, in <see cref="Tubes"/>' order.</summary>
    public static IReadOnlyList<int> FarEnds(int end, IReadOnlyList<TubeEnd> ends)
    {
        var far = new List<int>(2);
        foreach (var (a, b) in Tubes(ends))
        {
            if (a == end)
                far.Add(b);
            else if (b == end)
                far.Add(a);
        }
        return far;
    }

    /// <summary>Where end <paramref name="end"/>'s horn is in the world, or null if there's no such end on this train.</summary>
    public static Double3? Horn(TrainOnLine train, IReadOnlyList<TubeEnd> ends, int end) =>
        end >= 0 && end < ends.Count && ends[end].Car < train.Frames.Count ? train.Frames[ends[end].Car].ToWorld(ends[end].Mouth) : null;

    /// <summary>
    /// Someone stood on the floor in front of end <paramref name="end"/>'s mouthpiece, <paramref name="off"/> m out into the room
    /// from it and <paramref name="along"/> m along the car (tests, the voice bench, the staged views).
    /// </summary>
    public static PlayerState AtMouth(TrainOnLine train, int end, PlayerTuning p, double off = 0.5, double along = 0)
    {
        var e = Ends(train)[end];
        return new PlayerState
        {
            Parent = e.Car,
            Position = new Double3(e.Mouth.X + e.Facing.X * off, e.Mouth.Y - MouthHeight, e.Mouth.Z + e.Facing.Z * off + along),
            Surface = Surface.Deck,
            Health = p.Health,
            LineHint = train.Cars[e.Car].FrontDistance,
            Kit = p.StartingKit,
            // Facing the horn.
            Yaw = e.Facing.X > 0 ? Math.PI / 2 : -Math.PI / 2,
        };
    }

    /// <summary>
    /// What a speaker says comes out of: the far end's number and its horn in the world (the first of <see cref="HeardAll"/>),
    /// or null when they aren't at a mouthpiece (or the tube's broken).
    /// </summary>
    public static (int End, Double3 At)? Heard(in PlayerState speaker, TrainOnLine train, TubeTuning t) =>
        HeardAll(speaker, train, t) is { Count: > 0 } all ? all[0] : null;

    /// <summary>
    /// Every horn a speaker's voice comes out of (note 978: with the speaking tubes fitted, the neighbouring cars' as well as
    /// the far end of the cab's pipe); none when they aren't at a mouthpiece.
    /// </summary>
    public static IReadOnlyList<(int End, Double3 At)> HeardAll(in PlayerState speaker, TrainOnLine train, TubeTuning t)
    {
        var ends = Ends(train);
        if (Into(speaker, ends, t.MouthReach) is not { } into)
            return [];
        var heard = new List<(int End, Double3 At)>(2);
        foreach (int far in FarEnds(into, ends))
            if (Horn(train, ends, far) is { } at)
                heard.Add((far, at));
        return heard;
    }
}
