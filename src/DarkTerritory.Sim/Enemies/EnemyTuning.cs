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
    public CreatureSitesTuning Sites { get; init; } = new();
    public GrumblerTuning Grumbler { get; init; } = new();
    public ChoirSwarmV11 Choir { get; init; } = new();
    public MooseTuning Moose { get; init; } = new();
    public GannetTuning Gannet { get; init; } = new();
    public MournersTuning Mourners { get; init; } = new();
    public FreightBeetleTuning FreightBeetle { get; init; } = new();
    /// <summary>The damage model (GDD App. F.1, the director's decision of 6 Oct 2026; note 272): no creature's hit is chip.</summary>
    public DamageModelTuning Damage { get; init; } = new();
    /// <summary>
    /// The coordinated kill (the director's clarification of 7 Oct 2026, GDD App. F.1; note 288): blows wear down one of the
    /// creatures driven off by its rules only with this many crewmates on it at once.
    /// </summary>
    public CoordinatedKillTuning CoordinatedKill { get; init; } = new();
    /// <summary>Note 279: what holds a creature loose in the world (enemies.json <c>solidity</c>).</summary>
    public SolidityTuning Solidity { get; init; } = new();
    /// <summary>
    /// Whether a creature at the controls (the Stoker's runaway, the Track Doll's tampering) may let a standing train off its
    /// held brake (enemies.json; build 1121 playtest, note 263). False: a train nobody's driving never moves off by itself.
    /// </summary>
    public bool TamperReleasesStandingBrake { get; init; }
    /// <summary>
    /// Every creature's body as a cannonball finds it (note 290, enemies.json <c>bodies</c>): a stack of spheres, each
    /// [radius, height of its centre over where it stands], keyed by <see cref="EnemyKind"/>'s name.
    /// </summary>
    public Dictionary<string, double[][]> Bodies { get; init; } = new();
    /// <summary>A sleeping Gaunt's body, curled up: this share of its height (note 290).</summary>
    public double GauntAsleep { get; init; } = 0.35;

    Dictionary<EnemyKind, (double Radius, double Height)[]>? _bodies;

    /// <summary>The spheres of a kind's body; none for a kind that has no body (a ball goes through it).</summary>
    public (double Radius, double Height)[] Body(EnemyKind kind)
    {
        _bodies ??= Bodies.ToDictionary(
            b => Enum.Parse<EnemyKind>(b.Key, ignoreCase: true),
            b => b.Value.Select(s => (s[0], s[1])).ToArray());
        return _bodies.TryGetValue(kind, out var body) ? body : [];
    }
}

/// <summary>
/// enemies.json <c>damage</c> (GDD App. F.1, "a few big hits, never chip damage"; note 272): the least a creature's hit on a
/// player may be, and the least gap between its hits. Not read by the creatures: DamageModelTests holds every creature's
/// <c>...Damage</c> and its interval to them, so a chip number fails CI.
/// </summary>
public sealed record DamageModelTuning
{
    public int MinHit { get; init; } = 30;
    public double MinGapSeconds { get; init; } = 2.5;
}

/// <summary>
/// enemies.json <c>coordinatedKill</c> (note 288): a creature driven off by its rules is killed only by the crew together,
/// <see cref="Gang"/> different crewmates striking it within <see cref="WindowSeconds"/> (and, per creature, the setup its
/// rule asks for). A lone player's blows never wear it down.
/// </summary>
public sealed record CoordinatedKillTuning
{
    public int Gang { get; init; } = 2;
    public double WindowSeconds { get; init; } = 5;
}

/// <summary>App. C.2 melee: the tools already on the train. Field docs live in enemies.json.</summary>
public sealed record MeleeTuning
{
    public double Reach { get; init; } = 2.2;
    public double ConeDegrees { get; init; } = 70;
    public double SwingSeconds { get; init; } = 0.8;
    /// <summary>
    /// What a blow does with each tool in hand, in blows (App. C.2: "the boiler player's shovel doubling as the crew's best
    /// club"; note 275). Enemy health is counted in the crowbar's.
    /// </summary>
    public double Shovel { get; init; } = 1.5;
    public double Crowbar { get; init; } = 1;
    public double Wrench { get; init; } = 0.75;
    /// <summary>T108: a blow with nothing in hand (enemies.json).</summary>
    public double Barehanded { get; init; } = 0.25;

    /// <summary>The hardest blow any tool lands (note 275): what a client takes to be within one blow of a kill.</summary>
    public double Hardest => Math.Max(Shovel, Math.Max(Crowbar, Wrench));

    /// <summary>A blow with this in hand: the shovel the best of the train's tools, a fist a fraction of one.</summary>
    public double Blow(Player.Tool held) => held switch
    {
        Player.Tool.Shovel => Shovel,
        Player.Tool.Crowbar => Crowbar,
        Player.Tool.Wrench => Wrench,
        _ => Barehanded,
    };
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
    /// <summary>T121: a cannonball shatters her on the rail (gone for the run, as stopped short); off, it goes through her.</summary>
    public bool CannonShatters { get; init; } = true;
    /// <summary>Her escalation when ignored (the director, 6 Oct 2026; note 268): stage 2's neglect, stage 3's, the warning before each.</summary>
    public double ControlsAfter { get; init; } = 120;
    public double ReleaseAfter { get; init; } = 300;
    public double WarnSeconds { get; init; } = 30;
    /// <summary>A crewmate this close (or in her car) is attending her: the neglect clock winds back at <see cref="AttendedEase"/>.</summary>
    public double AttendRadius { get; init; } = 8;
    public double AttendedEase { get; init; } = 0.5;
    /// <summary>Stage 2 at the controls: the regulator nudged up to this, never the brake.</summary>
    public double NudgeThrottle { get; init; } = 0.25;
    /// <summary>Stage 3: this long at the controls at that stage, on a visit, before a standing train's held brake goes too.</summary>
    public double ReleaseAfterAtControls { get; init; } = 12;
    public bool FinalStageReleasesBrake { get; init; } = true;
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
    /// <summary>Every this much of the shell eaten, it's through the end wall again: the car breached (decided 1 Oct).</summary>
    public double BreachEaten { get; init; } = 0.1;
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
    public double NestDistance { get; init; } = 30;
    public double NestMinDistance { get; init; } = 8;
    public double NestStep { get; init; } = 4;
    /// <summary>How far from its gap a stop's own nest may be and still be where it runs (note 314).</summary>
    public double NestSiteReach { get; init; } = 110;
    public double NestMaxSlope { get; init; } = 0.4;
    public double NestMaxRise { get; init; } = 6;
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
    /// <summary>Boarding-first (GDD App. F.1, note 286): it slips aboard only while the train is under this (m/s): a stop.</summary>
    public double BoardBelow { get; init; } = double.MaxValue;
}

/// <summary>Fire Flies (v1.1 App. A.5, B.5). Field docs live in enemies.json.</summary>
public sealed record FireFliesTuning
{
    public double IgniteSeconds { get; init; } = 20;
    public double PullAwaySpeed { get; init; } = 15;
    public double PullAwaySeconds { get; init; } = 8;
    public double DepthWeight { get; init; } = 2;
    /// <summary>GDD App. F, 6 Oct 2026 (note 269): the director sends them only while the train is under this (m/s).</summary>
    public double StoppedBelow { get; init; } = 0.3;
    /// <summary>Note 269: "their pull … is rare": their weight at a stop, against the rest of the table.</summary>
    public double StoppedWeight { get; init; } = 0.5;
    /// <summary>Boarding-first (GDD App. F.1, note 286): a lit car with every door and its hatch shut (and no breach) keeps them out.</summary>
    public bool ShutCarKeepsOut { get; init; }
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
    public int BiteDamage { get; init; } = 35;
    public double BiteEvery { get; init; } = 3;
    public double GrabBelowHealth { get; init; } = 35;
    public double MaulSeconds { get; init; } = 8;
    public double CargoPerSecond { get; init; } = 0.004;
    public double FoodWeight { get; init; } = 2;
    /// <summary>Note 288: driven off by its rule (gang up), killed only by a gang. False: the old health-and-regen fight.</summary>
    public bool DrivenOff { get; init; } = true;
    public double GangRadius { get; init; } = 3;
    public double OutnumberedSeconds { get; init; } = 2;
    public double FleeSeconds { get; init; } = 8;
}

/// <summary>The Choir's swarm (v1.1 App. A.7): the ghosts it sends. Field docs live in enemies.json.</summary>
public sealed record ChoirSwarmV11
{
    public int Ghosts { get; init; } = 4;
    public double Health { get; init; } = 6;
    public double SeizeSeconds { get; init; } = 12;
    public double FlySpeed { get; init; } = 6;
    public int HitBackDamage { get; init; } = 35;
    /// <summary>Note 272 (App. F.1, no chip damage): a ghost hits back at most once in this long, however fast it's struck.</summary>
    public double HitBackEvery { get; init; } = 3;
    public double DisperseQuietSeconds { get; init; } = 10;
    public double Around { get; init; } = 12;
    /// <summary>Note 288: a seize broken by quiet or a shut door; a ghost killed only by a gang while the crew holds quiet. False: the old health fight.</summary>
    public bool DrivenOff { get; init; } = true;
    public double HushBreakSeconds { get; init; } = 3;
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
    public int Damage { get; init; } = 35;
    public double DamageSeconds { get; init; } = 2.5;
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
    /// <summary>Note 288: a blow finds it out and drives it off; killed only by a gang. False: the old five blows to kill.</summary>
    public bool DrivenOff { get; init; } = true;
    public double FleeSpeed { get; init; } = 2.2;
    public double UnmaskedSeconds { get; init; } = 2.5;
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
    public double BurnReach { get; init; } = 4;
    /// <summary>Note 265: the burn at full blaze once you've stood in it <see cref="BurnRampSeconds"/> (health a second).</summary>
    public double BurnPerSecond { get; init; } = 10;
    /// <summary>Note 265: the share of <see cref="BurnPerSecond"/> a brush against it burns at, from the first moment.</summary>
    public double BurnBrushShare { get; init; } = 0.5;
    /// <summary>Note 265: how long in it before it burns at the full rate (s).</summary>
    public double BurnRampSeconds { get; init; } = 3;
    public double CargoPerSecond { get; init; } = 0.003;
    public double IntegrityPerSecond { get; init; } = 0.0015;
    public double SpreadFrom { get; init; } = 0.8;
    public double SpreadSeconds { get; init; } = 30;
    public double ChemicalSpread { get; init; } = 2;
    public double ChemicalGrowth { get; init; } = 1.3;
    public double PowderGrowth { get; init; } = 1.6;
    public double FuelGrowth { get; init; } = 1.4;
    public double FuelSpread { get; init; } = 2;
    public double ExplodeAt { get; init; } = 1;
    public double ExplodeRadius { get; init; } = 14;
    public double ExplodeKillRadius { get; init; } = 5;
    public int ExplodeDamage { get; init; } = 150;
    public double BurnOutPerSecond { get; init; } = 0.05;
    public int MaxActive { get; init; } = 3;
    /// <summary>Note 267: the fire grid's cell size (m; App. F.1 "large cells of 1–2 m").</summary>
    public double CellSize { get; init; } = 1.5;
    /// <summary>Note 267: a cell this hot heats the cells round it, at <see cref="CatchPerSecond"/> x its heat, upward x <see cref="Climb"/>.</summary>
    public double CatchFrom { get; init; } = 0.5;
    public double CatchPerSecond { get; init; } = 0.04;
    public double Climb { get; init; } = 2;
    /// <summary>Note 267: what a cell burns of itself a second at full heat (1: all of it); it chars as it goes.</summary>
    public double CharPerSecond { get; init; } = 0.01;
    /// <summary>Note 267: of the spray on a cell, the share the cells round it get; and how long a sprayed cell stays wet (s).</summary>
    public double SprayShare { get; init; } = 0.4;
    public double DampSeconds { get; init; } = 3;
    /// <summary>Note 267: the roof's burn on whoever's under it, of a floor or wall cell's as near.</summary>
    public double CeilingBurnShare { get; init; } = 0.5;
    /// <summary>Note 267: how tall a crewmate is to the fire (m): what of them a burning cell can reach.</summary>
    public double BodyHeight { get; init; } = 1.8;
    /// <summary>Note 267: a cell cooler than this doesn't burn on its own, and goes out unless a cell round it heats it.</summary>
    public double OutBelow { get; init; } = 0.05;
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
    public int HitDamage { get; init; } = 60;
    public double HitEvery { get; init; } = 4;
    public double GrabBelowHealth { get; init; } = 35;
    public double CrushSeconds { get; init; } = 8;
    public double Health { get; init; } = 8;
    public double LootCargo { get; init; } = 0.3;
    public double LingerSeconds { get; init; } = 900;
    public int MinCrew { get; init; } = 2;
    public double SpawnOut { get; init; } = 25;
    public double LongStopWeight { get; init; } = 2;
    public double LongStopSeconds { get; init; } = 120;
    public double LeaveSpeed { get; init; } = 1.4;
    public double ClearedAt { get; init; } = 30;
    public double CarryHigh { get; init; } = 1.7;
    public double CarryLow { get; init; } = 0.55;
    /// <summary>Note 288: talked down, it leaves; killed only by a gang while someone talks to it. False: the old health fight.</summary>
    public bool DrivenOff { get; init; } = true;
    public bool TalkingCalms { get; init; } = true;
    public double TalkedDownSeconds { get; init; } = 45;
}

/// <summary>Climbers (App. A.4, B.4). Field docs live in enemies.json.</summary>
public sealed record ClimberTuning
{
    /// <summary>GDD §23 "lights fail" (note 183): coming over the engine's end, it smashes the forward lamp for this long.</summary>
    public double LampOutSeconds { get; init; } = 45;
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
    public int BiteDamage { get; init; } = 35;
    public double BiteEvery { get; init; } = 3;
    public double BoredSeconds { get; init; } = 120;
    public double Health { get; init; } = 3;
    public int MinGaps { get; init; } = 2;
    public double MinSpeed { get; init; } = 5;
    public double PerGapWeight { get; init; } = 0.5;
    public int[] PackSize { get; init; } = [2, 3];
    public double CountRadius { get; init; } = 8;
    public double TakeSeconds { get; init; } = 10;
    /// <summary>Getting into a shut car that's lit (with nobody in it) breaches it too; unset, only an unlit one (ARCHITECTURE §8).</summary>
    public bool BreachLitCars { get; init; }
    /// <summary>Note 288: outnumbered where it is, it drops back off; killed only by a gang that outnumbers it. False: the old three blows.</summary>
    public bool DrivenOff { get; init; } = true;
    public double OutnumberedSeconds { get; init; } = 2;
    /// <summary>
    /// Boarding-first (GDD App. F.1, note 286): a lit car with every door and its hatch shut keeps them out (they pass over
    /// it, as over a car with crew in it); unset, an empty lit car lets them in whatever its doors.
    /// </summary>
    public bool LitShutCarKeepsOut { get; init; }
    /// <summary>Note 286 ("slowing opens the doors"): at the gap they get a grip only with the train under this (m/s).</summary>
    public double MountBelow { get; init; } = double.MaxValue;
    /// <summary>Note 286: pacing a train too fast to mount, they give it up this long (s) after their pace.</summary>
    public double WaitForSlowSeconds { get; init; } = 30;
    /// <summary>Note 286: the director's weight for them with the train at or over <see cref="MountBelow"/>.</summary>
    public double AtSpeedWeight { get; init; } = 1;
    /// <summary>Note 286: a bend this sharp (radius, m) under the train or within <see cref="BendAheadM"/> ahead is a tight one.</summary>
    public double TightBendRadius { get; init; } = 350;
    public double BendAheadM { get; init; } = 300;
    /// <summary>Note 286: their weight on a tight bend, slow.</summary>
    public double BendWeight { get; init; } = 1;
}

/// <summary>The Stoker (App. A.5, B.5). Field docs live in enemies.json.</summary>
public sealed record StokerTuning
{
    // Note 265, the director's decision of 6 Oct 2026: drawn by heat, boards at the tender, worse once in, a break once beaten.
    public double HeatFirebox { get; init; } = 4.5;
    public double HeatSeconds { get; init; } = 20;
    public double BoardSeconds { get; init; } = 8;
    public double TenderBlowScale { get; init; } = 4;
    public double FeedRate { get; init; } = 4;
    public double Swing { get; init; } = 3;
    public double SwingSeconds { get; init; } = 3;
    public double EatPerSecond { get; init; } = 0;
    public double FireDoorShutSeconds { get; init; } = 6;
    public double SootSeconds { get; init; } = 4;
    public double RunawayRampSeconds { get; init; } = 20;
    public double Health { get; init; } = 4;
    /// <summary>Note 268 (Stoker v3): the door opened on it burns whoever's at it this much, and the second time this much.</summary>
    public int DoorBurn { get; init; } = 60;
    public int DoorKill { get; init; } = 1000;
    /// <summary>Note 268: starved under this much fire (of the firebox's 6), it leaves.</summary>
    public double StarveFirebox { get; init; } = 2;
    /// <summary>Note 268: an extinguisher held into the firebox this long kills it, taking this share of the fire.</summary>
    public double HoseSeconds { get; init; } = 2;
    public double HoseFireCost { get; init; } = 0.6;
    /// <summary>Note 268: how near the fire door (m, across the cab floor) counts as at it, to open it or hose it.</summary>
    public double DoorReach { get; init; } = 1.8;
    /// <summary>Note 265 (the director's decision of 6 Oct 2026): once one's gone, none comes back for this long (s).</summary>
    public double BreakSeconds { get; init; } = 150;
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
    /// <summary>
    /// Boarding-first (GDD App. F.1, note 286): they get under a car only with the train under this (m/s), at a stop or a
    /// slow bend, and wait there for someone on the roofs. Unset (the old rule): under a walked car at any speed.
    /// </summary>
    public double BoardBelow { get; init; } = double.MaxValue;
    /// <summary>Note 286: their weight on a tight bend (<see cref="ClimberTuning.TightBendRadius"/>), slow.</summary>
    public double BendWeight { get; init; } = 1;

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
    /// <summary>
    /// T121 playtest ("we appear to have derailed at a very low speed"): points thrown under a train crawling over them
    /// split, they don't throw it off. At or under this (m/s) the train runs through them: the engine takes
    /// <see cref="RunThroughDamage"/> and the clock takes the stop. Over it, it's off the rails.
    /// </summary>
    public double DerailAbove { get; init; } = 6.9;
    public double RunThroughDamage { get; init; } = 0.25;
    /// <summary>
    /// The director's decision of 7 Oct 2026 (note 286): "the switch itself shouldn't cause derail, it should be lines that
    /// lead nowhere". Off (the default), it never throws points under a train: every Switchman throws the junction ahead
    /// down a dead line. On, <see cref="DerailChance"/> of them throw under the train as v1.1 had it (for a mod).
    /// </summary>
    public bool ThrowsUnderTrain { get; init; }
    /// <summary>Note 286: killed before the train reaches its points, its lever falls back and the points go back to the main line.</summary>
    public bool KilledSetsBack { get; init; } = true;
    public int MinJunctions { get; init; } = 3;
    public double DeadLineWeight { get; init; } = 1.5;
}

/// <summary>enemies.json <c>sleepers</c> (track debris, GDD §22). Field docs live in that file.</summary>
/// <param name="Enabled">Placed on generated lines and run at all: off since the director's decision of 2026-10-06 (note 265); a mod can bring them back.</param>
/// <param name="BraceLeadSeconds">Note 266: they brace (and are heard) as far out as a train at its speed needs to brake under them: this long at its speed, then a service stop (train.json overspeed).</param>
/// <param name="DerailLeadSeconds">Note 266: they derail a train only once their telegraph has been up this long; short of it, they're the heavy damage.</param>
public sealed record SleeperTuning(double LampRevealDistance, double BraceDistance, double DerailAbove, double HeavyDamageAbove, double HeavyDamage, double MinorDamage,
    bool Enabled = false, double BraceLeadSeconds = 4, double DerailLeadSeconds = 4);

public sealed record HoundTuning(int[] PackSize, double Health, double Radius, double MaxSpeed, double ClosingSpeed, double SpawnBehind,
    double HowlSeconds, double LeapDistance, int BiteDamage, double BiteEverySeconds, double Reach, double BoredSeconds, double MinTrainSpeed,
    int SuppressRounds, double SuppressWindowSeconds)
{
    /// <summary>A crewmate pinned in the pack fight has this long for a friend to club it off (v1.1 App. A.1 GRAB).</summary>
    public double MaulSeconds { get; init; } = 8;
    /// <summary>
    /// GDD App. F, 6 Oct 2026 (note 269): "Cinder Hounds that board stay aboard." Off restores the v1.1 drop-off after
    /// <see cref="BoredSeconds"/> with nobody near.
    /// </summary>
    public bool StayAboard { get; init; } = true;
    /// <summary>Note 269: aboard with nobody near, each eats this share of its car's cargo a second.</summary>
    public double CargoPerSecond { get; init; } = 0.002;
    /// <summary>Note 269: and sets its car alight this long after it's left alone (and again, after it's put out).</summary>
    public double IgniteEverySeconds { get; init; } = 20;
}

public sealed record ChoirSwarmTuning(int ExposedDamage, double EverySeconds);

public sealed record DirectorTuning(
    Dictionary<string, double> BaseBudget, double LengthPerCarBeyondThird, double CrewBase, double CrewPerPlayer, double CrewCap,
    double FacilityLullSeconds, double[] CooldownSeconds, int MaxConcurrentZone,
    int MaxConcurrentSmallCrew, int MaxConcurrentLargeCrew, double[] PhaseShares, Dictionary<string, double> Costs,
    double HoundsLivestockWeight, double HoundsHotBoilerWeight)
{
    /// <summary>App. B.8: cargo name → (tuning name or "*") → weight.</summary>
    public Dictionary<string, Dictionary<string, double>> CargoWeights { get; init; } = new();
    /// <summary>
    /// GDD §18 "something already lives here" (note 185): facility name → (tuning name) → weight, while the train's stopped
    /// at a facility of that kind.
    /// </summary>
    public Dictionary<string, Dictionary<string, double>> Residents { get; init; } = new();
    public bool CometRelaxesGates { get; init; } = true;
    /// <summary>
    /// A threat that's gone this long with nobody alive within <see cref="LingerRadius"/> of it, short of a grab, gives up and
    /// goes (T114 playtest: "where are all the monsters": a Climber settled in a car nobody went into held the caps full).
    /// </summary>
    public double LingerSeconds { get; init; } = 120;
    public double LingerRadius { get; init; } = 12;
    /// <summary>
    /// The quiet spell at the start of a night (GDD App. B.1 "Grace period", design decision 2026-10): a range, picked per night
    /// from its seed (<see cref="PressureTuning.GraceTierScale"/> shortens it at the harder tiers).
    /// </summary>
    public double GraceMinSeconds { get; init; } = 20;
    public double GraceMaxSeconds { get; init; } = 90;
    /// <summary>The pressure model (GDD App. B.1, design decision 2026-10; ARCHITECTURE §8 note 266).</summary>
    public PressureTuning Pressure { get; init; } = new();
    /// <summary>T128 (note 273): the pressure on a crewmate the train has left behind.</summary>
    public AbandonedTuning Abandoned { get; init; } = new();
    /// <summary>Note 327 (GDD App. F.3): the crew on foot off the train, watched: the pressure they draw, and the signs they're shown.</summary>
    public AfootTuning Afoot { get; init; } = new();
    /// <summary>The hound run (note 328): a fast train's wave of Cinder Hounds, answered by the guns one hound at a time.</summary>
    public HoundRunTuning Run { get; init; } = new();
    /// <summary>The orchestrator's live crew and caps (note 336; orchestrator.md §3.2).</summary>
    public OrchestratorTuning Orchestrator { get; init; } = new();
    /// <summary>The last this many spawns: each of a kind among them halves that kind's weight (variety).</summary>
    public int VarietyWindow { get; init; } = 4;
    /// <summary>A spawn pressed for (pressure at <see cref="PressureTuning.PressAt"/>) may overdraw the budget's curve by up to this much: enough for a threat of this cost.</summary>
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
    /// A generated line's grace stretch (linegen plan §4) bans spawns only this far into the run, or to the end of the night's own
    /// grace (<see cref="GraceMinSeconds"/>..<see cref="GraceMaxSeconds"/>) if that's later. Negative: the whole stretch, as the
    /// plan has it.
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
    /// <summary>GDD v1.4 App. D.11, the dead's creature vote (enemies.json director.vote; note 180).</summary>
    public VoteTuning Vote { get; init; } = new();
    /// <summary>What draws the night's first threat, and the dark's answer (enemies.json director.draw; note 287).</summary>
    public DrawTuning Draw { get; init; } = new();
}

/// <summary>
/// The director's pressure (GDD App. B.1, design decision 2026-10; note 266): once a second after the grace it builds by
/// <c>tier × conditions × crew relief × busy × escalation × (base + quiet + loudness + cargo)</c>, banks to at most
/// <see cref="Max"/>, and past <see cref="Threshold"/> the director spends on what its weights pick, each spawn taking
/// <see cref="ReliefPerCost"/> × its cost off. Mirror of enemies.json <c>director.pressure</c>; field docs live there.
/// </summary>
public sealed record PressureTuning
{
    public double Threshold { get; init; } = 10;
    public double Start { get; init; } = 4;
    public double Max { get; init; } = 18;
    public double PressAt { get; init; } = 16;
    public double ReliefPerCost { get; init; } = 3;
    public double BasePerSecond { get; init; } = 0.03;
    public double Escalation { get; init; } = 3;
    public int EscalationPower { get; init; } = 1;
    public double QuietPerSecond { get; init; } = 0.1;
    public double QuietRampSeconds { get; init; } = 90;
    /// <summary>
    /// GDD App. F.1 (the director, 6 Oct 2026; note 270): quiet counted in line run, not seconds. Over 0, the quiet ramps over
    /// this many metres run since a threat was engaged, or over <see cref="QuietBackstopSeconds"/> if that's sooner (a
    /// stopped train can't wait it out); 0 keeps the ramp in seconds (<see cref="QuietRampSeconds"/>).
    /// </summary>
    public double QuietRampMetres { get; init; }
    public double QuietBackstopSeconds { get; init; } = 120;
    public double LoudPerSecond { get; init; } = 0.1;
    public double LoudCap { get; init; } = 1.5;
    public double CargoPerLoad { get; init; } = 0.01;
    public Dictionary<string, double> CargoValue { get; init; } = new();
    public Dictionary<string, double> Tier { get; init; } = new();
    public Dictionary<string, double> GraceTierScale { get; init; } = new();
    public double Dark { get; init; } = 0.15;
    public double Cold { get; init; } = 0.1;
    public double ColdPerStep { get; init; } = 0.05;
    public double Wet { get; init; } = 0.05;
    public double Wind { get; init; } = 0.05;
    public int DownPower { get; init; } = 2;
    public int HurtBelow { get; init; } = 35;
    public double HurtRelief { get; init; } = 0.25;
    public double Busy { get; init; } = 0.5;
    public double BusyFade { get; init; } = 1;
}

/// <summary>
/// T128 (build 1121: "a player left behind by the train should feel the world close in"; note 273): a pressure of its own
/// on each crewmate on the ground further than <see cref="BehindM"/> along the line from the train, built once a second on
/// top of the night's (its tier and conditions), and past <see cref="Threshold"/> a hunt sent at them alone, each bigger
/// and closer than the last. Mirror of enemies.json <c>director.abandoned</c>; field docs live there.
/// </summary>
public sealed record AbandonedTuning
{
    public bool On { get; init; } = true;
    public double BehindM { get; init; } = 150;
    public double PerSecond { get; init; } = 0.05;
    public double RampPerSecond { get; init; } = 0.15;
    public double RampSeconds { get; init; } = 60;
    public double PerKm { get; init; } = 0.3;
    public double Threshold { get; init; } = 6;
    public double Relief { get; init; } = 6;
    public int[] Pack { get; init; } = [2, 5];
    public double[] SpawnOut { get; init; } = [35, 18];
    /// <summary>From this hunt (0 the first) a Gaunt woken on them comes too (note 296); −1 never.</summary>
    public int GauntFrom { get; init; } = 2;
}

/// <summary>
/// The orchestrator's census and caps (ARCHITECTURE §8 note 336; docs/design/orchestrator.md §3.2 1, 3, 5): the budget's crew
/// multiplier from the crew alive now, and the engaged cap by it. Mirror of enemies.json <c>director.orchestrator</c>; field
/// docs live there.
/// </summary>
public sealed record OrchestratorTuning
{
    public bool On { get; init; } = true;
    public bool LiveCrew { get; init; } = true;
    public double EngagedPerActive { get; init; } = 0.75;
    public double PerPlayer { get; init; } = 1;
}

/// <summary>
/// Note 327 (GDD App. F.3, the director, 7 Oct 2026: "when they leave, there is this presence of threat at all times"). Field
/// docs in enemies.json director.afoot.
/// </summary>
public sealed record AfootTuning
{
    public bool On { get; init; } = true;
    public double FromTrainM { get; init; } = 20;
    public double PerSecond { get; init; } = 0.08;
    public double OutsideWeight { get; init; } = 2.5;
    public double[] SignEvery { get; init; } = [12, 24];
    public double FirstSign { get; init; } = 6;
    public double[] SignOut { get; init; } = [14, 22];
    public double SignReach { get; init; } = 160;
    public double SignSeconds { get; init; } = 3.5;
    public double SignSpread { get; init; } = 35;
    /// <summary>Eye height off the ground, by the creature's tuning name (default <see cref="SignHeightDefault"/>).</summary>
    public Dictionary<string, double> SignHeight { get; init; } = new();
    public double SignHeightDefault { get; init; } = 0.7;
}

/// <summary>
/// The hound run (ARCHITECTURE §8 note 328; docs/design/orchestrator.md §5.3, §6.1; GDD App. F.3, the director, 7 Oct 2026:
/// "things that are trying to attack the train sort of like tower defense style that gives our gunners things to do"): a
/// train run fast long enough draws a stream of Cinder Hounds faster than it is, sized to the crew active, that the guns
/// answer one hound at a time. Mirror of enemies.json <c>director.run</c>; field docs live there.
/// </summary>
public sealed record HoundRunTuning
{
    public bool On { get; init; } = true;
    public double FromSpeed { get; init; } = 19;
    public double StopSpeed { get; init; } = 2;
    public double AfterMetres { get; init; } = 2400;
    public double HotShorter { get; init; } = 0.75;
    public double Base { get; init; } = 1.4;
    public double PerActive { get; init; } = 0.6;
    public int[] Size { get; init; } = [2, 6];
    public double Spacing { get; init; } = 6;
    public double SpawnBehind { get; init; } = 200;
    public double[] Lateral { get; init; } = [4, 8];
    public double Closing { get; init; } = 5;
    public double Scatter { get; init; } = 5;
}

/// <summary>
/// D.11 and D.13: each vote multiplies its creature's spawn weight by <paramref name="PerVote"/>, to at most <paramref name="Cap"/>,
/// within its want tag; a dead player's ballot is <paramref name="Options"/> creatures drawn by weighted roll from what's eligible.
/// A dead bot, a crewmate like any other, casts its vote <paramref name="BotSeconds"/> after it's offered (note 202).
/// </summary>
public sealed record VoteTuning(double PerVote = 1.2, double Cap = 1.5, int Options = 3, double BotSeconds = 6);

/// <summary>The Moose (GDD §21, App. A.6, B.6; ARCHITECTURE §8 note 339). Field docs live in enemies.json.</summary>
public sealed record MooseTuning
{
    public double CrowdAt { get; init; } = 20;
    public double CrowdPerSecond { get; init; } = 25;
    public double CloseAt { get; init; } = 12;
    public double ClosePerSecond { get; init; } = 70;
    public double HearVoice { get; init; } = 15;
    public int TalkingAbove { get; init; } = 40;
    public double VoicePerSecond { get; init; } = 40;
    public double TrainPassAt { get; init; } = 25;
    public double TrainPass { get; init; } = 25;
    public double CalmPerSecond { get; init; } = 15;
    public double ListenAt { get; init; } = 20;
    public double WarnAt { get; init; } = 50;
    public double[] SquareUpAt { get; init; } = [12, 30];
    public double SquareUpSeconds { get; init; } = 2.5;
    public double HuntSpeed { get; init; } = 4.5;
    public double ChargeSpeed { get; init; } = 11;
    public double Overrun { get; init; } = 8;
    public double WheelSeconds { get; init; } = 2.5;
    public double RackSpan { get; init; } = 3.2;
    public double HitReach { get; init; } = 0.5;
    public double SnagSeconds { get; init; } = 4;
    public int BlockedCharges { get; init; } = 3;
    public int ChargeDamage { get; init; } = 60;
    public double GrabBelowHealth { get; init; } = 40;
    public double PinSeconds { get; init; } = 12;
    public double SightRange { get; init; } = 60;
    public double SearchSpeed { get; init; } = 2.5;
    public double SearchSeconds { get; init; } = 25;
    public double LeashRadius { get; init; } = 80;
    public double LostAtCar { get; init; } = 5;
    public double RamEvery { get; init; } = 3;
    public double RamSeconds { get; init; } = 15;
    public double TrackClearance { get; init; } = 3.2;
    public double MovingClearance { get; init; } = 6;
    public double GoneBeyond { get; init; } = 600;
    public double[] GroundAt { get; init; } = [30, 60];
    public int MinCrew { get; init; } = 1;
    public double PerGroundWeight { get; init; } = 0.5;
    public Dictionary<string, double> TierWeights { get; init; } = new() { ["local"] = 1, ["frontier"] = 1.5, ["deadLines"] = 2, ["deepTerritory"] = 2.5 };
    public Dictionary<string, double> Lineside { get; init; } = new() { ["local"] = 2, ["frontier"] = 3, ["deadLines"] = 4, ["deepTerritory"] = 5 };
    public double[] LinesideOut { get; init; } = [8, 22];
    public double LinesideAhead { get; init; } = 300;
    public Dictionary<string, double> BiomeWeights { get; init; } = new();

    /// <summary>A tier's weight in a table keyed by its camel-cased name (1 where it isn't listed).</summary>
    public static double ByTier(IReadOnlyDictionary<string, double> table, Route.RouteTier tier) =>
        table.GetValueOrDefault(char.ToLowerInvariant(tier.ToString()[0]) + tier.ToString()[1..], 1);
}

/// <summary>Where the outside creatures start: their sites in the stops' layouts (level-design H.2; note 309). Field docs in enemies.json.</summary>
public sealed record CreatureSitesTuning
{
    public double Around { get; init; } = 300;
    public double MinOut { get; init; } = 12;
    public double WarrenReach { get; init; } = 70;
    public double RoostReach { get; init; } = 400;
    public double CallReach { get; init; } = 160;
    public double GroundMargin { get; init; } = 4;
}

/// <summary>How a creature loose in the world is held by it (note 279): on the land, in the air over it, or its own way.</summary>
public enum Solid : byte { None, Ground, Air }

/// <summary>The world is solid for the creatures (note 279). Field docs live in enemies.json <c>solidity</c>.</summary>
public sealed record SolidityTuning
{
    public double ClimbM { get; init; } = 2.5;
    public double AirClearM { get; init; } = 0.2;
    public Dictionary<string, string> Kinds { get; init; } = new();

    Dictionary<EnemyKind, Solid>? _kinds;

    public Solid Of(EnemyKind kind)
    {
        _kinds ??= Kinds.ToDictionary(k => Enum.Parse<EnemyKind>(k.Key, ignoreCase: true), k => Enum.Parse<Solid>(k.Value, ignoreCase: true));
        return _kinds.TryGetValue(kind, out var s) ? s : Solid.None;
    }
}

/// <summary>The Gannet (GDD §21, App. A.4, B.4; ARCHITECTURE §8 note 340). Field docs live in enemies.json.</summary>
public sealed record GannetTuning
{
    public double ArriveAbove { get; init; } = 18;
    public double ArriveSeconds { get; init; } = 30;
    public double StallBelow { get; init; } = 12;
    public double StallSeconds { get; init; } = 6;
    public double QuietSeconds { get; init; } = 60;
    public double ReturnSeconds { get; init; } = 180;
    public double[] SoarHeight { get; init; } = [20, 35];
    public double SoarRadius { get; init; } = 14;
    public double FlySpeed { get; init; } = 12;
    public double PreyAbove { get; init; } = 0.8;
    public double HangSeconds { get; init; } = 2;
    public double FoldSeconds { get; init; } = 1.6;
    public double[] DiveEvery { get; init; } = [8, 12];
    public double StrikeRadius { get; init; } = 0.9;
    public int StabDamage { get; init; } = 35;
    public double StuckSeconds { get; init; } = 4;
    public double BankSeconds { get; init; } = 2.5;
    public int Pecks { get; init; } = 4;
    public double PeckEvery { get; init; } = 3;
    public int DriveOffBlows { get; init; } = 3;
    public double Health { get; init; } = 12;
    public double GiveUpBelow { get; init; } = 4;
    public double MarkReach { get; init; } = 200;
    public int MinCars { get; init; } = 2;
    public double PerRoofWeight { get; init; } = 1;
    public Dictionary<string, double> TierWeights { get; init; } = new() { ["local"] = 1, ["frontier"] = 1.5, ["deadLines"] = 2, ["deepTerritory"] = 2.5 };
    public Dictionary<string, double> BiomeWeights { get; init; } = new();
}

/// <summary>The Mourners (GDD §21, App. A.6, B.6; ARCHITECTURE §8 note 362). Field docs live in enemies.json.</summary>
public sealed record MournersTuning
{
    public bool Enabled { get; init; } = true;
    public double After { get; init; } = 15;
    public Dictionary<string, int> Count { get; init; } = new() { ["local"] = 3, ["frontier"] = 4, ["deadLines"] = 5, ["deepTerritory"] = 6 };
    public double ArriveAt { get; init; } = 40;
    public double ComeSeconds { get; init; } = 6;
    public double WaitAt { get; init; } = 10;
    public double Shy { get; init; } = 6;
    public double StartleTo { get; init; } = 12;
    public double DropWithin { get; init; } = 3;
    public double TakeReach { get; init; } = 0.9;
    public double Creep { get; init; } = 1.4;
    public double Approach { get; init; } = 3;
    public double Drag { get; init; } = 1.6;
    public double Scatter { get; init; } = 4;
    public double HoldAt { get; init; } = 0.7;
    public double HoldHeight { get; init; } = 0.5;
    public double ReturnAfter { get; init; } = 4;
    public double ScatterOnDeath { get; init; } = 6;
    public double LostAt { get; init; } = 120;
    public double LeaveSeconds { get; init; } = 30;
    public double Health { get; init; } = 1;

    /// <summary>How many come, by tier (3 where the tier isn't listed).</summary>
    public int CountFor(Route.RouteTier tier) =>
        Count.GetValueOrDefault(char.ToLowerInvariant(tier.ToString()[0]) + tier.ToString()[1..], 3);
}

/// <summary>The Freight Beetle (GDD §21, App. A.6, B.6; ARCHITECTURE §8 note 366). Field docs live in enemies.json.</summary>
public sealed record FreightBeetleTuning
{
    public double Notice { get; init; } = 25;
    public double FreightReach { get; init; } = 30;
    public double HeadAt { get; init; } = 1.1;
    public double PushReach { get; init; } = 0.35;
    public double Walk { get; init; } = 2;
    public double Push { get; init; } = 1.2;
    public double PushHeavy { get; init; } = 0.8;
    public double TurnDegrees { get; init; } = 90;
    public double StartleWithin { get; init; } = 1.5;
    public double StartleSeconds { get; init; } = 2;
    public int DriveOffBlows { get; init; } = 3;
    public double DriveOffSeconds { get; init; } = 10;
    public double AwaySeconds { get; init; } = 30;
    public double Health { get; init; } = 6;
    public double FacilityReach { get; init; } = 80;
    public Dictionary<string, double> TierWeights { get; init; } = new() { ["local"] = 1, ["frontier"] = 1.25, ["deadLines"] = 1.5, ["deepTerritory"] = 1.5 };
}
