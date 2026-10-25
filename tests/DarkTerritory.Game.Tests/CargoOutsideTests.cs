using Ballast;
using DarkTerritory.Game.Art;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// A car's cargo read from outside it (GDD Part Eleven question 7; note 1025): the placards and stencils on its sides, the
/// stain once it's spoiled, its doors cracked on what's in it, and what's heard through the slats, all behind look.json
/// <c>cargoOutside</c>.
/// </summary>
public class CargoOutsideTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TrainTuning Train = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
    static readonly CarShape Shape = CarShape.Build(Train.Geometry, VehicleKind.Cargo, hasCarBehind: true);
    static readonly LookTuning Look = DataFile.Load<LookTuning>(Path.Combine(Content, LookTuning.File));

    [Fact]
    public void TheFlagIsOnAndItsLinesAreInOrder()
    {
        var t = Look.CargoOutside;
        Assert.True(t.Enabled, "on by default while the inbox's nathaniel3-gdd-cargo-visible is open");
        Assert.InRange(t.LostBelow, 0, t.SpoiledBelow);
        Assert.InRange(t.SpoiledBelow, t.LostBelow, 1);
        // Cracked, not open: a gap a crew sees into, nobody gets through (the side door's leaf slides its own width).
        double width = Shape.DoorList.Where(Side).Max(d => d.Box.Max.Z - d.Box.Min.Z);
        Assert.InRange(t.Crack * width, 0.1, 0.35);
    }

    [Fact]
    public void OnlyALoadedCargoCarShowsAndLostIsLost()
    {
        var t = new CargoOutsideTuning();
        var car = new Vehicle(1, VehicleKind.Cargo, 1) { Cargo = CargoKind.Medicine };
        Assert.True(t.Shows(car));
        Assert.False(t.Shows(new Vehicle(2, VehicleKind.Cargo, 0)));
        Assert.False(t.Shows(new Vehicle(3, VehicleKind.Guard, 1)));
        Assert.False(t.Shows(new Vehicle(4, VehicleKind.Cargo, 1) { Taken = true }));
        Assert.False(new CargoOutsideTuning { Enabled = false }.Shows(car));
        car.CargoIntegrity = 0.3;
        Assert.True(t.Spoiled(car));
        Assert.False(t.Lost(car));
        car.CargoIntegrity = 0;
        Assert.True(t.Lost(car));
    }

    [Theory]
    [InlineData(CargoKind.Ammunition)]
    [InlineData(CargoKind.Chemicals)]
    [InlineData(CargoKind.Medicine)]
    [InlineData(CargoKind.Comet)]
    public void APlacardedCargoIsMarkedOnBothSidesClearOfTheDoor(CargoKind cargo)
    {
        var mesh = CargoKit.Placards(null, Shape, cargo);
        Assert.InRange(mesh.Vertices.Length / 3, 1, ArtCatalog.MediumProp.MaxTriangles);
        double w = Shape.HalfWidth, h = Shape.RoofHeight;
        var sides = mesh.Vertices.Where(v => v.Position.Y < h - 0.1).ToList();
        var roof = mesh.Vertices.Where(v => v.Position.Y >= h - 0.1).ToList();
        Assert.Contains(sides, v => v.Position.X > w);
        Assert.Contains(sides, v => v.Position.X < -w);
        Assert.All(sides, v => Assert.True(Math.Abs(v.Position.X) > w, "on the outside of the wall"));
        // Behind the side door, past where its leaf slides to fully open: never under the door or its leaf.
        foreach (var door in Shape.DoorList.Where(Side))
        {
            double open = door.Box.Max.Z + (door.Box.Max.Z - door.Box.Min.Z) * Math.Max(0, door.Slide);
            Assert.All(sides, v => Assert.True(v.Position.Z > open, $"{cargo}: at z {v.Position.Z} under the door's slide (to {open})"));
        }
        // On the roof, either side of the walk, on the roof (not under it, not floating), clear of the hatch.
        Assert.Contains(roof, v => v.Position.X > 0.45);
        Assert.Contains(roof, v => v.Position.X < -0.45);
        Assert.All(roof, v => Assert.True(Math.Abs(v.Position.X) is > 0.45f and < 1.5f && v.Position.Y < h + 0.02
            && (Shape.Hatch is not { } hatch || v.Position.Z < hatch.Min.Z), $"{cargo}: roof paint at {v.Position}"));
        Assert.True(CargoKit.Stencil(cargo).Word.Length > 0);
    }

    [Theory]
    [InlineData(CargoKind.Goods)]
    [InlineData(CargoKind.Grain)]
    [InlineData(CargoKind.Livestock)]
    [InlineData(CargoKind.Coal)]
    public void AnOrdinaryCargoHasNoPlacard(CargoKind cargo) => Assert.Empty(CargoKit.Placards(null, Shape, cargo).Vertices);

    [Fact]
    public void TheStainRunsDownTheSideWhereItCanBeSeen()
    {
        var mesh = CargoKit.Stain(null, Shape, CargoKind.Livestock, 2);
        Assert.NotEmpty(mesh.Vertices);
        double floor = Shape.Interior!.Value.Min.Y;
        Assert.All(mesh.Vertices, v => Assert.True(Math.Abs(v.Position.X) > Shape.HalfWidth && v.Position.Y < floor + 0.15,
            $"a stain at {v.Position} is on the outside, run down from the floor"));
        // Not under a side door, where its landing stands in front of it.
        foreach (var door in Shape.DoorList.Where(Side))
            Assert.DoesNotContain(mesh.Vertices, v => Math.Sign(v.Position.X) == Math.Sign(door.Box.Centre.X)
                && v.Position.Z > door.Box.Min.Z && v.Position.Z < door.Box.Max.Z);
        // The same car, the same stain.
        Assert.Equal(mesh.Vertices.Select(v => v.Position), CargoKit.Stain(null, Shape, CargoKind.Livestock, 2).Vertices.Select(v => v.Position));
    }

    [Fact]
    public void BottlesClinkAsTheCarRidesAndAreCaptioned()
    {
        var bank = new Ballast.Audio.SoundBank(Path.Combine(Content, "audio", "sounds"));
        Assert.Equal(6, bank.Get("world-cargo.bottles")!.Tier);
        double Peak(double speed) => AudioBench.RenderSound(Content, "world-cargo.bottles", 4, new Dictionary<string, double> { ["speed"] = speed }).Report.PeakDb;
        double riding = Peak(12), stood = Peak(0);
        Assert.True(riding > stood + 6, $"riding {riding} dB, stood {stood} dB");
        Assert.InRange(riding, -14, 0);
        Assert.Equal("BOTTLES CLINKING", new Captions(Captions.Load(Content)).CaptionOf("world-cargo.bottles"));
    }

    static bool Side(Door d) => d.Box.Max.Z - d.Box.Min.Z > d.Box.Max.X - d.Box.Min.X;
}
