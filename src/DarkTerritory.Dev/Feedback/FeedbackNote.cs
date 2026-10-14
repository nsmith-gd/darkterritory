using System.Text.Json;
using Ballast;
using DarkTerritory.Game;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Dev.Feedback;

/// <summary>Where the director was when they made the note.</summary>
public sealed record FeedbackPlace(int Id, string? Name, bool Alive, int Health, string Post, string Surface, int Car, double[] At);

/// <summary>Something live in the night near them: what it is, what it's doing, how far off.</summary>
public sealed record FeedbackThreat(int Id, string Kind, string Phase, double Metres, int Car);

/// <summary>A crewmate as the census had them.</summary>
public sealed record FeedbackCrewmate(int Id, string? Name, bool Alive, string Post);

/// <summary>
/// One of the director's notes made from inside the game (ARCHITECTURE §8 note 516): what they typed or said, and the moment
/// it was made at, fixed so an agent can take it without asking: the build, the night and its recording with the tick (to
/// replay to it), where the director stood and what was near, the crew at their posts, and tags for whose work it is.
/// Written as <c>note.json</c> in its bundle, beside <c>frame.png</c> (what was on the screen), <c>voice.wav</c> (what was
/// said, if it was spoken) and <c>report.txt</c>/<c>.json</c> (the crash report's header and the game's last lines, note 452).
/// </summary>
public sealed record FeedbackNote
{
    public string Id { get; init; } = "";
    /// <summary>When it was made, UTC.</summary>
    public DateTime At { get; init; }
    /// <summary><c>typed</c> or <c>voice</c>.</summary>
    public string Kind { get; init; } = "typed";
    /// <summary>What was typed; for a spoken note, its transcript once there is one.</summary>
    public string? Text { get; init; }
    public double? VoiceSeconds { get; init; }
    public string Build { get; init; } = "";
    public string Commit { get; init; } = "";
    /// <summary>The night (a route spec, or a line), its car count.</summary>
    public string? Night { get; init; }
    /// <summary>The night's recording (its file name), when this machine was hosting it (note 515).</summary>
    public string? Recording { get; init; }
    /// <summary>The host's tick when the note was made: where the recording's played to.</summary>
    public uint Tick { get; init; }
    public double Seconds { get; init; }
    public double Km { get; init; }
    public double Kmh { get; init; }
    public string? Phase { get; init; }
    public FeedbackPlace? Player { get; init; }
    public IReadOnlyList<FeedbackThreat> Threats { get; init; } = [];
    public IReadOnlyList<FeedbackCrewmate> Crew { get; init; } = [];
    /// <summary>Whose work it might be: <c>enemy:Gannet</c> (near), <c>post:Walker</c>, <c>phase:AtFacility</c>, <c>dead</c>, <c>wreck</c>.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];
    /// <summary>The session's status line, as the window's title shows it.</summary>
    public string? Status { get; init; }
    /// <summary>The command that shows the moment: the recording played to the tick, drawn through the director's eyes.</summary>
    public string? Replay { get; init; }

    public string ToJson() => JsonSerializer.Serialize(this, DataFile.Options);
    public static FeedbackNote FromJson(string json) => JsonSerializer.Deserialize<FeedbackNote>(json, DataFile.Options)!;

    /// <summary>How far round the director a thing counts as near (a note's tags, and its threats).</summary>
    public const double NearMetres = 60, ListMetres = 300;

    /// <summary>The moment as it is: the host's world when this machine hosts, the session's mirror of it when it joined.</summary>
    public static FeedbackNote Of(string id, DateTime at, IPlaySession session, HostSession? host, string? recording)
    {
        var world = host?.World ?? session.World;
        var train = world.Train;
        var me = session.Player;
        int myId = session.PlayerId;
        var here = PlayerMotor.WorldPosition(me, train);
        const double behind = 150;
        var threats = world.ActiveEnemies.Where(e => !e.Gone)
            .Select(e => new FeedbackThreat(e.Id, e.Kind.ToString(), e.Phase.ToString(), Math.Round((e.WorldPosition(train) - here).Length, 1), e.Attached))
            .Where(t => t.Metres <= ListMetres).OrderBy(t => t.Metres).Take(12).ToList();
        var crew = (host?.Players.Select(p => (Id: (int)p.Id, p.State)) ?? session.CrewStates(1))
            .Select(c => new FeedbackCrewmate(c.Id, world.Names.GetValueOrDefault(c.Id), c.State.Alive, Census.PostOf(c.State, train, behind).ToString()))
            .OrderBy(c => c.Id).ToList();
        string post = Census.PostOf(me, train, behind).ToString();
        var tags = new SortedSet<string>(StringComparer.Ordinal) { $"post:{post}" };
        foreach (var t in threats.Where(t => t.Metres <= NearMetres))
            tags.Add($"enemy:{t.Kind}");
        if (world.Run is { } run)
            tags.Add($"phase:{run.Phase}");
        if (!me.Alive)
            tags.Add("dead");
        if (train.Wreck is not null)
            tags.Add("wreck");
        uint tick = host?.Tick ?? world.Tick;
        var (version, commit) = Report.Build();
        string? night = session.Route?.Name is { } name ? $"{name}, {train.OwnVehicles - 1} cars" : null;
        return new FeedbackNote
        {
            Id = id,
            At = at,
            Build = version,
            Commit = commit,
            Night = night,
            Recording = recording is null ? null : Path.GetFileName(recording),
            Tick = tick,
            Seconds = Math.Round(tick * SimConstants.TickSeconds, 1),
            Km = Math.Round(train.Dynamics.Distance / 1000, 3),
            Kmh = Math.Round(train.Dynamics.Speed * 3.6, 1),
            Phase = world.Run?.Phase.ToString(),
            Player = new FeedbackPlace(myId, world.Names.GetValueOrDefault(myId), me.Alive, me.Health, post, me.Surface.ToString(), me.Parent,
                [Math.Round(here.X, 2), Math.Round(here.Y, 2), Math.Round(here.Z, 2)]),
            Threats = threats,
            Crew = crew,
            Tags = [.. tags],
            Status = session.Status(),
            Replay = recording is null ? null
                : $"dt replay recordings/{Path.GetFileName(recording)} --to {tick} --shot out/shots/{id}.png --view eye:{myId}",
        };
    }
}
