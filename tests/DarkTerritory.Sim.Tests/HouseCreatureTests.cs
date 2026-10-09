using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The house creatures (GDD §21; the director, 9 Oct 2026; ARCHITECTURE §8 notes 583–586), at frontier:7's first village:
/// the Lodger (its shriek, then a one-hit lunge; a shut door between stops it; it gives up past its pursuit radius), the
/// Householder (guests watched, never touched; a thing carried out unpaid is hunted; paid for, let go), the Hollow House (a
/// rumble, then the doors shut and it comes down on whoever's still inside; out in time and it's still), the Hanger (walk into
/// a strand and it has you; a friend prises you free), and which houses they live in.
/// </summary>
[Collection(nameof(LineGenTests))]
public class HouseCreatureTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly Route.Route Frontier = Routes.Generate(Content, "frontier:7", 6);
    static readonly EnemyTuning Quiet = Tuning.Enemies with
    {
        // Nothing but what each test puts in (the director's own spawns and the houses' own dwellers off).
        Director = Tuning.Enemies.Director with { Roster = ["lodger", "householder", "hollowHouse", "hanger"] },
        Dwellings = Tuning.Enemies.Dwellings with { Enabled = false },
    };
    static readonly PlayerIntent Use = new() { Buttons = PlayerButtons.Use };

    /// <summary>The train stood at the first stop with open houses, the stops' walls up; and that stop's one-door house nearest the line.</summary>
    static (Night Night, StopWalls.OpenHouse House) AtAHouse()
    {
        var f = Frontier.Features.First(x => x.Stop is { } s && s.Buildings.Any(b => b.Open));
        var n = new Night(4, speed: 0, route: Frontier, enemies: Quiet, front: f.Start + 60);
        var walls = StopWalls.Of(Frontier, n.Train.Line, tuning: Tuning.Run.Walls);
        n.Train.Walls = walls;
        int feature = Frontier.Features.ToList().IndexOf(f);
        var near = n.Train.Line.Sample(f.Start).Position;
        var house = walls.OpenHouses.Where(h => h.Feature == feature && walls.HouseDoors.Count(d => d.House == h.Index) == 1)
            .OrderBy(h => HouseWays.Flat(h.Middle - near)).First();
        return (n, house);
    }

    static StopWalls Walls(Night n) => n.Train.Walls!;
    static HouseDoor DoorOf(Night n, StopWalls.OpenHouse h) => Walls(n).HouseDoors.Single(d => d.House == h.Index);

    static PlayerState At(Night n, Double3 at)
    {
        double hint = n.Train.Dynamics.Distance;
        return PlayerMotor.SpawnOnGround(at with { Y = PlayerMotor.GroundAt(at, n.Train.Line, ref hint) }, n.Train.Line, hint, Tuning.Player);
    }

    /// <summary>Just inside the door, a step in.</summary>
    static Double3 InsideDoor(Night n, StopWalls.OpenHouse h, double inBy = 1.2) => DoorOf(n, h).At - DoorOf(n, h).Out * inBy;
    static Double3 OutsideDoor(Night n, StopWalls.OpenHouse h, double outBy = 3) => DoorOf(n, h).At + DoorOf(n, h).Out * outBy;

    static void Run(Night n, double seconds, Func<int, PlayerIntent>? intent = null)
    {
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
        {
            n.Run(SimConstants.TickSeconds, intent);
            n.World.StepBodies([.. n.Crew.Select(c => (c.Key, c.Value))]);
        }
    }

    // ---- The Lodger ----

    [Fact]
    public void TheLodgerSleepsTillSomeonesInItsHouseThenShrieksBeforeItLunges()
    {
        var (n, house) = AtAHouse();
        var lodger = n.World.AddEnemy(id => Lodger.In(id, house, Walls(n), Tuning.Enemies.Lodger));
        n.Crew[1] = At(n, OutsideDoor(n, house, 8));
        Run(n, 10);
        Assert.Equal(LodgerMode.Hidden, lodger.Mode);
        // In at the door: a moment, and it shrieks, and they're still alive through the shriek.
        n.Crew[1] = At(n, InsideDoor(n, house));
        Run(n, Tuning.Enemies.Lodger.NoticeSeconds + 0.2);
        Assert.Equal(LodgerMode.Shriek, lodger.Mode);
        Assert.Equal(1, lodger.Target);
        Run(n, Tuning.Enemies.Lodger.ShriekSeconds - 0.3);
        Assert.True(n.Crew[1].Alive);
        n.AssertFair();
    }

    [Fact]
    public void StoodStillWhereItLungesTheLodgerKillsInOneHit()
    {
        var (n, house) = AtAHouse();
        var lodger = n.World.AddEnemy(id => Lodger.In(id, house, Walls(n), Tuning.Enemies.Lodger));
        n.Crew[1] = At(n, lodger.Hide + (lodger.Hide - house.Middle).Normalized * -2.0);
        Run(n, 4);
        Assert.False(n.Crew[1].Alive);
        n.AssertFair();
    }

    [Fact]
    public void AShutDoorBetweenStopsTheLodgersLungeAndItHasToBreakIt()
    {
        var (n, house) = AtAHouse();
        var lodger = n.World.AddEnemy(id => Lodger.In(id, house, Walls(n), Tuning.Enemies.Lodger));
        n.Crew[1] = At(n, InsideDoor(n, house));
        Run(n, Tuning.Enemies.Lodger.NoticeSeconds + 0.2);
        Assert.Equal(LodgerMode.Shriek, lodger.Mode);
        // At the shriek: out of the door and shut it behind them.
        n.Crew[1] = At(n, OutsideDoor(n, house, 1.0));
        Walls(n).SetShut(DoorOf(n, house).Key, true);
        Run(n, 3);
        Assert.True(n.Crew[1].Alive);
        Assert.Contains(lodger.Mode, new[] { LodgerMode.Break, LodgerMode.Chase, LodgerMode.Shriek });
    }

    [Fact]
    public void PastItsPursuitRadiusTheLodgerGivesUpAndGoesBackToItsCorner()
    {
        var (n, house) = AtAHouse();
        var lodger = n.World.AddEnemy(id => Lodger.In(id, house, Walls(n), Tuning.Enemies.Lodger));
        n.Crew[1] = At(n, InsideDoor(n, house));
        Run(n, Tuning.Enemies.Lodger.NoticeSeconds + 0.2);
        // Gone, far off.
        n.Crew[1] = At(n, OutsideDoor(n, house, Tuning.Enemies.Lodger.PursuitRadius + 15));
        Run(n, 30);
        Assert.True(n.Crew[1].Alive);
        Assert.Equal(LodgerMode.Hidden, lodger.Mode);
        Assert.True(HouseWays.Flat(lodger.Local - lodger.Hide) < 0.1);
    }

    // ---- The Householder ----

    static (Night Night, StopWalls.OpenHouse House, Householder It, Body Thing) AtTheTable()
    {
        var (n, house) = AtAHouse();
        n.World.EnableBodies();
        var it = n.World.AddEnemy(id => Householder.In(id, house, Walls(n), Tuning.Enemies.Householder));
        // One of its things, lying by the back wall.
        var thing = n.World.Bodies.SpawnItem(house.Middle - (DoorOf(n, house).At - house.Middle).Normalized * 1.0, n.Train.Dynamics.Distance, BodyKind.RepairKit);
        Run(n, 1);
        return (n, house, it, thing);
    }

    [Fact]
    public void TheHouseholderWatchesItsGuestsAndNeverTouchesThem()
    {
        var (n, house, it, thing) = AtTheTable();
        Assert.Contains(thing.Id, it.Mine);
        n.Crew[1] = At(n, InsideDoor(n, house));
        Run(n, 20);
        Assert.True(n.Crew[1].Alive);
        Assert.Equal(HouseholderMode.Watch, it.Mode);
    }

    [Fact]
    public void ItsThingCarriedOutUnpaidTheHouseholderHuntsTheCarrier()
    {
        var (n, house, it, thing) = AtTheTable();
        n.Crew[1] = At(n, OutsideDoor(n, house, 4));
        thing.Carrier = 1;
        Run(n, Tuning.Enemies.Householder.RiseSeconds + 0.5);
        Assert.Equal(HouseholderMode.Hunt, it.Mode);
        Assert.Equal(1, (int)it.Extra);
        // Stood still with it, they're had, and with nobody to prise it off, killed.
        Run(n, 10 + Tuning.Enemies.Householder.GrabSeconds);
        Assert.False(n.Crew[1].Alive);
        n.AssertFair();
    }

    [Fact]
    public void PaidForWithSomethingLeftOnItsTableTheHouseholderLetsItGo()
    {
        var (n, house, it, thing) = AtTheTable();
        // Something of theirs left on its table.
        var pay = n.World.Bodies.SpawnItem(it.Table, n.Train.Dynamics.Distance, BodyKind.Toy);
        Run(n, 1);
        Assert.Equal(1, it.Credit);
        Assert.DoesNotContain(pay.Id, it.Mine);
        n.Crew[1] = At(n, OutsideDoor(n, house, 4));
        thing.Carrier = 1;
        Run(n, 10);
        Assert.Contains(it.Mode, new[] { HouseholderMode.Sit, HouseholderMode.Watch });
        Assert.True(n.Crew[1].Alive);
    }

    // ---- The Hollow House ----

    [Fact]
    public void SomeoneStillInsideWhenTheRumbleEndsTheHollowHouseShutsSinksAndComesDownOnThem()
    {
        var (n, house) = AtAHouse();
        var t = Tuning.Enemies.HollowHouse;
        var hollow = n.World.AddEnemy(id => HollowHouse.In(id, house));
        n.Crew[1] = At(n, house.Middle);
        Run(n, t.WaitSeconds + 0.2);
        Assert.Equal(HollowMode.Rumble, hollow.Mode);
        Run(n, t.RumbleSeconds + 0.5);
        Assert.True(Walls(n).Shut(DoorOf(n, house).Key));
        Run(n, t.SinkSeconds + 1.5);
        Assert.False(n.Crew[1].Alive);
        Assert.Equal(t.SinkDepth, hollow.Sunk, 6);
        Assert.Contains(hollow.Mode, new[] { HollowMode.Collapse, HollowMode.Settled });
        // It stays as it fell, shut.
        Walls(n).SetShut(DoorOf(n, house).Key, false);
        Run(n, 2);
        Assert.Equal(HollowMode.Settled, hollow.Mode);
        Assert.True(Walls(n).Shut(DoorOf(n, house).Key));
    }

    [Fact]
    public void OutBeforeTheRumbleEndsAndTheHollowHouseSettlesStill()
    {
        var (n, house) = AtAHouse();
        var t = Tuning.Enemies.HollowHouse;
        var hollow = n.World.AddEnemy(id => HollowHouse.In(id, house));
        n.Crew[1] = At(n, house.Middle);
        Run(n, t.WaitSeconds + 0.5);
        Assert.Equal(HollowMode.Rumble, hollow.Mode);
        // The ground's rumbling: out.
        n.Crew[1] = At(n, OutsideDoor(n, house, 5));
        Run(n, t.RumbleSeconds + 1);
        Assert.Equal(HollowMode.Still, hollow.Mode);
        Assert.False(Walls(n).Shut(DoorOf(n, house).Key));
        Assert.True(n.Crew[1].Alive);
    }

    // ---- The Hanger ----

    [Fact]
    public void KeepClearOfTheHangersStrandsAndNothingHappens()
    {
        var (n, house) = AtAHouse();
        var hanger = n.World.AddEnemy(id => Hanger.In(id, house, Tuning.Enemies.Hanger));
        // The floor furthest from any strand, inside.
        var plan = house.Plan;
        var clear = Enumerable.Range(0, 81).Select(i => house.World(plan.X + (i % 9 - 4) * plan.HalfX / 5, plan.Y + (i / 9 - 4) * plan.HalfY / 5))
            .Where(p => Walls(n).InHouse(house.Index, p + Double3.Up * 0.5))
            .MaxBy(p => hanger.StrandsAt.Min(s => HouseWays.Flat(s - p)));
        Assert.True(hanger.StrandsAt.Min(s => HouseWays.Flat(s - clear)) > Tuning.Enemies.Hanger.TouchWithin + 0.4);
        n.Crew[1] = At(n, clear);
        Run(n, 20);
        Assert.True(n.Crew[1].Alive);
        Assert.Equal(HangerMode.Hidden, hanger.Mode);
    }

    [Fact]
    public void WalkIntoAStrandAndTheHangerHasYouUpItTillAFriendPrisesYouFree()
    {
        var (n, house) = AtAHouse();
        var t = Tuning.Enemies.Hanger;
        var hanger = n.World.AddEnemy(id => Hanger.In(id, house, t));
        var strand = hanger.StrandsAt[0];
        n.Crew[1] = At(n, InsideDoor(n, house));
        Run(n, Tuning.Enemies.MinReactionSeconds + 0.2);
        n.Crew[1] = At(n, strand);
        double floor = n.Crew[1].Position.Y;
        Run(n, t.HaulSeconds + 0.5);
        Assert.Equal(1, (int)hanger.Extra);
        Assert.Equal(HangerMode.Hold, hanger.Mode);
        Assert.True(n.Crew[1].Position.Y > floor + t.HaulTo * 0.8, $"up {n.Crew[1].Position.Y - floor:0.00} m");
        // A friend beside them, holding Use: they're free, and it draws back into the roof.
        n.Crew[2] = At(n, strand + new Double3(0.8, 0, 0));
        Run(n, 0.5, id => id == 2 ? Use : default);
        Assert.Equal(-1, (int)hanger.Extra);
        Assert.True(n.Crew[1].Alive);
        Assert.Contains(hanger.Mode, new[] { HangerMode.Hit, HangerMode.Retreat });
        n.AssertFair();
    }

    [Fact]
    public void LeftOnTheStrandTheHangersOneDies()
    {
        var (n, house) = AtAHouse();
        var t = Tuning.Enemies.Hanger;
        var hanger = n.World.AddEnemy(id => Hanger.In(id, house, t));
        n.Crew[1] = At(n, InsideDoor(n, house));
        Run(n, Tuning.Enemies.MinReactionSeconds + 0.2);
        n.Crew[1] = At(n, hanger.StrandsAt[0]);
        Run(n, t.GrabSeconds + 1);
        Assert.False(n.Crew[1].Alive);
    }

    // ---- Which houses ----

    [Fact]
    public void AboutTheTiersShareOfOpenHousesHaveSomethingInThemNeverTheGauntsRoostAndAlwaysTheSame()
    {
        var walls = StopWalls.Of(Frontier, Frontier.Build(), tuning: Tuning.Run.Walls);
        var t = Tuning.Enemies.Dwellings;
        var houses = walls.OpenHouses.Where(h => h.Feature >= 0 && Frontier.Features[h.Feature].Stop is not null).ToList();
        var lives = houses.Select(h => Dwelling.Lives(Frontier, Frontier.Features[h.Feature].Stop!, h, t, null)).ToList();
        Assert.Equal(lives, houses.Select(h => Dwelling.Lives(Frontier, Frontier.Features[h.Feature].Stop!, h, t, null)).ToList());
        double share = lives.Count(k => k is not null) / (double)houses.Count;
        Assert.InRange(share, t.ShareFor(Frontier.Tier) - 0.15, t.ShareFor(Frontier.Tier) + 0.15);
        for (int i = 0; i < houses.Count; i++)
        {
            var stop = Frontier.Features[houses[i].Feature].Stop!;
            if (stop.Lairs.Any(l => l.Kind == Stops.LairKind.GauntRoost && l.Building == houses[i].Building))
                Assert.Null(lives[i]);
            if (lives[i] == EnemyKind.Householder)
                Assert.Contains(stop.Containers, c => c.Building == houses[i].Building);
        }
    }
}
