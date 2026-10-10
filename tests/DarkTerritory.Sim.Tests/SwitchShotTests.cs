using Ballast;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// A switch thrown by a cannonball (ARCHITECTURE §8 note 595; the director, 9 Oct 2026: "someone should be able to throw a
/// switch by shooting it with the cannon if their aim is good enough"): a ball that finds a stand's lever throws it over,
/// in the gunner's name; one that goes wide does nothing.
/// </summary>
public class SwitchShotTests
{
    static readonly PlayerTuning P = Tuning.Player;
    static readonly PlayerIntent Fire = new() { Buttons = PlayerButtons.Fire };

    /// <summary>A train stood short of a branch's points, a gunner in the engine's seat, and the branch.</summary>
    static (Night N, int Branch) Short()
    {
        var route = Routes.Generate(DataFile.FindContentRoot(), "frontier:7", 4);
        var line = route.Build();
        // Points ahead on the main line with a clear 90-160 m to them from the front.
        var b = line.Branches.First(b => b.Toe > 1500);
        var n = new Night(4, speed: 0, route: route, line: line, front: b.Toe - 120);
        var mount = n.Train.Frames[0].Shape.Gun!.Value;
        var s = PlayerMotor.SpawnOnRoof(n.Train, 0, mount.Position.Z - mount.Facing.Z * 0.7, P);
        s.Yaw = mount.Facing.Z < 0 ? 0 : Math.PI;
        s.Flags |= PlayerFlags.Seated;
        n.Crew[1] = s;
        return (n, b.Index);
    }

    /// <summary>The seated gunner's view, and the gun with it, laid straight on a point (world).</summary>
    static void LayOn(Night n, Double3 at)
    {
        var mount = Guns.Mount(n.Train, 0)!.Value;
        var frame = n.Train.Frames[0];
        var d = frame.DirToLocal(at - frame.ToWorld(mount.Position)).Normalized;
        double yaw = DMath.Atan2(-d.X, -d.Z), pitch = DMath.Asin(d.Y);
        ref var gun = ref n.Train.Vehicles[0].Gun;
        (gun.Traverse, gun.Elevation, gun.Cooldown, gun.ReloadNeeded) = (yaw - Guns.FacingYaw(mount), pitch, 0, 0);
        n.Crew[1] = n.Crew[1] with { Yaw = yaw, Pitch = pitch };
    }

    [Fact]
    public void ABallOnASwitchStandsLeverThrowsItOverInTheGunnersName()
    {
        var (n, branch) = Short();
        bool was = n.Train.Diverging(branch);
        LayOn(n, n.World.Switches!.LeverAt(n.Train.Line, branch));
        var thrown = new List<Rail.SwitchThrow>();
        n.Run(1.0 / SimConstants.TickRate, id => id == 1 ? Fire : default);
        thrown.AddRange(n.World.SwitchThrows);
        Assert.NotEqual(was, n.Train.Diverging(branch));
        var t = Assert.Single(thrown);
        Assert.Equal((branch, 1, !was, true), (t.Branch, t.PlayerId, t.Diverging, t.Moved));
        // And another good shot throws it back.
        LayOn(n, n.World.Switches!.LeverAt(n.Train.Line, branch));
        n.Run(1.0 / SimConstants.TickRate, id => id == 1 ? Fire : default);
        Assert.Equal(was, n.Train.Diverging(branch));
    }

    [Fact]
    public void AShotThatGoesWideOfTheLeverLeavesTheSwitchAsItIs()
    {
        var (n, branch) = Short();
        bool was = n.Train.Diverging(branch);
        var lever = n.World.Switches!.LeverAt(n.Train.Line, branch);
        // A couple of metres off it, on the far side of the line: the aim wasn't good enough.
        var side = (lever - n.Train.Line.Sample(n.Train.Line.Branches[branch].Toe).Position) with { Y = 0 };
        LayOn(n, lever - side.Normalized * 2.5);
        n.Run(1.0 / SimConstants.TickRate, id => id == 1 ? Fire : default);
        Assert.Equal(was, n.Train.Diverging(branch));
        Assert.Empty(n.World.SwitchThrows);
    }
}
