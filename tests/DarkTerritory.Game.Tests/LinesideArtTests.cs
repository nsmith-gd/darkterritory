using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The L0 sweep's art (note 149): a car's load in its cargo's cases, the line's boards and mail cranes as art, the dawn
/// coming up, and the Whistler's nest under it once it's there.
/// </summary>
public class LinesideArtTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);
    static readonly TrainTuning Tuning = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));

    static TrainOnLine Train() =>
        new(new TrainDynamics(Consist.Uniform(Tuning, 4, 1)), RailLine.Load(Path.Combine(Content, "lines/test-loop.json")), 1200);

    [Fact]
    public void TheNearSpruceIsModelledNarrowAndMassed()
    {
        // WorldKit.Spruce (the 5 October audit: from the chase camera the crossed cards read as a column of separate clumps):
        // boughs in many whorls, a narrow spire as black spruce is, beside the broader modelled pine; the far field's cards
        // are a handful of quads.
        var spruce = WorldKit.Spruce(Look, 1, 12);
        var pine = WorldKit.Pine(Look, 1, 12);
        var cards = NovaKit.Conifer(Look, 1, 12, 0.3f);
        float Width(MeshAsset m) => m.Vertices.Max(v => v.Position.X) - m.Vertices.Min(v => v.Position.X);
        Assert.True(spruce.Vertices.Length > cards.Vertices.Length * 20, $"{spruce.Vertices.Length} against the cards' {cards.Vertices.Length}");
        Assert.InRange(Width(spruce) / 12, 0.2f, 0.5f);
        Assert.True(Width(spruce) < Width(pine) * 0.75f, "a spire, narrower than the pine");
        // Its top a club, not a bare trunk: foliage up to the leader, and no bark above the boughs.
        Assert.True(spruce.Vertices.Max(v => v.Position.Y) > 11.5f);
    }

    [Fact]
    public void ACarsLoadComesInItsCargosCases()
    {
        var shape = Train().Frames[2].Shape;
        var sacks = PropArt.Of(Look).Get("freight_sacks")!;
        var food = TrainKit.Load(Look, shape, CargoKind.Food, 0);
        // Whole sacks stacked to fill the load's solids: a whole number of them, more than one.
        Assert.Equal(0, food.Vertices.Length % sacks.Vertices.Length);
        Assert.True(food.Vertices.Length / sacks.Vertices.Length >= 4);
        foreach (var cargo in new[] { CargoKind.Chemicals, CargoKind.Ore, CargoKind.Ammunition })
            Assert.NotEqual(food.Vertices.Length, TrainKit.Load(Look, shape, cargo, 0).Vertices.Length);
        // Inside the load's solids (the sim's collision), not through the walls.
        var cargoBox = shape.Solids.Where(s => s.Part == PartKind.Cargo).Select(s => s.Box).ToArray();
        float maxX = (float)cargoBox.Max(b => b.Max.X) + 0.05f, minX = (float)cargoBox.Min(b => b.Min.X) - 0.05f;
        Assert.All(food.Vertices, v => Assert.InRange(v.Position.X, minX, maxX));
        // Goods have no case of their own: the plain crate stack.
        Assert.Null(TrainKit.LoadProp(CargoKind.Goods));
    }

    [Fact]
    public void BoardsShineBackInTheLampAndCranesHangTheirBag()
    {
        var mesh = new MeshBuilder();
        var sign = new Sim.Route.Sign(1, Sim.Route.SignKind.SpeedLimit, 100, 100, 200, 25 / 3.6);
        Assert.True(Look.Art.LinesideBoard(mesh, sign, Vector3.Zero, Vector3.UnitX, Vector3.UnitZ, lit: false));
        Assert.True(Look.Art.LinesideBoard(mesh, sign, Vector3.Zero, Vector3.UnitX, Vector3.UnitZ, lit: true));
        Assert.Equal(1, mesh.Instances[0].Glow);
        Assert.True(mesh.Instances[1].Glow > 4);
        var cranes = new MeshBuilder();
        Assert.True(Look.Art.MailCrane(cranes, Vector3.Zero, Vector3.UnitX, Sim.Route.DropKind.Coal));
        // The crane, its two arms, and the bag.
        Assert.Equal(4, cranes.Instances.Count);
        // The bag in the clamps, reached in toward the line, at a doorway's height; coal's bag black.
        var bag = cranes.Instances[^1];
        Assert.InRange(bag.Model.Translation.X, 0.6f, 1.1f);
        Assert.InRange(bag.Model.Translation.Y, 2.0f, 2.7f);
        Assert.True(bag.Tint.X < 0.5f);
        // No word of a catch and the bag's to go (a client, the engine by): the crane's left, its arms out, empty.
        var gone = new MeshBuilder();
        Assert.True(Look.Art.MailCrane(gone, Vector3.Zero, Vector3.UnitX, Sim.Route.DropKind.Coal, hung: false));
        Assert.Equal(3, gone.Instances.Count);
    }

    [Fact]
    public void ACaughtBagIsSnatchedAndTheArmsFall()
    {
        var train = new Vector3(0, 0, -12);
        MeshBuilder At(double since)
        {
            var mesh = new MeshBuilder();
            Assert.True(Look.Art.MailCrane(mesh, Vector3.Zero, Vector3.UnitX, Sim.Route.DropKind.Mail, since, train));
            return mesh;
        }
        // Just taken: off the clamps, in toward the line and away with the car.
        var snatched = At(0.2);
        Assert.Equal(4, snatched.Instances.Count);
        var bag = snatched.Instances[^1].Model.Translation;
        Assert.True(bag.X > 1.1f, "in through the door, past the clamps");
        Assert.True(bag.Z < -1, "carried along with the car");
        // A second on: gone inside, and the arms hanging down by the post, their clamp ends below the hinge.
        var after = At(1.5);
        Assert.Equal(3, after.Instances.Count);
        foreach (var arm in after.Instances.Skip(1))
        {
            var clamp = Vector3.Transform(new Vector3(0.7f, 0, 0), arm.Model);
            Assert.True(clamp.Y < arm.Model.Translation.Y - 0.6f, "hanging, not held out");
            Assert.InRange(clamp.X, 0.05f, 0.3f);
        }
    }

    [Fact]
    public void AGuttedCarIsCharredAndSmoulders()
    {
        var shape = Train().Frames[2].Shape;
        var burning = new Effects.Burning(0.8f, true, 0, 0.8f);
        var out_ = new Effects.Burning(0, false, 60, 1);
        Assert.True(burning.Smoke > out_.Smoke && out_.Smoke > 0);
        Assert.True(new Effects.Burning(0, false, 900, 1).Smoke < 0.01f, "it's done smouldering in a quarter of an hour");
        Assert.True(out_.Char > 0.9f);
        // Alight: smoke out of it and the cracks' light; out, smoke only.
        var mesh = new MeshBuilder();
        Look.Art.Effects.CarSmoke(mesh, Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, shape, burning, 15, 3.2, 2);
        Assert.NotEmpty(mesh.PointLights);
        var smoulder = new MeshBuilder();
        Look.Art.Effects.CarSmoke(smoulder, Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, shape, out_, 0, 3.2, 2);
        Assert.Empty(smoulder.PointLights);
        // The charred body: darker than the same car unburnt, the scars as bad as the burn.
        var frame = Train().Frames[2];
        var vehicle = new Vehicle(2, false, 1);
        var clean = new MeshBuilder();
        var charred = new MeshBuilder();
        Assert.True(Look.Art.Car(clean, frame, frame.Origin, vehicle, false));
        Assert.True(Look.Art.Car(charred, frame, frame.Origin, vehicle, false, charred: out_.Char));
        var body = charred.Instances[0];
        Assert.True(body.Tint != default && body.Tint.X < 0.6f);
        Assert.True(body.Scar.X > clean.Instances[0].Scar.X);
    }

    [Fact]
    public void IronBridgesAreBuiltAsTheyAre()
    {
        // Distinct pieces, each of them iron (rust or oxide paint) and built along the bay.
        var girder = StructureKit.GirderBay(Look, 18, false);
        var truss = StructureKit.TrussSpan(Look, 18, false);
        var stone = StructureKit.ViaductBay(Look, 18, false);
        foreach (var piece in new[] { girder, truss })
        {
            Assert.NotEqual(stone.Vertices.Length, piece.Vertices.Length);
            Assert.InRange(piece.Vertices.Min(v => v.Position.Z), -StructureKit.Bay - 0.1f, -StructureKit.Bay + 1);
        }
        // The truss's cage goes up over the train (the roof crew clear under it); the girders are under the deck.
        Assert.True(truss.Vertices.Max(v => v.Position.Y) > 6.5f);
        Assert.True(girder.Vertices.Max(v => v.Position.Y) < 1.2f);
    }

    [Fact]
    public void TheDawnComesUpOverItsLastMinutes()
    {
        var dawn = Look.Tuning.Atmosphere.Dawn!;
        Assert.Equal(0, Look.DawnOf(dawn.LeadSeconds + 60));
        Assert.Equal(1, Look.DawnOf(0));
        var night = Look.Apply(FrameLighting.Night);
        var day = Look.Dawn(night, 1);
        Assert.Equal(dawn.FogColour, day.FogColor);
        Assert.True(day.Ambient > night.Ambient && Vector3.Dot(day.MoonDirection, Vector3.Normalize(dawn.SunDirection)) > 0.999f);
        Assert.Equal(night.FogColor, Look.Dawn(night, 0).FogColor);
    }

    [Fact]
    public void TheWhistlersNestIsUnderItOnceItsThere()
    {
        var art = new CreatureArt(Look, Content);
        var t = DataFile.Load<EnemyTuning>(Path.Combine(Content, EnemyTuning.File)).Whistler;
        var nest = PropArt.Of(Look).Get("whistler_nest")!;
        bool Nested(double seconds)
        {
            var w = new Whistler(7);
            w.Restore(SpinePhase.Grab, seconds, 3, -1, new Double3(10, 0, 10), 0, 0, 0, 0, 0);
            var mesh = new MeshBuilder();
            art.Enemy(mesh, Matrix4x4.CreateTranslation(10, 0, 10), w);
            return mesh.Instances.Any(i => i.Asset == nest);
        }
        Assert.False(Nested(t.NestDistance / t.RunSpeed * 0.5));
        Assert.True(Nested(t.NestDistance / t.RunSpeed + 1));
    }

    [Fact]
    public void TheLeavingsBesideTheLineAreDealtSoNoKindComesRoundAgainSoon()
    {
        // Note 325 (the director, GDD App. F.3: "the same three things over and over again"): what the railway left beside
        // its line is dealt from a deck of every kind, so a kind never comes up again within a few stretches, even across
        // a join between decks, and a few kilometres show nearly every kind there is.
        foreach (int seed in new[] { 1, 7, 42, 1234 })
        {
            var dealt = WorldArt.LeavingsAlong(0, 60_000, seed).ToList();
            Assert.True(dealt.Count > 400, $"seed {seed}: {dealt.Count} in 60 km");
            var last = new Dictionary<string, int>();
            foreach (var l in dealt)
            {
                if (last.TryGetValue(l.Kind, out int before))
                    Assert.True(l.Slot - before > 4, $"seed {seed}: {l.Kind} at stretch {before}, then again at {l.Slot}");
                last[l.Kind] = l.Slot;
                Assert.InRange(Math.Abs(l.Lateral), 3.1, 14.5);
            }
            Assert.True(dealt.Where(l => l.Along < 5000).Select(l => l.Kind).Distinct().Count() >= 12, $"seed {seed}: few kinds in 5 km");
        }
    }

    [Fact]
    public void EveryLeavingIsWithinItsBudget()
    {
        // The leavings' own pieces (JunkKit) are small props, a hut a medium one (GDD §27's budgets, as dt art check).
        var pieces = ArtCatalog.Entries(Look, Tuning).Where(e => e.Name.StartsWith("junk-", StringComparison.Ordinal)).ToList();
        Assert.True(pieces.Count >= 21, $"{pieces.Count} junk pieces in the catalog");
        foreach (var e in pieces)
            Assert.True(e.Make().Triangles <= e.Class.MaxTriangles, $"{e.Name} over its {e.Class.MaxTriangles}");
    }
}
