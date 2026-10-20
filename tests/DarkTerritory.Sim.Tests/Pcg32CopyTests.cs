using System.Reflection;
using System.Text.RegularExpressions;
using Ballast;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// <see cref="Pcg32"/> is a mutable struct: a <c>readonly</c> field of it, or a property handing it out, gives every draw a
/// defensive copy, so the generator returns its first number forever. Hot boxes, lamps, run-ins, bends and the wreck's
/// jostles all came at fixed spacing that way (note 872, Bram's find on #592). These hold that no generator is kept where
/// it can't advance.
/// </summary>
public class Pcg32CopyTests
{
    const BindingFlags Every = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    static bool IsRng(Type t) => (Nullable.GetUnderlyingType(t) ?? t) == typeof(Pcg32);

    [Fact]
    public void NoGeneratorIsAReadonlyFieldOrAProperty()
    {
        var bad = new List<string>();
        foreach (var asm in new[] { typeof(World).Assembly, typeof(Pcg32).Assembly })
            foreach (var type in asm.GetTypes())
            {
                bad.AddRange(type.GetFields(Every)
                    .Where(f => f.IsInitOnly && IsRng(f.FieldType))
                    .Select(f => $"{type.FullName}.{f.Name} (readonly field)"));
                bad.AddRange(type.GetProperties(Every)
                    .Where(p => IsRng(p.PropertyType))
                    .Select(p => $"{type.FullName}.{p.Name} (property: hands out a copy)"));
                bad.AddRange(type.GetMethods(Every).SelectMany(m => m.GetParameters()
                    .Where(p => p.IsIn && IsRng(p.ParameterType.GetElementType() ?? p.ParameterType))
                    .Select(p => $"{type.FullName}.{m.Name}({p.Name}) (in parameter: draws from a copy)")));
            }
        Assert.True(bad.Count == 0, "Pcg32 kept where it can't advance (note 872):\n" + string.Join("\n", bad));
    }

    // The reflection above sees the Sim and the engine core; this sees every project's source, the Game's and the App's too.
    static readonly Regex ReadonlyRng = new(@"\breadonly\s+(Ballast\.)?Pcg32\b");

    [Fact]
    public void NoSourceKeepsAReadonlyGenerator()
    {
        string src = Path.Combine(Path.GetDirectoryName(DataFile.FindContentRoot())!, "src");
        var bad = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (f, i, line)))
            .Where(x => ReadonlyRng.IsMatch(x.line))
            .Select(x => $"{Path.GetRelativePath(src, x.f)}:{x.i + 1}: {x.line.Trim()}")
            .ToList();
        Assert.True(bad.Count == 0, "readonly Pcg32 (note 872):\n" + string.Join("\n", bad));
    }
}
