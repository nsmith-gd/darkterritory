using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// GDD App. D.9, bodies as loot: every death costs the crew its fee at settlement, a body brought home refunds most of it
/// (never all), what it was carrying goes back to stores, and a drop-out's body is kit only. Carried like hand loot, but
/// the last of the crew can climb with one. And D.14's "no farming": across randomised deaths and recoveries, the wallet
/// never ends higher than the same night with nobody dying.
/// </summary>
[Collection(nameof(LineGenTests))]
public class BodyLootTests
{
    static readonly Route.Route Line = Routes.Generate(DataFile.FindContentRoot(), "frontier:7", 6);
    static readonly PlayerTuning P = Tuning.Player;
    static readonly HoldoutTuning H = Tuning.Holdouts;
    static double PerCar => Tuning.Run.Economy.PerCar["frontier"];

    /// <summary>A night stopped at the terminus: everything aboard is delivered when the run steps.</summary>
    static HoldoutTests.Night AtTheTerminus() => new(front: Line.Length - 150);

    /// <summary>Someone dies aboard car <paramref name="car"/> (inside it), leaving a body there.</summary>
    static Body DieInside(HoldoutTests.Night n, int id, int car, BodyKind? holding = null)
    {
        var room = n.Train.Frames[car].Shape.Interior!.Value;
        var s = PlayerMotor.SpawnOnRoof(n.Train, car, 0, P) with { Position = new Double3(0, room.Min.Y + 0.1, room.Centre.Z), Surface = Surface.Deck };
        if (holding is { } kind)
            n.World.Bodies.SpawnCrate(n.Train, car, Double3.Zero, kind).Carrier = id;
        n.Crew[id] = s with { Health = 0, Death = DeathCause.Mauled };
        n.World.StepBodies([.. n.Crew.Select(c => (c.Key, c.Value))]);
        return n.World.Bodies.All.Last(b => b.Kind == BodyKind.Ragdoll && b.Owner == id);
    }

    static RunReport Settle(HoldoutTests.Night n)
    {
        n.Crew[1] = PlayerMotor.SpawnInCab(n.Train, P);
        n.Step(0.2, holdSpeed: 0);
        return n.World.Run!.Report!;
    }

    [Fact]
    public void EveryDeathIsAFeeAndABodyBroughtHomeRefundsMostOfIt()
    {
        var n = AtTheTerminus();
        var body = DieInside(n, 2, 2);
        var record = n.World.BodyRecords[body.Id];
        Assert.Equal(H.FeeShare * PerCar, record.Fee);
        var r = Settle(n);
        Assert.Equal(RunEnd.Delivered, r.End);
        Assert.Equal(record.Fee, r.CrewLossFees);
        Assert.Equal(record.Refund, r.BodyRefunds);
        Assert.Equal(1, r.BodiesDelivered);
        Assert.Equal(0, r.BodiesLost);
        Assert.Equal(r.Gross - r.CoalCost - r.AmmoCost - r.RepairCost - r.CrewLossFees + r.BodyRefunds, r.Net, 0);
        // D.9's table: a recovered Frontier death nets −87.
        Assert.Equal(-87, r.BodyRefunds - r.CrewLossFees);
    }

    [Fact]
    public void ABodyNotBroughtHomeCostsTheWholeFeeAndOneCutAwayGoesWithItsCar()
    {
        var n = AtTheTerminus();
        DieInside(n, 2, 4);
        // Cut away behind car 3: car 4 and its body aren't attached at arrival.
        n.Train.Uncouple(3);
        n.Step(3, holdSpeed: 0);
        var r = Settle(n);
        Assert.Equal(H.FeeShare * PerCar, r.CrewLossFees);
        Assert.Equal(0, r.BodyRefunds);
        Assert.Equal(1, r.BodiesLost);
    }

    [Fact]
    public void DyingTwiceLeavesTwoBodiesAndTwoFees()
    {
        var n = AtTheTerminus();
        DieInside(n, 2, 2);
        // Freed, then dead again.
        n.Crew[2] = PlayerMotor.SpawnOnRoof(n.Train, 3, 0, P);
        n.World.StepBodies([.. n.Crew.Select(c => (c.Key, c.Value))]);
        DieInside(n, 2, 3);
        Assert.Equal(2, n.World.Bodies.All.Count(b => b.Kind == BodyKind.Ragdoll && b.Owner == 2));
        var r = Settle(n);
        Assert.Equal(2 * H.FeeShare * PerCar, r.CrewLossFees);
        Assert.Equal(2, r.BodiesDelivered);
    }

    [Fact]
    public void ABodyKeepsItsKitAndItsBackInStoresWhenItsDelivered()
    {
        var n = AtTheTerminus();
        var body = DieInside(n, 2, 2, BodyKind.Crowbar);
        Assert.DoesNotContain(n.World.Bodies.All, b => b.Kind == BodyKind.Crowbar);
        Assert.Equal([BodyKind.Crowbar], n.World.BodyRecords[body.Id].Kit);
        var r = Settle(n);
        Assert.Equal(1, r.KitReturned[BodyKind.Crowbar]);
    }

    [Fact]
    public void ADropOutsBodyIsKitOnlyNoFeeNoRefund()
    {
        var n = AtTheTerminus();
        var room = n.Train.Frames[2].Shape.Interior!.Value;
        n.Crew[2] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P) with { Position = new Double3(0, room.Min.Y + 0.1, room.Centre.Z), Surface = Surface.Deck };
        n.World.Bodies.SpawnCrate(n.Train, 2, Double3.Zero, BodyKind.Wrench).Carrier = 2;
        n.World.DroppedOut(2, n.Crew[2]);
        n.Crew.Remove(2);
        var body = n.World.Bodies.All.Single(b => b.Kind == BodyKind.Ragdoll);
        Assert.True(n.World.BodyRecords[body.Id].DropOut);
        Assert.Contains(n.World.Deaths, d => d.Player == 2 && d.DropOut);
        var r = Settle(n);
        Assert.Equal(0, r.CrewLossFees);
        Assert.Equal(0, r.BodyRefunds);
        Assert.Equal(1, r.KitReturned[BodyKind.Wrench]);
    }

    [Fact]
    public void ABodyIsValuedAtItsRefundWhenEnemiesRankLoot()
    {
        var n = AtTheTerminus();
        var body = DieInside(n, 2, 2);
        Assert.Equal(n.World.BodyRecords[body.Id].Refund, n.World.LootValue(body));
        Assert.Equal(0, n.World.LootValue(n.World.Bodies.SpawnCrate(n.Train, 1, Double3.Zero, BodyKind.Lamp)));
    }

    [Fact]
    public void ABodyIsCarriedLikeLootButTheLastOfTheCrewCanClimbWithIt()
    {
        var n = new HoldoutTests.Night(front: 5000);
        n.Step(0.1);
        var body = DieInside(n, 3, 2);
        // Two alive: carrying a body is heavy (2.8 m/s, no ladders).
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, 1, 0, P);
        n.Crew[2] = PlayerMotor.SpawnOnRoof(n.Train, 1, 3, P);
        body.Carrier = 1;
        n.World.LivingCrew = 2;
        var s = n.Crew[1];
        n.World.BeginTick();
        n.World.LivingCrew = 2;
        n.World.CrewAct(ref s, default, 1);
        Assert.True(s.Has(PlayerFlags.Heavy));
        Assert.False(s.Has(PlayerFlags.SoloCarry));
        // The other dies: the one left can climb with it.
        n.World.LivingCrew = 1;
        n.World.CrewAct(ref s, default, 1);
        Assert.True(s.Has(PlayerFlags.Heavy));
        Assert.True(s.Has(PlayerFlags.SoloCarry));
        // And it's a quarter of the climb: on a ladder, pushing up, it goes at soloCarryClimb.
        var ladder = n.Train.Frames[2].Shape.Ladders[0];
        var on = new PlayerState { Parent = 2, Position = ladder.Foot with { Y = 1 }, Surface = Surface.Ladder, Health = 100, Flags = s.Flags };
        var up = new PlayerIntent { MoveZ = 1 };
        double y0 = on.Position.Y;
        PlayerMotor.Step(ref on, up, n.Train, P, Tuning.Train, 1.0 / 30);
        Assert.Equal(P.SoloCarryClimb / 30, on.Position.Y - y0, 6);
        var plain = new PlayerState { Parent = 2, Position = ladder.Foot with { Y = 1 }, Surface = Surface.Ladder, Health = 100 };
        PlayerMotor.Step(ref plain, up, n.Train, P, Tuning.Train, 1.0 / 30);
        Assert.Equal(P.LadderClimb / 30, plain.Position.Y - y0, 6);
    }

    /// <summary>
    /// D.14 "no farming": the same night, with and without deaths (random how many, where they fall, whether each body's
    /// brought home, some drop-outs among them). The cargo and the running costs are the night's; the deaths only ever take
    /// away.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void AcrossRandomDeathsAndRecoveriesTheWalletNeverEndsHigher(int seed)
    {
        var rng = new Random(seed);
        for (int night = 0; night < 12; night++)
        {
            double clean = Settle(AtTheTerminus()).Net;
            var n = AtTheTerminus();
            int deaths = rng.Next(1, 7);
            for (int i = 0; i < deaths; i++)
            {
                int id = 10 + i;
                int car = rng.Next(1, n.Train.Frames.Count);
                bool dropOut = rng.Next(4) == 0;
                if (dropOut)
                {
                    var room = n.Train.Frames[car].Shape.Interior;
                    var st = PlayerMotor.SpawnOnRoof(n.Train, car, 0, P);
                    if (room is { } r)
                        st = st with { Position = new Double3(0, r.Min.Y + 0.1, r.Centre.Z), Surface = Surface.Deck };
                    n.World.DroppedOut(id, st);
                }
                else if (n.Train.Frames[car].Shape.Interior is not null)
                    DieInside(n, id, car, rng.Next(3) == 0 ? BodyKind.Wrench : null);
                // Some bodies aren't brought home: left out on the line.
                if (rng.Next(3) == 0 && n.World.Bodies.All.LastOrDefault(b => b.Kind == BodyKind.Ragdoll) is { } left)
                    n.World.Bodies.Remove(left);
            }
            var r2 = Settle(n);
            Assert.True(r2.Net <= clean, $"seed {seed} night {night}: {r2.Net} with {deaths} deaths against {clean} with none");
            Assert.True(r2.BodyRefunds <= r2.CrewLossFees);
            if (r2.CrewLossFees > 0)
                Assert.True(r2.Net < clean);
        }
    }
}
