using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 851 (queue #579; the director's playtest of 16 Oct, line 22: "Is that a fire in the back car? How did that even get
/// started? ... That doesn't seem like there's anything that was there"; the 16 Oct evening, line 5: "The fire in the rear car
/// didn't go out ever despite both of us fighting it"): every fire the game starts has a cause, told to the crew as it starts
/// (the news line) and written in the report (C.9's Fire row), the two the same fact; and a Cinder Hound on the roof of the
/// car the crew are fighting the fire in doesn't relight it over their heads, unseen, every twenty seconds.
/// </summary>
public class FireCauseTests
{
    static readonly EnemyTuning E = Tuning.Enemies;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly HotBoxTuning H = DataFile.Load<UpkeepTuning>(Path.Combine(DataFile.FindContentRoot(), UpkeepTuning.File)).HotBox;

    static CarFire TheFire(Night n, int car) => Assert.Single(n.World.ActiveEnemies.OfType<CarFire>(), f => !f.Gone && f.Attached == car);

    static NewsEvent TheNews(Night n) => Assert.Single(n.World.NewsLog, e => e.Kind == NewsKind.Fire);

    static CinderHound OnRoof(Night n, int car, double z)
    {
        var hound = n.World.AddEnemy(id => new CinderHound(id, id));
        hound.Restore(SpinePhase.Commit, 0, E.CinderHounds.Health, car, new Double3(0.6, n.Train.Frames[car].Shape.RoofHeight, z), 0, 0, 0.6, hound.Id, 0);
        return hound;
    }

    [Fact]
    public void TheHotBoxsFireIsToldAndReported()
    {
        // Note 331's hot box left too long: the car catches, and now the crew are told why, and the clerk writes it.
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(60_000)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), line, 5_000);
        train.Dynamics.Velocity = 10;
        var w = new World(train, Tuning.Combat);
        w.EnableEnemies(E with { Director = E.Director with { GraceMinSeconds = 1e9, GraceMaxSeconds = 1e9 } }, route: null, 1, crew: 1, authority: true);
        w.Upkeep = new UpkeepTuning { HotBox = H, Lamp = new() { Enabled = false } };
        train.Vehicles[2].HotBox = H.FireAfter - 1;
        for (int i = 0; i < 2 * SimConstants.TickRate; i++)
        {
            w.BeginTick();
            w.Step(new TrainControls { Throttle = 1, Reverser = 1 });
        }
        var fire = Assert.Single(w.ActiveEnemies.OfType<CarFire>(), f => !f.Gone && f.Attached == 2);
        Assert.Equal(FireCause.HotBox, fire.Cause);
        var news = Assert.Single(w.NewsLog, e => e.Kind == NewsKind.Fire);
        Assert.Equal("FIRE IN CAR 2 · ITS HOT BOX RAN HOT", News.Line(w, news));
        var row = Assert.Single(w.Attribution.Of(IncidentKind.Fire));
        Assert.Equal("Car 2's hot box set it alight", row.What);
        Assert.Equal($"Its rear axle box ran hot {H.FireAfter:0} s, ungreased.", row.Action);
        Assert.Equal(-1, row.Actor);
    }

    [Fact]
    public void TheHoundsFireIsToldAndReported()
    {
        // A hound feeding with nobody near sets its car alight (GDD App. F, 6 Oct 2026); before, with no record and no word.
        var n = new Night(4, speed: 14, enemies: HoundRunTests.Quiet);
        var hound = OnRoof(n, n.Train.Dynamics.Consist.Vehicles[^1].Id, 0);
        n.Run(E.CinderHounds.IgniteEverySeconds + E.CinderHounds.Aboard.IgniteWindUpSeconds + 1);
        // Wherever its patrol (note 472) has taken it by then.
        int car = hound.Attached;
        var fire = TheFire(n, car);
        Assert.Equal(FireCause.Hounds, fire.Cause);
        Assert.Equal($"FIRE IN CAR {car} · A CINDER HOUND ON ITS ROOF", News.Line(n.World, TheNews(n)));
        var row = Assert.Single(n.World.Attribution.Of(IncidentKind.Fire));
        Assert.Equal($"Cinder Hounds set car {car} alight", row.What);
        Assert.Equal("Feeding on its roof, nobody near.", row.Action);
    }

    [Fact]
    public void AFireThatJumpsTheCouplingSaysWhereItCameFrom()
    {
        var n = new Night(5, speed: 8);
        var t = E.CarFire;
        n.World.AddEnemy(id => CarFire.In(id, n.Train, 2, 99, t, FireCause.Set).Ablaze(1, 1.5));
        n.Run(t.SpreadSeconds + 2);
        int into = n.Train.VehicleBehind(2);
        var next = TheFire(n, into);
        Assert.Equal(FireCause.Jumped, next.Cause);
        Assert.Equal(2, next.From);
        var news = n.World.NewsLog.Where(e => e.Kind == NewsKind.Fire).ToList();
        Assert.Equal(2, news.Count);
        Assert.Equal("FIRE IN CAR 2", News.Line(n.World, news[0]));
        Assert.Equal($"FIRE IN CAR {into} · SPREAD FROM CAR 2", News.Line(n.World, news[1]));
        var rows = n.World.Attribution.Of(IncidentKind.Fire).ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal($"The fire jumped the coupling into car {into}", rows[1].What);
        Assert.Equal("From car 2.", rows[1].Action);
    }

    [Fact]
    public void APowderCarGoingUpNamesItselfInTheCarsItLights()
    {
        var t = E.CarFire;
        var n = new Night(6, speed: 8);
        int car = 3;
        n.Train.Vehicles[car].Cargo = CargoKind.Ammunition;
        var fire = n.World.AddEnemy(id => CarFire.In(id, n.Train, car, 2, t, FireCause.Set));
        fire.Ablaze(0.97);
        for (int s = 0; s < 60 && n.Train.Vehicles[car].Integrity > 0; s++)
            n.Run(1);
        Assert.Equal(0, n.Train.Vehicles[car].Integrity);
        foreach (int next in new[] { car - 1, car + 1 })
        {
            var caught = TheFire(n, next);
            Assert.Equal(FireCause.Blast, caught.Cause);
            Assert.Equal(car, caught.From);
            var news = Assert.Single(n.World.NewsLog, e => e.Kind == NewsKind.Fire && e.Cars[0] == next);
            Assert.Equal($"FIRE IN CAR {next} · CAR {car} WENT UP", News.Line(n.World, news));
            Assert.Contains(n.World.Attribution.Of(IncidentKind.Fire), r => r.What == $"Car {car} going up set car {next} alight");
        }
    }

    [Fact]
    public void AThrownLampsFireNamesItsThrower()
    {
        // Note 728's lamp, as before: the record was the throw's; now it's the fire's own, the same words.
        var n = new Night(4, speed: 0);
        n.World.Names[2] = "Priya";
        int car = 2;
        var room = n.Train.Frames[car].Shape.Interior!.Value;
        // Thrown down at the boards (SenseTableTests' throw).
        var lamp = n.World.Bodies.SpawnCrate(n.Train, car, new Double3(room.Centre.X, room.Min.Y + 1.0, room.Centre.Z - 2), BodyKind.Lamp);
        lamp.LastHeld = 2;
        lamp.Pbd.Particles[0].SetVelocity(new Double3(0, -9, 1), SimConstants.TickSeconds);
        lamp.Pbd.Wake();
        var controls = new TrainControls { Reverser = 1 };
        for (int i = 0; i < SimConstants.TickRate; i++)
        {
            n.World.BeginTick();
            n.World.Step(controls);
            n.World.ApplyDamage(id => n.Crew.TryGetValue(id, out var s) ? s : null, (id, s) => n.Crew[id] = s, n.Crew.Keys);
            n.World.StepBodies([.. n.Crew.Select(c => (c.Key, c.Value))]);
        }
        var fire = TheFire(n, car);
        Assert.Equal(FireCause.Lamp, fire.Cause);
        Assert.Equal(2, fire.Actor);
        Assert.Equal("FIRE IN CAR 2 · A LAMP THROWN BY PRIYA", News.Line(n.World, TheNews(n)));
        var row = Assert.Single(n.World.Attribution.Of(IncidentKind.Fire));
        Assert.Equal("A thrown lamp set car 2 alight", row.What);
        Assert.Equal("Thrown by {actor}.", row.Action);
    }

    /// <summary>
    /// <paramref name="crew"/> crewmates in <paramref name="car"/>, each with a charged extinguisher, the car at full blaze from
    /// <paramref name="heat"/>: each sprays the nearest burning cell in reach and walks to the next (FireCellsTests' hand), until
    /// it's out or <paramref name="seconds"/> are up. How long it took, and whether it's out.
    /// </summary>
    internal static (bool Out, double Seconds) Fought(Night n, int car, int crew, double heat, double seconds, CarFire? fire = null)
    {
        var t = E.CarFire;
        var room = n.Train.Frames[car].Shape.Interior!.Value;
        double doorX = Tuning.Train.Geometry.Interior!.DoorX;
        for (int k = 1; k <= crew; k++)
        {
            var feet = new Double3(doorX + (k - 1) * 0.5, room.Min.Y, room.Max.Z - 0.6 - (k - 1) * 0.6);
            n.Crew[k] = new PlayerState { Parent = car, Position = feet, Yaw = 0, Surface = Surface.Deck, Health = P.Health };
            var ext = n.World.Bodies.SpawnCrate(n.Train, car, feet with { Y = room.Min.Y + 0.1 }, BodyKind.Extinguisher);
            ext.Carrier = k;
            ext.Claimed = true;
        }
        fire ??= (CarFire)n.World.AddEnemy(id => CarFire.In(id, n.Train, car, 0, t, FireCause.Set).Ablaze(heat));
        n.Run(SimConstants.TickSeconds);
        var grid = FireGrid.Of(n.Train, car, t.CellSize)!;
        double eye = Tuning.Train.Pick.EyeHeight;
        uint t0 = n.World.Tick;
        for (int i = 0; i < seconds * SimConstants.TickRate && !fire.Gone; i++)
        {
            n.Run(SimConstants.TickSeconds, id =>
            {
                var s = n.Crew[id];
                var at = s.Position + Double3.Up * eye;
                int near = -1;
                double best = double.MaxValue;
                for (int c = 0; c < grid.Count && c < fire.Heat.Length; c++)
                    if (fire.Heat[c] > 0 && (grid.Centre[c] - at).Length is var d && d < best)
                        (best, near) = (d, c);
                if (near < 0)
                    return new PlayerIntent();
                var to = grid.Centre[near] - at;
                double yaw = DMath.Atan2(-to.X, -to.Z), pitch = DMath.Atan2(to.Y, Math.Sqrt(to.X * to.X + to.Z * to.Z));
                var look = new PlayerIntent
                {
                    LookYaw = (float)Math.Clamp(Math.IEEERemainder(yaw - s.Yaw, 2 * Math.PI), -0.5, 0.5),
                    LookPitch = (float)Math.Clamp(pitch - s.Pitch, -0.5, 0.5),
                };
                if (best < t.SprayReach - 0.3)
                    return look with { Buttons = PlayerButtons.Fire };
                var (step, _) = Bots.WarmUp.Steer(s, new Double3(doorX, 0, grid.Centre[near].Z), s.Yaw);
                return step with { LookYaw = look.LookYaw, LookPitch = look.LookPitch };
            });
        }
        return (fire.Gone, (n.World.Tick - t0) / (double)SimConstants.TickRate);
    }

    [Fact]
    public void AHoundOnTheRoofDoesntRelightTheCarTheCrewAreFightingTheFireIn()
    {
        // The 16 Oct evening's rear car: two inside on the extinguishers, a hound on the roof over them. It can't reach them
        // through the roof (note 471), and before it counted them as nobody near: it fed, and lit the car again every
        // igniteEverySeconds, with no word. Now it waits for them to come out; the fire they put out stays out.
        // The hound stays on that roof (its patrol off), as the director's did over them.
        var quiet = HoundRunTests.Quiet;
        var n = new Night(5, speed: 10, enemies: quiet with { CinderHounds = quiet.CinderHounds with { Patrol = quiet.CinderHounds.Patrol with { On = false } } });
        int car = n.Train.Dynamics.Consist.Vehicles[^1].Id;
        var hound = OnRoof(n, car, 0);
        var (gone, seconds) = Fought(n, car, 2, 1.0, 40);
        Assert.True(gone, $"still alight after {seconds:0.0} s");
        Assert.True(seconds <= 20, $"took {seconds:0.0} s");
        Assert.False(hound.Gone);
        Assert.Equal(car, hound.Attached);
        n.Run(E.CinderHounds.IgniteEverySeconds * 2 + 2);
        Assert.DoesNotContain(n.World.ActiveEnemies, e => e is CarFire f && !f.Gone);
        Assert.Single(n.World.NewsLog, e => e.Kind == NewsKind.Fire);
        Assert.All(n.Crew.Values, s => Assert.True(s.Alive));
        // Out of the car and off the train, nobody's near it: it feeds, and lights the car again, told as its own (on boards
        // the first fire charred it may not take; the telling's the point). Its clock ran out under them (note 891: it runs
        // the whole time it's aboard, only a blow sets it back), so it's at once.
        n.Crew.Clear();
        n.Run(E.CinderHounds.Aboard.IgniteWindUpSeconds + 3);
        var relit = n.World.NewsLog.Where(e => e.Kind == NewsKind.Fire).ToList();
        Assert.Equal(2, relit.Count);
        Assert.Equal(FireCause.Hounds, (FireCause)relit[1].Cause);
        Assert.Equal(car, relit[1].Cars[0]);
    }
}
