using Ballast;
using Ballast.Net;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// A private run's password (ARCHITECTURE §8 note 450; the director, 8 Oct 2026: "Private matches should be password
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
        // The refusals are held back a second (note 535); the right ones aren't.
        night.Run(10);
        Assert.True(host.Connected);
        Assert.True(shouting.Connected);
        Assert.Null(none.Refused);
        night.Run(60);
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
        night.Run(60);
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

    /// <summary>Ticks from now till <paramref name="who"/> is refused, or -1 if it isn't within <paramref name="most"/>.</summary>
    static int TicksTillRefused(Night night, ClientSession who, int most = 60 * SimConstants.TickRate)
    {
        for (int t = 0; t < most; t++)
        {
            if (who.Refused is not null)
                return t;
            night.Run(1);
        }
        return -1;
    }

    [Fact]
    public void EachWrongGuessFromOnePlaceIsRefusedLaterThanTheLast()
    {
        // Note 535: one machine guessing, a new connection each time, as a joiner's game does. The refusals come 1, 2, 4 s
        // after each guess (crew.guessDelaySeconds, doubling), up to the cap.
        var night = new Night("lantern");
        night.Host.Origin = _ => "10.0.0.9";
        var waits = new List<double>();
        foreach (var guess in new[] { "candle", "lamp", "torch", "wick", "flame", "spark", "ember" })
        {
            int ticks = TicksTillRefused(night, night.Add(guess));
            Assert.True(ticks > 0, $"{guess} was never refused");
            waits.Add(ticks / (double)SimConstants.TickRate);
        }
        var crew = P.Crew;
        for (int i = 0; i < waits.Count; i++)
        {
            double held = Math.Min(crew.GuessDelaySeconds * Math.Pow(crew.GuessGrowth, i), crew.GuessDelayMaxSeconds);
            // Held its delay, and answered within a few ticks of it (the Hello's trip, the refusal's).
            Assert.InRange(waits[i], held, held + 0.3);
        }
        Assert.Equal(7, night.Host.WrongPasswords);
        Assert.Equal(0, night.Host.Occupied);
    }

    [Fact]
    public void GuessesFromOnePlaceAreJudgedOneAtATimeAndTheRightOneWaitsItsTurn()
    {
        // Note 535: a guesser opening many connections at once gains nothing. Each is judged after the last one's refusal
        // goes, so the right password behind three wrong ones is let in only after their 1 + 2 + 4 s. Another machine with
        // the password isn't held up by them.
        var night = new Night("lantern");
        var lanes = new Dictionary<ulong, string>();
        night.Host.Origin = peer => lanes.GetValueOrDefault(peer.Value);
        ClientSession From(string where, string password)
        {
            var c = night.Add(password);
            lanes[night.Links[^1].LocalId.Value] = where;
            return c;
        }
        var wrong = new[] { From("10.0.0.9", "candle"), From("10.0.0.9", "lamp"), From("10.0.0.9", "torch") };
        var right = From("10.0.0.9", "lantern");
        var elsewhere = From("10.0.0.7", "lantern");
        night.Run(10);
        Assert.True(elsewhere.Connected);
        Assert.False(right.Connected);
        Assert.Equal(1, night.Host.GuessesHeld);
        night.Run(5 * SimConstants.TickRate);
        Assert.All(wrong, w => Assert.Null(w.PlayerId));
        Assert.Equal(2, wrong.Count(w => w.Refused is not null));
        Assert.False(right.Connected);
        night.Run(3 * SimConstants.TickRate);
        Assert.All(wrong, w => Assert.Equal("WRONG PASSWORD", w.Refused?.ToString()));
        Assert.True(right.Connected);
        Assert.Equal(2, night.Host.Occupied);
    }

    [Fact]
    public void AFriendAndAHeldPlaceAreNeverHeldUpByAGuesser()
    {
        var night = new Night(null);
        night.Host.Origin = _ => "10.0.0.9";
        night.Add(null);
        var back = night.Add(null);
        night.Run(20);
        byte id = back.PlayerId!.Value;
        night.Host.PasswordKey = Messages.PasswordKey("lantern");
        // Someone on the same address guesses, and is held.
        var guesser = night.Add("candle");
        night.Run(10);
        Assert.Equal(1, night.Host.GuessesHeld);
        // A crewmate whose link dropped is back on their token at once, and a friend the host trusts walks in.
        night.Links[1].Dispose();
        night.Run(5);
        night.Links[1] = night.Net.CreateClient();
        back.Reconnect(night.Links[1]);
        var friend = night.Add(null);
        var friendLink = night.Links[^1].LocalId;
        night.Host.Trusted = peer => peer == friendLink;
        night.Run(10);
        Assert.True(back.Connected);
        Assert.Equal(id, back.PlayerId);
        Assert.True(friend.Connected);
        Assert.Null(guesser.Refused);
        Assert.True(TicksTillRefused(night, guesser) > 0);
    }

    [Fact]
    public void AGuesserWhoHangsUpStillWaitsAndAQuietPlaceStartsAgain()
    {
        var tuning = P with { Crew = P.Crew with { GuessForgetSeconds = 20 } };
        var night = new Night("lantern", tuning);
        night.Host.Origin = _ => "10.0.0.9";
        Assert.True(TicksTillRefused(night, night.Add("candle")) > 0);
        // The second guess hangs up before its refusal comes: the next is still judged only after its 2 s.
        night.Add("lamp");
        night.Run(10);
        Assert.Equal(1, night.Host.GuessesHeld);
        night.Links[^1].Dispose();
        night.Run(5);
        Assert.Equal(0, night.Host.GuessesHeld);
        var right = night.Add("lantern");
        night.Run(SimConstants.TickRate);
        Assert.False(right.Connected);
        night.Run(2 * SimConstants.TickRate);
        Assert.True(right.Connected);
        // Quiet past guessForgetSeconds, a mistype costs the first second again, not the fourth guess's 8.
        night.Run(21 * SimConstants.TickRate);
        int ticks = TicksTillRefused(night, night.Add("candle"));
        Assert.InRange(ticks / (double)SimConstants.TickRate, 1, 1.3);
    }
}
