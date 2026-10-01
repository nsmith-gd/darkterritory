using System.Numerics;
using Ballast;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The Stoker (GDD v1.2 §21, App. A.5; tools/blender/stoker.py; ARCHITECTURE §8 note 120): in the firebox, seen only when
/// its door's open, its head out of the door into the cab and its hands on the floor.
/// </summary>
public class StokerTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);
    static readonly TrainTuning Train = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
    static readonly CarShape Engine = CarShape.Build(Train.Geometry, VehicleKind.Engine, true);
    static readonly Double3 Firebox = Engine.Interactables.First(i => i.Kind == InteractableKind.Firebox).Position;

    static Vertex[] Drawn(CreatureArt art, SpinePhase phase)
    {
        var s = new Stoker(1);
        s.Restore(phase, 1, 1, 0, Firebox, 0, 0, 0, 0, 0);
        var mesh = new MeshBuilder();
        // The engine's frame as the model's: the firebox's place at its origin.
        Assert.True(art.Enemy(mesh, Matrix4x4.CreateTranslation((float)Firebox.X, (float)Firebox.Y, (float)Firebox.Z), s));
        return mesh.Flattened();
    }

    [Fact]
    public void ItsSeenOnlyThroughTheOpenDoor()
    {
        var art = new CreatureArt(Look, Content);
        art.FireDoorOpen = null;
        Assert.Empty(Drawn(art, SpinePhase.Telegraph));
        Assert.Empty(Drawn(art, SpinePhase.Commit));
        art.FireDoorOpen = TrainKit.FireDoor(Engine) - new Vector3((float)Firebox.X, (float)Firebox.Y, (float)Firebox.Z);
        Assert.NotEmpty(Drawn(art, SpinePhase.Telegraph));
        Assert.NotEmpty(Drawn(art, SpinePhase.Commit));
        // Gone (or not yet in), nothing.
        Assert.Empty(Drawn(art, SpinePhase.Gone));
    }

    [Fact]
    public void ItsHeadIsOutOfTheDoorIntoTheCab()
    {
        var art = new CreatureArt(Look, Content) { FireDoorOpen = TrainKit.FireDoor(Engine) - new Vector3((float)Firebox.X, (float)Firebox.Y, (float)Firebox.Z) };
        var door = TrainKit.FireDoor(Engine);
        var v = Drawn(art, SpinePhase.Telegraph);
        // Into the cab is +Z from the backhead's face: its head and arms come out that way, some of it past the face.
        Assert.True(v.Max(p => p.Position.Z) > door.Z + 0.15f, $"out to z {v.Max(p => p.Position.Z)} from the face at {door.Z}");
        // And it's in the door: what's out in the cab is no wider than the firehole and its reach to the floor.
        var outside = v.Where(p => p.Position.Z > door.Z + 0.02f).ToArray();
        Assert.All(outside, p => Assert.InRange(p.Position.X, -0.6f, 0.6f));
        Assert.True(outside.Min(p => p.Position.Y) > (float)Firebox.Y - 0.05f, "its hands rest on the cab's floor, not under it");
    }
}
