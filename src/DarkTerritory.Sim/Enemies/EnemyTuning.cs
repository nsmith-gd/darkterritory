namespace DarkTerritory.Sim.Enemies;

/// <summary>Mirror of content/tuning/enemies.json. Field docs live in that file.</summary>
public sealed record EnemyTuning(
    double MinReactionSeconds, SleeperTuning Sleepers, HoundTuning CinderHounds, ChoirSwarmTuning ChoirSwarm, DirectorTuning Director,
    double InterestRadius)
{
    public const string File = "tuning/enemies.json";

    public SwitchmanTuning Switchman { get; init; } = new();
    public SootChildrenTuning SootChildren { get; init; } = new();
    public DraggerTuning Draggers { get; init; } = new();
    public StokerTuning Stoker { get; init; } = new();
    public CarFireTuning CarFire { get; init; } = new();
    public ClimberTuning Climbers { get; init; } = new();
    public GauntTuning Gaunt { get; init; } = new();
    public PassengerTuning Passenger { get; init; } = new();
    public FollowerTuning Followers { get; init; } = new();
    public DriftTuning Drift { get; init; } = new();
    public GrabTuning Grab { get; init; } = new();
    public MeleeTuning Melee { get; init; } = new();
    public TrackDollTuning TrackDoll { get; init; } = new();
    public CarHuggerTuning CarHugger { get; init; } = new();
    public WhistlerTuning Whistler { get; init; } = new();
    public TippyToesieTuning TippyToesie { get; init; } = new();
    public FireFliesTuning FireFlies { get; init; } = new();
    public RibbitTuning Ribbits { get; init; } = new();
    public GrumblerTuning Grumbler { get; init; } = new();
    public ChoirSwarmV11 Choir { get; init; } = new();
}

/// <summary>App. C.2 melee: the tools already on the train. Field docs live in enemies.json.</summary>
public sealed record MeleeTuning
{
    public double Reach { get; init; } = 2.2;
    public double ConeDegrees { get; init; } = 70;
    public double SwingSeconds { get; init; } = 0.8;
    public double Damage { get; init; } = 1;
}

/// <summary>The Track Doll (v1.1 App. A.2, B.2). Field docs live in enemies.json.</summary>
public sealed record TrackDollTuning
{
    public double LampRevealDistance { get; init; } = 200;
    public double DarkRevealDistance { get; init; } = 25;
    public double SpawnAhead { get; init; } = 700;
    public double StoppedBelow { get; init; } = 0.3;
    public double Health { get; init; } = 2;
    public double HauntMoveSeconds { get; init; } = 25;
    public double VanishNear { get; init; } = 3;
    public double CornerWithin { get; init; } = 6;
    public double TamperAfterEmpty { get; init; } = 8;
    public double ToyReach { get; init; } = 1.8;
    public double CargoPerSecond { get; init; } = 0.001;
    public double StraightNeeded { get; init; } = 250;
    public double EmptyCabWeight { get; init; } = 2;
}

/// <summary>The Car Hugger (v1.1 App. A.3, B.3). Field docs live in enemies.json.</summary>
public sealed record CarHuggerTuning
{
    public double SpeedCap { get; init; } = 9;
    public double DragFactor { get; init; } = 1.6;
    public double ShellPerSecond { get; init; } = 0.004;
    public double LootPerSecond { get; init; } = 0.006;
    public double MouthReach { get; init; } = 1.6;
    public double SwallowSeconds { get; init; } = 10;
    public double Health { get; init; } = 16;
    public double RegenPerSecond { get; init; } = 0.15;
    public double LurkAheadMin { get; init; } = 250;
    public double LurkAheadMax { get; init; } = 800;
    public double MaxGrade { get; init; } = 0.6;
    public double LowSpeed { get; init; } = 8;
    public double LowSpeedWeight { get; init; } = 2;
    public int MinCars { get; init; } = 2;
}

/// <summary>The Whistler (v1.1 App. A.4, B.4). Field docs live in enemies.json.</summary>
public sealed record WhistlerTuning
{
    public double StoppedBelow { get; init; } = 0.3;
    public double WhistleAfterStop { get; init; } = 4;
    public double WhistleSeconds { get; init; } = 2.5;
    public double PassReach { get; init; } = 1.6;
    public double PairRadius { get; init; } = 3;
    public double SpotReach { get; init; } = 2.5;
    public double RunSpeed { get; init; } = 4.5;
    public double NestDistance { get; init; } = 60;
    public double NestSeconds { get; init; } = 20;
    public double Health { get; init; } = 3;
    public double StopWeight { get; init; } = 0.3;
    public int MinCars { get; init; } = 2;
}

/// <summary>Tippy Toesie (v1.1 App. A.5, B.5). Field docs live in enemies.json.</summary>
public sealed record TippyToesieTuning
{
    public double IdleSeconds { get; init; } = 5;
    public double IdleBelow { get; init; } = 0.3;
    public double AloneRadius { get; init; } = 8;
    public double StartBehind { get; init; } = 9;
    public double ApproachSpeed { get; init; } = 0.6;
    public double ApproachSpeedCrewOfTwo { get; init; } = 0.4;
    public double SeenDegrees { get; init; } = 50;
    public double Reach { get; init; } = 0.8;
    public double SuffocateSeconds { get; init; } = 20;
    public double WaitAfterSeen { get; init; } = 20;
    public int MaxFlees { get; init; } = 3;
    public double Health { get; init; } = 2;
    public int MinCrew { get; init; } = 2;
    public double PerIdleWeight { get; init; } = 1;
}

/// <summary>Fire Flies (v1.1 App. A.5, B.5). Field docs live in enemies.json.</summary>
public sealed record FireFliesTuning
{
    public double IgniteSeconds { get; init; } = 20;
    public double PullAwaySpeed { get; init; } = 15;
    public double PullAwaySeconds { get; init; } = 8;
    public double DepthWeight { get; init; } = 2;
}

/// <summary>Ribbits (v1.1 App. A.6, B.6). Field docs live in enemies.json.</summary>
public sealed record RibbitTuning
{
    public int[] PackSize { get; init; } = [2, 4];
    public double HopSpeed { get; init; } = 4;
    public double GroupRadius { get; init; } = 8;
    public double TongueReach { get; init; } = 4;
    public double DevourSeconds { get; init; } = 8;
    public double GiveUpBeyond { get; init; } = 45;
    public double SpawnOut { get; init; } = 35;
    public double Health { get; init; } = 2;
    public double PerGroundWeight { get; init; } = 0.5;
}

/// <summary>The Grumbler (v1.1 App. A.8, B.8). Field docs live in enemies.json.</summary>
public sealed record GrumblerTuning
{
    public double Health { get; init; } = 6;
    public double RegenPerSecond { get; init; } = 1.5;
    public double GangSeconds { get; init; } = 5;
    public double HuntSpeed { get; init; } = 3.5;
    public double Reach { get; init; } = 1.4;
    public int BiteDamage { get; init; } = 15;
    public double BiteEvery { get; init; } = 1.5;
    public double GrabBelowHealth { get; init; } = 30;
    public double MaulSeconds { get; init; } = 8;
    public double CargoPerSecond { get; init; } = 0.004;
    public double FoodWeight { get; init; } = 2;
}

/// <summary>The Choir's swarm (v1.1 App. A.7): the ghosts it sends. Field docs live in enemies.json.</summary>
public sealed record ChoirSwarmV11
{
    public int Ghosts { get; init; } = 4;
    public double Health { get; init; } = 6;
    public double SeizeSeconds { get; init; } = 12;
    public double FlySpeed { get; init; } = 6;
    public int HitBackDamage { get; init; } = 15;
    public double DisperseQuietSeconds { get; init; } = 10;
    public double Around { get; init; } = 12;
}

/// <summary>The shared GRAB rescue state (GDD v1.1 App. A.1, C.1). Field docs live in enemies.json.</summary>
public sealed record GrabTuning
{
    public double PullReach { get; init; } = 1.6;
    public bool SoloStruggleOn { get; init; } = true;
    public double SoloStruggle { get; init; } = 6;
}

/// <summary>The Drift (App. A.4, B.4). Field docs live in enemies.json.</summary>
public sealed record DriftTuning
{
    public double StartRadius { get; init; } = 4;
    public double MaxRadius { get; init; } = 9;
    public double SpreadSpeed { get; init; } = 0.15;
    public double StillSpeed { get; init; } = 0.3;
    public double SurgeSpeed { get; init; } = 2.2;
    public double ContactReach { get; init; } = 1.2;
    public int Damage { get; init; } = 6;
    public double DamageSeconds { get; init; } = 1;
    public double StillSeconds { get; init; } = 4;
    public double ExitMargin { get; init; } = 60;
    public double LingerSeconds { get; init; } = 600;
    public double ChemicalSpread { get; init; } = 2;
}

/// <summary>Followers (App. A.3, B.3). Field docs live in enemies.json.</summary>
public sealed record FollowerTuning
{
    public double LatchSeconds { get; init; } = 3;
    public double CrawlSpeed { get; init; } = 0.8;
    public double NestSeconds { get; init; } = 60;
    public double EatPerSecond { get; init; } = 0.003;
    public double Health { get; init; } = 1;
    public double NestHealth { get; init; } = 3;
    public double LingerSeconds { get; init; } = 900;
    public int MaxActive { get; init; } = 2;
    public double PerGroundWeight { get; init; } = 1;
}

/// <summary>The Passenger (App. A.7, B.7). Field docs live in enemies.json.</summary>
public sealed record PassengerTuning
{
    public double WalkSpeed { get; init; } = 1.3;
    public double AloneRadius { get; init; } = 8;
    public double StalkSeconds { get; init; } = 5;
    public double Reach { get; init; } = 1.2;
    public double DragSpeed { get; init; } = 1.3;
    public double UncoupleSeconds { get; init; } = 4;
    public double Health { get; init; } = 5;
    public double LingerSeconds { get; init; } = 1500;
    public int MinCrew { get; init; } = 3;
    public int SplitPlaces { get; init; } = 3;
    public double SplitWeight { get; init; } = 2;
}

/// <summary>A car fire (the in-car incidents). Field docs live in enemies.json.</summary>
public sealed record CarFireTuning
{
    public double StartIntensity { get; init; } = 0.1;
    public double GrowPerSecond { get; init; } = 0.01;
    public double GrowWithSize { get; init; } = 0.03;
    public double BurnFrom { get; init; } = 0.35;
    public double SprayReach { get; init; } = 2.5;
    public double SprayPerSecond { get; init; } = 0.035;
    public double ChargeSeconds { get; init; } = 10;
    public double RechargeSeconds { get; init; } = 90;
    public int BurnDamage { get; init; } = 10;
    public double BurnReach { get; init; } = 4;
    public double BurnEverySeconds { get; init; } = 2;
    public double CargoPerSecond { get; init; } = 0.003;
    public double IntegrityPerSecond { get; init; } = 0.0015;
    public double SpreadFrom { get; init; } = 0.8;
    public double SpreadSeconds { get; init; } = 30;
    public double ChemicalSpread { get; init; } = 2;
    public double ChemicalGrowth { get; init; } = 1.3;
    public double PowderGrowth { get; init; } = 1.6;
    public double BurnOutPerSecond { get; init; } = 0.05;
    public int MaxActive { get; init; } = 3;
}

/// <summary>The Gaunt (App. A.4, B.4). Field docs live in enemies.json.</summary>
public sealed record GauntTuning
{
    public double StirAt { get; init; } = 9;
    public double WakeAt { get; init; } = 4;
    public double FollowAt { get; init; } = 1.2;
    public double FollowSpeed { get; init; } = 3;
    public double ListenRadius { get; init; } = 8;
    public double SilenceSeconds { get; init; } = 5;
    public byte TalkingAbove { get; init; } = 40;
    public bool AnyVoiceCounts { get; init; } = true;
    public int AttackAt { get; init; } = 4;
    public double Reach { get; init; } = 1.6;
    public int HitDamage { get; init; } = 35;
    public double HitEvery { get; init; } = 2;
    public double GrabBelowHealth { get; init; } = 35;
    public double CrushSeconds { get; init; } = 8;
    public double Health { get; init; } = 8;
    public double LootCargo { get; init; } = 0.3;
    public double LingerSeconds { get; init; } = 900;
    public int MinCrew { get; init; } = 2;
    public double SpawnOut { get; init; } = 25;
    public double LongStopWeight { get; init; } = 2;
    public double LongStopSeconds { get; init; } = 120;
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
    public double Health { get; init; } = 3;
    public int MinGaps { get; init; } = 2;
    public double MinSpeed { get; init; } = 5;
    public double PerGapWeight { get; init; } = 0.5;
    public int[] PackSize { get; init; } = [2, 3];
    public double CountRadius { get; init; } = 8;
    public double TakeSeconds { get; init; } = 10;
}

/// <summary>The Stoker (App. A.5, B.5). Field docs live in enemies.json.</summary>
public sealed record StokerTuning
{
    public double FeedRate { get; init; } = 1.5;
    public double StoppedBelow { get; init; } = 0.5;
    public double LowPressure { get; init; } = 40;
    public double LowPressureSeconds { get; init; } = 45;
    public double DoorOpenSeconds { get; init; } = 10;
    public double FireDoorShutSeconds { get; init; } = 6;
    public double SootSeconds { get; init; } = 4;
    public double RunawayRampSeconds { get; init; } = 40;
    public double Health { get; init; } = 4;
    public int BurnPerBlow { get; init; } = 12;
    public double OpenDoorWeight { get; init; } = 3;
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
    public double StampDamage { get; init; } = 0.5;
    public double HangSeconds { get; init; } = 8;
    public double LingerSeconds { get; init; } = 180;
    public int MinCars { get; init; } = 2;
    public int MaxAttached { get; init; } = 2;

    /// <summary>The grab range at a train speed (spec B.3: +50% at max).</summary>
    public double GrabAt(double speed) => GrabRange * (1 + (FastGrabScale - 1) * Math.Clamp((speed - FastFrom) / Math.Max(1e-6, FastAt - FastFrom), 0, 1));
}

/// <summary>The Soot Children (App. A.4, B.6). Field docs live in enemies.json.</summary>
public sealed record SootChildrenTuning
{
    public double CallOut { get; init; } = 45;
    public double CallEvery { get; init; } = 6;
    public double LungeWithin { get; init; } = 5;
    public double DrainSeconds { get; init; } = 14;
    public double Health { get; init; } = 2;
    public double HealthPerSecond { get; init; } = 0.35;
    public double RealChance { get; init; } = 0.5;
    public double IgnoredSeconds { get; init; } = 120;
    public int MinCrew { get; init; } = 2;
    public double NearFacility { get; init; } = 400;
}

/// <summary>The Switchman (App. A.7, B.7). Field docs live in enemies.json.</summary>
public sealed record SwitchmanTuning
{
    public double[] Ahead { get; init; } = [500, 1200];
    public double StandBeside { get; init; } = 1.2;
    public double RevealAt { get; init; } = 300;
    public double DerailChance { get; init; } = 0.3;
    public double GripAt { get; init; } = 250;
    public double Health { get; init; } = 3;
    public double LingerSeconds { get; init; } = 20;
    public int MinJunctions { get; init; } = 3;
    public double DeadLineWeight { get; init; } = 1.5;
}

public sealed record SleeperTuning(double LampRevealDistance, double BraceDistance, double DerailAbove, double HeavyDamageAbove, double HeavyDamage, double MinorDamage);

public sealed record HoundTuning(int[] PackSize, double Health, double Radius, double MaxSpeed, double ClosingSpeed, double SpawnBehind,
    double HowlSeconds, double LeapDistance, int BiteDamage, double BiteEverySeconds, double Reach, double BoredSeconds, double MinTrainSpeed,
    int SuppressRounds, double SuppressWindowSeconds)
{
    /// <summary>A crewmate pinned in the pack fight has this long for a friend to club it off (v1.1 App. A.1 GRAB).</summary>
    public double MaulSeconds { get; init; } = 8;
}

public sealed record ChoirSwarmTuning(int ExposedDamage, double EverySeconds);

public sealed record DirectorTuning(
    Dictionary<string, double> BaseBudget, double LengthPerCarBeyondThird, double CrewBase, double CrewPerPlayer, double CrewCap,
    double GraceSeconds, double FacilityLullSeconds, double[] CooldownSeconds, int MaxConcurrentZone,
    int MaxConcurrentSmallCrew, int MaxConcurrentLargeCrew, double[] PhaseShares, Dictionary<string, double> Costs,
    double HoundsLivestockWeight, double HoundsHotBoilerWeight)
{
    /// <summary>App. B.8: cargo name → (tuning name or "*") → weight.</summary>
    public Dictionary<string, Dictionary<string, double>> CargoWeights { get; init; } = new();
    public bool CometRelaxesGates { get; init; } = true;
    /// <summary>Quiet this long (nothing showing itself, no board, no bag) and the director sends something, cooldown or not.</summary>
    public double PaceSeconds { get; init; } = 18;
    /// <summary>The last this many spawns: each of a kind among them halves that kind's weight (variety).</summary>
    public int VarietyWindow { get; init; } = 4;
    /// <summary>A paced spawn may overdraw the budget's curve by up to this much: enough for a threat of this cost.</summary>
    public double PacedCost { get; init; } = 3;
    /// <summary>The in-car incidents' weight, each, against the other threats' 1.</summary>
    public double IncidentWeight { get; init; } = 0.5;
    /// <summary>Crew (less the driver) at which the incidents come at their full weight; fewer, proportionally less.</summary>
    public double IncidentFullCrew { get; init; } = 3;
    public string[][] Conflicts { get; init; } = [];
    public double PairWeight { get; init; } = 3;
    public double BehindPairWeight { get; init; } = 3;
    public Dictionary<string, int> PairsPerRun { get; init; } = new();
    public double SleepersAhead { get; init; } = 1500;
    public double GradeAhead { get; init; } = 600;
    public double GradePercent { get; init; } = 1.5;
    public string[] SaveFor { get; init; } = [];
    public double SaveFrom { get; init; } = 0.2;
    /// <summary>
    /// A generated line's grace stretch (linegen plan §4) bans spawns only this far into the run; after, the director's own
    /// grace (after the playtest, "out of the gate in 20 s") is the rule. Negative: the whole stretch, as the plan has it.
    /// </summary>
    public double LineGraceSeconds { get; init; } = 20;
    /// <summary>
    /// The kinds this edition has (their tuning names), or empty for all of them. The demo has GDD §21's five, one per
    /// pressure zone (T79); nothing outside it is sent, and nothing condition-triggered outside it comes up either.
    /// </summary>
    public string[] Roster { get; init; } = [];
    /// <summary>App. B.1 "want balance": each want's target share of the budget (kill, split, trust, cargo).</summary>
    public Dictionary<string, double> WantShares { get; init; } = new();
    /// <summary>App. B.1 hard caps: corrupted humans, at most this many at a time.</summary>
    public int MaxCorrupted { get; init; } = 1;
}
