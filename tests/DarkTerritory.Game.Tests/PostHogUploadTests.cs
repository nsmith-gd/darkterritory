using System.Net;
using System.Text.Json.Nodes;
using Ballast;
using DarkTerritory.Game.Telemetry;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The PostHog sink (queue #637, note 943), against a fake PostHog:
/// <list type="bullet">
/// <item>no key, no sink, and the game ships without one;</item>
/// <item>each session line goes as a <c>dt_&lt;type&gt;</c> event under the anonymous install id, with no person profile;</item>
/// <item>the batch is split by <c>eventsPerRequest</c>;</item>
/// <item>a 5xx or no network is tried again later, a wrong key isn't;</item>
/// <item>nothing is sent without consent, and what's sent is marked so.</item>
/// </list>
/// </summary>
public sealed class PostHogUploadTests : IDisposable
{
    static readonly string Content = DataFile.FindContentRoot();
    static CancellationToken Ct => TestContext.Current.CancellationToken;
    readonly string _dir = Path.Combine(Path.GetTempPath(), "dt-posthog-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    static readonly PostHogTuning Keyed = new(On: true, Host: "https://ph.example", ProjectKey: "phc_test", EventsPerRequest: 3);

    /// <summary>A fake PostHog: answers each request with the next status given (the last repeated), and keeps the bodies.</summary>
    sealed class FakePostHog(params HttpStatusCode[] answers) : HttpMessageHandler
    {
        int _call;
        public List<(Uri Uri, JsonObject Body)> Requests { get; } = [];
        public bool Offline { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            if (Offline)
                throw new HttpRequestException("no network");
            Requests.Add((request.RequestUri!, JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancel))!.AsObject()));
            return new HttpResponseMessage(answers[Math.Min(_call++, answers.Length - 1)]);
        }
    }

    /// <summary>A finished session in the spool: its session line (with an install id) and <paramref name="minutes"/> minute lines.</summary>
    SpooledSession Session(TelemetrySpool spool, string id, int minutes)
    {
        Directory.CreateDirectory(_dir);
        var lines = new List<string> { $$"""{"schema":1,"type":"session","t":0,"session":"{{id}}","install":"inst-1","build":"1.0.0"}""" };
        for (int m = 1; m <= minutes; m++)
            lines.Add($$"""{"schema":1,"type":"minute","t":{{m * 60}},"night":1,"minute":{{m}},"km":{{m}}.5}""");
        File.WriteAllLines(spool.PathOf(id), lines);
        return spool.Sessions().Single(s => s.Id == id);
    }

    [Fact]
    public void NoKeyNoSinkAndTheGameShipsWithoutOne()
    {
        var shipped = TelemetryTuning.Load(Content).Upload;
        Assert.Equal("", shipped.PostHog.ProjectKey);
        Assert.Null(TelemetryUploader.NetworkSinkFor(shipped));
        Assert.Null(TelemetryUploader.NetworkSinkFor(new UploadTuning { PostHog = Keyed with { On = false } }));
        // Plain http is never a sink: the key and the data go over https or not at all.
        Assert.Null(TelemetryUploader.NetworkSinkFor(new UploadTuning { PostHog = Keyed with { Host = "http://ph.example" } }));
        Assert.IsType<PostHogTelemetryUpload>(TelemetryUploader.NetworkSinkFor(new UploadTuning { PostHog = Keyed }));
    }

    [Fact]
    public async Task EachLineIsAnEventUnderTheInstallIdSplitIntoRequests()
    {
        var spool = new TelemetrySpool(_dir, new SpoolTuning());
        var s = Session(spool, "sess-a", minutes: 4);
        var fake = new FakePostHog(HttpStatusCode.OK);
        var result = await new PostHogTelemetryUpload(Keyed, fake).SendAsync([s], Ct);
        Assert.Equal(UploadOutcome.Sent, result.Outcome);
        // Five lines, three to a request: two requests, to the batch endpoint, with the write-only key.
        Assert.Equal(2, fake.Requests.Count);
        Assert.All(fake.Requests, r => Assert.Equal("https://ph.example/batch/", r.Uri.ToString()));
        Assert.All(fake.Requests, r => Assert.Equal("phc_test", r.Body["api_key"]!.GetValue<string>()));
        var events = fake.Requests.SelectMany(r => r.Body["batch"]!.AsArray()).Select(e => e!.AsObject()).ToList();
        Assert.Equal(["dt_session", "dt_minute", "dt_minute", "dt_minute", "dt_minute"], events.Select(e => e["event"]!.GetValue<string>()));
        Assert.All(events, e => Assert.Equal("inst-1", e["properties"]!["distinct_id"]!.GetValue<string>()));
        Assert.All(events, e => Assert.False(e["properties"]!["$process_person_profile"]!.GetValue<bool>()));
        Assert.All(events, e => Assert.Equal("sess-a", e["properties"]!["session"]!.GetValue<string>()));
        Assert.Equal(2.5, events[2]["properties"]!["km"]!.GetValue<double>());
        // The minutes a minute apart, the last at the file's last write.
        var times = events.Select(e => DateTime.Parse(e["timestamp"]!.GetValue<string>(), null, System.Globalization.DateTimeStyles.RoundtripKind)).ToList();
        Assert.Equal(TimeSpan.FromMinutes(1), times[2] - times[1]);
        Assert.Equal(s.Written, times[^1].ToUniversalTime());
    }

    [Fact]
    public async Task AServerErrorOrNoNetworkIsTriedAgainAndAWrongKeyIsNot()
    {
        var spool = new TelemetrySpool(_dir, new SpoolTuning());
        var s = Session(spool, "sess-b", minutes: 1);
        Assert.Equal(UploadOutcome.Retry, (await new PostHogTelemetryUpload(Keyed, new FakePostHog(HttpStatusCode.ServiceUnavailable)).SendAsync([s], Ct)).Outcome);
        Assert.Equal(UploadOutcome.Retry, (await new PostHogTelemetryUpload(Keyed, new FakePostHog(HttpStatusCode.TooManyRequests)).SendAsync([s], Ct)).Outcome);
        Assert.Equal(UploadOutcome.Retry, (await new PostHogTelemetryUpload(Keyed, new FakePostHog { Offline = true }).SendAsync([s], Ct)).Outcome);
        Assert.Equal(UploadOutcome.Refused, (await new PostHogTelemetryUpload(Keyed, new FakePostHog(HttpStatusCode.Unauthorized)).SendAsync([s], Ct)).Outcome);
        Assert.Equal(UploadOutcome.Refused, (await new PostHogTelemetryUpload(Keyed with { ProjectKey = " " }, new FakePostHog()).SendAsync([s], Ct)).Outcome);
    }

    [Fact]
    public async Task NothingGoesWithoutConsentAndWhatGoesIsMarkedSent()
    {
        var spool = new TelemetrySpool(_dir, new SpoolTuning());
        Session(spool, "older", minutes: 2);
        Session(spool, "writing", minutes: 1);
        var fake = new FakePostHog(HttpStatusCode.OK);
        var gate = new FixedTelemetryGate(false, _dir);
        var t = new UploadTuning { PostHog = Keyed };
        var up = new TelemetryUploader(spool, TelemetryUploader.NetworkSinkFor(t, fake)!, t, gate);
        Assert.Equal("no consent", (await up.PumpAsync(inNight: false, "writing", cancel: Ct)).Held);
        Assert.Empty(fake.Requests);
        gate.Recording = true;
        var sent = await up.PumpAsync(inNight: false, "writing", cancel: Ct);
        Assert.Equal(1, sent.Sent);
        Assert.True(spool.Sessions().Single(s => s.Id == "older").Sent);
        // This launch's own session waits for the next launch: sent while open, it'd go twice.
        Assert.False(spool.Sessions().Single(s => s.Id == "writing").Sent);
        Assert.DoesNotContain(fake.Requests.SelectMany(r => r.Body["batch"]!.AsArray()), e => e!["properties"]!["session"]!.GetValue<string>() == "writing");
    }
}
