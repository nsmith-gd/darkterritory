using System.Numerics;
using Ballast;
using Ballast.Assets;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// Tippy Toesie (GDD v1.2 §21, App. A.5; tools/blender/tippy_toesie.py; ARCHITECTURE §8 note 110): taller than the train
/// was built for, so it stoops under a car's roof and ducks through its doors (the standard doorway); hidden between
/// tries, but seen scuttling off; on its victim, stood to them.
/// </summary>
public class TippyToesieTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);
    static readonly CreatureArt Art = new(Look, Content);
    static readonly TrainTuning Train = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
    static readonly CarShape Car = CarShape.Build(Train.Geometry, VehicleKind.Cargo, hasCarBehind: true);

    static Model Model => Art.Get("tippy_toesie") ?? throw new FileNotFoundException("content/art/models/tippy_toesie.glb: run tools/models/build.sh tippy_toesie");

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

    [Fact]
    public void ItsTallerThanTheDoorsSoItDucksThroughThemAndStoopsInside()
    {
        float door = (float)Train.Geometry.Doorway.Height;
        var room = Car.Interior!.Value;
        float ceiling = (float)(room.Max.Y - Train.Geometry.Interior!.FloorHeight);
        // Stood on its points it's over a door's height, and nearly at a car's roof.
        Assert.True(Height("wait") > door + 0.25f, $"wait {Height("wait")}");
        Assert.True(Height("stalk") > door + 0.15f, $"stalk {Height("stalk")}");
        // Ducking, it clears the lintel by a hand; stooped, it's well under the roof (it bows: it doesn't brush it).
        Assert.True(Height("duck") < door - 0.15f, $"duck {Height("duck")} under a {door} m door");
        foreach (var clip in new[] { "stoop", "stalk_stoop", "flee", "smother" })
            Assert.True(Height(clip) < ceiling - 0.4f, $"{clip} {Height(clip)} under a {ceiling} m roof");
        foreach (var clip in new[] { "stoop", "stalk_stoop" })
            Assert.True(Height(clip) < Height(clip == "stoop" ? "wait" : "stalk") - 0.1f, $"{clip} is lower than standing");
    }

    [Fact]
    public void ItKnowsWhenItsInADoorwayOrUnderARoof()
    {
        var room = Car.Interior!.Value;
        double floor = Train.Geometry.Interior!.FloorHeight, l = Car.HalfLength, x = Train.Geometry.PlateX, w = Car.Bounds.Max.X;
        // Down the aisle: under the roof, no lintel.
        var aisle = CreatureArt.Room.Of(Car, new Double3(-0.3, floor, 0.5 - l / 2));
        Assert.True(aisle.Indoors && !aisle.Doorway);
        // At either end door, out through it along z; at a side door, out along x.
        foreach (int end in new[] { -1, 1 })
        {
            var at = CreatureArt.Room.Of(Car, new Double3(x, floor, end * (l - 0.3)));
            Assert.True(at.Doorway, $"end {end}");
            Assert.Equal(new Vector3(0, 0, end), at.Through);
            Assert.InRange(at.Headroom, Train.Geometry.Doorway.Height - 0.01, Train.Geometry.Doorway.Height + 0.01);
        }
        foreach (int side in new[] { -1, 1 })
        {
            var at = CreatureArt.Room.Of(Car, new Double3(side * (w - 0.3), floor, 0));
            Assert.True(at.Doorway, $"side {side}");
            Assert.Equal(new Vector3(side, 0, 0), at.Through);
        }
        // On the roof: in the open.
        var roof = CreatureArt.Room.Of(Car, new Double3(0, Car.RoofHeight, 0));
        Assert.False(roof.Indoors || roof.Doorway);
        // At the cab's doorway, under its lintel.
        var engine = CarShape.Build(Train.Geometry, VehicleKind.Engine, true);
        var lintel = engine.Solids.First(s => s.Part == PartKind.CabWall && s.Box.Min.Y > Train.Geometry.Engine.DeckHeight + 1.5).Box;
        Assert.True(CreatureArt.Room.Of(engine, new Double3(lintel.Centre.X - Math.Sign(lintel.Centre.X) * 0.3, Train.Geometry.Engine.DeckHeight, lintel.Centre.Z)).Doorway);
    }

    [Fact]
    public void HiddenBetweenTriesButSeenGoing()
    {
        var mesh = new MeshBuilder();
        int Drawn(TippyToesie t)
        {
            mesh.Clear();
            Assert.True(Art.Enemy(mesh, Matrix4x4.CreateTranslation(0, 0, -4), t));
            return mesh.Flattened().Length;
        }
        var tippy = new TippyToesie(1);
        // Never placed: nothing.
        tippy.Restore(SpinePhase.Dormant, 0.1, 2, 2, default, 0, 0, 0, -1, 0);
        Assert.Equal(0, Drawn(tippy));
        // Just seen (it's gone dormant from where it stood): scuttling off for a moment, then gone.
        tippy.Restore(SpinePhase.Dormant, 0.2, 2, 2, new Double3(0.3, 1.1, 2), 0, 0, 0, -1, 0);
        Assert.True(Drawn(tippy) > 0);
        tippy.Restore(SpinePhase.Dormant, 3, 2, 2, new Double3(0.3, 1.1, 2), 0, 0, 0, -1, 0);
        Assert.Equal(0, Drawn(tippy));
        // Stalking: there.
        tippy.Restore(SpinePhase.Telegraph, 1, 2, 2, new Double3(0.3, 1.1, 2), 0, 0, 0, 1, 0);
        Assert.True(Drawn(tippy) > 0);
    }

    [Fact]
    public void OnItsVictimItStandsTightBehindThemFacingTheWayTheyFace()
    {
        var mesh = new MeshBuilder();
        var tippy = new TippyToesie(1);
        tippy.Restore(SpinePhase.Grab, 1, 2, 2, new Double3(0.3, 1.1, 2), 0, 0, 0, 1, 0);
        // The victim 3 m off, facing +X: it's drawn behind them (towards −X of their feet), not where the sim left it.
        var prey = new CreatureArt.Prey(new Vector3(3, 0, -4), Vector3.UnitX);
        Assert.True(Art.Enemy(mesh, Matrix4x4.CreateTranslation(0, 0, -4), tippy, prey: prey));
        var v = mesh.Flattened();
        Assert.NotEmpty(v);
        float cx = v.Average(p => p.Position.X), cz = v.Average(p => p.Position.Z);
        Assert.InRange(cx, 2.0f, 3.1f);
        Assert.InRange(cz, -4.5f, -3.5f);
    }
}
