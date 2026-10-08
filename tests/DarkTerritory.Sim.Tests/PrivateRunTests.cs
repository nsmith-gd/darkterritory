using Ballast;
using Ballast.Net;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// A private run's password (ARCHITECTURE §8 note 437; the director, 8 Oct 2026: "Private matches should be password
/// gated"): the host lets a new joiner in only with the run's password in its Hello, or a friend it trusts, and turns
/// anyone else away WRONG PASSWORD before they take a place; a held place's token still gets back in.
/// </summary>
public class PrivateRunTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly RailLine Line = RailLine.Load(Path.Combine(DataFile.FindContentRoot(), "lines/test-loop.json"));

    static TrainOnLine NewTrain() => new(new TrainDynamics(Consist.Uniform(T, 6, 1)), Line, 1500);

    sealed class Night
    {
        public readonly LoopbackNetwork Net = new();
        public readonly HostSession Host;
        public readonly List<ClientSession> Clients = [];
        public readonly List<ITransport> Links = [];

        public Night(string? password, PlayerTuning? tuning = null)
        {
            Host = new HostSession(Net.CreateHost(), NewTrain(), T, tuning ?? P) { PasswordKey = Messages.PasswordKey(password) };
            Host.World.EnableBodies();
        }

        public ClientSession Add(string? password)
        {
            var link = Net.CreateClient();
            Links.Add(link);
            var client = new ClientSession(link, NewTrain(), T, P) { Name = $"crew{Clients.Count}", PasswordKey = Messages.PasswordKey(password) };
            Clients.Add(client);
            return client;
        }

        public void Run(int ticks)
        {
            for (int t = 0; t < ticks; t++)
            {
                Net.Advance(SimConstants.TickSeconds);
                Host.Step();
                foreach (var c in Clients)
                    c.Step(default);
            }
        }
    }

    [Fact]
    public void TheKeyIsTheHashedPasswordWhateverItsCaseOrSpaces()
    {
        var key = Messages.PasswordKey("Lantern");
        Assert.NotNull(key);
        Assert.Equal(Messages.KeyLength, key.Length);
        Assert.Equal(key, Messages.PasswordKey("  LANTERN "));
        Assert.NotEqual(key, Messages.PasswordKey("lanterns"));
        // The typed words never go on the wire.
        Assert.DoesNotContain("LANTERN", System.Text.Encoding.Latin1.GetString(key));
        Assert.Null(Messages.PasswordKey("   "));
        Assert.Equal("WRONG PASSWORD", new Refusal(RefusalReason.Password, 1, 8).ToString());
    }

    [Fact]
    public void OnlyTheRightPasswordGetsIntoAPrivateRun()
    {
        var night = new Night("lantern");
        var host = night.Add("lantern");
        var none = night.Add(null);
        var wrong = night.Add("candle");
        var shouting = night.Add("LANTERN");
        night.Run(30);
        Assert.True(host.Connected);
        Assert.True(shouting.Connected);
        Assert.Equal(RefusalReason.Password, none.Refused?.Reason);
        Assert.Equal("WRONG PASSWORD", none.Refused?.ToString());
        Assert.Equal("WRONG PASSWORD", wrong.Refused?.ToString());
        Assert.Null(none.PlayerId);
        Assert.Null(wrong.PlayerId);
        // Turned away before they took a place: the crew is the two who knew it, and a full crew isn't what refused them.
        Assert.Equal(2, night.Host.PlayerCount);
        Assert.Equal(2, night.Host.Occupied);
        Assert.Equal(2, night.Host.WrongPasswords);
        Assert.Equal(0, night.Host.Refusals);
    }

    [Fact]
    public void AnOpenRunIgnoresAPassword()
    {
        var night = new Night(null);
        var a = night.Add(null);
        var b = night.Add("anything");
        night.Run(20);
        Assert.True(a.Connected);
        Assert.True(b.Connected);
        Assert.Equal(0, night.Host.WrongPasswords);
    }

    [Fact]
    public void AFriendTheHostTrustsGetsInWithoutIt()
    {
        var night = new Night("lantern");
        night.Add("lantern");
        var friend = night.Add(null);
        var stranger = night.Add(null);
        // The second link is the host's friend (on Steam, a friend of the host's account; here, by its link: on the
        // loopback a client's own id is the host's id for it).
        var friendLink = night.Links[1].LocalId;
        night.Host.Trusted = peer => peer == friendLink;
        night.Run(30);
        Assert.True(friend.Connected);
        Assert.Equal("WRONG PASSWORD", stranger.Refused?.ToString());
        Assert.Equal(1, night.Host.WrongPasswords);
    }

    [Fact]
    public void ACrewmateWhoDroppedComesBackOnTheirToken()
    {
        // Let in while the run was open, then the host locked it: their held place is still theirs (note 253).
        var night = new Night(null);
        night.Add(null);
        var back = night.Add(null);
        night.Run(20);
        byte id = back.PlayerId!.Value;
        night.Host.PasswordKey = Messages.PasswordKey("lantern");
        night.Links[1].Dispose();
        night.Run(10);
        Assert.True(night.Host.IsReserved(id));
        night.Links[1] = night.Net.CreateClient();
        back.Reconnect(night.Links[1]);
        night.Run(30);
        Assert.True(back.Connected);
        Assert.Equal(id, back.PlayerId);
        Assert.Equal(0, night.Host.WrongPasswords);
    }
}
