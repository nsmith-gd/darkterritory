using DarkTerritory.Game;

namespace DarkTerritory.Game.Tests;

/// <summary>Crash reports (T33): the exception and what the game said last, in a file a player can send.</summary>
public sealed class CrashReportsTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"dt-crash-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void AReportHasTheExceptionAndTheLastLines()
    {
        var reports = new CrashReports(_dir, keepLines: 3);
        foreach (var line in new[] { "one", "two", "GPU: llvmpipe", "hosting on UDP port 27015" })
            reports.Remember(line);
        Assert.Equal(["two", "GPU: llvmpipe", "hosting on UDP port 27015"], reports.Recent);

        Exception thrown;
        try
        {
            throw new InvalidOperationException("the boiler burst");
        }
        catch (InvalidOperationException e)
        {
            thrown = e;
        }
        var path = reports.Write(thrown, new DateTime(2026, 9, 29, 7, 30, 0));
        Assert.Equal(Path.Combine(_dir, "crash-20260929-073000.txt"), path);
        var text = File.ReadAllText(path!);
        Assert.Contains("the boiler burst", text);
        Assert.Contains(nameof(AReportHasTheExceptionAndTheLastLines), text); // the stack
        Assert.Contains("hosting on UDP port 27015", text);
        Assert.DoesNotContain("  one", text);
    }
}
