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
/// A powder charge carried to a gun (note 992; the art checklist's powder-shot not-yet: "the carried charge uses the generic
/// carry clip"): a crewmate carrying it hugs it low in both hands (crew_clips' powder), and it's drawn there, between their
/// hands, once, not out in front at the crate's height where the sim holds it.
/// </summary>
public class PowderCarryTests
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
    /// A crewmate carrying a charge, in the powder's pose or not: how much is drawn about the sim's hold (two metres off them,
    /// in the air, where nothing else is), and where the bag was set between their hands (its foot, relative to their feet),
    /// if it was.
    /// </summary>
    static (int AtHold, Vector3? Hugged) Charge(CrewPose? act)
    {
        var train = Train();
        var feet = train.Frames[1].ToWorld(new Double3(0, train.Frames[1].Shape.RoofHeight, 0));
        var held = feet + new Double3(2, 1.2, 0);
        var bag = new Body(5, BodyKind.Powder, PlayerState.World, new PbdBody([new Particle(held, 1, 0.1)])) { Carrier = 3 };
        var eye = feet + new Double3(3, 1.5, 2);
        var mesh = new MeshBuilder();
        var scene = new GreyboxScene { Look = Look, Time = 0.37, Crew = [new Crewmate(3, feet, 0, true, default) with { Act = act }], Bodies = [bag] };
        Look.Art.Creatures!.Crewmate(new MeshBuilder(), Matrix4x4.Identity, CrewPose.Idle, 0, 0);
        var before = Look.Art.Creatures.LastCarried;
        scene.Build(mesh, train, eye);
        var hold = held.RelativeTo(eye);
        int atHold = mesh.Flattened().Count(v => Vector3.Distance(v.Position, hold) < 0.4f);
        var last = Look.Art.Creatures.LastCarried;
        return (atHold, last != before ? last - feet.RelativeTo(eye) : null);
    }

    [Fact]
    public void ACarriedChargeIsHuggedLowInBothHandsOnce()
    {
        var (atHold, hugged) = Charge(CrewPose.Powder);
        Assert.Equal(0, atHold);
        var bag = Assert.NotNull(hugged);
        // Its foot at the belly (its middle 0.1 m over that), under the crate's 1.1 m: low.
        Assert.InRange(bag.Y, 0.7f, 0.95f);
        // In front of them (facing −Z, yaw 0), clear of the coat, in the middle between the hands.
        Assert.InRange(-bag.Z, 0.18f, 0.45f);
        Assert.InRange(bag.X, -0.08f, 0.08f);
    }

    [Fact]
    public void NotHuggedItsWhereTheSimHasIt()
    {
        var (atHold, _) = Charge(CrewPose.Carry);
        Assert.True(atHold > 20, $"{atHold} vertices drawn at the sim's hold");
    }

    [Fact]
    public void ACarriedChargeIsThePowderPose()
    {
        Assert.Equal("powder", CreatureArt.ClipOf(CrewPose.Powder));
        Assert.Equal("powder_walk", CreatureArt.ClipOf(CrewPose.PowderWalk));
        Assert.NotNull(Look.Art.Creatures!.Get("crew")!.Clip("powder"));
        Assert.NotNull(Look.Art.Creatures.Get("crew")!.Clip("powder_walk"));
    }
}
