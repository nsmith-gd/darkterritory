using Ballast;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;

namespace DarkTerritory.Sim.Run;

/// <summary>Mirror of content/tuning/facilities.json. Field docs live in that file.</summary>
public sealed record FacilityTuning(CrateTuning Crates, WinchTuning Winch, Dictionary<string, string[]> Kinds)
{
    public const string File = "tuning/facilities.json";

    public IReadOnlyList<ModuleKind> ModulesOf(FacilityKind kind)
    {
        string key = char.ToLowerInvariant(kind.ToString()[0]) + kind.ToString()[1..];
        return Kinds.TryGetValue(key, out var names)
            ? [.. names.Select(n => Enum.Parse<ModuleKind>(n, ignoreCase: true))]
            : [];
    }
}

public sealed record CrateTuning(int[] Count, double LoadPerCrate, double SettleSeconds, double Lateral, double Along);
public sealed record WinchTuning(double HaulMetres, double Speed, double LoadPerSled, int Sleds, double HandleReach, double CarReach, double Along);

/// <summary>Spec D.2 loading modules built so far.</summary>
public enum ModuleKind : byte { Crates, Winch }

/// <summary>
/// One facility's loading modules and where they stand, laid out beside the line from the route (so every machine
/// agrees), and their state on the host. Clients mirror the state from the Run record.
/// </summary>
public sealed class Site
{
    public Site(int index, RouteFeature feature, IReadOnlyList<ModuleKind> modules, FacilityTuning t, RailLine line, int crates)
    {
        Index = index;
        Feature = feature;
        Modules = modules;
        double mid = (feature.Start + feature.End) / 2;
        int side = feature.Side == 0 ? 1 : feature.Side;
        Double3 At(double along, double lateral, double up = 0)
        {
            var sample = line.Sample(Math.Clamp(mid + along, 0, line.Length));
            var right = Double3.Cross(sample.Tangent, Double3.Up).Normalized;
            return sample.Position + right * (side * lateral) + Double3.Up * up;
        }
        CrateCount = Has(ModuleKind.Crates) ? crates : 0;
        CrateStack = [.. Enumerable.Range(0, CrateCount).Select(i => At(t.Crates.Along + i / 2 * 1.1, t.Crates.Lateral + i % 2 * 1.1, 0.6))];
        CrateLineHint = mid + t.Crates.Along;
        if (Has(ModuleKind.Winch))
        {
            var w = t.Winch;
            Capstan = At(w.Along, 5);
            Handles = [At(w.Along - 1.2, 5, 0.9), At(w.Along + 1.2, 5, 0.9)];
            SledFrom = At(w.Along, 5 + w.HaulMetres);
            SledTo = At(w.Along, 3.6);
            SledsLeft = w.Sleds;
        }
    }

    public int Index { get; }
    public RouteFeature Feature { get; }
    public IReadOnlyList<ModuleKind> Modules { get; }
    public bool Has(ModuleKind m) => Modules.Contains(m);

    public int CrateCount { get; }
    public Double3[] CrateStack { get; }
    public double CrateLineHint { get; }
    /// <summary>The crates are out on the platform (they appear when the train first stops here).</summary>
    public bool Stocked { get; internal set; }

    public Double3 Capstan { get; }
    public Double3[] Handles { get; } = [];
    public Double3 SledFrom { get; }
    public Double3 SledTo { get; }
    /// <summary>0 at the far end of the haul, 1 at the track.</summary>
    public double Progress { get; internal set; }
    public int SledsLeft { get; internal set; }
    public Double3 Sled => SledFrom + (SledTo - SledFrom) * Progress;
    /// <summary>Who is on each handle this tick (−1 for nobody). Host only.</summary>
    internal readonly int[] Cranking = [-1, -1];
    /// <summary>Both handles were turning last tick (for the HUD and the sound).</summary>
    public bool Turning { get; internal set; }

    /// <summary>Client side: adopts the host's state.</summary>
    public void Mirror(bool stocked, double progress, int sledsLeft, bool turning)
    {
        Stocked = stocked;
        Progress = progress;
        SledsLeft = sledsLeft;
        Turning = turning;
    }
}
