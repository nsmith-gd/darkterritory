using Ballast;
using Ballast.Audio;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The band heard (ARCHITECTURE §8 note 700): each crewmate playing their instrument's part of the host's tune, in time on the
/// host's clock, a late-comer in on the next bar; every instrument heard over the roof's wind, none drowning the others.
/// </summary>
public class BandAudioTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly BandTuning B = DataFile.Load<BandTuning>(Path.Combine(Content, BandTuning.File));

    [Fact]
    public void EachPlayerPlaysTheirPartOnTheHostsBeatAndALateComerComesInOnTheNextBar()
    {
        var tuning = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
        var player = DataFile.Load<PlayerTuning>(Path.Combine(Content, PlayerTuning.File));
        var line = RailLine.Load(Path.Combine(Content, "lines", "test-loop.json"));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(tuning, 4, 1)), line, 1200);
        var world = new World(train);
        world.EnableBand(B);
        int fiddle = B.IndexOf("fiddle"), drum = B.IndexOf("bodhran");
        // The host started the first tune on its tick 300; this machine's own world is on some other count.
        world.Band = new BandState(0, 300);
        world.Tick = 9;
        var score = B.Scores[0];
        var crew = new List<(int, PlayerState)>();
        foreach (var (id, which) in new[] { (1, fiddle), (2, drum) })
        {
            var held = world.Bodies.SpawnCrate(train, 2, new Double3(0, 3, id), Sim.Physics.BodyKind.Instrument);
            held.Instrument = (byte)which;
            held.Carrier = id;
            crew.Add((id, PlayerMotor.SpawnOnRoof(train, 2, 0, player)));
        }
        var audio = new GameAudio(Content);
        var ear = Listener.At(train.Frames[2].Origin, train.Frames[2].Heading);
        var heard = new Dictionary<string, List<double>> { ["band-fiddle"] = [], ["band-bodhran"] = [] };
        var seen = new HashSet<SoundInstance>(ReferenceEqualityComparer.Instance);
        int joinAt = 50;
        for (int k = 0; k <= 160; k++)
        {
            // The fiddler from the start; the drummer 50 ticks in, mid-bar.
            audio.CrewStates = [.. crew.Select(c => (c.Item1, c.Item1 == 1 || k >= joinAt ? c.Item2 with { Flags = c.Item2.Flags | PlayerFlags.Playing } : c.Item2))];
            audio.HostTick = 300 + k;
            audio.Update(world, new TrainControls { Reverser = 1 }, ear, exposed: true, SimConstants.TickSeconds);
            foreach (var v in audio.Mixer.Voices.Where(v => v.Name.StartsWith("band-", StringComparison.Ordinal) && seen.Add(v)))
                // Each note put down ahead of the clock by as far as it falls into the next tick.
                heard[v.Name].Add(k * SimConstants.TickSeconds - v.ClipSeconds);
        }
        double end = 161 * SimConstants.TickSeconds;
        static double[] Times(IEnumerable<BandNote> notes, double eighth, double from, double to) =>
            [.. notes.Select(n => n.At * eighth).Where(t => t >= from - 1e-9 && t < to - 1e-9).Distinct().Order()];
        // The fiddler: every note of the tune in order, on time to the sample.
        var tune = Times(score.Part(B.Instruments[fiddle]), score.EighthSeconds, 0, end);
        Assert.Equal(tune.Length, heard["band-fiddle"].Count);
        Assert.All(tune.Zip(heard["band-fiddle"].Order()), p => Assert.Equal(p.First, p.Second, 4));
        // The drummer: nothing till the bar after they started, then on the fiddler's beat.
        int bar = (int)Math.Ceiling(joinAt * SimConstants.TickSeconds / score.BarSeconds);
        var beat = Times(score.Part(B.Instruments[drum]), score.EighthSeconds, bar * score.BarSeconds, end);
        Assert.NotEmpty(beat);
        Assert.Equal(beat.Length, heard["band-bodhran"].Count);
        Assert.All(beat.Zip(heard["band-bodhran"].Order()), p => Assert.Equal(p.First, p.Second, 4));
        // The last stops: the band does.
        world.Band = BandState.Silent;
        audio.Update(world, new TrainControls { Reverser = 1 }, ear, exposed: true, SimConstants.TickSeconds);
        Assert.Null(audio.BandHeard);
    }

    [Fact]
    public void EveryInstrumentIsHeardOverTheRoofsWindAndNoneDrownsTheOthers()
    {
        // The whole band round a listener on the roof (`dt audio render --scenario band`), a reel and a jig.
        foreach (var tune in new[] { "0", "1" })
        {
            var (report, _) = AudioBench.Render(Content, "band", cars: 6, speed: 8, listenerCar: 3, seconds: 6, track: tune);
            double wind = 10 * Math.Log10(report.StemsDb.Where(s => s.Key.StartsWith("bed-wind.", StringComparison.Ordinal)).Sum(s => Math.Pow(10, s.Value / 10)) + 1e-30);
            var band = B.Instruments.Select(i => (i.Id, Db: report.StemsDb.GetValueOrDefault("band-" + i.Id, double.NegativeInfinity))).ToList();
            Assert.All(band, b => Assert.True(b.Db > wind + 3, $"tune {tune}: the {b.Id} at {b.Db} dB, the wind {wind:0.0}"));
            Assert.InRange(band.Max(b => b.Db) - band.Min(b => b.Db), 0, 14);
        }
    }

    static double Pitch(float[] note)
    {
        var x = note.Skip(Audio.SampleRate / 8).Take(Audio.SampleRate / 10).ToArray();
        int best = 0;
        double most = double.NegativeInfinity;
        for (int lag = Audio.SampleRate / 1200; lag < Audio.SampleRate / 150; lag++)
        {
            double sum = 0;
            for (int i = 0; i + lag < x.Length; i++)
                sum += x[i] * x[i + lag];
            if (sum > most)
                (most, best) = (sum, lag);
        }
        return (double)Audio.SampleRate / best;
    }

    [Fact]
    public void TheRecordingsArePlayedAtThePitchAskedAndEveryInstrumentHasThem()
    {
        // The director (14 Oct): "let's get recorded instruments in place instead of synthesized ones" (note 711).
        var manifest = BandSamples.Load(Content);
        Assert.NotNull(manifest);
        var audio = new GameAudio(Content);
        audio.Bank.Samples.InlineBytes = long.MaxValue;
        var recordings = new BandSamples(manifest, audio.Bank.Samples);
        Assert.All(B.Instruments, i => Assert.True(recordings.Recorded(i.Id), $"no recordings of the {i.Id}"));
        // The fiddle's A4 as recorded, and the B flat above it resampled off its nearest note: each at its pitch.
        foreach (var (midi, hz) in new[] { (69, 440.0), (70, 466.16), (62, 293.66) })
        {
            double heard = Pitch(recordings.Render("fiddle", midi, 0.5, 1));
            Assert.InRange(1200 * Math.Log2(heard / hz), -35, 35);
        }
        // A note's as long as it's held, and a breath; a stroke a take of its own.
        var held = recordings.Render("whistle", 74, 0.3, 1);
        Assert.InRange(held.Length / (double)Audio.SampleRate, 0.3, 0.45);
        Assert.NotEqual(recordings.Render("spoons", 0, 0.2, 1, take: 0), recordings.Render("spoons", 0, 0.2, 1, take: 1));
        Assert.True(recordings.Render("bodhran", 0, 0.2, 1).Max(Math.Abs) > recordings.Render("bodhran", 1, 0.2, 1).Max(Math.Abs) * 0.9);
    }

    [Fact]
    public void EveryInstrumentsNotesAreMadeAtThePitchTheyreAsked()
    {
        // A4 from each of the tune's instruments: its period where the note's most like itself a period on (autocorrelation,
        // past its attack), whatever its harmonics.
        foreach (var id in new[] { "fiddle", "whistle", "accordion", "banjo" })
        {
            var note = BandSynth.Render(id, 69, 0.5, 1);
            var x = note.Skip(Audio.SampleRate / 10).Take(Audio.SampleRate / 10).ToArray();
            int best = 0;
            double most = double.NegativeInfinity;
            for (int lag = Audio.SampleRate / 1000; lag < Audio.SampleRate / 300; lag++)
            {
                double sum = 0;
                for (int i = 0; i + lag < x.Length; i++)
                    sum += x[i] * x[i + lag];
                if (sum > most)
                    (most, best) = (sum, lag);
            }
            Assert.InRange((double)Audio.SampleRate / best, 434, 446);
            Assert.Equal(note, BandSynth.Render(id, 69, 0.5, 1));
        }
    }
}
