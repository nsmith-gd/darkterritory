using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The director's playtest of build 1121 (ARCHITECTURE §8 note 263): what a Stoker can do to a standing train, clubbing it
/// through the firebox door, the break after one's driven off, the fire's burn by the second, and fires that went out by
/// themselves.
/// </summary>
public class Build1121Tests
{
    static readonly PlayerTuning P = Tuning.Player;
    static readonly EnemyTuning E = Tuning.Enemies;

    /// <summary>In the cab, a step behind the firebox door and facing it.</summary>
    static PlayerState AtTheFirebox(TrainOnLine train)
    {
        var firebox = train.Frames[0].Shape.Interactables.First(i => i.Kind == InteractableKind.Firebox).Position;
        var s = PlayerMotor.SpawnInCab(train, P);
        return s with { Position = s.Position with { X = firebox.X, Z = firebox.Z + 0.9 }, Yaw = 0, Pitch = -0.3 };
    }

    static Night Standing(double pressure)
    {
        var n = new Night(4, speed: 0, boiler: true);
        n.Train.Boiler.Pressure = pressure;
        n.Controls = new TrainControls { Reverser = 1, Brake = 1 };
        return n;
    }

    [Fact]
    public void AStokerFeedingAStandingTrainDoesNotTakeItOffItsBrake()
    {
        // Build 1121: with steam driving (T97) a standing engine off its brake pulls away, and the Stoker's runaway let it off.
        var n = Standing(60);
        double at = n.Train.Dynamics.Distance;
        var stoker = n.World.AddEnemy(id => Stoker.InFirebox(id, n.Train, false, E.Stoker));
        n.Run(E.Stoker.SootSeconds + 8, holdSpeed: false);
        Assert.True(stoker.Feeding, $"{stoker.Phase}");
        Assert.Equal(at, n.Train.Dynamics.Distance, 6);
        // Under way, it still has the train: off the brake and the speed climbing with the pressure (App. A.5 FEED).
        n.Train.Dynamics.Velocity = 5;
        n.Run(10, holdSpeed: false);
        Assert.True(n.Train.Dynamics.Speed > 5, $"{n.Train.Dynamics.Speed:0.0} m/s");
    }

    [Fact]
    public void TheStokerIsClubbedOnlyThroughTheOpenFireboxDoor()
    {
        // Build 1121: "I just started swinging the crowbar at the firebox and it killed the creature, even though the firebox
        // wasn't open." App. A.5 KILL: open the firebox and bludgeon it.
        var n = Standing(60);
        var stoker = n.World.AddEnemy(id => Stoker.InFirebox(id, n.Train, false, E.Stoker));
        n.Crew[1] = AtTheFirebox(n.Train);
        Assert.False(n.Train.Boiler.FireDoorOpen);
        n.Run(6, _ => new PlayerIntent { Actions = PlayerActions.Swing }, holdSpeed: false);
        Assert.Equal(E.Stoker.Health, stoker.Health, 6);
        Assert.Equal(P.Health, n.Crew[1].Health);
        // A shovelful opens the door; then every blow lands, and burns.
        n.Run(2, _ => new PlayerIntent { Buttons = PlayerButtons.Use }, holdSpeed: false);
        Assert.True(n.Train.Boiler.FireDoorOpen);
        n.Run(4, _ => new PlayerIntent { Actions = PlayerActions.Swing }, holdSpeed: false);
        Assert.True(stoker.Health < E.Stoker.Health, $"hp {stoker.Health}");
        Assert.True(n.Crew[1].Health < P.Health);
    }

    /// <summary>A standing engine whose fire's kept where it's put (topped up each second), with someone or nobody in the cab.</summary>
    static double RunHot(Night n, double seconds, double firebox, Func<int, PlayerIntent>? intent = null, Func<bool>? until = null)
    {
        double start = n.World.ElapsedSeconds;
        for (int s = 0; s < seconds && until?.Invoke() != true; s++)
        {
            n.Train.Boiler.Firebox = firebox;
            n.Train.Boiler.Pressure = 70;
            n.Run(1, intent, holdSpeed: false);
        }
        return n.World.ElapsedSeconds - start;
    }

    static Stoker? StokerOf(Night n) => n.World.ActiveEnemies.OfType<Stoker>().FirstOrDefault(s => !s.Gone);

    [Fact]
    public void ALowOrWarmFireNeverDrawsTheStokerAndAHotOneDoes()
    {
        // The director's decision of 6 Oct 2026: "attracted to the train once the train goes above a certain heat in the
        // firebox, since it's something that looks for heat."
        var n = Standing(70);
        RunHot(n, 180, E.Stoker.HeatFirebox - 0.5);
        Assert.Null(StokerOf(n));
        var low = Standing(10);
        RunHot(low, 180, 0);
        Assert.Null(StokerOf(low));
        double took = RunHot(n, 60, Tuning.Boiler.FireboxCapacity, until: () => StokerOf(n) is not null);
        Assert.InRange(took, E.Stoker.HeatSeconds, E.Stoker.HeatSeconds + 2);
    }

    [Fact]
    public void ItBoardsAtTheTenderAndTelegraphsBeforeItGetsIn()
    {
        var n = Standing(70);
        RunHot(n, 60, Tuning.Boiler.FireboxCapacity, until: () => StokerOf(n) is not null);
        var stoker = StokerOf(n)!;
        var coal = n.Train.Frames[0].Shape.Interactables.First(i => i.Kind == InteractableKind.Coal).Position;
        var firebox = n.Train.Frames[0].Shape.Interactables.First(i => i.Kind == InteractableKind.Firebox).Position;
        Assert.True(stoker.Boarding);
        Assert.True((stoker.Local - coal).Length < 1, $"at {stoker.Local}, the coal at {coal}");
        // Telegraphing out in the open, crossing to the door, the boiler untouched.
        n.Run(1, holdSpeed: false);
        Assert.Equal(SpinePhase.Telegraph, stoker.Phase);
        Assert.Equal(0, n.Train.Boiler.ExternalHeat);
        n.Run(E.Stoker.BoardSeconds + 1, holdSpeed: false);
        Assert.True(stoker.Feeding);
        Assert.False(stoker.Boarding);
        Assert.Equal(firebox, stoker.Local);
        var entered = Assert.Single(n.Events, e => e.EnemyId == stoker.Id && e.To == SpinePhase.Commit);
        Assert.Equal(SpinePhase.Telegraph, entered.From);
        Assert.True(entered.SecondsInFrom >= E.Stoker.BoardSeconds - 1e-6, $"{entered.SecondsInFrom:0.00} s");
        n.AssertFair();
    }

    [Fact]
    public void TheFiremanCatchesItAtTheTenderAndItStaysGoneForTheBreak()
    {
        // "If the fireman is at the firebox, they can catch it on the way in." Then: "it should stay gone for at least a
        // couple minutes after it's been defeated."
        var n = Standing(70);
        n.Crew[1] = AtTheFirebox(n.Train) with { Yaw = Math.PI }; // turned round at the scrape on the coal
        RunHot(n, 60, Tuning.Boiler.FireboxCapacity, until: () => StokerOf(n) is not null);
        var stoker = StokerOf(n)!;
        for (int i = 0; i < E.Stoker.BoardSeconds * SimConstants.TickRate && !stoker.Gone; i++)
            n.Run(1.0 / SimConstants.TickRate, _ => new PlayerIntent { Actions = PlayerActions.Swing }, holdSpeed: false);
        Assert.True(stoker.Gone, $"{stoker.Phase}, hp {stoker.Health}");
        Assert.DoesNotContain(n.Events, e => e.EnemyId == stoker.Id && e.To == SpinePhase.Commit);
        Assert.Equal(P.Health, n.Crew[1].Health); // not in the fire yet: no burn
        Assert.Equal(0, n.Train.Boiler.ExternalHeat);
        // The fire still hot, and nothing for the break; then the heat draws the next.
        double back = RunHot(n, E.Stoker.BreakSeconds + E.Stoker.HeatSeconds + 10, Tuning.Boiler.FireboxCapacity, until: () => StokerOf(n) is not null);
        Assert.InRange(back, E.Stoker.BreakSeconds + E.Stoker.HeatSeconds - 1, E.Stoker.BreakSeconds + E.Stoker.HeatSeconds + 2);
        Assert.InRange(E.Stoker.BreakSeconds, 120, 180);
    }

    [Fact]
    public void OnceInItEatsTheFireAndSwingsTheGaugeToARupture()
    {
        // "Its consequences of getting into the firebox should be larger and more urgent."
        var n = Standing(75);
        n.Train.Boiler.Firebox = 5;
        var stoker = n.World.AddEnemy(id => Stoker.InFirebox(id, n.Train, false, E.Stoker));
        n.Run(E.Stoker.SootSeconds + 0.1, holdSpeed: false);
        Assert.True(stoker.Feeding);
        double fire = n.Train.Boiler.Firebox;
        var heat = new List<double>();
        var gauge = new List<double>();
        double ruptured = double.NaN;
        for (int i = 0; i < 90 * SimConstants.TickRate && double.IsNaN(ruptured); i++)
        {
            n.Run(1.0 / SimConstants.TickRate, holdSpeed: false);
            heat.Add(n.Train.Boiler.ExternalHeat);
            gauge.Add(n.Train.Boiler.Pressure);
            if (n.Train.Boiler.Ruptured)
                ruptured = (i + 1) / (double)SimConstants.TickRate;
            if (i == 10 * SimConstants.TickRate)
                Assert.True(n.Train.Boiler.Firebox <= fire - E.Stoker.EatPerSecond * 10 + 1e-6, $"the fire {fire:0.00} -> {n.Train.Boiler.Firebox:0.00}");
        }
        // Lurching: the heat it puts in swings, so the gauge climbs in surges, never falling back (which would reset the
        // rupture's clock at the top).
        Assert.Contains(heat, h => h > E.Stoker.FeedRate + E.Stoker.Swing * 0.9);
        Assert.Contains(heat, h => h < E.Stoker.FeedRate - E.Stoker.Swing * 0.9);
        var rises = Enumerable.Range(0, 6).Select(k => gauge[(k + 1) * SimConstants.TickRate / 2] - gauge[k * SimConstants.TickRate / 2]).ToList();
        Assert.True(rises.Max() > 2 * rises.Min(), $"half-second rises {string.Join(", ", rises.Select(r => r.ToString("0.0")))}");
        // From the working band, gone in well under a minute: someone has to drop what they're doing.
        Assert.InRange(ruptured, 20, 45);
    }

    [Fact]
    public void AFireBurnsByTheSecondABrushHurtsAndStandingInItKills()
    {
        // Build 1121: "I died just touching it. I should get some sort of burn damage. If I stay in the fire too long, then
        // yeah, I die" (the director's decision of 6 Oct 2026).
        int car = 2;
        (Night N, CarFire Fire) Blaze()
        {
            var n = new Night(5, speed: 8);
            var fire = n.World.AddEnemy(id => CarFire.In(id, n.Train, car, 2, E.CarFire).Ablaze(1, 1.5));
            n.Train.Vehicles[car].Cargo = CargoKind.None;
            n.Run(3); // alight (past its telegraph), with nobody in the car yet
            return (n, fire);
        }
        var room = Blaze().N.Train.Frames[car].Shape.Interior!.Value;
        PlayerState In(CarFire f) => new() { Parent = car, Position = new Double3(room.Centre.X, room.Min.Y, f.Local.Z + 0.5), Surface = Surface.Deck, Health = P.Health };

        // A brush: half a second in it, then out of reach down the car.
        var (a, fa) = Blaze();
        a.Crew[1] = In(fa);
        a.Run(0.5);
        // Out of it: past the burning cells (note 267: 1.5 m either side of it) and out of reach of them.
        a.Crew[1] = a.Crew[1] with { Position = a.Crew[1].Position with { Z = fa.Local.Z - 1.5 - E.CarFire.CellSize - E.CarFire.BurnReach - 0.5 } };
        a.Run(5);
        int brushed = P.Health - a.Crew[1].Health;
        Assert.InRange(brushed, 1, 6);
        Assert.True(a.Crew[1].Alive);

        // Standing in it: hurt every second, dead in about ten.
        var (b, fb) = Blaze();
        b.Crew[1] = In(fb);
        double died = double.NaN;
        int last = P.Health;
        for (int i = 0; i < 30 * SimConstants.TickRate && double.IsNaN(died); i++)
        {
            b.Run(1.0 / SimConstants.TickRate);
            if (i % SimConstants.TickRate == SimConstants.TickRate - 1 && b.Crew[1].Alive)
            {
                Assert.True(b.Crew[1].Health < last, $"no burn in second {i / SimConstants.TickRate}");
                last = b.Crew[1].Health;
            }
            if (!b.Crew[1].Alive)
                died = (i + 1) / (double)SimConstants.TickRate;
        }
        Assert.InRange(died, 8, 15);
        Assert.Equal(DeathCause.Burned, b.Crew[1].Death);
    }

    [Fact]
    public void AFireWithNobodyNearKeepsBurning()
    {
        // Build 1121: "putting the fire out in one car douses it in the whole train". The director's linger rule (T114) put
        // out every fire nobody had been within 12 m of for two minutes, while the crew were fighting another.
        var n = new Night(5, speed: 8);
        var near = n.World.AddEnemy(id => CarFire.In(id, n.Train, 2, 2, E.CarFire));
        var far = n.World.AddEnemy(id => CarFire.In(id, n.Train, 4, 2, E.CarFire));
        var room = n.Train.Frames[2].Shape.Interior!.Value;
        n.Crew[1] = new PlayerState { Parent = 2, Position = new Double3(room.Centre.X, room.Min.Y, near.Local.Z + 6), Surface = Surface.Deck, Health = 100_000 };
        n.Run(E.Director.LingerSeconds + 30);
        Assert.False(far.Gone, $"{far.Phase} at {far.Extra:0.00}");
        Assert.False(near.Gone);
    }
}
