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
    public required IReadOnlyList<CoalBurnRow> CoalBurn { get; init; }
    public int TenderCapacity { get; init; }
    public required SpeedBandTuning SpeedBands { get; init; }

    public const string File = "tuning/train.json";
}

public sealed record GeometryTuning(
    double CarLength, double CouplingGap, double EngineLength, double RoofWidth, double RoofSafeCentreline,
    double CarHeight, double EngineHeight, double CouplerHeight, double CouplerWidth, double LadderInset);
public sealed record MassTuning(double EngineTonnes, double EmptyCarTonnes, double LoadedCarTonnes);
public sealed record PerformanceRow(int Cars, double Accel, double Brake);
public sealed record CoalBurnRow(int Cars, double SecondsPerUnit);
public sealed record BrakeFadeTuning(double FadePerSecond, double RecoverPerSecond, double MinEfficiency, bool OnlyOnDescent);
public sealed record SpeedBandTuning(double Yard, double JumpOffLethal, double Slow, double WorkingMin, double Cruise);
