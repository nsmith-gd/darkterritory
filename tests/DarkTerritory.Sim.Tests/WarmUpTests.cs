using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Bots go in the way a person would (T31): in through a door, shut it, wait, out. Built for the cold (spec B.2, cut by note
/// 852); the way in is the same for shelter from the Choir, which these drive it with.
/// </summary>
public class WarmUpTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly PlayerTuning P = Tuning.Player;

    [Fact]
    public void AWalkerShelteringShutsASideDoorLeftOpenForLoading()
    {
        // Loading leaves a cargo car's side door open (T34); a car only shelters you shut, so whoever goes in shuts it.
        var line = RailLine.Load(Path.Combine(DataFile.FindContentRoot(), "lines/test-loop.json"));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 1)), line, 1500);
        var world = new World(train);
        world.Choir.Build = 1;
        const int car = 2;
        var shape = train.Frames[car].Shape;
        int side = shape.DoorList.First(d => Math.Abs(d.Box.Centre.Z) < 1).Index;
        train.Vehicles[car].ToggleDoor(side);
        var bot = new RoofWalkerBot(7);
        var s = new PlayerState
        {
            Parent = car,
            Position = new Double3(T.Geometry.Interior!.DoorX, T.Geometry.Interior.FloorHeight, 4),
            Surface = Surface.Deck,
            Health = P.Health,
            LineHint = train.Cars[car].FrontDistance,
        };
        for (uint tick = 0; tick < SimConstants.TickRate * 30 && train.Vehicles[car].DoorsOpen != 0; tick++)
        {
            world.BeginTick();
            var intent = bot.Decide(s, world, tick, out _);
            world.CrewAct(ref s, intent, 1);
            world.Step(new TrainControls { Brake = 1, Reverser = 1 });
            PlayerMotor.Step(ref s, intent, train, P, T, SimConstants.TickSeconds, applyLook: false);
        }
        Assert.Equal(0, train.Vehicles[car].DoorsOpen);
        Assert.True(PlayerMotor.Indoors(s, train), $"{s.Surface} on {s.Parent} at {s.Position}");
    }

    [Fact]
    public void TwoWalkersShelteringInOneCarGetItsDoorShut()
    {
        // The 100-night playtest: two walkers in from the cold stood at one open door, both pulled it, and each pull undid
        // the other's every time. They froze indoors, and the driver waited on them at the stop all night.
        var line = RailLine.Load(Path.Combine(DataFile.FindContentRoot(), "lines/test-loop.json"));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 1)), line, 1500);
        var world = new World(train);
        world.Choir.Build = 1;
        const int car = 2;
        int rear = train.Frames[car].Shape.DoorList.OrderBy(d => d.Box.Centre.Z).Last().Index;
        train.Vehicles[car].ToggleDoor(rear);
        train.Step(SimConstants.TickSeconds, new TrainControls { Brake = 1, Reverser = 1 });
        var bots = new[] { new RoofWalkerBot(3), new RoofWalkerBot(3) };
        var crew = bots.Select(_ => new PlayerState
        {
            Parent = car,
            Position = new Double3(T.Geometry.Interior!.DoorX, T.Geometry.Interior.FloorHeight, 2),
            Surface = Surface.Deck,
            Health = P.Health,
            LineHint = train.Cars[car].FrontDistance,
        }).ToArray();
        bool shut = false;
        for (uint tick = 0; tick < SimConstants.TickRate * 30 && !shut; tick++)
        {
            world.BeginTick();
            var intents = new PlayerIntent[crew.Length];
            for (int i = 0; i < crew.Length; i++)
            {
                intents[i] = bots[i].Decide(crew[i], world, tick, out _);
                world.CrewAct(ref crew[i], intents[i], i + 1);
            }
            world.Step(new TrainControls { Brake = 1, Reverser = 1 });
            for (int i = 0; i < crew.Length; i++)
                PlayerMotor.Step(ref crew[i], intents[i], train, P, T, SimConstants.TickSeconds, applyLook: false);
            shut = train.Vehicles[car].DoorsOpen == 0 && crew.All(c => PlayerMotor.Indoors(c, train));
        }
        Assert.True(shut, $"doors {train.Vehicles[car].DoorsOpen}");
    }

    /// <summary>
    /// Note 633 (the director, the inbox, 12 Oct: a lit car shut up keeps the Climbers out): warm, out of a lit car by its end
    /// door onto the plate, and the door shut behind it before it climbs. Left open, it was how note 629's packs got into the
    /// empty cars at the first stop. A dark car's left as it was: shut up dark, they'd force the roof.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OutOfALitCarAWalkerShutsTheDoorBehindIt(bool lit)
    {
        var line = RailLine.Load(Path.Combine(DataFile.FindContentRoot(), "lines/test-loop.json"));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 1)), line, 1500);
        var world = new World(train);
        const int car = 2;
        train.Vehicles[car].LampLit = lit;
        var bot = new RoofWalkerBot(3) { Me = 1, Crew = [] };
        var self = new PlayerState
        {
            Parent = car,
            Position = new Double3(T.Geometry.Interior!.DoorX, T.Geometry.Interior.FloorHeight, -1),
            Surface = Surface.Deck,
            Health = P.Health,
            LineHint = train.Cars[car].FrontDistance,
        };
        bool wentOut = false;
        double? up = null;
        for (uint tick = 0; tick < SimConstants.TickRate * 40 && up is null; tick++)
        {
            world.BeginTick();
            var intent = bot.Decide(self, world, tick, out _);
            world.CrewAct(ref self, intent, 1);
            world.Step(new TrainControls { Brake = 1, Reverser = 1 });
            PlayerMotor.Step(ref self, intent, train, P, T, SimConstants.TickSeconds, applyLook: false);
            wentOut |= train.Vehicles[car].DoorsOpen != 0;
            if (self.Surface == Surface.Roof)
                up = tick / (double)SimConstants.TickRate;
        }
        Assert.True(wentOut, "it never opened a door");
        Assert.True(up < 30, $"never up: {self.Surface} on {self.Parent} at {self.Position}, {bot.WarmUpStep}");
        if (lit)
            Assert.Equal(0, train.Vehicles[car].DoorsOpen);
        else
            Assert.NotEqual(0, train.Vehicles[car].DoorsOpen);
    }

    [Fact]
    public void TwoHandsOnADoorInOneTickMoveItOnce()
    {
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 3, 1)),
            RailLine.Load(Path.Combine(DataFile.FindContentRoot(), "lines/test-loop.json")), 1500);
        var v = train.Vehicles[1];
        v.ToggleDoor(0);
        v.ToggleDoor(0);
        Assert.True(v.DoorOpen(0));
        train.Step(SimConstants.TickSeconds, new TrainControls { Brake = 1, Reverser = 1 });
        v.ToggleDoor(0);
        Assert.False(v.DoorOpen(0));
    }
}

