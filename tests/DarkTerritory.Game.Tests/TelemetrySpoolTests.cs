using Ballast;
using DarkTerritory.Game.Telemetry;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The playtest telemetry's spool and upload (queue #626, note 919; the plan, note 907):
/// <list type="bullet">
/// <item>a session's file stops at its cap and keeps its start;</item>
/// <item>the spool keeps to its caps, dropping sent sessions first, oldest first, and never the one being written;</item>
/// <item>a sent session stays where #625's DELETE clears it;</item>
/// <item>the upload sends nothing without consent or in a night, batches by size, backs off and gives up;</item>
/// <item>a problem report's bundle carries the night being written without marking it sent.</item>
/// </list>
/// </summary>
public sealed class TelemetrySpoolTests : IDisposable
{
    static readonly string Content = DataFile.FindContentRoot();
    static CancellationToken Ct => TestContext.Current.CancellationToken;
    readonly string _dir = Path.Combine(Path.GetTempPath(), "dt-spool-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    /// <summary>A finished session of <paramref name="bytes"/> bytes in the spool, written <paramref name="minutesAgo"/> minutes ago.</summary>
    SpooledSession Put(TelemetrySpool spool, string id, int bytes, int minutesAgo, bool sent = false)
    {
        Directory.CreateDirectory(_dir);
        string path = spool.PathOf(id, sent);
        File.WriteAllText(path, new string('x', bytes - 1) + "\n");
        File.SetLastWriteTimeUtc(path, new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc).AddMinutes(-minutesAgo));
        return spool.Sessions().Single(s => s.Id == id);
    }

    [Fact]
    public void ASessionsFileStopsAtItsCapAndKeepsItsStart()
    {
        var inner = new MemoryTelemetrySink();
        var capped = new CappedTelemetrySink(inner, maxBytes: 20);
        capped.Write("0123456789");
        capped.Write("abcdefghijk");
        // Once a line didn't fit, nothing after it goes either, however small: the file is a whole start, never a sieve.
        capped.Write("z");
        Assert.Equal(["0123456789"], inner.Lines);
        Assert.Equal(2, capped.Dropped);
        Assert.Equal(11, capped.Bytes);
    }

    [Fact]
    public void TheSpoolDropsSentSessionsFirstThenTheOldestButNeverTheOneBeingWritten()
    {
        var spool = new TelemetrySpool(_dir, new SpoolTuning(MaxSessionBytes: 1000, MaxTotalBytes: 250, MaxSessions: 10));
        Put(spool, "old", 100, minutesAgo: 50);
        Put(spool, "writing", 100, minutesAgo: 40);
        Put(spool, "sent", 100, minutesAgo: 10, sent: true);
        Put(spool, "new", 100, minutesAgo: 5);
        var dropped = spool.Trim("writing");
        // 400 bytes over a 250 cap: the sent one first, then the oldest unsent that isn't being written.
        Assert.Equal(["sent", "old"], dropped.Select(s => s.Id));
        Assert.Equal(["writing", "new"], spool.Sessions().Select(s => s.Id));
        var counted = new TelemetrySpool(_dir, new SpoolTuning(MaxSessions: 1));
        Assert.Equal(["new"], counted.Trim("writing").Select(s => s.Id));
    }

    [Fact]
    public void ASentSessionIsKeptBesideTheOthersWhereDeleteClearsIt()
    {
        var spool = new TelemetrySpool(_dir, new SpoolTuning());
        var s = Put(spool, "a", 10, minutesAgo: 1);
        Put(spool, "b", 10, minutesAgo: 2);
        spool.MarkSent(s);
        Assert.Equal(["b"], spool.Pending(writing: null).Select(p => p.Id));
        Assert.True(spool.Sessions().Single(p => p.Id == "a").Sent);
        // #625's DELETE empties the folder's files: the sent ones are among them, not in a folder it doesn't look in.
        Assert.Equal(2, Directory.GetFiles(_dir).Length);
    }

    sealed class Fake(params UploadOutcome[] outcomes) : ITelemetryUpload
    {
        int _call;
        public List<string[]> Batches { get; } = [];
        public string Name => "fake";

        public Task<UploadResult> SendAsync(IReadOnlyList<SpooledSession> batch, CancellationToken cancel)
        {
            Batches.Add([.. batch.Select(s => s.Id)]);
            var outcome = outcomes[Math.Min(_call++, outcomes.Length - 1)];
            return Task.FromResult(new UploadResult(outcome, outcome.ToString()));
        }
    }

    [Fact]
    public async Task NothingIsSentWithoutConsentOrDuringANight()
    {
        var spool = new TelemetrySpool(_dir, new SpoolTuning());
        Put(spool, "a", 10, minutesAgo: 1);
        var sink = new Fake(UploadOutcome.Sent);
        var gate = new FixedTelemetryGate(false, _dir);
        var up = new TelemetryUploader(spool, sink, new UploadTuning(), gate);
        Assert.Equal("no consent", (await up.PumpAsync(inNight: false, writing: null, cancel: Ct)).Held);
        gate.Recording = true;
        Assert.Equal("in a night", (await up.PumpAsync(inNight: true, writing: null, cancel: Ct)).Held);
        Assert.Empty(sink.Batches);
        var sent = await up.PumpAsync(inNight: false, writing: null, cancel: Ct);
        Assert.Equal(1, sent.Sent);
        Assert.Equal("nothing to send", (await up.PumpAsync(inNight: false, writing: null, cancel: Ct)).Held);
    }

    [Fact]
    public async Task ABatchIsTheOldestThatFitAndAFailedOneBacksOffThenGivesUp()
    {
        var spool = new TelemetrySpool(_dir, new SpoolTuning());
        Put(spool, "first", 60, minutesAgo: 30);
        Put(spool, "second", 60, minutesAgo: 20);
        Put(spool, "third", 60, minutesAgo: 10);
        var now = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
        var sink = new Fake(UploadOutcome.Retry, UploadOutcome.Retry);
        var up = new TelemetryUploader(spool, sink, new UploadTuning(MaxBatchBytes: 130, Attempts: 2, BackoffSeconds: [30, 90]),
            new FixedTelemetryGate(true, _dir), () => now);
        await up.PumpAsync(false, null, cancel: Ct);
        Assert.Equal(["first", "second"], sink.Batches[0]);
        Assert.Equal("backing off", (await up.PumpAsync(false, null, cancel: Ct)).Held);
        now = now.AddSeconds(31);
        await up.PumpAsync(false, null, cancel: Ct);
        Assert.True(up.GaveUp);
        now = now.AddHours(1);
        Assert.Equal("gave up", (await up.PumpAsync(false, null, cancel: Ct)).Held);
        Assert.Equal(2, sink.Batches.Count);
        Assert.All(spool.Sessions(), s => Assert.False(s.Sent));
    }

    [Fact]
    public async Task AProblemReportsBundleCarriesTheNightBeingWrittenAndLeavesItUnsent()
    {
        var spool = new TelemetrySpool(_dir, new SpoolTuning());
        var gate = new FixedTelemetryGate(true, _dir, "0123abcd");
        Put(spool, "earlier", 40, minutesAgo: 60);
        var recorder = spool.Recorder(gate);
        recorder.Begin(new SessionFacts("full", Dev: false, "test", "test", Vr: false, "1280x720", 1, VSync: true, FieldOfView: 75));
        // The session line goes with the first one recorded (note 913): a quit to the menu, mid-launch.
        recorder.Quit("menu");
        spool.Writing!.Flush();
        string reports = Path.Combine(_dir, "reports");
        var report = await new TelemetryUploader(spool, new FileTelemetryUpload(reports), new UploadTuning(), gate)
            .PumpAsync(inNight: true, recorder.SessionId, withWriting: true, cancel: Ct);
        // A bundle beside the report: the finished session, then this one as far as it's written (its session line).
        Assert.Equal(UploadOutcome.Sent, report.Result!.Outcome);
        var lines = File.ReadAllLines(report.Result.Detail);
        Assert.Equal(new string('x', 39), lines[0]);
        Assert.Contains("\"type\":\"session\"", lines[1]);
        Assert.Contains("\"type\":\"quit\"", lines[2]);
        Assert.True(spool.Sessions().Single(s => s.Id == "earlier").Sent);
        Assert.False(spool.Sessions().Single(s => s.Id == recorder.SessionId).Sent);
        (recorder.Closer as IDisposable)?.Dispose();
    }

    [Fact]
    public void TheTuningIsTheContentsAndItsSinkIsTheFile()
    {
        var t = TelemetryTuning.Load(Content);
        Assert.Equal("file", t.Upload.Sink);
        Assert.True(t.Spool.MaxSessionBytes <= t.Upload.MaxBatchBytes, "a session must fit in one batch");
        Assert.True(t.Spool.MaxSessionBytes < t.Spool.MaxTotalBytes);
        Assert.IsType<FileTelemetryUpload>(TelemetryUploader.SinkFor(t.Upload, _dir));
        // https waits on the director's choice of backend (note 907): no sink is made for it yet.
        Assert.Null(TelemetryUploader.SinkFor(t.Upload with { Sink = "https" }, _dir));
    }
}
