using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// Note 996 (the director's playtest of 23 Oct, line 1: a felled coaling tower across the track, "the player has no way of
/// dealing with it"): at the wreck the crosshair says it's cleared by hand, Use held, with the crew's seconds so far.
/// </summary>
public class WreckPromptTests
{
    static readonly string Content = DataFile.FindContentRoot();

    [Fact]
    public void AtTheFelledTowerTheCrosshairSaysClearTheWreck()
    {
        var route = RouteGenerator.Generate(RouteTuning.Load(Content), RouteTier.Frontier, 7);
        var tower = route.Of(FeatureKind.Facility).First(f => f.Facility == FacilityKind.CoalingTower);
        var s = new PrototypeSession(Content, route, 4, enemies: true);
        var t = s.World.Enemies!.TowerJaw;
        int facility = s.World.Run!.Facilities.ToList().IndexOf(tower);
        var jaw = s.World.AddEnemy(id => TowerJaw.AtCoalingTower(id, s.World, facility, t));
        jaw.Restore(SpinePhase.Punish, 0, jaw.Health, -1, jaw.FallsAt, jaw.LineDistance, jaw.Lateral, (int)TowerJawMode.Wreck, 1, 0);
        double hint = s.Train.Dynamics.Distance;
        var at = jaw.FallsAt + new Double3(1.5, 0, 0);
        s.Player = PlayerMotor.SpawnOnGround(at with { Y = PlayerMotor.GroundAt(at, s.Train.Line, ref hint) }, s.Train.Line, hint, s.PlayerTuning);
        Assert.StartsWith("CLEAR THE WRECK", Hud.Prompt(s));
        // Out of reach of it: not about it.
        var away = jaw.FallsAt + new Double3(t.ClearReach + 6, 0, 0);
        s.Player = PlayerMotor.SpawnOnGround(away with { Y = PlayerMotor.GroundAt(away, s.Train.Line, ref hint) }, s.Train.Line, hint, s.PlayerTuning);
        Assert.False(Hud.Prompt(s)?.StartsWith("CLEAR THE WRECK", StringComparison.Ordinal) ?? false);
    }
}
