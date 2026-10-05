using Ballast;
using Ballast.Audio;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The yard's voice on the radio (GDD §9; ARCHITECTURE §8 note 215): the clerk's word bank says every line the dispatcher
/// and the clerk read, a line at a time as it comes on, through the set; a name it doesn't have is the set breaking up.
/// </summary>
public class ClerkVoiceTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TrainTuning Trains = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
    static readonly BoilerTuning Boilers = DataFile.Load<BoilerTuning>(Path.Combine(Content, BoilerTuning.File));
    static readonly PlayerTuning Players = DataFile.Load<PlayerTuning>(Path.Combine(Content, PlayerTuning.File));

    static ClerkVoice Clerk() => new GameAudio(Content).Clerk;

    [Fact]
    public void NumbersAreSaidAsWords()
    {
        Assert.Equal("zero", ClerkVoice.Say(0));
        Assert.Equal("fourteen", ClerkVoice.Say(14));
        Assert.Equal("sixty three", ClerkVoice.Say(63));
        Assert.Equal("two hundred sixty three", ClerkVoice.Say(263));
        Assert.Equal("two thousand four hundred fifty", ClerkVoice.Say(2450));
        Assert.Equal("one thousand", ClerkVoice.Say(1000));
        Assert.Equal("minus three hundred fifty", ClerkVoice.Say(-350));
        Assert.Equal("one million two hundred thousand", ClerkVoice.Say(1_200_000));
    }

    [Fact]
    public void TheBankSaysEveryWordOfABotCrewsManifestAndTally()
    {
        // Everything Sim.Run.Radio can say with a bot crew aboard is in the bank (tools/audio/clerk.py): nothing breaks up.
        var clerk = Clerk();
        Assert.True(clerk.Speaks);
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(5_000)]));
        var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Trains, 4, 1)), line, 600, Boilers));
        for (int i = 0; i < Players.BotNames.Count; i++)
            world.Names[i] = Players.BotName(i);
        var report = new RunReport(RunEnd.Delivered, 1000, 12, 3, 1, 2, 2450, 40, 12, 30, -350, 3, 1, Scavenged: 75, Mail: 90)
        {
            ChildrenHome = 2,
            ChildPay = 400,
            Lines =
            [
                new ReportLine(IncidentKind.Death, Players.BotName(0), "", 350, 263),
                new ReportLine(IncidentKind.Death, Players.BotName(5), "", 350, 0),
            ],
        };
        var lines = Radio.Manifest(world, Enumerable.Range(0, Players.BotNames.Count + 1)).Concat(Radio.Tally(report))
            .Concat(Enum.GetValues<CargoKind>().Where(k => k != CargoKind.None).Select(k => $"Freight: {Cargoes.Name(k)}."))
            .Append("Child survivor: 1. Paid 200.");
        foreach (var said in lines)
            Assert.DoesNotContain(clerk.Pieces(said), p => p.Kind == ClerkVoice.PieceKind.Breakup);
        // "Crew 8" (a crewmate with no name) is said too.
        Assert.Contains(clerk.Pieces("Crew: Crew 8."), p => p is { Kind: ClerkVoice.PieceKind.Word, Text: "eight" });
    }

    [Fact]
    public void ANameAPlayerTypedIsTheSetBreakingUpOverIt()
    {
        var clerk = Clerk();
        var pieces = clerk.Pieces("Crew: Priya.");
        Assert.Equal([ClerkVoice.PieceKind.Word, ClerkVoice.PieceKind.Pause, ClerkVoice.PieceKind.Breakup], pieces.Select(p => p.Kind));
        Assert.Equal("crew", pieces[0].Text);
        Assert.Equal("priya", pieces[2].Text);
        // Phrases whole, the longest first: "body not recovered" is one take, not "body" and three more.
        Assert.Contains(clerk.Pieces("Sam. Body not recovered. Fee 350."), p => p is { Kind: ClerkVoice.PieceKind.Word, Text: "body not recovered" });
        // The clip runs as long as the line's said to (what the reading's timed by), within the takes' padding.
        var clip = clerk.Render("Crew: Priya. Coal: 412.")!;
        Assert.Equal(Audio.SampleRate, clip.SampleRate);
        Assert.InRange(clip.Seconds, clerk.Seconds("Crew: Priya. Coal: 412.") - 0.05, clerk.Seconds("Crew: Priya. Coal: 412.") + 0.1);
        Assert.True(clip.Samples.Max(MathF.Abs) > 0.1f);
    }

    [Fact]
    public void EachLineIsSaidOnceAsItComesOnTheAirThroughTheSet()
    {
        var audio = new GameAudio(Content);
        var tap = new MeterTap(Audio.SampleRate * 8);
        audio.Mixer.Tap = tap;
        string[] reading = ["Yard to consist. Manifest follows.", "Crew: Okafor.", "Coal: 412."];
        var started = new List<SoundInstance>();
        var seen = new HashSet<int>();
        void Run(int onAir, double seconds)
        {
            for (int b = 0; b < seconds * Audio.SampleRate / Audio.Block; b++)
            {
                audio.Radio(reading, onAir);
                audio.Mixer.Render(new float[Audio.Block * 2]);
                started.AddRange(audio.Mixer.Voices.Where(v => seen.Add(v.Id)));
            }
        }
        Run(1, 2.5);
        Assert.Single(started, v => v.Name == "radio-clerk");
        var first = Assert.Single(started, v => v.Name == "radio-clerk-voice");
        Assert.NotNull(first.Clip);
        Assert.InRange(first.Clip!.Seconds, 1, 3);
        Run(2, 1.5);
        Assert.Equal(2, started.Count(v => v.Name == "radio-clerk-voice"));
        // The voice well over the set's static: it's what the crew are to hear.
        double voice = Meter.Db(tap.Stems["radio-clerk-voice"].AsSpan(0, tap.Written));
        double hiss = Meter.Db(tap.Stems["radio-clerk"].AsSpan(0, tap.Written));
        Assert.True(voice > hiss + 12, $"voice {voice:0.0} dB, static {hiss:0.0} dB");
        // Off the air: quiet.
        audio.Radio(null, 0);
        audio.Mixer.Render(new float[Audio.Block * 2]);
        Assert.DoesNotContain(audio.Mixer.Voices, v => v.Name.StartsWith("radio-clerk", StringComparison.Ordinal) && !v.Finished);
    }
}
