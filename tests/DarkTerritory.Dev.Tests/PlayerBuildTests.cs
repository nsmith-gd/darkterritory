using System.Text.RegularExpressions;
using Ballast;
using Ballast.Dev;

namespace DarkTerritory.Dev.Tests;

/// <summary>
/// ARCHITECTURE §8 note 514: the developer tools never reach a player's build. <see cref="PlayerBuild"/> finds them in a
/// folder by name, by reference and by the mark they carry; and the source keeps them out of what ships: nothing a player's
/// build is made of references them, and the app reaches them only under <c>#if DEVTOOLS</c>.
/// </summary>
public class PlayerBuildTests
{
    static readonly string Repo = Path.GetDirectoryName(DataFile.FindContentRoot())!;
    static readonly string Here = AppContext.BaseDirectory;

    static string Scratch()
    {
        string dir = Path.Combine(Path.GetTempPath(), "dt-playerbuild-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void TheDevAssembliesAreFoundByNameAndByTheirMark()
    {
        var result = PlayerBuild.Check(Here);
        Assert.False(result.Clean);
        foreach (var name in PlayerBuild.DevAssemblies)
        {
            Assert.Contains(result.Findings, f => f.File == $"{name}.dll" && f.Why == "a developer assembly");
            Assert.Contains(result.Findings, f => f.File == $"{name}.dll" && f.Why.StartsWith("built with", StringComparison.Ordinal));
        }
        // This assembly leans on both, as anything using the dev tools would, and is found by its references.
        Assert.Contains(result.Findings, f => f.File == "DarkTerritory.Dev.Tests.dll" && f.Why == "references DarkTerritory.Dev");
        Assert.Contains(result.Findings, f => f.File == "DarkTerritory.Dev.Tests.dll" && f.Why == "references Ballast.Dev");
        Assert.True(PlayerBuild.IsMarked(typeof(DevTools).Assembly));
        Assert.False(PlayerBuild.IsMarked(typeof(Game.Report).Assembly));
    }

    [Fact]
    public void WhatAPlayersBuildIsMadeOfIsClean()
    {
        string dir = Scratch();
        try
        {
            foreach (var name in new[] { "Ballast.Core", "Ballast.Net", "Ballast.Render", "DarkTerritory.Sim", "DarkTerritory.Game" })
                File.Copy(Path.Combine(Here, name + ".dll"), Path.Combine(dir, name + ".dll"));
            var result = PlayerBuild.Check(dir);
            Assert.True(result.Clean, string.Join("; ", result.Findings));
            Assert.Equal(5, result.Assemblies);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ADevAssemblyUnderAnotherNameIsStillFoundByItsMark()
    {
        string dir = Scratch();
        try
        {
            File.Copy(Path.Combine(Here, "Ballast.Dev.dll"), Path.Combine(dir, "Innocent.dll"));
            File.WriteAllText(Path.Combine(dir, "Game.deps.json"), """{ "libraries": { "DarkTerritory.Dev/1.0.0": { "type": "project" } } }""");
            var result = PlayerBuild.Check(dir);
            Assert.Contains(result.Findings, f => f.File == "Innocent.dll" && f.Why.StartsWith("built with", StringComparison.Ordinal));
            Assert.Contains(result.Findings, f => f.File == "Game.deps.json" && f.Why == "lists DarkTerritory.Dev");
            Assert.DoesNotContain(result.Findings, f => f.Why == "a developer assembly");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void NothingThatShipsReferencesTheDevTools()
    {
        // Every project under src/ but the dev assemblies, dt (never shipped) and the app (below) is in a player's build.
        string[] neverShipped = ["Ballast.Dev", "DarkTerritory.Dev", "DarkTerritory.Cli"];
        foreach (var project in Directory.EnumerateFiles(Path.Combine(Repo, "src"), "*.csproj", SearchOption.AllDirectories))
        {
            string name = Path.GetFileNameWithoutExtension(project);
            if (neverShipped.Contains(name) || name == "DarkTerritory.App")
                continue;
            string text = File.ReadAllText(project);
            Assert.False(text.Contains("Dev.csproj", StringComparison.Ordinal), $"{name} references a developer assembly: it would ship");
        }
        // The app references DarkTerritory.Dev only in a DevTools build.
        string app = File.ReadAllText(Path.Combine(Repo, "src/DarkTerritory.App/DarkTerritory.App.csproj"));
        var group = Regex.Match(app, @"<ItemGroup Condition=""'\$\(DevTools\)' == 'true'"">(.*?)</ItemGroup>", RegexOptions.Singleline);
        Assert.True(group.Success, "the app's DevTools item group is gone");
        Assert.Contains("DarkTerritory.Dev.csproj", group.Groups[1].Value);
        Assert.Single(Regex.Matches(app, "Dev\\.csproj"));
    }

    [Fact]
    public void TheAppReachesTheDevToolsOnlyUnderDevTools()
    {
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Repo, "src/DarkTerritory.App"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                continue;
            // Each open #if, and whether it's DEVTOOLS's own; the dev tools may be named only inside one of those.
            var open = new Stack<bool>();
            int line = 0;
            foreach (var text in File.ReadLines(file))
            {
                line++;
                string t = text.Trim();
                if (t.StartsWith("#if", StringComparison.Ordinal))
                    open.Push(t == "#if DEVTOOLS");
                else if (t.StartsWith("#endif", StringComparison.Ordinal))
                    open.Pop();
                else if (t.StartsWith("#el", StringComparison.Ordinal) && open.Peek())
                    Assert.Fail($"{Path.GetFileName(file)}:{line}: an #else to DEVTOOLS is what a player's build runs instead; write it outside the #if");
                else if (!open.Contains(true) && t.Contains("DarkTerritory.Dev", StringComparison.Ordinal) && !t.StartsWith("//", StringComparison.Ordinal))
                    Assert.Fail($"{Path.GetFileName(file)}:{line}: the dev tools reached outside #if DEVTOOLS: {t}");
            }
            Assert.Empty(open);
        }
    }
}
