using System.Collections.Specialized;
using System.Text.Json;
using Ballast;
using DarkTerritory.Editor;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Editor.Tests;

/// <summary>The designer's editor, against a scratch copy of content/ so the real files are never touched.</summary>
public sealed class EditorTests : IDisposable
{
    readonly string _content;
    readonly EditorServer _editor;

    public EditorTests()
    {
        _content = Path.Combine(Path.GetTempPath(), "dt-editor-" + Guid.NewGuid().ToString("N"), "content");
        Copy(DataFile.FindContentRoot(), _content);
        _editor = new EditorServer(_content);
    }

    static void Copy(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.EnumerateFiles(from))
            File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
        foreach (var d in Directory.EnumerateDirectories(from))
            Copy(d, Path.Combine(to, Path.GetFileName(d)));
    }

    public void Dispose()
    {
        _editor.Dispose();
        Directory.Delete(Path.GetDirectoryName(_content)!, recursive: true);
    }

    (int Status, JsonElement Json) Call(string method, string path, object? body = null, NameValueCollection? query = null)
    {
        var (status, _, text) = _editor.Handle(method, path, query ?? new NameValueCollection(), body is null ? "" : JsonSerializer.Serialize(body, DataFile.Options));
        return (status, status == 200 && text.Length > 0 && text[0] is '{' or '[' ? JsonDocument.Parse(text).RootElement : default);
    }

    [Fact]
    public void ItListsTheTuningWithWhatEachFileIsFor()
    {
        var (status, files) = Call("GET", "/api/tuning");
        Assert.Equal(200, status);
        var train = files.EnumerateArray().First(f => f.GetProperty("file").GetString() == TrainTuning.File);
        Assert.Contains("systems-spec", train.GetProperty("header").GetString());
        Assert.Contains(files.EnumerateArray(), f => f.GetProperty("file").GetString() == "audio/sounds/hound-howl.json");
    }

    [Fact]
    public void AnEditChangesTheValueAndKeepsEveryComment()
    {
        string file = Path.Combine(_content, RouteTuning.File);
        string before = File.ReadAllText(file);
        var (status, _) = Call("POST", "/api/tuning/set", new { file = RouteTuning.File, path = "dawnSlack", value = 0.25 });
        Assert.Equal(200, status);
        string after = File.ReadAllText(file);
        Assert.Equal(before.Replace("\"dawnSlack\": 0.18", "\"dawnSlack\": 0.25"), after);
        Assert.Equal(0.25, DataFile.Load<RouteTuning>(file).DawnSlack);
    }

    [Fact]
    public void AnEditTheGameCouldNotLoadIsRefusedAndNothingIsWritten()
    {
        string file = Path.Combine(_content, "audio/sounds/wind.json");
        string before = File.ReadAllText(file);
        var (status, _) = Call("POST", "/api/tuning/set", new { file = "audio/sounds/wind.json", path = "layers[0].source", value = "banana" });
        Assert.Equal(400, status);
        Assert.Equal(before, File.ReadAllText(file));
    }

    [Fact]
    public void OnlyContentFilesCanBeEdited()
    {
        var (status, _) = Call("POST", "/api/tuning/set", new { file = "../Ballast.slnx", path = "x", value = 1 });
        Assert.Equal(404, status);
    }

    [Fact]
    public void ARouteEditedAndSavedIsPlayable()
    {
        var q = new NameValueCollection { ["spec"] = "local:3" };
        var (status, described) = Call("GET", "/api/route", query: q);
        Assert.Equal(200, status);
        Assert.True(described.GetProperty("plan").GetArrayLength() > 100);
        var route = JsonSerializer.Deserialize<Route>(described.GetProperty("route").GetRawText(), DataFile.Options)!;
        // The designer drops a Sleepers patch in at 6 km.
        route = route with { Features = [.. route.Features, new RouteFeature(FeatureKind.Sleepers, 6000, 6020)] };
        var (saved, _) = Call("POST", "/api/route/save", new { name = "my-night", route });
        Assert.Equal(200, saved);
        var back = DataFile.Load<Route>(Path.Combine(_content, "lines", "my-night.route.json"));
        Assert.Equal("my-night", back.Name);
        Assert.Contains(back.Features, f => f.Kind == FeatureKind.Sleepers && f.Start == 6000);
        Assert.True(back.Build().Length > 17_000);
        Assert.True(File.Exists(Path.Combine(_content, "lines", "my-night.json")));
    }

    [Fact]
    public void BadRouteNamesAreRefused()
    {
        var route = RouteGenerator.Generate(DataFile.Load<RouteTuning>(Path.Combine(_content, RouteTuning.File)), RouteTier.Local, 1);
        Assert.Equal(400, Call("POST", "/api/route/save", new { name = "../../escape", route }).Status);
    }

    [Fact]
    public async Task ItServesThePageOverHttp()
    {
        _editor.Start();
        using var http = new HttpClient();
        string page = await http.GetStringAsync(_editor.Url, TestContext.Current.CancellationToken);
        Assert.Contains("DARK TERRITORY", page);
        string files = await http.GetStringAsync(_editor.Url + "api/tuning", TestContext.Current.CancellationToken);
        Assert.Contains("tuning/train.json", files);
    }
}
