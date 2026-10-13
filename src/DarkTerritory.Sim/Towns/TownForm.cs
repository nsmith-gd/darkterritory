namespace DarkTerritory.Sim.Towns;

/// <summary>
/// What a piece of a town's ground is (queue #424, note 692): a quarter of it (houses, a market, the works), water inside
/// the wall (a harbour's basin, a fen's channels, a drowned ward), a deck of planks on piles over it, a terrace or bench
/// stepped up or down from the yard's level, a growing plot, or the rail yard.
/// </summary>
public enum DistrictKind : byte { Quarter, Water, Deck, Terrace, Plots, Yard }

/// <summary>
/// One piece of a town's ground plan (note 692): its kind, its id (towns.json <c>forms</c>' district ids), the name the town
/// calls it, its outline in the town's rail frame (S along the line, D across, in order round it), and its
/// <see cref="Level"/>: metres over the yard's level (negative for a quarry bench below it, or a basin's bed).
/// </summary>
public sealed record TownDistrict(DistrictKind Kind, string Id, string Name, IReadOnlyList<(double S, double D)> Outline, double Level = 0)
{
    /// <summary>Whether a point (along the line, across it) is inside its outline.</summary>
    public bool Holds(double s, double d)
    {
        bool inside = false;
        for (int i = 0, j = Outline.Count - 1; i < Outline.Count; j = i++)
        {
            var (a, b) = (Outline[i], Outline[j]);
            if ((a.D > d) != (b.D > d) && s < (b.S - a.S) * (d - a.D) / (b.D - a.D) + a.S)
                inside = !inside;
        }
        return inside;
    }
}

/// <summary>
/// A way through a town that isn't one of the walled town's streets beside the line (note 692): a quay, a boardwalk over
/// a fen, a ramp down a quarry's benches, a causeway across a drowned ward. Its middle's points in the rail frame, its
/// width, and whether it's a <see cref="Deck"/> of planks on piles (walked on over water or a drop) or the ground.
/// </summary>
public sealed record TownWay(IReadOnlyList<(double S, double D)> Points, double Width, bool Deck = false, double Level = 0)
{
    /// <summary>A ramp's or a stair's level at each point (m over the yard's), where it climbs; null where it's all <see cref="Level"/>.</summary>
    public IReadOnlyList<double>? Levels { get; init; }
}

/// <summary>
/// Where a town stands on the line (note 692; the director, 13 Oct: "a town needs a gate that is the entrance from the
/// previous run, and then it also needs a gate that is the exit to the next run"): along its rail frame, the
/// <see cref="Entry"/> gate the line comes in at from the last night and the <see cref="Exit"/> gate it goes out at to
/// the next, and the yard between where the train stands, the longest consist from <see cref="Yard0"/> to
/// <see cref="Yard1"/>. The same town in the same frame whether it's arrived at or left from, so a night's line could
/// run on through it into the next one's without a load (the frame's origin is the yard's start in both).
/// </summary>
public sealed record TownFrame(double Entry, double Exit, double Yard0, double Yard1);
