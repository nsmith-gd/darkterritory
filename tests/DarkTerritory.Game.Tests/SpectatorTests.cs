using Ballast;
using DarkTerritory.Game;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// GDD App. D.10: dead, or waiting to board, you watch the living crew through their eyes. Whoever's first when you die,
/// Fire or a step right for the next, a step left back; when the one you watch dies, on to the next. No free camera.
/// </summary>
public class SpectatorTests
{
    static readonly string Content = DataFile.FindContentRoot();

    [Fact]
    public void TheDeadWatchTheLivingAndCycleThroughThem()
    {
        using var host = NetPlaySession.HostGame(Content, new SessionSetup(Route: "frontier:7", Cars: 4, Enemies: false), port: 0);
        var address = new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, host.Port);
        using var a = NetPlaySession.Join(Content, address, () => host.Step(default));
        using var b = NetPlaySession.Join(Content, address, () => { host.Step(default); a.Step(default); });
        void Step(int ticks, PlayerIntent intent = default)
        {
            for (int t = 0; t < ticks; t++)
            {
                host.Step(default);
                a.Step(intent);
                b.Step(default);
                Thread.Sleep(1);
            }
        }
        Step(SimConstants.TickRate);
        Assert.Equal(-1, a.Watching);
        Assert.Equal(a.Player, a.Viewpoint);

        var server = host.Host!;
        server.SetPlayerState((byte)a.PlayerId, a.Player with { Health = 0, Death = DeathCause.Mauled });
        Step(SimConstants.TickRate / 2);
        // The first of the living: the host's own player, in the cab.
        Assert.Equal(host.PlayerId, a.Watching);
        var hostEyes = PlayerMotor.WorldPosition(host.Player, host.Train) + Double3.Up * Eyes.Height;
        var seen = a.EyeCamera(a.InterpolatedFrames(1), 1, 0, 0);
        Assert.True((seen.Position - hostEyes).Length < 0.5, $"the camera's {(seen.Position - hostEyes).Length:0.00} m from the host's eyes");
        Assert.True((host.Player.Position - a.Viewpoint.Position).Length < 0.5);

        // Fire: the next. A step right wraps round; a step left goes back.
        Step(1, new PlayerIntent { Buttons = PlayerButtons.Fire });
        Step(1);
        Assert.Equal(b.PlayerId, a.Watching);
        Step(1, new PlayerIntent { MoveX = 1 });
        Step(1);
        Assert.Equal(host.PlayerId, a.Watching);
        Step(1, new PlayerIntent { MoveX = -1 });
        Step(1);
        Assert.Equal(b.PlayerId, a.Watching);
        // Held, it's one step, not one a tick.
        Step(10, new PlayerIntent { Buttons = PlayerButtons.Fire });
        Assert.Equal(host.PlayerId, a.Watching);
        Step(1);

        // The one you watch dies: on to whoever's left.
        server.SetPlayerState((byte)host.PlayerId, host.Player with { Health = 0, Death = DeathCause.Mauled });
        Step(SimConstants.TickRate / 2);
        Assert.Equal(b.PlayerId, a.Watching);
        // Nobody left: your own eyes, and the HUD says so.
        server.SetPlayerState((byte)b.PlayerId, b.Player with { Health = 0, Death = DeathCause.Mauled });
        Step(SimConstants.TickRate / 2);
        Assert.Equal(-1, a.Watching);
        Assert.Equal(a.Player, a.Viewpoint);
    }
}
