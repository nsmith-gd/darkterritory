using System.Text;
using DarkTerritory.Dev.Live;
using DarkTerritory.Game;

/// <summary>
/// `dt mcp` (ARCHITECTURE §8 note 524): live control, for agents. A Model Context Protocol server on stdin and stdout
/// (JSON-RPC, a message a line) whose tools host a night headless with a bot crew, step it, read it, say what comes, open a
/// recording to any moment, draw any of it framed on a subject (<c>subject:gaunt</c>), and run the rest of dt. An MCP
/// client starts it as a stdio server: <c>dotnet run --project src/DarkTerritory.Cli -- mcp</c> from the repo.
/// <list type="bullet">
/// <item><c>dt mcp [--shots dir] [--recordings dir]</c>: shots to out/live, recorded nights to out/recordings, by default.</item>
/// </list>
/// stdout is the protocol's alone: anything else written to it in this process (a warning, a stray line) goes to stderr.
/// </summary>
static class McpCommands
{
    public static int Run(string content, string[] args, bool noMods)
    {
        var protocol = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { NewLine = "\n" };
        Console.SetOut(Console.Error);
        using var live = new LiveSession(content, Str(args, "--shots", "out/live"), Str(args, "--recordings", "out/recordings"));
        var (version, commit) = Report.Build();
        var server = new McpServer("dt", $"{version} ({(commit.Length > 7 ? commit[..7] : commit)})", LiveTools.For(live, Launcher(noMods)), LiveTools.Instructions)
        {
            Log = Console.Error,
        };
        Console.Error.WriteLine($"dt mcp: {server.Tools.Count} tools ({string.Join(", ", server.Tools.Select(t => t.Name))}), on stdin/stdout");
        using var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        server.RunAsync(input, protocol).GetAwaiter().GetResult();
        return 0;
    }

    /// <summary>
    /// This dt again: its own executable, or the dotnet host and dt.dll when it was started that way; with the edition and
    /// the mods it was given, so the child reads the same content.
    /// </summary>
    static DtLauncher Launcher(bool noMods)
    {
        string program = Environment.ProcessPath ?? "dotnet";
        var prefix = new List<string>();
        if (Path.GetFileNameWithoutExtension(program).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            prefix.Add(typeof(McpCommands).Assembly.Location);
        if (Mods.Edition is { } edition)
            prefix.AddRange(["--edition", edition]);
        if (Mods.ManagerFolder is { } mods)
            prefix.AddRange(["--mods-dir", mods]);
        if (noMods)
            prefix.Add("--no-mods");
        return new DtLauncher(program, prefix, Environment.CurrentDirectory);
    }

    static string Str(string[] args, string name, string fallback)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
    }
}
