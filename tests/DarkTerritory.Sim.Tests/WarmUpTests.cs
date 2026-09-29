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
        // Ten minutes on the roofs: twice as long as it takes to freeze to death out there.
        var (crew, train, coldest, warmed, after) = Night(walkers: 3, seconds: 600);
        Assert.All(crew, s => Assert.True(s.Alive, $"died of {s.Death}"));
        for (int i = 0; i < crew.Count; i++)
        {
            Assert.True(warmed[i], $"walker {i} never got warm (coldest {coldest[i]:0} s)");
            // In before the onset (spec B.2: 200 s), never mind death.
            Assert.True(coldest[i] < P.Cold.OnsetSeconds, $"walker {i} got to {coldest[i]:0} s of cold");
        }
        // And they didn't stay in: out on the roofs again, getting cold again, after warming.
        Assert.Contains(after, a => a > 60);
        // Nobody cut the train on the way (Use on the coupler plate cuts it unless you're facing a door).
        Assert.Single(train.Rakes);
    }
}
