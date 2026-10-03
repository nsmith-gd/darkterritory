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
    public void WhenTheNightEndsTheJoinerAndTheHostsOwnPlayerHaveTheHostsReport()
    {
        // GDD App. D.12: the report is written by the host's world, but everyone's HUD, run-end sounds and (on the host's
        // machine) the campaign read it off the world their own client mirrors: the host plays through one too.
        using var host = NetPlaySession.HostGame(Content, new SessionSetup(Route: "frontier:7", Cars: 4, Enemies: false), port: 0);
        using var joiner = NetPlaySession.Join(Content, new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, host.Port), () => host.Step(default));
        var train = host.Host!.Train;
        void Step(int ticks, double? speed = null)
        {
            for (int t = 0; t < ticks; t++)
            {
                if (speed is { } v)
                    train.Dynamics.Velocity = v;
                host.Step(default);
                joiner.Step(default);
                Thread.Sleep(1);
            }
        }
        Step(SimConstants.TickRate);
        // The joiner struck on a roof; out through the gate (the night's begun); then off the rails, which takes the host's player.
        host.Host.SetPlayerState((byte)joiner.PlayerId, PlayerMotor.SpawnOnRoof(train, 2, 0, host.PlayerTuning) with { Health = 0, Death = DeathCause.Struck });
        Step(3 * SimConstants.TickRate, speed: 6);
        Assert.Null(joiner.World.Run!.Report);
        host.Host.World.Derail();
        for (int t = 0; t < 5 * SimConstants.TickRate && (joiner.World.Run.Report is null || host.World.Run!.Report is null); t++)
            Step(1);
        var report = host.Host.World.Run!.Report!;
        Assert.Equal(report, joiner.World.Run.Report);
        Assert.Equal(report, host.World.Run!.Report);
        Assert.Equal([(joiner.PlayerId, DeathCause.Struck), (host.PlayerId, DeathCause.Derailed)], report.Fatalities.Select(d => (d.Player, d.Cause)));
        Assert.Equal($"YOU: STRUCK BY A TUNNEL'S MOUTH. ON CAR 2'S ROOF, KM {report.Fatalities[0].Km:0.0}", Hud.DeathLines(report, joiner.PlayerId)[0]);
        Assert.StartsWith($"CREW {joiner.PlayerId}: STRUCK", Hud.DeathLines(report, host.PlayerId)[0]);
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
    public void AJoinerWithDifferentModsIsToldWhichMods()
    {
        // T49: the refusal names the files that differ, and the mods on each side when they don't match.
        var text = SessionSetup.Refusal(["tuning/enemies.json"], ["hard-edges 1.2"], []);
        Assert.Contains("tuning/enemies.json", text);
        Assert.Contains("the host's mods: hard-edges 1.2; yours: none", text);
        Assert.DoesNotContain("mods", SessionSetup.Refusal(["tuning/enemies.json"], [], []));
    }
}
