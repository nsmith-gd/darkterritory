using System.Text;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>
/// Crew memory's numbers (content/tuning/memory.json; GDD §9, §11, §3.1; the director, 14 Oct 2026; ARCHITECTURE §8 note
/// 721): the crew photo's staging and the logbook's place and reach. Presentation only. The defaults are the file's.
/// </summary>
public sealed record MemoryTuning
{
    public const string File = "tuning/memory.json";
    public PhotoTuning Photo { get; init; } = new();
    public LogbookTuning Logbook { get; init; } = new();
}

/// <summary>The crew photo at the gate (memory.json <c>photo</c>; GDD §9 Arrival, note 721). Field docs live in the file.</summary>
public sealed record PhotoTuning
{
    public int Width { get; init; } = 640;
    public int Height { get; init; } = 360;
    public float Fov { get; init; } = 40;
    public double DelaySeconds { get; init; } = 1.2;
    public double FlashSeconds { get; init; } = 0.6;
    public double Ahead { get; init; } = 2.5;
    public double Out { get; init; } = 3.0;
    public double Spacing { get; init; } = 0.95;
    public double CrateGap { get; init; } = 1.0;
    public double CameraOut { get; init; } = 8.0;
    public double CameraAhead { get; init; } = 3.0;
    public double CameraHeight { get; init; } = 1.6;
    public double LookHeight { get; init; } = 1.5;
    public double SlateOut { get; init; } = 2.6;
    public double SlateAside { get; init; } = 0.7;
    public double EmoteAt { get; init; } = 0.8;
    public float FlashRange { get; init; } = 18;
    public float FlashStrength { get; init; } = 3.0f;
}

/// <summary>The logbook (memory.json <c>logbook</c>; GDD §11, note 721). Field docs live in the file.</summary>
public sealed record LogbookTuning
{
    public string Place { get; init; } = "cab";
    public double Height { get; init; } = 1.25;
    public double Reach { get; init; } = 1.9;
    public double LookDegrees { get; init; } = 22;
}

/// <summary>Someone in the crew photo (note 721): who, alive or not, the emote they're holding, and a sooty face (note 717).</summary>
public readonly record struct Sitter(byte Id, string Name, bool Alive, Emote Emote = Emote.None, bool Sooted = false, int? Looks = null);

/// <summary>
/// What's in the photo besides the crew (note 721): the crate at the line's end with the dead's caps on it (each in its
/// crewmate's colours, by <see cref="Crewmate.Variant"/>), the slate on its easel with the night's tally chalked on it, and
/// where the flash went off. World positions; yaws face the lens (0 faces world −Z, as a crewmate's).
/// </summary>
public sealed record PhotoProps(Double3 Crate, double CrateYaw, IReadOnlyList<int> Caps, Double3 Slate, double SlateYaw, IReadOnlyList<string> Chalk,
    Double3 Flash, float FlashRange, float FlashStrength);

/// <summary>The crew photo staged: the lens, the figures stood for it, and the props.</summary>
public sealed record PhotoStaging(Camera Camera, IReadOnlyList<Crewmate> Crew, PhotoProps Props);

/// <summary>
/// The crew photo at the gate (GDD §9 Arrival; the director, 14 Oct 2026; ARCHITECTURE §8 note 721): on a delivered night,
/// as the train stands in at the terminus, the town photographer's flash. The living stand in a line beside the engine in the
/// yard, each in the emote they're holding (or stood), bots too; the dead are their caps on a crate at the line's end; the
/// night's tally is chalked on a slate on an easel. Presentation only, staged on every machine from the replicated world
/// alone (who, alive, their emote and soot; the engine where it stands), so every machine's photo is the same photo.
/// <para>U1's yard photographer (docs/design/upgrades.md §4) takes a departure photo with the same call (<see cref="Stage"/>
/// on the departure's engine) and keeps it with <see cref="BookmarkAlbum.KeepPhoto"/>.</para>
/// </summary>
public static class CrewPhoto
{
    /// <summary>
    /// Who's in the photo, from the replicated crew: the living by id (each holding the emote they're at, sooty if sooted; the
    /// sit and the lean aside, note 722: stood in a line, there's no edge to sit on),
    /// then the dead by id. Bots stand in like anyone.
    /// </summary>
    public static IReadOnlyList<Sitter> Sitters(World world, IEnumerable<(int Id, PlayerState State)> crew) =>
        [.. crew.Where(c => c.Id is >= 0 and <= byte.MaxValue).DistinctBy(c => c.Id).OrderBy(c => c.State.Alive ? 0 : 1).ThenBy(c => c.Id).Select(c =>
            new Sitter((byte)c.Id, IncidentLog.NameOf(world, c.Id), c.State.Alive,
                c.State.Alive && Art.CrewActs.EmoteOf(c.Id, c.State with { Velocity = default }, world).Kind is var held && !EmoteRules.Held(held) ? held : Emote.None,
                c.State.Has(PlayerFlags.Sooted), world.OutfitOf(c.Id) is var outfit && outfit != c.Id ? outfit : null))];

    /// <summary>
    /// The slate's chalk (note 721): the line's name, the night's number, and the net. "FRONTIER 7" / "NIGHT 3" / "NET 1450".
    /// </summary>
    public static IReadOnlyList<string> Chalk(string route, int night, double net)
    {
        var lines = new List<string> { route.ToUpperInvariant() };
        if (night > 0)
            lines.Add($"NIGHT {night}");
        lines.Add($"NET {net:0}");
        return lines;
    }

    /// <summary>A route's name for the slate and the page: "frontier:7" or "frontier-7" as "Frontier 7".</summary>
    public static string RouteName(string? route)
    {
        if (string.IsNullOrWhiteSpace(route))
            return "The line";
        string s = route.Replace(':', ' ').Replace('-', ' ').Trim();
        // "deepTerritory 3": the tier as said.
        s = Sim.Run.IncidentLog.Spoken(s);
        return char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();
    }

    /// <summary>
    /// The side of the track the photo's taken on (+1 the line's right, −1 its left): away from the town's square, whose side
    /// of the yard has the platform's canopy and its posts between any lens and the engine; the yard's open on the other.
    /// </summary>
    public static int YardSide(int? squareSide) => squareSide is < 0 ? 1 : squareSide is > 0 ? -1 : 1;

    /// <summary>
    /// Stages the photo beside the engine (frame 0) on <paramref name="side"/> of the track (+1 its right, −1 its left:
    /// <see cref="YardSide"/>). The living stand in a line, in <paramref name="crew"/>'s order from
    /// the picture's left, facing the lens; the crate with the dead's caps at the line's right-hand end; the slate in front. Deterministic: the same
    /// engine, crew and side give the same photo on every machine.
    /// </summary>
    public static PhotoStaging Stage(CarFrame engine, int side, IReadOnlyList<Sitter> crew, IReadOnlyList<string> chalk, PhotoTuning t)
    {
        side = side < 0 ? -1 : 1;
        // Level along and across the engine, at rail height under its middle.
        var forward = ((engine.Back * -1) with { Y = 0 }).Normalized;
        var right = (engine.Right with { Y = 0 }).Normalized * side;
        var ground = engine.Origin;
        Double3 At(double along, double across, double up = 0) => ground + forward * along + right * across + Double3.Up * up;

        var living = crew.Where(c => c.Alive).ToList();
        var dead = crew.Where(c => !c.Alive).ToList();
        double middle = t.Ahead;
        var lens = At(middle + t.CameraAhead, t.Out + t.CameraOut, t.CameraHeight);
        var camera = Camera.LookAt(lens, At(middle + t.CameraAhead * 0.25, t.Out, t.LookHeight), t.Fov);
        // Across the picture, left to right, as the lens sees it: its right is the engine's front on the engine's right side,
        // its back on the left.
        Double3 Across(double x, double outward, double up = 0) => At(middle + side * x, outward, up);
        var figures = new List<Crewmate>(living.Count);
        for (int i = 0; i < living.Count; i++)
        {
            var feet = Across((i - (living.Count - 1) / 2.0) * t.Spacing, t.Out);
            var c = living[i];
            figures.Add(new Crewmate(c.Id, feet, Facing(feet, lens), true, Looks: c.Looks, Emote: c.Emote,
                EmoteSeconds: c.Emote == Emote.None ? 0 : t.EmoteAt, Sooted: c.Sooted));
        }
        // The crate at the line's right-hand end, the slate on the left in front.
        var crate = Across((Math.Max(1, living.Count) - 1) / 2.0 * t.Spacing + t.CrateGap, t.Out);
        var slate = Across(-(Math.Max(1, living.Count) - 1) / 2.0 * t.Spacing - t.SlateAside, t.Out + t.SlateOut);
        var props = new PhotoProps(crate, Facing(crate, lens), [.. dead.Select(d => d.Looks ?? d.Id)], slate, Facing(slate, lens), chalk,
            lens, t.FlashRange, t.FlashStrength);
        return new PhotoStaging(camera, figures, props);
    }

    /// <summary>The yaw that faces from <paramref name="from"/> to <paramref name="to"/>, level (0 faces world −Z).</summary>
    static double Facing(Double3 from, Double3 to)
    {
        var d = to - from;
        return Math.Atan2(-d.X, -d.Z);
    }

    /// <summary>
    /// The photo's text sidecar (note 721), kept beside its PNG: what it is, who's in it from left to right, whose caps are
    /// on the crate, and the slate.
    /// </summary>
    public static string Sidecar(IReadOnlyList<Sitter> crew, IReadOnlyList<string> chalk, string crewName, string date)
    {
        var text = new StringBuilder();
        text.AppendLine($"DARK TERRITORY. THE CREW PHOTO AT THE GATE{(crewName.Length > 0 ? $": {crewName.ToUpperInvariant()}" : "")}. {date}.");
        text.AppendLine();
        text.AppendLine("ON THE SLATE: " + string.Join(" / ", chalk));
        var living = crew.Where(c => c.Alive).ToList();
        if (living.Count > 0)
            text.AppendLine("STANDING, LEFT TO RIGHT: " + string.Join(", ", living.Select(c => c.Name + (c.Emote != Emote.None ? $" ({c.Emote.ToString().ToLowerInvariant()})" : "")
                + (c.Sooted ? " (sooty)" : ""))));
        var dead = crew.Where(c => !c.Alive).ToList();
        if (dead.Count > 0)
            text.AppendLine("CAPS ON THE CRATE: " + string.Join(", ", dead.Select(c => c.Name)));
        return text.ToString();
    }
}
