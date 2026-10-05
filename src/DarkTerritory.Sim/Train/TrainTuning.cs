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
    /// <summary>Line Plan §12.6, never an unrecoverable body or kit (train.json <c>recovery</c>; note 181).</summary>
    public RecoveryTuning Recovery { get; init; } = new();
    /// <summary>What the consist's made of past engine, cargo and guard van, and what its fittings do (train.json <c>composition</c>; note 184).</summary>
    public CompositionTuning Composition { get; init; } = new();
    /// <summary>GDD §19's fragile medicine (train.json <c>fragile</c>; note 182). Unset, medicine rides like anything else.</summary>
    public FragileTuning? Fragile { get; init; }

    public const string File = "tuning/train.json";
}

/// <summary>
/// The consist's make-up and fittings (GDD §10, §26, spec F.3; train.json <c>composition</c>, ARCHITECTURE §8 note 184). The
/// counts and the flag are what the fortress sells (campaign.json's upgrades add to them); the rest is what each does.
/// </summary>
public sealed record CompositionTuning
{
    /// <summary>Guard cars, each with its gun: the van at the back, and any more in the middle of the cargo (spec F.3's third gun).</summary>
    public int GuardCars { get; init; } = 1;
    /// <summary>Crew cars (GDD §10 "utility cars"): stores and a stove, right behind the engine.</summary>
    public int UtilityCars { get; init; }
    /// <summary>Cars converted to armour (GDD §26 "reinforced plating, heavier mass"), from the rear forward.</summary>
    public int ArmouredCars { get; init; }
    /// <summary>Cars that stay cargo whatever's bought: a conversion that would leave fewer isn't made.</summary>
    public int MinCargoCars { get; init; } = 1;
    /// <summary>What an armoured car's plate weighs on top of the car.</summary>
    public double ArmourTonnes { get; init; } = 10;
    /// <summary>How much of a blow to its shell an armoured car takes (the plate turns the rest).</summary>
    public double ArmourDamage { get; init; } = 0.5;
    /// <summary>A crew car's stove warms this far round it with a door open; shut in, the whole car's warm.</summary>
    public double StoveReach { get; init; } = 2.5;
    /// <summary>How fast the cold comes on in an unheated car, against player.json <c>cold.indoorsRate</c> (spec F.3 car insulation).</summary>
    public double Insulation { get; init; } = 1;
    /// <summary>Handrails along every roof's edges (spec F.3 roof handrails).</summary>
    public bool Handrails { get; init; }
    /// <summary>What holding them does.</summary>
    public HandrailTuning Rails { get; init; } = new();
    /// <summary>The powered switch thrower (spec F.3 "removes the ground excursion at junctions"; note 196), its lever in the cab.</summary>
    public bool SwitchThrower { get; init; }
    /// <summary>How far ahead it reaches, and how slow the train has to be.</summary>
    public ThrowerTuning Thrower { get; init; } = new();
}

/// <summary>
/// The powered switch thrower (spec F.3; note 196): held at its lever in the cab for route.json's <c>throwSeconds</c>, it
/// throws the next points ahead of the engine, as the stand beside them would.
/// </summary>
/// <param name="Reach">How far ahead of the engine's front the points can be (m).</param>
/// <param name="MaxSpeed">The train no faster than this (m/s), either way.</param>
public sealed record ThrowerTuning(double Reach = 200, double MaxSpeed = 5);

/// <summary>
/// Roof handrails (spec F.3 "Dragger resistance"; note 184). A hand on the rail: a Dragger has to reach further in for you,
/// holds you over the side longer before it has you (friends' time to haul you back), and a bend taken too fast has to
/// be taken faster still to throw you off.
/// </summary>
/// <param name="DraggerGrab">The Draggers' grab range, times this.</param>
/// <param name="DraggerHang">How long one hangs on to you before it has you, times this.</param>
/// <param name="ThrowOver">How far over a bend's limit throws you off the roof (sight.json <c>throwOver</c>), times this.</param>
/// <param name="Wind">The wind's push on the roofs (GDD §22, player.json <c>wind</c>; note 201), times this.</param>
public sealed record HandrailTuning(double DraggerGrab = 0.6, double DraggerHang = 1.5, double ThrowOver = 1.5, double Wind = 0.4);

/// <summary>The train's kit from the fortress (train.json <c>kit</c>).</summary>
public sealed record KitTuning
{
    public int Radios { get; init; }
    /// <summary>GDD §23 "radio breaks" (note 183): the chance a radio on your belt smashes, per point of damage you take.</summary>
    public double RadioBreakPerDamage { get; init; } = 0.006;
    /// <summary>... and when something grabs you.</summary>
    public double RadioBreakOnGrab { get; init; } = 0.25;
    /// <summary>
    /// Note 200: how long Use is held with the repair kit in hand to mend a broken radio, worn or in reach (s).
    /// </summary>
    public double RadioMendSeconds { get; init; } = 8;
    /// <summary>
    /// Spec A.5 "dies in tunnels and mine spurs": how far into one, from a tunnel's mouth or a mine spur's points, a radio
    /// still carries (m). Spec F.3's radio range adds to it (note 196).
    /// </summary>
    public double RadioReach { get; init; }
    /// <summary>Toys in the guard van (GDD v1.1 App. C.4): hand loot, what the Track Doll will leave for.</summary>
    public int Toys { get; init; }
    /// <summary>
    /// What the guard van's toys sound like, in the order they're stocked (App. C.7: "some toys are noisy"); a toy past the
    /// list's end is quiet.
    /// </summary>
    public IReadOnlyList<Physics.ToyNoise> ToyNoises { get; init; } = [];
    /// <summary>
    /// Repair kits (GDD §12): the toolbox that mends a ruptured boiler and opens a Holdout's lock quietly (App. D.7), in
    /// <see cref="RepairKitCar"/>.
    /// </summary>
    public int RepairKits { get; init; }
    /// <summary>The car the repair kit rides in, counted back from the engine (1: the first car behind the tender).</summary>
    public int RepairKitCar { get; init; } = 1;
    /// <summary>
    /// Spare repair kits bought at the fortress (GDD v1.4 App. E.12 question 4, answered): stocked beside the first, in the
    /// lockers. The campaign sets it (<c>SessionSetup.SpareKits</c>).
    /// </summary>
    public int SpareKits { get; init; }
    /// <summary>
    /// Spare lamps and extinguishers bought at the fortress for the night (GDD §9 "stock ... lamps"; campaign.json <c>stores</c>,
    /// note 182): in the guard van beside its own lamp. The campaign sets them (<c>SessionSetup</c>).
    /// </summary>
    public int SpareLamps { get; init; }
    public int SpareExtinguishers { get; init; }
    /// <summary>The crew lockers in the kit's car (ARCHITECTURE §8 note 173). Unset, the car has none.</summary>
    public LockerTuning? Lockers { get; init; }
}

/// <summary>The crew lockers (train.json <c>kit.lockers</c>). Field docs live in that file.</summary>
public sealed record LockerTuning
{
    public IReadOnlyList<string> Names { get; init; } = [];
    public string KitLocker { get; init; } = "";
    public int Slots { get; init; } = 2;
    public IReadOnlyList<Physics.BodyKind> Holds { get; init; } = [];
    public double Width { get; init; } = 0.42;
    public double Depth { get; init; } = 0.5;
    public double Height { get; init; } = 1.9;
    public double FromFront { get; init; } = 0.7;
    public double DoorSeconds { get; init; } = 0.4;
}

public sealed record GeometryTuning(
    double CarLength, double CouplingGap, double EngineLength, double RoofWidth, double RoofSafeCentreline,
    double CarHeight, double EngineHeight, double CouplerHeight, double CouplerWidth, double LadderInset,
    EngineLayout Engine, InteriorLayout? Interior = null)
{
    /// <summary>How far the guard van's rear platform stands out behind it (GDD §24 THE WEIGHT's "rear platform").</summary>
    public double PlatformDepth { get; init; } = 1.2;

    /// <summary>The one doorway, on the train and off it (train.json <c>doorway</c>).</summary>
    public DoorwayTuning Doorway { get; init; } = new();

    /// <summary>
    /// Where the coupler plate's centre line is across the car: in line with the end doors it bridges between, so you step
    /// straight off it through a doorway. Solid greybox cars have no doors, and their plate stays on the centre line.
    /// </summary>
    public double PlateX => Interior?.DoorX ?? 0;

    /// <summary>The end ladders' line: right of the plate, their stiles just clear of its edge (the ladder is 0.4 m across).</summary>
    public double EndLadderX => PlateX + CouplerWidth / 2 + 0.3;
}

/// <summary>
/// The standard doorway (ARCHITECTURE §8 note 110): every door a person walks through is <see cref="Height"/> tall,
/// floor to lintel; on the train, <see cref="Width"/> wide (a cargo car's sliding doors are wider, for crates). Big doors
/// (sheds, barns, churches: drawn only) are all <see cref="BayHeight"/>.
/// </summary>
public sealed record DoorwayTuning
{
    public double Height { get; init; } = 2.1;
    public double Width { get; init; } = 0.9;
    public double BayHeight { get; init; } = 3.5;
}

/// <summary>Walk-in cars (GDD §10, §26): a floor, walls, a roof you can still walk on, and a door at each end.</summary>
public sealed record InteriorLayout(double FloorHeight, double WallThickness, double RoofThickness, double DoorX,
    double DoorSeconds, double CargoDepth, double CargoHeight)
{
    /// <summary>A cargo car's sliding side doors (one each side, in the middle), for loading from the ground (spec D.2).</summary>
    public double SideDoorWidth { get; init; } = 1.8;
    /// <summary>The steps up to each side door: how far out from the car side, and how long each tread is.</summary>
    public double StepWidth { get; init; } = 0.6;
    public double StepDepth { get; init; } = 0.4;
    /// <summary>
    /// A cargo car's roof hatch (T99 playtest: "a way to open the roofs of cars up so we can use the crane to lower crates
    /// in"): a lid across the roof between the walls, centred this far back and this long. Zero length is no hatch.
    /// </summary>
    public double HatchZ { get; init; } = 3.2;
    public double HatchLength { get; init; }
}

/// <summary>Greybox layout of the 20 m engine + tender unit, front to back: boiler, cab, tender.</summary>
public sealed record EngineLayout(double DeckHeight, double BoilerHalfWidth, double BoilerTop, double CabLength, double TenderLength, double TenderTop)
{
    /// <summary>The running boards: how far out past the cab side they stand (App. A.2 GREASE: "sanding from the running boards").</summary>
    public double RunningBoardWidth { get; init; } = 0.6;
    /// <summary>The sandboxes on the running boards: this far ahead of the cab front.</summary>
    public double SandboxAhead { get; init; } = 2.5;
    /// <summary>
    /// The gangway down the tender's left side, this wide at deck height, from its rear coupler into the back of the cab
    /// (T90: the crew had no way into the cab from the train). Zero is the old full-width tender.
    /// </summary>
    public double TenderGangway { get; init; }
}
public sealed record MassTuning(double EngineTonnes, double EmptyCarTonnes, double LoadedCarTonnes);
public sealed record PerformanceRow(int Cars, double Accel, double Brake);
public sealed record BrakeFadeTuning(double FadePerSecond, double RecoverPerSecond, double MinEfficiency, bool OnlyOnDescent);
/// <summary>Coupling, cutting and collision between rakes. Field docs live in train.json.</summary>
public sealed record CouplingTuning(double CoupleMaxSpeed, double SafeContactSpeed, double DamagePerSpeedSquared, double CargoDamageShare,
    double UncoupleSeconds, double UncoupleUnderLoadSeconds, double HandbrakeDecel, double ParkBelowSpeed, double HandbrakeSeconds,
    double UncoupleLookDownDegrees = 0);

/// <summary>Deceleration from rolling (m/s²) and air (per (m/s)²) resistance.</summary>
public sealed record ResistanceTuning(double Rolling, double Air);
public sealed record SpeedBandTuning(double Yard, double JumpOffLethal, double Slow, double WorkingMin, double Cruise);

/// <summary>
/// Line Plan §12.6 (note 181): the walkable corridor is <paramref name="CorridorM"/> either side of the track (where the line
/// generator keeps drop sides walkable) and no more than <paramref name="DropM"/> below the rails; a body or a kit at rest
/// beyond it is put back on the formation's edge, <paramref name="EdgeM"/> out from the track.
/// </summary>
/// <summary>train.json <c>fragile</c>: how coupling and brake shocks spoil a car of medicine (GDD §19). Field docs live in that file.</summary>
public sealed record FragileTuning(double SafeContactSpeed = 0.3, double ShockShare = 3, double BrakeShockDecel = 1.2, double BrakeShockPerSecond = 0.02);

public sealed record RecoveryTuning(double CorridorM = 40, double DropM = 15, double EdgeM = 3.5);
