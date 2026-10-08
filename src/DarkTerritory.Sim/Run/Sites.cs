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
    /// <summary>GDD §18's switchyard and wreck yard (WP15b, note 187): the yard's standing cars, and the wreck to salvage.</summary>
    public RakesTuning Rakes { get; init; } = new();
    public DerelictsTuning Derelicts { get; init; } = new();
    public WreckYardTuning Wreck { get; init; } = new();
    /// <summary>Who works a stop (note 261, spec D.2's "Crew" column): the driver getting down, people playing as hands.</summary>
    public StopCrewTuning Crew { get; init; } = new();

    /// <summary>On a spur, the modules are laid out from this far back from its buffer stop (beside the first cars).</summary>
    public double SpurLayout { get; init; } = 45;

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
}

/// <summary>Spec D.2 loading modules built so far.</summary>
public enum ModuleKind : byte { Crates, Winch, Crane, Spout, Ramp, Hose, Rakes, Wreck }

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
    /// restarted it at the powerhouse door, <see cref="Powerhouse"/>; <see cref="Restart"/> is the seconds held so far.
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
    }
}
