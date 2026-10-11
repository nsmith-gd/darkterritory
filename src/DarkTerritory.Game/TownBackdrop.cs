using Ballast.Render;
using DarkTerritory.Sim.Towns;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>
/// The town the crew's in, behind the front end after a delivered night (queue #336, ARCHITECTURE §8 note 604; the director,
/// 9 Oct 2026: "the town that we end in in a run should be the same town that we begin in in the next run ... we can do a
/// little bit of a loading screen"): the night's terminus town (note 600), the train standing home in its yard, seen from its
/// square, in place of the menus' stand-in yard. The board's picked over it, and the loading card's drawn over it.
/// </summary>
public sealed record TownBackdrop(Sim.Route.Route Route, TrainOnLine Train, Sim.Run.Run? Run, Town Town, Town? Departure)
{
    /// <summary>How long the built night's card holds before the night opens (s): long enough to read. Not a design number.</summary>
    public const double CardHoldSeconds = 2.5;

    /// <summary>Where it's seen from: the town's square, as a crewmate stepping off the train would stand (<see cref="Staging.TownCamera"/>).</summary>
    public Camera Camera => Staging.TownCamera(Town, "square");

    /// <summary>The scene it's drawn with: the night's route and both its towns, the arrival's in its turned frame.</summary>
    public GreyboxScene Scene(Look? look) => new() { Route = Route, Run = Run, Town = Departure, Arrival = Town, Look = look, Time = 0.37 };

    /// <summary>
    /// The loading card over it (note 604): the town by name, and the night ahead, "THE NIGHT TO GRIEVE" once the line's built
    /// (<paramref name="terminus"/>), "BUILDING THE NIGHT AHEAD" while it is.
    /// </summary>
    /// <param name="following">A joiner waiting on its host (note 606): "THE HOST IS PICKING THE NIGHT AHEAD" till the night's up.</param>
    public static (string Heading, string Under) Card(string town, string? terminus, bool following = false) =>
        (town.ToUpperInvariant(), terminus is { Length: > 0 } t ? $"THE NIGHT TO {t.ToUpperInvariant()}"
            : following ? "THE HOST IS PICKING THE NIGHT AHEAD" : "BUILDING THE NIGHT AHEAD");
}
