using System.Numerics;
using Ballast.Render;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Art;

/// <summary>An asset class from the pipeline plan's budget table: its LOD0 ceiling in triangles.</summary>
public sealed record AssetClass(string Name, int MaxTriangles);

/// <summary>A cooked piece by name, the class it's budgeted as, and how to make it.</summary>
public sealed record CatalogEntry(string Name, AssetClass Class, Func<MeshAsset> Make);

/// <summary>
/// Every piece the kits make, by name, with its budget class (pipeline plan, "Technical budgets": an asset over budget
/// fails validation, unless readability demands it and it says so). `dt art check` holds them to it; `dt art show`
/// turns any one of them on a turntable under a lantern.
/// </summary>
public static class ArtCatalog
{
    public static readonly AssetClass EngineCar = new("engine car + cab interior", 45_000);
    public static readonly AssetClass Car = new("tender / cargo / crew / guard car", 15_000);
    public static readonly AssetClass GunMount = new("gun car mount", 4_000);
    public static readonly AssetClass LargeProp = new("large prop / machine", 8_000);
    public static readonly AssetClass MediumProp = new("medium prop", 2_500);
    public static readonly AssetClass SmallProp = new("small prop", 800);
    /// <summary>A track and lineside cell (20 m) is 25k; a structure's bay is a fraction of a cell, so a bay gets 8k.</summary>
    public static readonly AssetClass StructureBay = new("structure bay (part of a 20 m track cell)", 8_000);
    public static readonly AssetClass Facility = new("facility (per spur)", 90_000);
    /// <summary>A whole ruined room seen from the line (tools/models: a sourced interior): one per village at most.</summary>
    public static readonly AssetClass Interior = new("ruined interior (a whole room)", 30_000);

    public static IReadOnlyList<CatalogEntry> Entries(Look? look, TrainTuning train)
    {
        var g = train.Geometry;
        var engine = CarShape.Build(g, VehicleKind.Engine, hasCarBehind: true);
        var cargo = CarShape.Build(g, VehicleKind.Cargo, hasCarBehind: true);
        var guard = CarShape.Build(g, VehicleKind.Guard, hasCarBehind: false);
        var list = new List<CatalogEntry>
        {
            new("engine", EngineCar, () => TrainKit.Engine(look, engine, 0)),
            new("gun", GunMount, () => TrainKit.Gun(look)),
        };
        foreach (var livery in Enum.GetValues<TrainKit.Livery>())
            list.Add(new($"car-{livery.ToString().ToLowerInvariant()}", Car, () => TrainKit.Car(look, cargo, livery, 0)));
        list.Add(new("guard", Car, () => TrainKit.Car(look, guard, TrainKit.Livery.Armoured, 0)));
        // A car's damage rides on its body (the car class's budget, what the body leaves of it: the kit's are 9-11k).
        list.Add(new("hatch-lid", SmallProp, () => TrainKit.HatchLid(look, new System.Numerics.Vector3(1.4f, 0.15f, 2.4f))));
        list.Add(new("damage-1", MediumProp, () => DamageKit.Car(look, cargo, 1, 3)));
        list.Add(new("damage-2", MediumProp, () => DamageKit.Car(look, cargo, 2, 3)));
        list.Add(new("car-wrecked", Car, () =>
        {
            // The two as the scene draws them, one over the other: what `dt art show` should turn.
            var k = new Kit(look);
            k.Append(TrainKit.Car(look, cargo, TrainKit.Livery.Steel, 0), Matrix4x4.Identity);
            k.Append(DamageKit.Car(look, cargo, 2, 3), Matrix4x4.Identity);
            return k.Build("car-wrecked");
        }));
        // The crew lockers in the kit's car (note 170): the row's cabinets, and its longest-named door.
        if (train.Kit.Lockers is { Names.Count: > 0 } lockerTuning)
        {
            var kitCar = CarShape.Build(g, VehicleKind.Cargo, hasCarBehind: true, lockerTuning);
            var bays = kitCar.Lockers;
            float lw = (float)lockerTuning.Width, lh = (float)lockerTuning.Height;
            list.Add(new("lockers", MediumProp, () => LockerKit.Row(look, bays, kitCar.LockerShelves)));
            var longest = bays.OrderByDescending(b => b.Name.Length).First();
            list.Add(new("locker-door", SmallProp, () => LockerKit.Door(look, longest.Name, lw, lh, LockerKit.LetterPixel(bays, lw))));
        }
        // The standard doorway (train.json doorway, note 110).
        var doorway = train.Geometry.Doorway;
        float doorH = (float)doorway.Height, sideW = (float)(train.Geometry.Interior?.SideDoorWidth ?? 1.8);
        list.Add(new("door-end", SmallProp, () => TrainKit.Door(look, new Vector3((float)doorway.Width, doorH, 0.1f), side: false)));
        list.Add(new("door-side", SmallProp, () => TrainKit.Door(look, new Vector3(0.1f, doorH, sideW), side: true)));
        for (int v = 0; v < 4; v++)
        {
            int variant = v;
            list.Add(new($"pine-{v}", LargeProp, () => WorldKit.Pine(look, variant, 12)));
            list.Add(new($"pinecard-{v}", SmallProp, () => WorldKit.PineCard(look, variant, 12)));
        }
        list.Add(new("dead-tree", SmallProp, () => WorldKit.DeadTree(look, 0, 10)));
        list.Add(new("tuft", SmallProp, () => WorldKit.Tuft(look, 0, weed: false)));
        list.Add(new("brass-weed", SmallProp, () => WorldKit.Tuft(look, 0, weed: true)));
        list.Add(new("rock", SmallProp, () => WorldKit.Rock(look, 0, 1)));
        list.Add(new("pole", MediumProp, () => WorldKit.Pole(look, 0)));
        list.Add(new("signal", MediumProp, () => WorldKit.Signal(look, lit: true)));
        list.Add(new("fence-post", SmallProp, () => WorldKit.FencePost(look, 0)));
        list.Add(new("viaduct-bay", StructureBay, () => StructureKit.ViaductBay(look, 18, lastPier: false)));
        list.Add(new("trestle-bent", StructureBay, () => StructureKit.TrestleBent(look, 18)));
        list.Add(new("tunnel-lining", StructureBay, () => StructureKit.TunnelLining(look, 10)));
        list.Add(new("portal", LargeProp, () => StructureKit.Portal(look)));
        list.Add(new("wall", StructureBay, () => StructureKit.Wall(look, 1)));
        list.Add(new("tower", LargeProp, () => StructureKit.Tower(look, 1)));
        list.Add(new("gatehouse", LargeProp, () => StructureKit.Gatehouse(look)));
        list.Add(new("platform", StructureBay, () => StructureKit.PlatformBay(look, 1)));
        list.Add(new("house", LargeProp, () => TownKit.House(look, 1)));
        list.Add(new("lived-house", LargeProp, () => TownKit.LivedHouse(look, 1)));
        list.Add(new("church", LargeProp, () => TownKit.Church(look)));
        list.Add(new("windmill", LargeProp, () => TownKit.Windmill(look)));
        list.Add(new("buffer-stop", MediumProp, () => StructureKit.BufferStop(look)));
        list.Add(new("switch-stand", MediumProp, () => StructureKit.SwitchStand(look)));
        // A stop's pieces (level-design P5, P7, P12): a yard shed, the hero's brick hall, a village find.
        list.Add(new("stop-shed", LargeProp, () => { var k = new Kit(look, 1400); StructureKit.Shed(k, 12, 30, 7, "wood_grey", 1); return k.Build("stop-shed"); }));
        list.Add(new("stop-hero", LargeProp, () => { var k = new Kit(look, 1401); StructureKit.Shed(k, 12, 22, 11, "brick_soot", 1); return k.Build("stop-hero"); }));
        list.Add(new("loot", SmallProp, () => PropKit.Loot(look, 0.15f)));
        // The sourced props (tools/models): each budgeted as what it stands in for.
        if (look is not null)
        {
            var props = PropArt.Of(look);
            foreach (var name in props.Names)
            {
                var n = name;
                // The hand lantern is also the crew's held lamp (the plan's first-person tool: 4-6k); a skull is seen
                // close, in a pile or a lantern, so it gets a medium prop's budget, not a pebble's.
                var cls = n switch
                {
                    "hand_lantern" or "skull" => MediumProp,
                    // What the crew carry (the physics bodies' models, tools/models make): picked up and seen close.
                    "stores_crate" or "heavy_crate" or "field_radio" => MediumProp,
                    // The train's stores (train_stores): the toys, the extinguisher and its board, the kit, the powder.
                    _ when n.StartsWith("toy_", StringComparison.Ordinal) => MediumProp,
                    "extinguisher" or "extinguisher_mount" or "repair_kit" or "shot_locker" or "powder_bag" => MediumProp,
                    _ when n.StartsWith("freight_", StringComparison.Ordinal) => MediumProp,
                    "boy_room" or "wake_room" or "portrait_room" => Interior,
                    _ => LargeProp,
                };
                list.Add(new($"prop-{n}", cls, () => props.Get(n)!));
            }
        }
        foreach (var kind in Enum.GetValues<FacilityKind>())
        {
            var k = kind;
            list.Add(new($"facility-{kind.ToString().ToLowerInvariant()}", Facility, () => StructureKit.Facility(look, k, 1)));
        }
        return list;
    }

    /// <summary>A piece's bounds, for framing it.</summary>
    public static (Vector3 Min, Vector3 Max) Bounds(MeshAsset piece)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var v in piece.Vertices)
        {
            min = Vector3.Min(min, v.Position);
            max = Vector3.Max(max, v.Position);
        }
        return piece.Vertices.Length == 0 ? (Vector3.Zero, Vector3.Zero) : (min, max);
    }
}
