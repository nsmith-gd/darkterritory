using System.Text.RegularExpressions;
using Ballast;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The sim's trigonometry goes through <see cref="DMath"/>, never the C library's (ARCHITECTURE §8 note 163): Windows
/// and Linux differ in the last bit of <c>Math.Sin</c> and friends for about one argument in a hundred, which split
/// the crossplay job's line plans (T116), and would drift a predicted train between a Windows client and a Linux host.
/// </summary>
public class DeterministicMathTests
{
    // Math's functions that call into the platform's C library. Sqrt, Abs, Floor, Round, Min, Max and the like are
    // exact by IEEE-754, so they're the same everywhere and stay allowed.
    static readonly Regex Platform = new(@"(?<![\w.])(System\.)?MathF?\.(Sin|Cos|Tan|Atan2?|Asin|Acos|Sinh|Cosh|Tanh|Asinh|Acosh|Atanh|Exp|Log|Log2|Log10|Pow|Cbrt|SinCos|ScaleB|ILogB)\(");

    [Fact]
    public void TheSimCallsNoPlatformMath()
    {
        string sim = Path.Combine(Path.GetDirectoryName(DataFile.FindContentRoot())!, "src", "DarkTerritory.Sim");
        var found = Directory.EnumerateFiles(sim, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (f, i, line)))
            .Where(x => !x.line.TrimStart().StartsWith("//") && Platform.IsMatch(x.line))
            .Select(x => $"{Path.GetRelativePath(sim, x.f)}:{x.i + 1}: {x.line.Trim()}")
            .ToList();
        Assert.True(found.Count == 0, "use DMath, not the C library's math:\n" + string.Join("\n", found));
    }
}
