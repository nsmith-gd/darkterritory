using System.Text.Json;
using Ballast;
using Ballast.Dev;

/// <summary>
/// `dt build check &lt;folder&gt; [--dev]` (ARCHITECTURE §8 note 514): whether a built game is fit for players, that is none of the
/// developer tools are in it (<see cref="PlayerBuild"/>). Exit 1 if any are. With <c>--dev</c>, the other way round: the
/// director's test build, which must carry them (its app marked, DarkTerritory.Dev beside it). tools/package.sh runs it on
/// every package it makes, and tools/upload.sh again on what it's about to send.
/// </summary>
static class BuildCommands
{
    public static int Run(string[] args)
    {
        if (args is not ["build", "check", var folder, ..])
        {
            Console.Error.WriteLine("usage: dt build check <folder> [--dev]");
            return 2;
        }
        bool dev = args.Contains("--dev");
        var result = PlayerBuild.Check(folder);
        // A dev build is the app marked, with the game's dev assembly beside it; a player's is clean.
        bool ok = dev
            ? result.Findings.Any(f => f.File.EndsWith("DarkTerritory.Dev.dll", StringComparison.OrdinalIgnoreCase))
              && result.Findings.Any(f => f.File.EndsWith("DarkTerritory.dll", StringComparison.OrdinalIgnoreCase) && f.Why.StartsWith("built with", StringComparison.Ordinal))
            : result.Clean;
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            folder = result.Folder,
            expect = dev ? "dev" : "player",
            ok,
            assemblies = result.Assemblies,
            findings = result.Findings,
        }, DataFile.Options));
        if (!ok)
            Console.Error.WriteLine(dev ? $"{folder} isn't a developer build: its app isn't marked, or DarkTerritory.Dev isn't beside it"
                : $"{folder} carries the developer tools: {string.Join("; ", result.Findings.Select(f => $"{f.File} ({f.Why})"))}");
        return ok ? 0 : 1;
    }
}
