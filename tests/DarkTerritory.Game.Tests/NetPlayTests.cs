using Ballast;
using DarkTerritory.Game;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Game.Tests;

/// <summary>Host and join over real UDP in one process: what the app's --host and --join do.</summary>
public class NetPlayTests
{
    static readonly string Content = DataFile.FindContentRoot();

    [Fact]
    public void AJoinerBuildsTheHostsWorldAndTheyPlayTogether()
    {
        using var host = NetPlaySession.HostGame(Content, new SessionSetup(Route: "frontier:7", Cars: 4, Enemies: true), port: 0);
        using var joiner = NetPlaySession.Join(Content, new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, host.Port), () => host.Step(default));

        // The joiner got the host's setup and built the same route and train from it.
        Assert.Equal("frontier:7", joiner.Setup.Route);
        Assert.Equal(host.Route!.Name, joiner.Route!.Name);
        Assert.Equal(host.Train.Frames.Count, joiner.Train.Frames.Count);

        // The host's own player came aboard first, so it has the cab and can drive; open the regulator.
        for (int t = 0; t < SimConstants.TickRate * 6; t++)
        {
            host.Step(new PlayerIntent { ThrottleNotch = (sbyte)(t % 10 == 0 ? 1 : 0) });
            joiner.Step(default);
            Thread.Sleep(1);
        }
        Assert.True(host.Client.Connected && joiner.Client.Connected);
        Assert.True(PlayerMotor.InCab(host.Player, host.Train));
        Assert.True(host.Controls.Throttle > 0);
        Assert.True(joiner.Train.Dynamics.Speed > 0.5, "the joiner should see the train moving");
        Assert.InRange(joiner.Train.Dynamics.Distance - host.Host!.Train.Dynamics.Distance, -1, 1);

        // Each sees the other.
        var frames = joiner.InterpolatedFrames(1);
        Assert.Single(joiner.Crew(frames, 1));
        Assert.Single(host.Crew(host.InterpolatedFrames(1), 1));
        Assert.Contains("aboard", joiner.Status());
    }

    [Fact]
    public void JoiningNobodyFailsCleanly()
    {
        int port;
        using (var probe = new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0)))
            port = ((System.Net.IPEndPoint)probe.Client.LocalEndPoint!).Port;
        Assert.Throws<IOException>(() => NetPlaySession.Join(Content, new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, port),
            options: new Ballast.Net.UdpOptions { ConnectSeconds = 0.5 }));
    }
}
