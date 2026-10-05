using Ballast;
using Ballast.Online;
using DarkTerritory.Game;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Game.Tests;

/// <summary>Host and join over real UDP in one process: what the app's --host and --join do.</summary>
public class NetPlayTests
{
    static readonly string Content = DataFile.FindContentRoot();

    [Fact]
    public void ANightAloneComesWithABotCrewThatDrives()
    {
        // T89: someone playing alone to see what the fuss is about gets a crew. Three bots, each a client over localhost:
        // one has the cab (first aboard), and the train gets going with nobody human at the controls.
        using var night = NetPlaySession.HostGame(Content, new SessionSetup(Route: "frontier:7", Cars: 4, Enemies: true), port: null, bots: 3);
        var crew = Assert.IsType<Sim.Bots.BotCrew>(night.BotCrew);
        Assert.Equal(3, crew.Bots.Count);
        Assert.All(crew.Bots, b => Assert.NotNull(b.Session.PlayerId));
        Assert.IsType<Sim.Bots.ConductorBot>(crew.Bots[0].Bot);
        double start = night.Host!.Train.Dynamics.Distance;
        for (int t = 0; t < SimConstants.TickRate * 40; t++)
        {
            night.Step(default);
            Thread.Sleep(1);
        }
        var driver = night.Host.Players.First(p => p.Id == crew.Bots[0].Session.PlayerId).State;
        Assert.True(PlayerMotor.InCab(driver, night.Host.Train), $"the driver bot is at {driver.Parent}");
        Assert.False(PlayerMotor.InCab(night.Player, night.Train) && night.Player.Parent == 0 && driver.Parent != 0);
        Assert.True(night.Host.Train.Dynamics.Distance > start + 20, $"the train went {night.Host.Train.Dynamics.Distance - start:0} m");
        // The human sees all three.
        Assert.Equal(3, night.Crew(night.InterpolatedFrames(1), 1).Count);
    }

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
        // Spec E: the joiner sees its ping to the host; the host has none to show.
        Assert.NotNull(joiner.Link?.PingMs);
        Assert.Null(host.Link?.PingMs);
        Assert.Equal(2, joiner.Link?.Aboard);
    }

    [Fact]
    public void JoiningNobodyFailsCleanly()
    {
        int port;
        using (var probe = new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0)))
            port = ((System.Net.IPEndPoint)probe.Client.LocalEndPoint!).Port;
        Assert.Throws<IOException>(() => NetPlaySession.Join(Content, new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, port),
            options: new Ballast.Net.DatagramOptions { ConnectSeconds = 0.5 }));
    }

    [Fact]
    public void AJoinerWithDifferentTuningIsToldWhichFile()
    {
        // Spec E: everyone plays the host's game. A joiner who'd edited train.json would predict a different train.
        string mine = Path.Combine(Path.GetTempPath(), "dt-content-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var file in Directory.EnumerateFiles(Content, "*", SearchOption.AllDirectories))
            {
                var to = Path.Combine(mine, Path.GetRelativePath(Content, file));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(file, to);
            }
            var train = Path.Combine(mine, "tuning", "train.json");
            File.WriteAllText(train, File.ReadAllText(train).Replace("\"maxSpeed\": 22.0", "\"maxSpeed\": 30.0"));
            // Line endings alone don't count.
            var boiler = Path.Combine(mine, "tuning", "boiler.json");
            File.WriteAllText(boiler, File.ReadAllText(boiler).Replace("\r\n", "\n").Replace("\n", "\r\n"));

            using var host = NetPlaySession.HostGame(Content, new SessionSetup(Cars: 4, Enemies: false), port: 0);
            var e = Assert.Throws<IOException>(() => NetPlaySession.Join(mine, new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, host.Port), () => host.Step(default)));
            Assert.Equal("your content differs from the host's: tuning/train.json", e.Message);
        }
        finally
        {
            Directory.Delete(mine, recursive: true);
        }
    }

    [Fact]
    public void AFriendJoinsThroughTheLobbyAndTheyPlayTogether()
    {
        // What a Steam game does, against the fake platform: host a lobby, invite, the friend joins it, and the
        // game connects them to its owner over relayed P2P. The host takes no UDP port at all.
        var cloud = new FakeOnline();
        var alice = cloud.SignIn("alice");
        var bob = cloud.SignIn("bob");
        using var host = NetPlaySession.HostGame(Content, new SessionSetup(Route: "frontier:7", Cars: 4, Enemies: false), port: null, online: alice);
        Assert.Equal(0, host.Port);
        for (int i = 0; i < 5 && host.Lobby!.Status != Lobby.State.Open; i++)
            host.Step(default);
        Assert.Equal(Lobby.State.Open, host.Lobby!.Status);

        alice.Invite(host.Lobby.Id, bob.Me);
        var events = new List<OnlineEvent>();
        bob.Poll(events);
        var invite = events.Single(e => e.Kind == OnlineEventKind.JoinRequested);
        using var joiner = NetPlaySession.JoinLobby(Content, bob, invite.Lobby, () => host.Step(default));
        Assert.Equal("frontier:7", joiner.Setup.Route);

        for (int t = 0; t < SimConstants.TickRate * 2; t++)
        {
            host.Step(default);
            joiner.Step(default);
            Thread.Sleep(1);
        }
        Assert.True(joiner.Client.Connected);
        Assert.Single(host.Crew(host.InterpolatedFrames(1), 1));
        Assert.Single(joiner.Crew(joiner.InterpolatedFrames(1), 1));
        Assert.Contains("joined alice on Fake", joiner.Status());
        Assert.Contains("ping", joiner.Status());
        Assert.Contains("Fake lobby 2/12", host.Status());
    }

    [Fact]
    public void JoiningALobbyThatsGoneSaysSo()
    {
        var bob = new FakeOnline().SignIn("bob");
        var e = Assert.Throws<IOException>(() => NetPlaySession.JoinLobby(Content, bob, new LobbyId(99)));
        Assert.Equal("couldn't join: that lobby is gone", e.Message);
    }

    [Fact]
    public void EightBotsPlayThroughALobby()
    {
        // dt harness --online: every bot a separate account in the host's lobby, over relayed P2P.
        using var online = new FakeLobbyNetwork();
        var line = Sim.Rail.RailLine.Load(Path.Combine(Content, "lines/test-loop.json"));
        var r = Sim.Net.Harness.Run(line, DataFile.Load<Sim.Train.TrainTuning>(Path.Combine(Content, Sim.Train.TrainTuning.File)),
            DataFile.Load<PlayerTuning>(Path.Combine(Content, PlayerTuning.File)), new Sim.Net.HarnessOptions { Bots = 8, Seconds = 20, Network = online });
        Assert.Equal("fake Steam lobby", r.Link);
        Assert.Equal(1, online.Cloud.Lobbies);
        Assert.Equal(9, online.Members); // the host and eight friends
        Assert.All(r.Clients, c => Assert.True(c.Snapshots > r.Ticks * 0.9, $"player {c.Id} got {c.Snapshots} of {r.Ticks} snapshots"));
        Assert.All(r.Clients, c => Assert.True(c.MaxCorrectionM < 0.01, $"player {c.Id} corrected by {c.MaxCorrectionM} m"));
        Assert.True(r.TrainSpeed > 5, "the conductor should have the train moving");
        Assert.Equal(0, online.Cloud.Refused);
    }

    [Fact]
    public void ADroppedJoinerReconnectsToItsOwnCrewmate()
    {
        // Note 253 over real UDP: the joiner's link goes, it says so, dials the host again on its own, and is back in its
        // slot (by its token, from a new socket: a new address to the host).
        using var host = NetPlaySession.HostGame(Content, new SessionSetup(Route: "frontier:7", Cars: 4, Enemies: false), port: 0);
        using var joiner = NetPlaySession.Join(Content, new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, host.Port), () => host.Step(default));
        void Step(int ticks, bool hosting = true)
        {
            for (int t = 0; t < ticks; t++)
            {
                if (hosting)
                    host.Step(default);
                joiner.Step(default);
                Thread.Sleep(1);
            }
        }
        Step(SimConstants.TickRate);
        byte id = (byte)joiner.PlayerId;
        Assert.True(joiner.Client.Connected);

        host.Host!.Drop(id);
        // The host busy elsewhere for a moment: the joiner's lost, and on its first try.
        Step(10, hosting: false);
        Assert.True(joiner.Lost);
        Assert.True(joiner.Reconnecting);
        Assert.Equal(1, joiner.Link?.Attempt);
        Assert.Contains("RECONNECTING (1/", joiner.Status());
        Assert.True(host.Host.IsReserved(id));

        for (int t = 0; t < SimConstants.TickRate * 5 && (joiner.Lost || !joiner.Client.Connected); t++)
            Step(1);
        Assert.False(joiner.Lost);
        Assert.Equal(id, joiner.PlayerId);
        Assert.Equal(1, host.Host.Rejoins);
        Assert.Equal(2, host.Host.PlayerCount);
        Step(SimConstants.TickRate / 2);
        Assert.Single(host.Crew(host.InterpolatedFrames(1), 1));
        Assert.Contains("ping", joiner.Status());
    }

    [Fact]
    public void ABotRejoinsThroughTheLobby()
    {
        // The same over the Steam path (the fake platform): back as another connection to the lobby's owner, its slot found
        // by the token, not by who or where it is.
        using var online = new FakeLobbyNetwork();
        var line = Sim.Rail.RailLine.Load(Path.Combine(Content, "lines/test-loop.json"));
        var r = Sim.Net.Harness.Run(line, DataFile.Load<Sim.Train.TrainTuning>(Path.Combine(Content, Sim.Train.TrainTuning.File)),
            DataFile.Load<PlayerTuning>(Path.Combine(Content, PlayerTuning.File)),
            new Sim.Net.HarnessOptions { Bots = 4, Seconds = 20, Network = online, DropRejoin = new Sim.Net.DropRejoin(2, 8, 3) });
        var back = Assert.IsType<Sim.Net.RejoinReport>(r.Rejoin);
        Assert.Equal(back.Was, back.Back);
        Assert.Equal(1, back.HostRejoins);
        Assert.True(back.OthersSeeOne);
        Assert.True(back.MaxCorrectionAfterM < 0.01, $"corrected by {back.MaxCorrectionAfterM} m after coming back");
    }

    [Fact]
    public void AJoinerWithDifferentModsIsToldWhichMods()
    {
        // T49: the refusal names the files that differ, and the mods on each side when they don't match.
        var text = SessionSetup.Refusal(["tuning/enemies.json"], ["hard-edges 1.2"], []);
        Assert.Contains("tuning/enemies.json", text);
        Assert.Contains("the host's mods: hard-edges 1.2; yours: none", text);
        Assert.DoesNotContain("mods", SessionSetup.Refusal(["tuning/enemies.json"], [], []));
    }
}
