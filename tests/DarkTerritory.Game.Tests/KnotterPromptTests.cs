using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// Note 945 (the director's playtest of 20 Oct evening, line 20: "I have hit this thing so many times from standing up here. Oh,
/// it won't die ... Can it not die while we're driving?"): a Knotter held taut shrugs blows off by design (GDD §21; its brief:
/// stop, kill it, couple up), and now the crosshair says so to whoever's at it.
/// </summary>
public class KnotterPromptTests
{
    static readonly string Content = DataFile.FindContentRoot();

    [Fact]
    public void AtATautKnotterTheCrosshairSaysStopTheTrain()
    {
        var s = new PrototypeSession(Content, RouteGenerator.Generate(RouteTuning.Load(Content), RouteTier.Frontier, 1), 4, enemies: true);
        const int car = 2;
        var shape = s.Train.Frames[car].Shape;
        var k = s.World.AddEnemy(id => Knotter.Into(id, s.Train, car, s.World.Enemies!.Knotter));
        k.Restore(SpinePhase.Telegraph, 1, k.Health, car, k.Local, 0, 0, (int)KnotterMode.Taut, -1, 0);
        // On the roof's back end over its gap, facing back along the train at it.
        s.Player = PlayerMotor.SpawnOnRoof(s.Train, car, shape.HalfLength - 0.8, s.PlayerTuning) with { Yaw = Math.PI };
        Assert.Equal(Hud.KnotterTaut, Hud.Prompt(s));
        // Turned away from it: not about it.
        s.Player = s.Player with { Yaw = 0 };
        Assert.NotEqual(Hud.KnotterTaut, Hud.Prompt(s));
        // Slack at a stand, a blow lands: nothing to say.
        s.Player = s.Player with { Yaw = Math.PI };
        k.Restore(SpinePhase.Telegraph, 1, k.Health, car, k.Local, 0, 0, (int)KnotterMode.Slack, -1, 0);
        Assert.NotEqual(Hud.KnotterTaut, Hud.Prompt(s));
    }
}
