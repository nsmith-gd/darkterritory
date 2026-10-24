using Ballast;

namespace DarkTerritory.Sim.Train;

/// <summary>
/// A car's local coordinate frame: +X right, +Y up, −Z towards the front of the train, origin at
/// rail height under the car's centre (ARCHITECTURE §6.1). Anything standing on a car lives here,
/// so the train can do 22 m/s round a curve without the people on it noticing.
/// </summary>
public readonly record struct CarFrame(int Index, Double3 Origin, Double3 Right, Double3 Up, Double3 Back, Double3 Velocity, CarShape Shape)
{
    public static CarFrame From(in CarPose pose, double trainVelocity, CarShape shape) =>
        new(pose.Index, pose.Centre, pose.Right, pose.Up, pose.Forward * -1, pose.Forward * trainVelocity, shape);

    public Double3 ToWorld(Double3 local) => Origin + Right * local.X + Up * local.Y + Back * local.Z;
    public Double3 ToLocal(Double3 world)
    {
        var d = world - Origin;
        return new(Double3.Dot(d, Right), Double3.Dot(d, Up), Double3.Dot(d, Back));
    }

    public Double3 DirToWorld(Double3 local) => Right * local.X + Up * local.Y + Back * local.Z;
    public Double3 DirToLocal(Double3 world) => new(Double3.Dot(world, Right), Double3.Dot(world, Up), Double3.Dot(world, Back));

    /// <summary>World velocity of a body moving at <paramref name="local"/> relative to this car.</summary>
    public Double3 VelocityToWorld(Double3 local) => DirToWorld(local) + Velocity;
    public Double3 VelocityToLocal(Double3 world) => DirToLocal(world - Velocity);

    /// <summary>Heading of the car's front in radians; 0 faces world −Z, positive turns left.</summary>
    public double Heading => DMath.Atan2(Back.X, Back.Z);
}

public readonly record struct Box(Double3 Min, Double3 Max)
{
    public bool ContainsXZ(Double3 p) => p.X >= Min.X && p.X <= Max.X && p.Z >= Min.Z && p.Z <= Max.Z;
    public bool Contains(Double3 p) => ContainsXZ(p) && p.Y >= Min.Y && p.Y <= Max.Y;
    public Double3 Centre => (Min + Max) * 0.5;
    public Double3 HalfSize => (Max - Min) * 0.5;

    public static Box FromCentre(Double3 centre, Double3 half) => new(centre - half, centre + half);
}

/// <summary>What standing on top of a solid means: roof speeds and wind (spec B.2), or ordinary footing.</summary>
public enum SurfaceKind : byte { Roof, Deck, Coupler }

/// <summary>What a solid is, so presentation can draw and colour it. Collision ignores this, but for an open roof hatch (T99).</summary>
/// <summary><see cref="CrewLocker"/> is one of the kit car's row of crew lockers (ARCHITECTURE §8 note 173); <see cref="Locker"/> the guard van's tool locker.</summary>
/// <summary><see cref="Stove"/> and <see cref="Bunk"/> are a crew car's (note 184): its stove, and the berths down its right side.</summary>
/// <summary>
/// <see cref="ChartTable"/>, <see cref="Gallery"/> (its raised floor and treads) and <see cref="Bench"/> are the big cab's, the
/// crew car's (note 716); <see cref="BbqDeck"/>, <see cref="Rail"/>, <see cref="Cooler"/> and <see cref="Grill"/> are the BBQ
/// deck's (note 698).
/// </summary>
public enum PartKind : byte
{
    Body, Chassis, Boiler, Stack, CabWall, CabRoof, Tender, Coupler, GunMount, Wall, Cargo, Locker, Steps, RunningBoard, Hatch, CrewLocker, Stove, Bunk, Firebox,
    ChartTable, Gallery, Bench,
    BbqDeck, Rail, Cooler, Grill,
    /// <summary>A piece of furniture the art draws as itself (the gramophone's table, note 715): solid, never drawn as a box.</summary>
    Furniture,
    /// <summary>A tread of the ramp up the engine's coupler plate from car 1's floor to its deck (note 905), drawn as one slope.</summary>
    Ramp
}

/// <summary>Where a gun is bolted on, and which way it faces in the car's frame (−Z forward, +Z back).</summary>
/// <summary>
/// Where a gun is and which way it faces, in its car's frame: <see cref="CarShape.Gun"/> is where it stands at departure;
/// where it is now is the vehicle's (<see cref="Combat.Guns.Mount"/>, T93).
/// </summary>
public readonly record struct GunMount(Double3 Position, Double3 Facing);

public readonly record struct Solid(Box Box, SurfaceKind Top, PartKind Part)
{
    /// <summary>
    /// There to stand on and bump into on this vehicle now: everything but an open roof hatch's lid (T99), and a cargo car's
    /// heap when nothing's been poured in (note 659).
    /// </summary>
    public bool Present(Vehicle v) => Part switch
    {
        PartKind.Hatch => !v.DoorOpen(CarShape.HatchBit),
        PartKind.Cargo => v.Bulk > HeapFrom,
        _ => true,
    };

    /// <summary>
    /// Where it is on this vehicle now: a cargo car's heap (note 659: what was poured, tipped or driven in, <see cref="Vehicle.Bulk"/>)
    /// as high up its load side as the car's that full; everything else where it's built.
    /// </summary>
    public Box BoxIn(Vehicle v) => Part != PartKind.Cargo ? Box
        : new Box(Box.Min, Box.Max with { Y = Box.Min.Y + (Box.Max.Y - Box.Min.Y) * Math.Min(1, v.Bulk) });

    /// <summary>Under this much of a load poured in, there's no heap to speak of (a few shovelfuls).</summary>
    public const double HeapFrom = 0.02;
}

/// <summary>
/// A ladder fixed to a face: its foot, how high it goes, and which way is "onto" what it serves. <see cref="Over"/> is how
/// much further than a step past its top its climber is set down: the engine's cab steps hang off the running board's outer
/// edge (note 559) and still come up into the cab, across the board.
/// </summary>
public readonly record struct Ladder(Double3 Foot, double Top, Double3 Inward, double Over = 0);

/// <summary>
/// A ladder standing in the world, not on a car (note 973: a houseboat's, leant up against its bow): its foot on the ground
/// and its top at the deck's edge, in world coordinates; which way is in over the top (level); and how far in past the top
/// a climber steps off. Grabbed and climbed as a car's is (<c>PlayerMotor</c>), the climber up along it as it leans.
/// </summary>
public readonly record struct WorldLadder(Double3 Foot, Double3 Top, Double3 Inward, double Onto)
{
    /// <summary>The point on it at height <paramref name="y"/> (world), held between its foot and its top.</summary>
    public Double3 At(double y)
    {
        double rise = Top.Y - Foot.Y;
        double t = rise > 1e-6 ? Math.Clamp((y - Foot.Y) / rise, 0, 1) : 0;
        return Foot + (Top - Foot) * t;
    }
}

/// <summary>
/// <see cref="Coal"/> is the tender's coal face, where a hand fills the shovel (T29). <see cref="Locker"/> is a crew locker's
/// door (its <see cref="Interactable.Index"/> the locker's), worked like a car door and wanting facing as one does.
/// </summary>
/// <remarks>
/// <see cref="Points"/> is the powered switch thrower's lever in the cab (spec F.3; note 196): it works only fitted.
/// <see cref="Whistle"/> is the whistle cord's handle (GDD §12; note 264): Use held there blows the engine's whistle, and
/// only when it's looked at (<see cref="CrewActions.NearestInteractable"/>), so nobody pulls it reaching for the firebox.
/// </remarks>
/// <remarks>
/// <see cref="Chair"/>, <see cref="Cooler"/> and <see cref="Grill"/> are on the BBQ deck (note 698): a Muskoka chair to sit in
/// (<see cref="Interactable.Index"/> which), the cooler of beers, the BBQ on its stand.
/// </remarks>
public enum InteractableKind : byte { Firebox, Vent, Handbrake, Door, Coal, Sandbox, Hatch, ToolRack, Locker, Points, Whistle, Chair, Cooler, Grill, Gramophone, Hammock, HotTub, Launcher, Bench, Bar }

/// <summary>
/// A thing a player uses by standing near it and holding Use. <see cref="Index"/> says which door. <see cref="Aim"/> is how
/// far over <see cref="Position"/> (its footing) the thing itself is, the point a look picks it by (note 264): where two
/// are in reach, the one looked at is the one worked. <see cref="LookDegrees"/>, when not 0: worked only with the view pitched
/// at least that far (up, or down when negative), and then it's the one looked at (note 675: a roof hatch's lid).
/// </summary>
public readonly record struct Interactable(InteractableKind Kind, Double3 Position, double Radius, int Index = 0, double Aim = 1.0,
    double LookDegrees = 0);

/// <summary>
/// Where the driver's hands go in the engine's cab (T29): the regulator's handle on the backhead, the brake valve's and
/// the reverser's, in the engine's frame. A headset player works them by hand; a keyboard's keys do the same thing.
/// </summary>
/// <remarks>Each is given at rest; the handles move with the controls (<see cref="RegulatorAt"/>...), back being +Z.</remarks>
public readonly record struct CabLevers(Double3 Regulator, Double3 Brake, Double3 Reverser)
{
    /// <summary>How far back the regulator's handle comes, shut to wide open.</summary>
    public const double RegulatorTravel = 0.3;
    /// <summary>How far back the brake handle comes, off to full.</summary>
    public const double BrakeTravel = 0.12;
    /// <summary>How far the reverser throws from mid gear: forward for ahead, back for reverse.</summary>
    public const double ReverserThrow = 0.15;

    public Double3 RegulatorAt(double throttle) => Regulator + new Double3(0, 0, RegulatorTravel * throttle);
    public Double3 BrakeAt(double brake) => Brake + new Double3(0, 0, BrakeTravel * brake);
    public Double3 ReverserAt(int reverser) => Reverser + new Double3(0, 0, -ReverserThrow * Math.Sign(reverser));
}

/// <summary>
/// One crew locker (ARCHITECTURE §8 note 173), in its car's frame: the cabinet's box against the wall (a solid: you bump
/// into it, things lie against it), its door on the face towards the aisle, hinged at its front edge. <see cref="Index"/>
/// is its bit in <see cref="Vehicle.LockersOpen"/> and its name's place in train.json's <c>kit.lockers.names</c>.
/// </summary>
public readonly record struct LockerBay(int Index, Box Box, string Name)
{
    /// <summary>The door's side of the cabinet: +1 when it opens towards +X (a locker on the left wall), −1 on the right.</summary>
    public int Facing => Box.Min.X < 0 ? 1 : -1;
    /// <summary>The middle of the door's face, at the cabinet's foot.</summary>
    public Double3 Front => new(Facing > 0 ? Box.Max.X : Box.Min.X, Box.Min.Y, (Box.Min.Z + Box.Max.Z) / 2);
}

/// <summary>
/// The BBQ deck's things in the engine's frame (note 698): each chair's seat (where its sitter's feet are; they face down
/// the line, −Z), the cooler's middle and the grill's, on the deck; and the deck's front edge (Z).
/// </summary>
public sealed record BbqLayout(IReadOnlyList<Double3> Chairs, Double3 Cooler, Double3 Grill, double Front);

/// <summary>
/// The dining car's room (upgrades.md §8.5, note 959), in its frame: the table laid down the front half, and the pub's bar
/// down the rear half (note 1016): its long <see cref="Counter"/> along the right, its <see cref="Return"/> closing the
/// counter's front end to the wall, the barman's walkway <see cref="Behind"/> it (in from the rear end) and the
/// <see cref="BackBar"/> along the wall behind that; and behind the counter, where you stand to pour, the <see cref="Bar"/>
/// (the bar's <see cref="InteractableKind.Bar"/>). Its stools are the car's <see cref="CarShape.Lounges"/>
/// (<see cref="LoungeKind.Stool"/>), down the counter's front.
/// </summary>
public sealed record DiningLayout(Box Table, Box Counter, Double3 Bar, Box Return, Box Behind, Box BackBar);

/// <summary>A whimsy's place to lie or soak (note 744): the hammock, the hot tub; a big cab's bench seat (note 905); or a dining car's bar stool (note 1016).</summary>
public enum LoungeKind : byte { Hammock, HotTub, Bench, Stool }

/// <summary>
/// Somewhere to lounge that isn't a Muskoka chair (note 744): where a crewmate's set in it (their feet's place in the car's
/// frame) and the way they face (a player's yaw: −Z at 0).
/// </summary>
public readonly record struct Lounge(LoungeKind Kind, Double3 At, double Yaw);

/// <summary>
/// A door: solid while shut. <see cref="Index"/> is its bit in <see cref="Vehicle.DoorsOpen"/>. Drawn open, a side door's leaf
/// steps <see cref="Out"/> m out from its wall and slides its own width along it, back (+Z, <see cref="Slide"/> 1) or forward
/// (−1): the engine's cab side doors (note 856) slide clear of the cab's rail and away from its windows.
/// </summary>
public readonly record struct Door(Box Box, int Index, int Slide = 1, double Out = 0.12);

/// <summary>
/// Greybox collision for one car in its own frame: solids to stand on and bump into, ladders,
/// interactables, and (on the engine) the cab volume that makes a player the crew in charge.
/// </summary>
public sealed record CarShape(Box Bounds, IReadOnlyList<Solid> Solids, IReadOnlyList<Ladder> Ladders, IReadOnlyList<Interactable> Interactables, Box? Cab,
    GunMount? Gun = null, Box? Interior = null, IReadOnlyList<Door>? Doors = null, CabLevers? Levers = null)
{
    public IReadOnlyList<Door> DoorList => Doors ?? [];
    /// <summary>The crew lockers along a wall (ARCHITECTURE §8 note 173): only the repair kit's car has them.</summary>
    public IReadOnlyList<LockerBay> Lockers { get; init; } = [];
    /// <summary>Shelves in each crew locker (train.json kit.lockers.slots).</summary>
    public int LockerShelves { get; init; }
    /// <summary>The repair kit's locker in <see cref="Lockers"/> (kit.lockers.kitLocker: the fitter's), or −1 without lockers.</summary>
    public int KitLocker { get; init; } = -1;
    /// <summary>The guard van's rear platform, when it's last in the train (its top is the footing); null on anything else.</summary>
    public Box? Platform { get; init; }
    /// <summary>
    /// The gun rail along the roof's centreline (T93): from its front end to its back, in local Z. A gun slides along it,
    /// and over the coupling onto the next car's while the two are coupled.
    /// </summary>
    public (double Front, double Back)? RoofRail { get; init; }
    /// <summary>
    /// A cargo car's roof hatch (T99): the opening in the roof its lid (the <see cref="PartKind.Hatch"/> solid) shuts, from
    /// the roof's underside to its top. Open is bit <see cref="HatchBit"/> of <see cref="Vehicle.DoorsOpen"/>.
    /// </summary>
    public Box? Hatch { get; init; }
    /// <summary>The hatch's bit in <see cref="Vehicle.DoorsOpen"/>: after the four doors.</summary>
    public const int HatchBit = 4;
    /// <summary>How steeply a crewmate looks down at the hatch's lid from the roof, or up at it from the floor, to work it (note 675).</summary>
    public const double HatchLookDegrees = 35;
    /// <summary>A crew car's stove (note 184), standing on its floor: the heat a crew car has of its own.</summary>
    public Box? Stove { get; init; }
    /// <summary>The BBQ deck on the engine's front (note 698), where it's fitted: its chairs, its cooler and its grill.</summary>
    public BbqLayout? Bbq { get; init; }
    /// <summary>The dining car's table and bar (note 959), where it's the dining car.</summary>
    public DiningLayout? Dining { get; init; }
    /// <summary>The hammock and the hot tub's seats, where fitted (note 744); a <see cref="InteractableKind.Hammock"/> or <see cref="InteractableKind.HotTub"/>'s Index is one.</summary>
    public IReadOnlyList<Lounge> Lounges { get; init; } = [];

    /// <summary>
    /// The middle of the engine's footplate, the work at the cab's front (note 716): where the 5 m cab's middle was, and what
    /// comes into the cab comes down onto (a Climber over the roof, the Lamplighter at the brake). The crew car behind it is
    /// the crew's. On anything but the engine, the origin.
    /// </summary>
    public Double3 FootplateMiddle => Cab is { } c ? c.Centre with { Z = Math.Min(c.Centre.Z, c.Min.Z + FootplateHalf) } : default;

    /// <summary>Half the footplate's length (m): half the cab it was before note 716.</summary>
    public const double FootplateHalf = 2.5;

    public double HalfLength => Bounds.Max.Z;
    public double HalfWidth => Bounds.Max.X;
    /// <summary>Height of the highest walkable roof.</summary>
    public double RoofHeight => Bounds.Max.Y;

    /// <summary>The highest walkable surface over a point in the car's frame (at or below <paramref name="maxY"/>), and what kind it is.</summary>
    /// <summary>
    /// The highest footing at (x, z) no higher than <paramref name="maxY"/> as it is on <paramref name="v"/> now: a cargo car's
    /// heap only as high as what's poured in, and not at all when nothing has been (<see cref="Solid.Present"/>). Note 708: a
    /// casting set down through a hatch came to rest on the empty car's heap at its full height, in the air.
    /// </summary>
    public (double Top, SurfaceKind Kind)? TopAt(double x, double z, Vehicle v, double maxY = double.MaxValue)
    {
        (double, SurfaceKind)? best = null;
        var p = new Double3(x, 0, z);
        foreach (var solid in Solids)
            if (solid.Present(v) && solid.BoxIn(v) is var box && box.ContainsXZ(p) && box.Max.Y <= maxY + 1e-6 && (best is null || box.Max.Y > best.Value.Item1))
                best = (box.Max.Y, solid.Top);
        return best;
    }

    public (double Top, SurfaceKind Kind)? TopAt(double x, double z, double maxY = double.MaxValue)
    {
        (double, SurfaceKind)? best = null;
        var p = new Double3(x, 0, z);
        foreach (var solid in Solids)
            if (solid.Box.ContainsXZ(p) && solid.Box.Max.Y <= maxY + 1e-6 && (best is null || solid.Box.Max.Y > best.Value.Item1))
                best = (solid.Box.Max.Y, solid.Top);
        return best;
    }

    /// <param name="lockers">This car has the crew lockers (the repair kit's car, ARCHITECTURE §8 note 173).</param>
    /// <param name="dining">The dining car's bar (train.json <c>composition.dining</c>, note 1016); its defaults unset.</param>
    public static CarShape Build(GeometryTuning g, VehicleKind kind, bool hasCarBehind, LockerTuning? lockers = null, DiningTuning? dining = null) =>
        lockers is { Names.Count: > 0 } && kind != VehicleKind.Engine && g.Interior is not null
            ? WithLockers(Build(g, kind, hasCarBehind, dining), lockers) : Build(g, kind, hasCarBehind, dining);

    public static CarShape Build(GeometryTuning g, VehicleKind kind, bool hasCarBehind, DiningTuning? dining) => kind switch
    {
        // The engine's rail is the cab roof's (T93; note 276): the gun stands at its front, and behind it is the boiler.
        VehicleKind.Engine => Engine(g, hasCarBehind) with
        {
            RoofRail = (EnginePlan.Of(g).CabFront + RailEnd, EnginePlan.Of(g).CabBack - RailEnd),
        },
        VehicleKind.Guard => Guard(g, hasCarBehind) with { RoofRail = (-g.CarLength / 2 + RailEnd, g.CarLength / 2 - RailEnd) },
        VehicleKind.Utility => Utility(g, hasCarBehind) with { RoofRail = (-g.CarLength / 2 + RailEnd, g.CarLength / 2 - RailEnd) },
        VehicleKind.Dining => DiningRoom(g, dining ?? new(), hasCarBehind) with { RoofRail = (-g.CarLength / 2 + RailEnd, g.CarLength / 2 - RailEnd) },
        _ => Car(g, hasCarBehind) with { RoofRail = (-g.CarLength / 2 + RailEnd, g.CarLength / 2 - RailEnd) },
    };

    /// <summary>How far in from a roof's end its gun rail stops.</summary>
    const double RailEnd = 0.4;

    public static CarShape Build(GeometryTuning g, bool isEngine, bool hasCarBehind) =>
        Build(g, isEngine ? VehicleKind.Engine : VehicleKind.Cargo, hasCarBehind);

    /// <summary>A car with the rear gun on its roof, facing back down the line.</summary>
    static CarShape Guard(GeometryTuning g, bool hasCarBehind)
    {
        var car = g.Interior is { } layout ? Shell(g, layout, hasCarBehind, cargo: false) : SolidCar(g, hasCarBehind);
        double l = g.CarLength / 2, h = g.CarHeight;
        var mount = new Double3(0, h, l - 1.6);
        // No mount solid: the gun slides on its rail (T93), so it isn't part of the car's collision.
        var solids = car.Solids.ToList();
        // The brake wheel moves to the front end so it isn't under the gun.
        var interactables = car.Interactables.Where(i => i.Kind != InteractableKind.Handbrake)
            .Append(new Interactable(InteractableKind.Handbrake, new Double3(BrakeWheelX(g), h, -l + 0.5), 0.8)).ToList();
        var ladders = car.Ladders.ToList();
        Box? platform = null;
        if (!hasCarBehind)
        {
            // Last in the train, the rear platform (GDD §24 THE WEIGHT: "melee from the rear platform"): a grating across
            // the car's back end at plate height, the rear door onto it, and a short ladder up from it to the roof. Under
            // it is the coupling the Weight takes hold of.
            var box = new Box(new Double3(-g.RoofWidth / 2, g.CouplerHeight - 0.1, l), new Double3(g.RoofWidth / 2, g.CouplerHeight, l + g.PlatformDepth));
            solids.Add(new Solid(box, SurfaceKind.Coupler, PartKind.Coupler));
            ladders.Add(new Ladder(new Double3(g.EndLadderX, g.CouplerHeight, l + 0.1), h, new Double3(0, 0, -1)));
            platform = box;
        }
        if (g.Interior is { } i)
        {
            // Tool storage along the left wall (GDD §10), and a hatch ladder up to the gun from inside.
            double w = g.RoofWidth / 2;
            solids.Add(new Solid(new Box(new Double3(-w + i.WallThickness, i.FloorHeight, -l + 1.5), new Double3(-w + i.WallThickness + 0.5, i.FloorHeight + 1.8, -l + 3.5)), SurfaceKind.Deck, PartKind.Locker));
            ladders.Add(new Ladder(new Double3(0.6, i.FloorHeight, l - 2.4), h, new Double3(0, 0, -1)));
        }
        return car with { Solids = solids, Interactables = interactables, Ladders = ladders, Gun = new GunMount(mount + new Double3(0, 0.9, 0), new Double3(0, 0, 1)), Platform = platform };
    }

    /// <summary>
    /// A crew car (GDD §10 "utility car", §26 "cramped, lamp-lit, human-scale"; note 184): walled like the guard van, no side
    /// doors and no freight; berths down the right side, where a cargo car's load stands, and the stove at the rear end on
    /// the left with its pipe up through the roof. The aisle runs from end door to end door between them. The crew lockers
    /// (its stores) go along the left wall from the front when it's the kit's car.
    /// </summary>
    static CarShape Utility(GeometryTuning g, bool hasCarBehind)
    {
        if (g.Interior is not { } i)
            return SolidCar(g, hasCarBehind);
        var car = Shell(g, i, hasCarBehind, cargo: false);
        var room = car.Interior!.Value;
        double floor = i.FloorHeight;
        var solids = car.Solids.ToList();
        // Two berths, one over the other, stood clear of the end doors: the lower one's a seat, the upper one's a shelf
        // you don't stand on (it's the frame that's solid, from the floor to the top berth).
        var bunks = new Box(new Double3(room.Max.X - i.CargoDepth * 0.7, floor, room.Min.Z + 1.5), new Double3(room.Max.X, floor + 1.45, room.Max.Z - 0.7));
        solids.Add(new Solid(bunks, SurfaceKind.Deck, PartKind.Bunk));
        // Against the left wall, clear of the rear doorway's edge (end doors stand left of centre, at interior.doorX).
        var stove = new Box(new Double3(room.Min.X + 0.05, floor, room.Max.Z - 1.0), new Double3(room.Min.X + 0.5, floor + 0.75, room.Max.Z - 0.5));
        solids.Add(new Solid(stove, SurfaceKind.Deck, PartKind.Stove));
        return car with { Solids = solids, Stove = stove };
    }

    /// <summary>
    /// The dining car (upgrades.md §8.5, note 959; the director, 13 Oct: "If we have a dining car, we should be able to order
    /// drinks at the bar"): a cargo car converted, walled like the crew car (no side doors, no freight). The aisle runs from
    /// end door to end door on the left (the end doors stand left of centre), past the crew lockers when it's the kit's car
    /// (along the left wall at the front). Its table laid for eight stands down the front half, right of the aisle; the pub's
    /// bar down the rear half (note 1016; the director's Look Review verdict, 23 Oct: "a cool classic wooden english pub bar
    /// ... at the back of the car clearly not obstructing the doors ... away from the lockers"): the counter along the right,
    /// stools down its front, and behind it the barman's walkway, in from the rear end beside the rear door (not across it),
    /// where Use pours a drink (train.json <c>composition.dining</c>'s bar: its numbers).
    /// </summary>
    static CarShape DiningRoom(GeometryTuning g, DiningTuning t, bool hasCarBehind)
    {
        if (g.Interior is not { } i)
            return SolidCar(g, hasCarBehind);
        var car = Shell(g, i, hasCarBehind, cargo: false);
        var room = car.Interior!.Value;
        double floor = i.FloorHeight;
        var solids = car.Solids.ToList();
        var b = t.Bar;
        double c0 = b.CounterX, c1 = c0 + b.CounterDepth, back = room.Max.Z - b.FromRear, front = back - b.CounterLength;
        var table = new Box(new Double3(0.05, floor, room.Min.Z + 0.6), new Double3(0.85, floor + 0.76, front - b.TableGap));
        var counter = new Box(new Double3(c0, floor, front), new Double3(c1, floor + b.CounterHeight, back));
        var ret = new Box(new Double3(c1, floor, front), new Double3(room.Max.X, floor + b.CounterHeight, front + b.CounterDepth));
        var backBar = new Box(new Double3(room.Max.X - b.BackBarDepth, floor, ret.Max.Z), new Double3(room.Max.X, room.Max.Y - 0.2, back));
        var behind = new Box(new Double3(c1, floor, ret.Max.Z), new Double3(backBar.Min.X, floor + b.CounterHeight, room.Max.Z));
        solids.Add(new Solid(table, SurfaceKind.Deck, PartKind.Furniture));
        solids.Add(new Solid(counter, SurfaceKind.Deck, PartKind.Furniture));
        solids.Add(new Solid(ret, SurfaceKind.Deck, PartKind.Furniture));
        solids.Add(new Solid(backBar, SurfaceKind.Deck, PartKind.Furniture));
        // The pumps, behind the counter's middle: only a hand behind the bar reaches them (from the stools it's the counter's
        // depth and more away).
        var bar = new Double3((behind.Min.X + behind.Max.X) / 2, floor, (counter.Min.Z + counter.Max.Z) / 2);
        var interactables = car.Interactables.Append(new Interactable(InteractableKind.Bar, bar, b.PourReach)).ToList();
        // The stools down the counter's front, facing it (+X: a player's yaw of −π/2); sat on from the aisle in front of each.
        var stools = new List<Lounge>();
        double pitch = (back - front) / Math.Max(1, b.Stools);
        for (int k = 0; k < b.Stools; k++)
        {
            double z = front + pitch * (k + 0.5);
            stools.Add(new Lounge(LoungeKind.Stool, new Double3(c0 - b.StoolOut, floor + b.StoolHeight, z), -Math.PI / 2));
            interactables.Add(new Interactable(InteractableKind.Bench, new Double3(c0 - b.StoolOut - b.StandOff, floor, z), SeatReach, k, Aim: b.StoolHeight));
        }
        return car with
        {
            Solids = solids,
            Interactables = interactables,
            Lounges = stools,
            Dining = new DiningLayout(table, counter, bar, ret, behind, backBar),
        };
    }

    /// <summary>
    /// The roof's brake wheel stands inboard of the end ladder's top: a reaching hand works it from the ladder (T29), and
    /// standing on the ladder you're just out of reach of it.
    /// </summary>
    static double BrakeWheelX(GeometryTuning g) => g.EndLadderX - 0.55;

    /// <summary>The plate across the gap behind a car, from its end to the next car's, on the end doors' line (<see cref="GeometryTuning.PlateX"/>).</summary>
    static Solid? CouplerPlate(GeometryTuning g, double halfLength, bool hasCarBehind) => hasCarBehind
        ? new Solid(new Box(new Double3(g.PlateX - g.CouplerWidth / 2, g.CouplerHeight - 0.1, halfLength), new Double3(g.PlateX + g.CouplerWidth / 2, g.CouplerHeight, halfLength + g.CouplingGap)),
            SurfaceKind.Coupler, PartKind.Coupler)
        : null;

    /// <summary>
    /// A car whose coupling behind is a Knotter (note 365): its plate gone, its body across the wider gap instead: a rope's
    /// width (<see cref="KnotWidth"/>) on the plate's line, from the car's end to the next car's, the same height.
    /// </summary>
    public static CarShape Knotted(CarShape shape, GeometryTuning g, double knot) => KnottedShape(shape, g, knot);

    /// <summary>
    /// The engine with the BBQ deck on its front (ARCHITECTURE §8 note 698; the director, 13 Oct 2026: "an extended front car
    /// cow catcher that has a deck, a couple muskoka chairs that can be sat in, a plastic cooler with beers, and a BBQ mounted
    /// on a stand on the deck"): a plank deck <see cref="BbqTuning.DeckLength"/> m long ahead of the pilot, as wide as the
    /// running boards and at their height, railed round its front and sides; the running boards run on forward along the
    /// cab's sides onto it, the way out to it from the cab doorways, outside, at speed. The cooler and the grill are solid
    /// (a walk round them); the chairs aren't (sat in, you're set in one). It lies beyond the engine's bounds, as the guard
    /// van's rear platform does: the train's length is the train's (spec B.4).
    /// </summary>
    public static CarShape WithBbqDeck(CarShape shape, GeometryTuning g, BbqTuning bbq)
    {
        var e = g.Engine;
        double w = g.RoofWidth / 2, l = shape.HalfLength, deck = e.DeckHeight, hw = w + e.RunningBoardWidth, front = -l - bbq.DeckLength;
        double rail = bbq.RailHeight, post = 0.06;
        var plan = EnginePlan.Of(g);
        double boardFront = plan.DoorFront - e.RunningBoardAhead;
        var solids = shape.Solids.ToList();
        // (The landings outside the cab's side doors, note 856, are under the boards run on forward: one board, not two.)
        solids.RemoveAll(s => s.Part == PartKind.RunningBoard && s.Box.Min.Z >= -l && s.Box.Max.Z <= boardFront + 1e-6);
        // The deck on its cowcatcher, and the running boards run on to it past the cab's front.
        solids.Add(new(new Box(new Double3(-hw, deck - 0.15, front), new Double3(hw, deck, -l)), SurfaceKind.Deck, PartKind.BbqDeck));
        foreach (int side in new[] { -1, 1 })
        {
            var (x0, x1) = side < 0 ? (-hw, -w) : (w, hw);
            solids.Add(new(new Box(new Double3(x0, deck - 0.1, -l), new Double3(x1, deck, boardFront)), SurfaceKind.Deck, PartKind.RunningBoard));
            // Its side rail, from the front back to a step short of the running board's way on.
            solids.Add(new(new Box(new Double3(side < 0 ? -hw : hw - post, deck, front), new Double3(side < 0 ? -hw + post : hw, deck + rail, -l - 0.6)), SurfaceKind.Deck, PartKind.Rail));
        }
        solids.Add(new(new Box(new Double3(-hw, deck, front), new Double3(hw, deck + rail, front + post)), SurfaceKind.Deck, PartKind.Rail));
        // The two chairs out front, facing down the line; the cooler between them; the grill on its stand back by the cab, on
        // the left, clear of the right-hand board's way on and of the chairs.
        double seatZ = front + 1.0;
        Double3[] chairs = [new(-0.95, deck, seatZ), new(0.95, deck, seatZ)];
        var cooler = new Double3(0, deck, seatZ - 0.05);
        var grill = new Double3(-1.15, deck, -l - 1.2);
        solids.Add(new(Box.FromCentre(cooler + new Double3(0, 0.22, 0), new Double3(0.3, 0.22, 0.2)), SurfaceKind.Deck, PartKind.Cooler));
        solids.Add(new(Box.FromCentre(grill + new Double3(0, 0.5, 0), new Double3(0.35, 0.5, 0.28)), SurfaceKind.Deck, PartKind.Grill));
        var interactables = shape.Interactables.ToList();
        for (int i = 0; i < chairs.Length; i++)
            interactables.Add(new(InteractableKind.Chair, chairs[i], 0.75, i, Aim: 0.45));
        // The cooler's lid from behind it, and the grill from behind it, facing down the line.
        interactables.Add(new(InteractableKind.Cooler, cooler + new Double3(0, 0, 0.55), 0.7, Aim: 0.45));
        interactables.Add(new(InteractableKind.Grill, grill + new Double3(0, 0, 0.6), 0.75, Aim: 1.0));
        return shape with { Solids = solids, Interactables = interactables, Bbq = new BbqLayout(chairs, cooler, grill, front) };
    }

    /// <summary>
    /// The guard van with the gramophone (note 715): its little table against the left wall (<see cref="Whimsy.Gramophone"/>),
    /// solid, and wound from the aisle beside it.
    /// </summary>
    public static CarShape WithGramophone(CarShape shape)
    {
        if (shape.Interior is null)
            return shape;
        var at = Whimsy.Gramophone(shape);
        var solids = shape.Solids.Append(new Solid(Box.FromCentre(at + new Double3(0, Whimsy.Table / 2, 0), new Double3(0.22, Whimsy.Table / 2, 0.22)),
            SurfaceKind.Deck, PartKind.Furniture)).ToList();
        var interactables = shape.Interactables.Append(new Interactable(InteractableKind.Gramophone, at + new Double3(0.6, 0, 0), 0.8, Aim: Whimsy.Table + 0.15)).ToList();
        return shape with { Solids = solids, Interactables = interactables };
    }

    /// <summary>
    /// The guard van with the hammock (note 744; docs/design/upgrades.md §7.4, "strung across a car end: lie in it, swinging"):
    /// slung across the van's rear end inside, its middle a body's width off the floor, lain in across the car.
    /// </summary>
    public static CarShape WithHammock(CarShape shape)
    {
        if (shape.Interior is not { } room)
            return shape;
        var at = new Double3(0, room.Min.Y + 0.1, room.Max.Z - 0.85);
        var lounges = shape.Lounges.Append(new Lounge(LoungeKind.Hammock, at, Math.PI / 2)).ToList();
        var interactables = shape.Interactables.Append(new Interactable(InteractableKind.Hammock, at + new Double3(0, 0, -0.75), 0.9, lounges.Count - 1, Aim: 0.7)).ToList();
        return shape with { Lounges = lounges, Interactables = interactables };
    }

    /// <summary>
    /// The engine with the hot tub (note 744; "a tub fed off the boiler ... on the roof at line speed, in the open"): a tub on
    /// the hood's roof behind the cab, <see cref="TubHalf"/> across, two seats in it facing each other across the train.
    /// </summary>
    public static CarShape WithHotTub(CarShape shape)
    {
        double roof = shape.RoofHeight, z = TubZ;
        Double3[] seats = [new(-0.42, roof, z), new(0.42, roof, z)];
        var lounges = shape.Lounges.ToList();
        var interactables = shape.Interactables.ToList();
        foreach (var (seat, yaw) in new[] { (seats[0], -Math.PI / 2), (seats[1], Math.PI / 2) })
        {
            lounges.Add(new Lounge(LoungeKind.HotTub, seat, yaw));
            interactables.Add(new Interactable(InteractableKind.HotTub, seat, 0.9, lounges.Count - 1, Aim: 0.5));
        }
        return shape with { Lounges = lounges, Interactables = interactables };
    }

    /// <summary>
    /// The guard van with the tennis-ball launcher (note 745) on its rear platform's rail: worked from the platform with a Use
    /// press, it fires a ball off the back, up the line behind.
    /// </summary>
    public static CarShape WithLauncher(CarShape shape) =>
        shape.Platform is { } p
            ? shape with { Interactables = [.. shape.Interactables, new Interactable(InteractableKind.Launcher, Launcher(p), 0.9, Aim: 1.0)] }
            : shape;

    /// <summary>Where a crewmate stands at the launcher: the platform's middle, on its grating.</summary>
    public static Double3 Launcher(Box platform) => new((platform.Min.X + platform.Max.X) / 2, platform.Max.Y, (platform.Min.Z + platform.Max.Z) / 2);

    /// <summary>Where the hot tub sits on the engine's hood (its middle, m along the engine's frame), and its half-size across and along.</summary>
    public const double TubZ = 0.8, TubHalf = 0.85;

    static CarShape KnottedShape(CarShape shape, GeometryTuning g, double knot)
    {
        double l = shape.HalfLength;
        // (And the engine's ramp up its plate, note 905: the Knotter's back is what's across the gap now.)
        var solids = shape.Solids.Where(s => s.Part is not (PartKind.Coupler or PartKind.Ramp)).ToList();
        solids.Add(new Solid(new Box(new Double3(g.PlateX - KnotWidth / 2, g.CouplerHeight - 0.1, l), new Double3(g.PlateX + KnotWidth / 2, g.CouplerHeight, l + g.CouplingGap + knot)),
            SurfaceKind.Coupler, PartKind.Coupler));
        return shape with { Solids = solids };
    }

    /// <summary>The Knotter's back, as a foot finds it (note 365).</summary>
    public const double KnotWidth = 0.4;

    static CarShape Car(GeometryTuning g, bool hasCarBehind) =>
        g.Interior is { } i ? Shell(g, i, hasCarBehind, cargo: true) : SolidCar(g, hasCarBehind);

    /// <summary>
    /// A walk-in car: floor level with the coupler plate, walls, a roof slab you can still walk the length of,
    /// and a door in each end wall. Cargo stacks down the right side; the aisle runs from door to door. A cargo car also
    /// has a sliding door in the middle of each side with steps up to it (spec D.2: freight is carried in from the ground).
    /// </summary>
    static CarShape Shell(GeometryTuning g, InteriorLayout i, bool hasCarBehind, bool cargo)
    {
        double w = g.RoofWidth / 2, l = g.CarLength / 2, h = g.CarHeight;
        double floor = i.FloorHeight, t = i.WallThickness, ceiling = h - i.RoofThickness;
        double d0 = i.DoorX - g.Doorway.Width / 2, d1 = i.DoorX + g.Doorway.Width / 2, lintel = floor + g.Doorway.Height;
        double sd = i.SideDoorWidth / 2;
        var solids = new List<Solid>
        {
            new(new Box(new Double3(-w, 0, -l), new Double3(w, floor, l)), SurfaceKind.Deck, PartKind.Chassis),
        };
        // The roof slab; on a cargo car, with its hatch between the walls (T99): the slab either side of it, the wall tops
        // along it, and the lid.
        Box? hatch = null;
        if (cargo && i.HatchLength > 0)
        {
            double z0 = i.HatchZ - i.HatchLength / 2, z1 = i.HatchZ + i.HatchLength / 2;
            solids.Add(new(new Box(new Double3(-w, ceiling, -l), new Double3(w, h, z0)), SurfaceKind.Roof, PartKind.Body));
            solids.Add(new(new Box(new Double3(-w, ceiling, z1), new Double3(w, h, l)), SurfaceKind.Roof, PartKind.Body));
            solids.Add(new(new Box(new Double3(-w, ceiling, z0), new Double3(-w + t, h, z1)), SurfaceKind.Roof, PartKind.Body));
            solids.Add(new(new Box(new Double3(w - t, ceiling, z0), new Double3(w, h, z1)), SurfaceKind.Roof, PartKind.Body));
            hatch = new Box(new Double3(-w + t, ceiling, z0), new Double3(w - t, h, z1));
            solids.Add(new(hatch.Value, SurfaceKind.Roof, PartKind.Hatch));
        }
        else
            solids.Add(new(new Box(new Double3(-w, ceiling, -l), new Double3(w, h, l)), SurfaceKind.Roof, PartKind.Body));
        foreach (int side in new[] { -1, 1 })
        {
            double x0 = side < 0 ? -w : w - t, x1 = side < 0 ? -w + t : w;
            if (!cargo)
            {
                solids.Add(new(new Box(new Double3(x0, floor, -l), new Double3(x1, ceiling, l)), SurfaceKind.Deck, PartKind.Wall));
                continue;
            }
            // Either side of the sliding door, and the lintel over it.
            solids.Add(new(new Box(new Double3(x0, floor, -l), new Double3(x1, ceiling, -sd)), SurfaceKind.Deck, PartKind.Wall));
            solids.Add(new(new Box(new Double3(x0, floor, sd), new Double3(x1, ceiling, l)), SurfaceKind.Deck, PartKind.Wall));
            solids.Add(new(new Box(new Double3(x0, lintel, -sd), new Double3(x1, ceiling, sd)), SurfaceKind.Deck, PartKind.Wall));
        }
        var doors = new List<Door>();
        var interactables = new List<Interactable>();
        foreach (int end in new[] { -1, 1 })
        {
            // End wall either side of the doorway, and the lintel over it.
            double z0 = end < 0 ? -l : l - t, z1 = end < 0 ? -l + t : l;
            solids.Add(new(new Box(new Double3(-w, floor, z0), new Double3(d0, ceiling, z1)), SurfaceKind.Deck, PartKind.Wall));
            solids.Add(new(new Box(new Double3(d1, floor, z0), new Double3(w, ceiling, z1)), SurfaceKind.Deck, PartKind.Wall));
            solids.Add(new(new Box(new Double3(d0, lintel, z0), new Double3(d1, ceiling, z1)), SurfaceKind.Deck, PartKind.Wall));
            int index = doors.Count;
            doors.Add(new Door(new Box(new Double3(d0, floor, z0), new Double3(d1, lintel, z1)), index));
            // Reachable from inside or from the coupler plate outside.
            interactables.Add(new Interactable(InteractableKind.Door, new Double3(i.DoorX, floor, end * l), 0.75, index));
        }
        if (cargo)
        {
            foreach (int side in new[] { -1, 1 })
            {
                // The sliding doors come after the end doors (bits 2 and 3: left, right), in reach from inside or the steps.
                double x0 = side < 0 ? -w : w - t, x1 = side < 0 ? -w + t : w;
                int index = doors.Count;
                doors.Add(new Door(new Box(new Double3(x0, floor, -sd), new Double3(x1, lintel, sd)), index));
                interactables.Add(new Interactable(InteractableKind.Door, new Double3(side * (w - t / 2), floor, 0), 0.9, index));
                // Outside it, a landing level with the floor, and treads coming up to it from the front along the car side:
                // rises under 0.3 m, which you walk up (player.json stepUp) with freight in your arms and can't climb.
                double o0 = side < 0 ? -w - i.StepWidth : w, o1 = side < 0 ? -w : w + i.StepWidth;
                solids.Add(new(new Box(new Double3(o0, 0, -sd), new Double3(o1, floor, sd)), SurfaceKind.Deck, PartKind.Steps));
                int rises = (int)Math.Ceiling(floor / 0.3);
                for (int k = 1; k < rises; k++)
                {
                    double z1 = -sd - (k - 1) * i.StepDepth;
                    solids.Add(new(new Box(new Double3(o0, 0, z1 - i.StepDepth), new Double3(o1, floor * (rises - k) / rises, z1)), SurfaceKind.Deck, PartKind.Steps));
                }
            }
            // The load stands down the right-hand side, either side of its door: what's poured in heaps there, as high as the car's
            // that full (note 659, Solid.BoxIn); what's put in by hand or hook lies where it's put.
            double c0 = w - t - i.CargoDepth, c1 = w - t, top = floor + i.CargoHeight;
            solids.Add(new(new Box(new Double3(c0, floor, -l + 1.2), new Double3(c1, top, -sd - 0.3)), SurfaceKind.Deck, PartKind.Cargo));
            solids.Add(new(new Box(new Double3(c0, floor, sd + 0.3), new Double3(c1, top, l - 1.2)), SurfaceKind.Deck, PartKind.Cargo));
        }
        if (CouplerPlate(g, l, hasCarBehind) is { } plate)
            solids.Add(plate);

        double ladderZ = l - g.LadderInset;
        var ladders = new List<Ladder>
        {
            new(new Double3(w + 0.15, 0, ladderZ), h, new Double3(-1, 0, 0)),
            new(new Double3(-w - 0.15, 0, ladderZ), h, new Double3(1, 0, 0)),
            new(new Double3(g.EndLadderX, 0, -l - 0.1), h, new Double3(0, 0, 1)),
        };
        if (hasCarBehind)
            ladders.Add(new Ladder(new Double3(g.EndLadderX, 0, l + 0.1), h, new Double3(0, 0, -1)));
        interactables.Add(new Interactable(InteractableKind.Handbrake, new Double3(BrakeWheelX(g), h, l - 0.5), 0.8));
        // The hatch's handle: on the roof at its front edge, left of the gun rail, worked from the roof walk. And its lid (note
        // 675; the director, 13 Oct: "I should be able to open the roof of a car for loading from inside the car ... opening of
        // the roofs is very inconsistent"): from anywhere along it or a metre off its ends, looking down at it from the roof, or
        // up at it from the floor under it. Only looked at, so a crewmate walking the roof or working inside isn't offered it.
        if (hatch is { } hb)
        {
            interactables.Add(new Interactable(InteractableKind.Hatch, new Double3(-0.6, h, hb.Min.Z - 0.35), 0.8, HatchBit));
            double reach = (hb.Max.Z - hb.Min.Z) / 2 + 1.0;
            var middle = (hb.Min.Z + hb.Max.Z) / 2;
            interactables.Add(new Interactable(InteractableKind.Hatch, new Double3(0, h, middle), reach, HatchBit, Aim: 0, LookDegrees: -HatchLookDegrees));
            interactables.Add(new Interactable(InteractableKind.Hatch, new Double3(0, floor, middle), reach, HatchBit, Aim: ceiling - floor,
                LookDegrees: HatchLookDegrees));
        }
        var interior = new Box(new Double3(-w + t, floor - 0.1, -l + t), new Double3(w - t, ceiling, l - t));
        return new CarShape(new Box(new Double3(-w, 0, -l), new Double3(w, h, l)), solids, ladders, interactables, null, Interior: interior, Doors: doors)
        {
            Hatch = hatch,
        };
    }

    /// <summary>
    /// The crew lockers (ARCHITECTURE §8 note 173): a row along the left wall from <see cref="LockerTuning.FromFront"/> behind
    /// the front end wall, back towards the side door (a cargo car's) and stopping short of it, each a solid cabinet with
    /// its door to the aisle (which runs from end door to end door, right of them). As many as there are names, and as fit:
    /// none crowds a door. In the guard van they stand where its tool locker did, and take its place.
    /// </summary>
    static CarShape WithLockers(CarShape car, LockerTuning t)
    {
        if (car.Interior is not { } room)
            return car;
        double floor = room.Min.Y + 0.1, x0 = room.Min.X, x1 = x0 + t.Depth;
        double z0 = room.Min.Z + t.FromFront;
        // Stop short of the left side door (a cargo car's) or the far end wall.
        double end = car.DoorList.Where(d => d.Box.Max.X < 0 && d.Box.Max.Z - d.Box.Min.Z > d.Box.Max.X - d.Box.Min.X)
            .Select(d => d.Box.Min.Z - 0.15).DefaultIfEmpty(room.Max.Z - 1.0).Min();
        int count = Math.Min(t.Names.Count, (int)Math.Floor((end - z0) / t.Width + 1e-9));
        var bays = new List<LockerBay>();
        for (int i = 0; i < count; i++)
            bays.Add(new LockerBay(i, new Box(new Double3(x0, floor, z0 + i * t.Width), new Double3(x1, floor + t.Height, z0 + (i + 1) * t.Width)), t.Names[i]));
        if (bays.Count == 0)
            return car;
        var span = new Box(bays[0].Box.Min, bays[^1].Box.Max);
        var solids = car.Solids.Where(s => !(s.Part == PartKind.Locker && s.Box.Min.Z < span.Max.Z && s.Box.Max.Z > span.Min.Z && s.Box.Min.X < span.Max.X))
            .Concat(bays.Select(b => new Solid(b.Box, SurfaceKind.Deck, PartKind.CrewLocker))).ToList();
        // Each door's handle: at its face, at the feet of whoever stands in front of it.
        var interactables = car.Interactables.Concat(bays.Select(b => new Interactable(InteractableKind.Locker, b.Front with { X = b.Front.X + 0.05 }, 0.6, b.Index, Aim: 1.1))).ToList();
        int kit = bays.FindIndex(b => string.Equals(b.Name, t.KitLocker, StringComparison.OrdinalIgnoreCase));
        return car with { Solids = solids, Interactables = interactables, Lockers = bays, LockerShelves = Math.Max(1, t.Slots), KitLocker = Math.Max(0, kit) };
    }

    static CarShape SolidCar(GeometryTuning g, bool hasCarBehind)
    {
        double w = g.RoofWidth / 2, l = g.CarLength / 2, h = g.CarHeight;
        var solids = new List<Solid> { new(new Box(new Double3(-w, 0, -l), new Double3(w, h, l)), SurfaceKind.Roof, PartKind.Body) };
        if (CouplerPlate(g, l, hasCarBehind) is { } plate)
            solids.Add(plate);
        // Side ladders at the rear corners (boarding from the ground), end ladders on each face
        // beside the coupler (climbing out of the gap).
        double ladderZ = l - g.LadderInset;
        var ladders = new List<Ladder>
        {
            new(new Double3(w + 0.15, 0, ladderZ), h, new Double3(-1, 0, 0)),
            new(new Double3(-w - 0.15, 0, ladderZ), h, new Double3(1, 0, 0)),
            new(new Double3(g.EndLadderX, 0, -l - 0.1), h, new Double3(0, 0, 1)),
        };
        if (hasCarBehind)
            ladders.Add(new Ladder(new Double3(g.EndLadderX, 0, l + 0.1), h, new Double3(0, 0, -1)));
        // Brake wheel on the roof at the rear end, above the end ladder.
        var interactables = new[] { new Interactable(InteractableKind.Handbrake, new Double3(BrakeWheelX(g), h, l - 0.5), 0.8) };
        return new CarShape(new Box(new Double3(-w, 0, -l), new Double3(w, h, l)), solids, ladders, interactables, null);
    }

    /// <summary>
    /// The engine, cab forward (ARCHITECTURE §8 note 276, the director's sketch: "controls at the front with full vis of the
    /// rail"), as one 20 m unit (spec B.4), front to back: the pilot; the walkable cab, the driver's controls at its front
    /// windows over the line and the firebox in its back wall with the coal bunker beside it; then the boiler, its stack
    /// at the rear, where car 1 couples on. The cab is where GDD §12's Conductor and Boiler roles are: whoever stands there.
    /// </summary>
    static CarShape Engine(GeometryTuning g, bool hasCarBehind)
    {
        var e = g.Engine;
        var plan = EnginePlan.Of(g);
        double w = g.RoofWidth / 2, l = plan.Half, cabFront = plan.CabFront, cabBack = plan.CabBack, doorFront = plan.DoorFront;
        double deck = e.DeckHeight, roof = g.EngineHeight - 0.2;
        var solids = new List<Solid>
        {
            new(new Box(new Double3(-w, 0, -l), new Double3(w, deck, l)), SurfaceKind.Deck, PartKind.Chassis),
            new(new Box(new Double3(-e.BoilerHalfWidth, deck, cabBack), new Double3(e.BoilerHalfWidth, e.BoilerTop, l - BoilerToEnd)), SurfaceKind.Roof, PartKind.Boiler),
            // (Up through the hood's roof, note 338.)
            new(new Box(new Double3(-0.35, e.BoilerTop, plan.StackZ - 0.35), new Double3(0.35, g.EngineHeight + 0.7, plan.StackZ + 0.35)), SurfaceKind.Roof, PartKind.Stack),
            new(new Box(new Double3(-w - 0.1, roof, cabFront), new Double3(w + 0.1, g.EngineHeight, cabBack)), SurfaceKind.Roof, PartKind.CabRoof),
            // The coal bunker in the cab's front left corner, and the firebox against the front wall beside it (note 280).
            new(plan.Bunker, SurfaceKind.Deck, PartKind.Tender),
            new(plan.Firebox, SurfaceKind.Deck, PartKind.Firebox),
        };
        // The cab's sides a waist over the gallery's floor (note 716: a sill at the gallery's knees would be one to fall out
        // over), the whole way from the front pillar to the doorway, so a walker along them meets no corner where the
        // gallery starts; under the footplate's slits, as they always were, so nothing's seen differently from the controls.
        double waist = plan.Gallery.Max.Z > plan.Gallery.Min.Z ? Math.Max(deck + 1.1, plan.Gallery.Max.Y + e.CrewCar.GalleryWaist) : deck + 1.1;
        // The cab's front: waist-high under its windows, the driver's console behind it; pillars at its corners hold the
        // roof, and the windows between them are the driver's view of the line.
        solids.Add(new(new Box(new Double3(-w, deck, cabFront), new Double3(w, deck + 1.1, cabFront + 0.15)), SurfaceKind.Deck, PartKind.CabWall));
        foreach (int side in new[] { -1, 1 })
        {
            double inner = side * (w - 0.1), outer = side * w;
            var (x0, x1) = (Math.Min(inner, outer), Math.Max(inner, outer));
            // Sides waist-high with a doorway at the back of each; corner pillars; the standard lintel over each doorway
            // (train.json doorway: the cab roof is higher, but a door is a door, note 110).
            // (Broken by the side door at the cab's front on this side, note 856: the door's leaf is the wall there, shut.)
            double wallFrom = cabFront;
            foreach (var d in plan.SideDoors.Where(d => d.Side == side))
            {
                solids.Add(new(new Box(new Double3(x0, deck, wallFrom), new Double3(x1, waist, d.Z0)), SurfaceKind.Deck, PartKind.CabWall));
                wallFrom = d.Z1;
            }
            solids.Add(new(new Box(new Double3(x0, deck, wallFrom), new Double3(x1, waist, doorFront)), SurfaceKind.Deck, PartKind.CabWall));
            solids.Add(new(new Box(new Double3(x0, deck, cabFront), new Double3(x1, roof, cabFront + 0.15)), SurfaceKind.Deck, PartKind.CabWall));
            solids.Add(new(new Box(new Double3(x0, deck, cabBack - 0.15), new Double3(x1, roof, cabBack)), SurfaceKind.Deck, PartKind.CabWall));
            solids.Add(new(new Box(new Double3(x0, deck + g.Doorway.Height, doorFront), new Double3(x1, roof, cabBack - 0.15)), SurfaceKind.Deck, PartKind.CabWall));
            // The back wall either side of the boiler, where it comes through, is open under a lintel (the director, 7 Oct
            // 2026, note 338: "cut open on both sides so players can walk straight to the cab"): the walkway between the
            // boiler and the cab's side runs on into the cab, the standard doorway's height, as wide as the walkway is.
            double b0 = side < 0 ? -w + 0.1 : e.BoilerHalfWidth, b1 = side < 0 ? -e.BoilerHalfWidth : w - 0.1;
            solids.Add(new(new Box(new Double3(b0, deck + g.Doorway.Height, cabBack - 0.15), new Double3(b1, roof, cabBack)), SurfaceKind.Deck, PartKind.CabWall));
        }
        // ... and across it (note 280: the firebox is at the front now; the boiler's end is plated over).
        solids.Add(new(new Box(new Double3(-e.BoilerHalfWidth, deck, cabBack - 0.15), new Double3(e.BoilerHalfWidth, roof, cabBack)), SurfaceKind.Deck, PartKind.CabWall));
        // The crew car (note 716; the director, 14 Oct 2026: "the cab itself becomes the crew car ... the captain's quarters,
        // shared by all"), behind the footplate: the chart table in the middle, the cab map on it; then the gallery, its floor
        // raised wall to wall with a tread up at each end, the benches down its middle facing forward; the ways down the cab
        // either side of them all to the back wall, its doors and the corridors along the boiler.
        solids.Add(new(plan.ChartTable, SurfaceKind.Deck, PartKind.ChartTable));
        if (plan.Gallery.Max.Z > plan.Gallery.Min.Z)
        {
            solids.Add(new(plan.Gallery, SurfaceKind.Deck, PartKind.Gallery));
            foreach (var tread in new[] { plan.FrontTread, plan.BackTread })
                if (tread.Max.Z > tread.Min.Z)
                    solids.Add(new(tread, SurfaceKind.Deck, PartKind.Gallery));
            foreach (var bench in plan.Benches)
                solids.Add(new(bench, SurfaceKind.Deck, PartKind.Bench));
        }
        // The hood (note 338; the director's silhouette, 7 Oct 2026: "This is a bulky survival train not a dainty city tram"):
        // armour the engine's whole length behind the cab, its sides flush with the cab's and a roof at the cab roof's
        // height, walked on as one with it. The walkway either side of the boiler runs inside it, a corridor from the deck
        // behind the smokebox straight into the cab; each corridor's back end open under a lintel, like its front.
        double hoodBack = l - BoilerToEnd;
        foreach (int side in new[] { -1, 1 })
        {
            double inner = side * (w - 0.1), outer = side * w;
            var (x0, x1) = (Math.Min(inner, outer), Math.Max(inner, outer));
            solids.Add(new(new Box(new Double3(x0, deck, cabBack), new Double3(x1, roof, hoodBack)), SurfaceKind.Deck, PartKind.CabWall));
            double b0 = side < 0 ? -w + 0.1 : e.BoilerHalfWidth, b1 = side < 0 ? -e.BoilerHalfWidth : w - 0.1;
            solids.Add(new(new Box(new Double3(b0, deck + g.Doorway.Height, hoodBack - 0.15), new Double3(b1, roof, hoodBack)), SurfaceKind.Deck, PartKind.CabWall));
        }
        solids.Add(new(new Box(new Double3(-w - 0.1, roof, cabBack), new Double3(w + 0.1, g.EngineHeight, hoodBack)), SurfaceKind.Roof, PartKind.CabRoof));
        // The running boards (App. A.2 GREASE: "sends someone onto the running boards at speed"): a walkway each side at
        // deck height, out past the body's side, from ahead of the cab's doorway (the director, 9 Oct 2026, note 559: "the
        // platform on the outer edge of the train really should extend to be in front of the cab doors and extend a bit
        // wider") back along the boiler to the engine's rear. Out of the doorway is onto it; the cab steps hang off its edge.
        double board = e.RunningBoardWidth, boardFront = doorFront - e.RunningBoardAhead;
        foreach (int side in new[] { -1, 1 })
        {
            var (x0, x1) = side < 0 ? (-w - board, -w) : (w, w + board);
            solids.Add(new(new Box(new Double3(x0, deck - 0.1, boardFront), new Double3(x1, deck, l - 0.3)), SurfaceKind.Deck, PartKind.RunningBoard));
        }
        // The side doors at the cab's front (note 856; the director, 16 Oct 2026: "a door near where the coal is, and I guess
        // on the other side of the front of the cab as well"): each a sliding door in the side, solid shut, worked from inside
        // or from the landing outside it, a board's width out at the running boards' height; the cab steps hung off its edge
        // up to it from the ballast, as they hang off the boards at the back.
        var doors = new List<Door>();
        var sideDoorways = new List<Interactable>();
        foreach (var d in plan.SideDoors)
        {
            double inner = d.Side * (w - 0.1), outer = d.Side * w;
            var (x0, x1) = (Math.Min(inner, outer), Math.Max(inner, outer));
            doors.Add(new Door(new Box(new Double3(x0, deck, d.Z0), new Double3(x1, deck + g.Doorway.Height, d.Z1)), d.Index, d.Slide, DoorOut));
            sideDoorways.Add(new Interactable(InteractableKind.Door, new Double3(d.Side * (w - 0.05), deck, d.Middle), 0.9, d.Index));
            var (b0, b1) = d.Side < 0 ? (-w - board, -w) : (w, w + board);
            solids.Add(new(new Box(new Double3(b0, deck - 0.1, d.Z0 - e.SideDoorLanding), new Double3(b1, deck, d.Z1 + e.SideDoorLanding)), SurfaceKind.Deck, PartKind.RunningBoard));
        }
        if (CouplerPlate(g, l, hasCarBehind) is { } plate)
        {
            solids.Add(plate);
            // Note 905 (the director, 16 Oct 2026: "We still have this step from car one to the locomotive car"): the plate
            // rises across the gap from car 1's floor to the engine's deck in treads too shallow to feel (each a rise of
            // RampRise at most), a ramp underfoot; the plate itself, its footing and what's found by it, are as they were.
            foreach (var tread in RampTreads(plate.Box, deck))
                solids.Add(new(tread, SurfaceKind.Coupler, PartKind.Ramp));
        }
        // A footplate off the coupler plate onto the deck beside the boiler (T90): the plate is lower than the deck, so
        // without it the step from one to the other is too tall; from there, the way to the cab is forward along the boiler.
        if (hasCarBehind)
            solids.Add(new(new Box(new Double3(-w, deck - 0.1, l - 0.01), new Double3(g.PlateX - g.CouplerWidth / 2 + 0.01, deck, l + g.CouplingGap * 0.45)),
                SurfaceKind.Deck, PartKind.RunningBoard));

        double doorZ = doorFront + g.Doorway.Width / 2;
        var ladders = new List<Ladder>
        {
            // Cab steps up from the ballast on both sides, at the doorways: hung off the running board's outer edge (note
            // 559), and up them is across the board and in at the doorway, as it was when they hung off the cab's side.
            new(new Double3(w + board + 0.15, 0, doorZ), deck, new Double3(-1, 0, 0), Over: board),
            new(new Double3(-w - board - 0.15, 0, doorZ), deck, new Double3(1, 0, 0), Over: board),
        };
        // ... and at the side doors (note 856), up onto the landing outside the door: shut, it's opened from there.
        foreach (var d in plan.SideDoors)
            ladders.Add(new(new Double3(d.Side * (w + board + 0.15), 0, d.Middle), deck, new Double3(-d.Side, 0, 0)));
        // Up the engine's back onto the hood's roof (note 338: level with the cab's, and its gun, from the top of the train;
        // before the hood it went onto the boiler's top, and another ladder up the cab's back wall from there).
        if (hasCarBehind)
            ladders.Add(new Ladder(new Double3(g.EndLadderX, 0, l + 0.1), g.EngineHeight, new Double3(0, 0, -1)));

        var bunker = plan.Bunker;
        var firebox = plan.Firebox;
        var interactables = new List<Interactable>
        {
            // The firebox door, on the firebox's face against the cab's front wall (note 280), worked from behind it, facing
            // forward: the line in view over it.
            new(InteractableKind.Firebox, new Double3(firebox.Centre.X, deck, firebox.Max.Z + 0.2), 1.1, Aim: 0.55),
            // The coal at the bunker's face beside it, a half step to the left: one person between the console and the bunker
            // reaches the coal, the fire and the controls without turning round.
            new(InteractableKind.Coal, new Double3(bunker.Max.X + 0.1, deck, firebox.Max.Z + 0.4), 1.0, Aim: 0.5),
            // The blow-off cock (T97: venting slows the train). T109 playtest: in the cab, so one player works it all from
            // the footplate; note 280, on the right side wall a step behind the console, its pipe up the side window's post
            // and out of the line's view (the front windows stay clear).
            // (Where it stood in the 5 m cab, a third of the way back along its side to the doorway: note 716 keeps the footplate
            // as it was, whatever's behind it.)
            new(InteractableKind.Vent, new Double3(w - 0.25, deck, cabFront + 0.15 + VentAlong), 0.6, Aim: 1.15),
            // The tool rack on the right side, the driver's (T109): the wrench, a tool to swing.
            new(InteractableKind.ToolRack, new Double3(w - 0.3, deck, cabFront + 2.4), 0.6, Aim: 1.2),
            // The powered switch thrower's lever (spec F.3, note 196), on the driver's side behind the tool rack: clear of the
            // firebox's reach, and of the regulator, brake and reverser a reaching hand works. Only fitted does it throw
            // anything (train.json composition.switchThrower).
            new(InteractableKind.Points, new Double3(w - 0.3, deck, cabFront + 3.1), 0.4, Aim: 0.9),
            // The whistle cord (GDD §12; note 264): down from the roof in the driver's front corner, over the brake valve,
            // where the driver's view takes it in.
            new(InteractableKind.Whistle, WhistleCordAt(w, deck, cabFront), 0.8, Aim: WhistleCordHeight),
            // A sandbox on each running board behind the cab, over the drivers: out there, Use sands the rail (App. A.2's
            // counter to Grease).
            new(InteractableKind.Sandbox, new Double3(w + board / 2, deck, cabBack + e.SandboxBehind), 0.8),
            new(InteractableKind.Sandbox, new Double3(-w - board / 2, deck, cabBack + e.SandboxBehind), 0.8),
        };
        interactables.AddRange(sideDoorways);
        // The benches' seats (note 905): the Seat press stood in front of one, looking at it, sits you on it.
        for (int i = 0; i < plan.Seats.Count; i++)
        {
            var seat = plan.Seats[i];
            double rise = seat.At.Y - plan.Gallery.Max.Y;
            interactables.Add(new Interactable(InteractableKind.Bench, plan.SeatStand(seat), SeatReach, i, Aim: rise));
        }
        var cab = new Box(new Double3(-w + 0.1, deck - 0.1, cabFront), new Double3(w - 0.1, roof, cabBack));
        var bounds = new Box(new Double3(-w, 0, -l), new Double3(w, g.EngineHeight, l));

        // Forward gun on the cab roof, reached by a hatch ladder up from the cab floor behind the bunker (note 280: the front
        // left corner's the coal's now).
        var mount = new Double3(0, g.EngineHeight, cabFront + 0.8);
        ladders.Add(new Ladder(new Double3(-w + 0.45, deck, bunker.Max.Z + 0.45), g.EngineHeight, new Double3(0, 0, 1)));
        // The driver's side is the right, at the front windows: the regulator on the console before them, the brake valve
        // on the cab side ahead, and the reverser standing from the floor beside them. Each comes back (+Z) towards the
        // driver as it's worked, as on the backhead it was.
        var levers = new CabLevers(
            Regulator: new Double3(0.55, deck + 1.25, cabFront + 0.35),
            Brake: new Double3(w - 0.4, deck + 1.15, cabFront + 0.6),
            Reverser: new Double3(w - 0.35, deck + 0.95, cabFront + 1.1));
        return new CarShape(bounds, solids, ladders, interactables, cab, new GunMount(mount + new Double3(0, 0.9, 0), new Double3(0, 0, -1)),
            Doors: doors.Count > 0 ? doors : null, Levers: levers)
        {
            Lounges = plan.Seats
        };
    }

    /// <summary>How far from where a bench's sitter stands up to a seat is in reach of the Seat press (m): less than the seats are apart.</summary>
    public const double SeatReach = 0.4;

    /// <summary>
    /// How far out from the cab's side a side door's leaf stands open (m, note 856): clear of the rolled rail along the cab's
    /// waist (the shell's, out to 0.09 past the side), where a car's door steps out 0.12.
    /// </summary>
    public const double DoorOut = 0.22;

    /// <summary>The vent's handle behind the cab's front wall (m): a step behind the console (note 280), where it's always been.</summary>
    const double VentAlong = 3.8 / 3;

    /// <summary>
    /// The ramp up the engine's coupler plate (note 905), from the plate's far end (car 1's floor, its top) to its near end
    /// (the engine's <paramref name="deck"/>): treads across the plate's width, each a rise no more than <see cref="RampRise"/>,
    /// the last one level with the deck. The plate's own top is the first step, at car 1's end.
    /// </summary>
    public static IEnumerable<Box> RampTreads(Box plate, double deck)
    {
        double rise = deck - plate.Max.Y;
        if (rise <= 1e-6)
            yield break;
        int n = (int)Math.Ceiling(rise / RampRise - 1e-9);
        double length = plate.Max.Z - plate.Min.Z;
        // The gap's length in n + 1 strips: the first, at the engine, a tread at the deck's height, each behind it rise / n
        // lower, and the last, at car 1, the plate's own top.
        for (int k = 0; k < n; k++)
        {
            double z0 = plate.Min.Z + length * k / (n + 1), z1 = plate.Min.Z + length * (k + 1) / (n + 1);
            yield return new Box(new Double3(plate.Min.X, plate.Min.Y, z0), new Double3(plate.Max.X, deck - rise * k / n, z1));
        }
    }

    /// <summary>The ramp's treads' rise (m): a pebble's, not a step's (player.json stepUp 0.35).</summary>
    public const double RampRise = 0.05;

    /// <summary>How far short of the engine's rear end the boiler's smokebox stops: the rear deck, stepped onto from car 1.</summary>
    public const double BoilerToEnd = 0.5;

    /// <summary>How high over the cab floor the whistle cord's handle hangs at rest (m): a hand above the shoulder.</summary>
    public const double WhistleCordHeight = 1.8;

    /// <summary>
    /// The whistle cord's footing in the engine's frame (note 264): the driver's front corner, over the brake valve, hung
    /// against the cab side (note 276: cab forward, further in it hung in the driver's window, across the line).
    /// </summary>
    public static Double3 WhistleCordAt(double halfWidth, double deck, double cabFront) => new(halfWidth - 0.24, deck, cabFront + 0.9);
}

/// <summary>
/// One of the cab's side doors at its front (note 856), in the engine's frame: its side (−1 left, the coal's; +1 right) and
/// its opening along the cab, <see cref="Z0"/> to <see cref="Z1"/>. Its bit in <see cref="Vehicle.DoorsOpen"/> is a car's
/// sliding side door's on that side (<see cref="Index"/>: 2 left, 3 right).
/// </summary>
public readonly record struct CabSideDoor(int Side, double Z0, double Z1)
{
    public int Index => Side < 0 ? 2 : 3;
    public double Middle => (Z0 + Z1) / 2;
    /// <summary>Which way its leaf slides open (<see cref="Door.Slide"/>): the left one back, the right one forward, each over plate, not glass.</summary>
    public int Slide => Side < 0 ? 1 : -1;
}

/// <summary>
/// The cab-forward engine's plan (ARCHITECTURE §8 notes 276, 280, 716) in its frame, from train.json: where the cab's front
/// windows and back wall are, where its doorways start, the coal bunker and the firebox at its front, and the stack; and the
/// crew car behind the footplate (note 716): the chart table, the gallery's raised floor and its benches. The sim, the bots
/// and the art all read it here.
/// </summary>
public readonly record struct EnginePlan(double Half, double CabFront, double CabBack, double DoorFront, Box Bunker, double StackZ, Box Firebox)
{
    /// <summary>The chart table (note 716): in the middle of the cab behind the footplate, its top the map's.</summary>
    public Box ChartTable { get; init; }
    /// <summary>The gallery's raised floor (note 716), wall to wall, its top the floor stood on.</summary>
    public Box Gallery { get; init; }
    /// <summary>The treads half way up to the gallery, at its front and its back, wall to wall.</summary>
    public Box FrontTread { get; init; }
    public Box BackTread { get; init; }
    /// <summary>The benches on the gallery (note 905), one along each side wall facing across the cab, left then right.</summary>
    public IReadOnlyList<Box> Benches { get; init; } = [];
    /// <summary>
    /// The places to sit on them (note 905), left bench's then right's, each from the bench's back end: where a sitter's feet
    /// are held (the seat's top, <see cref="SeatBack"/> off the wall) and which way they face, across the cab.
    /// </summary>
    public IReadOnlyList<Lounge> Seats { get; init; } = [];
    /// <summary>How far off the cab's side wall a bench's sitter is held (m): the pelvis's, sat back against it, on the perch.</summary>
    public const double SeatBack = 0.1;

    /// <summary>
    /// Where a bench's sitter stands, in front of their seat on the gallery's floor (note 905): got up, they're put there, and
    /// it's where the seat's in reach from. A body's radius and a little out past the bench's edge.
    /// </summary>
    public Double3 SeatStand(Lounge seat)
    {
        var bench = Benches.First(b => Math.Sign(b.Centre.X) == Math.Sign(seat.At.X));
        double edge = seat.At.X < 0 ? bench.Max.X : bench.Min.X;
        return new Double3(edge - Math.Sign(seat.At.X) * StandOff, bench.Min.Y, seat.At.Z);
    }

    /// <summary>How far out from a bench's edge its sitter stands up (m): a body's radius and a little (player.json radius 0.3).</summary>
    public const double StandOff = 0.35;
    /// <summary>
    /// The ways down the cab either side of the chart table (m off the centreline): a walker going between the footplate and
    /// the back wall keeps to one (<see cref="CabWay"/>), clear of the benches along the gallery's sides past it too (note 905).
    /// </summary>
    public double Lane { get; init; }
    /// <summary>
    /// The cab's side doors at its front (note 856): the left one beside the coal, the right one across the cab behind the
    /// points lever; each only where it stands clear of the back wall's doorway (none on the right in the old 5 m cab).
    /// </summary>
    public IReadOnlyList<CabSideDoor> SideDoors { get; init; } = [];

    public static EnginePlan Of(GeometryTuning g)
    {
        var e = g.Engine;
        double l = g.EngineLength / 2, w = g.RoofWidth / 2;
        double cabFront = -l + e.PilotLength, cabBack = cabFront + e.CabLength;
        // Each side's doorway at the back of the cab, inside the back corner pillar.
        double doorFront = cabBack - 0.15 - g.Doorway.Width;
        // Note 280: the front of the cab is the work. In its front left corner, the coal bunker back along the left wall;
        // against the front wall beside it, the firebox under the window sill; the driver's console under the right window.
        double wall = cabFront + 0.15;
        var bunker = new Box(new Double3(-w + 0.1, e.DeckHeight, wall), new Double3(-w + 0.1 + e.BunkerDepth, e.DeckHeight + e.BunkerHeight, wall + e.BunkerLength));
        double fx = bunker.Max.X + 0.02;
        var firebox = new Box(new Double3(fx, e.DeckHeight, wall), new Double3(fx + e.FireboxWidth, e.DeckHeight + e.FireboxHeight, wall + e.FireboxDepth));
        // The stack over the smokebox, near the boiler's rear end.
        double stackZ = l - CarShape.BoilerToEnd - 1.25;
        // Note 716: behind the footplate, down the middle, the chart table and then the gallery's benches; the gallery's floor
        // and its treads wall to wall. (A shorter cab than the crew car wants has as much of it as fits: none, at the old 5 m,
        // but the table; the treads and the floor clipped to the cab.)
        var c = e.CrewCar;
        double inner = w - 0.1, deck = e.DeckHeight, back = cabBack - 0.15;
        double z(double behind) => Math.Min(cabFront + behind, back);
        var table = new Box(new Double3(-c.TableWidth / 2, deck, z(c.TableFrom)), new Double3(c.TableWidth / 2, deck + c.TableHeight, z(c.TableFrom + c.TableLength)));
        double rise = deck + c.GalleryRise;
        var gallery = new Box(new Double3(-inner, deck, z(c.GalleryFrom)), new Double3(inner, rise, z(c.GalleryTo)));
        var frontTread = new Box(new Double3(-inner, deck, z(c.GalleryFrom - c.GalleryStep)), new Double3(inner, deck + c.GalleryRise / 2, z(c.GalleryFrom)));
        var backTread = new Box(new Double3(-inner, deck, z(c.GalleryTo)), new Double3(inner, deck + c.GalleryRise / 2, z(c.GalleryTo + c.GalleryStep)));
        // Note 905 (the director, 16 Oct 2026: "move them to like side benches so that we keep the middle clear"): one along
        // each side wall on the gallery, left then right, facing across the cab; the seats on each from its back end.
        var benches = new List<Box>();
        var seats = new List<Lounge>();
        double b0 = Math.Max(cabFront + c.BenchFrom, gallery.Min.Z), b1 = Math.Min(cabFront + c.BenchTo, gallery.Max.Z);
        if (c.BenchDepth > 0 && b1 - b0 >= c.SeatEvery)
            foreach (int side in new[] { -1, 1 })
            {
                double edge = side * (inner - c.BenchDepth);
                benches.Add(new Box(new Double3(Math.Min(edge, side * inner), rise, b0), new Double3(Math.Max(edge, side * inner), rise + c.BenchHeight, b1)));
                // Sat back against the wall (the motor holds a sitter where they sit, PlayerMotor), facing across the cab
                // (yaw: 0 faces −Z, π/2 faces −X), the knees over the seat's edge.
                for (int k = 0; k < c.BenchSeats && b1 - c.SeatEvery * (k + 0.5) >= b0 + c.SeatEvery / 2 - 1e-9; k++)
                    seats.Add(new Lounge(LoungeKind.Bench, new Double3(side * (inner - SeatBack), rise + c.BenchHeight, b1 - c.SeatEvery * (k + 0.5)), side * Math.PI / 2));
            }
        // Half way between the chart table's edge and the cab's side. (The benches' edge is a body's radius past it, note 905.)
        double lane = (c.TableWidth / 2 + inner) / 2;
        var sideDoors = new List<CabSideDoor>();
        foreach (var (side, behind) in new[] { (-1, e.CoalDoorFrom), (1, e.FrontDoorFrom) })
            if (behind > 0 && cabFront + behind + g.Doorway.Width <= doorFront - 0.5)
                sideDoors.Add(new CabSideDoor(side, cabFront + behind, cabFront + behind + g.Doorway.Width));
        return new(l, cabFront, cabBack, doorFront, bunker, stackZ, firebox)
        {
            SideDoors = sideDoors,
            ChartTable = table,
            Gallery = gallery,
            FrontTread = frontTread,
            BackTread = backTread,
            Benches = benches,
            Seats = seats,
            Lane = lane,
        };
    }

    /// <summary>How far behind the engine's front the coal bunker's middle is: where a coaling spout pours (spec D.4).</summary>
    public double CoalFromFront => Half + Bunker.Centre.Z;

    /// <summary>
    /// The crew car's furniture a walker goes round (note 716): the chart table and the benches along the gallery's sides
    /// (note 905), in the engine's frame. Not the gallery's floor and treads: those are walked up.
    /// </summary>
    public IEnumerable<Box> Furniture => Benches.Prepend(ChartTable).Where(b => b.Max.Z > b.Min.Z);

    /// <summary>
    /// The next place to make for in the cab from <paramref name="from"/> on the way to <paramref name="to"/> (note 716; both in
    /// the engine's frame): <paramref name="to"/> itself when nothing of the crew car's furniture stands between, else into
    /// the way down the cab on <paramref name="to"/>'s side of it (or <paramref name="from"/>'s, going to the middle), short of
    /// the furniture, and along it. A body <paramref name="radius"/> round is kept clear.
    /// </summary>
    public Double3 CabWay(Double3 from, Double3 to, double radius = 0.35)
    {
        if (!Furniture.Any(f => Crosses(from, to, f, radius)))
            return to;
        double toSide = Math.Abs(to.X) > 0.2 ? Math.Sign(to.X) : Math.Abs(from.X) > 0.05 ? Math.Sign(from.X) : -1;
        // Its own legs, across into a way and along it, need only miss the furniture itself by a little: a walker already
        // close by a face slides along it (PlayerMotor's push out) as it goes.
        double skim = Math.Max(0.05, radius - 0.2);
        foreach (double side in new[] { toSide, -toSide })
        {
            double lane = side * Lane;
            // Out in the way already: along it to abreast of where it's going, then across.
            if (Math.Abs(from.X - lane) < 0.15)
            {
                var along = to with { X = lane };
                if (!Furniture.Any(f => Crosses(from, along, f, skim)))
                    return along;
                continue;
            }
            // Across into it where that's clear: abreast of where it is, or (short of the furniture) just short of it, so a
            // walker from the footplate doesn't cut the bunker's corner (note 280's, along the left wall) getting there.
            double front = Furniture.Min(f => f.Min.Z), rear = Furniture.Max(f => f.Max.Z);
            double clear = radius + 0.05;
            double ez = from.Z < front - clear ? Math.Max(from.Z, front - clear) : from.Z > rear + clear ? Math.Min(from.Z, rear + clear) : from.Z;
            var into = new Double3(lane, from.Y, ez);
            if (!Furniture.Any(f => Crosses(from, into, f, skim)))
                return into;
        }
        // Hemmed in (abreast of the furniture with it between it and both ways: never, the ways being either side): on as it was.
        return to;
    }

    /// <summary>Whether the way from <paramref name="a"/> to <paramref name="b"/> (flat) passes within <paramref name="r"/> of the box.</summary>
    static bool Crosses(Double3 a, Double3 b, Box box, double r)
    {
        double x0 = box.Min.X - r, x1 = box.Max.X + r, z0 = box.Min.Z - r, z1 = box.Max.Z + r;
        // Slab test on the flat segment.
        double t0 = 0, t1 = 1, dx = b.X - a.X, dz = b.Z - a.Z;
        foreach (var (p, d, lo, hi) in new[] { (a.X, dx, x0, x1), (a.Z, dz, z0, z1) })
        {
            if (Math.Abs(d) < 1e-9)
            {
                if (p < lo || p > hi)
                    return false;
                continue;
            }
            double u = (lo - p) / d, v = (hi - p) / d;
            if (u > v)
                (u, v) = (v, u);
            t0 = Math.Max(t0, u);
            t1 = Math.Min(t1, v);
            if (t0 > t1)
                return false;
        }
        return true;
    }
}
