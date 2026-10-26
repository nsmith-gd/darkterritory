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
    /// <param name="apart">Nobody stood where another is (past six on the roofs, the next round is 1.5 m further back): a
    /// crew of seven or more otherwise has two on one spot of car 2's roof, whom the film's pile pushes apart (note 1032).</param>
    static World Derailed(int crew, double speed = 20, int cars = 6, bool apart = false)
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
            var s = i == 0 ? PlayerMotor.SpawnInCab(train, P) : PlayerMotor.SpawnOnRoof(train, 1 + i % cars, (i % 3 - 1) * 3.0 + (apart ? i / cars * 1.5 : 0), P);
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

    /// <summary>
    /// A crew spread as <see cref="Derailed"/> has them, apart, come off on a generated night's line at <paramref name="at"/>
    /// (its walls and its tunnels' rock standing, as the game's run has them; note 1055).
    /// </summary>
    static World OnTheLine(string spec, Func<Route.Route, double> at, int crew, double speed, int cars = 6)
    {
        var route = LineGen.Routes.Generate(DataFile.FindContentRoot(), spec, cars);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, cars, 1)), route.Build(), at(route));
        train.Dynamics.Velocity = speed;
        train.RefreshFrames();
        var world = new World(train, Tuning.Combat) { WreckTuning = W };
        world.EnableBodies();
        world.EnableRun(Tuning.Run, route, route.GateOr(Tuning.Route.YardLength), authority: true);
        world.BeginTick();
        for (int i = 0; i < crew; i++)
        {
            var s = i == 0 ? PlayerMotor.SpawnInCab(train, P) : PlayerMotor.SpawnOnRoof(train, 1 + i % cars, (i % 3 - 1) * 3.0 + i / cars * 1.5, P);
            world.CrewAct(ref s, default, i);
        }
        world.Attribution.Drove(0);
        world.Derail("took the bend in the tunnel too fast");
        return world;
    }

    static double InTheTunnel(Route.Route r) => r.Features.First(f => f.Kind == Route.FeatureKind.Tunnel) is var t ? t.Start + Math.Min(60, (t.End - t.Start) / 2) : 0;

    /// <summary>
    /// GDD E.3 "What collides: ... tunnel bores, ... static props" (note 1055): a derail in deadLines:5's tunnel. The crew
    /// and the cars meet the rock: no particle ends a substep more than 2 cm in anything, and no car's corner more than a
    /// substep's correction in the rock. Recorded without the world's solids, the cars ran on into the hill and the crew
    /// with them.
    /// </summary>
    [Fact]
    public void InATunnelTheRockIsSolid()
    {
        var w = OnTheLine("deadLines:5", InTheTunnel, crew: 4, speed: 20);
        var met = new FilmProbe();
        WreckFilm.Record(W, w.Film!, Ground(w), probe: met, world: w.FilmWorld());
        Assert.True(met.Over == 0, $"{met.Over} particle-substeps over; deepest {met.Deepest:0.000} m, {met.What}");
        Assert.True(met.CarDeepest < 0.2, $"a car {met.CarDeepest:0.00} m into the rock");
        var through = new FilmProbe { Unmet = w.FilmWorld() };
        WreckFilm.Record(W, w.Film!, Ground(w), probe: through);
        Assert.True(through.CarDeepest > 5 && through.Over > 100, $"without it: cars {through.CarDeepest:0.0} m in, {through.Over} particle-substeps over");
        // And the film's shots stay clean in there.
        Assert.Empty(w.ShootFilm()!.CameraFaults(Ground(w)));
    }

    /// <summary>
    /// GDD E.3's "static props" (note 1055): frontier:7 coming off at 2.5 km, where the lineside's trees and poles stand by
    /// the line. The crew meet them as walls: none ends a substep 2 cm in one. Recorded without them, a body went 0.64 m in.
    /// </summary>
    [Fact]
    public void AmongTheTreesTheLinesideIsSolid()
    {
        var w = OnTheLine("frontier:7", _ => 2500, crew: 8, speed: 20);
        var met = new FilmProbe();
        WreckFilm.Record(W, w.Film!, Ground(w), probe: met, world: w.FilmWorld());
        Assert.True(met.Over == 0, $"{met.Over} particle-substeps over; deepest {met.Deepest:0.000} m, {met.What}; " + string.Join("; ", met.Worst.Select(x => $"{x.Depth:0.000} at {x.At:0.000} {x.What}")));
        var through = new FilmProbe { Unmet = w.FilmWorld() };
        WreckFilm.Record(W, w.Film!, Ground(w), probe: through);
        Assert.Contains("the world", through.What);
        Assert.True(through.Over > 100, $"without them: {through.Over} particle-substeps over");
    }

    [Fact]
    public void ATunnelsBoreHoldsAThingInsideItsWallsAndUnderItsCrown()
    {
        var route = LineGen.Routes.Generate(DataFile.FindContentRoot(), "deadLines:5", 6);
        var line = route.Build();
        var land = line.Conditions!;
        var t = route.Features.First(f => f.Kind == Route.FeatureKind.Tunnel);
        var mid = line.Sample(Rail.RailLine.MainPath, (t.Start + t.End) / 2);
        var right = new Double3(-mid.Tangent.Z, 0, mid.Tangent.X).Normalized;
        // In the bore: as it is. Over the crown, held down; into the wall, held in.
        var inside = mid.Position + Double3.Up * 2;
        Assert.Equal(inside, land.Bore(inside, 0.2));
        var up = land.Bore(mid.Position + Double3.Up * 9, 0.2);
        Assert.True(up.Y < mid.Position.Y + 7.5, $"over the crown, held at {up.Y - mid.Position.Y:0.0} m over the rail");
        var into = land.Bore(mid.Position + right * 5 + Double3.Up, 0.2);
        Assert.InRange(Double3.Dot(into - mid.Position, right), 2, 3.0);
        // Past the portals it's the open air, and the world's solids say so.
        var past = line.Sample(Rail.RailLine.MainPath, t.Start - 200).Position + Double3.Up * 9;
        Assert.Equal(past, land.Bore(past, 0.2));
    }

    static Func<double, double, double> Ground(World w) => (x, z) =>
    {
        double hint = w.Film!.Along;
        return PlayerMotor.GroundAt(new Double3(x, 0, z), w.Train.Line, ref hint);
    };

    /// <summary>
    /// GDD v1.4 App. E.11 "No interpenetration: no particle rests inside a solid by more than 2 cm at the end of any substep"
    /// (note 1032): every particle of the crew, the stowed dead and what's thrown, against the film's own solids, seen at the
    /// end of each substep of the recording.
    /// </summary>
    [Theory]
    [InlineData(1, 20)]
    [InlineData(4, 20)]
    [InlineData(4, 8)]
    [InlineData(7, 26)]
    [InlineData(8, 20)]
    [InlineData(8, 8)]
    [InlineData(8, 30)]
    public void NoParticleEndsASubstepInsideASolid(int crew, double speed)
    {
        var w = Derailed(crew, speed, apart: true);
        var probe = new FilmProbe();
        WreckFilm.Record(W, w.Film!, Ground(w), probe: probe);
        Assert.True(probe.Seen > 0);
        Assert.True(probe.Over == 0, $"{probe.Over} of {probe.Seen} particle-substeps ({probe.Inside} inside at all) more than {W.Film.RestInside} m inside; deepest {probe.Deepest:0.000} m, {probe.What} at {probe.At:0.00} s");
    }

    /// <summary>
    /// GDD v1.4 App. E.11 "Clean camera: no camera sweep touches a collider, and nothing enters lens clearance" (note 1032):
    /// every finished shot's camera, a frame at a time, as the game moves it.
    /// </summary>
    [Theory]
    [InlineData(1, 20)]
    [InlineData(4, 20)]
    [InlineData(4, 8)]
    [InlineData(8, 20)]
    [InlineData(8, 14)]
    [InlineData(8, 30)]
    public void EveryFinishedShotsCameraIsClean(int crew, double speed)
    {
        var w = Derailed(crew, speed, apart: true);
        var film = w.ShootFilm()!;
        var faults = film.CameraFaults(Ground(w));
        Assert.True(faults.Count == 0, string.Join("; ", faults.Select(f => $"shot {f.Shot} ({f.Kind} {f.Subject}) at {f.Real} s: {f.What} by {f.Metres} m")));
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
        // The crew's vote: wreck.json's old rule, kept behind skip.own (note 315).
        host.World.WreckTuning = W with { Skip = new(Own: false) };
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
        host.World.WreckTuning = new WreckTuning { Skip = new(Own: false) };
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

    [Fact]
    public void EachPlayersOwnSkipIsNeverTheCrewsVote()
    {
        // GDD v1.4 App. E.5 after E.12 question 2 (the director, 7 Oct 2026: "It's up to EACH player if they want to skip
        // their film"; note 315): with skip.own, the host counts no votes, so the whole crew holding it, the host's own
        // player too, skips nobody's film but their own (a client's, not the host's to do).
        var net = new LoopbackNetwork();
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(5_000)]));
        TrainOnLine Train() => new(new TrainDynamics(Consist.Uniform(Tuning.Train, 3, 1)), line, 600);
        var host = new HostSession(net.CreateHost(), Train(), Tuning.Train, P);
        Assert.True(W.Skip.Own);
        host.World.WreckTuning = W;
        host.World.EnableBodies();
        var clients = Enumerable.Range(0, 3).Select(_ => new ClientSession(net.CreateClient(), Train(), Tuning.Train, P)).ToArray();
        for (int t = 0; t < 30; t++)
        {
            net.Advance(SimConstants.TickSeconds);
            host.Step();
            foreach (var c in clients)
                c.Step(default);
        }
        host.HostPlayer = clients[0].PlayerId!.Value;
        host.World.Derail("test");
        for (int t = 0; t < 30; t++)
        {
            net.Advance(SimConstants.TickSeconds);
            host.Step();
            foreach (var c in clients)
                c.Step(new PlayerIntent { Actions = PlayerActions.Skip });
        }
        Assert.False(host.World.FilmSkipped);
        Assert.Equal((0, 0), host.World.FilmVotes);
        Assert.All(clients, c => Assert.False(c.World.FilmSkipped));
    }
}
