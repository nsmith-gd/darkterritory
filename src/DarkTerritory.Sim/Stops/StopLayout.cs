using System.Text.Json.Serialization;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;

namespace DarkTerritory.Sim.Stops;

/// <summary>What a stop is (level-design P1): a yard, a yard with a village, or a village alone at a halt.</summary>
public enum StopKind : byte { Yard, YardAndVillage, Village }

/// <summary>
/// Yard forms (P20). Every one is a set of spurs off the main line, nested so the first switch leads to the outermost
/// track: a spur is one; a ladder is several with shed rows between them; a fan's tracks end in a curve away from the
/// main line with the hero laid along it; a split yard adds a spur on the far side of the main line.
/// </summary>
public enum YardForm : byte { Spur, Ladder, Fan, Split }

public enum VillageForm : byte { Blocks, Street, Crossroads, Farmsteads }

/// <summary>How a stop's village sits against its yard (P1): across the line, across and set back, or further along.</summary>
public enum Arrangement : byte { Single, Opposite, Setback, Along }

public enum StopZone : byte { Yard, Village }

public enum BuildingKind : byte { Shed, Hero, House, Outbuilding, Barn, Well, PrisonCar, SignalBox, LampRoom, WaterTower, Lockup }

/// <summary>A Holdout's type (GDD App. D.4): how it's freed, and what it looks like.</summary>
public enum HoldoutKind : byte { PrisonCar, Shelter, Lockup }

/// <summary>Which of D.4's sites a Holdout serves: a facility's pad, a halt, or a dead town.</summary>
public enum HoldoutSite : byte { Facility, Halt, Village }

/// <summary>Where an outside creature lives (GDD B.6, B.8): the director spawns it there.</summary>
public enum LairKind : byte { Warren, GauntRoost, FollowerGround, SootCall, GrumblerPerch, WhistlerNest }

public enum HouseShape : byte { Rect, L, Cross, Pair, Square }

public enum RoadKind : byte { Through, Street, Lane, Stub }

/// <summary>Container kinds (P14): where loot can be. The economy decides what's in each (loot.json).</summary>
public enum ContainerKind : byte { CraneBay, CrateStack, Strongroom, Cupboard, Cabinet, Cellar, UnderFloor, Bench, Hayloft }

/// <summary>A point in a stop's rail frame: <see cref="S"/> metres along the main line from the zone's start, <see cref="D"/> to its right.</summary>
public readonly record struct Pt(double S, double D)
{
    public static Pt operator +(Pt a, Pt b) => new(a.S + b.S, a.D + b.D);
    public static Pt operator -(Pt a, Pt b) => new(a.S - b.S, a.D - b.D);
    public static Pt operator *(Pt a, double k) => new(a.S * k, a.D * k);
    [JsonIgnore] public double Length => Math.Sqrt(S * S + D * D);
    [JsonIgnore] public Pt Unit => Length > 1e-12 ? this * (1 / Length) : new(1, 0);
    /// <summary>This turned a quarter towards +D: along the line, it points to the right.</summary>
    [JsonIgnore] public Pt Normal => new(-D, S);
    public static double Distance(Pt a, Pt b) => (a - b).Length;
}

/// <summary>A rectangle in a house's own frame (x along its length, y across), for L, cross and paired footprints.</summary>
public readonly record struct FootprintPart(double X, double Y, double Length, double Width);

/// <summary>
/// A building's footprint: centred at (S, D), <see cref="Length"/> along its own axis, which is <see cref="Yaw"/>
/// radians from the main line's direction towards +D.
/// </summary>
public sealed record StopBuilding(BuildingKind Kind, StopZone Zone, double S, double D, double Length, double Width, double Yaw)
{
    public HouseShape Shape { get; init; }
    public IReadOnlyList<FootprintPart> Parts { get; init; } = [];
    /// <summary>A variety index for the art (house colour and variant). Just variety (Example 1, answered).</summary>
    public int Variant { get; init; }
    /// <summary>The yard tracks a shed stands between (P5), by index into <see cref="StopLayout.Tracks"/>.</summary>
    public IReadOnlyList<int> Tracks { get; init; } = [];
    /// <summary>A house at the end of a stub road: better odds, furthest from the engine (P11).</summary>
    public bool Outlier { get; init; }
    [JsonIgnore] public Pt Centre => new(S, D);
}

/// <summary>
/// A yard track: a spur off the main line at its own switch (<see cref="Toe"/> along the zone), out through an S-curve
/// to <see cref="Offset"/> beside the main line, along the loading face, and (a fan) round a curve away. Its
/// <see cref="Segments"/> are what the route builds it from; <see cref="Path"/> is the same track sampled in the rail
/// frame (worked out from them, so not stored).
/// </summary>
public sealed record YardTrack(int Index, int Side, double Toe, double Offset, IReadOnlyList<TrackSegment> Segments, Pt FaceStart, Pt FaceEnd)
{
    IReadOnlyList<Pt>? _path;
    [JsonIgnore] public IReadOnlyList<Pt> Path => _path ??= Plan.Lay(Toe, Segments);

    /// <summary>
    /// Metres back from the buffer stop that cars stand on to be worked: the straight and, a fan, its curve; not the
    /// S-curve out from the main line (P16).
    /// </summary>
    public double Standing { get; init; }
    /// <summary>Cars that stand there with the engine at the buffer stop (SpurDrill's rule).</summary>
    public int Capacity { get; init; }
    /// <summary>
    /// Distance along the track (from its switch) to the middle of the loading face: where a facility's own loading
    /// modules are laid out on its primary track (spec D.2).
    /// </summary>
    public double Loading { get; init; }
    /// <summary>Cars that stand along the loading face at once.</summary>
    public int FaceCars { get; init; }
    /// <summary>Switches passed from the main line's first to reach it (P7's depth).</summary>
    public int Depth { get; init; }
    public CraneRunway? Crane { get; init; }
    /// <summary>The first track: the facility's own, where its loading modules stand (spec D.2).</summary>
    public bool Primary { get; init; }
    /// <summary>On the far side of the main line (a split yard).</summary>
    public bool Across { get; init; }
    [JsonIgnore] public double Length => Segments.Sum(s => s.Length);
}

/// <summary>A gantry crane's runway along a track's loading face, <see cref="From"/> to <see cref="To"/> along the zone.</summary>
public sealed record CraneRunway(double From, double To, double Reach, int Bays)
{
    [JsonIgnore] public double Length => To - From;
}

public sealed record StopRoad(RoadKind Kind, IReadOnlyList<Pt> Points);

/// <summary>
/// A Holdout (GDD App. D.4): a sealed building where a dead player waits for the crew. <see cref="Building"/> is its
/// footprint in <see cref="StopLayout.Buildings"/>; the crew breaches it at <see cref="Door"/>; its lamp at
/// <see cref="Lamp"/> is seen from the approach. <see cref="Second"/> is a facility's second, live only with a crew of five
/// or more (holdouts.json).
/// </summary>
public sealed record StopHoldout(int Index, HoldoutKind Kind, HoldoutSite Site, int Building, Pt Door, Pt Lamp, bool Second = false)
{
    /// <summary>A prison car's spare siding: derelict rails under it, their points long since lifted (not part of the line).</summary>
    public IReadOnlyList<Pt> Siding { get; init; } = [];
    /// <summary>How far a crew walks to it from the stopped consist, round buildings (D.14 "recoverability").</summary>
    public double Walk { get; init; }
}

/// <summary>An outside creature's place in a stop (GDD B.6, B.8): a spot and its reach, and a building or track if it's in one.</summary>
public sealed record StopLair(LairKind Kind, StopZone Zone, Pt At, double Radius, int Building = -1, int Track = -1);

/// <summary>Where loot can be (P14): a kind, a place, and which building it's in (−1 for none).</summary>
/// <param name="Band">P2's portability band: 0 under a crane, 1 crates, 2 pocketable off the line.</param>
/// <param name="Track">For a crane bay, the track whose crane reaches it (−1 otherwise).</param>
/// <param name="Floor">P12's floor guarantee: the one find a village always has.</param>
public sealed record StopContainer(int Index, ContainerKind Kind, StopZone Zone, Pt At, int Band, int Depth, int Building, int Track = -1, bool Outlier = false, bool Floor = false);

/// <summary>
/// What the crew has to do to work the stop (P15, level-design D.1), counted move by move for the tier's typical
/// empties, and the score those counts come to.
/// </summary>
public sealed record StopMoves
{
    public int Empties { get; init; }
    public int Trips { get; init; }
    public int Throws { get; init; }
    public int Couplings { get; init; }
    public int Reversals { get; init; }
    public int BlindMoves { get; init; }
    public int Respots { get; init; }
    public int HandCars { get; init; }
    /// <summary>Metres crates are carried from their sheds to the cars, there and back (P2: band 1 is carried).</summary>
    public double CarryWalk { get; init; }
    public int Unfilled { get; init; }
    public double SwitchWalk { get; init; }
    public bool CrossingBlocked { get; init; }
    public double VillageWalk { get; init; }
    public int Houses { get; init; }
    public double Yard { get; init; }
    public double Village { get; init; }
    public double Score { get; init; }
}

public sealed record StopCheck(string Name, bool Pass, string Detail, bool Applies = true);

/// <summary>
/// A stop's layout (level-design Parts D and Z): its yard tracks, buildings, roads and loot containers in the stop's
/// rail frame, how hard it is to work, and the invariants it was checked against. Generated from the route's seed,
/// so every machine builds the same one.
/// </summary>
public sealed record StopLayout
{
    public required ulong Seed { get; init; }
    /// <summary>Which attempt was kept (P15): 0 is the first.</summary>
    public required int Attempt { get; init; }
    public required RouteTier Tier { get; init; }
    public required StopKind Kind { get; init; }
    public YardForm? Form { get; init; }
    public VillageForm? VillageForm { get; init; }
    public Arrangement Arrangement { get; init; }
    /// <summary>The yard's side of the main line and the village's: −1 left, +1 right.</summary>
    public int YardSide { get; init; }
    public int VillageSide { get; init; }
    public double VillageOffset { get; init; }
    public required double ZoneLength { get; init; }
    public IReadOnlyList<YardTrack> Tracks { get; init; } = [];
    public IReadOnlyList<StopBuilding> Buildings { get; init; } = [];
    public IReadOnlyList<StopRoad> Roads { get; init; } = [];
    public IReadOnlyList<StopContainer> Containers { get; init; } = [];
    public Pt? Crossing { get; init; }
    /// <summary>A village halt's platform beside the main line (village-only stops).</summary>
    public Pt? Halt { get; init; }
    public double HaltLength { get; init; }
    /// <summary>Where the consist stops to work the stop (a yard's first loading face, or the halt): what D.4 measures from.</summary>
    public Pt StopPoint { get; init; }
    public IReadOnlyList<StopHoldout> Holdouts { get; init; } = [];
    public IReadOnlyList<StopLair> Lairs { get; init; } = [];
    /// <summary>Where the loaded cars wait on the main line, front and length (GDD §17).</summary>
    public double CutFront { get; init; }
    public double CutLength { get; init; }
    public required StopMoves Moves { get; init; }
    public bool InBand { get; init; }
    public IReadOnlyList<StopCheck> Checks { get; init; } = [];
    [JsonIgnore] public bool Valid => Checks.All(c => c.Pass || !c.Applies);

    [JsonIgnore] public bool HasYard => Kind != StopKind.Village;
    [JsonIgnore] public bool HasVillage => Kind != StopKind.Yard;
}
