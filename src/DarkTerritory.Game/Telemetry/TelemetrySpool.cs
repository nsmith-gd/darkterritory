using Ballast;

namespace DarkTerritory.Game.Telemetry;

/// <summary>
/// The spool's and the upload's numbers (content/tuning/telemetry.json; queue #626, note 919; the plan, note 907): how big
/// one session's file may grow, how much the spool keeps, and how the upload batches and retries.
/// </summary>
public sealed record TelemetryTuning
{
    public const string File = "tuning/telemetry.json";
    public SpoolTuning Spool { get; init; } = new();
    public UploadTuning Upload { get; init; } = new();

    public static TelemetryTuning Load(string content)
    {
        string path = Path.Combine(content, File);
        return System.IO.File.Exists(path) ? DataFile.Load<TelemetryTuning>(path) : new();
    }
}

/// <param name="MaxSessionBytes">One session's file stops growing here: what's past it isn't written (a night stays well under it, note 913).</param>
/// <param name="MaxTotalBytes">The spool keeps at most this much on disk, the oldest sessions dropped first (sent ones before unsent).</param>
/// <param name="MaxSessions">And at most this many session files.</param>
public sealed record SpoolTuning(long MaxSessionBytes = 2_000_000, long MaxTotalBytes = 32_000_000, int MaxSessions = 200);

/// <param name="Sink">
/// Where finished sessions go: <c>file</c> (bundled beside a problem report, for the tester to send with it, note 452), or
/// <c>none</c>. <c>https</c> waits on the director's choice of backend (note 907): it's an interface, not a sink, until then.
/// </param>
/// <param name="MaxBatchBytes">One upload carries at most this much (a session bigger than it goes alone).</param>
/// <param name="Attempts">Tries a batch gets before the upload gives up until the next launch.</param>
/// <param name="BackoffSeconds">The wait after each failed try, the last repeated.</param>
public sealed record UploadTuning(string Sink = "file", long MaxBatchBytes = 4_000_000, int Attempts = 3, double[]? BackoffSeconds = null)
{
    public double[] Backoff => BackoffSeconds is { Length: > 0 } b ? b : [30, 120, 600];
}

/// <summary>A session's file in the spool: its id, path, size, when it was last written, and whether it's been sent.</summary>
public sealed record SpooledSession(string Id, string Path, long Bytes, DateTime Written, bool Sent);

/// <summary>
/// The local spool (queue #626, note 919; the plan, note 907): one JSON-lines file a session in the consent's sessions
/// folder (<see cref="ITelemetryGate.SessionsDirectory"/>, the folder #625's DELETE empties). A session's file is capped as
/// it's written (<see cref="CappedTelemetrySink"/>); the spool as a whole is trimmed, oldest first, never the session being
/// written. A sent session stays in the folder as <c>&lt;id&gt;.sent.jsonl</c> (so DELETE still clears it, and trimming takes
/// it before anything unsent) until the caps push it out.
/// </summary>
public sealed class TelemetrySpool(string directory, SpoolTuning t)
{
    const string Sent = ".sent.jsonl", Open = ".jsonl";

    public string Directory { get; } = directory;
    public SpoolTuning Tuning { get; } = t;

    /// <summary>A recorder writing a new session into the spool, its file capped (the app's, in place of <see cref="SessionRecorder.ToFile"/>).</summary>
    public SessionRecorder Recorder(ITelemetryGate gate, Func<double>? clock = null)
    {
        string id = Guid.NewGuid().ToString("N");
        Writing = new CappedTelemetrySink(new FileTelemetrySink(PathOf(id)), Tuning.MaxSessionBytes);
        return new SessionRecorder(Writing, gate, clock, id);
    }

    /// <summary>The last recorder's sink: flushed before its session's file is read while it's still being written.</summary>
    public CappedTelemetrySink? Writing { get; private set; }

    public string PathOf(string id, bool sent = false) => System.IO.Path.Combine(Directory, id + (sent ? Sent : Open));

    /// <summary>Every session in the spool, oldest first (by when it was last written, then by name, so ties don't depend on the disk).</summary>
    public List<SpooledSession> Sessions()
    {
        if (!System.IO.Directory.Exists(Directory))
            return [];
        return [.. System.IO.Directory.GetFiles(Directory, "*" + Open)
            .Select(f => new FileInfo(f))
            .Select(f => f.Name.EndsWith(Sent, StringComparison.Ordinal)
                ? new SpooledSession(f.Name[..^Sent.Length], f.FullName, f.Length, f.LastWriteTimeUtc, true)
                : new SpooledSession(f.Name[..^Open.Length], f.FullName, f.Length, f.LastWriteTimeUtc, false))
            .OrderBy(s => s.Written).ThenBy(s => s.Id, StringComparer.Ordinal)];
    }

    /// <summary>The finished sessions not yet sent, oldest first: everything unsent but <paramref name="writing"/> (this launch's own).</summary>
    public List<SpooledSession> Pending(string? writing) => [.. Sessions().Where(s => !s.Sent && s.Id != writing)];

    /// <summary>
    /// Holds the spool to its caps: drops sent sessions, oldest first, then unsent ones, until it's within both. Never the
    /// session being written (<paramref name="writing"/>). Returns what it dropped.
    /// </summary>
    public List<SpooledSession> Trim(string? writing)
    {
        var all = Sessions();
        long total = all.Sum(s => s.Bytes);
        int count = all.Count;
        var dropped = new List<SpooledSession>();
        foreach (var s in all.Where(s => s.Sent).Concat(all.Where(s => !s.Sent)))
        {
            if (total <= Tuning.MaxTotalBytes && count <= Tuning.MaxSessions)
                break;
            if (s.Id == writing)
                continue;
            try
            {
                System.IO.File.Delete(s.Path);
            }
            catch (IOException)
            {
                continue;
            }
            total -= s.Bytes;
            count--;
            dropped.Add(s);
        }
        return dropped;
    }

    /// <summary>A session's gone where the sink sends them: kept as sent until the caps push it out.</summary>
    public void MarkSent(SpooledSession s)
    {
        if (!s.Sent && System.IO.File.Exists(s.Path))
            System.IO.File.Move(s.Path, PathOf(s.Id, sent: true), overwrite: true);
    }
}

/// <summary>
/// A sink that stops at <paramref name="maxBytes"/> (the spool's per-session cap): a line that would take the file past it
/// isn't written, nor anything after it. A capped night keeps its start (the session, the first minutes) whole.
/// </summary>
public sealed class CappedTelemetrySink(ITelemetrySink inner, long maxBytes) : ITelemetrySink, IDisposable
{
    public long Bytes { get; private set; }
    /// <summary>Lines left out once the cap was reached.</summary>
    public int Dropped { get; private set; }
    public ITelemetrySink Inner => inner;

    public void Write(string line)
    {
        long size = System.Text.Encoding.UTF8.GetByteCount(line) + 1;
        if (Dropped > 0 || Bytes + size > maxBytes)
        {
            Dropped++;
            return;
        }
        Bytes += size;
        inner.Write(line);
    }

    public void Flush() => inner.Flush();

    public void Dispose() => (inner as IDisposable)?.Dispose();
}
