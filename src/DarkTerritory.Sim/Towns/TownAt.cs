using DarkTerritory.Sim.LineGen;

namespace DarkTerritory.Sim.Towns;

/// <summary>
/// A town the crew is in between nights (ARCHITECTURE §8 note 591; the director, 9 Oct 2026: "the town that we end in in a
/// run should be the same town that we begin in in the next run ... so that you feel like you are still living up this
/// fantasy of I'm going from town to town to town"). What makes it that town and no other: its name, what it makes and its
/// seed (its layout, its people and their names are all drawn from that; <see cref="TownGenerator.Generate"/>). A night's
/// terminus is one (<see cref="Terminus"/>); delivered there, it's where the campaign's next night departs from, carried in
/// that night's route spec (<c>frontier:7@Fort Boudreau/coal/1234</c>), so every machine makes the same town.
/// </summary>
public sealed record TownAt(string Name, string Industry, ulong Seed)
{
    /// <summary>How it rides in a route spec, after its <c>@</c>.</summary>
    public string Spec => $"{Name}/{Industry}/{Seed}";

    /// <summary>
    /// The town at the end of <paramref name="route"/>: its terminus's name, a trade drawn from <paramref name="industries"/>
    /// (linegen tiers.json fortress.identities) by its own stream, and its own seed. Null for a silent settlement (nobody
    /// lives there to depart from) or a hand-laid line with no plan.
    /// </summary>
    public static TownAt? Terminus(Route.Route route, IReadOnlyList<string> industries)
    {
        if (route.Plan?.Terminus is not { Silent: false } t)
            return null;
        var rng = Streams.Rng(route.Seed, "terminus", t.Name);
        return new TownAt(t.Name, industries.Count > 0 ? rng.Pick(industries) : "", Streams.Mix(route.Seed, "terminus", t.Name));
    }

    /// <summary>A spec's carried town (<see cref="Spec"/>), or null if it doesn't read as one.</summary>
    public static TownAt? Parse(string text) =>
        text.Split('/') is [var name, var industry, var seed] && name.Length > 0 && ulong.TryParse(seed, out var s) ? new TownAt(name, industry, s) : null;

    /// <summary>
    /// A route spec split at its carried town: <c>frontier:7@Fort Boudreau/coal/1234</c> is <c>frontier:7</c> departing from
    /// Fort Boudreau. A spec with none carries none.
    /// </summary>
    public static (string Spec, TownAt? From) Split(string spec) =>
        spec.IndexOf('@') is var at and >= 0 ? (spec[..at], Parse(spec[(at + 1)..])) : (spec, null);

    /// <summary><paramref name="spec"/> departing from <paramref name="from"/> (none, as it is).</summary>
    public static string With(string spec, TownAt? from) => from is null ? Split(spec).Spec : $"{Split(spec).Spec}@{from.Spec}";
}
