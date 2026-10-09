using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 471 (queue #207; the director, 8 Oct 2026, on the test build: "Cinder Hounds were able to grab me through the car. I
/// was in the car, they were on top, they grabbed me while I was under them. That shouldn't happen. Also they overlap on each
/// other when on top of the car, they should pick their own spots to be."): a hound on a roof bites only who's out on the
/// train with it, and hounds coming aboard each take their own spot.
/// </summary>
public class HoundsAboardTests
{
    static readonly PlayerTuning P = Tuning.Player;
    static readonly Train.TrainTuning T = Tuning.Train;

    static CinderHound OnRoof(Night n, int car, double z)
    {
        var hound = n.World.AddEnemy(id => new CinderHound(id, id));
        hound.Restore(SpinePhase.Commit, 0, Tuning.Enemies.CinderHounds.Health, car, new Double3(0.6, n.Train.Frames[car].Shape.RoofHeight, z), 0, 0, 0.6, hound.Id, 0);
        return hound;
    }

    [Fact]
    public void AHoundOnTheRoofNeverBitesWhoeversInTheCarUnderIt()
    {
        var n = new Night(4, 10, enemies: HoundRunTests.Quiet);
        int car = 3;
        var room = n.Train.Frames[car].Shape.Interior!.Value;
        var hound = OnRoof(n, car, room.Centre.Z);
        // Inside, under it: in its old reach (4 m), straight down through the roof.
        n.Crew[1] = new PlayerState { Parent = car, Position = new Double3(room.Centre.X, room.Min.Y, room.Centre.Z), Surface = Surface.Deck, Health = P.Health };
        n.Run(Tuning.Enemies.CinderHounds.BiteEverySeconds * 4);
        Assert.Equal(P.Health, n.Crew[1].Health);
        // Up on the roof with it (wherever its patrol's taken it, note 472), it has them.
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, hound.Attached, hound.Local.Z + (hound.Local.Z > 0 ? -2 : 2), P);
        n.Run(Tuning.Enemies.CinderHounds.BiteEverySeconds + 1);
        Assert.True(n.Crew[1].Health < P.Health, "not bitten on the roof beside it");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AHoundAtTheFrontOfItsGroundNeverBitesWhoeversUncouplingTheGapAheadOfIt(bool onItsPlate)
    {
        // Note 528 (D1, for the director's counter; D1's frontier:7 seed 3): a hound bites and chases only who's on its ground,
        // the cars from its FrontCar back. The coupling gap ahead of that is out of reach: there the driver uncoupled, a hound
        // at the ground's front end beside it, and was held and mauled. On the car ahead's back plate, or its own front car's
        // front plate, a metre or two from it: never bitten. One step onto its roof, it has them.
        var n = new Night(4, 0, enemies: HoundRunTests.Quiet);
        int car = 2, ahead = n.Train.VehicleAhead(car);
        double half = T.Geometry.CarLength / 2;
        var hound = OnRoof(n, car, -half + 1.2);
        n.Run(SimConstants.TickSeconds);
        Assert.Equal(car, hound.FrontCar(n.Train, Tuning.Enemies.CinderHounds));
        var plate = new PlayerState
        {
            Parent = onItsPlate ? car : ahead,
            Surface = Surface.Coupler,
            Position = new Double3(T.Geometry.PlateX, T.Geometry.CouplerHeight, onItsPlate ? -half - 0.7 : half + 0.7),
            Yaw = Math.PI / 2,
            Health = P.Health,
        };
        for (int i = 0; i < Tuning.Enemies.CinderHounds.BiteEverySeconds * 4 * SimConstants.TickRate; i++)
        {
            n.Crew[1] = plate;
            n.Run(SimConstants.TickSeconds);
            plate = plate with { Health = n.Crew[1].Health };
        }
        Assert.Equal(P.Health, n.Crew[1].Health);
        // On its ground beside it (wherever its patrol's taken it), it has them.
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, hound.Attached, hound.Local.Z + (hound.Local.Z > 0 ? -2 : 2), P);
        n.Run(Tuning.Enemies.CinderHounds.BiteEverySeconds + 1);
        Assert.True(n.Crew[1].Health < P.Health, "not bitten on its roof beside it");
    }

    [Fact]
    public void HoundsComingAboardTheSameCarEachTakeTheirOwnSpot()
    {
        // A run of four from behind (no lane ahead or from the flank): all aboard the rear car, each its own spot on its roof.
        var e = HoundRunTests.Quiet with { Director = HoundRunTests.Quiet.Director with { Run = HoundRunTests.Quiet.Director.Run with { AheadEvery = 0, FlankEvery = 0, Size = [4, 4] } } };
        var n = new Night(4, 21, enemies: e);
        n.Crew[0] = PlayerMotor.SpawnInCab(n.Train, P);
        for (int s = 0; s < 300 && n.World.Director!.HoundRuns.Count == 0; s++)
            n.Run(1);
        int rear = n.Train.Dynamics.Consist.Vehicles[^1].Id;
        var aboard = new HashSet<CinderHound>();
        var hounds = new List<CinderHound>();
        for (int s = 0; s < 60 * 60 && aboard.Count < 4; s++)
        {
            n.Run(1.0 / 60);
            hounds = [.. n.World.ActiveEnemies.OfType<CinderHound>()];
            // Each where it came up, clear of every hound already on that roof (note 471).
            foreach (var a in hounds.Where(h => h.Attached >= 0 && aboard.Add(h)))
            {
                Assert.Equal(rear, a.Attached);
                foreach (var b in hounds.Where(b => b != a && b.Attached == a.Attached && b.Aboard != HoundMode.Leap))
                    Assert.True((a.Local - b.Local).Length >= 1.6 - 1e-9, $"{a.Local} and {b.Local}");
            }
        }
        Assert.Equal(4, aboard.Count);
        // And patrolling after (note 472), never on top of each other on a roof.
        for (int s = 0; s < 60 * 4; s++)
        {
            n.Run(0.25);
            foreach (var a in hounds)
                foreach (var b in hounds.Where(b => b != a && b.Attached == a.Attached && a.Aboard != HoundMode.Leap && b.Aboard != HoundMode.Leap))
                    Assert.True((a.Local - b.Local).Length >= 1.2, $"{a.Local} and {b.Local} on {a.Attached}");
        }
    }
}
