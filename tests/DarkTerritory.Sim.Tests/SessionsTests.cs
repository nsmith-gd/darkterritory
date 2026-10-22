using System.Text.Json;
using Ballast;
using Ballast.Net;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Sessions (GDD §33.1, §11, §23.4; ARCHITECTURE §8 note 724; the director, 14 Oct 2026, App. F.6: friend groups break on
/// uneven attendance, slow joins and strangers in public games): the crew's save sent to everyone aboard, a friend taking a
/// bot's seat, kick and the public lobby's vote, and the lobby's crew mischief switch.
/// </summary>
public class SessionsTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly RailLine Line = RailLine.Load(Path.Combine(DataFile.FindContentRoot(), "lines/test-loop.json"));

    static TrainOnLine NewTrain() => new(new TrainDynamics(Consist.Uniform(T, 6, 1)), Line, 1500);

    sealed class Night(PlayerTuning? tuning = null)
    {
        public readonly LoopbackNetwork Net = new();
        public readonly PlayerTuning Tuning = tuning ?? P;
        public HostSession Host = null!;
        public readonly List<ClientSession> Clients = [];
        public readonly BotCrew Bots = new(null);

        public static Night With(PlayerTuning? tuning = null)
        {
            var n = new Night(tuning);
            n.Host = new HostSession(n.Net.CreateHost(), NewTrain(), T, n.Tuning);
            n.Host.World.EnableBodies();
            n.Host.BotLeft = n.Bots.Retire;
            return n;
        }

        public ClientSession Human(string name)
        {
            var c = new ClientSession(Net.CreateClient(), NewTrain(), T, Tuning) { Name = name };
            Clients.Add(c);
            return c;
        }

        public ClientSession Bot(string name)
        {
            var c = new ClientSession(Net.CreateClient(), NewTrain(), T, Tuning) { Name = name, Bot = true };
            Bots.Add(c, new StandBot());
            return c;
        }

        public void Run(int ticks, Func<ClientSession, PlayerIntent>? intent = null)
        {
            for (int t = 0; t < ticks; t++)
            {
                Net.Advance(SimConstants.TickSeconds);
                Host.Step();
                foreach (var c in Clients)
                    c.Step(intent?.Invoke(c) ?? default);
                Bots.Step();
            }
        }

        public PlayerState State(byte id) => Host.Players.First(p => p.Id == id).State;
    }

    /// <summary>A bot that stands where it's put, so where its seat is doesn't move under the test.</summary>
    sealed class StandBot : IBot
    {
        public string Name => "stand";
        public PlayerIntent Decide(in PlayerState self, TrainOnLine train, uint tick) => default;
    }

    // ---- The crew's save travels (GDD §11).

    static byte[] SaveBytes(CampaignState s) => JsonSerializer.SerializeToUtf8Bytes(s, DataFile.Options);

    [Fact]
    public void TheCrewsSaveGoesToEveryHumanAboardInChunksAndNotToTheBots()
    {
        var n = Night.With();
        var dave = n.Human("dave");
        var priya = n.Human("priya");
        var bot = n.Bot("Dunmore");
        n.Run(30);
        Assert.Equal(3, n.Host.PlayerCount);
        // A long campaign: its history alone runs past one chunk, even packed.
        var rng = new Random(3);
        var save = new CampaignState
        {
            Slot = 2,
            Name = "Owls",
            Seed = 99,
            Runs = 400,
            LastHost = "Nick",
            SettledAt = 1_760_000_000_000,
            History = [.. Enumerable.Range(1, 400).Select(i => new RunLog(i, $"frontier:{rng.Next()}", RunEnd.Delivered, rng.Next(-900, 4000), rng.Next(0, 3), rng.Next()))],
        };
        var bytes = SaveBytes(save);
        Assert.True(Messages.CrewSaveMessages(bytes).Count > 1, "one chunk");
        n.Host.ShareSave(bytes);
        n.Run(10);
        foreach (var human in new[] { dave, priya })
        {
            Assert.Equal(1, human.CrewSaves);
            var got = human.TakeCrewSave();
            Assert.Equal(bytes, got);
            Assert.Null(human.TakeCrewSave());
            var back = JsonSerializer.Deserialize<CampaignState>(got!, DataFile.Options)!;
            Assert.Equal((save.CrewId, 400, "Nick"), (back.CrewId, back.Runs, back.LastHost));
        }
        Assert.Equal(0, bot.CrewSaves);
        // A second night's settling replaces it, whole.
        n.Host.ShareSave(SaveBytes(save with { Runs = 401 }));
        n.Run(10);
        Assert.Equal(401, JsonSerializer.Deserialize<CampaignState>(dave.TakeCrewSave()!, DataFile.Options)!.Runs);
    }

    [Fact]
    public void OfTwoCopiesTheOneWithMoreNightsWinsAndDawnClosesEveryCopy()
    {
        var a = new CampaignState { Name = "Owls", Seed = 5, Runs = 6, SettledAt = 100, LastHost = "Dave" };
        var b = a with { Runs = 7, SettledAt = 50, LastHost = "Priya" };
        // More nights wins, whichever is held and whichever came in, whatever the clocks said.
        Assert.Equal("Priya", Campaign.Campaign.Prefer(a, b).LastHost);
        Assert.Equal("Priya", Campaign.Campaign.Prefer(b, a).LastHost);
        // As many: the newest settlement; that too, the one held.
        var c = a with { SettledAt = 200, LastHost = "Sam" };
        Assert.Equal("Sam", Campaign.Campaign.Prefer(a, c).LastHost);
        Assert.Equal("Sam", Campaign.Campaign.Prefer(c, a).LastHost);
        Assert.Equal("Dave", Campaign.Campaign.Prefer(a, a with { LastHost = "Copy" }).LastHost);
        // A train taken at dawn (note 632) closes every copy: a fork with more nights is over too.
        var ended = a with { Runs = 7, Over = true };
        var fork = a with { Runs = 9 };
        Assert.True(Campaign.Campaign.Prefer(fork, ended).Over);
        Assert.Equal(9, Campaign.Campaign.Prefer(fork, ended).Runs);
        Assert.True(Campaign.Campaign.Prefer(ended, fork).Over);
        // The crew's id is the same on every copy: its own, or for an older save its seed's.
        Assert.Equal(a.CrewId, b.CrewId);
        Assert.Equal("0000000000000005", a.CrewId);
        Assert.Equal("abc", (a with { Crew = "abc" }).CrewId);
    }

    // ---- A friend takes a bot's seat (GDD §33.1).

    [Fact]
    public void AFriendJoiningMidNightTakesABotsSeatWithItsBodySlotsHealthAndPost()
    {
        var n = Night.With();
        var host = n.Human("nick");
        var first = n.Bot("Dunmore");
        var second = n.Bot("Hale");
        n.Run(30);
        byte botId = second.PlayerId!.Value;
        Assert.True(n.Host.IsBot(botId));
        // The newest bot, out on car 3's roof with the shovel and the crowbar, short of health.
        var kit = Kit.With(Kit.With(0, 0, Tool.Shovel), 1, Tool.Crowbar);
        n.Host.SetPlayerState(botId, PlayerMotor.SpawnOnRoof(n.Host.Train, 3, 1.5, P) with { Health = 61, Kit = kit, HeldSlot = 1 });
        n.Run(5);
        var was = n.State(botId);
        var post = Census.PostOf(was, n.Host.Train, 100);
        // Between stops now: a joiner would wait for one.
        n.Host.CanBoard = () => false;
        var friend = n.Human("priya");
        n.Run(20);
        Assert.Equal(1, n.Host.BotSeatsTaken);
        Assert.Equal(botId, friend.PlayerId);
        Assert.True(friend.Connected);
        Assert.Null(friend.WaitingReason);
        Assert.False(n.Host.IsBot(botId));
        Assert.Equal("priya", n.Host.World.Names[botId]);
        // Where the bot stood, with what it had: its body, slots, health and post.
        var now = n.State(botId);
        Assert.Equal((was.Parent, was.Surface, was.Health, was.Kit, was.HeldSlot), (now.Parent, now.Surface, now.Health, now.Kit, now.HeldSlot));
        Assert.True((now.Position - was.Position).Length < 0.05, $"moved {(now.Position - was.Position).Length:0.000} m");
        Assert.Equal(post, Census.PostOf(now, n.Host.Train, 100));
        Assert.True((friend.Predicted.Position - now.Position).Length < 0.05);
        // The bot's gone: one fewer run by this machine, the crew the same size, the other bot left alone.
        Assert.Single(n.Bots.Bots);
        Assert.Equal(first, n.Bots.Bots[0].Session);
        Assert.Equal(3, n.Host.PlayerCount);
        Assert.True(n.Host.IsBot(first.PlayerId!.Value));
        // Their own inputs drive it from here.
        n.Run(20, c => c == friend ? new PlayerIntent { MoveZ = 1 } : default);
        Assert.True((n.State(botId).Position - now.Position).Length > 0.5);
        Assert.Equal(0, n.Host.Waiting);
    }

    [Fact]
    public void WithNoBotAboardOrTheFlagOffAJoinerWaitsAsBefore()
    {
        foreach (bool flag in new[] { true, false })
        {
            var n = Night.With(P with { Crew = P.Crew with { JoinTakesBotSeat = flag } });
            n.Human("nick");
            if (!flag)
                n.Bot("Dunmore");
            n.Run(30);
            n.Host.CanBoard = () => false;
            var friend = n.Human("priya");
            n.Run(20);
            Assert.Equal(0, n.Host.BotSeatsTaken);
            Assert.NotNull(friend.WaitingReason);
            Assert.Equal(1, n.Host.Waiting);
            Assert.Equal(flag ? 0 : 1, n.Bots.Bots.Count);
        }
    }

    [Fact]
    public void AFullCrewTakesABotsSeatRatherThanTurnAFriendAway()
    {
        var n = Night.With(P with { Crew = P.Crew with { Cap = 3 } });
        n.Human("nick");
        n.Bot("Dunmore");
        n.Bot("Hale");
        n.Run(30);
        Assert.True(n.Host.Full);
        var friend = n.Human("priya");
        n.Run(20);
        Assert.Null(friend.Refused);
        Assert.True(friend.Connected);
        Assert.Equal(1, n.Host.BotSeatsTaken);
        Assert.Single(n.Bots.Bots);
        // A bot never takes another bot's seat: a fourth bot is turned away.
        var extra = n.Bot("Ross");
        n.Run(20);
        Assert.Equal(RefusalReason.CrewFull, extra.Refused?.Reason);
    }

    // ---- Kick and the public lobby's vote (GDD §33.1).

    static (Night N, ClientSession Host, ClientSession A, ClientSession B, ClientSession C) Lobby(bool vote)
    {
        var n = Night.With();
        var h = n.Human("nick");
        var a = n.Human("dave");
        var b = n.Human("priya");
        var c = n.Human("troll");
        n.Run(30);
        n.Host.HostPlayer = h.PlayerId!.Value;
        n.Host.VoteKicks = vote;
        return (n, h, a, b, c);
    }

    [Fact]
    public void APublicLobbysVotePassesByAMajorityAndThePutOffCantComeBack()
    {
        var (n, _, a, b, c) = Lobby(vote: true);
        byte target = c.PlayerId!.Value;
        a.Kick(target);
        n.Run(5);
        // The host, dave and priya are the vote (the troll isn't): two of three carry it.
        Assert.Equal(new KickVote(target, 1, 2, n.Host.Vote.Until), n.Host.Vote);
        Assert.All(new[] { a, b, c }, x => Assert.Equal(n.Host.Vote, x.KickVote));
        Assert.Contains(n.Host.Players, p => p.Id == target);
        // Saying it twice is one yes.
        a.Kick(target);
        n.Run(5);
        Assert.Equal(1, n.Host.Vote.Yes);
        b.Kick(target);
        n.Run(5);
        Assert.Equal(1, n.Host.Kicks);
        Assert.DoesNotContain(n.Host.Players, p => p.Id == target);
        Assert.True(c.Kicked);
        Assert.False(n.Host.Vote.Open);
        Assert.False(a.KickVote.Open);
        Assert.Equal("THE CREW PUT YOU OFF AT THE NEXT STOP", Messages.PutOff);
        // Hung up on once the word's had time to get there; their place isn't held for them.
        n.Run((int)(P.Crew.RefuseLingerSeconds * SimConstants.TickRate) + 10);
        Assert.False(n.Host.IsReserved(target));
        // Coming back on a new link with their old token, or as a new joiner by the same name: turned away.
        c.Reconnect(n.Net.CreateClient());
        var again = n.Human("TROLL");
        n.Run(20);
        Assert.Equal(RefusalReason.Kicked, c.Refused?.Reason);
        Assert.Equal(RefusalReason.Kicked, again.Refused?.Reason);
        Assert.Equal(3, n.Host.PlayerCount);
        Assert.Equal(Messages.PutOff, again.Refused.ToString());
    }

    [Fact]
    public void AVoteNobodyBacksRunsOutAndTheHostAndOnePlayerCantBeVotedOnAlone()
    {
        var (n, h, a, b, c) = Lobby(vote: true);
        byte target = c.PlayerId!.Value;
        a.Kick(target);
        n.Run(5);
        Assert.True(n.Host.Vote.Open);
        // One vote at a time: priya's on dave waits.
        b.Kick(a.PlayerId!.Value);
        n.Run(5);
        Assert.Equal(target, n.Host.Vote.Target);
        n.Run((int)(P.Crew.Kick.VoteSeconds * SimConstants.TickRate) + 5);
        Assert.False(n.Host.Vote.Open);
        Assert.Equal(0, n.Host.Kicks);
        Assert.Contains(n.Host.Players, p => p.Id == target);
        // Nobody votes on the host.
        a.Kick(h.PlayerId!.Value);
        n.Run(5);
        Assert.False(n.Host.Vote.Open);
    }

    [Fact]
    public void InAPrivateRunOnlyTheHostPutsSomeoneOff()
    {
        var (n, h, a, _, c) = Lobby(vote: false);
        byte target = c.PlayerId!.Value;
        a.Kick(target);
        n.Run(5);
        Assert.False(n.Host.Vote.Open);
        Assert.Contains(n.Host.Players, p => p.Id == target);
        // The host's own player, outright.
        h.Kick(target);
        n.Run(5);
        Assert.Equal(1, n.Host.Kicks);
        Assert.True(c.Kicked);
        Assert.DoesNotContain(n.Host.Players, p => p.Id == target);
        // The host can't put themselves off.
        Assert.False(n.Host.Kick(h.PlayerId!.Value));
    }

    // ---- The crew mischief switch (GDD §23.4).

    static readonly PlayerIntent Shove = new() { Moves = PlayerMoves.Shove };

    [Fact]
    public void WithCrewMischiefOffAShoveLandsOnNobodyAndTheSwitchReachesEveryMachine()
    {
        var n = Night.With();
        var a = n.Human("dave");
        var b = n.Human("priya");
        n.Run(30);
        Assert.True(a.World.CrewMischief);
        n.Host.Mischief = false;
        n.Run(3);
        Assert.False(n.Host.World.MischiefOn);
        Assert.All(new[] { a, b }, c => Assert.False(c.World.MischiefOn));
        byte ia = a.PlayerId!.Value, ib = b.PlayerId!.Value;
        n.Host.SetPlayerState(ia, PlayerMotor.SpawnOnRoof(n.Host.Train, 2, 0, P) with { Yaw = 0 });
        n.Host.SetPlayerState(ib, PlayerMotor.SpawnOnRoof(n.Host.Train, 2, -0.7, P));
        n.Run(10);
        n.Run(1, c => c == a ? Shove : default);
        n.Run(5);
        Assert.Equal(0, n.State(ib).Stagger);
        Assert.Equal(Surface.Roof, n.State(ib).Surface);
        Assert.DoesNotContain(n.Host.World.Horseplays, e => e.Target == ib);
        // A joiner's told the switch as it stands, not as the night's setup had it.
        var late = n.Human("sam");
        n.Run(20);
        Assert.False(late.World.CrewMischief);
        // On again in the yard: the same shove lands.
        n.Host.Mischief = true;
        n.Run(60);
        n.Host.SetPlayerState(ia, PlayerMotor.SpawnOnRoof(n.Host.Train, 2, 0, P) with { Yaw = 0 });
        n.Host.SetPlayerState(ib, PlayerMotor.SpawnOnRoof(n.Host.Train, 2, -0.7, P));
        n.Run(10);
        n.Run(1, c => c == a ? Shove : default);
        n.Run(2);
        Assert.True(a.World.CrewMischief);
        Assert.Contains(n.Host.World.Horseplays, e => e.Target == ib && e.Kind == KnockKind.Shove);
    }

    [Fact]
    public void TheWireCarriesTheSessionsMessages()
    {
        var w = new NetWriter();
        var vote = new KickVote(4, 2, 3, 12345);
        Messages.WriteKickVote(w, vote);
        var r = new NetReader(w.Written);
        Assert.Equal((byte)MessageType.KickVote, r.U8());
        Assert.Equal(vote, Messages.ReadKickVote(ref r));
        Messages.WriteKick(w, 7);
        Assert.Equal([(byte)MessageType.Kick, 7], w.Written.ToArray());
        Messages.WriteMischief(w, false);
        Assert.Equal([(byte)MessageType.Mischief, 0], w.Written.ToArray());
        Messages.WriteRefused(w, new Refusal(RefusalReason.Kicked, 3, 8));
        r = new NetReader(w.Written);
        r.U8();
        Assert.Equal(RefusalReason.Kicked, Messages.ReadRefused(ref r).Reason);
        Assert.Equal(87, Protocol.Version);
    }
}
