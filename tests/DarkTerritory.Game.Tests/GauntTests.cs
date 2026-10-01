using System.Numerics;
using Ballast;
using Ballast.Assets;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The Gaunt (GDD v1.2 §21, App. A.6; tools/blender/gaunt.py; ARCHITECTURE §8 note 118): far too tall for the train, so
/// aboard it drags itself along on its arms and squats to listen; woken, it faces its waker, lopes after them while they
/// go and stands over them when they stop, leant in further at every point of its anger.
/// </summary>
public class GauntTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);
    static readonly CreatureArt Art = new(Look, Content);
    static readonly TrainTuning Train = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
    static readonly CarShape Car = CarShape.Build(Train.Geometry, VehicleKind.Cargo, hasCarBehind: true);
    static readonly Matrix4x4 There = Matrix4x4.CreateTranslation(0, 0, -4);

    static Model Model => Art.Get("gaunt") ?? throw new FileNotFoundException("content/art/models/gaunt.glb: run tools/models/build.sh gaunt");

    /// <summary>How tall it stands in a clip (m): the highest skinned vertex over its frames.</summary>
    static float Height(string clip)
    {
        var m = Model;
        var c = m.Clips[clip];
        var pose = new Pose(m.Skeleton.Count);
        var skinner = new Skinner();
        float top = float.NegativeInfinity;
        for (int f = 0; f < c.Frames; f += 2)
        {
            skinner.Evaluate(m, c, f / (double)c.Fps, true, pose);
            foreach (var p in m.Parts)
                for (int i = 0; i < p.Positions.Length; i++)
                {
                    var v = Vector3.Zero;
                    for (int k = 0; k < 4; k++)
                        if (p.Weights[i * 4 + k] > 0)
                            v += Vector3.Transform(p.Positions[i], pose.Skin[p.Joints[i * 4 + k]]) * p.Weights[i * 4 + k];
                    top = MathF.Max(top, v.Y);
                }
        }
        return top;
    }

    static Gaunt Woken(SpinePhase phase = SpinePhase.Telegraph, double anger = 0)
    {
        var g = new Gaunt(1);
        g.Restore(phase, 1.3, 8, Enemy.Loose, default, 0, 0, 0, 2, anger);
        return g;
    }

    static Vertex[] Drawn(Gaunt g, CreatureArt.Prey? prey = null, CreatureArt.Room? room = null, float pace = 0)
    {
        var mesh = new MeshBuilder();
        Assert.True(Art.Enemy(mesh, There, g, prey: prey, room: room, pace: pace));
        var v = mesh.Flattened();
        Assert.NotEmpty(v);
        return v;
    }

    [Fact]
    public void ItCantStandUpInACarSoAboardItGetsDown()
    {
        float door = (float)Train.Geometry.Doorway.Height;
        var room = Car.Interior!.Value;
        float ceiling = (float)(room.Max.Y - Train.Geometry.Interior!.FloorHeight);
        // Stood (stooped, even) it's over a door's height and near a car's roof.
        foreach (var clip in new[] { "listen", "follow" })
            Assert.True(Height(clip) > door + 0.2f, $"{clip} {Height(clip)}");
        // Dragging itself along it goes under a lintel; squatted or smashing from the squat, under the roof.
        Assert.True(Height("crawl") < door - 0.3f, $"crawl {Height("crawl")} under a {door} m door");
        foreach (var clip in new[] { "squat", "smash" })
            Assert.True(Height(clip) < ceiling - 0.2f, $"{clip} {Height(clip)} under a {ceiling} m roof");
    }

    [Fact]
    public void ItsDrawnAsTheRoomLetsItAndAsFastAsItsGoing()
    {
        static float Top(Vertex[] v) => v.Max(p => p.Position.Y);
        var inside = CreatureArt.Room.Of(Car, new Double3(-0.3, Train.Geometry.Interior!.FloorHeight, 0.5 - Car.HalfLength / 2));
        Assert.True(inside.Indoors);
        float door = (float)Train.Geometry.Doorway.Height;
        // Outside it's stood up, going or not; inside it's down, going (crawling) or not (squatted).
        foreach (float pace in new[] { 0f, 1.5f })
        {
            Assert.True(Top(Drawn(Woken(), pace: pace)) > door, $"outside at {pace} m/s");
            Assert.True(Top(Drawn(Woken(), room: inside, pace: pace)) < door - 0.3f, $"inside at {pace} m/s");
        }
        // And going, it's another clip from standing: the lope's not the listen.
        var still = Drawn(Woken());
        var going = Drawn(Woken(), pace: 1.5f);
        Assert.NotEqual(still.Average(p => p.Position.Z), going.Average(p => p.Position.Z));
    }

    [Fact]
    public void ItsAngerLeansItInAndTipsItsHeadOver()
    {
        var calm = Drawn(Woken());
        var angry = Drawn(Woken(anger: 4));
        // Leant in: lower (its head was out ahead of its chest already: the bend brings it down more than on), and further
        // out in front of it (the model faces −Z).
        Assert.True(angry.Max(p => p.Position.Y) < calm.Max(p => p.Position.Y) - 0.1f, $"top {angry.Max(p => p.Position.Y)} from {calm.Max(p => p.Position.Y)}");
        // (Its front over the height its hands hang to: its face.)
        static float Head(Vertex[] v) => v.Where(p => p.Position.Y > 1.4f).Min(p => p.Position.Z);
        Assert.True(Head(angry) < Head(calm) - 0.05f, $"head at z {Head(angry)} from {Head(calm)}");
        // (And no more than that at the threshold: its anger saturates.)
        Assert.Equal(Drawn(Woken(anger: 4)).Max(p => p.Position.Y), Drawn(Woken(anger: 9)).Max(p => p.Position.Y), 3);
    }

    [Fact]
    public void WokenItFacesItsWaker()
    {
        // Its waker off to its +X: its head's out over them, to that side of its feet.
        var prey = new CreatureArt.Prey(new Vector3(3, 0, -4), -Vector3.UnitX);
        var v = Drawn(Woken(), prey: prey);
        float top = v.Max(p => p.Position.Y);
        float head = v.Where(p => p.Position.Y > top - 0.3f).Average(p => p.Position.X);
        Assert.True(head > 0.15f, $"head at x {head}");
        // Asleep it faces nobody.
        var asleep = new Gaunt(1);
        asleep.Restore(SpinePhase.Dormant, 1, 8, Enemy.Loose, default, 0, 0, 0, -1, 0);
        var heap = Drawn(asleep, prey: prey);
        Assert.True(heap.Max(p => p.Position.Y) < 1.4f, "asleep, it's a heap");
    }
}
