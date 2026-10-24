using Ballast;
using DarkTerritory.Sim.Route;

namespace DarkTerritory.Sim.LineGen;

// Mirrors of content/linegen/*.json (docs/design/linegen-plan.md §19.2). Field docs live in those files, which cite the
// plan's sections; every number is there and none is here. `LineGenConfigTests` holds every property below to a value
// in the files, so a field can't silently come out zero.

/// <summary>Everything the line generator reads from content: the eight linegen files.</summary>
public sealed record LineGenConfig(
    TiersFile Tiers, SetPiecesFile SetPieces, BiomesFile Biomes, FacilitiesFile Facilities, SignageFile Signage, NamesFile Names,
    FallbackSeedsFile FallbackSeeds, FootprintsFile Footprints)
{
    public const string Directory = "linegen";
    public static readonly string[] Files = ["tiers.json", "setpieces.json", "biomes.json", "facilities.json", "signage.json", "names.json", "fallback_seeds.json", "footprints.json"];

    public static LineGenConfig Load(string content)
    {
        string P(string f) => Path.Combine(content, Directory, f);
        return new LineGenConfig(
            DataFile.Load<TiersFile>(P("tiers.json")), DataFile.Load<SetPiecesFile>(P("setpieces.json")), DataFile.Load<BiomesFile>(P("biomes.json")),
            DataFile.Load<FacilitiesFile>(P("facilities.json")), DataFile.Load<SignageFile>(P("signage.json")), DataFile.Load<NamesFile>(P("names.json")),
            DataFile.Load<FallbackSeedsFile>(P("fallback_seeds.json")), DataFile.Load<FootprintsFile>(P("footprints.json")));
    }
}

// ---------------------------------------------------------------- tiers.json

public sealed record TiersFile
{
    public string Version { get; init; } = "";
    /// <summary>Which generator the game's nights come from: "linegen", or "legacy" (the prototype's RouteGenerator).</summary>
    public string Routes { get; init; } = "linegen";
    public required TierColumns Columns { get; init; }
    public required BudgetCurve Budget { get; init; }
    public required CurveRules Curves { get; init; }
    public required ReactionRules Reaction { get; init; }
    public required DemoSwitches Demo { get; init; }
    public required ConsistRules Consist { get; init; }
    public required FortressTemplate Fortress { get; init; }
    public required TerminusTemplate Terminus { get; init; }
    public required JunctionRules Junctions { get; init; }
    public required AlternateRules Alternates { get; init; }
    public required DeadLineRules DeadLines { get; init; }
    public required HazardRules Hazards { get; init; }
    public required AlignmentRules Alignment { get; init; }
    public required ProfileRules Profile { get; init; }
    public required AuthorityRules Authority { get; init; }
    public required TerrainRules Terrain { get; init; }
    public required WeatherRules Weather { get; init; }
    public required DirectorRules Director { get; init; }
    public required ValidationRules Validation { get; init; }
    public required ConflictDefaults Conflicts { get; init; }
}

/// <summary>§3.2: each column is the start of a tier; Deep territory lerps toward <see cref="DeepMax"/>.</summary>
public sealed record TierColumns(TierColumn Local, TierColumn Frontier, TierColumn DeadLines, TierColumn DeepTerritory, TierColumn DeepMax)
{
    public TierColumn this[int i] => i switch { 0 => Local, 1 => Frontier, 2 => DeadLines, 3 => DeepTerritory, _ => DeepMax };
}

/// <summary>One column of the §3.2 table. Two-element arrays are [min, max] ranges.</summary>
public sealed record TierColumn
{
    public double Facilities { get; init; }
    public double MainGrade { get; init; }
    /// <summary>0: no momentum banks at this tier.</summary>
    public double MomentumGrade { get; init; }
    public double BranchGrade { get; init; }
    public double MinRadius { get; init; }
    public double MinVerticalRadius { get; init; }
    public double MaxTunnel { get; init; }
    /// <summary>Max grade inside tunnels; 0 for "ruling" (the main-line grade).</summary>
    public double TunnelGrade { get; init; }
    public bool CurvedTunnels { get; init; }
    public double StackCap { get; init; }
    /// <summary>Dead lines' "2 (3 once)": one stack may go one deeper.</summary>
    public bool StackOnceDeeper { get; init; }
    public double RecoveryMin { get; init; }
    public double TellMargin { get; init; }
    public double LineSpeed { get; init; }
    public double Redundancy { get; init; }
    public double SignageSurvival { get; init; }
    public double[] Junctions { get; init; } = [];
    public double[] Alternates { get; init; } = [];
    public double[] DeadLines { get; init; } = [];
    public double[] Washouts { get; init; } = [];
    public double[] WeakBridges { get; init; } = [];
    public double[] MomentumBanks { get; init; } = [];
    public double[] BrassFields { get; init; } = [];
    public double BudgetPerKm { get; init; }
    public double Corruption { get; init; }
    public double[] Fog { get; init; } = [];
    /// <summary>Chance a night rains (§14), and the cold (0..1) it starts at.</summary>
    public double RainChance { get; init; }
    public double[] Cold { get; init; } = [];
    public double[] Wind { get; init; } = [];
    /// <summary>§15.2 / §22.3: Sleeper density.</summary>
    public double SleeperDensity { get; init; }
    /// <summary>Note 278: how many hard bends a night carries (bends that derail the train under its top speed), [min, max].</summary>
    public double[] Bends { get; init; } = [];
    /// <summary>Note 278: the speeds (m/s) a hard bend derails at, [min, max]; its radius v² / aDerail, never under minRadius.</summary>
    public double[] BendDerail { get; init; } = [];
    /// <summary>Note 359: the chance a hard bend is laid as an S-bend, two hard turns either way held as one.</summary>
    public double SBends { get; init; }
}

/// <summary>§3.3 budget curve: zone shares and the reserved stretches.</summary>
public sealed record BudgetCurve(double Opening, double Middle, double FinalApproach, double GraceM, double HomeStraightM, double[] SpikeWindowM,
    double LullBeforeM, double LullAfterM, double RecoveryShare);

/// <summary>§8.5 curve limits and §8.1 transition lengths.</summary>
public sealed record CurveRules(double ADerail, double APost, double TransitionMinM, double TransitionSpeedFactor, double TransitionMaxM)
{
    /// <summary>T111: a curve that derails below this (m/s, the engine's top speed on full steam) is boarded whatever the line speed.</summary>
    public double BoardDerailBelow { get; init; } = 22;
    /// <summary>Note 278: no hard bend this far past a dead line's toe (turning its way, the main line could cross it).</summary>
    public double BendDeadLineClearM { get; init; } = 1500;
}

/// <summary>§9.3 and §16.2.</summary>
public sealed record ReactionRules(double TReactS, double SloppySpeedFactor, double SloppyTReactS, RouteTier SloppyUpToTier);

/// <summary>§3.2 demo configuration: a config switch, not a code branch.</summary>
public sealed record DemoSwitches(bool Enabled, RouteTier MaxTier, bool NoWashouts, bool NoWeakBridges, bool NoMomentumBanks);

/// <summary>§3.4.</summary>
public sealed record ConsistRules(double MainGradeShareOfClimbMax, int MaxCars);

/// <summary>§10: the departure fortress and the threshold. <see cref="PadM"/>: how far either side of the line the yard's
/// ground is level (the walled town's, queue #74, note 335; 70 when the yard was a corridor between two walls).</summary>
public sealed record FortressTemplate(double DepartureRoadExtraM, double ThroatM, double InnerGateBeforeM, double TowersFromM, double KillZoneM,
    double LastLightM, double[] DressingTransitionM, double YardSpeed, int[] ThroatSwitches, string[] Identities, double MaxThresholdGrade,
    double ThresholdMinRadius, double PadM = 70);

/// <summary>§11.4: the terminus and arrival. <see cref="TownYard"/> (note 600): a terminus that answers is a town, the one
/// the next night departs from, so its yard is the departure yard's length and its ground the fortress's pad;
/// <see cref="ArrivalYardM"/> is a silent settlement's (or every terminus's, without it). <see cref="TownYardLimitSpeed"/>
/// (note 1009): that town's yard limit, its streets' speed, not a silent settlement's crawl; null, <see cref="YardLimitSpeed"/>.</summary>
public sealed record TerminusTemplate(double SkyGlowM, double YardLimitBoardM, double SpawnBanM, double WallsResolveM, double ArrivalYardM,
    double YardLimitSpeed, RouteTier SilentFromTier, double HomeStraightMinRadius, bool TownYard = false, double? TownYardLimitSpeed = null);

/// <summary>§6.3.</summary>
public sealed record JunctionRules(double TangentClearM, double MaxGrade, double LampVisibleM, double SeparationM, double SeparationWithinM, double PadM,
    double MinSpacingM, double BoardFarM, double BoardNearM, double TurnoutRadius, double TurnoutLength);

/// <summary>§6.2 step 4.</summary>
public sealed record AlternateRules(double[] WindowKm, Dictionary<string, TradeOff> TradeOffs, double ClosureReserveM, double MinBulgeM, double LengthOverChordMin);

/// <summary>What an alternate trades against the main line it bypasses (§6.2).</summary>
/// <param name="Pieces">Set pieces it must carry (by id), in order of preference.</param>
public sealed record TradeOff(double Weight, double[] LengthRatio, string[] Pieces, bool Level, bool RadioBlackout, string Summary, double MinD);

/// <summary>§6.2 step 7.</summary>
public sealed record DeadLineRules(double[] LengthKm, double WreckYardChance, double[] AngleDeg, double MaxGrade);

/// <summary>§6.2 steps 5–6, §9.6, §22.5.</summary>
public sealed record HazardRules(double[] WeakBridgeSpeed, int[] WeakBridgeCarsBelowPlan, double[] WashoutLengthM, double WashoutAfterJunctionM,
    double BrassCuttingSpeed, double[] BrassLengthM, double BrassRampM, double BrassDamagePerSpeedSquared, double BrassDrag);

/// <summary>§8.1 horizontal alignment.</summary>
public sealed record AlignmentRules(double[] WanderDeg, double WanderMinRadius, double BandDeg, double GuideAmplitudeDeg, double[] GuideWavelengthKm,
    double SeparationM, double SameEdgeGapM, double JunctionExemptM, double GridM, double ClosureTolM, double ClosureTolDeg, int ClosureIterations,
    double ConnectorRadiusFactor, int PieceRetries);

/// <summary>§8.3 vertical profile.</summary>
public sealed record ProfileRules(double DriftAmplitudeM, double[] DriftWavelengthKm, double DriftMaxGrade, double LevelGrade, double PadGrade,
    double HoldingGrade, double ApproachGrade);

/// <summary>§9: speed authority and tells.</summary>
/// <remarks>The Sleepers' thresholds and the lamp's reach are enemies.json's (plan §22.2, §22.6): one source for both.</remarks>
public sealed record AuthorityRules(double SleeperSafeMargin, double LampHeightM,
    double TargetHeightM, double SightStepM, double MinRestrictedSpeed, double[] RestrictedLengthM, double RestrictedPerKm, double RestrictedEmptyShare,
    double BoardBeforeM, double ResumeAfterExtraM, double LimitRoundTo, double MinBoardGapM, double BlindSightM);

/// <summary>§12.</summary>
public sealed record TerrainRules(double CorridorM, double TileM, double GridM, double FormationM, double ShoulderM, double BlendM, double NoiseAmplitudeM,
    double[] NoiseWavelengthM, double JunctionPadM, double SkirtDropM, double SampleM, double WalkableSlope, double WalkableWithinM,
    double TunnelCoverM, double RiverWidthM, double ReliefM, double[] ReliefWavelengthM, double ReliefFromM, double ReliefFullM, double ReliefRidged,
    double ReliefUp, DrumlinRules Drumlins, KnobRules Knobs, PlateauRules Plateau, LakeRules Lakes, ShoreRules Shore, DykeRules Dykes, RiverRules Rivers, RoadRules Roads)
{
    /// <summary>Note 279: a tunnel's bore either side of the rail (StructureKit.TunnelHalf), and its crown over the rail.</summary>
    public double BoreHalfM { get; init; } = 3.1;
    public double BoreCrownM { get; init; } = 7.5;
    /// <summary>Note 279: a bridge's deck either side of the rail.</summary>
    public double DeckHalfM { get; init; } = 2.6;
    /// <summary>Note 279: what runs beside a train in a bore or on a deck keeps this inside the lining or the deck's edge.</summary>
    public double BesideClearM { get; init; } = 0.6;
}

/// <summary>The Maritimes' landforms (TerrainField.Landform): heights as a share of reliefM, lengths in metres.</summary>
public sealed record DrumlinRules(double WavelengthM, double Stretch, double FlowDeg, double Height);
public sealed record KnobRules(double WavelengthM, double Height);
public sealed record PlateauRules(double WavelengthM, double Height, double GorgeWavelengthM, double GorgeWidth, double GorgeDepth);

/// <summary>
/// The Southern Upland's lakes (docs/design/maritime-rules.md §2): shallow, dark, lying along the ice's flow, some
/// beside the line and some crossed on a fill. Radii in metres (the minor semi-axis; stretch lengthens it along the
/// flow); offM: from the track to the near shore, in minor radii; levelBelowRailM under the lowest rail near it.
/// </summary>
public sealed record LakeRules(double[] RadiusM, double[] Stretch, double[] OffM, double CrossChance, double[] CrossRadiusM, double TurnDeg, double DepthM,
    double LevelBelowRailM, double ShoreSlope, double Wobble, double ClearM, double FillSlope, double TrestleChance = 0, double TrestleAbutmentM = 6,
    double TrestleMaxM = 0, double TrestleClearM = 50, double RimM = 0, double RimCrestM = 0, double RimSlope = 0.25);

/// <summary>
/// A shore the line runs along (maritime-rules.md §3): the sea on one side, its edge wandering in coves and headlands
/// (nearM from the track, coveM more at most, over coveWavelengthM); a shingle beach up to the bluff, a cliff where the
/// rail stands more than cliffAboveM over the water; Fundy's mudflats flatM wide at low water; islands offshore.
/// </summary>
public sealed record ShoreRules(double LevelBelowRailM, double[] NearM, double[] CoveM, double[] CoveWavelengthM, double BeachSlope, double CliffAboveM,
    double CliffSlope, double DepthM, double[] FlatM, double TaperM, double MinM, double IslandShare, double IslandWavelengthM, double TidalSpanFactor,
    double TidalDepthFactor, double SeaBelowRailM, double[] SeaNearM, double[] SeaCoveM);

/// <summary>
/// A river the line runs up the valley of (maritime-rules.md §5, §7: the lines kept to the valleys): its near bank bankM
/// out, meandering up to meanderM further, widthM across, its water belowRailM under the rail and depthM deep; the far
/// bank rising at farSlope into the valley side.
/// </summary>
public sealed record RiverRules(double[] BankM, double[] MeanderM, double[] WidthM, double BelowRailM, double DepthM, double FarSlope);

/// <summary>
/// The country road beside the line (maritime-rules.md §2.2): a gravel road offsetM out, wandering by wanderM over
/// wanderWavelengthM, that crosses the line at grade every crossEveryM or so (over rampM either side), its bed
/// halfWidthM each side of its centre with banks at bankSlope; pieces shorter than minM aren't laid, and it keeps
/// clearM from structures, cuttings and junctions.
/// </summary>
public sealed record RoadRules(double[] OffsetM, double[] WanderM, double[] WanderWavelengthM, double[] CrossEveryM, double RampM, double HalfWidthM,
    double BankSlope, double MinM, double ClearM);

/// <summary>Acadian dykeland (maritime-rules.md §4): hay fields dead flat under the rail, a dyke along the shore, salt marsh and mud past it.</summary>
public sealed record DykeRules(double FieldsBelowRailM, double HeightM, double CrestM, double SideSlope, double[] OutM, double MarshAboveWaterM, double LandwardM,
    double MaxRailRangeM);

/// <summary>§14.</summary>
public sealed record WeatherRules(double FogLowGround, double FogCrest, double WindExposed, double WetAdhesion,
    double WetBiasAdhesion, double FogDensityPerInverseMetre, double CrestLengthM, double FogWater = 1, double FogWaterM = 0, double FogBlendM = 0, bool FogAlongLine = false);

/// <summary>§15.</summary>
public sealed record DirectorRules(double[] PreGradeM, double TunnelExitM, double NearFacilityM, double GreaseWetCold, double GreasePerKm,
    double[] GreaseLengthM, double SleeperZonesPerKm, double SleeperLengthM, Dictionary<string, QuotaRow> Quotas, double PressureStepM,
    double DeadSettlementNearFacilityM, Dictionary<string, Dictionary<string, double>> Affinity, string[] SpawnBans, double PressureCeiling);

/// <summary>§15.3: affordances a run must contain, by tier (each tier inherits the ones before it).</summary>
public sealed record QuotaRow(Dictionary<string, int> Tags, int FacingJunctions, int DeadLines, int Tunnels, int DeadSettlementNearFacility);

/// <summary>§16.</summary>
public sealed record ValidationRules(int Attempts, double MinBrakeEfficiency, double DawnMinutesPerFacility, double CoalingEnduranceShare, double MaxSeconds,
    double StallRollbackExtraM, double DriverBandMs, bool DawnWithStopsHard, double OverspeedTolerance, int MaxParallelDrives);

/// <summary>§22: the config defaults for the conflicts found in the source documents, each logged when it applies.</summary>
public sealed record ConflictDefaults(bool DawnFromSpecFormula, bool SilentGateSafe, bool WeakBridgeCollapses,
    bool WaterStops, bool HaltScavenging, bool RockfallsOnLedges, bool CouplerBreaksOnRollers);

// ---------------------------------------------------------------- setpieces.json

public sealed record SetPiecesFile(IReadOnlyList<PieceDef> Pieces, IReadOnlyList<SignatureDef> Signatures);

/// <summary>A set piece (§7.2). Its kind picks the code that realises it; everything it's realised from is here.</summary>
/// <param name="MinD">The difficulty it's allowed from. <paramref name="FullD"/>: where it reaches its full form (the Drop).</param>
/// <param name="Weight">How often it's picked among what fits, before the biome's bias (biomes.json).</param>
public sealed record PieceDef(string Id, string Kind, double MinD, double FullD, double[] LengthM, double Cost, double CostScale, bool Crunch,
    string[] Tags, Dictionary<string, double[]> Params, double Weight)
{
    public double Param(string key, int i = 0) => Params.TryGetValue(key, out var v) && v.Length > i ? v[i] : 0;
    public double[] Range(string key) => Params.TryGetValue(key, out var v) ? v : [0, 0];
}

/// <summary>A signature stack (§7.4): pieces laid one after another, each overlapping the last's demand window.</summary>
public sealed record SignatureDef(string Id, double MinD, string[] Chain, bool AfterFacility, bool OnDescent, double Weight);

// ---------------------------------------------------------------- biomes.json

public sealed record BiomesFile(Dictionary<string, BiomeDef> Biomes, Dictionary<string, Dictionary<string, double>> TierWeights, double[] RegionKm,
    double TransitionM, Dictionary<string, double> CorruptionNear, ScatterRules Scatter);

/// <summary>A biome (§13.1): what grows, what the ground is, how rough, and which set pieces it favours (§7.5).</summary>
/// <param name="CorruptedTrees">The share of its trees the Corruption has (note 696; the director, 13 Oct 2026: about three
/// in ten of all the trees), living or dead.</param>
/// <param name="CorruptedKinds">Which of the corrupted trees' kinds grow here, by weight (the art's alone: CorruptedTreeArt).</param>
/// <param name="DeepWoods">Its big forests (note 1006): how much of its line runs through deep woods and how they stand (the DeepWoods); none where null.</param>
public sealed record BiomeDef(string Name, string Ground, string[] Materials, double NoiseScale, double TreeDensity, string[] Trees, double DeadTrees,
    double Water, Dictionary<string, double> Pieces, Dictionary<string, double> Flora, double Rocks, string Verge, Dictionary<string, PropRule> Props,
    Dictionary<string, double> Landform, double LakesPerKm, double Shore, string ShoreKind, double SweepChance, bool TidalRivers, double Roads, double[] Colour,
    string? Ballast = null, double[]? BallastTint = null, double CorruptedTrees = 0, Dictionary<string, double>? CorruptedKinds = null, DeepWoodsRule? DeepWoods = null);

/// <summary>
/// A biome's big forests (note 1006; the director, 23 Oct 2026): the <paramref name="Share"/> of its main line laid as deep
/// woods, in stretches <paramref name="LengthM"/> long, eased in over <paramref name="EdgeM"/> at each end. In them the stands
/// cover <paramref name="Cover"/> of the land (LinesideProps.Stand), the first trunks <paramref name="NearM"/> from the line's
/// middle, <paramref name="ExtraTries"/> more tries a 12 m slot at a tree, within <paramref name="WallM"/> of the line, the trees grown as tall as the
/// thickest forest's, and the forest floor and the trees darkened by <paramref name="Shade"/> (the art's: under the canopy).
/// None within <paramref name="GatesM"/> of either town's gate.
/// </summary>
public sealed record DeepWoodsRule(double Share, double[] LengthM, double EdgeM, double Cover, double NearM, int ExtraTries, double WallM, double Shade, double GatesM);

/// <summary>A dressing piece a biome stands along the line: per 150 m, its chance, how far out, how many.</summary>
public sealed record PropRule(double Chance, double[] OutM, int Count);

/// <summary>§13.2 lineside scatter bands.</summary>
public sealed record ScatterRules(double TrackKitM, double LinesideM, double PoleOffsetM, double PoleEveryM, double LowVegetationM, double TreesFromM,
    double BrokenPolesAtCorruption, double CellM);

// ---------------------------------------------------------------- footprints.json

/// <summary>
/// What each kit piece the lineside deals stands on (ARCHITECTURE §8 note 389), measured off the art's meshes
/// (<c>dt art footprints --write</c>): by piece, each variant's boxes as [centre x, centre z, half x, half z, turn, top] in
/// the piece's own frame. A piece with no boxes is walked through.
/// </summary>
public sealed record FootprintsFile(string Version, Dictionary<string, double[][][]> Pieces);

// ---------------------------------------------------------------- facilities.json

public sealed record FacilitiesFile(FacilitySlotRules Slots, Dictionary<string, FacilityDef> Types, PoiScale Scale, double[] PowerBiasByTier);

/// <summary>§6.2 step 3 and §11.1.</summary>
public sealed record FacilitySlotRules(double FirstMinM, double FirstMinShare, double SpacingM, double LastBeforeEndM, double ApproachM,
    double ApproachMinRadius, double HoldingExtraM, double HoldingGrade, double DepartureM, double BoardFarM, double BoardNearM, double[] SpurLengthM,
    double[] SpurGrade, double PickupFromTrackM);

/// <summary>A facility type (§11.1): the biomes it belongs in, its terrain intent, and whether it's on a spur.</summary>
public sealed record FacilityDef(FacilityKind Kind, RouteTier FromTier, Dictionary<string, double> Biomes, string Intent, bool Spur, bool MinePortal,
    string[] Owners, string Type);

/// <summary>Spec D.1 scale: pad radius, compact to sprawling.</summary>
public sealed record PoiScale(double[] PadRadiusM, double[] SpurScale);

// ---------------------------------------------------------------- signage.json

public sealed record SignageFile(Dictionary<string, BoardDef> Boards, double KmPostEveryM, double MinorPostEveryM, double SideOffsetM,
    Dictionary<string, double> StateWeights, double RepeatBoardGapM, double WhistleBeforeM, double GradientPostMinShare)
{
    /// <summary>How the lettered boards stand, read and shine (signage.json "billboard", note 560).</summary>
    public BillboardRules Billboard { get; init; } = new();
    /// <summary>
    /// How far past a stop a junction's or facility's far board stands, when the stop comes between it and what it counts to
    /// (note 702: "J1, one kilometer ... came before the Talbot Foundry stop though, so that's a bit confusing").
    /// </summary>
    public double PastStopM { get; init; } = 300;
}

/// <summary>
/// A kind of lineside board (§9.5, §18 signage kit). <see cref="HeightM"/>: a tunnel plate's top on its portal, a board's
/// bottom edge over the rail; <see cref="WidthM"/> its least width (a plate's most). Lettered <see cref="LetterM"/> tall, at
/// most <see cref="LineChars"/> to a line (note 560).
/// </summary>
public sealed record BoardDef(string Text, double HeightM, double WidthM, double VisibleM, bool Reflective)
{
    public double LetterM { get; init; } = 1;
    public int LineChars { get; init; } = 9;
    /// <summary>It warns of what's ahead (a limit, a closed line): moved to be seen, it's only ever moved back toward the train.</summary>
    public bool Warns { get; init; }
    /// <summary>
    /// The side it stands on, if not the billboards' (1 the right, as the train coming sees it): the km posts by the poles,
    /// low, out of the billboards' way (`pt13-signs`). 0: billboard.side.
    /// </summary>
    public int Side { get; init; }
    /// <summary>It stands where the plan puts it (a km post marks its km): never moved, and the billboards keep clear of it instead.</summary>
    public bool Fixed { get; init; }
    /// <summary>
    /// It stands on the side its sign gives, whatever billboard.side, and never moved across the line: a mail board on its
    /// crane's side, so the board and its arrow say the same side (the director's evening playtest, line 10: "I have a mail
    /// sign here that's pointing in the wrong direction"; note 702).
    /// </summary>
    public bool SignSide { get; init; }
    /// <summary>How far its bottom stands over the land at its foot at least (m), if not billboard.overGroundM (0): a km post low, under the eye line.</summary>
    public double OverGroundM { get; init; }
}

/// <summary>Mirror of signage.json's <c>billboard</c> (note 560): field docs live in that file.</summary>
public sealed record BillboardRules
{
    public int Side { get; init; } = -1;
    public double StackM { get; init; } = 60;
    public double StackGapM { get; init; } = 0.5;
    public double StackMaxM { get; init; } = 24;
    public double PostClearM { get; init; } = 20;
    public double InnerM { get; init; } = 4.5;
    public double PostsFromM { get; init; } = 6.5;
    public double OverGroundM { get; init; } = 2.5;
    public double TowardDeg { get; init; } = 10;
    public double MarginLetters { get; init; } = 0.3;
    public double EyeM { get; init; } = 3.2;
    public double EyeAcrossM { get; init; } = 1.1;
    public IReadOnlyList<double> CabBlindDeg { get; init; } = [8.6, 14.2, -35.3, -32];
    public double ClearFromM { get; init; } = 250;
    /// <summary>How far from the track a board stands back where it can't be read by the line on either side (note 693): past a dead line alongside.</summary>
    public double BackInnerM { get; init; } = 18;
    /// <summary>A through truss's sides as the sight test has them (note 686; StructureKit.TrussSpan's): off the centreline, and from under the rail to over it.</summary>
    public double TrussHalfM { get; init; } = 2.7;
    public double TrussLowM { get; init; } = -0.55;
    public double TrussHighM { get; init; } = 7.0;
    public double ReadToM { get; init; } = 40;
    public double SightStepM { get; init; } = 15;
    public double SeenShare { get; init; } = 0.75;
    public double LampConeDeg { get; init; } = 18;
    public double StepBackM { get; init; } = 15;
    public double MaxBackM { get; init; } = 150;
    public double ClearOfM { get; init; } = 1;
    public double CrownShare { get; init; } = 0.2;
    public double DrawM { get; init; } = 400;
    public double Emissive { get; init; } = 0.9;
    public double PlainShare { get; init; } = 0.3;
    public double DarkGlow { get; init; } = 0.06;
    public double LitGlow { get; init; } = 0.8;
    public double FarGlow { get; init; } = 2.0;
    public double GlowConeDeg { get; init; } = 24;
}

// ---------------------------------------------------------------- names.json

public sealed record NamesFile(string[] Surnames, string[] Features, string[] Owners, Dictionary<string, string[]> Patterns, string[] HaltSuffixes,
    string[] TunnelWords, string[] BridgeKinds);

// ---------------------------------------------------------------- fallback_seeds.json

/// <summary>§16.4: per tier, per consist band ("3-5"), seeds proven to pass.</summary>
public sealed record FallbackSeedsFile(Dictionary<string, Dictionary<string, ulong[]>> Seeds);
