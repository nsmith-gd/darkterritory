using Ballast;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>T117 playtest ("a full on dramatic cinematic derailment ... the full physics carnage"): the train comes off.</summary>
public class WreckTests
{
    static World Derailed(double speed, int cars = 6, double radius = 300)
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(2000), new TrackSegment(600, radius), new TrackSegment(2000)]));
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

    [Fact]
    public void AThirtyKilometreDerailGoesOverRatherThanGlidingOff()
    {
        // Note 330 (#69, the director, 7 Oct: "took a curve going into a yard at 30 kilometers an hour ... glided off the
        // rails"): the kick was a share of the speed, so at 8 m/s nothing went past 9°. Now the engine and most of the
        // train go over.
        var w = Derailed(8.3);
        var wreck = w.Train.Wreck!;
        var rolled = new double[wreck.Bodies.Count];
        for (int i = 0; i < 40 * SimConstants.TickRate && !wreck.Settled; i++)
        {
            w.Step(default);
            for (int k = 0; k < wreck.Bodies.Count; k++)
                rolled[k] = Math.Max(rolled[k], Math.Acos(Math.Clamp(Double3.Dot(wreck.Bodies[k].Up, Double3.Up), -1, 1)) * 180 / Math.PI);
        }
        Assert.True(wreck.Settled, "never came to rest");
        Assert.True(rolled[0] > 60, $"the engine stayed up ({rolled[0]:0}°)");
        Assert.True(rolled.Count(r => r > 35) * 2 > rolled.Length, $"most of the train stayed up ({string.Join(", ", rolled.Select(r => r.ToString("0")))})");
    }

    [Theory]
    [InlineData(300)]
    [InlineData(-300)]
    public void ABendTakenTooFastThrowsTheTrainToItsOutside(double radius)
    {
        // Note 578 (queue #310; the director, 9 Oct 2026: "we took a bend too fast and it derailed inwards"). Over a bend's speed the train goes
        // off the outside of the curve, with its momentum: the engine (first off) and the train as a whole end up further
        // from the bend's centre than the rails they left.
        var w = Derailed(22, radius: radius);
        var line = w.Train.Line;
        var wreck = w.Train.Wreck!;
        // The bend's centre: radius metres to the left of the rails (positive curves left), from the engine's place on them.
        var at = line.Sample(w.Train.Cars[0].FrontDistance - w.Train.Cars[0].Length / 2);
        var left = Double3.Cross(Double3.Up, at.Tangent).Normalized;
        var centre = at.Position + left * radius;
        double Out(Double3 p) => new Double3(p.X - centre.X, 0, p.Z - centre.Z).Length - Math.Abs(radius);
        // Where each car would be, had it stayed on: its distance from the centre is the radius.
        for (int i = 0; i < 3 * SimConstants.TickRate; i++)
            w.Step(default);
        var engine = wreck.Bodies.First(b => b.Vehicle == 0);
        Assert.True(Out(engine.Centre) > 3, $"the engine went {Out(engine.Centre):0.0} m outside the bend (negative: inside)");
        double mean = wreck.Bodies.Average(b => Out(b.Centre));
        Assert.True(mean > 0, $"the train went {mean:0.0} m outside the bend on average (negative: inside)");
        // Thrown out, not in: its sideways speed off the rails is outward.
        var outward = left * -Math.Sign(radius);
        Assert.True(Double3.Dot(engine.Centre - at.Position, outward) > 0, "the engine is on the inside of where it came off");
    }
}
