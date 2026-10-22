using Ballast;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Game.Tests;

/// <summary>The human cannonball's gunner (note 746): a gun loaded with a crewmate says who goes when it's fired.</summary>
public class CannonballHudTests
{
    [Fact]
    public void TheGunnerOfAGunLoadedWithACrewmateIsToldWho()
    {
        var s = new PrototypeSession(DataFile.FindContentRoot(), "test-loop", 4);
        s.World.Names[2] = "Sam";
        int gun = s.Train.Vehicles.Count - 1;
        var inside = new PlayerState { Parent = gun, Flags = PlayerFlags.InCannon };
        Assert.Equal("LOADED: SAM", Hud.Loaded(s.World, gun, [(1, s.Player), (2, inside)]));
        // In another car's gun, or out of it: a ball (or nothing), so no line.
        Assert.Null(Hud.Loaded(s.World, gun - 1, [(1, s.Player), (2, inside)]));
        Assert.Null(Hud.Loaded(s.World, gun, [(1, s.Player), (2, inside with { Flags = PlayerFlags.None })]));
    }
}
