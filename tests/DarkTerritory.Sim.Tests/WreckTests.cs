using Ballast;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>T117 playtest ("a full on dramatic cinematic derailment ... the full physics carnage"): the train comes off.</summary>
public class WreckTests
{
    static World Derailed(double speed, int cars = 6)
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(2000), new TrackSegment(600, 300), new TrackSegment(2000)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, cars, 1)), line, 2250);
        train.Dynamics.Velocity = speed;
        var world = new World(train, Tuning.Combat) { WreckTuning = DataFile.Load<WreckTuning>(Path.Combine(DataFile.FindContentRoot(), WreckTuning.File)) };
        world.Derail("test");
        return world;
    }

    [Fact]
    public void OffTheRailsAtSpeedTheCarsPloughOnAndComeToRest()
    {
        var w = Derailed(22);
        var wreck = w.Train.Wreck!;
        var start = wreck.Bodies.Select(b => b.Origin).ToList();
        double spun = 0;
        for (int i = 0; i < 40 * SimConstants.TickRate && !wreck.Settled; i++)
        {
            w.Step(default);
            foreach (var b in wreck.Bodies)
            {
                Assert.True(double.IsFinite(b.Origin.X + b.Origin.Y + b.Origin.Z), "the wreck blew up");
                spun = Math.Max(spun, Math.Acos(Math.Clamp(Double3.Dot(b.Up, Double3.Up), -1, 1)));
            }
        }
        Assert.True(wreck.Settled, $"still moving after {wreck.Seconds:0} s");
        for (int i = 0; i < wreck.Bodies.Count; i++)
        {
            var b = wreck.Bodies[i];
            // On the ground: lying on it (on its side, its roof or its wheels), not in it, not hung over it.
            Assert.InRange(b.Origin.Y, -1.5, 4.5);
            Assert.True((b.Origin - start[i]).Length < 400, "flung off the map");
            // Its frame is the wreck's: whoever's aboard went with it.
            Assert.Equal(b.Origin.X, w.Train.Frames[b.Vehicle].Origin.X, 6);
        }
        // Ploughed on: the engine a long way off its line, the cars behind it piling up after (the rear ones may stop short).
        Assert.True((wreck.Bodies[0].Origin - start[0]).Length > 20, "the engine stopped dead");
        Assert.True(wreck.Bodies.Select((b, i) => (b.Origin - start[i]).Length).Average() > 15, "the train stopped dead");
        // Carnage: something went over (rolled past 35°), and the train came apart somewhere.
        Assert.True(spun > 35 * Math.PI / 180, $"nothing rolled (most {spun * 180 / Math.PI:0}°)");
        Assert.True(wreck.Snapped > 0 || wreck.Bodies.Any(b => Math.Abs(Double3.Dot(b.Back, w.Train.Wreck!.Bodies[0].Back)) < 0.8),
            "neither a coupling snapped nor a car jackknifed");
    }

    [Fact]
    public void AClientSeesTheHostsWreck()
    {
        // The host simulates it; the clients are sent the poses (a puppet), and their cars lie where the host's do.
        var host = Derailed(20);
        for (int i = 0; i < 3 * SimConstants.TickRate; i++)
            host.Step(default);
        var line = host.Train.Line;
        var client = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), line, 2250), Tuning.Combat);
        var controls = default(TrainControls);
        var records = Net.WorldRecords.Capture(host, controls, []);
        Net.WorldRecords.Apply(records, client, ref controls, []);
        Assert.NotNull(client.Train.Wreck);
        Assert.True(client.Train.Wreck!.Puppet);
        foreach (var f in host.Train.Frames)
        {
            var c = client.Train.Frames[f.Index];
            Assert.True((c.Origin - f.Origin).Length < 0.01, $"car {f.Index} at {c.Origin}, host's at {f.Origin}");
            Assert.True(Double3.Dot(c.Up, f.Up) > 0.999, $"car {f.Index} lies differently");
        }
    }
}
