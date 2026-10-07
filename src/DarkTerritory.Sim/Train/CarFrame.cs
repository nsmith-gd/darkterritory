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
public enum PartKind : byte { Body, Chassis, Boiler, Stack, CabWall, CabRoof, Tender, Coupler, GunMount, Wall, Cargo, Locker, Steps, RunningBoard, Hatch, CrewLocker, Stove, Bunk, Firebox }

/// <summary>Where a gun is bolted on, and which way it faces in the car's frame (−Z forward, +Z back).</summary>
/// <summary>
/// Where a gun is and which way it faces, in its car's frame: <see cref="CarShape.Gun"/> is where it stands at departure;
/// where it is now is the vehicle's (<see cref="Combat.Guns.Mount"/>, T93).
/// </summary>
public readonly record struct GunMount(Double3 Position, Double3 Facing);

public readonly record struct Solid(Box Box, SurfaceKind Top, PartKind Part)
{
    /// <summary>There to stand on and bump into on this vehicle now: everything but an open roof hatch's lid (T99).</summary>
    public bool Present(Vehicle v) => Part != PartKind.Hatch || !v.DoorOpen(CarShape.HatchBit);
}

/// <summary>A ladder fixed to a face: its foot, how high it goes, and which way is "onto" what it serves.</summary>
public readonly record struct Ladder(Double3 Foot, double Top, Double3 Inward);

/// <summary>
/// <see cref="Coal"/> is the tender's coal face, where a hand fills the shovel (T29). <see cref="Locker"/> is a crew locker's
/// door (its <see cref="Interactable.Index"/> the locker's), worked like a car door and wanting facing as one does.
/// </summary>
/// <remarks>
/// <see cref="Points"/> is the powered switch thrower's lever in the cab (spec F.3; note 196): it works only fitted.
/// <see cref="Whistle"/> is the whistle cord's handle (GDD §12; note 264): Use held there blows the engine's whistle, and
/// only when it's looked at (<see cref="CrewActions.NearestInteractable"/>), so nobody pulls it reaching for the firebox.
/// </remarks>
public enum InteractableKind : byte { Firebox, Vent, Handbrake, Door, Coal, Sandbox, Hatch, ToolRack, Locker, Points, Whistle }

/// <summary>
/// A thing a player uses by standing near it and holding Use. <see cref="Index"/> says which door. <see cref="Aim"/> is how
/// far over <see cref="Position"/> (its footing) the thing itself is, the point a look picks it by (note 264): where two
/// are in reach, the one looked at is the one worked.
/// </summary>
public readonly record struct Interactable(InteractableKind Kind, Double3 Position, double Radius, int Index = 0, double Aim = 1.0);

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

/// <summary>A hinged door: solid while shut. <see cref="Index"/> is its bit in <see cref="Vehicle.DoorsOpen"/>.</summary>
public readonly record struct Door(Box Box, int Index);

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
    /// <summary>A crew car's stove (note 184), standing on its floor: the heat a crew car has of its own.</summary>
    public Box? Stove { get; init; }

    public double HalfLength => Bounds.Max.Z;
    public double HalfWidth => Bounds.Max.X;
    /// <summary>Height of the highest walkable roof.</summary>
    public double RoofHeight => Bounds.Max.Y;

    /// <summary>The highest walkable surface over a point in the car's frame (at or below <paramref name="maxY"/>), and what kind it is.</summary>
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
    public static CarShape Build(GeometryTuning g, VehicleKind kind, bool hasCarBehind, LockerTuning? lockers = null) =>
        lockers is { Names.Count: > 0 } && kind != VehicleKind.Engine && g.Interior is not null
            ? WithLockers(Build(g, kind, hasCarBehind), lockers) : Build(g, kind, hasCarBehind);

    public static CarShape Build(GeometryTuning g, VehicleKind kind, bool hasCarBehind) => kind switch
    {
        // The engine's rail is the cab roof's (T93; note 276): the gun stands at its front, and behind it is the boiler.
        VehicleKind.Engine => Engine(g, hasCarBehind) with
        {
            RoofRail = (EnginePlan.Of(g).CabFront + RailEnd, EnginePlan.Of(g).CabBack - RailEnd),
        },
        VehicleKind.Guard => Guard(g, hasCarBehind) with { RoofRail = (-g.CarLength / 2 + RailEnd, g.CarLength / 2 - RailEnd) },
        VehicleKind.Utility => Utility(g, hasCarBehind) with { RoofRail = (-g.CarLength / 2 + RailEnd, g.CarLength / 2 - RailEnd) },
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
    /// The roof's brake wheel stands inboard of the end ladder's top: a reaching hand works it from the ladder (T29), and
    /// standing on the ladder you're just out of reach of it.
    /// </summary>
    static double BrakeWheelX(GeometryTuning g) => g.EndLadderX - 0.55;

    /// <summary>The plate across the gap behind a car, from its end to the next car's, on the end doors' line (<see cref="GeometryTuning.PlateX"/>).</summary>
    static Solid? CouplerPlate(GeometryTuning g, double halfLength, bool hasCarBehind) => hasCarBehind
        ? new Solid(new Box(new Double3(g.PlateX - g.CouplerWidth / 2, g.CouplerHeight - 0.1, halfLength), new Double3(g.PlateX + g.CouplerWidth / 2, g.CouplerHeight, halfLength + g.CouplingGap)),
            SurfaceKind.Coupler, PartKind.Coupler)
        : null;

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
            // The load stands down the right-hand side, either side of its door.
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
        // The hatch's handle: on the roof at its front edge, left of the gun rail, worked from the roof walk.
        if (hatch is { } hb)
            interactables.Add(new Interactable(InteractableKind.Hatch, new Double3(-0.6, h, hb.Min.Z - 0.35), 0.8, HatchBit));
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
            new(new Box(new Double3(-0.35, e.BoilerTop, plan.StackZ - 0.35), new Double3(0.35, e.BoilerTop + 1.0, plan.StackZ + 0.35)), SurfaceKind.Roof, PartKind.Stack),
            new(new Box(new Double3(-w - 0.1, roof, cabFront), new Double3(w + 0.1, g.EngineHeight, cabBack)), SurfaceKind.Roof, PartKind.CabRoof),
            // The coal bunker in the cab's front left corner, and the firebox against the front wall beside it (note 280).
            new(plan.Bunker, SurfaceKind.Deck, PartKind.Tender),
            new(plan.Firebox, SurfaceKind.Deck, PartKind.Firebox),
        };
        // The cab's front: waist-high under its windows, the driver's console behind it; pillars at its corners hold the
        // roof, and the windows between them are the driver's view of the line.
        solids.Add(new(new Box(new Double3(-w, deck, cabFront), new Double3(w, deck + 1.1, cabFront + 0.15)), SurfaceKind.Deck, PartKind.CabWall));
        foreach (int side in new[] { -1, 1 })
        {
            double inner = side * (w - 0.1), outer = side * w;
            var (x0, x1) = (Math.Min(inner, outer), Math.Max(inner, outer));
            // Sides waist-high with a doorway at the back of each; corner pillars; the standard lintel over each doorway
            // (train.json doorway: the cab roof is higher, but a door is a door, note 110).
            solids.Add(new(new Box(new Double3(x0, deck, cabFront), new Double3(x1, deck + 1.1, doorFront)), SurfaceKind.Deck, PartKind.CabWall));
            solids.Add(new(new Box(new Double3(x0, deck, cabFront), new Double3(x1, roof, cabFront + 0.15)), SurfaceKind.Deck, PartKind.CabWall));
            solids.Add(new(new Box(new Double3(x0, deck, cabBack - 0.15), new Double3(x1, roof, cabBack)), SurfaceKind.Deck, PartKind.CabWall));
            solids.Add(new(new Box(new Double3(x0, deck + g.Doorway.Height, doorFront), new Double3(x1, roof, cabBack - 0.15)), SurfaceKind.Deck, PartKind.CabWall));
            // The back wall either side of the boiler's end.
            double b0 = side < 0 ? -w + 0.1 : e.BoilerHalfWidth, b1 = side < 0 ? -e.BoilerHalfWidth : w - 0.1;
            solids.Add(new(new Box(new Double3(b0, deck, cabBack - 0.15), new Double3(b1, roof, cabBack)), SurfaceKind.Deck, PartKind.CabWall));
        }
        // ... and across it (note 280: the firebox is at the front now; the boiler's end is plated over).
        solids.Add(new(new Box(new Double3(-e.BoilerHalfWidth, deck, cabBack - 0.15), new Double3(e.BoilerHalfWidth, roof, cabBack)), SurfaceKind.Deck, PartKind.CabWall));
        // The running boards (App. A.2 GREASE: "sends someone onto the running boards at speed"): a walkway each side at
        // deck height, out past the body's side, from partway across the cab's doorway (out of it and back onto it) back
        // along the boiler to the engine's rear. Its front stops short of the doorway's front, where the cab steps come up.
        double board = e.RunningBoardWidth, boardFront = doorFront + g.Doorway.Width * 0.6;
        foreach (int side in new[] { -1, 1 })
        {
            var (x0, x1) = side < 0 ? (-w - board, -w) : (w, w + board);
            solids.Add(new(new Box(new Double3(x0, deck - 0.1, boardFront), new Double3(x1, deck, l - 0.3)), SurfaceKind.Deck, PartKind.RunningBoard));
        }
        if (CouplerPlate(g, l, hasCarBehind) is { } plate)
            solids.Add(plate);
        // A footplate off the coupler plate onto the deck beside the boiler (T90): the plate is lower than the deck, so
        // without it the step from one to the other is too tall; from there, the way to the cab is forward along the boiler.
        if (hasCarBehind)
            solids.Add(new(new Box(new Double3(-w, deck - 0.1, l - 0.01), new Double3(g.PlateX - g.CouplerWidth / 2 + 0.01, deck, l + g.CouplingGap * 0.45)),
                SurfaceKind.Deck, PartKind.RunningBoard));

        double doorZ = doorFront + g.Doorway.Width / 2;
        var ladders = new List<Ladder>
        {
            // Cab steps up from the ballast on both sides, at the doorways.
            new(new Double3(w + 0.15, 0, doorZ), deck, new Double3(-1, 0, 0)),
            new(new Double3(-w - 0.15, 0, doorZ), deck, new Double3(1, 0, 0)),
            // Up the cab's back wall from the boiler's top to the cab roof and its gun, from the top of the train (T90).
            new(new Double3(g.EndLadderX, e.BoilerTop, cabBack + 0.15), g.EngineHeight, new Double3(0, 0, -1)),
        };
        if (hasCarBehind)
            ladders.Add(new Ladder(new Double3(g.EndLadderX, 0, l + 0.1), e.BoilerTop, new Double3(0, 0, -1)));

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
            new(InteractableKind.Vent, new Double3(w - 0.25, deck, cabFront + 0.15 + (doorFront - cabFront - 0.15) / 3), 0.6, Aim: 1.15),
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
        return new CarShape(bounds, solids, ladders, interactables, cab, new GunMount(mount + new Double3(0, 0.9, 0), new Double3(0, 0, -1)), Levers: levers);
    }

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
/// The cab-forward engine's plan (ARCHITECTURE §8 notes 276, 280) in its frame, from train.json: where the cab's front
/// windows and back wall are, where its doorways start, the coal bunker and the firebox at its front, and the stack. The
/// sim, the bots and the art all read it here.
/// </summary>
public readonly record struct EnginePlan(double Half, double CabFront, double CabBack, double DoorFront, Box Bunker, double StackZ, Box Firebox)
{
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
        return new(l, cabFront, cabBack, doorFront, bunker, stackZ, firebox);
    }

    /// <summary>How far behind the engine's front the coal bunker's middle is: where a coaling spout pours (spec D.4).</summary>
    public double CoalFromFront => Half + Bunker.Centre.Z;
}
