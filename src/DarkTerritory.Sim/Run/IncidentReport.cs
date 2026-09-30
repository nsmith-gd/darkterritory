using System.Text.Json;
using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Run;

/// <param name="Where">Where it happened, as the crew would say it: the site, or the km post.</param>
public sealed record ReportDeath(int Player, double Seconds, string Where, DeathCause Cause, bool DropOut);

/// <param name="By">Who broke them out.</param>
public sealed record ReportRescue(int Player, int By, double Seconds, string Site);

public sealed record ReportVote(int Player, EnemyKind Kind);

/// <summary>A commendation given at the run's end (D.12): from whom, to whom, which award.</summary>
public sealed record Commendation(int From, int To, string Award);

/// <summary>
/// The run-end screen's incident report (GDD App. D.12): the deaths (who and where), the rescues (who freed whom, where),
/// the bodies delivered and lost, the cars lost, what the dead voted for, and the bookmarks; and, with it, the night's
/// settlement (D.9) and the commendations given on the screen. The host makes it when the run's over and sends it to
/// everyone, again whenever a commendation's given.
/// </summary>
public sealed record IncidentReport(RunReport Run)
{
    public IReadOnlyList<ReportDeath> Deaths { get; init; } = [];
    public IReadOnlyList<ReportRescue> Rescues { get; init; } = [];
    public int BodiesDelivered => Run.BodiesDelivered;
    public int BodiesLost => Run.BodiesLost;
    public int CarsLost => Run.CarsLost;
    public IReadOnlyList<ReportVote> Votes { get; init; } = [];
    public IReadOnlyList<Bookmark> Bookmarks { get; init; } = [];
    public IReadOnlyList<Commendation> Commendations { get; init; } = [];
    /// <summary>Who was in the session at the end (the living, the dead, the lobbied): they can all give one.</summary>
    public IReadOnlyList<int> Session { get; init; } = [];

    static readonly JsonSerializerOptions Compact = new(DataFile.Options) { WriteIndented = false };
    public string ToJson() => JsonSerializer.Serialize(this, Compact);
    public static IncidentReport FromJson(string json) => JsonSerializer.Deserialize<IncidentReport>(json, Compact) ?? throw new InvalidDataException("no report");

    /// <summary>Its JSON, Brotli-compressed: what the host sends (in datagram-sized parts, <see cref="Messages.WriteReport"/>).</summary>
    public byte[] Compress()
    {
        using var buffer = new MemoryStream();
        using (var brotli = new System.IO.Compression.BrotliStream(buffer, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            brotli.Write(System.Text.Encoding.UTF8.GetBytes(ToJson()));
        return buffer.ToArray();
    }

    public static IncidentReport Decompress(byte[] data)
    {
        using var brotli = new System.IO.Compression.BrotliStream(new MemoryStream(data), System.IO.Compression.CompressionMode.Decompress);
        using var text = new StreamReader(brotli, System.Text.Encoding.UTF8);
        return FromJson(text.ReadToEnd());
    }

    /// <summary>The commendations one player has been given (their profile keeps these, D.12).</summary>
    public IEnumerable<Commendation> To(int player) => Commendations.Where(c => c.To == player);

    /// <summary>The report for a world whose run is over (host).</summary>
    public static IncidentReport Of(World world, IEnumerable<int> session)
    {
        var run = world.Run!;
        var holdouts = world.Holdouts;
        string Where(DeathRecord d) =>
            d.Site is { } site && holdouts?.All.FirstOrDefault(h => h.Site.Site == site) is { } h ? h.Site.Name
            : world.TrackPlan is { } plan ? $"km {plan.Km(d.Chainage):0.0}" : $"{d.Chainage / 1000:0.0} km";
        return new IncidentReport(run.Report!)
        {
            Deaths = [.. world.Deaths.Select(d => new ReportDeath(d.Player, d.Seconds, Where(d), d.Cause, d.DropOut))],
            Rescues = holdouts is null ? [] : [.. holdouts.History.Where(e => e.Event.Outcome == HoldoutOutcome.Freed)
                .Select(e => new ReportRescue(e.Event.Player, e.Event.By, Math.Round(e.Seconds, 1), holdouts.All[e.Event.Holdout].Site.Name))],
            Votes = [.. world.VoteLog.Select(v => new ReportVote(v.Player, v.Kind))],
            Bookmarks = [.. world.Dead.Bookmarks],
            Commendations = [.. world.Commendations],
            Session = [.. session.Order()],
        };
    }
}
