using Ballast;
using DarkTerritory.Game;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// A night starts at the fortress's gate, ready to depart (run.json <c>departShortOfGateM</c>): the train still in the yard,
/// so everyone joining boards at the fortress, and the run begins as it moves off rather than after a crawl down the yard
/// at yard speed.
/// </summary>
public class DepartureTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly RunTuning Run = DataFile.Load<RunTuning>(Path.Combine(Content, RunTuning.File));

    [Theory]
    [InlineData("frontier:7")]
    [InlineData("local:3")]
    [InlineData("deadLines:2")]
    public void TheEngineStandsAtTheGateAndTheRunBeginsAsItMovesOff(string spec)
    {
        var route = Sim.LineGen.Routes.Generate(Content, spec, 6);
        var session = new PrototypeSession(Content, route, cars: 6, enemies: false);
        double gate = session.World.Run!.YardLength;
        Assert.Equal(gate - Run.DepartShortOfGateM, session.Train.Dynamics.Distance, 6);
        Assert.Equal(RunPhase.Yard, session.World.Run.Phase);
        // Standing there while the crew gathers, it doesn't creep through the gate on its own.
        for (int t = 0; t < 90 * SimConstants.TickRate; t++)
            session.Step(default);
        Assert.Equal(RunPhase.Yard, session.World.Run.Phase);
        Assert.True(session.Train.Dynamics.Distance < gate, $"crept to {session.Train.Dynamics.Distance:0.0} of {gate:0.0}");
        // Regulator open: through the gate and under way within seconds, not minutes.
        session.Controls.Throttle = 0.6;
        int ticks = 0;
        for (; ticks < 30 * SimConstants.TickRate && session.World.Run.Phase == RunPhase.Yard; ticks++)
            session.Step(default);
        Assert.NotEqual(RunPhase.Yard, session.World.Run.Phase);
        Assert.True(ticks < 20 * SimConstants.TickRate, $"{ticks / (double)SimConstants.TickRate:0.0} s to the gate");
    }

    [Fact]
    public void AHostedNightStartsAtTheGateWithEveryoneAboard()
    {
        using var host = NetPlaySession.HostGame(Content, new SessionSetup(Route: "frontier:7", Cars: 4, Enemies: false), port: 0);
        using var joiner = NetPlaySession.Join(Content, new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, host.Port), () => host.Step(default));
        for (int t = 0; t < SimConstants.TickRate * 2; t++)
        {
            host.Step(default);
            joiner.Step(default);
            Thread.Sleep(1);
        }
        var world = host.Host!.World;
        Assert.Equal(world.Run!.YardLength - Run.DepartShortOfGateM, host.Host.Train.Dynamics.Distance, 1);
        Assert.Equal(RunPhase.Yard, world.Run.Phase);
        // Both aboard at the fortress: the run hasn't left the gate, so nobody's lobbied (GDD App. D.3; a lobbied joiner
        // waits, not alive).
        Assert.Equal(2, host.Host.Players.Count());
        Assert.All(host.Host.Players, p => Assert.True(p.State.Alive));
        Assert.Equal(host.Host.Train.Dynamics.Distance, joiner.Train.Dynamics.Distance, 1);
    }
}
