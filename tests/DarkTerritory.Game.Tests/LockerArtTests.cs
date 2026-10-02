using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The crew lockers as drawn (note 170): every grade fits its door's plate at one letter size, a door hangs in its
/// cabinet's face shut and out in the aisle open, and the repair kit fits a locker's shelf the way it's stowed.
/// </summary>
public class LockerArtTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TrainTuning Tuning = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));

    static CarShape KitCar()
    {
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning, 4, 1)), RailLine.Load(Path.Combine(Content, "lines/test-loop.json")), 1200);
        return train.Frames[Sim.World.RepairKitCar(train)!.Value].Shape;
    }

    static (Vector3 Min, Vector3 Max) Bounds(MeshAsset m) =>
        (m.Vertices.Aggregate(new Vector3(float.MaxValue), (a, v) => Vector3.Min(a, v.Position)),
         m.Vertices.Aggregate(new Vector3(float.MinValue), (a, v) => Vector3.Max(a, v.Position)));

    [Fact]
    public void EveryGradeFitsItsDoorAtOneLetterSize()
    {
        var shape = KitCar();
        float w = (float)Tuning.Kit.Lockers!.Width, h = (float)Tuning.Kit.Lockers.Height;
        float px = LockerKit.LetterPixel(shape.Lockers, w);
        // Big enough to read across the aisle: the letters 3.5 cm tall or more.
        Assert.True(BitmapFont.Default.Height * px >= 0.035f, $"{BitmapFont.Default.Height * px:0.000} m letters");
        foreach (var bay in shape.Lockers)
        {
            var door = LockerKit.Door(null, bay.Name, w, h, px);
            var (min, max) = Bounds(door);
            // Within the door's own outline (the handle stands proud of its face; nothing past its edges).
            Assert.InRange(min.Z, -0.001f, 0.01f);
            Assert.InRange(max.Z, w - 0.01f, w + 0.001f);
            Assert.True(BitmapFont.Default.Measure(bay.Name) * px <= w - 0.06f, bay.Name);
        }
    }

    [Fact]
    public void ADoorShutsInItsCabinetsFaceAndOpensClearOfIt()
    {
        var bay = KitCar().Lockers[0];
        var free = new Vector3(0, 1, (float)(bay.Box.Max.Z - bay.Box.Min.Z));
        var shut = Vector3.Transform(free, LockerKit.DoorAt(bay, open: false));
        var open = Vector3.Transform(free, LockerKit.DoorAt(bay, open: true));
        Assert.Equal((float)bay.Box.Max.X, shut.X, 3);
        Assert.Equal((float)bay.Box.Max.Z, shut.Z, 3);
        // Swung out off the cabinet's face and back towards the row, clear of the opening.
        Assert.True(open.X > bay.Box.Max.X + 0.15, $"{open}");
        Assert.True(open.Z < bay.Box.Min.Z, $"{open}");
    }

    [Fact]
    public void TheKitLiesOnItsShelfInsideItsLocker()
    {
        var shape = KitCar();
        var bay = Sim.World.KitLocker(shape)!.Value;
        var props = PropArt.Of(Look.Load(Content));
        var (min, max) = Bounds(props.Get("repair_kit")!);
        // Stowed at yaw 0 its length runs into the locker (Bodies.Stow); a body's 0.1 m radius over the shelf.
        var at = Lockers.SlotAt(bay, 0, shape.LockerShelves, 0.1);
        Assert.True(max.X - min.X < bay.Box.Max.X - bay.Box.Min.X - 0.02, $"{max.X - min.X} across a {bay.Box.Max.X - bay.Box.Min.X} deep locker");
        Assert.True(max.Z - min.Z < bay.Box.Max.Z - bay.Box.Min.Z - 0.02);
        Assert.True(at.Y + min.Y >= Lockers.SlotAt(bay, 0, shape.LockerShelves, 0).Y - 0.02);
    }
}
