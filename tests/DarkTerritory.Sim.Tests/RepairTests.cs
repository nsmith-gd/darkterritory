using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 301 (queue #39; the director, 7 Oct, GDD App. F.3): the wrench is the repair tool, Sea of Thieves style, and every
/// break is called out where it is.
/// </summary>
public class RepairTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly PlayerTuning P = Tuning.Player;

    static World World(int cars = 4)
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(50_000)]));
        return new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(T, cars, 1)), line, 1_000, Tuning.Boiler));
    }

    /// <summary>On a car's floor in the aisle, abreast of its dent, the wrench in hand.</summary>
    static PlayerState AtTheDent(World world, int car)
    {
        var dent = Repairs.DentAt(world.Train.Frames[car].Shape)!.Value;
        return new PlayerState
        {
            Parent = car,
            Position = new Double3(T.Geometry.Interior!.DoorX, T.Geometry.Interior.FloorHeight, dent.Z),
            Surface = Surface.Deck,
            Health = P.Health,
            LineHint = world.Train.Cars[car].FrontDistance,
            Kit = P.StartingKit,
            HeldSlot = 1,
        };
    }

    static void Hold(World world, ref PlayerState s, double seconds, PlayerIntent? intent = null)
    {
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
        {
            world.BeginTick();
            world.CrewAct(ref s, intent ?? new PlayerIntent { Buttons = PlayerButtons.Use }, 1);
            world.Step(new TrainControls { Reverser = 1, Brake = 1 });
        }
    }

    [Fact]
    public void ABatteredCarIsMendedAtItsDentWithTheWrench()
    {
        var world = World();
        var train = world.Train;
        const int car = 2;
        train.Vehicles[car].Integrity = 0.5;
        Assert.True(Repairs.Dented(train, car));
        var s = AtTheDent(world, car);
        Assert.Equal(BreakKind.Dent, Repairs.At(s, train));
        // The crowbar in hand: nothing.
        s.HeldSlot = 0;
        Hold(world, ref s, 2);
        Assert.Equal(0.5, train.Vehicles[car].Integrity);
        // The wrench: integrityPerSecond back while it's worked, and whole in the end.
        s.HeldSlot = 1;
        Hold(world, ref s, 2);
        Assert.Equal(0.5 + 2 * T.Repair.IntegrityPerSecond, train.Vehicles[car].Integrity, 2);
        Hold(world, ref s, 0.5 / T.Repair.IntegrityPerSecond);
        Assert.Equal(1, train.Vehicles[car].Integrity, 6);
        Assert.False(Repairs.Dented(train, car));
        Assert.Equal(BreakKind.None, Repairs.At(s, train));
    }

    [Fact]
    public void WhatTheCarHuggerAteIsntMended()
    {
        var world = World();
        var v = world.Train.Vehicles[2];
        v.Eaten = 0.3;
        v.Integrity = 0.4;
        var s = AtTheDent(world, 2);
        Hold(world, ref s, 1 / T.Repair.IntegrityPerSecond);
        Assert.Equal(0.7, v.Integrity, 6);
    }

    [Fact]
    public void FarFromTheDentNothingsMended()
    {
        var world = World();
        world.Train.Vehicles[2].Integrity = 0.5;
        var s = AtTheDent(world, 2);
        s.Position = s.Position with { Z = s.Position.Z - 4 };
        Hold(world, ref s, 2);
        Assert.Equal(0.5, world.Train.Vehicles[2].Integrity);
    }

    [Fact]
    public void EveryBreakIsCalledOutWhereItIs()
    {
        var world = World(5);
        var train = world.Train;
        Assert.Empty(RepairCallouts.Of(train));
        train.Boiler.Ruptured = true;
        train.Vehicles[1].Integrity = 0.5;
        train.Vehicles[2].Integrity = T.Repair.DentedBelow + 0.05; // scuffed, not called out
        train.Vehicles[3].Breach(Breaches.EndWall(train.Frames[3].Shape)!.Value);
        var callouts = RepairCallouts.Of(train);
        Assert.Equal(
            [(BreakKind.Rupture, 0), (BreakKind.Dent, 1), (BreakKind.Breach, 3)],
            callouts.Select(c => (c.Kind, c.Vehicle)).ToArray());
        Assert.Equal(train.Vehicles[3].BreachAt, callouts[2].At);
        Assert.True(train.Frames[1].Shape.Interior!.Value.Contains(callouts[1].At));
    }
    [Fact]
    public void TheKitsGoneNoneStowedAndTheFortressSellsNone()
    {
        // Note 301, slice 2: the wrench is the repair tool, so the kit doesn't ride in the fitter's locker, spares or not.
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(50_000)]));
        var t = T with { Kit = T.Kit with { SpareKits = 2 } };
        var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(t, 4, 1)), line, 1_000, Tuning.Boiler));
        world.EnableBodies();
        world.Stock();
        Assert.DoesNotContain(world.Bodies.All, b => b.Kind == Physics.BodyKind.RepairKit);
        Assert.False(world.KitStocked);
        // campaign.json: no spare kits in the stores.
        var c = DataFile.Load<Campaign.CampaignTuning>(Path.Combine(DataFile.FindContentRoot(), Campaign.CampaignTuning.File));
        Assert.Null(c.SpareKit);
        Assert.NotNull(Campaign.Campaign.BuySpareKit(c, new Campaign.CampaignState { Scrip = 10_000 }).Refused);
    }
    [Fact]
    public void TheEngineBatteredByWhatItRanIntoIsMendedFromItsRunningBoard()
    {
        // Slice 2: what the engine runs into (the Sleepers, the line's debris) dents the front; it's mended at the boiler's
        // left flank, from the running board, not from the cab.
        var world = World();
        var train = world.Train;
        train.Vehicles[0].Integrity = 0.5;
        Assert.True(Repairs.Dented(train, 0));
        var dent = Repairs.DentAt(train, 0)!.Value;
        var callout = Assert.Single(RepairCallouts.Of(train));
        Assert.Equal((BreakKind.Dent, 0, dent), (callout.Kind, callout.Vehicle, callout.At));
        var board = new PlayerState
        {
            Parent = 0,
            Surface = Surface.Deck,
            Position = new Double3(dent.X - 0.35, dent.Y - 0.9, dent.Z),
            Health = P.Health,
            Kit = P.StartingKit,
            HeldSlot = 1,
        };
        Assert.False(PlayerMotor.InCab(board, train));
        Assert.Equal(BreakKind.Dent, Repairs.At(board, train));
        Hold(world, ref board, 2);
        Assert.Equal(0.5 + 2 * T.Repair.IntegrityPerSecond, train.Vehicles[0].Integrity, 2);
        // From the cab, nothing.
        var cab = PlayerMotor.SpawnInCab(train, P) with { HeldSlot = 1 };
        Assert.Equal(BreakKind.None, Repairs.At(cab, train));
    }
    [Fact]
    public void ASmashedHeadlampStaysOutTillTheWrenchMendsItFromTheCabsFrontWindows()
    {
        // Slice 2, the director (8 Oct): the smashed headlamp is the wrench's. It no longer comes back by itself.
        var world = World();
        world.EnableBodies(); // the host's (it relights the lamp)
        var train = world.Train;
        world.SmashLamp(Tuning.Enemies.Climbers.LampOutSeconds);
        var idle = PlayerMotor.SpawnInCab(train, P);
        Hold(world, ref idle, Tuning.Enemies.Climbers.LampOutSeconds + 1, new PlayerIntent());
        Assert.False(world.LampLit);
        Assert.True(Repairs.LampSmashed(train));
        // Called out on the cab's nose, and on the sill inside where it's mended from.
        Assert.Equal([(BreakKind.Lamp, 0), (BreakKind.Lamp, 0)], RepairCallouts.Of(train).Select(c => (c.Kind, c.Vehicle)).ToArray());
        // At the back of the cab (the fire door), nothing; at the front windows with the crowbar, nothing.
        var cab = train.Frames[0].Shape.Cab!.Value;
        var front = PlayerMotor.SpawnInCab(train, P) with { HeldSlot = 0 };
        front.Position = front.Position with { Z = PlayerMotor.CabFloorZ(train.Frames[0].Shape) };
        Assert.Equal(BreakKind.Lamp, Repairs.At(front, train));
        Hold(world, ref front, 2);
        Assert.Equal(Tuning.Enemies.Climbers.LampOutSeconds, world.LampOutSeconds, 6);
        // The wrench in hand: lampMendRate seconds of glass to each one worked, then it's lit again as the driver had it.
        front.HeldSlot = 1;
        Hold(world, ref front, Tuning.Enemies.Climbers.LampOutSeconds / T.Repair.LampMendRate - 0.5);
        Assert.False(world.LampLit);
        Hold(world, ref front, 0.6);
        Assert.Equal(0, world.LampOutSeconds);
        Assert.True(world.LampLit);
        Assert.Empty(RepairCallouts.Of(train));
    }
    [Fact]
    public void TheBotDriverMendsASmashedHeadlampFromTheControls()
    {
        // Slice 2: the driver stands at the front windows, where the lamp's mended; its wrench into hand, and the glass in.
        var world = World();
        world.EnableBodies();
        var train = world.Train;
        world.SmashLamp(Tuning.Enemies.Climbers.LampOutSeconds);
        var bot = new Bots.ConductorBot(new Bots.CrewCalls(), 0);
        var s = PlayerMotor.SpawnInCab(train, P);
        for (uint tick = 0; tick < (Tuning.Enemies.Climbers.LampOutSeconds / T.Repair.LampMendRate + 15) * SimConstants.TickRate
            && Repairs.LampSmashed(train); tick++)
        {
            var intent = bot.Decide(s, world, tick, out _);
            world.BeginTick();
            world.CrewAct(ref s, intent, 1);
            world.Step(new TrainControls { Reverser = 1, Brake = 1 });
            PlayerMotor.Step(ref s, intent, train, P, T, SimConstants.TickSeconds, applyLook: false);
        }
        Assert.False(Repairs.LampSmashed(train));
    }
}
