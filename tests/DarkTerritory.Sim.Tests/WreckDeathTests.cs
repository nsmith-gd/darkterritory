using Ballast;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The director's calls of 5 Oct 2026 on the derailment film (ARCHITECTURE §8 note 257): the cars hit the bodies, the gunner
/// is thrown out of the gun's seat and never through the gun, each body's landings are recorded, and each player dies on
/// their own first hard hit in the wreck, not on the derail tick (GDD v1.4 App. E.2 step 1), the same on every machine.
/// </summary>
public class WreckDeathTests
{
    static readonly WreckTuning W = DataFile.Load<WreckTuning>(Path.Combine(DataFile.FindContentRoot(), WreckTuning.File));
    static readonly PlayerTuning P = Tuning.Player;
    static double Flat(double x, double z) => 0;

    /// <summary>One car on flat ground, at the origin facing −Z, rolling on the rails at <paramref name="speed"/>.</summary>
    static FilmCar Car(double speed, bool gun = false) => new(0, Double3.Zero, new Double3(1, 0, 0), Double3.Up, new Double3(0, 0, 1),
        new Double3(0, 0, -speed), Double3.Zero, 30, 1.4, 3.5, 7, Railed: 3, Floor: 1.1, Gun: gun, GunAt: new Double3(0, 3.5 + 0.9, -2), GunYaw: 0);

    [Fact]
    public void ACarPloughingIntoABodyThrowsIt()
    {
        // Stood on the line 3 m ahead of a car rolling at 10 m/s: it hops on its kick, and the car comes through it.
        var player = new FilmPlayer(7, "Sam", "on the line", new Double3(0, 0, -10), Double3.Zero, 0, -1);
        var start = new FilmStart(1, [Car(10)], [], [player], "test", 10);
        var film = WreckFilm.Shoot(W, start, Flat);
        var first = Centre(film.Frames[0].Ragdolls[0]);
        var later = Centre(film.Frames[45].Ragdolls[0]);
        // Carried and thrown on ahead of the car, the way it was going: well past where it stood (it only hopped by itself).
        Assert.True(later.Z < first.Z - 5, $"from z {first.Z:0.0} to {later.Z:0.0}");
        // Never inside the car: every joint stays ahead of its front face (or over its roof).
        for (int f = 0; f < film.Frames.Count; f++)
        {
            var (o, right, carUp, back) = film.Frames[f].Cars[0];
            foreach (var j in film.Frames[f].Ragdolls[0])
            {
                double along = Double3.Dot(j - o, back), up = Double3.Dot(j - o, carUp), across = Double3.Dot(j - o, right);
                Assert.False(Math.Abs(along) < 7 - 0.05 && up > 0.05 && up < 3.5 - 0.05 && Math.Abs(across) < 1.4 - 0.05, $"frame {f}: a joint inside the car");
            }
        }
        // The car's hit is a landing off a car, and they die in a hit, not in the air (take 3: alive till the one that kills).
        Assert.Contains(film.Frames.SelectMany(f => f.Landings), l => l.Doll == 0 && l.Car && l.Speed >= W.Film.ThudSpeed);
        var death = film.Deaths[7];
        Assert.NotEqual(FilmDeathKind.Settle, death.Kind);
        Assert.Contains(film.Frames[(int)Math.Round(death.At * WreckFilm.Rate)].Landings, l => l.Doll == 0);
    }

    [Fact]
    public void TheGunnerStartsClearOfTheGunAndIsThrownUpOutOfTheSeat()
    {
        var car = Car(15, gun: true);
        double yaw = car.GunYaw;
        var seat = new Double3(car.GunAt.X + Math.Sin(yaw) * 0.75, 3.5, car.GunAt.Z + Math.Cos(yaw) * 0.75);
        var gunner = new FilmPlayer(3, "Kofi", "on the gun", seat, new Double3(0, 0, -15), yaw, -1, Seated: true);
        var film = WreckFilm.Shoot(W, new FilmStart(2, [car], [], [gunner], "test", 15), Flat);
        static Double3 Local(FilmFrame f, Double3 j)
        {
            var (o, right, up, back) = f.Cars[0];
            var d = j - o;
            return new Double3(Double3.Dot(d, right), Double3.Dot(d, up), Double3.Dot(d, back));
        }
        // Inside the gun's solid (its car's frame; laid straight ahead, so its frame is the car's about the pivot).
        static bool In((Double3 Min, Double3 Max) box, Double3 g) =>
            g.X > box.Min.X && g.X < box.Max.X && g.Y > box.Min.Y && g.Y < box.Max.Y && g.Z > box.Min.Z && g.Z < box.Max.Z;
        bool InGun(Double3 local) => In(WreckFilm.GunBarrel, local - car.GunAt) || In(WreckFilm.GunPedestal, local - car.GunAt);
        for (int f = 0; f < film.Frames.Count; f++)
            foreach (var j in film.Frames[f].Ragdolls[0])
                Assert.False(InGun(Local(film.Frames[f], j)), $"frame {f}: a joint inside the gun");
        // Up out of the seat at once, and high: the body's middle over a metre higher than it sat, at its height.
        double seated = Local(film.Frames[0], Centre(film.Frames[0].Ragdolls[0])).Y;
        Assert.True(Local(film.Frames[3], Centre(film.Frames[3].Ragdolls[0])).Y > seated + 0.2, "not leaving the seat upward");
        double top = film.Frames.Take(30).Max(f => Local(f, Centre(f.Ragdolls[0])).Y);
        Assert.True(top > seated + 1.0, $"thrown {top - seated:0.00} m up");
    }

    /// <summary>A world with <paramref name="crew"/> aboard (cab, roofs), derailed on a curve at 20 m/s; their states.</summary>
    static (World World, Dictionary<int, PlayerState> Crew) Derailed(int crew)
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(2000), new TrackSegment(600, 300), new TrackSegment(2000)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), line, 2250);
        train.Dynamics.Velocity = 20;
        train.RefreshFrames();
        var world = new World(train, Tuning.Combat) { WreckTuning = W, Tick = 500 };
        world.EnableBodies();
        world.BeginTick();
        var states = new Dictionary<int, PlayerState>();
        for (int i = 0; i < crew; i++)
        {
            var s = i == 0 ? PlayerMotor.SpawnInCab(train, P) : PlayerMotor.SpawnOnRoof(train, 1 + i % 6, (i % 3 - 1) * 3.0, P);
            world.CrewAct(ref s, default, i);
            states[i] = s;
        }
        world.Derail("took the 45 km/h bend at 72 km/h, 27 km/h too fast");
        return (world, states);
    }

    [Fact]
    public void EachPlayerDiesInTheirOwnFatalHitNotOnTheDerailTickTheSameEverywhere()
    {
        var (w, crew) = Derailed(4);
        var film = w.ShootFilm()!;
        var t = W.Film;
        uint derail = w.DerailTick;
        Assert.Equal(crew.Keys.Order(), w.DoomedAt.Keys.Order());
        foreach (var (id, death) in film.Deaths)
        {
            int doll = film.Start.Players.ToList().FindIndex(p => p.Id == id);
            // Take 3: alive through the hits before (each a landing), and the hit that kills is one: of the killing speed
            // once they've survived theirs, of certain death at any time, any past the give-up time or after the most they
            // take, or (never killed) their last. Never the derail tick.
            Assert.True(death.At > 0, $"{id} died on the derail tick");
            int at = (int)Math.Round(death.At * WreckFilm.Rate);
            int survived = film.Frames.Take(at).Sum(f => f.Landings.Count(l => l.Doll == doll));
            Assert.Equal(survived, death.Survived);
            if (death.Kind == FilmDeathKind.Impact)
            {
                var hit = film.Frames[at].Landings.Single(l => l.Doll == doll);
                bool lastHit = !film.Frames.Skip(at + 1).Any(f => f.Landings.Any(l => l.Doll == doll));
                Assert.True(hit.Speed >= t.CertainDeathSpeed || lastHit
                    || survived >= t.SurviveHits && (hit.Speed >= t.ImpactSpeed || death.At >= t.GiveUpSeconds || survived >= t.MostHits),
                    $"{id} died of a {hit.Speed:0.0} m/s hit with {survived} survived at {death.At:0.00} s");
            }
            // The host kills them as it lands in their own first person.
            Assert.Equal(derail + (uint)Math.Ceiling(t.DeathDelay(death.At) * SimConstants.TickRate - 1e-9), w.DoomedAt[id]);
            Assert.True(film.FirstPersonOf(id) > t.DeathDelay(death.At));
        }
        // Over the ticks: alive till their own tick, dead from it. Every death counted on the derail tick (the settlement's).
        var last = w.DoomedAt.Values.Max();
        for (uint tick = derail; tick <= last; tick++)
        {
            w.Tick = tick;
            w.ApplyDamage(id => crew.TryGetValue(id, out var s) ? s : null, (id, s) => crew[id] = s, crew.Keys);
            w.StepBodies([.. crew.Select(c => (c.Key, c.Value))]);
            Assert.Equal(crew.Count, w.Bodies.Deaths);
            foreach (var (id, s) in crew)
                Assert.Equal(tick < w.DoomedAt[id], s.Alive);
        }
        Assert.All(crew.Values, s => Assert.Equal(DeathCause.Derailed, s.Death));
        // They take hits and live (the director: two or three before the one that kills, where the physics allows).
        Assert.Contains(film.Deaths.Values, d => d.Survived >= t.SurviveHits);
        Assert.Equal(crew.Count, w.Bodies.All.Count(b => b.Kind == Physics.BodyKind.Ragdoll));
        // Deterministic: recorded again, and shot by a client from the start as it's sent, the same deaths.
        var again = WreckFilm.Record(W, w.Film!, (x, z) =>
        {
            double hint = w.Film!.Along;
            return PlayerMotor.GroundAt(new Double3(x, 0, z), w.Train.Line, ref hint);
        });
        Assert.Equal(film.Deaths, again.Deaths);
        var messages = Messages.FilmMessages(w.Film!);
        var start = Messages.ReadFilm(messages.Select((m, i) => (i, m[3..])).ToDictionary(x => x.i, x => x.Item2), messages.Count)!;
        var there = new World(w.Train) { WreckTuning = W, Film = start }.ShootFilm()!;
        Assert.Equal(film.Deaths, there.Deaths);
    }

    [Fact]
    public void EveryBodysLandingsAreRecorded()
    {
        var (w, _) = Derailed(4);
        var film = w.ShootFilm()!;
        // Each of them lands at least once (the hit that killed them is one).
        for (int doll = 0; doll < film.Start.Players.Count; doll++)
        {
            var times = film.Frames.Select((f, i) => (f, i)).Where(x => x.f.Landings.Any(l => l.Doll == doll)).Select(x => x.i / (double)WreckFilm.Rate).ToList();
            Assert.NotEmpty(times);
            // Apart by thudGap at the least, but for a hit hard enough to kill (it always thuds).
            var soft = film.Frames.Select((f, i) => (f, i)).Where(x => x.f.Landings.Any(l => l.Doll == doll && l.Speed < W.Film.ImpactSpeed)).Select(x => x.i / (double)WreckFilm.Rate).ToList();
            for (int k = 1; k < soft.Count; k++)
                Assert.True(soft[k] - soft[k - 1] >= W.Film.ThudGap - 1e-9);
            Assert.All(film.Frames.SelectMany(f => f.Landings).Where(l => l.Doll == doll), l => Assert.True(l.Speed >= W.Film.ThudSpeed));
        }
    }

    static Double3 Centre(Double3[] joints) => joints.Aggregate(Double3.Zero, (a, j) => a + j) * (1.0 / joints.Length);
}
