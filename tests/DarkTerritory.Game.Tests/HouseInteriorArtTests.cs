using System.Numerics;
using System.Text.Json;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Stops;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The open houses' insides, as the director asked for them on 8 Oct (GDD App. F.3; ARCHITECTURE §8 note 326): a dim light
/// in each, and finds that stand out by their texture (each kind its own model on its own cell of the finds atlas).
/// </summary>
public class HouseInteriorArtTests
{
    static readonly string Content = DataFile.FindContentRoot();

    [Fact]
    public void EveryKindOfFindIsItsOwnThingOnItsOwnCell()
    {
        // The atlas's cells as the texture tool built them (index.json), in FindKit's order: the content contract.
        var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(Content, "art", "textures", "index.json"))).RootElement;
        var atlas = index.EnumerateArray().Single(e => e.GetProperty("name").GetString() == "finds_atlas");
        var cells = atlas.GetProperty("cells").EnumerateArray().Select(c => c.GetProperty("name").GetString()!).ToArray();
        Assert.Equal(FindKit.Cells, cells);
        // Every item loot.json can deal out has a model of its own and a cell, not the plain bundle.
        var loot = DataFile.Load<LootTuning>(Path.Combine(Content, LootTuning.File));
        var plain = FindKit.Find(null, "nothing", 0.15f).Vertices.Length;
        // (A creature's trophy, the Gannet's head, isn't a village find: a model of its own, but no cell. Nor is a comet
        // fragment, note 718.)
        // Note 719: nor are the fragile valuables (glass, walnut and brass: the kit's own stock, not the atlas's) nor the two-person
        // jackpots (drawn by FindKit.Jackpot, as big as they are).
        // Note 730: nor are the odd outcomes' gifts (Dave's painting, the coin back up the well): never dealt in a house, drawn
        // from the kit's own stock.
        var odd = DataFile.Load<RunTuning>(Path.Combine(Content, RunTuning.File)).Oddities;
        var trophies = loot.Trophies.Values.Select(t => t.Item).Append(loot.Comet!.Item).Concat(loot.Fragile!.Items.Keys)
            .Append(odd.Painting.Item).Append(odd.Well.CoinItem).ToHashSet();
        var jackpots = loot.Jackpots!.Items;
        foreach (var item in loot.Items.Keys)
        {
            if (!trophies.Contains(item) && !jackpots.ContainsKey(item))
                Assert.Contains(item, FindKit.Cells);
            var model = jackpots.TryGetValue(item, out var big) ? FindKit.Jackpot(null, item, (float)big.Radius) : FindKit.Find(null, item, 0.15f);
            Assert.True(model.Vertices.Length > 0 && model.Vertices.Length != plain, $"{item} is the plain bundle");
        }
        // A fragile find knocked looks it: cracked, then smashed, each a model of its own.
        foreach (var item in loot.Fragile.Items.Keys)
        {
            var whole = FindKit.Find(null, item, 0.15f, 0).Vertices.Length;
            Assert.NotEqual(whole, FindKit.Find(null, item, 0.15f, 1).Vertices.Length);
            Assert.NotEqual(FindKit.Find(null, item, 0.15f, 1).Vertices.Length, FindKit.Find(null, item, 0.15f, 2).Vertices.Length);
        }
    }

    /// <summary>
    /// What a find's kept in (note 760; the director, 14 Oct, `docs/playtest/2026-10-14.md` line 21: "Cupboards that definitely
    /// need a redesign"): a cupboard's a kitchen dresser and a cabinet a sideboard, not a plain box. They're made of their
    /// parts (doors, frames, knobs, hinges), their doors painted and their carcass stained, and they stand in the sim's box for
    /// them (StopWalls.Furniture), all but the worktop's and the cornice's lip and the knobs, a few centimetres out of their
    /// face.
    /// </summary>
    [Theory]
    [InlineData(ContainerKind.Cupboard, 1.9, 300)]
    [InlineData(ContainerKind.Cabinet, 1.0, 150)]
    public void ACupboardIsADresserAndACabinetASideboardInTheSimsBoxForThem(ContainerKind kind, double top, int least)
    {
        var look = Look.Load(Content);
        var route = Routes.Generate(Content, "frontier:7", 6);
        var (b, c) = route.Features.Where(f => f.Stop is not null).SelectMany(f => f.Stop!.Containers
            .Where(c => c.Kind == kind && c.Building >= 0 && f.Stop.Buildings[c.Building].Open && StopWalls.Walled(f.Stop, c.Building))
            .Select(c => (f.Stop.Buildings[c.Building], c))).First();
        Vertex[] House(IEnumerable<StopContainer> kept)
        {
            var k = new Kit(look, 0);
            TownKit.OpenHouse(k, b, kept);
            return k.Build("house").Vertices;
        }
        // What's drawn for it is what's drawn after the empty house.
        var empty = House([]);
        var piece = House([c])[empty.Length..];
        Assert.True(piece.Length / 3 >= least, $"a {kind} of {piece.Length / 3} triangles");
        // Painted, stained, iron: more than one finish.
        Assert.True(piece.Select(v => v.Color).Distinct().Count() >= 4);
        // In the sim's box (the kit's frame is the building's turned: TownKit.OpenHouse's K), but a few centimetres.
        var box = StopWalls.Furniture(b, [c]).Single();
        const double Floor = 0.17, Proud = 0.04 + 1e-3;
        foreach (var v in piece)
        {
            double x = -v.Position.Z, y = v.Position.X;
            Assert.InRange(Math.Abs(x - box.X), 0, box.HalfX + Proud);
            Assert.InRange(Math.Abs(y - box.Y), 0, box.HalfY + Proud);
            Assert.InRange(v.Position.Y, Floor - 1e-3, Floor + top + 1e-3);
        }
    }

    [Theory]
    [InlineData("frontier:7")]
    [InlineData("deadLines:7")]
    public void EveryOpenHouseHasItsDimLightInsideItClearOfTheFurniture(string spec)
    {
        var route = Routes.Generate(Content, spec, 6);
        int candles = 0, lamps = 0, nests = 0;
        foreach (var f in route.Features.Where(f => f.Stop is not null))
        {
            var stop = f.Stop!;
            for (int i = 0; i < stop.Buildings.Count; i++)
            {
                var b = stop.Buildings[i];
                if (!b.Open || !StopWalls.Walled(stop, i))
                    continue;
                int index = i;
                var kept = stop.Containers.Where(c => c.Building == index).ToList();
                var clutter = StopWalls.ClutterOf(stop, i);
                var nest = StopWalls.Nest(stop, i);
                // The Gaunt's house is dark; every other has its light.
                if (TownKit.HouseLight(b, kept, clutter, nest) is not var (x, y, height, lamp))
                {
                    Assert.NotNull(nest);
                    nests++;
                    continue;
                }
                Assert.Null(nest);
                foreach (var c in clutter.Where(c => c.Solid && !lamp))
                    Assert.False(Math.Abs(c.X - x) < c.Box.HalfX + 0.1 && Math.Abs(c.Y - y) < c.Box.HalfY + 0.1, $"{spec} at {f.Start:0}: house {i}'s candle is under its {c.Kind}");
                Assert.True(StopWalls.InParts(b, x, y), $"{spec} at {f.Start:0}: house {i}'s light is outside it");
                // Inside the room, not in a wall, and a candle not in a cupboard or a cabinet.
                foreach (var (wx, wy, hx, hy) in StopWalls.OpenWalls(b))
                    Assert.False(Math.Abs(x - wx) < hx && Math.Abs(y - wy) < hy, $"{spec} at {f.Start:0}: house {i}'s light is in a wall");
                if (!lamp)
                    foreach (var piece in StopWalls.Furniture(b, kept))
                        Assert.False(Math.Abs(x - piece.X) < piece.HalfX + 0.1 && Math.Abs(y - piece.Y) < piece.HalfY + 0.1,
                            $"{spec} at {f.Start:0}: house {i}'s candle is in its {piece.Kind}");
                Assert.InRange(height, 0.1f, 2.6f);
                candles += lamp ? 0 : 1;
                lamps += lamp ? 1 : 0;
            }
        }
        // Candles in the corners, and lamps.
        Assert.True(candles > 0 && lamps > 0 && nests > 0, $"{spec}: {candles} candles, {lamps} lamps, {nests} nests");
    }
    [Fact]
    public void AnOpenBarnOrShedIsARoomLitByALanternTurnedLowUnlessTheGauntSleepsInIt()
    {
        // Note 462 (note 417's "not yet"): inside an open barn, outbuilding or goods shed the moon and the sky are kept out, as
        // in an open house's parts (a Room the renderer's lighting leaves out). Note 475 (the director: "interior lighting
        // ... functional"): a hurricane lantern turned low hangs in it, inside its walls. Note 488: not in the one the Gaunt
        // roosts in (frontier:7's last stop has it in an outbuilding; deadLines:1 in two barns): its dark is the tell, as a
        // house's is.
        int seen = 0, dark = 0;
        foreach (var spec in new[] { "frontier:7", "deadLines:1" })
            Barns(spec, ref seen, ref dark);
        Assert.True(seen > 0 && dark > 1, $"{seen} lit barns or sheds, {dark} dark");
    }

    static void Barns(string spec, ref int seen, ref int dark)
    {
        var route = DarkTerritory.Sim.LineGen.Routes.Generate(Content, spec, 6);
        var line = route.Build();
        var trains = DataFile.Load<DarkTerritory.Sim.Train.TrainTuning>(Path.Combine(Content, DarkTerritory.Sim.Train.TrainTuning.File));
        var look = Look.Load(Content);
        foreach (var f in route.Features.Where(f => f.Stop is not null))
        {
            var stop = f.Stop!;
            for (int i = 0; i < stop.Buildings.Count; i++)
            {
                var b = stop.Buildings[i];
                if (!StopWalls.OpenShed(b) || !StopWalls.Shelled(stop, i) || stop.Holdouts.Any(h => h.Building == i))
                    continue;
                // Stood in the middle of it, at a crewman's eye, the train standing at the stop.
                double ground = WorldArt.Ground(route, f.Start + b.S, (float)b.D, 18);
                var eye = Run.StopWorld(line, f, b.Centre, ground + 1.6);
                var train = new DarkTerritory.Sim.Train.TrainOnLine(new DarkTerritory.Sim.Train.TrainDynamics(DarkTerritory.Sim.Train.Consist.Uniform(trains, 4, 1)), line, f.Start);
                var mesh = new MeshBuilder();
                new GreyboxScene { Look = look, Route = route, Time = 0.37 }.Build(mesh, train, eye);
                // (Camera-relative: the eye's at the origin.)
                Assert.Contains(mesh.Rooms, r => Inside(r, Vector3.Zero));
                // Its lantern: a light inside its walls, under its eaves. The Gaunt's has none, and nor has a powerhouse, lit by
                // its power instead (note 509; with no run here, it's dark).
                bool Lantern(PointLight l) => StopWalls.InParts(b, Along(l.Position), Across(l.Position))
                    && l.Position.Y > -1.6f && l.Position.Y < WorldArt.OpenShedHeight(b.Kind) - 1.6f;
                if (StopWalls.Nest(stop, i) is null && b.Kind != BuildingKind.Powerhouse)
                {
                    Assert.Contains(mesh.PointLights, Lantern);
                    seen++;
                }
                else
                {
                    Assert.DoesNotContain(mesh.PointLights, Lantern);
                    dark++;
                }

                // The light's place in the barn's own frame (x along it, y across), from camera-relative.
                double Along(Vector3 at) => Frame(at).X;
                double Across(Vector3 at) => Frame(at).Y;
                (double X, double Y) Frame(Vector3 at)
                {
                    var o = Run.StopWorld(line, f, StopWalls.InHouse(b, 0, 0), 0) with { Y = 0 };
                    var ex = (Run.StopWorld(line, f, StopWalls.InHouse(b, 1, 0), 0) with { Y = 0 }) - o;
                    var ey = (Run.StopWorld(line, f, StopWalls.InHouse(b, 0, 1), 0) with { Y = 0 }) - o;
                    var d = new Double3(eye.X + at.X, 0, eye.Z + at.Z) - o;
                    return (d.X * ex.X + d.Z * ex.Z, d.X * ey.X + d.Z * ey.Z);
                }
            }
        }
    }

    /// <summary>A camera-relative point's place in a building's own frame (x along it, y across, from its middle).</summary>
    static (double X, double Y) InFrame(RailLine line, RouteFeature f, StopBuilding b, Double3 eye, Vector3 at)
    {
        var o = Run.StopWorld(line, f, StopWalls.InHouse(b, 0, 0), 0) with { Y = 0 };
        var ex = (Run.StopWorld(line, f, StopWalls.InHouse(b, 1, 0), 0) with { Y = 0 }) - o;
        var ey = (Run.StopWorld(line, f, StopWalls.InHouse(b, 0, 1), 0) with { Y = 0 }) - o;
        var d = new Double3(eye.X + at.X, 0, eye.Z + at.Z) - o;
        return (d.X * ex.X + d.Z * ex.Z, d.X * ey.X + d.Z * ey.Z);
    }

    static bool Inside(Room r, Vector3 p)
    {
        var d = p - r.Centre;
        return Math.Abs(Vector3.Dot(d, Vector3.Normalize(r.Right))) <= r.Half.X && Math.Abs(Vector3.Dot(d, Vector3.Normalize(r.Up))) <= r.Half.Y
            && Math.Abs(Vector3.Dot(d, Vector3.Normalize(r.Back))) <= r.Half.Z;
    }

    [Fact]
    public void AYardsShedsAndItsStrongroomAreRoomsAlongTheirRoofedLengthsAndAGantrysCutIsOpenAir()
    {
        // Note 465 (note 462's "not yet"): a yard's walk-in sheds and its hero (note 387) keep the moon and sky out along each
        // roofed length; a gantry's cut through a shed (the castings under the hook) is the open air.
        // Each length has its lanterns turned low (note 475), but the one the Gaunt roosts in has none (note 488: frontier:7's
        // Talbot Foundry has it in a shed).
        int lengths = 0, cuts = 0, dark = 0;
        foreach (var spec in new[] { "frontier:7", "deadLines:2", "deepTerritory:2", "frontier:3" })
            Yard(spec, ref lengths, ref cuts, ref dark);
        Assert.True(lengths > 0 && cuts > 0 && dark > 0, $"{lengths} roofed lengths, {cuts} cuts, {dark} dark");
    }

    static void Yard(string spec, ref int lengths, ref int cuts, ref int dark)
    {
        var route = DarkTerritory.Sim.LineGen.Routes.Generate(Content, spec, 6);
        var line = route.Build();
        var trains = DataFile.Load<DarkTerritory.Sim.Train.TrainTuning>(Path.Combine(Content, DarkTerritory.Sim.Train.TrainTuning.File));
        var look = Look.Load(Content);
        foreach (var f in route.Features.Where(f => f.Stop is not null))
        {
            var stop = f.Stop!;
            for (int i = 0; i < stop.Buildings.Count; i++)
            {
                var b = stop.Buildings[i];
                if (b.Kind is not (BuildingKind.Shed or BuildingKind.Hero) || stop.Holdouts.Any(h => h.Building == i))
                    continue;
                var roofed = StopWalls.Roofed(stop, i).ToList();
                double ground = WorldArt.Ground(route, f.Start + b.S, (float)b.D, 18);
                var train = new DarkTerritory.Sim.Train.TrainOnLine(new DarkTerritory.Sim.Train.TrainDynamics(DarkTerritory.Sim.Train.Consist.Uniform(trains, 4, 1)), line, f.Start);
                MeshBuilder At(double x)
                {
                    var mesh = new MeshBuilder();
                    new GreyboxScene { Look = look, Route = route, Time = 0.37 }.Build(mesh, train, Run.StopWorld(line, f, StopWalls.InHouse(b, x, 0), ground + 1.6));
                    return mesh;
                }
                // Stood in the middle of each roofed length, a crewman's eye: a Room holds it, and a lantern hangs in it, under its
                // eaves, unless the Gaunt sleeps in it.
                bool roost = StopWalls.Nest(stop, i) is not null;
                foreach (var (lo, hi) in roofed)
                {
                    var eye = Run.StopWorld(line, f, StopWalls.InHouse(b, (lo + hi) / 2, 0), ground + 1.6);
                    var mesh = At((lo + hi) / 2);
                    Assert.Contains(mesh.Rooms, r => Inside(r, Vector3.Zero));
                    bool Lantern(PointLight l)
                    {
                        var (x, y) = InFrame(line, f, b, eye, l.Position);
                        return x > lo && x < hi && Math.Abs(y) < b.Width / 2 && l.Position.Y > -1.6f && l.Position.Y < WorldArt.YardShedHeight(b) - 1.6f;
                    }
                    if (roost)
                        Assert.DoesNotContain(mesh.PointLights, Lantern);
                    else
                        Assert.Contains(mesh.PointLights, Lantern);
                    lengths++;
                }
                if (roost && roofed.Count > 0)
                    dark++;
                // Stood in a cut (its length's stretches not roofed, between two or off an end): none does.
                var edges = roofed.SelectMany(r => new[] { r.Lo, r.Hi }).Prepend(-b.Length / 2).Append(b.Length / 2).ToList();
                for (int k = 0; k + 1 < edges.Count; k += 2)
                    if (edges[k + 1] - edges[k] > 2)
                    {
                        Assert.DoesNotContain(At((edges[k] + edges[k + 1]) / 2).Rooms, r => Inside(r, Vector3.Zero));
                        cuts++;
                    }
            }
        }
    }
    [Fact]
    public void TheHangersStrandsQuiverWhileSomeonesInItsHouse()
    {
        // Note 683 (the director, 13 Oct, App. F.5: "pulled up by a hanger despite not seeing one at all or not having any
        // telegraph"): its tell is the strands' quiver. Over a second, a quivering strand's end moves far more than a still
        // one's slow sway, and it shivers (many turns of direction, not the sway's one).
        static (double Path, int Turns) Moves(bool quiver)
        {
            double path = 0;
            int turns = 0;
            var last = GreyboxScene.StrandSway(10, 2, 7, quiver);
            float dLast = 0;
            for (int f = 1; f <= 120; f++)
            {
                var now = GreyboxScene.StrandSway(10 + f / 120.0, 2, 7, quiver);
                path += Vector3.Distance(now, last);
                float d = now.X - last.X;
                if (dLast != 0 && MathF.Sign(d) != MathF.Sign(dLast))
                    turns++;
                (last, dLast) = (now, d);
            }
            return (path, turns);
        }
        var still = Moves(false);
        var quivering = Moves(true);
        Assert.True(quivering.Path > 5 * still.Path, $"quivering {quivering.Path:0.000} m vs still {still.Path:0.000} m in a second");
        Assert.True(quivering.Turns >= 12, $"{quivering.Turns} turns in a second");
        Assert.True(still.Turns <= 2);
    }
}
