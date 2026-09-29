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
public enum PartKind : byte { Body, Chassis, Boiler, Stack, CabWall, CabRoof, Tender, Coupler, GunMount }

/// <summary>Where a gun is bolted on, and which way it faces in the car's frame (−Z forward, +Z back).</summary>
public readonly record struct GunMount(Double3 Position, Double3 Facing);

public readonly record struct Solid(Box Box, SurfaceKind Top, PartKind Part);

/// <summary>A ladder fixed to a face: its foot, how high it goes, and which way is "onto" what it serves.</summary>
public readonly record struct Ladder(Double3 Foot, double Top, Double3 Inward);

public enum InteractableKind : byte { Firebox, Vent, Handbrake }

/// <summary>A thing a player uses by standing near it and holding Use.</summary>
public readonly record struct Interactable(InteractableKind Kind, Double3 Position, double Radius);

/// <summary>
/// Greybox collision for one car in its own frame: solids to stand on and bump into, ladders,
/// interactables, and (on the engine) the cab volume that makes a player the crew in charge.
/// </summary>
public sealed record CarShape(Box Bounds, IReadOnlyList<Solid> Solids, IReadOnlyList<Ladder> Ladders, IReadOnlyList<Interactable> Interactables, Box? Cab,
    GunMount? Gun = null)
{
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
        var car = Car(g, hasCarBehind);
        double l = g.CarLength / 2, h = g.CarHeight;
        var mount = new Double3(0, h, l - 1.6);
        var solids = car.Solids.Append(new Solid(Box.FromCentre(mount + new Double3(0, 0.25, 0), new Double3(0.35, 0.25, 0.35)), SurfaceKind.Roof, PartKind.GunMount)).ToList();
        // The brake wheel moves to the front end so it isn't under the gun.
        var interactables = new[] { new Interactable(InteractableKind.Handbrake, new Double3(0, h, -l + 0.5), 0.8) };
        return car with { Solids = solids, Interactables = interactables, Gun = new GunMount(mount + new Double3(0, 0.9, 0), new Double3(0, 0, 1)) };
    }

    static Solid? CouplerPlate(GeometryTuning g, double halfLength, bool hasCarBehind) => hasCarBehind
        ? new Solid(new Box(new Double3(-g.CouplerWidth / 2, g.CouplerHeight - 0.1, halfLength), new Double3(g.CouplerWidth / 2, g.CouplerHeight, halfLength + g.CouplingGap)),
            SurfaceKind.Coupler, PartKind.Coupler)
        : null;

    static CarShape Car(GeometryTuning g, bool hasCarBehind)
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
