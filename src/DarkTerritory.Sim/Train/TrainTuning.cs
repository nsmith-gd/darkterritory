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

    public const string File = "tuning/train.json";
}

public sealed record GeometryTuning(
    double CarLength, double CouplingGap, double EngineLength, double RoofWidth, double RoofSafeCentreline,
    double CarHeight, double EngineHeight, double CouplerHeight, double CouplerWidth, double LadderInset,
    EngineLayout Engine);

/// <summary>Greybox layout of the 20 m engine + tender unit, front to back: boiler, cab, tender.</summary>
public sealed record EngineLayout(double DeckHeight, double BoilerHalfWidth, double BoilerTop, double CabLength, double TenderLength, double TenderTop, double DoorWidth);
public sealed record MassTuning(double EngineTonnes, double EmptyCarTonnes, double LoadedCarTonnes);
public sealed record PerformanceRow(int Cars, double Accel, double Brake);
public sealed record BrakeFadeTuning(double FadePerSecond, double RecoverPerSecond, double MinEfficiency, bool OnlyOnDescent);
/// <summary>Coupling, cutting and collision between rakes. Field docs live in train.json.</summary>
public sealed record CouplingTuning(double CoupleMaxSpeed, double SafeContactSpeed, double DamagePerSpeedSquared, double CargoDamageShare,
    double UncoupleSeconds, double UncoupleUnderLoadSeconds, double HandbrakeDecel, double ParkBelowSpeed, double HandbrakeSeconds);

/// <summary>Deceleration from rolling (m/s²) and air (per (m/s)²) resistance.</summary>
public sealed record ResistanceTuning(double Rolling, double Air);
public sealed record SpeedBandTuning(double Yard, double JumpOffLethal, double Slow, double WorkingMin, double Cruise);
