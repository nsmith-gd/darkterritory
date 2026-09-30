using Ballast;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;

namespace DarkTerritory.Sim.Run;

/// <summary>Mirror of content/tuning/facilities.json. Field docs live in that file.</summary>
public sealed record FacilityTuning(CrateTuning Crates, WinchTuning Winch, Dictionary<string, string[]> Kinds)
{
    public const string File = "tuning/facilities.json";

    public CraneTuning Crane { get; init; } = new();

    /// <summary>On a spur, the modules are laid out from this far back from its buffer stop (beside the first cars).</summary>
    public double SpurLayout { get; init; } = 45;

    /// <summary>A facility's modules: its own, if the route gives it some (T44), else its kind's.</summary>
    public IReadOnlyList<ModuleKind> ModulesOf(RouteFeature facility) =>
        facility.Modules is { } own ? [.. own.Select(n => Enum.Parse<ModuleKind>(n, ignoreCase: true))]
        : facility.Facility is { } kind ? ModulesOf(kind) : [];

    public IReadOnlyList<ModuleKind> ModulesOf(FacilityKind kind)
    {
        string key = char.ToLowerInvariant(kind.ToString()[0]) + kind.ToString()[1..];
        return Kinds.TryGetValue(key, out var names)
            ? [.. names.Select(n => Enum.Parse<ModuleKind>(n, ignoreCase: true))]
            : [];
    }
}

public sealed record CrateTuning(int[] Count, double LoadPerCrate, double SettleSeconds, double Lateral, double Along)
{
    public HeavyCrateTuning Heavy { get; init; } = new();
}

/// <summary>D.2 "heavy items need two" (T43). Field docs in facilities.json.</summary>
public sealed record HeavyCrateTuning
{
    public int[] Count { get; init; } = [1, 2];
    public double LoadPerCrate { get; init; } = 0.5;
    public double Radius { get; init; } = 0.55;
    public double Span { get; init; } = 2.4;
    public double Along { get; init; } = -6;
}

public sealed record WinchTuning(double HaulMetres, double Speed, double LoadPerSled, int Sleds, double HandleReach, double CarReach, double Along)
{
    public CrankTuning Crank { get; init; } = new();
}

/// <summary>The winch's two cranks and D.2's desync stall (T43). Field docs in facilities.json.</summary>
public sealed record CrankTuning
{
    public double RevsPerSecond { get; init; } = 0.5;
    public double Radius { get; init; } = 0.3;
    public double InRhythm { get; init; } = 0.6;
    public double SmoothSeconds { get; init; } = 0.25;
}

/// <summary>The gantry crane (spec D.2, T48). Field docs in facilities.json.</summary>
public sealed record CraneTuning
{
    public double Along { get; init; } = -4;
    public double Length { get; init; } = 20;
    public double[] Span { get; init; } = [-3.5, 9];
    public double Height { get; init; } = 8;
    public double StackLateral { get; init; } = 7.5;
    public int Castings { get; init; } = 2;
    public double Spacing { get; init; } = 3;
    public double LoadPerCasting { get; init; } = 0.5;
    public double[] CastingSize { get; init; } = [1.4, 0.9, 1.0];
    public double BridgeSpeed { get; init; } = 1.2;
    public double TrolleySpeed { get; init; } = 1.0;
    public double HoistSpeed { get; init; } = 0.8;
    public double ControlsReach { get; init; } = 1.2;
    public double RigReach { get; init; } = 1.5;
    public double RigHeight { get; init; } = 2.2;
    public double RigSeconds { get; init; } = 2;
    public double DropAbove { get; init; } = 0.6;
    public double CrushRadius { get; init; } = 1.4;
}

/// <summary>Where a crane's casting is (T48): on the ground where it was stacked, on the hook, or lashed on a car.</summary>
public enum CastingState : byte { Stacked, Hooked, Loaded, Lost }

/// <summary>A site's replicated state (the Run record), for a client to adopt.</summary>
public readonly record struct SiteState(bool Stocked, double Progress, int SledsLeft, bool Turning, bool OutOfRhythm, double Crank);

/// <summary>Spec D.2 loading modules built so far.</summary>
public enum ModuleKind : byte { Crates, Winch, Crane }

/// <summary>
/// One facility's loading modules and where they stand, laid out beside its track from the route (so every machine
/// agrees), and their state on the host. Clients mirror the state from the Run record.
/// <para>
/// The track is the facility's spur (GDD §17), laid out from where the cars stand when the engine's at the buffer
/// stop, on the side away from the main line; or, for a facility without one, the main line at the zone's middle.
/// </para>
/// </summary>
public sealed class Site
{
    /// <param name="track">The line the modules stand beside: a spur's own line, or the main line.</param>
    /// <param name="mid">Distance along <paramref name="track"/> the modules are laid out from.</param>
    /// <param name="mainDistance">About where that is along the main line (to find the ground from).</param>
    public Site(int index, RouteFeature feature, IReadOnlyList<ModuleKind> modules, FacilityTuning t, RailLine track, double mid, int side,
        double mainDistance, int crates, int spur = RailLine.MainPath, int heavy = 0)
    {
        Index = index;
        Feature = feature;
        Modules = modules;
        Spur = spur;
        Track = track;
        Mid = mid;
        Side = side;
        Double3 At(double along, double lateral, double up = 0)
        {
            var sample = track.Sample(Math.Clamp(mid + along, 0, track.Length));
            var right = Double3.Cross(sample.Tangent, Double3.Up).Normalized;
            return sample.Position + right * (side * lateral) + Double3.Up * up;
        }
        CrateCount = Has(ModuleKind.Crates) ? crates : 0;
        LoadPerCrate = t.Crates.LoadPerCrate;
        CrateStack = [.. Enumerable.Range(0, CrateCount).Select(i => At(t.Crates.Along + i / 2 * 1.1, t.Crates.Lateral + i % 2 * 1.1, 0.6))];
        CrateLineHint = mainDistance + t.Crates.Along;
        var h = t.Crates.Heavy;
        HeavyStack = [.. Enumerable.Range(0, CrateCount > 0 ? heavy : 0).Select(i => At(t.Crates.Along + h.Along - i * (h.Radius * 2 + 0.6), t.Crates.Lateral, h.Radius))];
        HeavyRadius = h.Radius;
        LoadPerHeavy = h.LoadPerCrate;
        if (Has(ModuleKind.Winch))
        {
            var w = t.Winch;
            Capstan = At(w.Along, 5);
            Handles = [At(w.Along - 1.2, 5, 0.9), At(w.Along + 1.2, 5, 0.9)];
            SledFrom = At(w.Along, 5 + w.HaulMetres);
            SledTo = At(w.Along, 3.6);
            SledsLeft = w.Sleds;
            // The drum lies along the track between the two cranks; each turns in the plane across it, and "forward"
            // (hauling in) takes a crank over the top towards the track, winding the rope in off the top of the drum.
            Axis = (Handles[1] - Handles[0]) with { Y = 0 };
            Axis = Axis.Normalized;
            Outward = ((SledFrom - SledTo) with { Y = 0 }).Normalized;
            CrankRadius = w.Crank.Radius;
        }
        if (Has(ModuleKind.Crane))
            Crane = new Crane(t.Crane, (along, lateral, up) => At(along, lateral, up));
    }

    /// <summary>The gantry crane here, if the facility has one (T48).</summary>
    public Crane? Crane { get; }

    /// <summary>
    /// The yard's gantries from its stop layout (level-design P5, P18): one over each craned loading face but the
    /// facility's own, each with a casting in every bay its runway reaches.
    /// </summary>
    public IReadOnlyList<Crane> YardCranes { get; internal set; } = [];

    /// <summary>Every crane here: the facility's own first.</summary>
    public IReadOnlyList<Crane> Cranes => Crane is null ? YardCranes : [Crane, .. YardCranes];

    /// <summary>The crane someone at <paramref name="at"/> is working: the one whose controls or hook are nearest (for the HUD and the cab's view).</summary>
    public Crane? CraneNear(Double3 at) => Cranes.Count == 0 ? null
        : Cranes.MinBy(c => Math.Min(((c.Controls - at) with { Y = 0 }).Length, ((c.HookAt - at) with { Y = 0 }).Length));

    public int Index { get; }
    public RouteFeature Feature { get; }
    /// <summary>The branch the facility's track is (a spur), or <see cref="RailLine.MainPath"/>.</summary>
    public int Spur { get; }
    /// <summary>The line the modules stand beside, where along it they're laid out from, and on which side.</summary>
    public RailLine Track { get; }
    public double Mid { get; }
    public int Side { get; }
    public IReadOnlyList<ModuleKind> Modules { get; }
    public bool Has(ModuleKind m) => Modules.Contains(m);

    public int CrateCount { get; }
    /// <summary>How much of a car's load a stowed crate is (facilities.json).</summary>
    public double LoadPerCrate { get; }
    public Double3[] CrateStack { get; }
    public double CrateLineHint { get; }
    /// <summary>The crates are out on the platform (they appear when the train first stops here).</summary>
    public bool Stocked { get; internal set; }

    /// <summary>D.2's heavy crates (T43): where they stand, how big, and how much of a car's load each is.</summary>
    public Double3[] HeavyStack { get; }
    public double HeavyRadius { get; }
    public double LoadPerHeavy { get; }

    public Double3 Capstan { get; }
    /// <summary>The hub of each crank, one at either end of the drum.</summary>
    public Double3[] Handles { get; } = [];
    /// <summary>Along the drum (handle 0 to handle 1), and out from the track: the cranks turn in the plane of this and up.</summary>
    public Double3 Axis { get; }
    public Double3 Outward { get; }
    public double CrankRadius { get; }
    /// <summary>The drum's turn, radians (T43): crank 0's grip is out from the track at 0, over the top at π/2.</summary>
    public double Crank { get; internal set; }
    /// <summary>Both cranks are manned but out of rhythm, so the drum's stalled (D.2's desync).</summary>
    public bool OutOfRhythm { get; internal set; }

    /// <summary>Where a crank's grip is now: the two are half a turn apart.</summary>
    public Double3 Grip(int handle)
    {
        double a = Crank + handle * Math.PI;
        return Handles[handle] + (Outward * Math.Cos(a) + Double3.Up * Math.Sin(a)) * CrankRadius;
    }

    /// <summary>
    /// A reaching hand on a crank (T43): within <paramref name="grab"/> of the circle its grip goes round, and out from
    /// the hub (a hand on the axle isn't going round). Returns the hand's angle round the hub, or null.
    /// </summary>
    public double? OnCrank(int handle, Double3 hand, double grab)
    {
        var d = hand - Handles[handle];
        double a = Double3.Dot(d, Axis), u = Double3.Dot(d, Outward), v = d.Y;
        double rho = Math.Sqrt(u * u + v * v);
        if (rho < CrankRadius * 0.5 || Math.Sqrt((rho - CrankRadius) * (rho - CrankRadius) + a * a) > grab)
            return null;
        return Math.Atan2(v, u) - handle * Math.PI;
    }
    public Double3 SledFrom { get; }
    public Double3 SledTo { get; }
    /// <summary>0 at the far end of the haul, 1 at the track.</summary>
    public double Progress { get; internal set; }
    public int SledsLeft { get; internal set; }
    public Double3 Sled => SledFrom + (SledTo - SledFrom) * Progress;
    /// <summary>Who is on each handle this tick (−1 for nobody). Host only.</summary>
    internal readonly int[] Cranking = [-1, -1];
    /// <summary>
    /// Host only (T43): a reaching hand's angle on each crank this tick (null for a keyboard's hold), the last one it
    /// had, and each crank's pace in turns a second, a hand's smoothed.
    /// </summary>
    internal readonly double?[] HandAngle = [null, null], LastAngle = [null, null];
    internal readonly double[] Pace = [0, 0];
    /// <summary>Both handles were turning last tick (for the HUD and the sound).</summary>
    public bool Turning { get; internal set; }

    public SiteState State => new(Stocked, Progress, SledsLeft, Turning, OutOfRhythm, Crank);

    /// <summary>Client side: adopts the host's state.</summary>
    public void Mirror(in SiteState s)
    {
        Stocked = s.Stocked;
        Progress = s.Progress;
        SledsLeft = s.SledsLeft;
        Turning = s.Turning;
        OutOfRhythm = s.OutOfRhythm;
        Crank = s.Crank;
    }
}
