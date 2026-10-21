namespace DarkTerritory.Game.Telemetry;

/// <summary>
/// The playtest telemetry's event schema (queue #624, note 913; the plan, note 907): one JSON object a line, each with
/// <c>schema</c> (<see cref="Version"/>), <c>type</c> (one of <see cref="Events"/>' keys) and <c>t</c> (seconds since the
/// session began), and then only the fields <see cref="Events"/> lists for its type. The recorder can't write a field the
/// table doesn't list (<see cref="SessionRecorder"/> throws), and <c>TelemetrySchemaTests</c> pins the table to note 913's:
/// a change to either is a change to both, and to <see cref="Version"/> when a field's meaning or a type goes.
/// <para>Nothing personal (note 907, #625): no names, no Steam id, no IP, no text anyone typed but the survey's comment,
/// which the player writes for us (#628, note 916). The crew is counted, never named; an incident is its kind and cause,
/// never its line (which names players).</para>
/// </summary>
public static class TelemetrySchema
{
    public const int Version = 1;

    /// <summary>Every event type and its fields, in the order they're written.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Events = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        // Once, when the game starts recording: what it is and what it runs on; and the playtest it's for (#628).
        ["session"] = ["session", "install", "build", "commit", "edition", "dev", "os", "gpu", "vr", "resolution", "renderScale", "vsync", "fov", "cohort"],
        // A night begins: where, how long a train, who's aboard (counted), and this machine's part in it.
        ["night"] = ["night", "role", "route", "tier", "cars", "humans", "bots"],
        // Each minute of a night: how far, who's alive, how it ran, how the link was.
        ["minute"] = ["night", "minute", "km", "alive", "crew", "frames", "hitches", "p50", "p95", "p99", "pingMs", "loss"],
        // A crewmate's death: when, where along the line, how, and where they were working.
        ["death"] = ["night", "s", "km", "cause", "post", "me"],
        // The host's incident log (C.9), its kind and cause only.
        ["incident"] = ["night", "s", "kind", "cause"],
        // The host's encounter log (note 880), one a spawn, at the night's end.
        ["encounter"] = ["night", "s", "kind", "channel", "where", "part", "afoot", "engagedAt", "confrontedAt", "nearAt", "grabs",
            "punishes", "end", "endedAt"],
        // The first time this machine's player did something (UX friction): "move", "job", "stop", "delivery".
        ["first"] = ["night", "what", "s"],
        // A purchase in the fortress, between nights.
        ["purchase"] = ["what", "cost", "scripAfter"],
        // A control rebound, by its name (never the key: that's harmless, but it's not needed).
        ["rebind"] = ["control"],
        // A menu or panel closed, and how long it was open.
        ["menu"] = ["name", "seconds"],
        // A prompt (a card, a hint, a "press E") shown, and whether it was acted on.
        ["prompt"] = ["night", "name", "acted"],
        // A night over: how it ended and what came of it, and how it ran.
        ["nightEnd"] = ["night", "end", "seconds", "km", "carsDelivered", "carsLost", "cargoDelivered", "net", "crewHome", "crewLost",
            "deaths", "encounters", "hitches", "p50", "p95", "p99"],
        // The end-of-night survey (#628, note 916): fun, fear and play-again, 1-5 or null; the player's own comment, if any;
        // or that they skipped it.
        ["survey"] = ["night", "fun", "fear", "again", "comment", "skipped"],
        // Leaving: to the menu mid-night, quitting the game, kicked, or the link lost; and how far into a night.
        ["quit"] = ["how", "night", "s"],
    };

    /// <summary>The fields every event carries, before its own.</summary>
    public static readonly string[] Common = ["schema", "type", "t"];
}
