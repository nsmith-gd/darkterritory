using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DarkTerritory.Dev.Feedback;

/// <summary>
/// Sends the director's notes where every agent can take them (note 516): one commit per note on the repository's
/// <c>feedback</c> branch, through GitHub's Git Data API (blobs, a tree, a commit, the branch moved on), so a note's files
/// land together or not at all. The branch is its own history (it starts from nothing, not from main), holding
/// <c>notes/&lt;id&gt;/…</c> and <c>recordings/&lt;night&gt;.dtrec</c>. Only when a token's set: a developer build on the director's
/// machine, never a player's (there's none of this in one).
/// </summary>
public sealed class FeedbackUpload
{
    readonly HttpClient _http;
    readonly string _repo, _branch;

    public FeedbackUpload(string repo, string token, string branch = "feedback", HttpClient? http = null, string api = "https://api.github.com/")
    {
        _repo = repo;
        _branch = branch;
        _http = http ?? new HttpClient();
        _http.BaseAddress = new Uri(api);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("DarkTerritory-dev-feedback");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public string Repo => _repo;
    public string Branch => _branch;

    /// <summary>
    /// The uploader the director's machine is set up for: a token in <c>DT_FEEDBACK_TOKEN</c> or the app data's
    /// <c>feedback-token.txt</c> (a fine-grained token with Contents read and write on the repository), the repository in
    /// <c>DT_FEEDBACK_REPO</c> (nsmith-gd/darkterritory by default). Null, with no token: the notes stay on this machine.
    /// </summary>
    public static FeedbackUpload? FromEnvironment()
    {
        string? token = Environment.GetEnvironmentVariable("DT_FEEDBACK_TOKEN");
        string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DarkTerritory", "feedback-token.txt");
        if (string.IsNullOrWhiteSpace(token) && File.Exists(file))
            token = File.ReadAllText(file);
        if (string.IsNullOrWhiteSpace(token))
            return null;
        string repo = Environment.GetEnvironmentVariable("DT_FEEDBACK_REPO") is { Length: > 0 } r ? r : "nsmith-gd/darkterritory";
        return new FeedbackUpload(repo, token.Trim());
    }

    /// <summary>A note's bundle (and its night's recording, when there's one), as one commit. False if it couldn't be sent.</summary>
    public async Task<bool> SendAsync(string bundle, string? recording, CancellationToken cancel = default)
    {
        string id = Path.GetFileName(bundle.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var files = Directory.EnumerateFiles(bundle).Order(StringComparer.Ordinal).Select(f => ($"notes/{id}/{Path.GetFileName(f)}", f)).ToList();
        if (recording is not null && File.Exists(recording))
            files.Add(($"recordings/{Path.GetFileName(recording)}", recording));
        return await CommitAsync(files, $"Feedback {id}", cancel).ConfigureAwait(false);
    }

    /// <summary>Files (repository path, local path) committed onto the branch together; the branch is made if it isn't there.</summary>
    public async Task<bool> CommitAsync(IReadOnlyList<(string Path, string File)> files, string message, CancellationToken cancel = default)
    {
        try
        {
            var blobs = new List<(string Path, string Sha)>();
            foreach (var (path, file) in files)
            {
                // A recording still being written is read as far as it's got (it's opened shared, so the recorder carries on).
                byte[] bytes;
                using (var s = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    using var copy = new MemoryStream();
                    await s.CopyToAsync(copy, cancel).ConfigureAwait(false);
                    bytes = copy.ToArray();
                }
                var blob = await PostAsync("git/blobs", new JsonObject { ["content"] = Convert.ToBase64String(bytes), ["encoding"] = "base64" }, cancel).ConfigureAwait(false);
                blobs.Add((path, (string)blob["sha"]!));
            }
            // The branch moved on by someone else between our read and our write: read it again and go on top (a few times).
            for (int attempt = 0; attempt < 4; attempt++)
            {
                var (head, tree) = await HeadAsync(cancel).ConfigureAwait(false);
                var entries = new JsonArray([.. blobs.Select(b => (JsonNode)new JsonObject { ["path"] = b.Path, ["mode"] = "100644", ["type"] = "blob", ["sha"] = b.Sha })]);
                var treeBody = new JsonObject { ["tree"] = entries };
                if (tree is not null)
                    treeBody["base_tree"] = tree;
                var newTree = await PostAsync("git/trees", treeBody, cancel).ConfigureAwait(false);
                var commitBody = new JsonObject { ["message"] = message, ["tree"] = (string)newTree["sha"]!, ["parents"] = new JsonArray(head is null ? [] : [(JsonNode)head]) };
                var commit = await PostAsync("git/commits", commitBody, cancel).ConfigureAwait(false);
                string sha = (string)commit["sha"]!;
                using var response = head is null
                    ? await SendAsync(HttpMethod.Post, "git/refs", new JsonObject { ["ref"] = $"refs/heads/{_branch}", ["sha"] = sha }, cancel).ConfigureAwait(false)
                    : await SendAsync(HttpMethod.Patch, $"git/refs/heads/{_branch}", new JsonObject { ["sha"] = sha, ["force"] = false }, cancel).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                    return true;
                if ((int)response.StatusCode != 422)
                    return false;
            }
            return false;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>The branch's head commit and its tree, or nulls when there's no branch yet.</summary>
    async Task<(string? Head, string? Tree)> HeadAsync(CancellationToken cancel)
    {
        using var refResponse = await _http.GetAsync($"repos/{_repo}/git/ref/heads/{_branch}", cancel).ConfigureAwait(false);
        if (refResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
            return (null, null);
        refResponse.EnsureSuccessStatusCode();
        var reference = JsonNode.Parse(await refResponse.Content.ReadAsStringAsync(cancel).ConfigureAwait(false))!;
        string head = (string)reference["object"]!["sha"]!;
        using var commitResponse = await _http.GetAsync($"repos/{_repo}/git/commits/{head}", cancel).ConfigureAwait(false);
        commitResponse.EnsureSuccessStatusCode();
        var commit = JsonNode.Parse(await commitResponse.Content.ReadAsStringAsync(cancel).ConfigureAwait(false))!;
        return (head, (string)commit["tree"]!["sha"]!);
    }

    async Task<JsonNode> PostAsync(string path, JsonObject body, CancellationToken cancel)
    {
        using var response = await SendAsync(HttpMethod.Post, path, body, cancel).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false))!;
    }

    Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, JsonObject body, CancellationToken cancel) =>
        _http.SendAsync(new HttpRequestMessage(method, $"repos/{_repo}/{path}") { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") }, cancel);
}
