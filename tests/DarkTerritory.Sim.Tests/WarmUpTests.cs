using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>Bots keep out of the cold the way a person would (T31, spec B.2): in through a door, shut it, wait, out.</summary>
public class WarmUpTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly PlayerTuning P = Tuning.Player;

    /// <summary>Walkers on the roofs of a standing train for a while, stepped as the host steps a player.</summary>
    static (List<PlayerState> Crew, TrainOnLine Train, double[] Coldest, bool[] Warmed, double[] ColdestAfterWarm) Night(int walkers, double seconds)
    {
        var line = RailLine.Load(Path.Combine(DataFile.FindContentRoot(), "lines/test-loop.json"));
        // No boiler model: a shut car is always warm (with one, it's warm while the boiler has steam).
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 6, 1)), line, 1500);
        var world = new World(train);
        var bots = Enumerable.Range(0, walkers).Select(i => new RoofWalkerBot(100 + i, P.Cold)).ToList();
        var crew = Enumerable.Range(0, walkers).Select(i => PlayerMotor.SpawnOnRoof(train, 2 + i, -2 + i, P)).ToList();
        var coldest = new double[walkers];
        var coldestAfter = new double[walkers];
        var warmed = new bool[walkers];
        for (uint tick = 0; tick < seconds * SimConstants.TickRate; tick++)
        {
            world.BeginTick();
            var intents = new PlayerIntent[walkers];
            for (int i = 0; i < walkers; i++)
            {
                intents[i] = bots[i].Decide(crew[i], world, tick, out _);
                var s = crew[i];
                world.CrewAct(ref s, intents[i], i + 1);
                crew[i] = s;
            }
            world.Step(new TrainControls { Brake = 1, Reverser = 1 });
            for (int i = 0; i < walkers; i++)
            {
                var s = crew[i];
                PlayerMotor.Step(ref s, intents[i], train, P, T, SimConstants.TickSeconds, applyLook: false);
                crew[i] = s;
                coldest[i] = Math.Max(coldest[i], s.Cold);
                if (warmed[i])
                    coldestAfter[i] = Math.Max(coldestAfter[i], s.Cold);
                if (s.Cold < 10 && tick > SimConstants.TickRate * 60)
                    warmed[i] = true;
            }
        }
        return (crew, train, coldest, warmed, coldestAfter);
    }

    [Fact]
    public void WalkersGoInBeforeTheColdSlowsThemAndComeBackOut()
    {
        // Twenty-five minutes on the roofs: longer than it takes to freeze to death out there.
        var (crew, train, coldest, warmed, after) = Night(walkers: 3, seconds: 1500);
        Assert.All(crew, s => Assert.True(s.Alive, $"died of {s.Death}"));
        for (int i = 0; i < crew.Count; i++)
        {
            Assert.True(warmed[i], $"walker {i} never got warm (coldest {coldest[i]:0} s)");
            // In before the onset (spec B.2: 600 s), never mind death.
            Assert.True(coldest[i] < P.Cold.OnsetSeconds, $"walker {i} got to {coldest[i]:0} s of cold");
        }
        // And they didn't stay in: out on the roofs again, getting cold again, after warming.
        Assert.Contains(after, a => a > 60);
        // Nobody cut the train on the way (Use on the coupler plate cuts it unless you're facing a door).
        Assert.Single(train.Rakes);
    }

    [Fact]
    public void AWalkerWarmingUpShutsASideDoorLeftOpenForLoading()
    {
        // Loading leaves a cargo car's side door open (T34); a car only warms you shut, so whoever goes in shuts it.
        var line = RailLine.Load(Path.Combine(DataFile.FindContentRoot(), "lines/test-loop.json"));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 1)), line, 1500);
        var world = new World(train);
        const int car = 2;
        var shape = train.Frames[car].Shape;
        int side = shape.DoorList.First(d => Math.Abs(d.Box.Centre.Z) < 1).Index;
        train.Vehicles[car].ToggleDoor(side);
        var bot = new RoofWalkerBot(7, P.Cold);
        var s = new PlayerState
        {
            Parent = car,
            Position = new Double3(T.Geometry.Interior!.DoorX, T.Geometry.Interior.FloorHeight, 4),
            Surface = Surface.Deck,
            Health = P.Health,
            Cold = P.Cold.OnsetSeconds * 0.8,
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
        Assert.True(PlayerMotor.NearHeat(s, train), $"{s.Surface} on {s.Parent} at {s.Position}");
    }

    [Fact]
    public void TwoWalkersWarmingUpInOneCarGetItsDoorShut()
    {
        // The 100-night playtest: two walkers in from the cold stood at one open door, both pulled it, and each pull undid
        // the other's every time. They froze indoors, and the driver waited on them at the stop all night.
        var line = RailLine.Load(Path.Combine(DataFile.FindContentRoot(), "lines/test-loop.json"));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 1)), line, 1500);
        var world = new World(train);
        const int car = 2;
        int rear = train.Frames[car].Shape.DoorList.OrderBy(d => d.Box.Centre.Z).Last().Index;
        train.Vehicles[car].ToggleDoor(rear);
        train.Step(SimConstants.TickSeconds, new TrainControls { Brake = 1, Reverser = 1 });
        var bots = new[] { new RoofWalkerBot(3, P.Cold), new RoofWalkerBot(3, P.Cold) };
        var crew = bots.Select(_ => new PlayerState
        {
            Parent = car,
            Position = new Double3(T.Geometry.Interior!.DoorX, T.Geometry.Interior.FloorHeight, 2),
            Surface = Surface.Deck,
            Health = P.Health,
            Cold = P.Cold.OnsetSeconds * 0.8,
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
            shut = train.Vehicles[car].DoorsOpen == 0 && crew.All(c => PlayerMotor.NearHeat(c, train));
        }
        Assert.True(shut, $"doors {train.Vehicles[car].DoorsOpen}");
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

