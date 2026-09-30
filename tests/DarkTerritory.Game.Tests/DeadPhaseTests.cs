using Ballast;
using Ballast.Render;
using DarkTerritory.Game;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// GDD App. D.10 on a real session: a waiting player sees through a living crewmate's eyes and hears from where they are,
/// the one they watch isn't drawn in front of them, and the HUD shows the dead phase. And D.12's commendations reaching the
/// profile once each.
/// </summary>
public class DeadPhaseTests
{
    static readonly string Content = DataFile.FindContentRoot();

    static void Run(NetPlaySession host, NetPlaySession joiner, double seconds)
    {
        for (int t = 0; t < seconds * SimConstants.TickRate; t++)
        {
            host.Step(default);
            joiner.Step(default);
            Thread.Sleep(1);
        }
    }

    [Fact]
    public void ALobbiedJoinerWatchesThroughTheHostsEyesAndHearsFromThere()
    {
        // Past the gates: the host's own player boards (nobody else is aboard), the joiner is lobbied (D.3).
        using var host = NetPlaySession.HostGame(Content, new SessionSetup(Route: "frontier:7", Cars: 4, Enemies: false) { Start = 3000 }, port: 0);
        using var joiner = NetPlaySession.Join(Content, new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, host.Port), () => host.Step(default));
        Run(host, joiner, 1.5);
        Assert.True(host.Player.Alive);
        Assert.True(joiner.Player.Has(PlayerFlags.Lobbied));
        Assert.True(DeadPhaseControls.Waiting(joiner));
        // Locked to the only living crewmate.
        Assert.Equal(host.PlayerId, joiner.Following);
        var frames = joiner.InterpolatedFrames(1);
        var eye = joiner.EyeCamera(frames, 1, pendingYaw: 1.0, pendingPitch: 0.5);
        var theirs = host.EyeCamera(host.InterpolatedFrames(1), 1, 0, 0);
        Assert.InRange((eye.Position - theirs.Position).Length, 0, 0.5);
        // The mouse doesn't turn it: no free camera.
        Assert.Equal(joiner.EyeCamera(frames, 1, 0, 0).Yaw, eye.Yaw, 9);
        // Their ears: where the host is, not where the lobbied record stands.
        Assert.True(joiner.Viewpoint.Alive);
        Assert.InRange((PlayerMotor.WorldPosition(joiner.Viewpoint, joiner.Train) - PlayerMotor.WorldPosition(host.Player, host.Train)).Length, 0, 0.5);
        // And the one being watched isn't drawn over the eyes.
        Assert.DoesNotContain(joiner.Crew(frames, 1), c => c.Id == host.PlayerId);
        // The host doesn't draw the lobbied player (they aren't anywhere).
        Assert.Empty(host.Crew(host.InterpolatedFrames(1), 1));
        // The dead phase's panel draws.
        var o = new Overlay();
        var dead = new DeadPhaseControls();
        Hud.Build(o, 480, 270, joiner, dead: dead);
        int withPanel = o.Count;
        Hud.Build(o, 480, 270, host, dead: dead);
        Assert.True(withPanel > o.Count);
        // Cycling with one living crewmate stays on them; a lobbied player has no vote (D.11).
        dead.Cycle(joiner, 1);
        Run(host, joiner, 0.3);
        Assert.Equal(host.PlayerId, joiner.Following);
        Assert.False(DeadPhaseControls.CanVote(joiner));
        Assert.Contains(host.Host!.Requests, q => q.Request.Kind == RequestKind.Follow && q.Allowed);
    }

    [Fact]
    public void TheProfileTakesEachCommendationOnce()
    {
        var profile = new PlayerProfile { Id = "me" };
        var dead = new DeadPhaseControls();
        var s = new FakeEnd(2);
        s.Report = new Sim.Run.IncidentReport(new Sim.Run.RunReport(Sim.Run.RunEnd.Delivered, 1, 1, 1, 0, 1, 1, 0, 0, 0, 1, 2, 0))
        {
            Session = [1, 2],
            Commendations = [new(1, 2, "Kept the Fire")],
        };
        foreach (var award in dead.TakeCommendations(s))
            profile = profile.Commended(award);
        // The next revision carries it again, and one more.
        s.Report = s.Report with { Commendations = [new(1, 2, "Kept the Fire"), new(3, 2, "Came Back For Me")] };
        foreach (var award in dead.TakeCommendations(s))
            profile = profile.Commended(award);
        Assert.Empty(dead.TakeCommendations(s));
        Assert.Equal(1, profile.Commendations["Kept the Fire"]);
        Assert.Equal(1, profile.Commendations["Came Back For Me"]);
        // Never to themselves: the choice is only ever the others.
        Assert.Equal([1], DeadPhaseControls.Commendable(s));
    }

    sealed class FakeEnd(int id) : IPlaySession
    {
        readonly PrototypeSession _inner = new(Content, "test-loop", 4);
        public Sim.Run.IncidentReport? Report { get; set; }
        public int PlayerId => id;
        public Sim.Train.TrainOnLine Train => _inner.Train;
        public World World => _inner.World;
        public Sim.Route.Route? Route => _inner.Route;
        public PlayerState Player => _inner.Player;
        public Sim.Train.TrainControls Controls => _inner.Controls;
        public long Tick => _inner.Tick;
        public PlayerTuning PlayerTuning => _inner.PlayerTuning;
        public string Status() => "";
        public void Step(in PlayerIntent intent) { }
        public IReadOnlyList<Sim.Train.CarFrame> InterpolatedFrames(double alpha) => _inner.InterpolatedFrames(alpha);
        public Camera EyeCamera(IReadOnlyList<Sim.Train.CarFrame> frames, double alpha, double pendingYaw, double pendingPitch) => _inner.EyeCamera(frames, alpha, pendingYaw, pendingPitch);
        public IReadOnlyList<Crewmate> Crew(IReadOnlyList<Sim.Train.CarFrame> frames, double alpha) => [];
    }
}
