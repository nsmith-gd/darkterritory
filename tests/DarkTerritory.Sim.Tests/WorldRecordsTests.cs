using Ballast;
using Ballast.Net;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

public class WorldRecordsTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly PlayerTuning P = Tuning.Player;

    static TrainOnLine Train() =>
        new(new TrainDynamics(Consist.Uniform(T, 8, 1)), new RailLine(new LineDefinition("t", [new TrackSegment(30_000)])), 12_345.678, Tuning.Boiler);

    static List<WireRecord> RoundTrip(IReadOnlyList<WireRecord> current, IReadOnlyList<WireRecord>? baseline, out int bytes)
    {
        var w = new NetWriter();
        WorldRecords.WriteDelta(w, current, baseline);
        bytes = w.Length;
        var r = new NetReader(w.Written);
        return WorldRecords.ReadDelta(ref r, baseline);
    }

    static void SameRecords(IReadOnlyList<WireRecord> a, IReadOnlyList<WireRecord> b)
    {
        Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++)
            Assert.True(a[i].SameAs(b[i]), $"record {a[i].Key:X4} differs");
    }

    [Fact]
    public void FullAndDeltaSnapshotsRoundTrip()
    {
        var train = Train();
        var players = new List<PlayerSnapshot> { new(1, PlayerMotor.SpawnInCab(train, P)), new(2, PlayerMotor.SpawnOnRoof(train, 4, 2, P)) };
        var a = WorldRecords.Capture(train, new TrainControls { Throttle = 0.5, Reverser = 1 }, players);
        SameRecords(a, RoundTrip(a, null, out int full));

        train.Step(SimConstants.TickSeconds, new TrainControls { Throttle = 1, Reverser = 1 });
        train.Uncouple(5);
        players[1] = players[1] with { State = players[1].State with { Position = players[1].State.Position + new Double3(0, 0, -0.05) } };
        players.RemoveAt(0);
        var b = WorldRecords.Capture(train, new TrainControls { Throttle = 1, Reverser = 1 }, players);
        SameRecords(b, RoundTrip(b, a, out int delta));
        Assert.True(delta < full / 3, $"delta {delta} B vs full {full} B");
    }

    [Fact]
    public void AnUnchangedWorldCostsAlmostNothing()
    {
        var train = Train();
        var a = WorldRecords.Capture(train, default, []);
        RoundTrip(a, a, out int bytes);
        Assert.Equal(2, bytes);
    }

    [Fact]
    public void QuantisingTwiceChangesNothing()
    {
        var train = Train();
        train.Dynamics.Velocity = 13.3333333;
        var controls = new TrainControls { Throttle = 1 / 3.0, Reverser = 1 };
        var players = new List<PlayerSnapshot> { new(1, PlayerMotor.SpawnInCab(train, P) with { Yaw = Math.PI / 7 }) };
        var once = WorldRecords.Quantise(train, ref controls, players);
        var twice = WorldRecords.Quantise(train, ref controls, players);
        SameRecords(once, twice);
    }

    [Fact]
    public void EightPlayersOnATwentyCarTrainFitTheBandwidthBudget()
    {
        // ARCHITECTURE §6.2: ≤ 64 kbit/s down per client typical.
        var line = RailLine.Load(Path.Combine(DataFile.FindContentRoot(), "lines/test-loop.json"));
        var r = Harness.Run(line, T, P, new HarnessOptions { Bots = 8, Cars = 20, Seconds = 60 }, Tuning.Boiler);
        Assert.True(r.DownKbpsPerClient < 64, $"{r.DownKbpsPerClient} kbit/s");
        Assert.True(r.SnapshotBytes < 400, $"{r.SnapshotBytes} B");
    }
}
