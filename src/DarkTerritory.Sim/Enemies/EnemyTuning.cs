namespace DarkTerritory.Sim.Enemies;

/// <summary>Mirror of content/tuning/enemies.json. Field docs live in that file.</summary>
public sealed record EnemyTuning(
    double MinReactionSeconds, SleeperTuning Sleepers, HoundTuning CinderHounds, ClingerTuning Clingers,
    HollowTuning Hollow, ChoirSwarmTuning ChoirSwarm, DirectorTuning Director, double InterestRadius)
{
    public const string File = "tuning/enemies.json";

    public SwitchmanTuning Switchman { get; init; } = new();
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
