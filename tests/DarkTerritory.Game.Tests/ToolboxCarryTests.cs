using System.Numerics;
using Ballast;
using Ballast.Physics;
using Ballast.Render;
using DarkTerritory.Game.Art;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The repair kit carried in one hand (note 513; the art checklist's repair-kit "next": "a one-hand toolbox carry clip"):
/// a crewmate carrying it holds it by its handle down at their right side (crew_clips' toolbox), and it's drawn there, in
/// the fist, once, not out in front where the sim holds it.
/// </summary>
public class ToolboxCarryTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Look Look = Look.Load(Content);

    static TrainOnLine Train()
    {
        var t = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
        var line = RailLine.Load(Path.Combine(Content, "lines", "test-loop.json"));
        return new TrainOnLine(new TrainDynamics(Consist.Uniform(t, 3, 1)), line, 1200);
    }

    /// <summary>
    /// A crewmate carrying the kit, stood or not in the toolbox's pose: how much is drawn about the sim's hold (two metres
    /// off them, in the air, where nothing else is), and where the fist set the kit down (relative to their feet), if it did.
    /// </summary>
    static (int AtHold, Vector3? InHand) Kit(CrewPose? act)
    {
        var train = Train();
        var feet = train.Frames[1].ToWorld(new Double3(0, train.Frames[1].Shape.RoofHeight, 0));
        var held = feet + new Double3(2, 1.2, 0);
        var kit = new Body(5, BodyKind.RepairKit, PlayerState.World, new PbdBody([new Particle(held, 1, 0.15)])) { Carrier = 3 };
        var eye = feet + new Double3(3, 1.5, 2);
        var mesh = new MeshBuilder();
        var scene = new GreyboxScene { Look = Look, Time = 0.37, Crew = [new Crewmate(3, feet, 0, true, default) with { Act = act }], Bodies = [kit] };
        scene.Build(mesh, train, eye);
        var hold = held.RelativeTo(eye);
        int atHold = mesh.Flattened().Count(v => Vector3.Distance(v.Position, hold) < 0.4f);
        Vector3? inHand = Look.Art.KitInHand(3, scene.Time) ? Look.Art.Creatures!.LastCarried - feet.RelativeTo(eye) : null;
        return (atHold, inHand);
    }

    [Fact]
    public void CarriedByItsHandleItHangsAtTheirRightSideOnce()
    {
        var (atHold, inHand) = Kit(CrewPose.Toolbox);
        Assert.Equal(0, atHold);
        var kit = Assert.NotNull(inHand);
        // Down at their side: within an arm's length of them across the ground, its foot between their knee and their shin.
        Assert.InRange(new Vector2(kit.X, kit.Z).Length(), 0.15f, 0.6f);
        Assert.InRange(kit.Y, 0.2f, 0.75f);
        // Facing −Z (yaw 0), their right is +X.
        Assert.True(kit.X > 0.1f, $"the kit at {kit}, not at their right");
    }

    [Fact]
    public void NotInTheirHandItsWhereTheSimHasIt()
    {
        var (atHold, inHand) = Kit(null);
        Assert.Null(inHand);
        Assert.True(atHold > 20, $"{atHold} vertices drawn at the sim's hold");
    }

    [Fact]
    public void ACarriedKitIsTheToolboxPose()
    {
        Assert.Equal("toolbox", CreatureArt.ClipOf(CrewPose.Toolbox));
        Assert.Equal("toolbox_walk", CreatureArt.ClipOf(CrewPose.ToolboxWalk));
    }
}
