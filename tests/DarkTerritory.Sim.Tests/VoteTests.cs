using Ballast.Net;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// GDD v1.4 App. D.11 and D.12 (ARCHITECTURE §8 note 180): the dead's creature vote (a ballot from what's eligible, once a
/// run, ×1.2 a vote to ×1.5, inside its want tag, a cue to the dead alone, revealed on the report) and commendations (one
/// each, never yourself, at run end).
/// </summary>
public class VoteTests
{
    static readonly VoteTuning V = Tuning.Enemies.Director.Vote;

    static World Night(ulong seed = 3)
    {
        var route = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, seed);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 5, 1)), route.Build(), 3_000, Tuning.Boiler);
        var world = new World(train, Tuning.Combat);
        world.EnableEnemies(Tuning.Enemies, route, seed, crew: 4, authority: true);
        return world;
    }

    [Fact]
    public void ABallotIsDrawnFromWhatTheDirectorCouldSendAndKeptForTheRun()
    {
        var w = Night();
        var d = w.Director!;
        var ballot = d.Ballot(w, 2);
        Assert.InRange(ballot.Count, 1, V.Options);
        Assert.Equal(ballot.Count, ballot.Distinct().Count());
        Assert.All(ballot, k => Assert.NotNull(Spawns.For(k)));
        Assert.DoesNotContain(EnemyKind.Choir, ballot);
        Assert.DoesNotContain(EnemyKind.Stoker, ballot);
        // The same ballot when asked again (a second death), and the same on any machine with the seed.
        Assert.Equal(ballot, d.Ballot(w, 2));
        Assert.Equal(ballot, Night().Director!.Ballot(Night(), 2));
        // Once a run, locked on submit; only for what was offered.
        Assert.True(d.CanVote(2));
        Assert.False(d.Vote(2, Enum.GetValues<EnemyKind>().First(k => !ballot.Contains(k))));
        Assert.True(d.Vote(2, ballot[0]));
        Assert.False(d.CanVote(2));
        Assert.False(d.Vote(2, ballot[^1]));
        Assert.Equal(ballot[0], d.VoteOf(2));
    }

    [Fact]
    public void EachVoteIsATwelfthMoreToAHalfMoreAndNeverMovesWeightBetweenWants()
    {
        var w = Night();
        var d = w.Director!;
        // Three of the dead vote for whatever's on all their ballots most.
        var kind = d.Ballot(w, 1)[0];
        foreach (int voter in new[] { 1, 2, 3, 4 })
            if (d.Ballot(w, voter).Contains(kind))
                d.Vote(voter, kind);
        int votes = d.VotersFor(kind).Count;
        Assert.Equal(Math.Min(V.Cap, Math.Pow(V.PerVote, votes)), d.VoteWeight(kind), 9);
        Assert.True(d.VoteWeight(kind) <= V.Cap);
        // Inside its want: the want's total is what it was; within it, the voted creature's share goes up.
        var options = Spawns.Rules.Select(r => (r.Kind, Weight: 1.0)).ToList();
        var weighed = d.WeighVotes(options);
        foreach (var want in options.Select(o => Director.WantOf(o.Kind)).Distinct())
            Assert.Equal(options.Where(o => Director.WantOf(o.Kind) == want).Sum(o => o.Weight),
                weighed.Where(o => Director.WantOf(o.Kind) == want).Sum(o => o.Weight), 9);
        if (options.Count(o => Director.WantOf(o.Kind) == Director.WantOf(kind)) > 1)
            Assert.True(weighed.First(o => o.Kind == kind).Weight > 1);
    }

    [Fact]
    public void TheDeadVoteOverTheWireTheDeadAloneHearTheCueAndTheReportRevealsIt()
    {
        var net = new LoopbackNetwork();
        var route = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, 3);
        TrainOnLine Train() => new(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 1)), route.Build(), 3_000, Tuning.Boiler);
        var host = new HostSession(net.CreateHost(), Train(), Tuning.Train, Tuning.Player);
        host.World.EnableEnemies(Tuning.Enemies, route, 3, crew: 2, authority: true);
        var dead = new ClientSession(net.CreateClient(), Train(), Tuning.Train, Tuning.Player) { Name = "Priya" };
        var living = new ClientSession(net.CreateClient(), Train(), Tuning.Train, Tuning.Player) { Name = "Dave" };
        void Run(int ticks, byte select = 0)
        {
            for (int t = 0; t < ticks; t++)
            {
                net.Advance(SimConstants.TickSeconds);
                host.Step();
                dead.Step(new PlayerIntent { Select = select });
                living.Step(default);
            }
        }
        Run(30);
        int me = dead.PlayerId!.Value;
        host.SetPlayerState((byte)me, dead.Predicted with { Health = 0, Death = DeathCause.Mauled });
        Run(10);
        // The ballot reaches the dead player alone.
        Assert.NotNull(dead.Ballot);
        Assert.Null(living.Ballot);
        var choice = dead.Ballot!.Value.Options[0];
        Run(5, select: 1);
        Assert.Equal(choice, host.World.Director!.VoteOf(me));
        Run(5);
        Assert.Equal(choice, dead.Ballot!.Value.Cast);
        // It comes: the dead hear who called it; the living hear nothing.
        host.World.Director.Charge(host.World, choice, host.World.ActiveEnemies);
        Run(5);
        var cue = Assert.Single(dead.VoteCues);
        Assert.Equal(choice, cue.Kind);
        Assert.Equal([me], cue.Voters);
        Assert.Empty(living.VoteCues);
        // The report says what the dead voted for.
        var lines = IncidentLog.Lines(host.World, 350, 263, _ => false);
        Assert.Contains(lines, l => l.Kind == IncidentKind.Voted && l.Text == $"The dead voted for the {IncidentLog.Spoken(choice.ToString())}: Priya.");
    }

    [Fact]
    public void ADeadBotVotesAWhileAfterItsOfferedAndTheSameEveryTime()
    {
        // Note 201: a dead bot is a crewmate with a vote (D.11), cast through the same intent as a player's, after
        // V.BotSeconds by the host's clock, for its id's turn round the ballot. A living bot doesn't.
        (EnemyKind? Vote, double At, EnemyKind[] Ballot, int Me) Night()
        {
            var net = new LoopbackNetwork();
            var route = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, 3);
            TrainOnLine Train() => new(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 1)), route.Build(), 3_000, Tuning.Boiler);
            var host = new HostSession(net.CreateHost(), Train(), Tuning.Train, Tuning.Player);
            host.World.EnableEnemies(Tuning.Enemies, route, 3, crew: 2, authority: true);
            var bot = new ClientSession(net.CreateClient(), Train(), Tuning.Train, Tuning.Player);
            var living = new ClientSession(net.CreateClient(), Train(), Tuning.Train, Tuning.Player);
            var walker = new RoofWalkerBot(7);
            int ticks = 0;
            void Run(int n)
            {
                for (int t = 0; t < n; t++, ticks++)
                {
                    net.Advance(SimConstants.TickSeconds);
                    host.Step();
                    // The whole of a bot's tick (BotCrew.Think), as the harness and a solo crew run it.
                    bot.Step(bot.Connected ? BotCrew.Think(bot, walker, (uint)ticks, null) : default);
                    living.Step(BotCrew.Vote(default, living, living.PlayerId ?? 0));
                }
            }
            Run(30);
            int me = bot.PlayerId!.Value;
            Assert.Equal(0, BotCrew.Vote(default, bot, me).Select); // alive: no vote
            host.SetPlayerState((byte)me, bot.Predicted with { Health = 0, Death = DeathCause.Mauled });
            int died = ticks;
            while (host.World.Director!.VoteOf(me) is null && ticks < died + 30 * SimConstants.TickRate)
                Run(1);
            Run(10);
            Assert.Equal(host.World.Director.VoteOf(me), bot.Ballot!.Value.Cast);
            Assert.Null(host.World.Director.VoteOf(living.PlayerId!.Value));
            Assert.Equal(0, BotCrew.Vote(default, bot, me).Select); // cast: it stops asking
            return (host.World.Director.VoteOf(me), (ticks - 10 - died) * SimConstants.TickSeconds, [.. bot.Ballot!.Value.Options], me);
        }
        var (vote, at, ballot, me) = Night();
        Assert.Equal(ballot[me % ballot.Length], vote);
        // Not at once (it deliberates), and not long after.
        Assert.InRange(at, V.BotSeconds, V.BotSeconds + 1.5);
        // The same night votes the same.
        Assert.Equal(vote, Night().Vote);
    }

    [Fact]
    public void ACommendationIsOneEachNeverYourselfAndOnlyAtRunEnd()
    {
        var w = Night();
        w.EnableRun(Tuning.Run, RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, 3), 600, authority: true);
        int[] session = [0, 1, 2];
        Assert.False(Commendations.Give(w, 0, 1, 0, session)); // the run's still on
        w.Run!.Mirror(RunPhase.Arrived, RunEnd.Delivered, 100, -1, false, [.. Enumerable.Repeat(0.0, w.Run.FacilityCount)]);
        Assert.False(Commendations.Give(w, 0, 0, 0, session)); // yourself
        Assert.False(Commendations.Give(w, 0, 7, 0, session)); // not here
        Assert.False(Commendations.Give(w, 0, 1, 9, session)); // no such award
        Assert.True(Commendations.Give(w, 0, 1, 2, session));
        Assert.False(Commendations.Give(w, 0, 2, 1, session)); // one each
        Assert.True(Commendations.Give(w, 2, 1, 0, session));
        Assert.Equal([(0, 1, (byte)2), (2, 1, (byte)0)], w.Commendations);
    }
}
