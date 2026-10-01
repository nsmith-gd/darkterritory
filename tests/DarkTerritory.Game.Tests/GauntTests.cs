using System.Numerics;
using Ballast;
using Ballast.Assets;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The Gaunt (GDD v1.2 §21, App. A.6; tools/blender/gaunt.py; ARCHITECTURE §8 notes 118, 131): a thing on stilts, far
/// too tall for the train, so aboard it creeps with its legs folded and squats to listen; woken, it faces its waker,
/// stalks after them while they go and stands over them when they stop, leant in further at every point of its anger.
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
        // (Its head and ears: the front of it, out ahead of its forelegs (the model faces −Z), over the height of a man's
        // chest.)
        static Vertex[] Head(Vertex[] v)
        {
            float front = v.Where(p => p.Position.Y > 1.2f).Min(p => p.Position.Z);
            return v.Where(p => p.Position.Y > 1.2f && p.Position.Z < front + 0.35f).ToArray();
        }
        // Leant in: its head let down lower over you (its neck from the shoulders; at full anger to just over a crewmate's
        // helmet, not into it), and no further back.
        float calmY = Head(calm).Average(p => p.Position.Y), angryY = Head(angry).Average(p => p.Position.Y);
        Assert.True(angryY < calmY - 0.06f, $"head at y {angryY} from {calmY}");
        Assert.True(Head(angry).Min(p => p.Position.Z) < Head(calm).Min(p => p.Position.Z) + 0.05f, "still out over you");
        // And tipped over: its ears no longer level.
        static float Roll(Vertex[] v) => v.Where(p => p.Position.X > 0).Average(p => p.Position.Y) - v.Where(p => p.Position.X < 0).Average(p => p.Position.Y);
        Assert.True(MathF.Abs(Roll(Head(angry)) - Roll(Head(calm))) > 0.05f, $"roll {Roll(Head(angry))} from {Roll(Head(calm))}");
        // (And no more than that at the threshold: its anger saturates.)
        Assert.Equal(Head(Drawn(Woken(anger: 4))).Average(p => p.Position.Y), Head(Drawn(Woken(anger: 9))).Average(p => p.Position.Y), 3);
    }

    [Fact]
    public void WokenItFacesItsWaker()
    {
        // Its waker off to its +X: its head's out over them, to that side of its feet. (Its head and ears are half of it:
        // the mesh's middle goes with them.)
        var prey = new CreatureArt.Prey(new Vector3(3, 0, -4), -Vector3.UnitX);
        var v = Drawn(Woken(), prey: prey);
        float head = v.Average(p => p.Position.X);
        Assert.True(head > 0.15f, $"middle at x {head}");
        // Asleep it faces nobody.
        var asleep = new Gaunt(1);
        asleep.Restore(SpinePhase.Dormant, 1, 8, Enemy.Loose, default, 0, 0, 0, -1, 0);
        var heap = Drawn(asleep, prey: prey);
        Assert.True(heap.Max(p => p.Position.Y) < 1.4f, "asleep, it's a heap");
    }
}
