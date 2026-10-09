using Ballast;
using Ballast.Dev;
using Ballast.Net;
using DarkTerritory.Dev.Replay;
using DarkTerritory.Game;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Dev.Tests;

/// <summary>
/// ARCHITECTURE §8 note 515: a night this machine hosts, recorded as it heard it, plays again tick for tick, and a replay
/// that parts from it says on which tick. One night is recorded for the class (a crew of four, the threats on) and each
/// test reads it.
/// </summary>
public class RecordReplayTests : IClassFixture<RecordReplayTests.Night>
{
    static readonly string Content = DataFile.FindContentRoot();

    /// <summary>Twenty seconds of frontier:7 hosted with three bots and the host's own player, recorded.</summary>
    public sealed class Night : IDisposable
    {
        public const int Seconds = 20;
        public readonly string Dir = Path.Combine(Path.GetTempPath(), "dt-replay-" + Guid.NewGuid().ToString("N"));
        public readonly string File;
        public readonly uint Ticks;
        public readonly double Distance;
        public readonly Dictionary<byte, PlayerState> Crew;
        public readonly int Enemies;
        public readonly (TimeSpan Spent, long Polls, long RawBytes) Cost;

        public Night()
        {
            var recorder = new NightRecorder(Dir);
            using (var night = NetPlaySession.HostGame(Content, new SessionSetup(Route: "frontier:7", Cars: 4, Enemies: true), port: 0, bots: 3, tap: recorder))
            {
                for (int t = 0; t < Seconds * SimConstants.TickRate; t++)
                    night.Step(default);
                var host = night.Host!;
                Ticks = host.Tick;
                Distance = host.Train.Dynamics.Distance;
                Crew = host.Players.ToDictionary(p => p.Id, p => p.State);
                Enemies = host.World.ActiveEnemies.Count;
            }
            File = recorder.Current!;
            Cost = recorder.Cost;
        }

        public void Dispose() => Directory.Delete(Dir, recursive: true);
    }

    readonly Night _night;

    public RecordReplayTests(Night night) => _night = night;

    [Fact]
    public void AHostedNightPlaysAgainTickForTick()
    {
        using var replay = NightReplay.Open(_night.File, Content);
        Assert.Empty(replay.ContentDifferences);
        replay.Run();
        Assert.Null(replay.DivergedAt);
        Assert.False(replay.Truncated);
        Assert.Equal(_night.Ticks, replay.Host.Tick);
        Assert.Equal((int)_night.Ticks, replay.Steps);
        // The same night to the bit: the train where it was, every crewmate where they stood.
        Assert.Equal(_night.Distance, replay.Host.Train.Dynamics.Distance);
        var crew = replay.Host.Players.ToDictionary(p => p.Id, p => p.State);
        Assert.Equal(_night.Crew.Keys.Order(), crew.Keys.Order());
        foreach (var (id, state) in _night.Crew)
        {
            Assert.Equal(state.Parent, crew[id].Parent);
            Assert.Equal(state.Position, crew[id].Position);
            Assert.Equal(state.Health, crew[id].Health);
        }
        Assert.Equal(_night.Enemies, replay.Host.World.ActiveEnemies.Count);
        Assert.Equal(4, crew.Count);
    }

    [Fact]
    public void ItStopsAtTheTickItsAskedFor()
    {
        using var replay = NightReplay.Open(_night.File, Content);
        replay.Run(tick: 300);
        Assert.Equal(300u, replay.Host.Tick);
        Assert.False(replay.Finished);
        Assert.Null(replay.DivergedAt);
    }

    [Fact]
    public void ARecordingThatPartsFromTheNightIsCaughtAtItsTick()
    {
        // The recording with one step's arrivals lost (as if the network had dropped them): the host's next sends differ.
        const int lost = 400;
        string tampered = Path.Combine(_night.Dir, "tampered" + TransportLog.Extension);
        var records = TransportLogReader.Read(_night.File).ToList();
        var header = (LogHeader)records[0];
        using (var log = new TransportLogWriter(System.IO.File.Create(tampered), header.Json))
        {
            int polls = 0;
            foreach (var r in records.Skip(1))
                switch (r)
                {
                    case LogPoll p:
                        var events = polls++ == lost ? new List<TransportEvent>() : [.. p.Events];
                        log.Polled(events, 0);
                        break;
                    case LogSent s:
                        // Kept as recorded: the digest's value is what's compared, so it's written back raw.
                        log.SentRaw(s.Digest, s.Count, s.Bytes);
                        break;
                    case LogNote n:
                        log.Note(n.Json);
                        break;
                }
        }
        using var replay = NightReplay.Open(tampered, Content);
        replay.Run();
        Assert.Equal((uint)lost, replay.DivergedAt);
    }

    [Fact]
    public void ARecordingCutOffByACrashPlaysToWhereItGot()
    {
        string cut = Path.Combine(_night.Dir, "cut" + TransportLog.Extension);
        var bytes = System.IO.File.ReadAllBytes(_night.File);
        System.IO.File.WriteAllBytes(cut, bytes[..(bytes.Length * 6 / 10)]);
        using var replay = NightReplay.Open(cut, Content);
        replay.Run();
        Assert.True(replay.Truncated);
        Assert.Null(replay.DivergedAt);
        Assert.InRange(replay.Steps, (int)_night.Ticks / 3, (int)_night.Ticks - 1);
    }

    [Fact]
    public void RecordingCostsTheHostMicrosecondsATick()
    {
        // Note 515's budget: what the host's thread spends recording, against the 33 ms of a tick (measured ~12 µs, Debug).
        double micros = _night.Cost.Spent.TotalMilliseconds * 1000 / _night.Cost.Polls;
        Assert.InRange(micros, 0, 250);
        Assert.Equal(_night.Ticks, (uint)_night.Cost.Polls);
        // And it's small: a crew of four's twenty seconds in well under a megabyte.
        Assert.InRange(new FileInfo(_night.File).Length, 1, 1 << 20);
    }

    [Fact]
    public void AVoiceFramesAudioIsBlankedItsLengthKept()
    {
        byte[] frame = [(byte)MessageType.Voice, 7, 0, 1, 0xDE, 0xAD, 0xBE, 0xEF];
        var scrubbed = NightRecorder.ScrubVoice(frame).ToArray();
        Assert.Equal(frame.Length, scrubbed.Length);
        Assert.Equal(frame[..4], scrubbed[..4]);
        Assert.All(scrubbed[4..], b => Assert.Equal(0, b));
        Assert.Equal(0xDE, frame[4]);
        byte[] input = [(byte)MessageType.Input, 1, 2, 3];
        Assert.True(NightRecorder.ScrubVoice(input).SequenceEqual(input));
        Assert.False(NightRecorder.Digested(frame));
        Assert.True(NightRecorder.Digested(input));
    }

    [Fact]
    public void TheHeaderCarriesWhatTheHostOnlyKnows()
    {
        var header = NightHeader.FromJson(((LogHeader)TransportLogReader.Read(_night.File).First()).Json);
        var setup = SessionSetup.Decode(header.Setup);
        Assert.Equal("frontier:7", setup.Route);
        Assert.Equal(4, setup.Cars);
        Assert.NotNull(setup.Content);
        Assert.Equal(4, header.Crew);
        Assert.False(header.Voice);
        Assert.Equal(Report.Build().Commit, header.Commit);
    }
}
