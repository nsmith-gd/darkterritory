namespace DarkTerritory.Game.Telemetry;

/// <summary>How one send went: sent (the sessions can be marked so), worth trying again later, or refused for good.</summary>
public enum UploadOutcome : byte { Sent, Retry, Refused }

/// <summary>What a send did: its outcome, and where the data went or why not.</summary>
public sealed record UploadResult(UploadOutcome Outcome, string Detail);

/// <summary>
/// Where finished sessions go (queue #626, note 919; the plan, note 907). <see cref="FileTelemetryUpload"/> is the one there
/// is: the sessions bundled into a file a tester sends with a problem report (note 452). An <c>https</c> sink is the
/// director's call (note 907: a small self-hosted ingest, a hosted analytics service, or none), and implements this when
/// it's made. Whatever it is, the client holds no secret but a write-only ingest key, never note 516's feedback token.
/// </summary>
public interface ITelemetryUpload
{
    string Name { get; }
    /// <summary>Sends <paramref name="batch"/> (every line of each session, oldest first) in one go.</summary>
    Task<UploadResult> SendAsync(IReadOnlyList<SpooledSession> batch, CancellationToken cancel);
}

/// <summary>
/// The <c>file</c> sink: the batch's sessions, one after another, in one JSON-lines file (<c>playtest-data-&lt;stamp&gt;.jsonl</c>)
/// in <paramref name="directory"/>. The app points it at the folder a problem report was just written to, so the tester's
/// mail carries the data with the report.
/// </summary>
public sealed class FileTelemetryUpload(string directory, Func<DateTime>? now = null) : ITelemetryUpload
{
    public string Name => "file";
    public string Directory { get; } = directory;

    public async Task<UploadResult> SendAsync(IReadOnlyList<SpooledSession> batch, CancellationToken cancel)
    {
        string path = System.IO.Path.Combine(Directory, $"playtest-data-{(now ?? (() => DateTime.Now))():yyyyMMdd-HHmmss}.jsonl");
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            await using var output = new FileStream(path, FileMode.Create, FileAccess.Write);
            foreach (var s in batch)
            {
                await using var input = new FileStream(s.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                await input.CopyToAsync(output, cancel);
            }
            return new UploadResult(UploadOutcome.Sent, path);
        }
        catch (IOException e)
        {
            return new UploadResult(UploadOutcome.Retry, e.Message);
        }
        catch (UnauthorizedAccessException e)
        {
            return new UploadResult(UploadOutcome.Refused, e.Message);
        }
    }
}

/// <summary>What one pump did: the sessions sent, dropped by the caps, and the send's result (null when nothing was sent).</summary>
public sealed record PumpReport(int Sent, int Dropped, int Pending, UploadResult? Result, string? Held);

/// <summary>
/// Sends the spool's finished sessions (queue #626, note 919): between nights only, never during one; a batch at a time, up
/// to <see cref="UploadTuning.MaxBatchBytes"/>; a failed batch waits its backoff and is tried again, up to
/// <see cref="UploadTuning.Attempts"/> times, then left for the next launch. Only with consent: off, it sends nothing.
/// </summary>
public sealed class TelemetryUploader(TelemetrySpool spool, ITelemetryUpload sink, UploadTuning t, ITelemetryGate gate, Func<DateTime>? clock = null)
{
    readonly Func<DateTime> _clock = clock ?? (() => DateTime.UtcNow);
    int _failures;
    DateTime _next = DateTime.MinValue;

    public ITelemetryUpload Sink => sink;
    /// <summary>The upload gave up for this launch (<see cref="UploadTuning.Attempts"/> failures in a row, or a refusal).</summary>
    public bool GaveUp { get; private set; }

    /// <summary>
    /// One go: trims the spool, then sends the oldest batch of finished sessions if it may (consent on, not in a night, not
    /// waiting out a backoff, not given up). <paramref name="writing"/> is this launch's own session, never sent while open,
    /// unless <paramref name="withWriting"/>: a problem report's bundle carries the night it was written in (as far as it's
    /// been written), and the session stays unsent, to go again whole. That bundle is the player's own doing, so it goes in a
    /// night too; the between-nights rule is for what's sent of the game's own accord.
    /// </summary>
    public async Task<PumpReport> PumpAsync(bool inNight, string? writing, bool withWriting = false, CancellationToken cancel = default)
    {
        int dropped = spool.Trim(writing).Count;
        var pending = spool.Pending(writing);
        var open = withWriting && writing is not null ? spool.Sessions().FirstOrDefault(s => s.Id == writing && !s.Sent) : null;
        string? held = !gate.Recording ? "no consent" : inNight && open is null ? "in a night" : GaveUp ? "gave up" : _clock() < _next ? "backing off"
            : pending.Count == 0 && open is null ? "nothing to send" : null;
        if (held is not null)
            return new PumpReport(0, dropped, pending.Count, null, held);
        var batch = Batch(pending);
        var result = await sink.SendAsync(open is null ? batch : [.. batch, open], cancel);
        switch (result.Outcome)
        {
            case UploadOutcome.Sent:
                foreach (var s in batch)
                    spool.MarkSent(s);
                _failures = 0;
                _next = DateTime.MinValue;
                return new PumpReport(batch.Count, dropped, pending.Count - batch.Count, result, null);
            case UploadOutcome.Retry:
                _failures++;
                GaveUp = _failures >= t.Attempts;
                _next = _clock().AddSeconds(t.Backoff[Math.Min(_failures - 1, t.Backoff.Length - 1)]);
                break;
            default:
                GaveUp = true;
                break;
        }
        return new PumpReport(0, dropped, pending.Count, result, null);
    }

    /// <summary>The oldest sessions that fit in one batch (the first always goes, however big: a session's file is capped smaller).</summary>
    List<SpooledSession> Batch(List<SpooledSession> pending)
    {
        var batch = new List<SpooledSession>();
        long bytes = 0;
        foreach (var s in pending)
        {
            if (batch.Count > 0 && bytes + s.Bytes > t.MaxBatchBytes)
                break;
            batch.Add(s);
            bytes += s.Bytes;
        }
        return batch;
    }

    /// <summary>
    /// The network sink the spool is pumped to between nights (queue #637, note 943): PostHog when it's on with a project key,
    /// else none (nothing leaves the machine of the game's own accord).
    /// </summary>
    public static ITelemetryUpload? NetworkSinkFor(UploadTuning t, HttpMessageHandler? handler = null) =>
        t.PostHog.Ready ? new PostHogTelemetryUpload(t.PostHog, handler) : null;

    /// <summary>The sink tuning names, made: <c>file</c> into <paramref name="fileDirectory"/>; <c>none</c> null (the network one is <see cref="NetworkSinkFor"/>).</summary>
    public static ITelemetryUpload? SinkFor(UploadTuning t, string fileDirectory) => t.Sink switch
    {
        "file" => new FileTelemetryUpload(fileDirectory),
        _ => null,
    };
}
