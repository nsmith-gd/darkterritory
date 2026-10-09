using Ballast;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;

namespace DarkTerritory.Sim.Run;

/// <summary>Mirror of content/tuning/facilities.json. Field docs live in that file.</summary>
public sealed record FacilityTuning(CrateTuning Crates, WinchTuning Winch, Dictionary<string, string[]> Kinds)
{
    public const string File = "tuning/facilities.json";

    public CraneTuning Crane { get; init; } = new();
    public PowerTuning Power { get; init; } = new();
    /// <summary>GDD §18's set pieces (WP15, note 185): the grain elevator's spout, the slaughterhouse's ramp, the chemical works' hose, the depot's powder.</summary>
    public SpoutTuning Spout { get; init; } = new();
    public RampTuning Ramp { get; init; } = new();
    public HoseTuning Hose { get; init; } = new();
    public KegTuning Kegs { get; init; } = new();
    /// <summary>The mine head's steam lift (spec D.2; queue #105, note 368).</summary>
    public LiftTuning Lift { get; init; } = new();
    /// <summary>The grain elevator's conveyor line (spec D.2; queue #136, note 400).</summary>
    public ConveyorTuning Conveyor { get; init; } = new();
    /// <summary>The mine head's tipple (spec D.2; queue #159, note 423).</summary>
    public TippleTuning Tipple { get; init; } = new();
    /// <summary>GDD §18's switchyard and wreck yard (WP15b, note 187): the yard's standing cars, and the wreck to salvage.</summary>
    public RakesTuning Rakes { get; init; } = new();
    public DerelictsTuning Derelicts { get; init; } = new();
    public WreckYardTuning Wreck { get; init; } = new();
    /// <summary>Who works a stop (note 261, spec D.2's "Crew" column): the driver getting down, people playing as hands.</summary>
    public StopCrewTuning Crew { get; init; } = new();

    /// <summary>On a spur, the modules are laid out from this far back from its buffer stop (beside the first cars).</summary>
    public double SpurLayout { get; init; } = 45;

    /// <summary>
    /// Each stop's own modules (spec D.1: "POIs are procedurally assembled from a module grammar, so no two facilities operate
    /// identically"; queue #185, note 449): drawn per stop from its kind's (<see cref="ModulesFor"/>). Off, every facility has
    /// all its kind's, as before.
    /// </summary>
    public ModuleDrawTuning Draw { get; init; } = new();

    /// <summary>The modules a kind can draw beyond its own list (note 449): only drawn, never part of the full set.</summary>
    public Dictionary<string, string[]> Extras { get; init; } = new();

    /// <summary>
    /// This stop's modules (note 449): a facility the route gives its own (T44) has those; otherwise, with the draw on, its
    /// kind's first module (its signature: the spout, the crane, the lift) and the rest of its kind's list and its extras each
    /// with <see cref="ModuleDrawTuning.Chance"/>, from the night's seed and the facility's place in the night, filled up to
    /// the fewest and cut to the most in that order (the kind's own first); with it off, all its kind's.
    /// </summary>
    public IReadOnlyList<ModuleKind> ModulesFor(RouteFeature facility, ulong seed, int index)
    {
        var all = ModulesOf(facility);
        if (!Draw.Enabled || facility.Modules is not null || facility.Facility is not { } kind || all.Count == 0)
            return all;
        var offered = all.Skip(1).Concat(Names(Extras, kind).Where(m => !all.Contains(m))).ToList();
        var rng = new Pcg32(seed ^ 0x4D4F44554C4553UL, (ulong)index * 2 + 1);
        var drawn = new bool[offered.Count];
        for (int i = 0; i < offered.Count; i++)
            drawn[i] = rng.NextDouble() < Draw.Chance;
        for (int i = 0; i < offered.Count && 1 + drawn.Count(d => d) < Draw.Count[0]; i++)
            drawn[i] = true;
        var modules = new List<ModuleKind> { all[0] };
        for (int i = 0; i < offered.Count && modules.Count < Draw.Count[^1]; i++)
            if (drawn[i])
                modules.Add(offered[i]);
        return modules;
    }

    static IEnumerable<ModuleKind> Names(Dictionary<string, string[]> byKind, FacilityKind kind) =>
        byKind.TryGetValue(char.ToLowerInvariant(kind.ToString()[0]) + kind.ToString()[1..], out var names)
            ? names.Select(n => Enum.Parse<ModuleKind>(n, ignoreCase: true)) : [];

    /// <summary>A facility's modules: its own, if the route gives it some (T44), else its kind's.</summary>
    public IReadOnlyList<ModuleKind> ModulesOf(RouteFeature facility) =>
        facility.Modules is { } own ? [.. own.Select(n => Enum.Parse<ModuleKind>(n, ignoreCase: true))]
        : facility.Facility is { } kind ? ModulesOf(kind) : [];

    /// <summary>What a facility of this kind loads: its <c>cargo</c> entry, or goods.</summary>
    public Dictionary<string, string> Cargo { get; init; } = new();

    public Train.CargoKind CargoOf(FacilityKind kind) =>
        Cargo.TryGetValue(char.ToLowerInvariant(kind.ToString()[0]) + kind.ToString()[1..], out var name)
            && Enum.TryParse<Train.CargoKind>(name, ignoreCase: true, out var cargo) ? cargo : Train.CargoKind.Goods;

    public IReadOnlyList<ModuleKind> ModulesOf(FacilityKind kind)
    {
        string key = char.ToLowerInvariant(kind.ToString()[0]) + kind.ToString()[1..];
        return Kinds.TryGetValue(key, out var names)
            ? [.. names.Select(n => Enum.Parse<ModuleKind>(n, ignoreCase: true))]
            : [];
    }
}

/// <summary>Each stop's own modules (spec D.1; queue #185, note 449). Field docs in facilities.json.</summary>
public sealed record ModuleDrawTuning
{
    public bool Enabled { get; init; }
    public double Chance { get; init; } = 0.6;
    public int[] Count { get; init; } = [2, 4];
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

/// <summary>A yard's power (level-design D.2, spec D.1 restart). Field docs in facilities.json.</summary>
public sealed record PowerTuning
{
    public double LowSpeed { get; init; } = 0.5;
    public double RestartSeconds { get; init; } = 8;
    public double RestartRounds { get; init; } = 0.25;
    public double Reach { get; init; } = 2;
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

/// <summary>Who works a facility stop (note 261; spec D.2's "Crew" column). Field docs in facilities.json.</summary>
public sealed record StopCrewTuning
{
    public bool DriverWorks { get; init; } = true;
    public bool PeopleAreHands { get; init; } = true;
    public double PeopleWait { get; init; } = 45;
    /// <summary>Note 326: the share of the crate hands that go to search the village's houses once the crates are in (0: none).</summary>
    public double VillageShare { get; init; }
    /// <summary>How far from the train a hiding spot may be for a hand to go to it (m).</summary>
    public double VillageReach { get; init; } = 150;
    /// <summary>Nobody goes to the village with less of the night than this left (s).</summary>
    public double VillageDawnSpare { get; init; } = 900;
    /// <summary>Note 403: how far from where it stands a crate lying loose elsewhere in the yard may be for a hand to fetch it (m; 0: none).</summary>
    public double YardReach { get; init; }
    /// <summary>Note 413: how far the Choir's gathered (0 to 1) when a hand on foot at a stop gets behind a door (0: never).</summary>
    public double ShelterAt { get; init; }
    /// <summary>Gathered less than this, and not here, it comes out again.</summary>
    public double ShelterOutAt { get; init; } = 0.25;
    /// <summary>How far an open house's door may be for a hand to shelter in it rather than make for the train (m).</summary>
    public double ShelterReach { get; init; } = 60;
    /// <summary>A house is taken over the train while its door's no further than this many times the train's nearest car.</summary>
    public double ShelterOverTrain { get; init; } = 2;
}

/// <summary>The grain elevator's spout (GDD §18 "one spout, one car at a time"; spec D.2 gravity chute). Field docs in facilities.json.</summary>
public sealed record SpoutTuning
{
    public double Back { get; init; } = 80;
    public double Height { get; init; } = 5.2;
    public double LeverAlong { get; init; } = 2;
    public double LeverLateral { get; init; } = 3.4;
    public double LeverReach { get; init; } = 1.2;
    public double Tolerance { get; init; } = 1.5;
    public double PourPerSecond { get; init; } = 0.05;
    public double Bin { get; init; } = 3;
    public double OverfillDamagePerLoad { get; init; } = 0.6;
}

/// <summary>
/// The mine head's steam lift (spec D.2: "requires the locomotive coupled nearby and venting pressure to power it"; queue #105,
/// note 368). Field docs in facilities.json.
/// </summary>
public sealed record LiftTuning
{
    public double Along { get; init; } = -25;
    public double ChuteHeight { get; init; } = 4.6;
    public double FrameLateral { get; init; } = 16;
    public double LeverAlong { get; init; } = -3;
    public double LeverLateral { get; init; } = 3.4;
    public double LeverReach { get; init; } = 1.2;
    public double SteamReach { get; init; } = 56;
    public double SteamPerSkip { get; init; } = 20;
    public double PerSkip { get; init; } = 0.25;
    public double Tolerance { get; init; } = 1.5;
    public double Ore { get; init; } = 3;
    public double OverfillDamagePerLoad { get; init; } = 0.6;
}

/// <summary>
/// The grain elevator's conveyor line (spec D.2: "start machinery at a powerhouse, then clear jams as they occur. 1 + 1
/// roaming"; queue #136, note 400). Field docs in facilities.json.
/// </summary>
public sealed record ConveyorTuning
{
    public double Ahead { get; init; } = 15.5;
    public double Back { get; init; } = 64.5;
    public double HeadHeight { get; init; } = 4.8;
    public double KneeLateral { get; init; } = 4.6;
    public double BeltHeight { get; init; } = 1.0;
    public double Run { get; init; } = 26;
    public double TailLateral { get; init; } = 9;
    public double StarterAlong { get; init; } = 2.5;
    public double StarterLateral { get; init; } = 6.5;
    public double StarterReach { get; init; } = 1.4;
    public double StartSeconds { get; init; } = 4;
    public double StartRounds { get; init; } = 0.25;
    public double Tolerance { get; init; } = 1.5;
    public double PerSecond { get; init; } = 0.04;
    public double Grain { get; init; } = 3;
    public double[] JamEvery { get; init; } = [30, 60];
    public double JamFrom { get; init; } = 0.1;
    public double JamTo { get; init; } = 1.0;
    public double ClearReach { get; init; } = 1.6;
    public double ClearSeconds { get; init; } = 2.5;
    public double StallSeconds { get; init; } = 20;
    public double NearBelt { get; init; } = 25;
}

/// <summary>
/// The mine head's tipple (spec D.2: "Clamp the car, rotate it to load. 1 crew. Bad clamp derails the car on the spur"; queue
/// #159, note 423). Field docs in facilities.json.
/// </summary>
public sealed record TippleTuning
{
    public double Back { get; init; } = 84;
    public double BehindLift { get; init; } = 15.5;
    public double BinLateral { get; init; } = 7;
    public double BinHeight { get; init; } = 6;
    public double LeverAlong { get; init; } = 3.5;
    public double LeverLateral { get; init; } = 3.6;
    public double LeverReach { get; init; } = 1.3;
    public double Tolerance { get; init; } = 1.2;
    public double GoodClamp { get; init; } = 0.6;
    public double ClampSeconds { get; init; } = 1.5;
    public double RollSeconds { get; init; } = 6;
    public double BackSeconds { get; init; } = 3;
    public double PerRoll { get; init; } = 0.5;
    public double Ore { get; init; } = 3;
    public double BadAt { get; init; } = 0.3;
    public double Spill { get; init; } = 0.5;
    public double DerailDamage { get; init; } = 0.2;
    public double RerailSeconds { get; init; } = 20;
    public double RerailReach { get; init; } = 2.5;
    public double RollDegrees { get; init; } = 150;
}

/// <summary>The slaughterhouse's livestock ramp (GDD §18; spec D.2 livestock ramp). Field docs in facilities.json.</summary>
public sealed record RampTuning
{
    public double Along { get; init; } = -10;
    public double RampLateral { get; init; } = 2.6;
    public double PenLateral { get; init; } = 10;
    public double PenRadius { get; init; } = 4;
    public int[] Head { get; init; } = [4, 6];
    public double LoadPerHead { get; init; } = 0.2;
    public double SecondsPerHead { get; init; } = 6;
    public int Herders { get; init; } = 2;
    public double ExtraHerder { get; init; } = 0.25;
    public double CarReach { get; init; } = 9;
    public double ChoirFloor { get; init; } = 0.35;
}

/// <summary>The chemical works' fluid gantry (GDD §18 "leaks. Do not fire indoors"; spec D.2). Field docs in facilities.json.</summary>
public sealed record HoseTuning
{
    public double Along { get; init; } = 4;
    public double Lateral { get; init; } = 4.2;
    public double Reach { get; init; } = 1.4;
    public double CarReach { get; init; } = 9;
    public double TearSlack { get; init; } = 2;
    public double ConnectSeconds { get; init; } = 2.5;
    public double FlowPerSecond { get; init; } = 0.04;
    public double PressureRise { get; init; } = 0.05;
    public double Bleed { get; init; } = 0.15;
    public double LeakRadius { get; init; } = 6;
    public double LeakDamagePerSecond { get; init; } = 12;
    public double SpoilPerSecond { get; init; } = 0.02;
    public double TornSeconds { get; init; } = 20;
    public double TornSpoil { get; init; } = 0.25;
    public double IndoorsM { get; init; } = 60;
}

/// <summary>The military depot's powder (GDD §18-19 "explodes"). Field docs in facilities.json.</summary>
public sealed record KegTuning
{
    public double BlowSpeed { get; init; } = 6;
    public double KillRadius { get; init; } = 3;
    public double Radius { get; init; } = 9;
    public int Damage { get; init; } = 150;
    public double Chain { get; init; } = 3;
    public double CarDamage { get; init; } = 0.3;
}

/// <summary>The switchyard's standing cars (GDD §18 "cars scattered across six sidings"; WP15b). Field docs in facilities.json.</summary>
public sealed record RakesTuning
{
    public int[] Cars { get; init; } = [1, 2];
    public double[] Load { get; init; } = [0.5, 1];
    public string[] Cargoes { get; init; } = ["goods"];
    public double Back { get; init; } = 1;
    public int Spare { get; init; } = 1;
}

/// <summary>A blocked siding's derelict cars as they stand in the world (level-design D.2; note 294). Field docs in facilities.json.</summary>
public sealed record DerelictsTuning
{
    public double Back { get; init; } = 1;
    public double Integrity { get; init; } = 0.4;
    public double Pays { get; init; } = 0.3;
}

/// <summary>The wreck yard's derailed train (GDD §18 "unstable, unlit"; WP15b). Field docs in facilities.json.</summary>
public sealed record WreckYardTuning
{
    public int[] Heaps { get; init; } = [3, 4];
    public double[][] Layout { get; init; } = [[8, 1.5], [20, 4.5], [-24, 12], [16, 15]];
    public int[] Salvage { get; init; } = [2, 3];
    public double LampReach { get; init; } = 6;
    public double BeamLength { get; init; } = 40;
    public double BeamSpread { get; init; } = 0.2;
    public double StrainPerPiece { get; init; } = 0.4;
    public double WarnSeconds { get; init; } = 3;
    public double CrushRadius { get; init; } = 3.5;
    public int Damage { get; init; } = 45;
    public double Settle { get; init; } = 0.6;
    public int Shifts { get; init; } = 2;
}

/// <summary>
/// One heap of the wreck yard's derailed train (GDD §18; WP15b, note 187): a car on its side beside the line, and the
/// salvage still in it, which nobody finds in the dark. Its stability goes as pieces are pulled out of it; at none it groans
/// for a few seconds and then shifts, on whoever's by it.
/// </summary>
public sealed class WreckHeap(int index, Double3 centre, double yaw, int salvage)
{
    public int Index { get; } = index;
    /// <summary>Where it lies (on the ground), and which way its length runs (radians, from the track's heading).</summary>
    public Double3 Centre { get; } = centre;
    public double Yaw { get; } = yaw;
    public int SalvageStart { get; } = salvage;
    /// <summary>Pieces still in it, unfound: they come out onto the ground beside it the first time a lamp's on it.</summary>
    public int Salvage { get; internal set; } = salvage;
    public bool Found { get; internal set; }
    /// <summary>1 settled, 0 about to go.</summary>
    public double Stability { get; internal set; } = 1;
    /// <summary>Seconds of groaning left before it shifts (0: quiet). The tell.</summary>
    public double Groan { get; internal set; }
    /// <summary>How many times it's shifted (each tips it further; it settles for good after the tuning's shifts).</summary>
    public int Shifts { get; internal set; }
    /// <summary>Who pulled the last piece out of it. Host only.</summary>
    internal int By = -1;

    public HeapState State => new(Salvage, Found, Stability, Groan, Shifts);

    public void Mirror(in HeapState s)
    {
        Salvage = s.Salvage;
        Found = s.Found;
        Stability = s.Stability;
        Groan = s.Groan;
        Shifts = s.Shifts;
    }
}

/// <summary>A wreck heap's replicated state.</summary>
public readonly record struct HeapState(int Salvage, bool Found, double Stability, double Groan, int Shifts);

/// <summary>Where a crane's casting is (T48): on the ground where it was stacked, on the hook, or lashed on a car.</summary>
public enum CastingState : byte { Stacked, Hooked, Loaded, Lost }

/// <summary>A site's replicated state (the Run record), for a client to adopt.</summary>
public readonly record struct SiteState(bool Stocked, double Progress, int SledsLeft, bool Turning, bool OutOfRhythm, double Crank,
    Stops.PowerState Power = Stops.PowerState.Live, double Restart = 0)
{
    /// <summary>The set pieces' (WP15, note 185): the spout's grain left and whether it's pouring; the herd left, how far the next is up the ramp, and whether it's being driven; the hose's car, its pressure, and a torn hose's leak left.</summary>
    public double Bin { get; init; }
    public bool Pouring { get; init; }
    public int Head { get; init; }
    public double Herd { get; init; }
    public bool Herding { get; init; }
    public int HoseCar { get; init; } = -1;
    public double Pressure { get; init; }
    public double Leak { get; init; }
    /// <summary>The steam lift's (note 368): the ore left down the shaft, how far the skip's wound up (0..1), and whether it's winding.</summary>
    public double Ore { get; init; }
    public double Wind { get; init; }
    public bool Winding { get; init; }
    /// <summary>
    /// The conveyor line's (note 400): the grain left in the elevator for it, whether its drive's running and whether it's
    /// carrying grain into a car, where along its low run it's jammed (0..1 from the tail; −1 not), how long it's been jammed,
    /// and how far through starting it and clearing the jam whoever's at them are (seconds held).
    /// </summary>
    public double Grain { get; init; }
    public bool Running { get; init; }
    public bool Carrying { get; init; }
    public double Jam { get; init; } = -1;
    public double JamFor { get; init; }
    public double Start { get; init; }
    public double Clear { get; init; }
    /// <summary>
    /// The tipple's (note 423): the ore left in its bin, the car clamped in the cradle (−1 none) and whether the clamp is good,
    /// how far over it's rolled (0..1) and whether it's rolling back, how far through clamping someone is (s held), and how
    /// far a car off its rails at it is put back (s of wrench work).
    /// </summary>
    public double TippleOre { get; init; }
    public int Clamped { get; init; } = -1;
    public bool GoodClamp { get; init; }
    public double Roll { get; init; }
    public bool RollingBack { get; init; }
    public double Clamp { get; init; }
    public double Rerail { get; init; }
}

/// <summary>Spec D.2 loading modules built so far.</summary>
public enum ModuleKind : byte { Crates, Winch, Crane, Spout, Ramp, Hose, Rakes, Wreck, Lift, Conveyor, Tipple }

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
        double mainDistance, int crates, int spur = RailLine.MainPath, int heavy = 0, double? room = null, int head = 0, int[]? salvage = null)
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
        if (Has(ModuleKind.Spout))
        {
            // Over the track, as far back from the end as the standing track allows (up to "back"): the car nearest the
            // engine comes under it with the engine well short of the buffer stop, and the ones behind it as it runs on in.
            var sp = t.Spout;
            double back = Math.Min(sp.Back, Math.Max(0, (room ?? track.Length - mid) - 4));
            SpoutAlong = room is null ? mid : Math.Max(0, track.Length - back);
            Spout = At(SpoutAlong - mid, 0, sp.Height);
            SpoutLever = At(SpoutAlong - mid + sp.LeverAlong, sp.LeverLateral, 0.9);
            Bin = sp.Bin;
        }
        if (Has(ModuleKind.Lift))
        {
            // The headframe stands over its shaft out on the site's side, where the facility's buildings put it (the art's
            // headframe), its chute reaching over the track: the train's walked under it a car at a time, as under the spout. The
            // lever's on the near side, back along from it, where whoever's on it sees the car under the chute and the engine.
            var l = t.Lift;
            LiftAlong = Math.Clamp(mid + l.Along, 0, track.Length);
            LiftChute = At(LiftAlong - mid, 0, l.ChuteHeight);
            Headframe = At(LiftAlong - mid, l.FrameLateral);
            LiftLever = At(LiftAlong - mid + l.LeverAlong, l.LeverLateral, 0.9);
            Ore = l.Ore;
        }
        if (Has(ModuleKind.Conveyor))
        {
            // Its head over the track a car ahead of the spout (or laid as the spout is, from the buffer stop), the riser down to
            // the knee beside the track, and the low run on along it toward the elevator, out to the tail and its drive house:
            // whoever starts it there sees down the belt to the head, and whoever roams it walks beside the low run.
            var c = t.Conveyor;
            double back = Math.Min(c.Back, Math.Max(0, (room ?? track.Length - mid) - 4));
            ConveyorAlong = Has(ModuleKind.Spout) ? Math.Min(track.Length, SpoutAlong + c.Ahead) : room is null ? mid : Math.Max(0, track.Length - back);
            ConveyorHead = At(ConveyorAlong - mid, 0, c.HeadHeight);
            ConveyorKnee = At(ConveyorAlong - mid, c.KneeLateral, c.BeltHeight);
            ConveyorTail = At(ConveyorAlong - mid + c.Run, c.TailLateral, c.BeltHeight);
            ConveyorStarter = At(ConveyorAlong - mid + c.Run + c.StarterAlong, c.StarterLateral, 0.9);
            Grain = c.Grain;
        }
        if (Has(ModuleKind.Tipple))
        {
            // The cradle on the track a car's pitch behind the lift's chute, so a car under the chute and the one behind it in the
            // cradle load at once (without a lift, laid from the buffer stop as the spout is); the ore bin out on the site's side
            // with its chute over where a car rolled toward it would take it, the lever on from it.
            var tp = t.Tipple;
            double back = Math.Min(tp.Back, Math.Max(0, (room ?? track.Length - mid) - 4));
            TippleAlong = Has(ModuleKind.Lift) ? Math.Max(0, LiftAlong - tp.BehindLift) : room is null ? mid : Math.Max(0, track.Length - back);
            Cradle = At(TippleAlong - mid, 0);
            TippleBin = At(TippleAlong - mid, tp.BinLateral, tp.BinHeight);
            TippleLever = At(TippleAlong - mid + tp.LeverAlong, tp.LeverLateral, 0.9);
            TippleOre = tp.Ore;
        }
        if (Has(ModuleKind.Ramp))
        {
            var r = t.Ramp;
            RampTop = At(r.Along, r.RampLateral, 1.2);
            Pen = At(r.Along, r.PenLateral);
            PenRadius = r.PenRadius;
            HeadStart = Head = head;
        }
        if (Has(ModuleKind.Hose))
            HoseStand = At(t.Hose.Along, t.Hose.Lateral);
        MainDistance = mainDistance;
        if (Has(ModuleKind.Wreck) && salvage is not null)
        {
            // Laid out from the end of the line, where the last train came off (GDD §18): some through the buffer stop and
            // strewn on beyond it, in the headlamp of an engine run up to it; the rest beside the track, in the dark.
            var w = t.Wreck;
            var heaps = new List<WreckHeap>();
            for (int i = 0; i < salvage.Length && i < w.Layout.Length; i++)
            {
                double along = w.Layout[i][0], lateral = w.Layout[i][1];
                heaps.Add(new WreckHeap(i, Off(track, track.Length + along, side * lateral), (i % 2 == 0 ? 1 : -1) * (0.35 + 0.3 * i), salvage[i]));
            }
            Heaps = heaps;
        }
    }

    /// <summary>A point beside a track, <paramref name="lateral"/> to its right, past its end on along its last heading.</summary>
    internal static Double3 Off(RailLine track, double along, double lateral)
    {
        var sample = track.Sample(Math.Clamp(along, 0, track.Length));
        var right = Double3.Cross(sample.Tangent, Double3.Up).Normalized;
        return sample.Position + sample.Tangent * Math.Max(0, along - track.Length) + right * lateral;
    }

    /// <summary>About where the modules are along the main line (to find the ground, and a body's line hint, from).</summary>
    public double MainDistance { get; }

    /// <summary>The wreck yard's heaps (WP15b, note 187); none elsewhere.</summary>
    public IReadOnlyList<WreckHeap> Heaps { get; } = [];

    /// <summary>The spout's mouth over the track, how far along the track it is, its lever beside the track, and the car-loads of grain in the bin.</summary>
    public Double3 Spout { get; }
    public double SpoutAlong { get; }
    public Double3 SpoutLever { get; }
    public double Bin { get; internal set; }
    /// <summary>Someone's holding the spout's lever and grain's coming down (the elevator's machinery: loud).</summary>
    public bool Pouring { get; internal set; }
    /// <summary>Who's on the spout's lever this tick (−1 for nobody). Host only.</summary>
    internal int Pourer = -1;

    /// <summary>
    /// The steam lift (note 368): its chute's mouth over the track and how far along the track it is, the headframe over the
    /// shaft, the lever beside the track; the car-loads of ore left down the shaft, how far the skip's wound up (0..1), and
    /// whether it's winding (someone on the lever, and the engine's steam coming).
    /// </summary>
    public Double3 LiftChute { get; }
    public double LiftAlong { get; }
    public Double3 Headframe { get; }
    public Double3 LiftLever { get; }
    public double Ore { get; internal set; }
    public double Wind { get; internal set; }
    public bool Winding { get; internal set; }
    /// <summary>Who's on the lift's lever this tick (−1 for nobody). Host only.</summary>
    internal int Winder = -1;
    /// <summary>Someone was on the lift's lever last tick, steam or none (host only): what the driver's told to vent for ("steam!").</summary>
    public bool LeverHeld { get; internal set; }

    /// <summary>
    /// The conveyor line (note 400): its head over the track and how far along the track it is, the knee where the riser
    /// comes down beside the track, the tail out by the elevator, and the drive house's starter; the grain left for it.
    /// </summary>
    public Double3 ConveyorHead { get; }
    public double ConveyorAlong { get; }
    public Double3 ConveyorKnee { get; }
    public Double3 ConveyorTail { get; }
    public Double3 ConveyorStarter { get; }
    public double Grain { get; internal set; }
    /// <summary>The drive's on (started, and not stalled since); carrying grain into a car this tick.</summary>
    public bool Running { get; internal set; }
    public bool Carrying { get; internal set; }
    /// <summary>Where along the low run it's jammed (0 the tail, 1 the knee), or −1; how long it has been (s).</summary>
    public double Jam { get; internal set; } = -1;
    public double JamFor { get; internal set; }
    /// <summary>Seconds someone's held the starter (it starts at <see cref="ConveyorTuning.StartSeconds"/>), and the jam.</summary>
    public double Start { get; internal set; }
    public double Clear { get; internal set; }
    /// <summary>Host: seconds of carrying till the next jam (drawn as each comes), and how many there have been.</summary>
    internal double NextJam = -1;
    internal int Jams;
    /// <summary>How many times it's jammed this night (host only).</summary>
    public int JamCount => Jams;
    /// <summary>Who's at the starter, and at the jam, this tick (−1 for nobody). Host only.</summary>
    internal int Starter = -1, Clearer = -1;
    /// <summary>Where the jam is: on the low run, from the tail to the knee.</summary>
    public Double3 JamAt => Double3.Lerp(ConveyorTail, ConveyorKnee, Math.Clamp(Jam, 0, 1));

    /// <summary>
    /// The tipple (note 423): its cradle on the track and how far along the track it is, the ore bin's chute out on the site's
    /// side, its lever; the ore left in the bin.
    /// </summary>
    public Double3 Cradle { get; }
    public double TippleAlong { get; }
    public Double3 TippleBin { get; }
    public Double3 TippleLever { get; }
    public double TippleOre { get; internal set; }
    /// <summary>The car clamped in the cradle (−1 none), and whether it was clamped true (a bad clamp derails it on the roll).</summary>
    public int Clamped { get; internal set; } = -1;
    public bool GoodClamp { get; internal set; }
    /// <summary>How far over the cradle has rolled its car (0..1), and whether it's on its way back.</summary>
    public double Roll { get; internal set; }
    public bool RollingBack { get; internal set; }
    /// <summary>Seconds someone's held the lever to clamp; seconds of wrench work on a car off its rails here.</summary>
    public double Clamp { get; internal set; }
    public double Rerail { get; internal set; }
    /// <summary>Who's on the tipple's lever this tick (−1 nobody), and how many have a wrench to a car off its rails here. Host only.</summary>
    internal int Tippler = -1, Rerailers;

    /// <summary>The ramp's top by the cars, the pen out beyond it, the head penned at the start and left.</summary>
    public Double3 RampTop { get; }
    public Double3 Pen { get; }
    public double PenRadius { get; }
    public int HeadStart { get; }
    public int Head { get; internal set; }
    /// <summary>How far the next one's been driven up the ramp (0..1), and whether enough are driving them now.</summary>
    public double Herd { get; internal set; }
    public bool Herding { get; internal set; }
    /// <summary>The herd's stirred up (being driven, or some aboard and some still penned): spec D.2 "constant noise. Raises Choir floor while active".</summary>
    public bool Stirred => Head > 0 && (Herding || Head < HeadStart);
    /// <summary>Who's driving the herd this tick. Host only.</summary>
    internal readonly SortedSet<int> Herders = [];

    /// <summary>The fluid gantry's stand, the car its hose is on (−1 for none), its pressure (0..1, leaking at 1), and a torn hose's leak left (seconds).</summary>
    public Double3 HoseStand { get; }
    public int HoseCar { get; internal set; } = -1;
    public double Pressure { get; internal set; }
    public double Leak { get; internal set; }
    public bool Leaking => Pressure >= 1 - 1e-9 || Leak > 0;
    /// <summary>Someone's at the stand minding the pressure this tick. Host only.</summary>
    internal bool Attended;

    /// <summary>The gantry crane here, if the facility has one (T48).</summary>
    public Crane? Crane { get; }

    /// <summary>
    /// The yard's gantries from its stop layout (level-design P5, P18): one over each craned loading face but the
    /// facility's own, each with a casting in every bay its runway reaches.
    /// </summary>
    public IReadOnlyList<Crane> YardCranes { get; internal set; } = [];

    /// <summary>Every crane here: the facility's own first.</summary>
    public IReadOnlyList<Crane> Cranes => Crane is null ? YardCranes : [Crane, .. YardCranes];

    /// <summary>
    /// Where the crew work this site's modules (note 279): each crane's stand, legs and castings, the spout and its lever, the
    /// ramp and the pen, the hose stand, the capstan and the sled's run, the crate and heavy-crate stacks. The modules are laid
    /// from the spur, not round the stop's buildings, so a stop's walls give way where they stand (<see cref="StopWalls.Clear"/>).
    /// </summary>
    public IEnumerable<Double3> WorkPoints()
    {
        foreach (var c in Cranes)
        {
            yield return c.Controls;
            for (int end = 0; end < 2; end++)
                for (int side = 0; side < 2; side++)
                    yield return c.Corner(end, side);
            foreach (var k in c.Castings)
                yield return k.At;
        }
        if (Has(ModuleKind.Spout))
        {
            yield return Spout;
            yield return SpoutLever;
        }
        if (Has(ModuleKind.Ramp))
        {
            yield return RampTop;
            yield return Pen;
        }
        if (Has(ModuleKind.Hose))
            yield return HoseStand;
        if (Has(ModuleKind.Lift))
        {
            yield return LiftChute;
            yield return Headframe;
            yield return LiftLever;
        }
        if (Has(ModuleKind.Tipple))
        {
            yield return Cradle;
            yield return TippleBin with { Y = Cradle.Y };
            yield return TippleLever;
        }
        if (Has(ModuleKind.Conveyor))
        {
            // The belt's whole low run (anywhere a jam can be cleared from), the riser and the drive house.
            for (int i = 0; i <= 4; i++)
                yield return Double3.Lerp(ConveyorTail, ConveyorKnee, i / 4.0);
            yield return ConveyorHead;
            yield return ConveyorStarter;
        }
        if (Has(ModuleKind.Winch))
        {
            yield return Capstan;
            double run = (SledTo - SledFrom).Length;
            for (double x = 0; x <= run; x += 2)
                yield return SledFrom + (SledTo - SledFrom) * (run > 0 ? x / run : 0);
        }
        foreach (var p in CrateStack ?? [])
            yield return p;
        foreach (var p in HeavyStack ?? [])
            yield return p;
    }

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
        return Handles[handle] + (Outward * DMath.Cos(a) + Double3.Up * DMath.Sin(a)) * CrankRadius;
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
        return DMath.Atan2(v, u) - handle * Math.PI;
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

    /// <summary>
    /// The yard's power (level-design D.2): live, low (the cranes at half speed) or dead (not at all) until someone's
    /// restarted it at the powerhouse door, or an open powerhouse's switchboard inside (note 509), <see cref="Powerhouse"/>;
    /// <see cref="Restart"/> is the seconds held so far.
    /// </summary>
    public Stops.PowerState Power { get; internal set; }
    public Double3? Powerhouse { get; internal set; }
    public double Restart { get; internal set; }
    /// <summary>Who's at the powerhouse restarting it this tick (−1 for nobody). Host only.</summary>
    internal int Restarter = -1;

    public SiteState State => new(Stocked, Progress, SledsLeft, Turning, OutOfRhythm, Crank, Power, Restart)
    {
        Bin = Bin,
        Pouring = Pouring,
        Head = Head,
        Herd = Herd,
        Herding = Herding,
        HoseCar = HoseCar,
        Pressure = Pressure,
        Leak = Leak,
        Ore = Ore,
        Wind = Wind,
        Winding = Winding,
        Grain = Grain,
        Running = Running,
        Carrying = Carrying,
        Jam = Jam,
        JamFor = JamFor,
        Start = Start,
        Clear = Clear,
        TippleOre = TippleOre,
        Clamped = Clamped,
        GoodClamp = GoodClamp,
        Roll = Roll,
        RollingBack = RollingBack,
        Clamp = Clamp,
        Rerail = Rerail,
    };

    /// <summary>Client side: adopts the host's state.</summary>
    public void Mirror(in SiteState s)
    {
        Stocked = s.Stocked;
        Progress = s.Progress;
        SledsLeft = s.SledsLeft;
        Turning = s.Turning;
        OutOfRhythm = s.OutOfRhythm;
        Crank = s.Crank;
        Power = s.Power;
        Restart = s.Restart;
        Bin = s.Bin;
        Pouring = s.Pouring;
        Head = s.Head;
        Herd = s.Herd;
        Herding = s.Herding;
        HoseCar = s.HoseCar;
        Pressure = s.Pressure;
        Leak = s.Leak;
        Ore = s.Ore;
        Wind = s.Wind;
        Winding = s.Winding;
        Grain = s.Grain;
        Running = s.Running;
        Carrying = s.Carrying;
        Jam = s.Jam;
        JamFor = s.JamFor;
        Start = s.Start;
        Clear = s.Clear;
        TippleOre = s.TippleOre;
        Clamped = s.Clamped;
        GoodClamp = s.GoodClamp;
        Roll = s.Roll;
        RollingBack = s.RollingBack;
        Clamp = s.Clamp;
        Rerail = s.Rerail;
    }
}
