using Ballast;
using Ballast.Audio;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// K1's #1038 heard (queue #519, note 786): each odd outcome once where it happens, by its own sound now AU1's are installed
/// (notes 730, 786); the high five's and the fist bump's own where the hands meet, once a pair; a sit and a lean. Each cue is
/// installed here as a short tone (the real takes come from tools/audio/install.py), so what plays can be counted off the mixer.
/// </summary>
public class OddSoundTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TrainTuning T = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
    static readonly PlayerTuning P = DataFile.Load<PlayerTuning>(Path.Combine(Content, PlayerTuning.File));
    static readonly CombatTuning C = DataFile.Load<CombatTuning>(Path.Combine(Content, CombatTuning.File));

    static readonly string[] Own =
    [
        "place-town.bell", "world-odd.well-splash", "world-odd.coin", "world-odd.wash", "world-odd.can-kick", "world-odd.bark",
        "world-odd.scarecrow", "world-odd.letter", "world-odd.painting", "world-odd.sooted", "world-odd.lurch",
        "crew-emotes.high-five", "crew-emotes.fist-bump", "crew-emotes.sit", "crew-emotes.lean",
        "crew-mishaps.bare-slap", "crew-mishaps.body-knock",
    ];

    sealed class Bench
    {
        readonly HashSet<int> _seen = [];
        public readonly World World;
        public readonly GameAudio Audio = new(Content);
        public (int Id, PlayerState State)[] Crew = [];

        public Bench()
        {
            World = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 1)), new RailLine(new LineDefinition("t", [new TrackSegment(20_000)])), 5_000), C);
            foreach (var s in Own)
                Audio.Bank.Add(s, new SoundDef(4, [new LayerDef(SourceKind.Sine, 0.3, Frequency: 440)], Duration: 0.1, MaxInstances: 16));
            Audio.PlayerTuning = P;
        }

        public TrainOnLine Train => World.Train;

        public List<(string Name, Double3 At)> Run(double seconds)
        {
            var heard = new List<(string, Double3)>();
            for (int i = 0; i < seconds * SimConstants.TickRate; i++)
            {
                World.BeginTick();
                World.Step(World.Controls);
                Audio.CrewStates = Crew;
                Audio.Update(World, World.Controls, Listener.At(Train.Frames[1].Origin + Double3.Up * 2, 0), exposed: true, SimConstants.TickSeconds);
                heard.AddRange(Audio.Mixer.Voices.Where(v => _seen.Add(v.Id) && Own.Contains(v.Name)).Select(v => (v.Name, v.Position)));
            }
            return heard;
        }
    }

    [Theory]
    [InlineData(OddKind.Bell, "place-town.bell")]
    [InlineData(OddKind.Splash, "world-odd.well-splash")]
    [InlineData(OddKind.Coin, "world-odd.coin")]
    [InlineData(OddKind.Wash, "world-odd.wash")]
    [InlineData(OddKind.Kick, "world-odd.can-kick")]
    [InlineData(OddKind.Answer, "world-odd.bark")]
    [InlineData(OddKind.Turned, "world-odd.scarecrow")]
    [InlineData(OddKind.Letter, "world-odd.letter")]
    [InlineData(OddKind.Painting, "world-odd.painting")]
    [InlineData(OddKind.Sooted, "world-odd.sooted")]
    [InlineData(OddKind.Lurch, "world-odd.lurch")]
    public void EachOddOutcomeIsHeardOnceWhereItHappensByItsOwnSound(OddKind kind, string own)
    {
        var b = new Bench();
        b.Run(0.2);
        var at = b.Train.Frames[1].Origin + new Double3(20, 0, 0);
        b.World.OddEvents.Add(new OddEvent(700_001, b.World.Tick, kind, -1, 0, at));
        var heard = b.Run(0.5);
        var (name, where) = Assert.Single(heard);
        Assert.Equal(own, name);
        Assert.True((where - at).Length < 0.5, $"{name} {(where - at).Length:0.0} m off");
        // Kept on the wire a while (note 730's keepSeconds), it's heard once, not each tick it's there.
        Assert.Empty(b.Run(1));
    }

    [Fact]
    public void HandsMeetingAreHeardByTheirOwnSoundOnceAPairAndASitAndALeanEach()
    {
        var b = new Bench();
        var roof = b.Train.Frames[1].Shape.RoofHeight;
        PlayerState On(double z) => PlayerMotor.StandUp(b.Train, 1, new Double3(0, roof, z), b.Train.Cars[1].FrontDistance, P);
        b.Crew = [(1, On(-0.5)), (2, On(0.5))];
        b.Run(0.2);
        // Both records carry the meet; the pair's heard once, on the lower id's.
        b.World.Emotes.Add(new EmoteEvent(800_001, b.World.Tick, 1, Emote.HighFive, b.World.Tick, 2));
        b.World.Emotes.Add(new EmoteEvent(800_002, b.World.Tick, 2, Emote.HighFive, b.World.Tick, 1));
        b.World.Emotes.Add(new EmoteEvent(800_003, b.World.Tick, 1, Emote.FistBump, b.World.Tick, 2));
        b.World.Emotes.Add(new EmoteEvent(800_004, b.World.Tick, 2, Emote.SitEdge));
        b.World.Emotes.Add(new EmoteEvent(800_005, b.World.Tick, 1, Emote.LeanOut));
        var heard = b.Run(0.5).Select(h => h.Name).Order().ToList();
        Assert.Equal(["crew-emotes.fist-bump", "crew-emotes.high-five", "crew-emotes.lean", "crew-emotes.sit"], heard);
    }
}
