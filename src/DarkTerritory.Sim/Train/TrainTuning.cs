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
    /// <summary>A car's shell given way, and boarding it up (train.json <c>breach</c>, spec B.9).</summary>
    public BreachTuning Breach { get; init; } = new();
    /// <summary>The wrench is the repair tool, Sea of Thieves style (train.json <c>repair</c>; queue #39, note 301).</summary>
    public RepairTuning Repair { get; init; } = new();
    /// <summary>A battered car or engine let go goes on to cost the night (train.json <c>failing</c>; note 576).</summary>
    public FailingTuning Failing { get; init; } = new();
    /// <summary>Line Plan §12.6, never an unrecoverable body or kit (train.json <c>recovery</c>; note 181).</summary>
    public RecoveryTuning Recovery { get; init; } = new();
    /// <summary>What the consist's made of past engine, cargo and guard van, and what its fittings do (train.json <c>composition</c>; note 184).</summary>
    public CompositionTuning Composition { get; init; } = new();
    /// <summary>GDD §19's fragile medicine (train.json <c>fragile</c>; note 182). Unset, medicine rides like anything else.</summary>
    public FragileTuning? Fragile { get; init; }
    /// <summary>Which of the things in reach a look picks (train.json <c>pick</c>; note 264).</summary>
    public PickTuning Pick { get; init; } = new();
    /// <summary>A bend taken too fast: its warning, its stress, and the lead it must give (train.json <c>overspeed</c>; note 265).</summary>
    public OverspeedTuning Overspeed { get; init; } = new();
    /// <summary>
    /// How far the engine's headlamp reaches, over the night's own ranges (train.json <c>headlampReach</c>; queue #243, note
    /// 506): the boards read (sight.json <c>lampSignRange</c>), the grease and the debris on the rail (<c>lampGreaseRange</c>),
    /// the Track Doll's face (enemies.json <c>lampRevealDistance</c>), the wreck yard's beam (facilities.json
    /// <c>wreck.beamLength</c>) and the drawn beam. 1 as the fortress issues it; spec F.3's lamp brightness raises it.
    /// </summary>
    public double HeadlampReach { get; init; } = 1;

    /// <summary>What running into a branch's buffer stop does (train.json <c>bufferStop</c>; note 661).</summary>
    public BufferStopTuning BufferStop { get; init; } = new();

    /// <summary>The engine's whistle on the cord (train.json <c>whistle</c>; note 839): how long it blows on once let go.</summary>
    public WhistleTuning Whistle { get; init; } = new();

    /// <summary>The handcar (train.json <c>handcar</c>; GDD §24.1, note 729): what it is and how it runs, when the night has one.</summary>
    public HandcarTuning Handcar { get; init; } = new();

    /// <summary>The night has a handcar (note 729): bought (campaign.json's <c>handcar</c> upgrade) or <c>handcar.always</c>.</summary>
    public bool HasHandcar => Composition.Handcar || Handcar.Always;

    public const string File = "tuning/train.json";
}

/// <summary>
/// Picking what Use works by where you look (note 264, the director's notes on build 1121: neighbouring lockers, and the
/// whistle cord over the firebox, fought over one press). Field docs live in train.json <c>pick</c>.
/// </summary>
/// <param name="EyeHeight">The eye over the feet (m), where the look is taken from.</param>
/// <param name="LookDegrees">A thing in reach within this of the view's centre is looked at; the nearest to it wins.</param>
public sealed record PickTuning(double EyeHeight = 1.6, double LookDegrees = 30)
{
    /// <summary>Note 664: the eye of a crouched player (player.json crouch.eyeDrop under <see cref="EyeHeight"/>).</summary>
    public double CrouchedEyeHeight { get; init; } = 1.05;
}
/// <summary>A bend taken too fast (note 265). Field docs live in train.json <c>overspeed</c>.</summary>
public sealed record OverspeedTuning
{
    public double LeadSeconds { get; init; } = 4;
    public double BrakeShare { get; init; } = 0.6;
    public double MarginM { get; init; } = 30;
    public double LurchAt { get; init; } = 0.5;
    public double RepeatSeconds { get; init; } = 1.5;
    public string WarningSound { get; init; } = "warn-overspeed";
    /// <summary>
    /// A dead line's buffers run into over this (m/s) and the train goes through them and off the end (the director's
    /// decision of 7 Oct 2026, note 286; <see cref="DeadEnds"/>), once this tuning's warning has been up its lead.
    /// </summary>
    public double DeadEndDerailAbove { get; init; } = 6.9;
    /// <summary>
    /// The cars lean out on a bend taken too fast (note 370): this far at the derailing speed (degrees), about the outer
    /// rail. Presentation only (DarkTerritory.Game's CarLean): the sim's frames never lean.
    /// </summary>
    public double LeanDegrees { get; init; } = 4;
    /// <summary>How long a car takes to settle into its lean (s, the time constant).</summary>
    public double LeanSeconds { get; init; } = 0.35;
    /// <summary>Past half way to derailing, a car rocks on its springs by this share of <see cref="LeanDegrees"/>.</summary>
    public double LeanRock { get; init; } = 0.18;

    /// <summary>
    /// How far ahead a demand at <paramref name="safe"/> must be told of to a train at <paramref name="speed"/> braking at
    /// <paramref name="rated"/> (the consist's rated deceleration, spec B.5): the lead at the speed now, a service stop down
    /// to it at <see cref="BrakeShare"/> of the rated brake, and the margin. Used for bends and for the Sleepers alike.
    /// </summary>
    public double WarnDistance(double speed, double safe, double rated, double leadSeconds) =>
        speed * leadSeconds + Math.Max(0, speed * speed - safe * safe) / (2 * Math.Max(0.05, rated * BrakeShare)) + MarginM;
}

/// <summary>Boarding up a breached car (decided 1 Oct; spec B.9). Field docs live in train.json <c>breach</c>.</summary>
public sealed record BreachTuning
{
    public double BoardSeconds { get; init; } = 8;
    public double BoardReach { get; init; } = 1.5;
    /// <summary>Boarding up wants the repair kit in hand (note 150: it's an item someone carries).</summary>
    public bool NeedsKit { get; init; }
}

/// <summary>
/// Mending with the wrench (the director, 7 Oct, GDD App. F.3; ARCHITECTURE §8 note 301). Field docs live in train.json
/// <c>repair</c>. Unset (<see cref="Wrench"/> false), the repair kit mends the boiler and anyone's hands board a breach, as
/// before.
/// </summary>
public sealed record RepairTuning
{
    public bool Wrench { get; init; }
    public double DentedBelow { get; init; } = 0.9;
    public double IntegrityPerSecond { get; init; } = 0.04;
    public double DentReach { get; init; } = 1.3;
    public double EngineDentBehind { get; init; } = 2.5;
    public double LampMendRate { get; init; } = 6;
    /// <summary>
    /// Note 752 (GDD §12, one can, two do it faster): each of two (or more) crewmates boarding up the same breach, or mending
    /// the same burst boiler, works it this many times as fast. 1 (or less): each alone, as before.
    /// </summary>
    public double PairRate { get; init; } = 1;
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
    /// <summary>Note 678, §8.8: the cab's glass is armour glass in riveted frames (the driver keeps full sight of the line); a Climber coming over the tender holds there for <c>climbers.cabGlassSeconds</c> first.</summary>
    public bool CabArmour { get; init; }
    /// <summary>Cars that stay cargo whatever's bought: a conversion that would leave fewer isn't made.</summary>
    public int MinCargoCars { get; init; } = 1;
    /// <summary>What an armoured car's plate weighs on top of the car.</summary>
    public double ArmourTonnes { get; init; } = 10;
    /// <summary>How much of a blow to its shell an armoured car takes (the plate turns the rest).</summary>
    public double ArmourDamage { get; init; } = 0.5;
    /// <summary>Handrails along every roof's edges (spec F.3 roof handrails).</summary>
    public bool Handrails { get; init; }
    /// <summary>What holding them does.</summary>
    public HandrailTuning Rails { get; init; } = new();
    /// <summary>The powered switch thrower (spec F.3 "removes the ground excursion at junctions"; note 196), its lever in the cab.</summary>
    public bool SwitchThrower { get; init; }
    /// <summary>How far ahead it reaches, and how slow the train has to be.</summary>
    public ThrowerTuning Thrower { get; init; } = new();
    /// <summary>The handcar (GDD §24.1, note 729), hung on the back of the guard van: campaign.json's <c>handcar</c> upgrade.</summary>
    public bool Handcar { get; init; }
    /// <summary>The BBQ deck on the engine's front (note 698): its chairs, its cooler and its grill.</summary>
    public bool BbqDeck { get; init; }
    /// <summary>The dining car (upgrades.md §8.5, note 959): a cargo car converted, its table laid and its bar, behind the crew cars.</summary>
    public bool DiningCar { get; init; }
    /// <summary>What its bar pours.</summary>
    public DiningTuning Dining { get; init; } = new();
    /// <summary>
    /// The train's dressing (note 701): the silly and the fun bought from the blacksmith that are only things on the train to
    /// see (fuzzy dice, flames, the hubcaps, the tiny caboose...), by campaign.json's ids (effect <c>dress:&lt;id&gt;</c>). The
    /// art draws each; none changes the sim but by its own effect beside it.
    /// </summary>
    public IReadOnlyList<string> Dress { get; init; } = [];

    /// <summary>The train's paint (note 714): 0 the works' rust and plate; 1 crimson lined gold, 2 forest green and brass, 3 black with a white stripe.</summary>
    public int Livery { get; init; }

    /// <summary>Whether <paramref name="id"/> is fitted (<see cref="Dress"/>).</summary>
    public bool Dressed(string id) => Dress.Contains(id);
    /// <summary>What's on it, and what each does.</summary>
    public BbqTuning Bbq { get; init; } = new();
    /// <summary>The tiny caboose's string and size (note 713).</summary>
    public CabooseTuning Caboose { get; init; } = new();
    /// <summary>The whimsies' numbers (note 715): Gerald, the gnomes, the canary, the gramophone, the calliope.</summary>
    public WhimsyTuning Whimsy { get; init; } = new();
}

/// <summary>The whimsies (train.json <c>composition.whimsy</c>; ARCHITECTURE §8 note 715). Field docs live in that file.</summary>
public sealed record WhimsyTuning(int Gnomes = 5, int CanaryCars = 1, double CanaryGasM = 10, double CanaryFumesSeconds = 20,
    double GramophoneHears = 22, double GramophoneLoudness = 0.25, double CalliopeHears = 80, double TubSteam = 0.15,
    double FishReach = 4, double[]? FishSeconds = null, int FishHeals = 40, double CatSpeed = 1.1, double DogSpeed = 1.8, double[]? Wander = null,
    int CatWarnCars = 1, double DogHears = 40, double Fetch = 0.45, double Drop = 1.2, double LaunchSpeed = 14, double LaunchEvery = 2,
    double BallSeen = 70, double BallTake = 1.5, double BallWorry = 4)
{
    /// <summary>How long the cat and the dog stay put before wandering on (s), from the first to the second.</summary>
    public double[] Idle => Wander ?? [4, 10];

    /// <summary>How long a line in the water waits for a bite (s), from the first to the second, by the tick it's cast.</summary>
    public double[] Bite => FishSeconds ?? [12, 30];
}

/// <summary>The tiny caboose (train.json <c>composition.caboose</c>; ARCHITECTURE §8 note 713). Field docs live in that file.</summary>
public sealed record CabooseTuning(double Rope = 1.4, double Length = 0.36, double Radius = 0.1, double CouplerHeight = 1.0, double DerailAccel = 0.8,
    double Friction = 0.15, double DraggedFriction = 0.6, double Reach = 2.0);

/// <summary>The dining car's bar (train.json <c>composition.dining</c>; ARCHITECTURE §8 note 959). Field docs live in that file.</summary>
public sealed record DiningTuning
{
    public int Drinks { get; init; } = 8;
    public int DrinkHeals { get; init; }
}

/// <summary>The BBQ deck (train.json <c>composition.bbq</c>; ARCHITECTURE §8 note 698). Field docs live in that file.</summary>
public sealed record BbqTuning
{
    public double DeckLength { get; init; } = 3.0;
    public double RailHeight { get; init; } = 1.0;
    public int Beers { get; init; } = 24;
    public int BeerHeals { get; init; }
    public double LightSeconds { get; init; } = 0.6;
    public double BurnSeconds { get; init; } = 300;
    public double GrillSeconds { get; init; } = 6;
    public int SausageHeals { get; init; } = 20;
    public double GrillReach { get; init; } = 2.0;
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
    /// <summary>The loose radio sets the train leaves with (T41): one in the cab, the rest in the guard van. None with <see cref="RadioEach"/>.</summary>
    public int Radios { get; init; }
    /// <summary>
    /// Note 749 (GDD §12; the director, 14 Oct 2026: "make the radios part of the default kit"; "Radio is part of uniform and
    /// doesnt take inventory space"): every living crewmate wears a radio from the start of the night, a joiner and one back
    /// from a Holdout too, and it never comes off the belt. Off, the train carries <see cref="Radios"/> loose sets as before.
    /// </summary>
    public bool RadioEach { get; init; }
    /// <summary>Note 266: the forward lamp a Climber smashed comes back lit once its glass is in (train.json kit).</summary>
    public bool RelightSmashedLamp { get; init; } = true;
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
    /// <summary>Note 678's grab irons: how many times player.json's <c>ladder.grabMaxRelativeSpeed</c> a ladder can be caught at, 1 as built.</summary>
    public double GrabIrons { get; init; } = 1;
    /// <summary>
    /// Note 978 (upgrades.md §6's speaking tubes; GDD App. C.8): a speaking tube's horn in every car with a room, joined to
    /// the next car's either way along the train, as well as the cab's to the guard van's (<see cref="SpeakingTube"/>). The
    /// campaign sets it (effect <c>speakingTubes</c>).
    /// </summary>
    public bool SpeakingTubes { get; init; }
    /// <summary>
    /// The band's instruments the blacksmith sold the crew (note 700; band.json <c>instruments</c> ids), in the guard van; the
    /// campaign sets it (effect <c>instrument:&lt;id&gt;</c>).
    /// </summary>
    public IReadOnlyList<string> Instruments { get; init; } = [];
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
    /// <summary>
    /// Flare pistols bought at the fortress for the night (note 871; campaign.json <c>stores.flares</c>): in the guard van, each
    /// loaded with <see cref="FlareShots"/> flares. The campaign sets them (<c>SessionSetup</c>).
    /// </summary>
    public int FlarePistols { get; init; }
    public int FlareShots { get; init; } = 3;
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
    /// <summary>
    /// What each locker starts the night with besides the repair kit (note 264), by its name. Looked up locker by locker in
    /// the row's order, never iterated.
    /// </summary>
    public Dictionary<string, Physics.BodyKind[]> Stock { get; init; } = new(StringComparer.OrdinalIgnoreCase);
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

/// <summary>
/// Greybox layout of the 20 m engine, cab forward (note 276), front to back: the pilot, the cab (everything at its front,
/// facing forward, run by one: the coal bunker, the firebox and the driver's console across it under the windows, note
/// 280), the boiler with the stack at the rear.
/// </summary>
public sealed record EngineLayout(double DeckHeight, double BoilerHalfWidth, double BoilerTop, double CabLength)
{
    /// <summary>The pilot ahead of the cab: buffer beam, plough and headlamp.</summary>
    public double PilotLength { get; init; } = 0.6;
    /// <summary>The coal bunker in the cab (note 280): in its front left corner, back along the left wall this long.</summary>
    public double BunkerLength { get; init; } = 1.8;
    /// <summary>How far in from the cab's left wall the bunker reaches.</summary>
    public double BunkerDepth { get; init; } = 0.62;
    /// <summary>
    /// The firebox (note 280): against the cab's front wall beside the bunker, under the windows, its door facing back into
    /// the cab: this wide, this deep from the front wall, this high over the floor (under the sill, out of the line's view).
    /// </summary>
    public double FireboxWidth { get; init; } = 0.96;
    public double FireboxDepth { get; init; } = 0.7;
    public double FireboxHeight { get; init; } = 1.05;
    /// <summary>The bunker's top over the cab floor.</summary>
    public double BunkerHeight { get; init; } = 1.0;
    /// <summary>The running boards: how far out past the body's sides they stand (App. A.2 GREASE: "sanding from the running boards").</summary>
    public double RunningBoardWidth { get; init; } = 0.85;
    /// <summary>How far ahead of the cab's doorways the running boards run on (note 559: "in front of the cab doors").</summary>
    public double RunningBoardAhead { get; init; } = 1.0;
    /// <summary>The sandboxes on the running boards: this far behind the cab, over the drivers.</summary>
    public double SandboxBehind { get; init; } = 2.5;
    /// <summary>
    /// The cab's side doors at its front (note 856): the left one's opening this far behind the cab's front (beside the coal),
    /// the right one's; 0 is none. Each the standard doorway's size, a landing <see cref="SideDoorLanding"/> past it either side.
    /// </summary>
    public double CoalDoorFrom { get; init; }
    public double FrontDoorFrom { get; init; }
    public double SideDoorLanding { get; init; } = 0.3;
    /// <summary>The big cab behind the footplate (ARCHITECTURE §8 note 716): the chart table, the gallery, the back wall.</summary>
    public CrewCarLayout CrewCar { get; init; } = new();
}

/// <summary>
/// The cab as the crew car (ARCHITECTURE §8 note 716; the director, 14 Oct 2026: "the cab itself becomes the crew car"),
/// train.json <c>geometry.engine.crewCar</c>. Every place is how far behind the cab's front (m). Field docs live in that file.
/// </summary>
public sealed record CrewCarLayout
{
    public double FootplateLength { get; init; } = 2.5;
    public double TableFrom { get; init; } = 2.9;
    public double TableLength { get; init; } = 1.75;
    public double TableWidth { get; init; } = 0.9;
    public double TableHeight { get; init; } = 1.0;
    public double GalleryFrom { get; init; } = 5.1;
    public double GalleryTo { get; init; } = 8.4;
    public double GalleryRise { get; init; } = 0.45;
    public double GalleryStep { get; init; } = 0.45;
    public double GalleryWaist { get; init; } = 1.1;
    public double BenchFrom { get; init; } = 5.15;
    public double BenchTo { get; init; } = 8.35;
    public double BenchDepth { get; init; } = 0.175;
    public double BenchHeight { get; init; } = 0.45;
    public int BenchSeats { get; init; } = 4;
    public double SeatEvery { get; init; } = 0.7;
    public double KettleFrom { get; init; } = 10;
    public double CuckooEvery { get; init; } = 60;
    public double CuckooOutSeconds { get; init; } = 3;
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

/// <summary>train.json <c>bufferStop</c> (note 661): a buffer stop's knock. Field docs live in that file.</summary>
public sealed record BufferStopTuning(double SafeSpeed = 2.5, double DamagePerSpeedSquared = 0.02);

public sealed record RecoveryTuning(double CorridorM = 40, double DropM = 15, double EdgeM = 3.5);

/// <summary>
/// The engine's whistle on the cord (train.json <c>whistle</c>; GDD §12, note 839; the director, 16 Oct 2026: "while I'm holding
/// the key, I am holding the whistle pull. And then when I let go of the key, it immediately stops the whistle. That way we can
/// actually do the longs and the shorts"). Held, it blows; let go, it blows on only <see cref="TailSeconds"/>, the steam off the
/// valve, so the whistle codes' shorts and longs come out as blown. (It was a second, so two shorts ran into one long.)
/// </summary>
public sealed record WhistleTuning
{
    public double TailSeconds { get; init; } = 0.12;
}
