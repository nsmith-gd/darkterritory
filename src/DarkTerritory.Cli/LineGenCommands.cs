using System.Diagnostics;
using System.Globalization;
using DarkTerritory.Game.LineGen;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Route;

/// <summary>`dt linegen`: the line generator's tools (plan §20.2).</summary>
static class LineGenCommands
{
    public static object Run(string content, string verb, string[] args) => verb switch
    {
        "generate" => Generate(content, args),
        "sweep" => Sweep(content, args),
        "debug" => LineGenerator.Debug(LineGenContent.Load(content), Parameters(args), (int)Opt(args, "--attempt", 0)).ToList(),
        _ => throw new ArgumentException($"linegen {verb}? (generate, sweep)"),
    };

    static string Str(string[] args, string name, string fallback)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
    }

    static double Opt(string[] args, string name, double fallback)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? double.Parse(args[i + 1], CultureInfo.InvariantCulture) : fallback;
    }

    static RunParameters Parameters(string[] args)
    {
        var tier = Enum.Parse<RouteTier>(Str(args, "--tier", "frontier"), ignoreCase: true);
        ulong seed = ulong.Parse(Str(args, "--seed", "1"));
        double severity = args.Contains("--severity") ? Opt(args, "--severity", 0.5) : RunParameters.SeverityOf(tier, seed);
        return new RunParameters(tier, seed, severity, (int)Opt(args, "--cars", 8), []);
    }

    static object Generate(string content, string[] args)
    {
        var c = LineGenContent.Load(content);
        var p = Parameters(args);
        var clock = Stopwatch.StartNew();
        var plan = LineGenerator.Generate(c, p);
        long ms = clock.ElapsedMilliseconds;
        var route = plan.ToRoute(c.Route);
        var line = route.Build();
        string output = Str(args, "--out", $"out/linegen/{p.RouteId}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, plan.ToJson(indented: true));
        string map = Str(args, "--map", Path.ChangeExtension(output, ".map.png"));
        string profile = Str(args, "--profile", Path.ChangeExtension(output, ".profile.png"));
        PlanImages.Map(plan, line, map);
        PlanImages.Profile(plan, line, profile);
        return new
        {
            plan = Path.GetFullPath(output),
            map = Path.GetFullPath(map),
            profile = Path.GetFullPath(profile),
            ms,
            route = plan.Route,
            lengthKm = Math.Round(plan.Km(plan.TerminusM), 2),
            attempts = plan.Validation.Attempts,
            fallback = plan.Validation.Fallback,
            passed = plan.Validation.Passed,
            failed = plan.Validation.Checks.Where(k => !k.Pass).Select(k => $"{k.Name}: {k.Detail}"),
            warnings = plan.Validation.Warnings,
            compressedKb = Math.Round(plan.CompressedBytes() / 1024.0, 1),
            edges = plan.Alignment.Select(a => new { a.Edge, a.Role, lengthKm = Math.Round(a.Length / 1000, 2), a.Toe, a.Rejoin }),
            pieces = plan.Pieces.GroupBy(x => x.Type).ToDictionary(g => g.Key, g => g.Count()),
            metrics = plan.Validation.Metrics,
        };
    }

    static object Sweep(string content, string[] args)
    {
        return new { todo = true };
    }
}
