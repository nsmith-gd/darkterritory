using System.Text.Json;
using System.Text.Json.Serialization;
using Ballast;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;

namespace DarkTerritory.Sim.LineGen;

/// <summary>
/// One night's line as data (plan §19.1): the route graph, every edge's alignment and profile, what's on and beside the
/// track, the speed authority and its tells, the stations, the terrain intents, the director's ground, the route card
/// and how validation went. Compact, deterministic, the same on every machine. The terrain and dressing are pure
/// functions of it (§4).
/// <para>
/// Distances ("s") are along an edge: the main line's own distance from the back of the fortress yard (so they are
/// <see cref="RailLine"/> distances), and a branch's from its points. The outer gate, km post 0, is at <see cref="GateM"/>.
/// </para>
/// </summary>
public sealed record LinePlan
{
    public string Version { get; init; } = "";
    public string Seed { get; init; } = "";
    public required PlanRoute Route { get; init; }
    public required PlanConsist Consist { get; init; }
    public required PlanWeather Weather { get; init; }
    /// <summary>Main-line distance of the outer gate (km post 0), the terminus's outer gate, and the line's end.</summary>
    public double GateM { get; init; }
    public double TerminusM { get; init; }
    public double LengthM { get; init; }
    public required PlanGraph Graph { get; init; }
    /// <summary>Each edge's track as the rail model builds it, by edge id.</summary>
    public required IReadOnlyList<PlanAlignment> Alignment { get; init; }
    public IReadOnlyList<PlanPiece> Pieces { get; init; } = [];
    public IReadOnlyList<PlanIntent> Intents { get; init; } = [];
    public IReadOnlyList<PlanStructure> Structures { get; init; } = [];
    public IReadOnlyList<PlanWater> Water { get; init; } = [];
    /// <summary>The lakes beside and across the line, and the shores it runs along (maritime-rules.md §2-4).</summary>
    public IReadOnlyList<PlanLake> Lakes { get; init; } = [];
    public IReadOnlyList<PlanShore> Shores { get; init; } = [];
    /// <summary>The country roads beside the main line and where they cross it at grade (maritime-rules.md §2.2).</summary>
    public IReadOnlyList<PlanRoad> Roads { get; init; } = [];
    public IReadOnlyList<PlanCrossing> Crossings { get; init; } = [];
    public required PlanAuthority Authority { get; init; }
    public IReadOnlyList<PlanSign> Signage { get; init; } = [];
    public IReadOnlyList<PlanPoi> Pois { get; init; } = [];
    public IReadOnlyList<PlanLandmark> Landmarks { get; init; } = [];
    public IReadOnlyList<PlanPad> Pads { get; init; } = [];
    public IReadOnlyList<PlanBiome> Biomes { get; init; } = [];
    public IReadOnlyList<PlanExposure> Exposure { get; init; } = [];
    public required PlanDirector Director { get; init; }
    public required PlanRouteCard RouteCard { get; init; }
    public IReadOnlyList<PlanMarker> Markers { get; init; } = [];
    public required PlanFortress Fortress { get; init; }
    public required PlanTerminus Terminus { get; init; }
    public PlanValidation Validation { get; init; } = new();
    /// <summary>The rules the sim holds a train to on this line, from content when it was generated (a save keeps them).</summary>
    public required PlanRules Rules { get; init; }

    /// <summary>Km as the posts and paperwork count it: from the outer gate.</summary>
    public double Km(double mainDistance) => (mainDistance - GateM) / 1000;

    public PlanAlignment Edge(string id) => Alignment.First(a => a.Edge == id);
    public PlanAlignment? EdgeOfBranch(int branch) => Alignment.FirstOrDefault(a => a.Branch == branch);

    static readonly JsonSerializerOptions Compact = new(DataFile.Options) { WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <summary>The plan as JSON: byte-identical for the same inputs (M0).</summary>
    public string ToJson(bool indented = false) => JsonSerializer.Serialize(this, indented ? new JsonSerializerOptions(Compact) { WriteIndented = true } : Compact);

    public static LinePlan FromJson(string json) => JsonSerializer.Deserialize<LinePlan>(json, Compact) ?? throw new InvalidDataException("no line plan");

    /// <summary>Size compressed, the §17.5 budget's measure.</summary>
    public int CompressedBytes() => Compress().Length;

    /// <summary>The plan's JSON, Brotli-compressed: what a save keeps (§17.4) and the network would send (§17.3).</summary>
    public byte[] Compress()
    {
        using var buffer = new MemoryStream();
        using (var gz = new System.IO.Compression.BrotliStream(buffer, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            gz.Write(System.Text.Encoding.UTF8.GetBytes(ToJson()));
        return buffer.ToArray();
    }

    public static LinePlan Decompress(byte[] data)
    {
        using var gz = new System.IO.Compression.BrotliStream(new MemoryStream(data), System.IO.Compression.CompressionMode.Decompress);
        using var text = new StreamReader(gz, System.Text.Encoding.UTF8);
        return FromJson(text.ReadToEnd());
    }

    /// <summary>A short fingerprint of the plan (FNV-1a over its JSON): whether two machines have the same line.</summary>
    public string Fingerprint() => Streams.Hash(ToJson()).ToString("x16");
}

public sealed record PlanRoute(string Id, RouteTier Tier, double Severity, double D, string Region, string Name);
public sealed record PlanConsist(int NPlan, double MaxMassT, double LengthM, double ClimbMaxPct, double BrakeMs2, double MainGradePct);
/// <param name="FogMinM">The night's thinnest sight through fog: what fairness plans against (§14).</param>
public sealed record PlanWeather(double FogMinM, double FogMaxM, bool Rain, double Cold, double Wind, int TempStep, double FogDensity);

public enum NodeType : byte { FortressDepart, Terminus, JunctionFacing, JunctionTrailing, FacilityJunction, DeadEnd }
public enum EdgeRole : byte { Main, Alternate, Spur, DeadLine }

/// <param name="S">Main-line distance, for nodes on the main line; a branch's end for a dead end.</param>
/// <param name="DefaultEdge">At a facing junction, the edge its switch is set for as the night starts (§6.3).</param>
public sealed record PlanNode(string Id, NodeType Type, double S, string? Name = null, string? DefaultEdge = null, int Number = 0);
public sealed record PlanEdge(string Id, EdgeRole Role, string From, string To, double LengthM, int Branch = -1);
public sealed record PlanGraph(IReadOnlyList<PlanNode> Nodes, IReadOnlyList<PlanEdge> Edges);

/// <summary>
/// An edge's track: the segments the rail model integrates (clothoids and vertical curves included), and for a branch
/// where it leaves the main line, which side, and for an alternate where it comes back.
/// </summary>
public sealed record PlanAlignment(string Edge, EdgeRole Role, int Branch, double Toe, int Side, double? Rejoin, IReadOnlyList<TrackSegment> Segments)
{
    public double Length => Segments.Sum(s => s.Length);
}

/// <param name="Depth">Stack depth over its range (§7.3), as laid.</param>
public sealed record PlanPiece(string Id, string Type, string Kind, string Edge, double S0, double S1, IReadOnlyDictionary<string, double> Params,
    IReadOnlyList<string> Tags, double Cost, int Depth = 1, string? Signature = null);

public enum IntentType : byte { Plain, Cutting, Embankment, LedgeUp, LedgeDrop, Ravine, River, Marsh, Mountain, Pad }

/// <summary>What the land does beside the rail (§12.1), relative to it: a height and the kind of slope.</summary>
public readonly record struct SideIntent(IntentType T, double H);

public sealed record PlanIntent(string Edge, double S0, double S1, SideIntent Left, SideIntent Right, string? Biome = null);

public enum StructureType : byte { Tunnel, Trestle, Girder, Truss, Viaduct, Causeway, RetainingWall, Washout, BrassField, Platform, BufferStop }

/// <param name="Weak">A weak bridge's car limit and crossing speed (§9.6).</param>
public sealed record PlanStructure(string Id, StructureType Type, string Edge, double S0, double S1, double HeightM = 0, WeakLimit? Weak = null,
    string? Name = null, string? Material = null, int Side = 0);
public sealed record WeakLimit(int MaxCars, double SpeedMs);

/// <summary>A water body's flat plane (§12.4): a river where it crosses, or a marsh's standing water along a stretch.</summary>
public sealed record PlanWater(string Id, string Type, double LevelM, string Edge, double S0, double S1, double WidthM);

/// <summary>
/// A lake (maritime-rules.md §2): an ellipse at (X, Z), <paramref name="RadiusM"/> across and <paramref name="Stretch"/>
/// times that along its heading (the ice's flow, as a unit vector so the terrain needs no trigonometry, §17.3), its
/// shore wobbling by <paramref name="Wobble"/>; its water at <paramref name="LevelM"/>, <paramref name="DepthM"/> deep.
/// </summary>
public sealed record PlanLake(string Id, double X, double Z, double RadiusM, double Stretch, double Cos, double Sin, double Wobble, double LevelM, double DepthM,
    bool Crossed);

/// <summary>
/// A country road beside the main line from <paramref name="S0"/> to <paramref name="S1"/>: starting on
/// <paramref name="FirstSide"/>, <paramref name="OffsetM"/> out, wandering <paramref name="WanderM"/> over
/// <paramref name="WavelengthM"/> (at <paramref name="Phase"/>), changing sides at each of its crossings.
/// </summary>
public sealed record PlanRoad(string Id, string Edge, double S0, double S1, int FirstSide, double OffsetM, double WanderM, double WavelengthM, double Phase);

/// <summary>Where a road crosses the line at grade: a level crossing, planks between the rails and a crossbuck each side.</summary>
public sealed record PlanCrossing(string Road, double S);

/// <summary>
/// The kinds of shore (maritime-rules.md §3-5): the Atlantic's rock and shingle, Fundy's red mudflats, a dyked marsh;
/// and a river the line runs up the valley of, its water falling with the rail (its level is then how far under it).
/// </summary>
public enum ShoreKind : byte { Sea, Fundy, Dyke, River }

/// <summary>
/// A shore along an edge from <paramref name="S0"/> to <paramref name="S1"/> on <paramref name="Side"/> (+1 right): the
/// water at <paramref name="LevelM"/>, its edge <paramref name="NearM"/> out plus up to <paramref name="CoveM"/> more in
/// coves every <paramref name="CoveWavelengthM"/> or so; <paramref name="FlatM"/> of mud at low water (Fundy), or the
/// river's width (River, whose <paramref name="LevelM"/> is its water's depth under the rail);
/// a dyke's crest <paramref name="DykeM"/> out, its fields <paramref name="FieldsM"/> under the rail (Dyke).
/// </summary>
public sealed record PlanShore(string Id, ShoreKind Kind, string Edge, double S0, double S1, int Side, double LevelM, double NearM, double CoveM,
    double CoveWavelengthM, double Phase, double FlatM, double DykeM = 0, double FieldsM = 0);

public enum LimitSource : byte { LineSpeed, Board, Form19, Restricted, Yard, Curve, Bridge, Brass }

public sealed record PlanLimit(string Edge, double S0, double S1, double VMs, LimitSource Source, string? Why = null);
/// <param name="Sleepers">Whether this zone holds Sleeper candidates (not every one does, §9.4).</param>
public sealed record PlanRestricted(string Edge, double S0, double S1, double VMs, double SightM, bool Sleepers, string Reason);

public enum DemandType : byte { Curve, WeakBridge, Brass, FacingJunction, FacilityStop, Washout, BufferStop, Restricted, YardLimit }

/// <summary>A point where the track needs the train at or below <see cref="VReq"/> by <see cref="SReq"/> (§9.2).</summary>
/// <param name="TellAt">Where the tell zone starts: at least one required tell sits at or before it.</param>
/// <param name="VIn">The communicated speed approaching it.</param>
/// <param name="VLethal">Above this at the demand the train is lost (a curve's derail speed); null when it's not a lethal check.</param>
public sealed record PlanDemand(string Id, DemandType Type, string Edge, double SReq, double VReq, double VIn, double WarnM, double TellAt, double? VLethal,
    double SEnd, string? Piece = null);

public sealed record PlanAuthority(double LineSpeedMs, double YardSpeedMs, IReadOnlyList<PlanLimit> Limits, IReadOnlyList<PlanRestricted> Restricted,
    IReadOnlyList<PlanDemand> Demands, double TellMargin, double TReactS, double BrakeMs2);

public enum SignState : byte { Intact, Fallen, Missing }

/// <param name="Side">+1 right, −1 left of the direction of travel.</param>
/// <param name="For">The demand or place it tells of.</param>
public sealed record PlanSign(string Type, string Edge, double S, int Side, string Text, SignState State, bool Required, double? Value = null, string? For = null);

/// <summary>A facility slot (§11.1) as handed to the POI generator.</summary>
public sealed record PlanPoi(string Id, FacilityKind Type, string Name, string Junction, double S, int Side, PlanRange Approach, PlanRange Holding,
    PlanPad Pad, string? SpurEdge, double SpurGrade, string SubSeed, double PowerBias, double[] Pickup, bool MinePortal);
public sealed record PlanRange(string Edge, double S0, double S1);

/// <summary>Flattened ground (§12.1 "pad"): a facility's, the fortress's, a settlement's.</summary>
/// <param name="Box">A box rather than a capsule: flat out to <paramref name="RadiusM"/> either side of its centre line and
/// <paramref name="HalfLengthM"/> along it, blended out past those (a stop's ground, PlanStops); the rail's formation stays
/// its own through it.</param>
public sealed record PlanPad(string Id, double X, double Z, double ElevM, double RadiusM, double HalfLengthM = 0, double HeadingDeg = 0, bool Box = false);

public sealed record PlanLandmark(string Type, string Name, string Edge, double S0, double S1, int Side = 0, bool LootTable = false);
public sealed record PlanBiome(string Edge, double S0, double S1, string Biome);

/// <summary>§14's per-segment weather modifiers.</summary>
public sealed record PlanExposure(string Edge, double S0, double S1, double Fog, double Wind, double Adhesion, int ColdStep, string Why);

public sealed record PlanTag(string Tag, string Edge, double S0, double S1);
public sealed record PlanZone(string Edge, double S0, double S1);
public sealed record PlanPressure(double StepM, IReadOnlyList<double> Values);
public sealed record PlanDirector(IReadOnlyList<PlanTag> Tags, IReadOnlyList<PlanZone> SleeperZones, IReadOnlyList<PlanZone> GreaseZones, PlanPressure Pressure,
    IReadOnlyList<PlanZone> SleeperPlaced, IReadOnlyList<PlanZone> GreasePlaced)
{
    /// <summary>§15.1 as the director reads it: under a tag, an enemy's weight (by its content name) is multiplied by this.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> Affinity { get; init; } = new Dictionary<string, IReadOnlyDictionary<string, double>>();
    /// <summary>Tags under which nothing spawns (the grace stretch, the terminus's safety).</summary>
    public IReadOnlyList<string> SpawnBans { get; init; } = [];
    /// <summary>§15.4: the director holds its own spikes while the terrain pressure is at or over this.</summary>
    public double PressureCeiling { get; init; } = double.PositiveInfinity;

    /// <summary>The terrain pressure at a main-line distance.</summary>
    public double PressureAt(double s) => Pressure.Values.Count == 0 ? 0 : Pressure.Values[Math.Clamp((int)(s / Pressure.StepM), 0, Pressure.Values.Count - 1)];

    /// <summary>The tags over a main-line distance.</summary>
    public IEnumerable<string> TagsAt(double s) => Tags.Where(t => t.Edge == "main" && s >= t.S0 && s <= t.S1).Select(t => t.Tag);
}

/// <summary>A line of paperwork (§9.7). Km as counted from the outer gate.</summary>
public sealed record CardLine(string Kind, double Km, string Text, double? KmTo = null);
public sealed record KnownGrade(string Route, double RulingPct, double LengthKm, bool Passable, string Note);
public sealed record PlanRouteCard(string Title, IReadOnlyList<CardLine> Timetable, IReadOnlyList<CardLine> Form19, IReadOnlyList<KnownGrade> KnownGrades,
    double DawnS, double LineSpeedMs);

public sealed record PlanMarker(string Type, string Edge, double S);

public sealed record PlanFortress(string Name, string Identity, double DepartureRoadM, double InnerGateM, double OuterGateM, int ThroatSwitches,
    IReadOnlyList<double[]> Lights);
public sealed record PlanTerminus(string Name, bool Silent, bool GateSafe, double GateM, double SkyGlowFromM, double HomeStraightFromM, IReadOnlyList<double[]> Lights);

/// <summary>
/// §8.5 and §9.2's lethal checks and hazards as the sim applies them, and the terrain rules the height field is built
/// with: part of the plan so that a plan (and a save of one) plays the same whatever content changes after.
/// </summary>
public sealed record PlanRules(double ADerail, double BrassCuttingSpeed, double BrassDamagePerSpeedSquared, double BrassDrag, bool WeakBridgeCollapses,
    double WetAdhesion, double WetBiasAdhesion, TerrainRules Terrain)
{
    /// <summary>Plan §8.5's posting acceleration (tiers.json curves.aPost): a bend is boarded at floor(√(aPost R)) (note 265's stress starts there).</summary>
    public double APost { get; init; } = 0.7;
    /// <summary>How far out from the track the boards stand (signage.json sideOffsetM).</summary>
    public double SignOffsetM { get; init; } = 3.2;
    /// <summary>How far either side the fog factor is averaged as the eye sees it (tiers.json weather.fogBlendM; note 313).</summary>
    public double FogBlendM { get; init; }
    /// <summary>Whether the eye's fog follows §14's factor along the line at all (tiers.json weather.fogAlongLine; note 313).</summary>
    public bool FogAlongLine { get; init; }
    /// <summary>The biomes as their dressing reads them (biomes.json): the ground, the trees, the water.</summary>
    public IReadOnlyDictionary<string, BiomeDef> Biomes { get; init; } = new Dictionary<string, BiomeDef>();
    /// <summary>Each biome's landform mix (biomes.json "landform"), by biome.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> BiomeLandforms { get; init; } = new Dictionary<string, IReadOnlyDictionary<string, double>>();
    /// <summary>How rough each biome's land is (biomes.json noiseScale): the terrain's relief is multiplied by it.</summary>
    public IReadOnlyDictionary<string, double> BiomeRelief { get; init; } = new Dictionary<string, double>();
    /// <summary>Each kind of board as the signage kit builds it (signage.json "boards"), by type.</summary>
    public IReadOnlyDictionary<string, BoardDef> Boards { get; init; } = new Dictionary<string, BoardDef>();
}

public sealed record PlanCheck(string Name, bool Pass, string Detail);
public sealed record PlanValidation
{
    public double IdealTransitS { get; init; }
    public double SloppyTransitS { get; init; }
    public double DawnSlackS { get; init; }
    public int Attempts { get; init; }
    public bool Fallback { get; init; }
    public double MinBrakeEfficiency { get; init; } = 1;
    public IReadOnlyList<PlanCheck> Checks { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyDictionary<string, double> Metrics { get; init; } = new SortedDictionary<string, double>();
    public bool Passed => Checks.All(c => c.Pass);
}
