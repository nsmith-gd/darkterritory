using Ballast;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The derailment film's extras and water (queue #110, ARCHITECTURE §8 note 373; GDD App. E.3 "Extras: bodies of the
/// already-dead stowed in cars, crates, loot and extinguishers aboard all join the wreck. They never get their own shot";
/// "Water: bodies get buoyancy and drag and float face down. Cars sink"; E.7).
/// </summary>
public class FilmExtrasTests
{
    static readonly WreckTuning W = DataFile.Load<WreckTuning>(Path.Combine(DataFile.FindContentRoot(), WreckTuning.File));
    static readonly PlayerTuning P = Tuning.Player;

    [Fact]
    public void TheStowedDeadAndWhatsAboardGoIntoTheWreckWithNoShotOfTheirOwn()
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(2000), new TrackSegment(600, 300), new TrackSegment(2000)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), line, 2250);
        train.Dynamics.Velocity = 20;
        train.RefreshFrames();
        var w = new World(train, Tuning.Combat) { WreckTuning = W, Tick = 500 };
        w.EnableBodies();
        w.BeginTick();
        // Two crew alive; one already dead, lying on car 2's roof; two crates and an extinguisher in car 3; a crate in someone's
        // locker (not loose) and one carried off by something (not the train's).
        var crew = new Dictionary<int, PlayerState> { [0] = PlayerMotor.SpawnInCab(train, P), [1] = PlayerMotor.SpawnOnRoof(train, 4, 0, P) };
        foreach (var (id, s) in crew)
        {
            var st = s;
            w.CrewAct(ref st, default, id);
        }
        var dead = PlayerMotor.SpawnOnRoof(train, 2, 2, P);
        var corpse = w.Bodies.SpawnRagdoll(train, 9, dead);
        double floor = train.Frames[3].Shape.Interior?.Min.Y ?? 1;
        var crates = new[] { w.Bodies.SpawnCrate(train, 3, new Double3(0, floor, -3)), w.Bodies.SpawnCrate(train, 3, new Double3(0, floor, 3)) };
        var extinguisher = w.Bodies.SpawnCrate(train, 3, new Double3(0.8, floor, 0), BodyKind.Extinguisher);
        var takenOff = w.Bodies.SpawnCrate(train, 5, new Double3(0, floor, 0));
        takenOff.TakenBy = 77;
        w.Derail("took the 45 km/h bend at 72 km/h, 27 km/h too fast");

        var extras = w.Film!.Extras!;
        Assert.Equal(4, extras.Count);
        var body = Assert.Single(extras, x => x.Kind == BodyKind.Ragdoll);
        Assert.Equal(9, body.Owner);
        Assert.Equal(2, body.Inside);
        Assert.Equal(11, body.Joints.Count);
        Assert.Equal(3, extras.Count(x => x.Inside == 3));
        Assert.Contains(extras, x => x.Kind == BodyKind.Extinguisher);
        // At the train's speed as it came off.
        Assert.All(extras, x => Assert.InRange(x.Velocity.Length, 15, 25));

        var film = w.ShootFilm()!;
        // The extras are in every frame, and they go with the wreck: on from where they were, and never their own shot.
        Assert.All(film.Frames, f => Assert.Equal(3, f.Items!.Count));
        Assert.All(film.Frames, f => Assert.Single(f.Dead!));
        var first = film.Frames[0];
        var last = film.Frames[^1];
        Assert.True((Centre(last.Dead![0]) - Centre(first.Dead![0])).Length > 5, "the stowed body didn't go with the wreck");
        foreach (var item in first.Items!)
            Assert.True((last.Items!.Single(i => i.Doll == item.Doll).At - item.At).Length > 3, $"extra {item.Doll} didn't go with the wreck");
        Assert.Equal(crew.Count, film.Shots.Count(s => s.Kind == ShotKind.Player));
        Assert.Equal(crew.Keys.Order(), film.Deaths.Keys.Order());
        // Never far under the ground (E.3's floor safety), on every machine the same: recorded again, and shot by a client from
        // the start as it's sent.
        Assert.All(film.Frames.SelectMany(f => f.Dead![0]), j => Assert.True(j.Y > -1.5, $"a joint at {j.Y:0.0}"));
        var again = WreckFilm.Record(W, w.Film!, (x, z) =>
        {
            double hint = w.Film!.Along;
            return PlayerMotor.GroundAt(new Double3(x, 0, z), w.Train.Line, ref hint);
        });
        Assert.Equal(Centre(last.Dead![0]), Centre(again.Frames[^1].Dead![0]));
        var messages = Messages.FilmMessages(w.Film!);
        var start = Messages.ReadFilm(messages.Select((m, i) => (i, m[3..])).ToDictionary(x => x.i, x => x.Item2), messages.Count)!;
        Assert.Equal(4, start.Extras!.Count);
        var there = new World(w.Train) { WreckTuning = W, Film = start }.ShootFilm()!;
        Assert.Equal(Centre(last.Dead![0]), Centre(there.Frames[^1].Dead![0]));
        Assert.Equal(last.Items!.Select(i => i.At), there.Frames[^1].Items!.Select(i => i.At));
        _ = (corpse, crates, extinguisher);
    }

    [Fact]
    public void TheBudgetSimulatesTheBodiesAndThingsNearestTheCrewAndTheRestRideTheirCars()
    {
        // E.3: "up to 8 player ragdolls, 8 extra ragdolls, 20 cars and 40 loose items. Past that, the loose items furthest
        // from any player freeze in place."
        var t = W.Film;
        Double3[] joints(double x) => [.. Enumerable.Range(0, 11).Select(_ => new Double3(x, 1, 0))];
        var all = new List<FilmExtra>();
        for (int i = 0; i < 12; i++)
            all.Add(new FilmExtra(BodyKind.Ragdoll, joints(i * 5), Double3.Zero, 0, Owner: i));
        for (int i = 0; i < 50; i++)
            all.Add(new FilmExtra(BodyKind.Crate, [new Double3(-i * 2, 1, 0)], Double3.Zero, 0));
        var budgeted = WreckFilm.Budget(all, [Double3.Zero], t);
        // All of them, in the order they were given; the nearest simulated, the rest riding.
        Assert.Equal(all, budgeted.Select(x => x with { Rides = false }));
        var kept = budgeted.Where(x => !x.Rides).ToList();
        Assert.Equal(t.ExtraDolls, kept.Count(x => x.Kind == BodyKind.Ragdoll));
        Assert.Equal(t.ExtraItems, kept.Count(x => x.Kind == BodyKind.Crate));
        Assert.Equal(Enumerable.Range(0, t.ExtraDolls), kept.Where(x => x.Kind == BodyKind.Ragdoll).Select(x => x.Owner));
        Assert.All(kept.Where(x => x.Kind == BodyKind.Crate), x => Assert.True(x.Joints[0].X > -2 * t.ExtraItems));

        // In the film: only the kept ones are simulated; one riding stays where it was in its car as the car turns over.
        var car = new FilmCar(0, new Double3(0, 0, 0), new Double3(1, 0, 0), Double3.Up, new Double3(0, 0, 1), new Double3(0, 0, -10),
            new Double3(0, 0, 3), 30, 1.4, 3.5, 7, 3);
        var start = new FilmStart(5, [car], [], [], "test", 10, Extras: budgeted);
        var recording = WreckFilm.Record(W, start, (_, _) => 0);
        Assert.All(recording.Frames, f => Assert.Equal(t.ExtraItems, f.Items!.Count));
        Assert.All(recording.Frames, f => Assert.Equal(t.ExtraDolls, f.Dead!.Count));
        var rider = budgeted.Last(x => x.Rides);
        var last = recording.Frames[^1];
        var at = WreckFilm.Riding(start, last, rider)[0];
        var (origin, right, up, back) = last.Cars[0];
        var was = rider.Joints[0] - car.Origin;
        var now = at - origin;
        Assert.Equal(Double3.Dot(was, car.Right), Double3.Dot(now, right), 6);
        Assert.Equal(Double3.Dot(was, car.Up), Double3.Dot(now, up), 6);
        Assert.Equal(Double3.Dot(was, car.Back), Double3.Dot(now, back), 6);
        Assert.True((origin - car.Origin).Length > 1, "the car didn't move");
    }

    [Fact]
    public void ABodyThatGoesInTheWaterComesUpAndFloats()
    {
        // A car standing well off, and a body and a crate dropped over a pond: 2 m of water over a bed at nothing.
        var car = new FilmCar(0, Double3.Zero, new Double3(1, 0, 0), Double3.Up, new Double3(0, 0, 1), Double3.Zero, Double3.Zero, 30, 1.4, 3.5, 7, 3);
        Double3[] joints = [.. WreckFilm.TaskPose(FilmTask.None).Select(j => new Double3(30, 3, 0) + j)];
        var start = new FilmStart(5, [car], [], [], "test", 0, Extras:
            [new FilmExtra(BodyKind.Ragdoll, joints, Double3.Zero, -1, Owner: 3), new FilmExtra(BodyKind.Crate, [new Double3(34, 4, 0)], Double3.Zero, -1)]);
        double? pond(double x, double z) => x > 20 ? 2 : null;
        var wet = WreckFilm.Record(W, start, (_, _) => 0, pond);
        var dry = WreckFilm.Record(W, start, (_, _) => 0);
        double Floating(FilmFrame f) => Centre(f.Dead![0]).Y;
        // Dry, it lies on the ground; in the water it's up at the surface, not on the bed, and so is the crate.
        Assert.True(Floating(dry.Frames[^1]) < 0.6, $"dry, at {Floating(dry.Frames[^1]):0.00}");
        Assert.InRange(Floating(wet.Frames[^1]), 1.4, 2.6);
        Assert.InRange(wet.Frames[^1].Items![0].At.Y, 1.4, 2.6);
        // Not where the water isn't: the car (the wreck's) is the same wet or dry.
        Assert.Equal(dry.Frames[^1].Cars[0], wet.Frames[^1].Cars[0]);
    }

    static Double3 Centre(Double3[] joints) => joints.Aggregate(Double3.Zero, (a, j) => a + j) * (1.0 / joints.Length);
}
