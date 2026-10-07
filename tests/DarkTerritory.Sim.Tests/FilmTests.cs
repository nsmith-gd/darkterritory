using Ballast;
using Ballast.Net;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// GDD v1.4 App. E.2-E.5 and E.11 (ARCHITECTURE §8 note 177): the derailment film. The host takes the wreck's start and the
/// crew as they were on the derail tick; every machine shoots the same film from it; everybody gets a shot, the biggest
/// flight last, inside the beat caps; the cause card is the clerk's; a majority (or the host) skips to it.
/// </summary>
public class FilmTests
{
    static readonly WreckTuning W = DataFile.Load<WreckTuning>(Path.Combine(DataFile.FindContentRoot(), WreckTuning.File));
    static readonly PlayerTuning P = Tuning.Player;

    /// <summary>A world with <paramref name="crew"/> aboard, spread down the train (cab, roofs, cars), derailed on a curve.</summary>
    static World Derailed(int crew, double speed = 20, int cars = 6)
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(2000), new TrackSegment(600, 300), new TrackSegment(2000)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, cars, 1)), line, 2250);
        train.Dynamics.Velocity = speed;
        train.RefreshFrames();
        var world = new World(train, Tuning.Combat) { WreckTuning = W };
        world.EnableBodies();
        string[] names = ["Dave", "Priya", "Sam", "Ana", "Kofi", "Mei", "Lars", "Zoe"];
        for (int i = 0; i < crew; i++)
            world.Names[i] = names[i];
        world.BeginTick();
        for (int i = 0; i < crew; i++)
        {
            var s = i == 0 ? PlayerMotor.SpawnInCab(train, P) : PlayerMotor.SpawnOnRoof(train, 1 + i % cars, (i % 3 - 1) * 3.0, P);
            world.CrewAct(ref s, default, i);
        }
        world.Attribution.Drove(0);
        world.Derail("took the 45 km/h bend at 72 km/h, 27 km/h too fast");
        return world;
    }

    [Fact]
    public void TheHostTakesTheCrewAsTheyWereAndTheClerksCauseCard()
    {
        var w = Derailed(4);
        var film = w.Film!;
        Assert.Equal(4, film.Players.Count);
        Assert.Equal(["Dave", "Priya", "Sam", "Ana"], film.Players.Select(p => p.Name));
        Assert.Equal("on the throttle", film.Players[0].Role);
        Assert.StartsWith("on the roof of car", film.Players[1].Role);
        Assert.Equal(w.Train.Frames.Count, film.Cars.Count);
        // Going with the train when it came off: that's the fling.
        Assert.All(film.Players, p => Assert.InRange(p.Velocity.Length, 19, 21));
        Assert.Matches(@"^Consist derailed at km \d+, 72 km/h\. Took the 45 km/h bend at 72 km/h, 27 km/h too fast\. Throttle: Dave\. Recovery not scheduled\.$", film.Cause);
    }

    [Fact]
    public void EveryMachineShootsTheSameFilmFromTheStartItIsSent()
    {
        var w = Derailed(4);
        var here = w.ShootFilm()!;
        // Over the wire: JSON, compressed, in chunks; a client with every chunk has the start exactly.
        var messages = Messages.FilmMessages(w.Film!);
        var chunks = messages.Select((m, i) => (i, m[3..])).ToDictionary(x => x.i, x => x.Item2);
        var start = Messages.ReadFilm(chunks, messages.Count)!;
        var client = new World(w.Train) { WreckTuning = W, Film = start };
        var there = client.ShootFilm()!;
        Assert.Equal(here.Frames.Count, there.Frames.Count);
        for (int f = 0; f < here.Frames.Count; f++)
        {
            Assert.Equal(here.Frames[f].Cars, there.Frames[f].Cars);
            for (int d = 0; d < here.Frames[f].Ragdolls.Count; d++)
                Assert.Equal(here.Frames[f].Ragdolls[d], there.Frames[f].Ragdolls[d]);
        }
        Assert.Equal(here.Shots, there.Shots);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    public void EverybodyGetsAShotTheBiggestLastInsideTheCaps(int crew)
    {
        var film = Derailed(crew).ShootFilm()!;
        var t = W.Film;
        var players = film.Shots.Where(s => s.Kind == ShotKind.Player).ToList();
        Assert.Equal(film.Start.Players.Select(p => p.Id).Order(), players.Select(s => s.Subject).Order());
        // E.5: ascending order of peak score, so the biggest flight comes last.
        var scores = players.Select(s => film.Peaks[s.Subject].Score).ToList();
        Assert.Equal(scores.Order(), scores);
        Assert.True(players.Sum(s => s.Real) <= t.ShotCap + 1e-9);
        Assert.All(players, s => Assert.InRange(s.Real, t.ShotMin, t.ShotSeconds));
        Assert.All(players, s => Assert.Matches(@"^[A-Z]+ - [A-Z0-9 ]+$", s.Card));
        Assert.Equal(ShotKind.Cause, film.Shots[^1].Kind);
        Assert.Equal(film.Start.Cause, film.Shots[^1].Card);
        // Every frame finite, every body still on (or over) the ground.
        Assert.All(film.Frames, f => Assert.All(f.Ragdolls, r => Assert.All(r, j => Assert.True(double.IsFinite(j.X + j.Y + j.Z)))));
    }

    [Fact]
    public void ASlowTipOverStillThrowsSomebody()
    {
        // E.3: below minKickSpeed the kick's given in full regardless.
        var film = Derailed(2, speed: 3).ShootFilm()!;
        Assert.Contains(film.Peaks.Values, p => p.Score > 0.8);
    }

    [Fact]
    public void AMajorityOrTheHostSkipsToTheCauseCard()
    {
        var net = new LoopbackNetwork();
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(5_000)]));
        TrainOnLine Train() => new(new TrainDynamics(Consist.Uniform(Tuning.Train, 3, 1)), line, 600);
        var host = new HostSession(net.CreateHost(), Train(), Tuning.Train, P);
        host.World.WreckTuning = W;
        host.World.EnableBodies(); // a real host simulates the bodies (and so knows its crew's states)
        var clients = Enumerable.Range(0, 3).Select(_ => new ClientSession(net.CreateClient(), Train(), Tuning.Train, P)).ToArray();
        var skip = new PlayerIntent { Actions = PlayerActions.Skip };
        void Run(int ticks, Func<int, PlayerIntent> intent)
        {
            for (int t = 0; t < ticks; t++)
            {
                net.Advance(SimConstants.TickSeconds);
                host.Step();
                for (int i = 0; i < clients.Length; i++)
                    clients[i].Step(intent(i));
            }
        }
        Run(30, _ => default);
        host.World.Derail("test");
        Run(15, _ => default);
        // The start reaches everyone.
        Assert.All(clients, c => Assert.Equal(host.World.Film!.Players.Count, c.World.Film?.Players.Count));
        Assert.Equal(3, host.World.Film!.Players.Count);
        // One of three: counted, not enough.
        Run(15, i => i == 0 ? skip : default);
        Assert.False(host.World.FilmSkipped);
        Assert.All(clients, c => Assert.Equal((1, 3), c.World.FilmVotes));
        // Two of three: skipped, and every client knows.
        Run(15, i => i < 2 ? skip : default);
        Assert.True(host.World.FilmSkipped);
        Assert.All(clients, c => Assert.True(c.World.FilmSkipped));
    }

    [Fact]
    public void TheHostAloneCanSkip()
    {
        var net = new LoopbackNetwork();
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(5_000)]));
        TrainOnLine Train() => new(new TrainDynamics(Consist.Uniform(Tuning.Train, 3, 1)), line, 600);
        var host = new HostSession(net.CreateHost(), Train(), Tuning.Train, P);
        var clients = Enumerable.Range(0, 3).Select(_ => new ClientSession(net.CreateClient(), Train(), Tuning.Train, P)).ToArray();
        for (int t = 0; t < 30; t++)
        {
            net.Advance(SimConstants.TickSeconds);
            host.Step();
            foreach (var c in clients)
                c.Step(default);
        }
        host.HostPlayer = clients[2].PlayerId!.Value;
        host.World.Derail("test");
        for (int t = 0; t < 15; t++)
        {
            net.Advance(SimConstants.TickSeconds);
            host.Step();
            for (int i = 0; i < clients.Length; i++)
                clients[i].Step(i == 2 ? new PlayerIntent { Actions = PlayerActions.Skip } : default);
        }
        Assert.True(host.World.FilmSkipped);
    }
}
