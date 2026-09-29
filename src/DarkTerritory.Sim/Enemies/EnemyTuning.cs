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
    public LamplighterTuning Lamplighters { get; init; } = new();
    public DeadmanTuning Deadman { get; init; } = new();
    public StokerTuning Stoker { get; init; } = new();
    public FerrymanTuning Ferryman { get; init; } = new();
    public LongWhistleTuning LongWhistle { get; init; } = new();
    public ClimberTuning Climbers { get; init; } = new();
    public WeightTuning Weight { get; init; } = new();
    public GauntTuning Gaunt { get; init; } = new();
}

/// <summary>The Gaunt (App. A.4, B.4). Field docs live in enemies.json.</summary>
public sealed record GauntTuning
{
    public double ViewHalfAngleDegrees { get; init; } = 35;
    public double ViewRange { get; init; } = 60;
    public double EyeHeight { get; init; } = 1.6;
    public double AdvanceSpeed { get; init; } = 5;
    public double Reach { get; init; } = 1.3;
    public int StrikeDamage { get; init; } = 100;
    public double RetreatSeconds { get; init; } = 60;
    public double LingerSeconds { get; init; } = 300;
    public int MinCrew { get; init; } = 3;
    public double TunnelExitWithin { get; init; } = 150;
    public double StoppedBelow { get; init; } = 0.5;
    public double InteriorSeconds { get; init; } = 180;
    public double InteriorWeight { get; init; } = 2;
}

/// <summary>The Weight (App. A.3, B.3). Field docs live in enemies.json.</summary>
public sealed record WeightTuning
{
    public double AheadMin { get; init; } = 200;
    public double AheadMax { get; init; } = 1200;
    public double IntoCrossing { get; init; } = 8;
    public double MaxGradePercent { get; init; } = 1;
    public double DragFactor { get; init; } = 1.25;
    public double StoppedBelow { get; init; } = 0.3;
    public double MeleeReach { get; init; } = 2.2;
    public int BlowsToRelease { get; init; } = 5;
    public int TearOffDamage { get; init; } = 60;
    public double LingerSeconds { get; init; } = 300;
    public double LingerPast { get; init; } = 50;
    public int MinCars { get; init; } = 2;
    public double LowSpeed { get; init; } = 10;
    public double LowSpeedWeight { get; init; } = 2;
}

/// <summary>Climbers (App. A.4, B.4). Field docs live in enemies.json.</summary>
public sealed record ClimberTuning
{
    public double PaceOut { get; init; } = 2.5;
    public double PaceSeconds { get; init; } = 6;
    public double Catch { get; init; } = 1.2;
    public double MaxSpeed { get; init; } = 18;
    public double LoseBehind { get; init; } = 60;
    public double ScrabbleSeconds { get; init; } = 2.5;
    public double HoldReach { get; init; } = 2;
    public int MaxTries { get; init; } = 3;
    public double TraverseSpeed { get; init; } = 2.2;
    public double Reach { get; init; } = 1.8;
    public int BiteDamage { get; init; } = 25;
    public double BiteEvery { get; init; } = 1.5;
    public double BoredSeconds { get; init; } = 120;
    public double Health { get; init; } = 40;
    public int MinGaps { get; init; } = 2;
    public double MinSpeed { get; init; } = 5;
    public double PerGapWeight { get; init; } = 0.5;
}

/// <summary>The Long Whistle (App. A.2, B.2). Field docs live in enemies.json.</summary>
public sealed record LongWhistleTuning
{
    public double AheadMin { get; init; } = 400;
    public double AheadMax { get; init; } = 900;
    public double ShortOf { get; init; } = 60;
    public double CurveRadius { get; init; } = 1200;
    public double GradePercent { get; init; } = 1;
    public double BlastEvery { get; init; } = 9;
    public int Escalations { get; init; } = 2;
    public double HardBrake { get; init; } = 4;
    public double StoppedBelow { get; init; } = 0.5;
    public double CommitSeconds { get; init; } = 45;
    public double Region { get; init; } = 1500;
    public double FogFrom { get; init; } = 0.02;
    public double FogWeight { get; init; } = 2;
}

/// <summary>The Ferryman (App. A.2, B.2). Field docs live in enemies.json.</summary>
public sealed record FerrymanTuning
{
    public double SpawnAhead { get; init; } = 600;
    public double WaitLateral { get; init; } = 2.6;
    public double SlowTolerance { get; init; } = 2.5;
    public double StepAsideAt { get; init; } = 30;
    public double StepAsideLateral { get; init; } = 3.5;
    public double StepSpeed { get; init; } = 3;
    public double AdvanceSpeed { get; init; } = 5;
    public double BoardReach { get; init; } = 3;
    public int StrikeDamage { get; init; } = 100;
    public double StrikeReach { get; init; } = 20;
    public double AboardSeconds { get; init; } = 2;
    public double LoseBehind { get; init; } = 40;
    public double LingerSeconds { get; init; } = 180;
    public double MinSpeed { get; init; } = 8;
    public double MidRunFrom { get; init; } = 0.4;
    public double StraightRadius { get; init; } = 1500;
    public double MaxGradePercent { get; init; } = 1;
    public double ClearPast { get; init; } = 100;
    public double StopMargin { get; init; } = 600;
    public double FalsePositiveWeight { get; init; } = 2;
}

/// <summary>The Deadman (App. A.5, B.5). Field docs live in enemies.json.</summary>
public sealed record DeadmanTuning
{
    public double EmptySeconds { get; init; } = 30;
    public double EmptySecondsDeep { get; init; } = 20;
    public double TelegraphSeconds { get; init; } = 10;
    public double EvictSeconds { get; init; } = 4;
    public int EvictDamage { get; init; } = 25;
}

/// <summary>The Stoker (App. A.5, B.5). Field docs live in enemies.json.</summary>
public sealed record StokerTuning
{
    public double FeedRate { get; init; } = 1.5;
    public double DriveOutSeconds { get; init; } = 3;
    public int DriveOutDamage { get; init; } = 30;
    public double StoppedBelow { get; init; } = 0.5;
    public double UnattendedSeconds { get; init; } = 10;
}

/// <summary>The Lamplighters (App. A.6, B.6). Field docs live in enemies.json.</summary>
public sealed record LamplighterTuning
{
    public double PaceLateral { get; init; } = 14;
    public double PaceBehind { get; init; } = 6;
    public double StrikeLateral { get; init; } = 1.9;
    public double StrikeReach { get; init; } = 2.6;
    public double CloseSpeed { get; init; } = 2.5;
    public double MaxSpeed { get; init; } = 16;
    public double Catch { get; init; } = 1.5;
    public double LoseBehind { get; init; } = 40;
    public double RelightSeconds { get; init; } = 45;
    public int BiteDamage { get; init; } = 40;
    public double BiteReach { get; init; } = 20;
    public double LingerSeconds { get; init; } = 240;
    public int MaxActive { get; init; } = 2;
    public double DepthWeight { get; init; } = 2;
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
