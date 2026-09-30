using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ballast;
using Ballast.Audio;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Editor;

/// <summary>
/// The designer's editor (T18, GDD brief: "a game designer first and a level designer second"). A local web
/// page, served by <c>dt edit</c>, over the same text files everything else reads:
/// <list type="bullet">
/// <item>Tuning: every value in content/tuning and content/audio, with its comment as the help text. Edits change
/// only the value's characters (comments and spec citations survive), are validated against the game's own
/// record types before they're written, and the running game hot-reloads them.</item>
/// <item>Routes: generate any tier and seed, see the plan and the profile, edit the features and the line, and
/// save it as a named route the game can play (<c>--route-file name</c>).</item>
/// </list>
/// </summary>
public sealed class EditorServer : IDisposable
{
    readonly HttpListener _listener = new();
    readonly string _content;
    readonly RouteTuning _routeTuning;
    CancellationTokenSource? _stop;
    Task? _loop;

    /// <summary>Tuning files the editor shows, and the record each must still deserialise into after an edit.</summary>
    static readonly (string File, Type Type)[] Known =
    [
        (TrainTuning.File, typeof(TrainTuning)), (PlayerTuning.File, typeof(PlayerTuning)), (BoilerTuning.File, typeof(BoilerTuning)),
        (CombatTuning.File, typeof(CombatTuning)), (EnemyTuning.File, typeof(EnemyTuning)), (RouteTuning.File, typeof(RouteTuning)),
        (RunTuning.File, typeof(RunTuning)), (MixDef.File, typeof(MixDef)),
        (FacilityTuning.File, typeof(FacilityTuning)), (VigilTuning.File, typeof(VigilTuning)), (HoldoutTuning.File, typeof(HoldoutTuning)),
        (DarkTerritory.Sim.Campaign.CampaignTuning.File, typeof(DarkTerritory.Sim.Campaign.CampaignTuning)),
    ];

    public EditorServer(string content, int port = 0)
    {
        _content = content;
        _routeTuning = DataFile.Load<RouteTuning>(Path.Combine(content, RouteTuning.File));
        Port = port > 0 ? port : FreePort();
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
    }

    public int Port { get; }
    public string Url => $"http://127.0.0.1:{Port}/";

    static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    public void Start()
    {
        _listener.Start();
        _stop = new CancellationTokenSource();
        _loop = Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); }
                catch (Exception) when (_stop.IsCancellationRequested) { return; }
                catch (HttpListenerException) { return; }
                _ = Task.Run(() => Serve(ctx));
            }
        });
    }

    void Serve(HttpListenerContext ctx)
    {
        try
        {
            var (status, type, body) = Handle(ctx.Request.HttpMethod, ctx.Request.Url!.AbsolutePath, ctx.Request.QueryString, ReadBody(ctx.Request));
            var bytes = Encoding.UTF8.GetBytes(body);
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = type;
            ctx.Response.OutputStream.Write(bytes);
        }
        catch (Exception e)
        {
            ctx.Response.StatusCode = 500;
            ctx.Response.OutputStream.Write(Encoding.UTF8.GetBytes(e.Message));
        }
        finally
        {
            ctx.Response.Close();
        }
    }

    static string ReadBody(HttpListenerRequest r)
    {
        if (!r.HasEntityBody)
            return "";
        using var reader = new StreamReader(r.InputStream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    const string Json = "application/json; charset=utf-8";

    /// <summary>The whole API, callable without a socket (the tests use this directly too).</summary>
    public (int Status, string Type, string Body) Handle(string method, string path, System.Collections.Specialized.NameValueCollection query, string body)
    {
        switch (method, path)
        {
            case ("GET", "/"):
                return (200, "text/html; charset=utf-8", Page("index.html"));
            case ("GET", "/api/tuning"):
                return (200, Json, Serialize(TuningFiles()));
            case ("GET", "/api/tuning/file"):
                return (200, Json, Serialize(TuningFile(query["name"] ?? "")));
            case ("POST", "/api/tuning/set"):
                return SetTuning(body);
            case ("GET", "/api/route"):
                return (200, Json, Serialize(DescribeRoute(LoadRoute(query["spec"], query["file"]))));
            case ("POST", "/api/route/preview"):
                var previewed = ParseRoute(body);
                return Invalid(previewed) is { } why ? (400, "text/plain", why) : (200, Json, Serialize(DescribeRoute(previewed)));
            case ("POST", "/api/route/save"):
                return SaveRoute(body);
            case ("GET", "/api/routes"):
                return (200, Json, Serialize(Directory.EnumerateFiles(Path.Combine(_content, "lines"), "*.route.json")
                    .Select(f => Path.GetFileName(f)[..^".route.json".Length]).Order()));
            default:
                return (404, "text/plain", $"no {method} {path}");
        }
    }

    static string Serialize(object value) => JsonSerializer.Serialize(value, DataFile.Options);

    static string Page(string name)
    {
        var asm = typeof(EditorServer).Assembly;
        var resource = asm.GetManifestResourceNames().First(n => n.EndsWith("www." + name, StringComparison.Ordinal));
        using var s = asm.GetManifestResourceStream(resource)!;
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    IEnumerable<string> EditableFiles() =>
        Known.Select(k => k.File).Concat(Directory.EnumerateFiles(Path.Combine(_content, "audio", "sounds"), "*.json")
            .Select(f => Path.GetRelativePath(_content, f).Replace('\\', '/')).Order());

    object TuningFiles() => EditableFiles().Select(f =>
    {
        var text = File.ReadAllText(Path.Combine(_content, f));
        // The file's opening comment: what it is and which spec section it answers to.
        string header = string.Join(" ", text.Split('\n').TakeWhile(l => l.TrimStart().StartsWith("//", StringComparison.Ordinal)).Select(l => l.Trim().TrimStart('/').Trim()));
        return new { file = f, header };
    });

    object TuningFile(string file)
    {
        if (!EditableFiles().Contains(file))
            throw new FileNotFoundException(file);
        var text = File.ReadAllText(Path.Combine(_content, file));
        return new
        {
            file,
            values = Jsonc.Scalars(text).Select(s => new { path = s.Path, kind = s.Kind.ToString().ToLowerInvariant(), text = s.Text, comment = s.Comment }),
        };
    }

    Type? TypeFor(string file) => file.StartsWith("audio/sounds/", StringComparison.Ordinal) ? typeof(SoundDef) : Known.FirstOrDefault(k => k.File == file).Type;

    (int, string, string) SetTuning(string body)
    {
        var req = JsonNode.Parse(body)!;
        string file = (string)req["file"]!, path = (string)req["path"]!;
        if (!EditableFiles().Contains(file))
            return (404, "text/plain", $"not an editable file: {file}");
        string full = Path.Combine(_content, file);
        string before = File.ReadAllText(full);
        var kind = Jsonc.Scalars(before).FirstOrDefault(s => s.Path == path)?.Kind ?? throw new KeyNotFoundException(path);
        var raw = req["value"]!;
        object value = kind switch
        {
            JsoncKind.Number => raw.GetValue<double>(),
            JsoncKind.Bool => raw.GetValue<bool>(),
            _ => raw.GetValue<string>(),
        };
        string after = Jsonc.Set(before, path, value);
        // Refuse anything the game wouldn't load: the running game never sees a broken file from here.
        if (TypeFor(file) is { } type)
        {
            try
            {
                JsonSerializer.Deserialize(after, type, DataFile.Options);
            }
            catch (JsonException e)
            {
                return (400, "text/plain", $"the game wouldn't load that: {e.Message}");
            }
        }
        string temp = full + ".editing";
        File.WriteAllText(temp, after);
        File.Move(temp, full, overwrite: true);
        return (200, Json, Serialize(new { file, path, text = Jsonc.Scalars(after).First(s => s.Path == path).Text }));
    }

    Route LoadRoute(string? spec, string? file)
    {
        if (!string.IsNullOrEmpty(file))
            return DataFile.Load<Route>(Path.Combine(_content, "lines", Path.GetFileName(file) + ".route.json"));
        // The night the game would play for this spec, from the line generator (content/linegen), with the usual six cars.
        return Sim.LineGen.Routes.Generate(_content, string.IsNullOrEmpty(spec) ? "frontier:7" : spec, 6);
    }

    static Route ParseRoute(string body) => JsonSerializer.Deserialize<Route>(body, DataFile.Options) ?? throw new InvalidDataException("no route");

    /// <summary>
    /// The route plus what the page needs to draw it: a plan view and an elevation profile, every 25 m. And the loading
    /// modules there are, with what each kind of facility has unless the route gives it its own (T44).
    /// </summary>
    object DescribeRoute(Route route)
    {
        var facilities = DataFile.Load<FacilityTuning>(Path.Combine(_content, FacilityTuning.File));
        var line = route.Build();
        var plan = new List<double[]>();
        var profile = new List<double[]>();
        for (double s = 0; s <= line.Length; s += 25)
        {
            var t = line.Sample(s);
            plan.Add([Math.Round(t.Position.X, 1), Math.Round(t.Position.Z, 1)]);
            profile.Add([Math.Round(s, 0), Math.Round(t.Position.Y, 2), Math.Round(t.GradePercent, 2)]);
        }
        return new
        {
            route,
            length = Math.Round(line.Length, 1),
            plan,
            profile,
            tightestRadius = TightestRadius(),
            modules = new
            {
                names = Enum.GetNames<ModuleKind>().Select(n => char.ToLowerInvariant(n[0]) + n[1..]),
                byKind = facilities.Kinds,
            },
        };
    }

    /// <summary>
    /// The tightest curve any tier lays: the prototype generator's (route.json) or the line generator's deepest column
    /// (linegen/tiers.json), whichever is tighter, since the editor opens nights from either.
    /// </summary>
    double TightestRadius() => Math.Min(_routeTuning.Tiers.TightestRadius(), Sim.LineGen.LineGenContent.Cached(_content).Config.Tiers.Columns.DeepMax.MinRadius);

    /// <summary>A route the game would refuse: modules that don't exist, on a facility.</summary>
    static string? Invalid(Route route)
    {
        foreach (var f in route.Features.Where(f => f.Modules is not null))
        {
            if (f.Kind != FeatureKind.Facility)
                return $"only a facility has loading modules (the {f.Kind} at {f.Start:0} m has some)";
            if (f.Modules!.FirstOrDefault(m => !Enum.TryParse<ModuleKind>(m, ignoreCase: true, out _)) is { } unknown)
                return $"no loading module called '{unknown}' (there are {string.Join(", ", Enum.GetNames<ModuleKind>())})";
        }
        return null;
    }

    (int, string, string) SaveRoute(string body)
    {
        var req = JsonNode.Parse(body)!;
        string name = (string)req["name"]!;
        if (name.Length == 0 || name.Any(c => !(char.IsLetterOrDigit(c) || c is '-' or '_')))
            return (400, "text/plain", "a route name is letters, digits, - and _");
        var route = JsonSerializer.Deserialize<Route>(req["route"]!.ToJsonString(), DataFile.Options)!;
        route = route with { Name = name, Line = route.Line with { Name = name } };
        if (Invalid(route) is { } why)
            return (400, "text/plain", why);
        double tightest = TightestRadius();
        static bool Tight(double r, double min) => r != 0 && Math.Abs(r) < min - 0.5;
        if (route.Line.Segments.Count == 0 || route.Line.Segments.Any(s => s.Length <= 0 || Tight(s.Radius, tightest) || Tight(s.EndRadius ?? 0, tightest)))
            return (400, "text/plain", $"every piece of track needs a length, and a curve no tighter than {tightest:0} m (the tightest tier's)");
        route.Build(); // refuse a line that can't be built
        DataFile.Save(Path.Combine(_content, "lines", name + ".json"), route.Line);
        DataFile.Save(Path.Combine(_content, "lines", name + ".route.json"), route);
        return (200, Json, Serialize(new { name, play = $"dotnet run --project src/DarkTerritory.App -- --route-file {name}" }));
    }

    public void Dispose()
    {
        _stop?.Cancel();
        if (_listener.IsListening)
            _listener.Stop();
        _listener.Close();
    }
}
