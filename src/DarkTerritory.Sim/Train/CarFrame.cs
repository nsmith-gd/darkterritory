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
    public double Heading => Math.Atan2(Back.X, Back.Z);
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

/// <summary>What a solid is, so presentation can draw and colour it. Collision ignores this.</summary>
public enum PartKind : byte { Body, Chassis, Boiler, Stack, CabWall, CabRoof, Tender, Coupler, GunMount, Wall, Cargo, Locker, Steps }

/// <summary>Where a gun is bolted on, and which way it faces in the car's frame (−Z forward, +Z back).</summary>
public readonly record struct GunMount(Double3 Position, Double3 Facing);

public readonly record struct Solid(Box Box, SurfaceKind Top, PartKind Part);

/// <summary>A ladder fixed to a face: its foot, how high it goes, and which way is "onto" what it serves.</summary>
public readonly record struct Ladder(Double3 Foot, double Top, Double3 Inward);

public enum InteractableKind : byte { Firebox, Vent, Handbrake, Door }

/// <summary>A thing a player uses by standing near it and holding Use. <see cref="Index"/> says which door.</summary>
public readonly record struct Interactable(InteractableKind Kind, Double3 Position, double Radius, int Index = 0);

/// <summary>A hinged door: solid while shut. <see cref="Index"/> is its bit in <see cref="Vehicle.DoorsOpen"/>.</summary>
public readonly record struct Door(Box Box, int Index);

/// <summary>
/// Greybox collision for one car in its own frame: solids to stand on and bump into, ladders,
/// interactables, and (on the engine) the cab volume that makes a player the crew in charge.
/// </summary>
public sealed record CarShape(Box Bounds, IReadOnlyList<Solid> Solids, IReadOnlyList<Ladder> Ladders, IReadOnlyList<Interactable> Interactables, Box? Cab,
    GunMount? Gun = null, Box? Interior = null, IReadOnlyList<Door>? Doors = null)
{
    public IReadOnlyList<Door> DoorList => Doors ?? [];
    /// <summary>End ladders sit to the right of the coupler so they don't collide with the plate.</summary>
    public const double EndLadderX = 0.55;

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

    public static CarShape Build(GeometryTuning g, VehicleKind kind, bool hasCarBehind) => kind switch
    {
        VehicleKind.Engine => Engine(g, hasCarBehind),
        VehicleKind.Guard => Guard(g, hasCarBehind),
        _ => Car(g, hasCarBehind),
    };

    public static CarShape Build(GeometryTuning g, bool isEngine, bool hasCarBehind) =>
        Build(g, isEngine ? VehicleKind.Engine : VehicleKind.Cargo, hasCarBehind);

    /// <summary>A car with the rear gun on its roof, facing back down the line.</summary>
    static CarShape Guard(GeometryTuning g, bool hasCarBehind)
    {
        var car = g.Interior is { } layout ? Shell(g, layout, hasCarBehind, cargo: false) : SolidCar(g, hasCarBehind);
        double l = g.CarLength / 2, h = g.CarHeight;
        var mount = new Double3(0, h, l - 1.6);
        var solids = car.Solids.Append(new Solid(Box.FromCentre(mount + new Double3(0, 0.25, 0), new Double3(0.35, 0.25, 0.35)), SurfaceKind.Roof, PartKind.GunMount)).ToList();
        // The brake wheel moves to the front end so it isn't under the gun.
        var interactables = car.Interactables.Where(i => i.Kind != InteractableKind.Handbrake)
            .Append(new Interactable(InteractableKind.Handbrake, new Double3(0, h, -l + 0.5), 0.8)).ToList();
        var ladders = car.Ladders.ToList();
        if (g.Interior is { } i)
        {
            // Tool storage along the left wall (GDD §10), and a hatch ladder up to the gun from inside.
            double w = g.RoofWidth / 2;
            solids.Add(new Solid(new Box(new Double3(-w + i.WallThickness, i.FloorHeight, -l + 1.5), new Double3(-w + i.WallThickness + 0.5, i.FloorHeight + 1.8, -l + 3.5)), SurfaceKind.Deck, PartKind.Locker));
            ladders.Add(new Ladder(new Double3(0.6, i.FloorHeight, l - 2.4), h, new Double3(0, 0, -1)));
        }
        return car with { Solids = solids, Interactables = interactables, Ladders = ladders, Gun = new GunMount(mount + new Double3(0, 0.9, 0), new Double3(0, 0, 1)) };
    }

    static Solid? CouplerPlate(GeometryTuning g, double halfLength, bool hasCarBehind) => hasCarBehind
        ? new Solid(new Box(new Double3(-g.CouplerWidth / 2, g.CouplerHeight - 0.1, halfLength), new Double3(g.CouplerWidth / 2, g.CouplerHeight, halfLength + g.CouplingGap)),
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
        double d0 = i.DoorX - i.DoorWidth / 2, d1 = i.DoorX + i.DoorWidth / 2, lintel = floor + i.DoorHeight;
        double sd = i.SideDoorWidth / 2;
        var solids = new List<Solid>
        {
            new(new Box(new Double3(-w, 0, -l), new Double3(w, floor, l)), SurfaceKind.Deck, PartKind.Chassis),
            new(new Box(new Double3(-w, ceiling, -l), new Double3(w, h, l)), SurfaceKind.Roof, PartKind.Body),
        };
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
            new(new Double3(EndLadderX, 0, -l - 0.1), h, new Double3(0, 0, 1)),
        };
        if (hasCarBehind)
            ladders.Add(new Ladder(new Double3(EndLadderX, 0, l + 0.1), h, new Double3(0, 0, -1)));
        interactables.Add(new Interactable(InteractableKind.Handbrake, new Double3(0, h, l - 0.5), 0.8));
        var interior = new Box(new Double3(-w + t, floor - 0.1, -l + t), new Double3(w - t, ceiling, l - t));
        return new CarShape(new Box(new Double3(-w, 0, -l), new Double3(w, h, l)), solids, ladders, interactables, null, Interior: interior, Doors: doors);
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
            new(new Double3(EndLadderX, 0, -l - 0.1), h, new Double3(0, 0, 1)),
        };
        if (hasCarBehind)
            ladders.Add(new Ladder(new Double3(EndLadderX, 0, l + 0.1), h, new Double3(0, 0, -1)));
        // Brake wheel on the roof at the rear end, above the end ladder.
        var interactables = new[] { new Interactable(InteractableKind.Handbrake, new Double3(0, h, l - 0.5), 0.8) };
        return new CarShape(new Box(new Double3(-w, 0, -l), new Double3(w, h, l)), solids, ladders, interactables, null);
    }

    /// <summary>
    /// Locomotive and tender as one 20 m unit (spec B.4): boiler forward, open cab, coal tender behind.
    /// The cab is walkable: the firebox, vent and controls are in it, and GDD §12's Conductor and Boiler
    /// roles are simply whoever is standing there.
    /// </summary>
    static CarShape Engine(GeometryTuning g, bool hasCarBehind)
    {
        var e = g.Engine;
        double w = g.RoofWidth / 2, l = g.EngineLength / 2;
        double cabFront = l - e.TenderLength - e.CabLength, cabBack = l - e.TenderLength;
        double deck = e.DeckHeight;
        var solids = new List<Solid>
        {
            new(new Box(new Double3(-w, 0, -l), new Double3(w, deck, l)), SurfaceKind.Deck, PartKind.Chassis),
            new(new Box(new Double3(-e.BoilerHalfWidth, deck, -l + 0.5), new Double3(e.BoilerHalfWidth, e.BoilerTop, cabFront)), SurfaceKind.Roof, PartKind.Boiler),
            new(new Box(new Double3(-0.35, e.BoilerTop, -l + 1.4), new Double3(0.35, e.BoilerTop + 1.0, -l + 2.1)), SurfaceKind.Roof, PartKind.Stack),
            new(new Box(new Double3(-w - 0.1, g.EngineHeight - 0.2, cabFront), new Double3(w + 0.1, g.EngineHeight, cabBack)), SurfaceKind.Roof, PartKind.CabRoof),
            new(new Box(new Double3(-w, deck, cabBack), new Double3(w, e.TenderTop, l)), SurfaceKind.Roof, PartKind.Tender),
        };
        // Cab sides are waist-high with a doorway at the back of each side, and corner pillars hold the roof.
        double doorFront = cabBack - e.DoorWidth;
        foreach (int side in new[] { -1, 1 })
        {
            double inner = side * (w - 0.1), outer = side * w;
            var (x0, x1) = (Math.Min(inner, outer), Math.Max(inner, outer));
            solids.Add(new(new Box(new Double3(x0, deck, cabFront), new Double3(x1, deck + 1.1, doorFront)), SurfaceKind.Deck, PartKind.CabWall));
            solids.Add(new(new Box(new Double3(x0, deck, cabFront), new Double3(x1, g.EngineHeight - 0.2, cabFront + 0.15)), SurfaceKind.Deck, PartKind.CabWall));
            solids.Add(new(new Box(new Double3(x0, deck, cabBack - 0.15), new Double3(x1, g.EngineHeight - 0.2, cabBack)), SurfaceKind.Deck, PartKind.CabWall));
        }
        if (CouplerPlate(g, l, hasCarBehind) is { } plate)
            solids.Add(plate);

        double doorZ = doorFront + e.DoorWidth / 2;
        var ladders = new List<Ladder>
        {
            // Cab steps up from the ballast on both sides, at the doorways.
            new(new Double3(w + 0.15, 0, doorZ), deck, new Double3(-1, 0, 0)),
            new(new Double3(-w - 0.15, 0, doorZ), deck, new Double3(1, 0, 0)),
        };
        if (hasCarBehind)
            ladders.Add(new Ladder(new Double3(EndLadderX, 0, l + 0.1), e.TenderTop, new Double3(0, 0, -1)));

        var interactables = new List<Interactable>
        {
            new(InteractableKind.Firebox, new Double3(0, deck, cabFront + 0.2), 1.1),
            new(InteractableKind.Vent, new Double3(-w + 0.4, deck, cabFront + 0.9), 0.7),
        };
        var cab = new Box(new Double3(-w + 0.1, deck - 0.1, cabFront), new Double3(w - 0.1, g.EngineHeight - 0.2, cabBack));
        var bounds = new Box(new Double3(-w, 0, -l), new Double3(w, g.EngineHeight, l));

        // Forward gun on the cab roof, reached by a hatch ladder up from the cab floor.
        var mount = new Double3(0, g.EngineHeight, cabFront + 0.8);
        solids.Add(new Solid(Box.FromCentre(mount + new Double3(0, 0.25, 0), new Double3(0.35, 0.25, 0.35)), SurfaceKind.Roof, PartKind.GunMount));
        ladders.Add(new Ladder(new Double3(-w + 0.45, deck, cabBack - 0.35), g.EngineHeight, new Double3(0, 0, -1)));
        return new CarShape(bounds, solids, ladders, interactables, cab, new GunMount(mount + new Double3(0, 0.9, 0), new Double3(0, 0, -1)));
    }
}
