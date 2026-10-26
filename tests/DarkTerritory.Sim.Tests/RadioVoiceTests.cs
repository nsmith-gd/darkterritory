using Ballast.Net;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// A corrupted human on the crew's radio (GDD Part Eleven q4; queue #729, ARCHITECTURE §8 note 1040): off by default; on, the
/// Brakeman, hidden a while with nobody near him, says a few of a crewmate's own radio words back on the radio channel only,
/// to everyone with a set but that crewmate, once a night, and the night's report names it.
/// </summary>
public class RadioVoiceTests
{
    static readonly EnemyTuning E = Tuning.Enemies;
    static readonly PlayerTuning P = Tuning.Player;
    /// <summary>On, and quick: the words a second old will do, and a second hidden. Nothing else sent tonight.</summary>
    static readonly EnemyTuning On = E with
    {
        Director = E.Director with { GraceMinSeconds = 1e9, GraceMaxSeconds = 1e9 },
        Brakeman = E.Brakeman with { Radio = E.Brakeman.Radio with { On = true, MinAge = 1, HiddenFor = 1 } },
    };

    /// <summary>Up at the tail working toward it: nothing left to wind, he's straight down out of sight (hidden).</summary>
    static Brakeman HiddenAtTail(World world)
    {
        var b = world.AddEnemy(id =>
        {
            var last = world.Train.Cars[^1];
            return Brakeman.Up(id, world.Train, last.FrontDistance - last.Length + 0.5, -1, E.Brakeman);
        });
        return b;
    }

    /// <summary>Something said on the radio: <paramref name="frames"/> frames, each its own bytes (its index), at this tick on.</summary>
    static void Said(World world, int player, int frames, byte mark)
    {
        for (int i = 0; i < frames; i++)
            world.RadioVoices.Hear(player, [mark, (byte)i], world.Tick + (uint)(i * SimConstants.TickRate / RadioVoice.FramesPerSecond));
    }

    static Night Crewed(EnemyTuning tuning)
    {
        var n = new Night(6, speed: 10, enemies: tuning);
        n.Crew[1] = PlayerMotor.SpawnInCab(n.Train, P);
        n.Crew[2] = PlayerMotor.SpawnInCab(n.Train, P, 0.9);
        return n;
    }

    static List<(int, PlayerState)> CrewOf(Night n) => [.. n.Crew.Select(c => (c.Key, c.Value))];

    [Fact]
    public void ItsOffUntilTheDirectorSays()
    {
        Assert.False(E.Brakeman.Radio.On);
        var n = Crewed(E with { Director = On.Director });
        Said(n.World, 1, 100, 7);
        var b = HiddenAtTail(n.World);
        n.Run(E.Brakeman.ClimbSeconds + 5);
        Assert.Equal(BrakemanMode.Hidden, b.Mode);
        Assert.Null(RadioVoice.Step(n.World, CrewOf(n), _ => true));
        Assert.Empty(n.World.Attribution.Of(IncidentKind.Heard));
    }

    [Fact]
    public void HiddenAndFarHeSaysAFewOfACrewmatesOwnRadioWordsOnceANightAndTheReportNamesIt()
    {
        var n = Crewed(On);
        // Priya (2) said something on the radio; Dave (1) said nothing.
        Said(n.World, 2, 200, 9);
        n.Run(5);
        var b = HiddenAtTail(n.World);
        n.Run(E.Brakeman.ClimbSeconds + 0.5);
        Assert.Equal(BrakemanMode.Hidden, b.Mode);
        // Not yet hidden long enough.
        Assert.Null(RadioVoice.Step(n.World, CrewOf(n), _ => true));
        n.Run(On.Brakeman.Radio.HiddenFor + 0.5);
        var play = RadioVoice.Step(n.World, CrewOf(n), _ => true);
        Assert.NotNull(play);
        Assert.Equal(b.Id, play.Brakeman);
        Assert.Equal(2, play.Voice);
        // A few words: the start of what she said, cut to maxSeconds.
        Assert.Equal((int)(On.Brakeman.Radio.MaxSeconds * RadioVoice.FramesPerSecond), play.Frames.Count);
        Assert.Equal([9, 0], play.Frames[0]);
        Assert.Equal(On.Brakeman.Radio.MaxSeconds, play.Seconds, 3);
        // The report names it, in her voice.
        var line = Assert.Single(IncidentLog.Lines(n.World, 0, 0, _ => false), l => l.Kind == IncidentKind.Heard);
        Assert.Contains("The Brakeman on the radio", line.Text);
        Assert.Contains($"In {IncidentLog.NameOf(n.World, 2)}'s voice", line.Text);
        // Once a night: never again, however long he's hidden.
        Said(n.World, 1, 100, 3);
        n.Run(5);
        Assert.Null(RadioVoice.Step(n.World, CrewOf(n), _ => true));
        Assert.Single(n.World.Attribution.Of(IncidentKind.Heard));
    }

    [Fact]
    public void NeverNearNeverAnEchoAndNeverWithNobodyElseToHearIt()
    {
        var n = Crewed(On with { Brakeman = On.Brakeman with { Radio = On.Brakeman.Radio with { MinAge = 30 } } });
        Said(n.World, 2, 200, 9);
        var b = HiddenAtTail(n.World);
        n.Run(E.Brakeman.ClimbSeconds + 2);
        // Words a few seconds old: an echo, not yet.
        Assert.Null(RadioVoice.Choose(n.World, n.World.Enemies!.Brakeman.Radio, CrewOf(n), _ => true));
        n.Run(30);
        Assert.Equal(BrakemanMode.Hidden, b.Mode);
        var t = n.World.Enemies!.Brakeman.Radio;
        Assert.NotNull(RadioVoice.Choose(n.World, t, CrewOf(n), _ => true));
        // Nobody but her with a set: nobody to hear her voice.
        Assert.Null(RadioVoice.Choose(n.World, t, CrewOf(n), id => id == 2));
        // Dave by the tail, within farFrom of him: never near.
        var last = n.Train.Cars.Count - 1;
        var nearHim = PlayerMotor.SpawnOnRoof(n.Train, last, 0, P);
        Assert.True((PlayerMotor.WorldPosition(nearHim, n.Train) - b.WorldPosition(n.Train)).Length < t.FarFrom);
        Assert.Null(RadioVoice.Choose(n.World, t, [(1, nearHim), (2, n.Crew[2])], _ => true));
        // Alone (a crew of one has no friend's voice to hear), nothing.
        Assert.Null(RadioVoice.Choose(n.World, t, [(2, n.Crew[2])], _ => true));
    }

    [Fact]
    public void ThePickIsSeededTheSameNightSaysTheSameThing()
    {
        RadioVoicePlay? Night()
        {
            var n = Crewed(On);
            n.Crew[3] = PlayerMotor.SpawnInCab(n.Train, P, 0.5);
            Said(n.World, 1, 60, 1);
            Said(n.World, 2, 60, 2);
            Said(n.World, 3, 60, 3);
            HiddenAtTail(n.World);
            n.Run(E.Brakeman.ClimbSeconds + 2);
            return RadioVoice.Step(n.World, CrewOf(n), _ => true);
        }
        var a = Night();
        var b = Night();
        Assert.NotNull(a);
        Assert.Equal(a.Voice, b!.Voice);
        Assert.Equal(a.Tick, b.Tick);
        Assert.Equal(a.Frames.Select(f => f[0]), b.Frames.Select(f => f[0]));
    }

    static readonly RailLine Line = new(new LineDefinition("t", [new TrackSegment(40_000)]));
    static TrainOnLine Train() => new(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), Line, 2_000);

    [Fact]
    public void OverTheWireItsOnTheRadioOnlyInHerVoiceToEveryoneButHer()
    {
        var net = new LoopbackNetwork();
        var host = new HostSession(net.CreateHost(), Train(), Tuning.Train, P);
        host.World.EnableEnemies(On, null, 1, crew: 3, authority: true);
        var priya = new ClientSession(net.CreateClient(), Train(), Tuning.Train, P);
        var dave = new ClientSession(net.CreateClient(), Train(), Tuning.Train, P);
        var mo = new ClientSession(net.CreateClient(), Train(), Tuning.Train, P);
        void Step(int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                net.Advance(SimConstants.TickSeconds);
                host.Step();
                priya.Step(default);
                dave.Step(default);
                mo.Step(default);
            }
        }
        Step(30);
        foreach (var c in new[] { priya, dave, mo })
            host.SetPlayerState(c.PlayerId!.Value, PlayerMotor.SpawnInCab(host.Train, P, 0.3 * c.PlayerId!.Value));
        // Priya on the radio: a second of it (heard by the others as herself, on the radio).
        for (ushort i = 0; i < 50; i++)
        {
            priya.SendVoice(i, radio: true, new byte[] { 5, (byte)i });
            if (i % 2 == 1)
                Step(1);
        }
        Step(10);
        dave.VoiceFrames.Clear();
        mo.VoiceFrames.Clear();
        priya.VoiceFrames.Clear();
        var b = HiddenAtTail(host.World);
        // Hidden, then a few seconds of the voice's pace for the words to go out.
        Step((int)((E.Brakeman.ClimbSeconds + On.Brakeman.Radio.HiddenFor + 4) * SimConstants.TickRate));
        var said = host.World.RadioVoiceSaid;
        Assert.NotNull(said);
        Assert.Equal(priya.PlayerId!.Value, said.Voice);
        foreach (var listener in new[] { dave, mo })
        {
            var frames = listener.VoiceFrames.Where(f => f.Path.HasFlag(VoicePath.Mimic)).ToList();
            // Every frame of it, in order, from him, in her voice, on the radio and nowhere else.
            Assert.Equal(said.Frames.Count, frames.Count);
            Assert.All(frames, f => Assert.Equal(VoicePath.Mimic | VoicePath.Radio, f.Path));
            Assert.All(frames, f => Assert.Equal(b.Id, f.Source));
            Assert.All(frames, f => Assert.Equal(priya.PlayerId!.Value, f.Speaker));
            Assert.Equal(said.Frames.Select(f => f[1]), frames.Select(f => f.Opus[1]));
        }
        // She doesn't hear herself.
        Assert.DoesNotContain(priya.VoiceFrames, f => f.Path.HasFlag(VoicePath.Mimic));
        Assert.Equal(2L * said.Frames.Count, host.RadioVoiceFramesSent);
    }
}
