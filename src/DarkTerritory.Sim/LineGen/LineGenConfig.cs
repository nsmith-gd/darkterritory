using Ballast;
using DarkTerritory.Sim.Route;

namespace DarkTerritory.Sim.LineGen;

// Mirrors of content/linegen/*.json (docs/design/linegen-plan.md §19.2). Field docs live in those files, which cite the
// plan's sections; every number is there and none is here. `LineGenConfigTests` holds every property below to a value
// in the files, so a field can't silently come out zero.

/// <summary>Everything the line generator reads from content: the seven linegen files.</summary>
public sealed record LineGenConfig(
    TiersFile Tiers, SetPiecesFile SetPieces, BiomesFile Biomes, FacilitiesFile Facilities, SignageFile Signage, NamesFile Names,
    FallbackSeedsFile FallbackSeeds)
{
    public const string Directory = "linegen";
    public static readonly string[] Files = ["tiers.json", "setpieces.json", "biomes.json", "facilities.json", "signage.json", "names.json", "fallback_seeds.json"];

    public static LineGenConfig Load(string content)
    {
        string P(string f) => Path.Combine(content, Directory, f);
        return new LineGenConfig(
            DataFile.Load<TiersFile>(P("tiers.json")), DataFile.Load<SetPiecesFile>(P("setpieces.json")), DataFile.Load<BiomesFile>(P("biomes.json")),
            DataFile.Load<FacilitiesFile>(P("facilities.json")), DataFile.Load<SignageFile>(P("signage.json")), DataFile.Load<NamesFile>(P("names.json")),
            DataFile.Load<FallbackSeedsFile>(P("fallback_seeds.json")));
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
    public double LengthKm { get; init; }
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
}

/// <summary>§3.3 budget curve: zone shares and the reserved stretches.</summary>
public sealed record BudgetCurve(double Opening, double Middle, double FinalApproach, double GraceM, double HomeStraightM, double[] SpikeWindowM,
    double LullBeforeM, double LullAfterM, double RecoveryShare);

/// <summary>§8.5 curve limits and §8.1 transition lengths.</summary>
public sealed record CurveRules(double ADerail, double APost, double TransitionMinM, double TransitionSpeedFactor, double TransitionMaxM);

/// <summary>§9.3 and §16.2.</summary>
public sealed record ReactionRules(double TReactS, double SloppySpeedFactor, double SloppyTReactS, RouteTier SloppyUpToTier);

/// <summary>§3.2 demo configuration: a config switch, not a code branch.</summary>
public sealed record DemoSwitches(bool Enabled, RouteTier MaxTier, bool NoWashouts, bool NoWeakBridges, bool NoMomentumBanks);

/// <summary>§3.4.</summary>
public sealed record ConsistRules(double MainGradeShareOfClimbMax, int MaxCars);

/// <summary>§10: the departure fortress and the threshold.</summary>
public sealed record FortressTemplate(double DepartureRoadExtraM, double ThroatM, double InnerGateBeforeM, double TowersFromM, double KillZoneM,
    double LastLightM, double[] DressingTransitionM, double YardSpeed, int[] ThroatSwitches, string[] Identities, double MaxThresholdGrade,
    double ThresholdMinRadius);

/// <summary>§11.4: the terminus and arrival.</summary>
public sealed record TerminusTemplate(double SkyGlowM, double YardLimitBoardM, double SpawnBanM, double WallsResolveM, double ArrivalYardM,
    double YardLimitSpeed, RouteTier SilentFromTier, double HomeStraightMinRadius);

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
    double ReliefUp);

/// <summary>§14.</summary>
public sealed record WeatherRules(double FogLowGround, double FogCrest, double WindExposed, double ColdStepPerM, double ColdExposedStep, double WetAdhesion,
    double WetBiasAdhesion, double FogDensityPerInverseMetre, double CrestLengthM);

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
public sealed record ConflictDefaults(bool DawnFromSpecFormula, double DawnAverageSpeed, double DawnSlack, bool SilentGateSafe, bool WeakBridgeCollapses,
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
public sealed record BiomeDef(string Name, string Ground, string[] Materials, double NoiseScale, double TreeDensity, string[] Trees, double DeadTrees,
    double Water, Dictionary<string, double> Pieces, Dictionary<string, double> Flora, double Rocks, string Verge, Dictionary<string, PropRule> Props,
    double[] Colour);

/// <summary>A dressing piece a biome stands along the line: per 150 m, its chance, how far out, how many.</summary>
public sealed record PropRule(double Chance, double[] OutM, int Count);

/// <summary>§13.2 lineside scatter bands.</summary>
public sealed record ScatterRules(double TrackKitM, double LinesideM, double PoleOffsetM, double PoleEveryM, double LowVegetationM, double TreesFromM,
    double BrokenPolesAtCorruption, double CellM);

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
    Dictionary<string, double> StateWeights, double RepeatBoardGapM, double WhistleBeforeM, double GradientPostMinShare);

/// <summary>A kind of lineside board (§9.5, §18 signage kit).</summary>
public sealed record BoardDef(string Text, double HeightM, double WidthM, double VisibleM, bool Reflective);

// ---------------------------------------------------------------- names.json

public sealed record NamesFile(string[] Surnames, string[] Features, string[] Owners, Dictionary<string, string[]> Patterns, string[] HaltSuffixes,
    string[] TunnelWords, string[] BridgeKinds);

// ---------------------------------------------------------------- fallback_seeds.json

/// <summary>§16.4: per tier, per consist band ("3-5"), seeds proven to pass.</summary>
public sealed record FallbackSeedsFile(Dictionary<string, Dictionary<string, ulong[]>> Seeds);
