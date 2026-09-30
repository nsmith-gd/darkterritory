namespace DarkTerritory.Sim.Train;

/// <summary>Mirror of content/tuning/train.json. Field docs live in that file.</summary>
public sealed record TrainTuning
{
    public double MaxSpeed { get; init; }
    public double PlayerRunSpeed { get; init; }
    public double Gravity { get; init; } = 9.81;
    public required GeometryTuning Geometry { get; init; }
    public required MassTuning Mass { get; init; }
    public required IReadOnlyList<PerformanceRow> Performance { get; init; }
    public required BrakeFadeTuning BrakeFade { get; init; }
    public required SpeedBandTuning SpeedBands { get; init; }
    public required ResistanceTuning Resistance { get; init; }
    public required CouplingTuning Couplings { get; init; }
    /// <summary>What the train carries from the fortress (T41). Unset, nothing.</summary>
    public KitTuning Kit { get; init; } = new();

    public const string File = "tuning/train.json";
}

/// <summary>The train's kit from the fortress (train.json <c>kit</c>).</summary>
public sealed record KitTuning
{
    public int Radios { get; init; }
    /// <summary>Toys in the guard van (GDD v1.1 App. C.4): hand loot, what the Track Doll will leave for.</summary>
    public int Toys { get; init; }
}

public sealed record GeometryTuning(
    double CarLength, double CouplingGap, double EngineLength, double RoofWidth, double RoofSafeCentreline,
    double CarHeight, double EngineHeight, double CouplerHeight, double CouplerWidth, double LadderInset,
    EngineLayout Engine, InteriorLayout? Interior = null)
{
    /// <summary>How far the guard van's rear platform stands out behind it (GDD §24 THE WEIGHT's "rear platform").</summary>
    public double PlatformDepth { get; init; } = 1.2;
}

/// <summary>Walk-in cars (GDD §10, §26): a floor, walls, a roof you can still walk on, and a door at each end.</summary>
public sealed record InteriorLayout(double FloorHeight, double WallThickness, double RoofThickness, double DoorWidth, double DoorHeight, double DoorX,
    double DoorSeconds, double CargoDepth, double CargoHeight)
{
    /// <summary>A cargo car's sliding side doors (one each side, in the middle), for loading from the ground (spec D.2).</summary>
    public double SideDoorWidth { get; init; } = 1.8;
    /// <summary>The steps up to each side door: how far out from the car side, and how long each tread is.</summary>
    public double StepWidth { get; init; } = 0.6;
    public double StepDepth { get; init; } = 0.4;
}

/// <summary>Greybox layout of the 20 m engine + tender unit, front to back: boiler, cab, tender.</summary>
public sealed record EngineLayout(double DeckHeight, double BoilerHalfWidth, double BoilerTop, double CabLength, double TenderLength, double TenderTop, double DoorWidth)
{
    /// <summary>The running boards: how far out past the cab side they stand (App. A.2 GREASE: "sanding from the running boards").</summary>
    public double RunningBoardWidth { get; init; } = 0.6;
    /// <summary>The sandboxes on the running boards: this far ahead of the cab front.</summary>
    public double SandboxAhead { get; init; } = 2.5;
}
public sealed record MassTuning(double EngineTonnes, double EmptyCarTonnes, double LoadedCarTonnes);
public sealed record PerformanceRow(int Cars, double Accel, double Brake);
public sealed record BrakeFadeTuning(double FadePerSecond, double RecoverPerSecond, double MinEfficiency, bool OnlyOnDescent);
/// <summary>Coupling, cutting and collision between rakes. Field docs live in train.json.</summary>
public sealed record CouplingTuning(double CoupleMaxSpeed, double SafeContactSpeed, double DamagePerSpeedSquared, double CargoDamageShare,
    double UncoupleSeconds, double UncoupleUnderLoadSeconds, double HandbrakeDecel, double ParkBelowSpeed, double HandbrakeSeconds);

/// <summary>Deceleration from rolling (m/s²) and air (per (m/s)²) resistance.</summary>
public sealed record ResistanceTuning(double Rolling, double Air);
public sealed record SpeedBandTuning(double Yard, double JumpOffLethal, double Slow, double WorkingMin, double Cruise);
