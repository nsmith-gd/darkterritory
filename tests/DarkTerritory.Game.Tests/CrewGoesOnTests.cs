using System.Net;
using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Towns;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The crew goes on together (queue #338, ARCHITECTURE §8 note 606; D1: "it's a co-op game, and town to town only holds if the
/// whole crew goes together"): a host leaving a delivered campaign night tells its crew it's going on from the town, and each
/// joiner follows it to the next night, the same route and town, without its menu in between.
/// </summary>
public class CrewGoesOnTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly PlayerTuning P = DataFile.Load<PlayerTuning>(Path.Combine(Content, PlayerTuning.File));

    [Fact]
    public void BothJoinersFollowTheHostToTheNextNightFromTheSameTown()
    {
        var first = NetPlaySession.HostGame(Content, new SessionSetup(Route: "frontier:7", Cars: 4, Enemies: false), port: 0);
        int port = first.Port;
        var at = new IPEndPoint(IPAddress.Loopback, port);
        using var a = NetPlaySession.Join(Content, at, () => first.Step(default));
        using var b = NetPlaySession.Join(Content, at, () => first.Step(default));
        void Run(NetPlaySession host, double seconds)
        {
            for (int t = 0; t < seconds * SimConstants.TickRate; t++)
            {
                host.Step(default);
                a.Step(default);
                b.Step(default);
            }
        }
        Run(first, 1);
        Assert.Equal(3, first.Host!.Players.Count());

        // Delivered to the terminus town (the campaign's Arrived, note 591), the host leaves for the next night and says so.
        var town = Assert.IsType<TownAt>(TownAt.Terminus(first.Route!, LineGenContent.Cached(Content).Config.Tiers.Fortress.Identities));
        first.CarryOn(town);
        Run(first, 0.5);
        Assert.Equal(town, a.GoingOn?.Town);
        Assert.Equal(town, b.GoingOn?.Town);
        string left = first.Setup.Route!;
        first.Dispose();
        // Gone: neither tries to rejoin the night it left (that's over); each is to follow.
        for (int t = 0; t < SimConstants.TickRate * 30 && !(a.Lost && b.Lost); t++)
        {
            a.Step(default);
            b.Step(default);
            Thread.Sleep(1);
        }
        Assert.True(a.Lost && b.Lost);
        Assert.False(a.Reconnecting || b.Reconnecting);
        var goingA = a.GoingOn!;
        var goingB = b.GoingOn!;

        // The host's next night: the contract it picked, from that town (note 591), on the same port as before.
        using var next = NetPlaySession.HostGame(Content, new SessionSetup(Route: TownAt.With("frontier:3", town), Cars: 4, Enemies: false), port: port);
        var quick = P with { Rejoin = P.Rejoin with { RetrySeconds = 0.1, FollowSeconds = 60 } };
        using var fa = NetPlaySession.Follow(Content, goingA, () => next.Step(default), quick);
        using var fb = NetPlaySession.Follow(Content, goingB, () => next.Step(default), quick);
        for (int t = 0; t < SimConstants.TickRate; t++)
        {
            next.Step(default);
            fa.Step(default);
            fb.Step(default);
        }

        // Both aboard the host's next night: its route, departing from the town they came in to, and not the night they left.
        Assert.Equal(3, next.Host!.Players.Count());
        foreach (var joined in new[] { fa, fb })
        {
            Assert.Equal(next.Setup.Route, joined.Setup.Route);
            Assert.NotEqual(left, joined.Setup.Route);
            Assert.Equal(town.Name, joined.Route!.Plan!.Fortress.Name);
            Assert.Equal(town.Seed, joined.Route.Plan.Fortress.TownSeed);
            Assert.Equal(next.World.Town!.Plan.Name, joined.World.Town!.Plan.Name);
        }
    }
}
