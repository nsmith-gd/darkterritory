using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Ballast;
using DarkTerritory.Dev.Feedback;
using DarkTerritory.Dev.Replay;
using DarkTerritory.Game;
using DarkTerritory.Sim;

namespace DarkTerritory.Dev.Tests;

/// <summary>
/// ARCHITECTURE §8 note 516: the director's notes from inside the game carry their moment (the recording and its tick, where
/// they stood, what was near), the moment replays from the recording, and the notes go up to the repository as one commit
/// each on a branch of their own.
/// </summary>
public class FeedbackTests
{
    static readonly string Content = DataFile.FindContentRoot();

    static string Scratch() => Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "dt-feedback-" + Guid.NewGuid().ToString("N"))).FullName;

    [Fact]
    public void ANoteCarriesItsMomentAndTheRecordingPlaysToIt()
    {
        string dir = Scratch();
        try
        {
            var recorder = new NightRecorder(Path.Combine(dir, "recordings"));
            FeedbackNote note;
            string bundle;
            using (var night = NetPlaySession.HostGame(Content, new SessionSetup(Route: "frontier:7", Cars: 4, Enemies: true), port: 0, bots: 2, tap: recorder))
            {
                for (int t = 0; t < 12 * SimConstants.TickRate; t++)
                    night.Step(default);
                note = FeedbackNote.Of("fb-test-1", DateTime.UtcNow, night, night.Host, recorder.Current) with { Text = "too quiet here" };
                Assert.True(recorder.Mark("feedback", new JsonObject { ["id"] = note.Id }));
                bundle = FeedbackBundle.Write(Path.Combine(dir, "notes"), note, null, [0.1f, -0.1f], 48000, null);
                for (int t = 0; t < 2 * SimConstants.TickRate; t++)
                    night.Step(default);
            }
            // What the note says of its moment.
            Assert.Equal(Path.GetFileName(recorder.Current), note.Recording);
            Assert.InRange(note.Tick, 12u * SimConstants.TickRate, 12u * SimConstants.TickRate + 60);
            Assert.Equal(3, note.Crew.Count);
            Assert.NotNull(note.Player);
            Assert.Contains(note.Tags, t => t.StartsWith("post:", StringComparison.Ordinal));
            Assert.Contains("phase:", string.Join(" ", note.Tags));
            Assert.Contains($"--to {note.Tick}", note.Replay);
            Assert.Equal(Report.Build().Commit, note.Commit);
            // The bundle, read back.
            Assert.True(File.Exists(Path.Combine(bundle, FeedbackBundle.NoteFile)));
            Assert.True(File.Exists(Path.Combine(bundle, FeedbackBundle.VoiceFile)));
            var listed = Assert.Single(FeedbackBundle.List(Path.Combine(dir, "notes")));
            Assert.Equal("too quiet here", listed.Note.Text);
            // And the moment, replayed: the recording plays to the note's tick, the note's mark is at it.
            using var replay = NightReplay.Open(recorder.Current!, Content);
            replay.Run(note.Tick);
            Assert.Equal(note.Tick, replay.Host.Tick);
            Assert.Null(replay.DivergedAt);
            replay.Run();
            var mark = Assert.Single(replay.Marks, m => m.Kind == "feedback");
            Assert.Equal(note.Tick, mark.Tick);
            Assert.Equal("fb-test-1", (string?)mark.Fields["id"]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task NotesGoUpAsOneCommitEachOnABranchOfTheirOwn()
    {
        string dir = Scratch();
        try
        {
            string bundle = Directory.CreateDirectory(Path.Combine(dir, "fb-1")).FullName;
            File.WriteAllText(Path.Combine(bundle, "note.json"), "{}");
            File.WriteAllBytes(Path.Combine(bundle, "frame.png"), [1, 2, 3]);
            string recording = Path.Combine(dir, "night.dtrec");
            File.WriteAllBytes(recording, [9, 9]);
            var github = new FakeGitHub();
            var upload = new FeedbackUpload("me/game", "token", http: new HttpClient(github));

            // The first: no branch yet, so its commit has no parent and its tree no base; the branch is made on it.
            Assert.True(await upload.SendAsync(bundle, recording, TestContext.Current.CancellationToken));
            var first = github.Commits.Single();
            Assert.Empty(first.Parents);
            Assert.Null(github.Trees[first.Tree].Base);
            Assert.Equal(["notes/fb-1/frame.png", "notes/fb-1/note.json", "recordings/night.dtrec"], github.Trees[first.Tree].Paths.Order());
            Assert.Equal(first.Sha, github.Head);
            Assert.Equal("Bearer token", github.Auth);

            // The next goes on top; and when the branch moved under it, it reads it again and goes on top of that.
            github.RefuseNextMove = true;
            Assert.True(await upload.CommitAsync([("notes/fb-2/note.json", Path.Combine(bundle, "note.json"))], "Feedback fb-2", TestContext.Current.CancellationToken));
            var last = github.Commits[^1];
            Assert.Equal([first.Sha], last.Parents);
            Assert.Equal(first.Tree, github.Trees[last.Tree].Base);
            Assert.Equal(last.Sha, github.Head);
            Assert.Equal(1, github.Refused);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ASpokenNoteIsAPlainWav()
    {
        string dir = Scratch();
        try
        {
            string path = Path.Combine(dir, "v.wav");
            Wav.Write(path, [0f, 1f, -1f, 0.5f], 48000);
            var b = File.ReadAllBytes(path);
            Assert.Equal(44 + 8, b.Length);
            Assert.Equal("RIFF", Encoding.ASCII.GetString(b, 0, 4));
            Assert.Equal("WAVE", Encoding.ASCII.GetString(b, 8, 4));
            Assert.Equal(48000, BitConverter.ToInt32(b, 24));
            Assert.Equal(short.MaxValue, BitConverter.ToInt16(b, 46));
            Assert.Equal(-short.MaxValue, BitConverter.ToInt16(b, 48));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>Just enough of GitHub's Git Data API for the uploader: refs, commits, trees and blobs, in memory.</summary>
    sealed class FakeGitHub : HttpMessageHandler
    {
        public sealed record Commit(string Sha, string Tree, string[] Parents);
        public sealed record Tree(string? Base, string[] Paths);
        public readonly List<Commit> Commits = new();
        public readonly Dictionary<string, Tree> Trees = new();
        public string? Head;
        public string? Auth;
        public bool RefuseNextMove;
        public int Refused;
        int _n;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Auth = request.Headers.Authorization?.ToString();
            string path = request.RequestUri!.AbsolutePath;
            var body = request.Content is null ? null : JsonNode.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
            HttpResponseMessage Json(JsonNode node, HttpStatusCode code = HttpStatusCode.OK) =>
                new(code) { Content = new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json") };
            switch (request.Method.Method, path)
            {
                case ("GET", "/repos/me/game/git/ref/heads/feedback"):
                    return Head is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : Json(new JsonObject { ["object"] = new JsonObject { ["sha"] = Head } });
                case ("GET", var p) when p.StartsWith("/repos/me/game/git/commits/", StringComparison.Ordinal):
                    return Json(new JsonObject { ["tree"] = new JsonObject { ["sha"] = Commits.Single(c => c.Sha == p.Split('/')[^1]).Tree } });
                case ("POST", "/repos/me/game/git/blobs"):
                    return Json(new JsonObject { ["sha"] = $"blob{++_n}" }, HttpStatusCode.Created);
                case ("POST", "/repos/me/game/git/trees"):
                    string tree = $"tree{++_n}";
                    Trees[tree] = new Tree((string?)body!["base_tree"], [.. body["tree"]!.AsArray().Select(e => (string)e!["path"]!)]);
                    return Json(new JsonObject { ["sha"] = tree }, HttpStatusCode.Created);
                case ("POST", "/repos/me/game/git/commits"):
                    var commit = new Commit($"commit{++_n}", (string)body!["tree"]!, [.. body["parents"]!.AsArray().Select(x => (string)x!)]);
                    Commits.Add(commit);
                    return Json(new JsonObject { ["sha"] = commit.Sha }, HttpStatusCode.Created);
                case ("POST", "/repos/me/game/git/refs"):
                    Head = (string)body!["sha"]!;
                    return Json(new JsonObject(), HttpStatusCode.Created);
                case ("PATCH", "/repos/me/game/git/refs/heads/feedback"):
                    if (RefuseNextMove)
                    {
                        // Someone else's note landed first: the branch is somewhere else now.
                        RefuseNextMove = false;
                        Refused++;
                        return new HttpResponseMessage(HttpStatusCode.UnprocessableEntity);
                    }
                    Head = (string)body!["sha"]!;
                    return Json(new JsonObject());
                default:
                    return new HttpResponseMessage(HttpStatusCode.BadRequest);
            }
        }
    }
}
