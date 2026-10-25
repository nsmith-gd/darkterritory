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
            new("engine-dress", LargeProp, () => TrainKit.EngineDress(look, engine)),
            new("gun", GunMount, () => TrainKit.Gun(look)),
            new("gun-shield", SmallProp, () => TrainKit.GunShield(look)),
        };
        foreach (var livery in Enum.GetValues<TrainKit.Livery>())
            list.Add(new($"car-{livery.ToString().ToLowerInvariant()}", Car, () => TrainKit.Car(look, cargo, livery, 0)));
        list.Add(new("guard", Car, () => TrainKit.Car(look, guard, TrainKit.Livery.Armoured, 0)));
        // The consist's other cars (note 184): a guard car with cars behind it (the second guard car's), the crew car's body
        // and its fit-out, and an armoured car's plate and the roof handrails, each over a car's body.
        var midGuard = CarShape.Build(g, VehicleKind.Guard, hasCarBehind: true);
        var crew = CarShape.Build(g, VehicleKind.Utility, hasCarBehind: true, train.Kit.Lockers);
        list.Add(new("guard-mid", Car, () => TrainKit.Car(look, midGuard, TrainKit.Livery.Armoured, 0)));
        list.Add(new("car-crew", Car, () => TrainKit.Car(look, crew, TrainKit.Livery.Steel, 0, load: false)));
        list.Add(new("crew-fit", MediumProp, () => TrainKit.UtilityFit(look, crew)));
        list.Add(new("armour-plate", MediumProp, () => TrainKit.ArmourPlate(look, cargo)));
        list.Add(new("guard-armoured", Car, () =>
        {
            // The armoured guard van as the scene draws it: the body, its plate and the handrails over it.
            var k = new Kit(look);
            k.Append(TrainKit.Car(look, guard, TrainKit.Livery.Armoured, 0), Matrix4x4.Identity);
            k.Append(TrainKit.ArmourPlate(look, guard), Matrix4x4.Identity);
            k.Append(TrainKit.RoofHandrails(look, guard), Matrix4x4.Identity);
            return k.Build("guard-armoured");
        }));
        list.Add(new("roof-handrails", SmallProp, () => TrainKit.RoofHandrails(look, cargo)));
        // The flare pistol (notes 871, 964): lying in the guard van, as a fist holds it, and a flare fired from it.
        list.Add(new("flare-pistol", SmallProp, () => PropKit.FlarePistol(look)));
        list.Add(new("flare-pistol-held", SmallProp, () => PropKit.FlarePistolHeld(look)));
        list.Add(new("flare", SmallProp, () => PropKit.Flare(look)));
        // The voice's fittings (note 720): the speaking tube's horn and its pipe down to the floor, and the whistle codes' card.
        list.Add(new("speaking-tube", SmallProp, () => VoiceKit.TubeEnd(look, (float)SpeakingTube.MouthHeight + 0.1f)));
        list.Add(new("whistle-card", MediumProp, () => VoiceKit.WhistleCard(look, "WHISTLE CODES",
            ["ONE LONG - STOPPING", "TWO LONG - GOING", "THREE SHORT - BACKING", "FOUR SHORT - HELP", "TWO SHORT - HEARD YOU"])));
        // A car's damage rides on its body (the car class's budget, what the body leaves of it: the kit's are 9-11k).
        // The boiler's tear and the plate bolted over it once mended (note 991).
        list.Add(new("rupture-tear", SmallProp, () => TrainKit.RuptureTear(look)));
        list.Add(new("rupture-tear-open", SmallProp, () => TrainKit.RuptureTear(look, open: true)));
        list.Add(new("rupture-patch", SmallProp, () => TrainKit.RupturePatch(look)));
        // A house's cellar and loose boards, shut and searched (note 991).
        list.Add(new("cellar-hatch", SmallProp, () => TownKit.CellarHatch(look)));
        list.Add(new("cellar-open", SmallProp, () => TownKit.CellarOpen(look)));
        // Note 1005: a basement's chest, shut and searched.
        list.Add(new("cellar-chest", SmallProp, () => TownKit.CellarChest(look, open: false)));
        list.Add(new("cellar-chest-open", SmallProp, () => TownKit.CellarChest(look, open: true)));
        list.Add(new("loose-boards", SmallProp, () => TownKit.LooseBoards(look)));
        list.Add(new("underfloor-open", SmallProp, () => TownKit.UnderFloorOpen(look)));
        list.Add(new("hatch-lid", SmallProp, () => TrainKit.HatchLid(look, new System.Numerics.Vector3(1.4f, 0.15f, 2.4f))));
        list.Add(new("utility-fit", MediumProp, () => TrainKit.UtilityFit(look, cargo)));
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
        // The crew lockers in the kit's car (note 173): the row's cabinets, and its longest-named door.
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
        // The trees' forms (note 395): the near spruce modelled, the far field's cards; and the forest floor.
        foreach (var form in new[] { NovaKit.TreeForm.Flagged, NovaKit.TreeForm.Broken, NovaKit.TreeForm.Forked })
        {
            var f = form;
            list.Add(new($"spruce3d-{f.ToString().ToLowerInvariant()}", LargeProp, () => WorldKit.Spruce(look, 1, 12, f)));
            list.Add(new($"spruce-{f.ToString().ToLowerInvariant()}", SmallProp, () => NovaKit.Conifer(look, 1, 12, 0.3f, f)));
            list.Add(new($"fir-{f.ToString().ToLowerInvariant()}", SmallProp, () => NovaKit.Conifer(look, 1, 12, 0.46f, f)));
        }
        foreach (var kind in Enum.GetValues<NovaKit.FloorKind>())
            for (int v = 0; v < 3; v++)
            {
                var (fk, fv) = (kind, v);
                list.Add(new($"floor-{fk.ToString().ToLowerInvariant()}-{fv}", SmallProp, () => NovaKit.Floor(look, fk, fv)));
            }
        list.Add(new("dead-tree", SmallProp, () => WorldKit.DeadTree(look, 0, 10)));
        // Red maple in its fall red, and a dory left on the mud by the tide (note 1031).
        for (int v = 0; v < 3; v++)
        {
            int variant = v;
            list.Add(new($"maple-{v}", SmallProp, () => NovaKit.RedMaple(look, variant)));
            list.Add(new($"dory-aground-{v}", SmallProp, () => MaritimeKit.Aground(look, variant)));
        }
        list.Add(new("tuft", SmallProp, () => WorldKit.Tuft(look, 0, weed: false)));
        list.Add(new("brass-weed", SmallProp, () => WorldKit.Tuft(look, 0, weed: true)));
        list.Add(new("rock", SmallProp, () => WorldKit.Rock(look, 0, 1)));
        // What stands between a town's houses (note 989): the fish shed at its biggest, hung, its door ajar; the tallest stack
        // of traps with its coil; the granite; the longest fence of each kind.
        list.Add(new("between-shed", SmallProp, () => BetweenKit.Shed(look, 2.0f, 1.5f, 3.3f, 2, true, true)));
        list.Add(new("between-traps", SmallProp, () => BetweenKit.Traps(look, 11)));
        list.Add(new("between-granite", SmallProp, () => BetweenKit.Granite(look, 0)));
        foreach (int v in new[] { 0, 1, 2 })
            list.Add(new($"between-fence-{v}", SmallProp, () => BetweenKit.Fence(look, v, 12, 1.15f)));
        list.Add(new("debris-tree", LargeProp, () => DebrisKit.Of(look, 0)));
        list.Add(new("debris-rocks", LargeProp, () => DebrisKit.Of(look, 1)));
        list.Add(new("debris-tangle", LargeProp, () => DebrisKit.Of(look, 2)));
        list.Add(new("pole", MediumProp, () => WorldKit.Pole(look, 0)));
        list.Add(new("signal", MediumProp, () => WorldKit.Signal(look, lit: true)));
        list.Add(new("fence-post", SmallProp, () => WorldKit.FencePost(look, 0)));
        list.Add(new("viaduct-bay", StructureBay, () => StructureKit.ViaductBay(look, 18, lastPier: false)));
        list.Add(new("trestle-bent", StructureBay, () => StructureKit.TrestleBent(look, 18)));
        list.Add(new("girder-bay", StructureBay, () => StructureKit.GirderBay(look, 18, lastPier: false)));
        list.Add(new("truss-span", StructureBay, () => StructureKit.TrussSpan(look, 18, lastPier: false)));
        list.Add(new("tunnel-lining", StructureBay, () => StructureKit.TunnelLining(look, 10)));
        list.Add(new("portal", LargeProp, () => StructureKit.Portal(look)));
        list.Add(new("wall", StructureBay, () => StructureKit.Wall(look, 1)));
        list.Add(new("tower", LargeProp, () => StructureKit.Tower(look, 1)));
        // A harbour town's Hope Tower (note 819): one a town, seen from a kilometre off, so a large prop's budget.
        list.Add(new("hope-tower", LargeProp, () => HopeKit.Tower(look)));
        // The starting town's pieces the director looked at on 19 Oct (note 870): the bitten van, the night market's stalls
        // (every form and colouring), the works' heaps.
        list.Add(new("square-carriage", MediumProp, () => SquareKit.Fixture(look, "carriage")));
        for (int v = 0; v < SquareKit.StallVariants; v++)
        {
            int variant = v;
            list.Add(new($"square-marketstall-{v}", MediumProp, () => SquareKit.Fixture(look, "marketstall", variant)));
        }
        list.Add(new("works-slag", LargeProp, () => WorksKit.Piece(look, "slag")));
        list.Add(new("works-coal", LargeProp, () => WorksKit.Piece(look, "coal")));
        // A fortress town begun and stamped flat by a Waker (note 824), out beside the line: a large prop's budget each.
        for (int v = 0; v < RuinKit.Variants; v++)
        {
            int variant = v;
            list.Add(new($"fortruin-{v}", LargeProp, () => RuinKit.FortRuin(look, variant)));
        }
        list.Add(new("gatehouse", LargeProp, () => StructureKit.Gatehouse(look)));
        // Its gates (note 847): the plain one and the one with the wicket in it, each side.
        foreach (int side in new[] { -1, 1 })
        {
            int s = side;
            list.Add(new($"gateleaf-{s}", SmallProp, () => StructureKit.GateLeaf(look, s, false)));
            list.Add(new($"gateleaf-wicket-{s}", SmallProp, () => StructureKit.GateLeaf(look, s, true)));
        }
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
        // The village finds, each its own (FindKit; the director, 8 Oct).
        foreach (var item in new[] { "tinnedFood", "treats", "candles", "medicine", "bandages", "morphine", "preserves", "lampOil",
                     "tools", "valuableTools", "pocketWatch", "lampParts", "rope", "gannetHead",
                     // Note 718: the comet fragment and the team kills' trophies.
                     "cometFragment", "grumblerJaw", "gauntHand", "climberClaw", "passengerCap", "brakemanWheel", "knotterClaw",
                     "hotboxPlate", "beetleShovel", "towerJawTooth" })
            list.Add(new($"find-{item}", SmallProp, () => FindKit.Find(look, item, 0.15f)));
        // Note 719: the fragile valuables, whole, knocked and smashed; and the two-person jackpots.
        foreach (var item in new[] { "glassware", "decanter", "mantelClock" })
            for (int knocks = 0; knocks <= 2; knocks++)
            {
                int k = knocks;
                list.Add(new($"find-{item}-{k}", SmallProp, () => FindKit.Find(look, item, 0.15f, k)));
            }
        foreach (var (item, radius) in new[] { ("safe", 0.42f), ("churchBell", 0.45f), ("figurehead", 0.42f) })
            list.Add(new($"jackpot-{item}", SmallProp, () => FindKit.Jackpot(look, item, radius)));
        // What the railway left beside its line (note 325, WorldArt.Leavings): every variant the leavings deal.
        for (int v = 0; v < 3; v++)
        {
            int variant = v;
            list.Add(new($"junk-ties-{v}", SmallProp, () => JunkKit.Ties(look, variant)));
            list.Add(new($"junk-drums-{v}", SmallProp, () => JunkKit.Drums(look, variant)));
        }
        for (int v = 0; v < 2; v++)
        {
            int variant = v;
            list.Add(new($"junk-rails-{v}", SmallProp, () => JunkKit.Rails(look, variant)));
            list.Add(new($"junk-milepost-{v}", SmallProp, () => JunkKit.Milepost(look, variant)));
            list.Add(new($"junk-hut-{v}", MediumProp, () => JunkKit.Hut(look, variant)));
            list.Add(new($"junk-coal-{v}", SmallProp, () => JunkKit.Coal(look, variant)));
            list.Add(new($"junk-camp-{v}", SmallProp, () => JunkKit.Camp(look, variant)));
            list.Add(new($"junk-grave-{v}", SmallProp, () => JunkKit.Grave(look, variant)));
        }
        list.Add(new("junk-reel", SmallProp, () => JunkKit.Reel(look)));
        list.Add(new("junk-wheelset", SmallProp, () => JunkKit.Wheelset(look)));
        list.Add(new("junk-trolley", SmallProp, () => JunkKit.Trolley(look)));
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
        // The coaling tower Tower Jaw's brought down across the line, and its heap cleared off it (note 363).
        list.Add(new("coaling-fallen", Facility, () => StructureKit.CoalingTowerFallen(look, cleared: false)));
        list.Add(new("coaling-fallen-cleared", Facility, () => StructureKit.CoalingTowerFallen(look, cleared: true)));
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
