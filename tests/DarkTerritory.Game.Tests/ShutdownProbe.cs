// TEMPORARY PROBE for queue #591 (not to be committed): every managed thread's stack as the last test finishes.
using System.Diagnostics;
[assembly: Xunit.AssemblyFixture(typeof(DarkTerritory.Game.Tests.ShutdownProbe))]
namespace DarkTerritory.Game.Tests;
public sealed class ShutdownProbe : IDisposable
{
    public void Dispose()
    {
        var psi = new ProcessStartInfo(Environment.GetEnvironmentVariable("PROBE_STACK") ?? "dotnet-stack", $"report -p {Environment.ProcessId}")
        { RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = Process.Start(psi)!;
        string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        File.WriteAllText(Environment.GetEnvironmentVariable("PROBE_OUT") ?? "probe-stacks.txt", o);
    }
}
