using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

/// <summary>
/// Named camera setups for screenshots, so an agent or a designer can ask for "the roof of car 2"
/// instead of coordinates. Each view also places the engine's headlamp.
/// </summary>
public static class Views
{
    public static readonly string[] Names = ["trackside", "roof", "cab", "fireman", "chase", "ahead", "gap", "gangway", "gun"];

    public static Camera Get(string name, TrainOnLine train, int car = 2)
    {
        var engine = train.Frames[0];
        var target = train.Frames[Math.Min(car, train.Frames.Count - 1)];
        double roof = target.Shape.RoofHeight;
        double engineHalf = engine.Shape.HalfLength;
        return name switch
        {
            "trackside" => Camera.LookAt(engine.ToWorld(new Double3(9, 1.7, -engineHalf - 25)), target.ToWorld(new Double3(0, 2.5, 0)), 60),
            "roof" => Camera.LookAt(target.ToWorld(new Double3(0.2, roof + 1.65, 3)), target.ToWorld(new Double3(0, roof + 1.2, -40)), 75),
            // (Not one of Names, which the perf budgets walk.) Close on the roof crew (Staging.Crew), and what's behind them.
            "crew" => Camera.LookAt(target.ToWorld(new Double3(0.6, roof + 1.75, -1.6)), target.ToWorld(new Double3(-0.6, roof + 1.45, -6.2)), 50),
            // Inside the car, at its front end, looking back down the aisle past the cargo.
            "inside" => Camera.LookAt(target.ToWorld(new Double3(-0.5, Floor(train) + 1.65, -target.Shape.HalfLength + 0.6)), target.ToWorld(new Double3(0, Floor(train) + 1.3, 2)), 70),
            // From inside the car behind, through both open end doors at this car's rear doorway (note 110: who comes through).
            "door" => DoorCamera(train, car),
            // On the ballast beside the gap behind this car, looking in under the plate (what checks a gap: the Whistler's).
            "gapside" => GapSideCamera(train, car),
            // Off the second car's left, over the shoulder of crewmate 4 (Staging.Lone) at the Ribbit pack beyond them.
            "pack" => Camera.LookAt(train.Frames[Math.Min(2, train.Frames.Count - 1)].ToWorld(new Double3(-(train.Frames[Math.Min(2, train.Frames.Count - 1)].Shape.HalfWidth + 0.4), 2.1, 1.2)),
                train.Frames[Math.Min(2, train.Frames.Count - 1)].ToWorld(new Double3(-(train.Frames[Math.Min(2, train.Frames.Count - 1)].Shape.HalfWidth + 5.2), 0.4, -1.6)), 55),
            // Off the second car's left, over crewmate 4's shoulder, up at what's stood in front of them (the staged Gaunt).
            "gaunt" => Camera.LookAt(train.Frames[Math.Min(2, train.Frames.Count - 1)].ToWorld(new Double3(-(train.Frames[Math.Min(2, train.Frames.Count - 1)].Shape.HalfWidth + 1.2), 1.75, 0.2)),
                train.Frames[Math.Min(2, train.Frames.Count - 1)].ToWorld(new Double3(-(train.Frames[Math.Min(2, train.Frames.Count - 1)].Shape.HalfWidth + 3.4), 1.9, -1.5)), 55),
            // The same, from in front of it, close, up at its face.
            "gauntface" => Camera.LookAt(train.Frames[Math.Min(2, train.Frames.Count - 1)].ToWorld(new Double3(-(train.Frames[Math.Min(2, train.Frames.Count - 1)].Shape.HalfWidth + 2.0), 1.6, -3.0)),
                train.Frames[Math.Min(2, train.Frames.Count - 1)].ToWorld(new Double3(-(train.Frames[Math.Min(2, train.Frames.Count - 1)].Shape.HalfWidth + 2.9), 2.45, -1.6)), 50),
            // Off the second car's left, over crewmate 4's shoulder, down at what's low on the ground in front of them (the
            // staged Grumbler).
            "grumbler" => Camera.LookAt(train.Frames[Math.Min(2, train.Frames.Count - 1)].ToWorld(new Double3(-(train.Frames[Math.Min(2, train.Frames.Count - 1)].Shape.HalfWidth + 1.3), 1.35, 0.1)),
                train.Frames[Math.Min(2, train.Frames.Count - 1)].ToWorld(new Double3(-(train.Frames[Math.Min(2, train.Frames.Count - 1)].Shape.HalfWidth + 3.4), 0.4, -1.6)), 55),
            // Off the second car's left, over crewmate 4's shoulder, at what's squatted in the ash out there (the staged Soot
            // Child), or on them.
            "soot" => Camera.LookAt(train.Frames[Math.Min(2, train.Frames.Count - 1)].ToWorld(new Double3(-(train.Frames[Math.Min(2, train.Frames.Count - 1)].Shape.HalfWidth + 1.0), 1.55, 0.3)),
                train.Frames[Math.Min(2, train.Frames.Count - 1)].ToWorld(new Double3(-(train.Frames[Math.Min(2, train.Frames.Count - 1)].Shape.HalfWidth + 3.6), 0.6, -1.5)), 50),
            // From the side, at crewmate 4 and what's on them (the staged Soot Child drinking).
            "sootside" => Camera.LookAt(train.Frames[Math.Min(2, train.Frames.Count - 1)].ToWorld(new Double3(-(train.Frames[Math.Min(2, train.Frames.Count - 1)].Shape.HalfWidth + 3.4), 1.45, 0.4)),
                train.Frames[Math.Min(2, train.Frames.Count - 1)].ToWorld(new Double3(-(train.Frames[Math.Min(2, train.Frames.Count - 1)].Shape.HalfWidth + 2.45), 1.2, -1.5)), 45),
            // Close behind crewmate 4 (Staging.Lone), at their back: what their friends see there and they can't (the
            // staged Follower).
            "follower" => Camera.LookAt(train.Frames[Math.Min(2, train.Frames.Count - 1)].ToWorld(new Double3(-(train.Frames[Math.Min(2, train.Frames.Count - 1)].Shape.HalfWidth + 1.1), 1.6, -1.25)),
                train.Frames[Math.Min(2, train.Frames.Count - 1)].ToWorld(new Double3(-(train.Frames[Math.Min(2, train.Frames.Count - 1)].Shape.HalfWidth + 2.2), 1.25, -1.5)), 50),
            // Off the last car's side, looking up at what's over its roof (the staged Choir besieging the guard van).
            "choir" => Camera.LookAt(train.Frames[^1].ToWorld(new Double3(4.6, train.Frames[^1].Shape.RoofHeight + 0.6, 4.5)),
                train.Frames[^1].ToWorld(new Double3(0, train.Frames[^1].Shape.RoofHeight + 2.0, 0)), 55),
            // In car 2's aisle, close in front of what's stood in it (the staged Passenger, Staging.Passenger), a little off
            // to one side to see what it's dragging behind it.
            "passenger" => Camera.LookAt(target.ToWorld(new Double3(0.2, Floor(train) + 1.6, -target.Shape.HalfLength + 3.3)),
                target.ToWorld(new Double3(-0.35, Floor(train) + 1.25, -target.Shape.HalfLength + 5.9)), 55),
            // In the middle car, under its forward lantern, up at what's round it (the staged Fire Flies).
            "flies" => FliesCamera(train),
            "crewside" => Camera.LookAt(target.ToWorld(new Double3(2.6, roof + 1.9, -4.2)), target.ToWorld(new Double3(-0.8, roof + 1.45, -5.6)), 45),
            "cab" => CabCamera(engine),
            // Crouched where the fireman shovels, at the firebox door (what's seen when it's open: the staged Stoker).
            "firebox" => FireboxCamera(engine),
            // The fireman's side (T101): out of the left window down the running board to the blow-off by the smokebox.
            "fireman" => CabCamera(engine, -1),
            // T109: across the cab from the driver's place to the vent on its left side, and from the fireman's to the
            // engineering kit's rack on the right.
            "vent" => SideCamera(engine, InteractableKind.Vent, 1),
            "rack" => SideCamera(engine, InteractableKind.ToolRack, -1),
            "chase" => ChaseCamera(train),
            // On the line 47 m ahead of the engine, at the staged Switchman by its lever 8 m on (Staging.Threats).
            "switchman" => Camera.LookAt(engine.ToWorld(new Double3(1.6, 1.8, -engineHalf - 50.5)), engine.ToWorld(new Double3(3.8, 1.1, -engineHalf - 55)), 50),
            "ahead" => Camera.LookAt(engine.ToWorld(new Double3(1.5, 2.2, -engineHalf - 70)), engine.ToWorld(new Double3(0, 2.2, 0)), 55),
            "gap" => GapCamera(train, car),
            // From over the car behind, down at a cargo car's roof hatch (T99): its lid, shut, or open down the side.
            "hatch" => Camera.LookAt(target.ToWorld(new Double3(4.2, roof + 2.2, 9.5)), target.ToWorld(new Double3(0.6, roof - 1.4, 3.2)), 70),
            // Over the last car's roof, looking back at its gun on its rail (T93).
            "gun" => GunCamera(train),
            // (Not one of Names.) Sat in the cannon's seat (note 137), the gunner's eye over the breech, along the barrel;
            // and off its side, close, the whole of it.
            "cannon" => CannonCamera(train, side: false),
            "cannonside" => CannonCamera(train, side: true),
            // (Not one of Names.) Down the aisle of the first cargo car at the face of its load, where the staged fire
            // burns (dt screenshot --threats --view fire: Staging.Threats' car fire, Effects.CarFire).
            "fire" => FireCamera(train),
            // (Not one of Names.) In the guard van at its back end, down at the stores World.Stock puts there: the crates,
            // the lamp and radios, the toys (App. C.4), and its extinguisher on the wall beyond them.
            "stores" => StoresCamera(train),
            // (Not one of Names.) In the gun car (the guard van) at its front end: the powder and shot locker in the corner
            // ahead of the tool lockers on the left (SceneArt.Fittings).
            "locker" => LockerCamera(train),
            // (Not one of Names.) In this car, across the aisle at its extinguisher stood on its board (World.ExtinguisherMount;
            // with dt screenshot --stocked, its charge in the glass).
            "mount" => Camera.LookAt(target.ToWorld(new Double3(-0.05, Floor(train) + 1.3, -target.Shape.HalfLength + 2.95)),
                target.ToWorld(new Double3(-1.15, Floor(train) + 0.45, -target.Shape.HalfLength + 2.05)), 55),
            // (Not one of Names.) Off the engine's side, up at the tender under a coaling tower's spout (dt screenshot
            // --route tier:seed --coaling --view coaling: the chute pouring, Effects.CoalPour).
            "coaling" => Camera.LookAt(engine.ToWorld(new Double3(9, 3.5, engineHalf - 1)), engine.ToWorld(new Double3(0, 5.5, engineHalf - 3)), 60),
            // On the plate behind the tender, looking up its gangway into the cab and at the ladder to the cab roof (T90).
            "gangway" => Camera.LookAt(engine.ToWorld(new Double3(-0.2, 2.9, engineHalf + 1.4)), engine.ToWorld(new Double3(-0.9, 0.6, engineHalf - 8)), 75),
            _ => throw new ArgumentException($"unknown view '{name}' (known: {string.Join(", ", Names)})"),
        };
    }

    /// <summary>Standing in the cab beside the boiler, looking past it down the line.</summary>
    static Camera FliesCamera(TrainOnLine train)
    {
        var car = train.Frames[Math.Max(0, (train.Frames.Count - 1) / 2)];
        if (car.Shape.Interior is not { } room)
            return Camera.LookAt(car.ToWorld(new Double3(0, 2, 0)), car.ToWorld(new Double3(0, 2, -5)), 60);
        var lamp = Art.SceneArt.LampPositions(room).First();
        var at = new Double3(lamp.X, lamp.Y, lamp.Z);
        return Camera.LookAt(car.ToWorld(at + new Double3(0.8, -0.55, 0.75)), car.ToWorld(at + new Double3(0, -0.04, 0)), 45);
    }

    static Camera FireboxCamera(in CarFrame engine)
    {
        var door = Art.TrainKit.FireDoor(engine.Shape);
        var at = new Double3(door.X, door.Y, door.Z);
        return Camera.LookAt(engine.ToWorld(at + new Double3(0.12, 0.22, 0.8)), engine.ToWorld(at), 60);
    }

    /// <summary>From one side of the cab at a standing eye, across at an interactable on the other side.</summary>
    static Camera SideCamera(in CarFrame engine, InteractableKind kind, int from)
    {
        var at = engine.Shape.Interactables.First(i => i.Kind == kind).Position;
        var eye = CabEye(engine.Shape, from) with { Z = at.Z + 0.6 };
        return Camera.LookAt(engine.ToWorld(eye), engine.ToWorld(at + new Double3(0, 1.1, 0)), 70);
    }

    static Camera CabCamera(in CarFrame engine, int side = 1)
    {
        var eye = CabEye(engine.Shape, side);
        return Camera.LookAt(engine.ToWorld(eye), engine.ToWorld(eye + new Double3(-side * 3, -2.5, -60)), 75);
    }

    /// <summary>
    /// A standing eye at the driver's place (T101), in the engine's frame: by the brake and the reverser on the right
    /// (<paramref name="side"/> 1), in line with the window beside the boiler; −1 is the fireman's, across the cab.
    /// </summary>
    public static Double3 CabEye(CarShape engine, int side = 1)
    {
        var cab = engine.Cab!.Value;
        var levers = engine.Levers!.Value;
        // As far out as a player stands: against the cab side (its wall 0.1 thick, a player 0.3 round).
        return new Double3(side * (engine.HalfWidth - 0.4), cab.Min.Y + 1.75, levers.Reverser.Z + 0.55);
    }

    /// <summary>
    /// Standing in the coupling gap behind <paramref name="car"/>, on the car behind's side of it, looking at the end door the
    /// plate leads to (GDD §32 "readable gap and coupling danger"): the plate underfoot, the doorway, the ballast either side.
    /// </summary>
    static Camera GapCamera(TrainOnLine train, int car)
    {
        var ahead = train.Frames[Math.Clamp(car, 0, train.Frames.Count - 2)];
        double l = ahead.Shape.HalfLength;
        return Camera.LookAt(ahead.ToWorld(new Double3(0.35, 2.75, l + 1.3)), ahead.ToWorld(new Double3(-0.4, 1.4, l)), 80);
    }

    static Camera GapSideCamera(TrainOnLine train, int car)
    {
        var at = train.Frames[Math.Clamp(car, 0, train.Frames.Count - 2)];
        double z = at.Shape.HalfLength + train.Dynamics.Tuning.Geometry.CouplingGap / 2, w = at.Shape.HalfWidth;
        return Camera.LookAt(at.ToWorld(new Double3(w + 2.6, 1.7, z + 0.6)), at.ToWorld(new Double3(0, 1.0, z)), 55);
    }

    static double Floor(TrainOnLine train) => train.Dynamics.Tuning.Geometry.Interior?.FloorHeight ?? 1.1;

    static Camera DoorCamera(TrainOnLine train, int car)
    {
        var at = train.Frames[Math.Clamp(car, 0, train.Frames.Count - 2)];
        double l = at.Shape.HalfLength, floor = Floor(train), x = train.Dynamics.Tuning.Geometry.PlateX;
        double gap = train.Dynamics.Tuning.Geometry.CouplingGap;
        return Camera.LookAt(at.ToWorld(new Double3(x + 0.1, floor + 1.6, l + gap + 1.6)), at.ToWorld(new Double3(x, floor + 1.15, l - 0.4)), 60);
    }

    static Camera GunCamera(TrainOnLine train)
    {
        var last = train.Frames[^1];
        double roof = last.Shape.RoofHeight;
        return Camera.LookAt(last.ToWorld(new Double3(1.6, roof + 2.2, -2)), last.ToWorld(new Double3(0, roof + 0.4, last.Shape.HalfLength - 2.5)), 60);
    }

    static Camera FireCamera(TrainOnLine train)
    {
        int v = Enumerable.Range(0, train.Vehicles.Count).FirstOrDefault(i => train.Vehicles[i].Kind == Sim.Train.VehicleKind.Cargo, 1);
        var f = train.Frames[v];
        double floor = train.Dynamics.Tuning.Geometry.Interior?.FloorHeight ?? 1.1;
        return Camera.LookAt(f.ToWorld(new Double3(-0.55, floor + 1.6, 5.2)), f.ToWorld(new Double3(0.5, floor + 0.9, 1.5)), 70);
    }

    static Camera StoresCamera(TrainOnLine train)
    {
        int v = Enumerable.Range(0, train.Vehicles.Count).LastOrDefault(i => train.Vehicles[i].Kind == Sim.Train.VehicleKind.Guard, train.Vehicles.Count - 1);
        var f = train.Frames[v];
        double floor = Floor(train), l = f.Shape.HalfLength;
        return Camera.LookAt(f.ToWorld(new Double3(-0.2, floor + 1.55, l - 0.5)), f.ToWorld(new Double3(0.2, floor + 0.2, l - 2.6)), 75);
    }

    static Camera LockerCamera(TrainOnLine train)
    {
        int v = Enumerable.Range(1, train.Vehicles.Count - 1).FirstOrDefault(i => train.Vehicles[i].HasGun && train.Frames[i].Shape.Interior is not null, train.Vehicles.Count - 1);
        var f = train.Frames[v];
        double floor = Floor(train), l = f.Shape.HalfLength;
        return Camera.LookAt(f.ToWorld(new Double3(0.1, floor + 1.5, -l + 1.5)), f.ToWorld(new Double3(-0.95, floor + 0.2, -l + 0.6)), 65);
    }

    static Camera CannonCamera(TrainOnLine train, bool side)
    {
        int v = Enumerable.Range(0, train.Vehicles.Count).LastOrDefault(i => train.Vehicles[i].HasGun, -1);
        if (v < 0 || Sim.Combat.Guns.Mount(train, v) is not { } mount)
            return GunCamera(train);
        var f = train.Frames[v];
        var p = mount.Position;
        double dir = mount.Facing.Z;             // the barrel's way along the car (−1: towards the engine)
        var seat = Art.TrainKit.CannonSeat;
        // The seat is behind the breech: back along the car from the pivot, its height under it.
        var eye = new Double3(p.X, p.Y + seat.Y + 0.78, p.Z - dir * seat.Z);
        return side
            ? Camera.LookAt(f.ToWorld(new Double3(p.X + 2.2, p.Y + 0.6, p.Z + dir * 0.4)), f.ToWorld(new Double3(p.X, p.Y - 0.25, p.Z + dir * 0.1)), 50)
            : Camera.LookAt(f.ToWorld(eye), f.ToWorld(new Double3(p.X, p.Y + 0.1, p.Z + dir * 12)), 65);
    }

    static Camera ChaseCamera(TrainOnLine train)
    {
        var last = train.Frames[^1];
        var mid = train.Frames[train.Frames.Count / 2];
        return Camera.LookAt(last.ToWorld(new Double3(-12, 14, last.Shape.HalfLength + 30)), mid.ToWorld(new Double3(0, 2, 0)), 60);
    }

    public static FrameLighting Lighting(TrainOnLine train, Look? look = null, float dawn = 0) => Lighting(train.Frames[0], look, dawn);

    /// <param name="look">The art pass's atmosphere (look.json) over the night's defaults, when there is one.</param>
    /// <param name="dawn">How far the dawn's come up (0..1, <see cref="Look.DawnOf"/>).</param>
    public static FrameLighting Lighting(in CarFrame engine, Look? look = null, float dawn = 0)
    {
        var light = look?.Apply(FrameLighting.Night) ?? FrameLighting.Night;
        if (look is not null)
            light = look.Dawn(light, dawn);
        light.LampPosition = Sim.World.LampPosition(engine);
        var fwd = engine.Back * -1;
        light.LampDirection = Vector3.Normalize(new Vector3((float)fwd.X, (float)fwd.Y - 0.04f, (float)fwd.Z));
        return light;
    }
}
