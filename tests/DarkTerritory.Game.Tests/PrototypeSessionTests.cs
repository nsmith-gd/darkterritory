using Ballast;
using DarkTerritory.Game;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>The single-player prototype runs the same host-side enemies as a real session, with text cues for the telegraphs.</summary>
public class PrototypeSessionTests
{
    static readonly string Content = DataFile.FindContentRoot();

    static PrototypeSession Frontier()
    {
        var tuning = DataFile.Load<RouteTuning>(Path.Combine(Content, RouteTuning.File));
        return new PrototypeSession(Content, RouteGenerator.Generate(tuning, RouteTier.Frontier, 7), cars: 4);
    }

    [Fact]
    public void TheLampsTelegraphShowsAsACueAndSpeedDerailsTheTrain()
    {
        var session = Frontier();
        double s = session.Train.Dynamics.Distance;
        session.World.AddEnemy(id => new Sleepers(id) { LineDistance = s + 150, Height = 0.2 });
        session.Train.Dynamics.Velocity = 15; // above the derail threshold, and nobody touches the brake
        string? cue = null;
        for (int i = 0; i < SimConstants.TickRate * 15 && session.Player.Alive; i++)
        {
            session.Step(default);
            if (session.Threats().Contains("lamp"))
                cue ??= session.Threats();
        }
        Assert.NotNull(cue);
        Assert.False(session.Player.Alive);
        Assert.Equal(DeathCause.Derailed, session.Player.Death);
    }

    [Fact]
    public void AClingerShowsItsDrillProgress()
    {
        var session = Frontier();
        var car = session.Train.Frames[1].Shape;
        session.World.AddEnemy(id => new Clinger(id) { Attached = 1, Local = new Double3(car.HalfWidth + 0.15, 2, 0) });
        for (int i = 0; i < SimConstants.TickRate * 10; i++)
            session.Step(default);
        Assert.Contains("drilling on car 1", session.Threats());
    }
}
