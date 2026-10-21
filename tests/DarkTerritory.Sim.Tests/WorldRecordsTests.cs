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

    [Theory]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(134)]
    [InlineData(200)]
    public void ARecordOfAnyLengthGoesAsADelta(int length)
    {
        // Note 921: the run's record is 9 fields and 25 a facility; past 64 it went whole every tick, a third of a client's
        // downlink on a route of three facilities. A field changed in each 64 costs a few bytes, wherever it is.
        uint key = WireRecord.MakeKey(RecordKind.Run, 0);
        var before = new WireRecord(key, [.. Enumerable.Range(0, length).Select(i => (long)i * 1_000_003)]);
        var fields = before.Fields.ToArray();
        fields[2] += 33_333;
        fields[^1] -= 7;
        var after = new WireRecord(key, fields);
        SameRecords([after], RoundTrip([after], [before], out int delta));
        RoundTrip([after], null, out int full);
        Assert.True(delta <= 24 && delta * 10 < full, $"{length} fields: delta {delta} B vs full {full} B");
    }

    [Fact]
    public void FullAndDeltaSnapshotsRoundTrip()
    {
        var train = Train();
        var players = new List<PlayerSnapshot> { new(1, PlayerMotor.SpawnInCab(train, P)), new(2, PlayerMotor.SpawnOnRoof(train, 4, 2, P)) };
        var a = WorldRecords.Capture(new World(train), new TrainControls { Throttle = 0.5, Reverser = 1 }, players);
        SameRecords(a, RoundTrip(a, null, out int full));

        train.Step(SimConstants.TickSeconds, new TrainControls { Throttle = 1, Reverser = 1 });
        train.Uncouple(5);
        players[1] = players[1] with { State = players[1].State with { Position = players[1].State.Position + new Double3(0, 0, -0.05) } };
        players.RemoveAt(0);
        var b = WorldRecords.Capture(new World(train), new TrainControls { Throttle = 1, Reverser = 1 }, players);
        SameRecords(b, RoundTrip(b, a, out int delta));
        Assert.True(delta < full / 3, $"delta {delta} B vs full {full} B");
    }

    [Fact]
    public void AnUnchangedWorldCostsAlmostNothing()
    {
        var train = Train();
        var a = WorldRecords.Capture(new World(train), default, []);
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
        var world = new World(train);
        var once = WorldRecords.Quantise(world, ref controls, players);
        var twice = WorldRecords.Quantise(world, ref controls, players);
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

    [Fact]
    public void AnEightCrewNightOnARouteStaysInTheBudget()
    {
        // Note 921: a route's night with its stops and creatures, over a lossy link, as `dt harness --bots 8 --enemies --route
        // frontier:7` measures it. Its run record is past 64 fields (three facilities and more), which went whole every tick
        // (25 kbit/s a client, and 81 in all over ten minutes); as a delta it's a few. The first minute is the yard's, the crew
        // loading stores by hand, so it's held to the peak budget (128); the night's typical (64) is note 921's ten-minute
        // measurement, 60.5.
        string content = DataFile.FindContentRoot();
        var route = LineGen.Routes.Generate(content, "frontier:7", 10);
        double yard = route.GateOr(Tuning.Route.YardLength);
        var r = Harness.Run(route.Build(), T, P, new HarnessOptions
        {
            Bots = 8,
            Cars = 10,
            Seconds = 60,
            Seed = 1,
            Link = new LinkConditions(0.06, 0.02, 0.03),
            StartDistance = Tuning.Run.DepartFrom(yard, Consist.Uniform(T, 10, 1).LengthMetres),
            Combat = Tuning.Combat,
            Enemies = Tuning.Enemies,
            Route = route,
            Run = Tuning.Run,
            Facilities = DataFile.Load<Run.FacilityTuning>(Path.Combine(content, Run.FacilityTuning.File)),
            YardLength = yard,
            Wire = true,
        }, Tuning.Boiler);
        Assert.True(r.Wire!["Run"] < 4, $"the run record: {r.Wire["Run"]} kbit/s");
        Assert.True(r.DownKbpsPerClient < 128, $"{r.DownKbpsPerClient} kbit/s: {string.Join(", ", r.Wire.Select(w => $"{w.Key} {w.Value}"))}");
    }
}
