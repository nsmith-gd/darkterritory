using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DarkTerritory.Game.Telemetry;

/// <summary>
/// The PostHog sink's settings (content/tuning/telemetry.json <c>upload.postHog</c>; queue #637, note 943; the director's
/// backend answer, 20 Oct: a hosted analytics service, PostHog). Off, or with no project key, no sink is made and nothing is
/// sent. The project key is PostHog's write-only "project API key": it can add events and read nothing, so it's no secret
/// in a client (note 907); the director makes the project and its key.
/// </summary>
/// <param name="On">Send the spool's sessions to PostHog between nights.</param>
/// <param name="Host">The PostHog ingest host (US or EU cloud, or a self-hosted one); https only.</param>
/// <param name="ProjectKey">The project's write-only API key (phc_...); empty: nothing is sent.</param>
/// <param name="EventsPerRequest">One request carries at most this many events; a batch of sessions goes in as many as it needs.</param>
/// <param name="TimeoutSeconds">A request not answered in this long is a retry, not a hang.</param>
public sealed record PostHogTuning(bool On = false, string Host = "https://us.i.posthog.com", string ProjectKey = "", int EventsPerRequest = 500,
    double TimeoutSeconds = 20)
{
    /// <summary>Whether a sink can be made: on, a key, and an https host.</summary>
    public bool Ready => On && ProjectKey.Trim().Length > 0 && Uri.TryCreate(Host, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps;
}

/// <summary>
/// The PostHog sink (queue #637, note 943): each of a batch's sessions, line by line, as PostHog events through its batch
/// capture endpoint (<c>POST {host}/batch/</c>). Each line is one event, <c>dt_&lt;type&gt;</c>, its fields its properties. Its
/// <c>distinct_id</c> is the session line's anonymous install id (#625; the session id when there's none), with no person
/// profile made (<c>$process_person_profile</c> false). Each event's time is the session's start, reckoned from when its file was last
/// written less its last line's clock, plus the line's own <c>t</c>. Answers: 2xx sent; 429, 5xx, a timeout or no network
/// tried again later (the uploader's backoff); any other 4xx refused for this launch (a wrong key won't come right by
/// retrying).
/// </summary>
public sealed class PostHogTelemetryUpload(PostHogTuning t, HttpMessageHandler? handler = null) : ITelemetryUpload
{
    readonly HttpClient _http = new(handler ?? new HttpClientHandler()) { Timeout = TimeSpan.FromSeconds(t.TimeoutSeconds) };

    public string Name => "posthog";
    public PostHogTuning Tuning => t;
    public Uri Endpoint => new(new Uri(t.Host.TrimEnd('/') + "/"), "batch/");

    public async Task<UploadResult> SendAsync(IReadOnlyList<SpooledSession> batch, CancellationToken cancel)
    {
        if (!t.Ready)
            return new UploadResult(UploadOutcome.Refused, "no PostHog project key");
        var events = new List<JsonObject>();
        foreach (var s in batch)
            events.AddRange(Events(s));
        int requests = 0;
        foreach (var chunk in events.Chunk(Math.Max(1, t.EventsPerRequest)))
        {
            var body = new JsonObject { ["api_key"] = t.ProjectKey.Trim(), ["batch"] = new JsonArray([.. chunk]) };
            try
            {
                using var response = await _http.PostAsync(Endpoint, new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"), cancel);
                if (!response.IsSuccessStatusCode)
                    return new UploadResult(Retryable(response.StatusCode) ? UploadOutcome.Retry : UploadOutcome.Refused,
                        $"PostHog answered {(int)response.StatusCode} after {requests} of {Requests(events.Count)} requests");
            }
            catch (HttpRequestException e)
            {
                return new UploadResult(UploadOutcome.Retry, e.Message);
            }
            catch (TaskCanceledException) when (!cancel.IsCancellationRequested)
            {
                return new UploadResult(UploadOutcome.Retry, "PostHog timed out");
            }
            requests++;
        }
        return new UploadResult(UploadOutcome.Sent, $"{events.Count} events in {requests} requests to {Endpoint.Host}");
    }

    int Requests(int events) => (events + Math.Max(1, t.EventsPerRequest) - 1) / Math.Max(1, t.EventsPerRequest);

    static bool Retryable(HttpStatusCode code) => code == HttpStatusCode.TooManyRequests || code == HttpStatusCode.RequestTimeout || (int)code >= 500;

    /// <summary>A session's lines as PostHog events (what <c>dt telemetry send --dry-run</c> prints); a line that isn't JSON is left out.</summary>
    public static List<JsonObject> Events(SpooledSession s)
    {
        var lines = new List<JsonObject>();
        foreach (var line in File.ReadLines(s.Path))
        {
            try
            {
                if (JsonNode.Parse(line) is JsonObject o)
                    lines.Add(o);
            }
            catch (JsonException)
            {
            }
        }
        var session = lines.FirstOrDefault(l => l["type"]?.GetValue<string>() == "session");
        string id = session?["session"]?.GetValue<string>() ?? s.Id;
        string who = session?["install"]?.GetValue<string>() is { Length: > 0 } install ? install : id;
        double last = lines.Select(l => l["t"]?.GetValue<double>() ?? 0).DefaultIfEmpty(0).Max();
        var start = s.Written - TimeSpan.FromSeconds(last);
        var events = new List<JsonObject>();
        foreach (var l in lines)
        {
            var props = new JsonObject
            {
                ["distinct_id"] = who,
                ["$process_person_profile"] = false,
                ["session"] = id,
            };
            foreach (var (k, v) in l)
                if (k is not ("type" or "session"))
                    props[k] = v?.DeepClone();
            double at = l["t"]?.GetValue<double>() ?? 0;
            events.Add(new JsonObject
            {
                ["event"] = "dt_" + (l["type"]?.GetValue<string>() ?? "line"),
                ["properties"] = props,
                ["timestamp"] = start.AddSeconds(at).ToUniversalTime().ToString("o", System.Globalization.CultureInfo.InvariantCulture),
            });
        }
        return events;
    }
}
