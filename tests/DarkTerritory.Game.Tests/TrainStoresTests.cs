using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The train's stores and the loot carried by hand (tools/models/recipes/train_stores.py, note 148): each made and the
/// right size for its body; the extinguisher's charge shown in its glass; every car's mount where the sim stands its
/// extinguisher; and a gun car's powder and shot locker holding what's left of the gun's stock.
/// </summary>
public class TrainStoresTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);
    static readonly TrainTuning Tuning = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));

    static TrainOnLine Train() =>
        new(new TrainDynamics(Consist.Uniform(Tuning, 4, 1)), RailLine.Load(Path.Combine(Content, "lines/test-loop.json")), 1200);

    static (Vector3 Min, Vector3 Max) Bounds(MeshAsset m) =>
        (m.Vertices.Aggregate(new Vector3(float.MaxValue), (a, v) => Vector3.Min(a, v.Position)),
         m.Vertices.Aggregate(new Vector3(float.MinValue), (a, v) => Vector3.Max(a, v.Position)));

    [Fact]
    public void TheStoresAreMadeAndTheToysFitTheirBodies()
    {
        var props = PropArt.Of(Look);
        foreach (var name in new[] { "toy_bear", "toy_horse", "toy_doll", "extinguisher", "extinguisher_mount", "repair_kit", "shot_locker", "powder_bag" })
            Assert.NotNull(props.Get(name));
        // A toy's body is 0.15 m round (Bodies.SpawnCrate): its foot 0.15 m under the body's middle, it sits on the floor
        // the body does.
        foreach (var toy in new[] { "toy_bear", "toy_horse", "toy_doll" })
        {
            var (min, max) = Bounds(props.Get(toy)!);
            Assert.InRange(max.Y - min.Y, 0.2f, 0.32f);
            Assert.InRange(min.Y, -0.16f, -0.14f);
        }
    }

    [Fact]
    public void TheExtinguisherShowsItsChargeInItsGlass()
    {
        var props = PropArt.Of(Look);
        var lo = props.Socket("extinguisher", "glass_lo")!.Value;
        var hi = props.Socket("extinguisher", "glass_hi")!.Value;
        // Up its front (+Z, the face the mount turns to the room), the low gland below the high.
        Assert.True(hi.Y - lo.Y > 0.3f && lo.Z > 0.08f, $"glass {lo} .. {hi}");
        float Top(double charge)
        {
            var mesh = new MeshBuilder();
            var b = new Sim.Physics.Body(1, Sim.Physics.BodyKind.Extinguisher, Sim.Player.PlayerState.World,
                new Ballast.Physics.PbdBody([new Ballast.Physics.Particle(new Double3(0, 0.15, 0), 1, 0.15)]))
            { Charge = charge };
            Assert.True(Look.Art.Body(mesh, [], b, Double3.Zero, 0.5, 0));
            // The water's the only thing lit that much and not wholly (TrainKit.SightWater).
            var water = mesh.Vertices.ToArray().Where(v => v.Emissive is > 0.55f and < 0.65f).ToArray();
            return water.Length == 0 ? float.NaN : water.Max(v => v.Position.Y);
        }
        Assert.True(Top(1) > Top(0.3) + 0.15f, $"full {Top(1)}, a third {Top(0.3)}");
        Assert.True(float.IsNaN(Top(0)), "a spent one's glass is empty");
    }

    [Fact]
    public void EveryRoomHasItsMountAndTheGunCarItsPowderAndShot()
    {
        var train = Train();
        var props = PropArt.Of(Look);
        var mount = props.Get("extinguisher_mount")!;
        var locker = props.Get("shot_locker")!;
        int Balls(int ammo, int car)
        {
            var v = train.Vehicles[car];
            v.Gun = v.Gun with { Ammo = ammo };
            var mesh = new MeshBuilder();
            Look.Art.Fittings(mesh, train.Frames[car], train.Frames[car].Origin, v);
            Assert.Contains(mesh.Instances, i => i.Asset == mount);
            Assert.Equal(v.HasGun, mesh.Instances.Any(i => i.Asset == locker));
            // What's in it: the balls and bags appended into the scene, a ball's worth of vertices and a bag's each.
            return mesh.Vertices.Length;
        }
        int gunCar = Enumerable.Range(1, train.Vehicles.Count - 1).First(i => train.Vehicles[i].HasGun && train.Frames[i].Shape.Interior is not null);
        int full = Balls(24, gunCar), half = Balls(12, gunCar), none = Balls(0, gunCar);
        Assert.True(full > half && half > none && none == 0, $"full {full}, half {half}, none {none}");
        Balls(0, 1);
        // The guard van's mount stands clear of its tool lockers.
        var shape = train.Frames[gunCar].Shape;
        var at = Sim.World.ExtinguisherMount(shape, shape.Interior!.Value);
        Assert.DoesNotContain(shape.Solids, s => s.Part == PartKind.Locker && s.Box.Contains(at with { Y = s.Box.Min.Y + 0.5 }));
    }
}
