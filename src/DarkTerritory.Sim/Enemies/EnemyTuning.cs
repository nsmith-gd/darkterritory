namespace DarkTerritory.Sim.Enemies;

/// <summary>Mirror of content/tuning/enemies.json. Field docs live in that file.</summary>
public sealed record EnemyTuning(
    double MinReactionSeconds, SleeperTuning Sleepers, HoundTuning CinderHounds, ClingerTuning Clingers,
    HollowTuning Hollow, ChoirSwarmTuning ChoirSwarm, DirectorTuning Director, double InterestRadius)
{
    public const string File = "tuning/enemies.json";

    public SwitchmanTuning Switchman { get; init; } = new();
    public SootChildrenTuning SootChildren { get; init; } = new();
    public DraggerTuning Draggers { get; init; } = new();
    public RattleTuning Rattle { get; init; } = new();
}

/// <summary>The Rattle (App. A.5, B.5). Field docs live in enemies.json.</summary>
public sealed record RattleTuning
{
    public double ArmRadius { get; init; } = 6;
    public double QuietSeconds { get; init; } = 10;
    public int GrabDamage { get; init; } = 200;
    public double LingerSeconds { get; init; } = 300;
    public int MinCars { get; init; } = 2;
    public double PerCarWeight { get; init; } = 0.25;
}

/// <summary>The Draggers (App. A.4, B.4, spec B.3). Field docs live in enemies.json.</summary>
public sealed record DraggerTuning
{
    public double GrabRange { get; init; } = 1.0;
    public double FastFrom { get; init; } = 14;
    public double FastAt { get; init; } = 22;
    public double FastGrabScale { get; init; } = 1.5;
    public double ReachAlong { get; init; } = 1.5;
    public double CreepSpeed { get; init; } = 1.2;
    public double TelegraphSeconds { get; init; } = 1.0;
    public double AllyRadius { get; init; } = 4;
    public double FreeSeconds { get; init; } = 2;
    public double FreeReach { get; init; } = 1.5;
    public double AloneSeconds { get; init; } = 0.4;
    public double PullSpeed { get; init; } = 3;
    public double RearmSeconds { get; init; } = 8;
    public int MinCars { get; init; } = 2;
    public int MaxAttached { get; init; } = 2;

    /// <summary>The grab range at a train speed (spec B.3: +50% at max).</summary>
    public double GrabAt(double speed) => GrabRange * (1 + (FastGrabScale - 1) * Math.Clamp((speed - FastFrom) / Math.Max(1e-6, FastAt - FastFrom), 0, 1));
}

/// <summary>The Soot Children (App. A.4, B.6). Field docs live in enemies.json.</summary>
public sealed record SootChildrenTuning
{
    public double StandOff { get; init; } = 6;
    public double CallRadius { get; init; } = 40;
    public double CallSeconds { get; init; } = 2.5;
    public double CallEverySeconds { get; init; } = 7;
    public double LureRadius { get; init; } = 7;
    public double IgnoredSeconds { get; init; } = 30;
    public double RecentVoiceSeconds { get; init; } = 120;
    public int MinCrew { get; init; } = 2;
    public double NearFacility { get; init; } = 250;
    public int TakeDamage { get; init; } = 200;
}

/// <summary>The Switchman (App. A.7, B.7). Field docs live in enemies.json.</summary>
public sealed record SwitchmanTuning
{
    public double[] Ahead { get; init; } = [500, 1200];
    public double StandBeside { get; init; } = 1.2;
    public double FleeRadius { get; init; } = 25;
    public double LingerSeconds { get; init; } = 20;
    public int MinJunctions { get; init; } = 3;
}

public sealed record SleeperTuning(double LampRevealDistance, double BraceDistance, double DerailAbove, double HeavyDamageAbove, double HeavyDamage, double MinorDamage);

public sealed record HoundTuning(int[] PackSize, double Health, double Radius, double MaxSpeed, double ClosingSpeed, double SpawnBehind,
    double HowlSeconds, double LeapDistance, int BiteDamage, double BiteEverySeconds, double Reach, double BoredSeconds, double MinTrainSpeed,
    int SuppressRounds, double SuppressWindowSeconds);

public sealed record ClingerTuning(double DrillSeconds, double PrySeconds, double PryReach, double BreachCargoLossPerSecond);

public sealed record HollowTuning(double LowFireSeconds, double DescendSeconds, int BiteDamage, double BiteEverySeconds, double RetreatFireFraction);

public sealed record ChoirSwarmTuning(int ExposedDamage, double EverySeconds);

public sealed record DirectorTuning(
    Dictionary<string, double> BaseBudget, double LengthPerCarBeyondThird, double CrewBase, double CrewPerPlayer, double CrewCap,
    double GraceSeconds, double FacilityLullSeconds, double[] CooldownSeconds, int MaxConcurrentZone,
    int MaxConcurrentSmallCrew, int MaxConcurrentLargeCrew, double[] PhaseShares, Dictionary<string, double> Costs,
    double HoundsLivestockWeight, double HoundsHotBoilerWeight);
