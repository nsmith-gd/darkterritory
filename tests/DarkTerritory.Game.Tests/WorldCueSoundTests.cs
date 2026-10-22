using Ballast;
using Ballast.Audio;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// Sound cues in the world heard (queue #466, note 732): a loose pin's moment heard as what jumped it (a run-in, a knock, a
/// bend), its knocking straight after; the house creatures' creaks and the Householder's shift, the far bell and the dog, each
/// once where the host made it. Each hook's own cue where it's installed, the stand-in where it isn't and one fits, nothing
/// where nothing does. The sounds are stand-ins, so what's installed doesn't change what's tested.
/// </summary>
public class WorldCueSoundTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TrainTuning T = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
    static readonly UpkeepTuning U = DataFile.Load<UpkeepTuning>(Path.Combine(Content, UpkeepTuning.File));
    static readonly CombatTuning C = DataFile.Load<CombatTuning>(Path.Combine(Content, CombatTuning.File));

    sealed class Bench
    {
        readonly HashSet<int> _seen = [];
        readonly HashSet<string> _under;
        public readonly World World;
        public readonly GameAudio Audio;
        public double Speed = 8;
        /// <summary>The car whose side the ear's beside (4 m out, at head height).</summary>
        public int EarCar = 2;

        public Bench(string[] installed, params string[] listen)
        {
            var rail = new RailLine(new LineDefinition("t", [new TrackSegment(60_000)]));
            World = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 6, 1)), rail, 5_000), C);
            World.Upkeep = U;
            World.Train.Dynamics.Velocity = Speed;
            Audio = new GameAudio(Content);
            _under = [.. listen];
            // What's under test is installed as a plain stand-in; a hook's own cue not in `installed` is taken out (AU1's are in
            // the content since note 785), so the stand-ins are what's heard without it.
            foreach (var own in Own)
                if (!installed.Contains(own))
                    Audio.Bank.Remove(own);
            foreach (var s in listen)
                if (installed.Contains(s) || Audio.HasCue(s))
                    Audio.Bank.Add(s, new SoundDef(5, [new LayerDef(SourceKind.Noise, 0.1)], Duration: 0.1, MaxInstances: 16));
        }

        public TrainOnLine Train => World.Train;

        public List<(string Name, Double3 At)> Run(double seconds)
        {
            var heard = new List<(string, Double3)>();
            for (int i = 0; i < seconds * SimConstants.TickRate; i++)
            {
                World.BeginTick();
                World.Step(new TrainControls { Reverser = 1 });
                World.Train.Dynamics.Velocity = Speed;
                var frame = Train.Frames[EarCar];
                Audio.Update(World, new TrainControls { Reverser = 1 }, Listener.At(frame.ToWorld(new Double3(4, 1.6, 0)), frame.Heading), exposed: true,
                    SimConstants.TickSeconds);
                heard.AddRange(Audio.Mixer.Voices.Where(v => _seen.Add(v.Id) && _under.Contains(v.Name)).Select(v => (v.Name, v.Position)));
            }
            return heard;
        }
    }

    /// <summary>Each hook's own cue (AU1's, note 785).</summary>
    static readonly string[] Own =
    [
        "state-coupling-loose.jump-brake", "state-coupling-loose.jump-coupled", "state-coupling-loose.jump-bend", "tell-house.creak",
        "tell-householder.shift", "world-bell.far", "world-dog.bark", Warn,
    ];

    const string Warn = "state-coupling-loose.warn", Knock = "state-coupling-loose.knock", PinLift = "crew-coupling.pin-lift", RunIn = "bed-slack.run-in", Frame = "bed-groan.creak";

    static Double3 PinOf(TrainOnLine train, int car) => train.Frames[car].ToWorld(Couplings.Pin(train.Frames[car].Shape, train.Dynamics.Tuning));

    [Theory]
    [InlineData(LooseCause.RunIn, "state-coupling-loose.jump-brake")]
    [InlineData(LooseCause.Knock, "state-coupling-loose.jump-coupled")]
    [InlineData(LooseCause.Bend, "state-coupling-loose.jump-bend")]
    public void APinsMomentIsHeardAsWhatJumpedItAtItsGapAndItsKnockingFollowsStraightOn(LooseCause cause, string own)
    {
        // At a stand, so where it's heard is where the pin is still (a loose pin's knocking goes on; its count holds).
        var b = new Bench([own, Knock], own, Knock, PinLift, RunIn, Frame) { Speed = 0 };
        b.Run(0.5);
        var v = b.Train.Vehicles[2];
        v.Loose = SimConstants.TickSeconds;
        v.LooseCause = cause;
        var heard = b.Run(1.5);
        // Its own sound, once, at the pin, and none of the stand-ins.
        var (_, at) = Assert.Single(heard, h => h.Name == own);
        Assert.True((at - PinOf(b.Train, 2)).Length < 1.5, $"{(at - PinOf(b.Train, 2)).Length:0.0} m off the pin");
        Assert.DoesNotContain(heard, h => h.Name == PinLift);
        // The knocking from straight after (a just-loose pin knocks 1.4 a second, its first at once).
        Assert.Contains(heard, h => h.Name == Knock);
        // Heard once: it's the moment, not the state.
        Assert.DoesNotContain(b.Run(3), h => h.Name == own);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void APinWorkingLooseIsHeardFromTheCabByItsOwnWarningAtThePinOrTheKnockPulledTowardTheEar(bool warn)
    {
        // Note 865's warning (#592), told the length of the train (note 980): a pin working loose four cars back from the engine,
        // heard from beside the engine. With its own long-carrying warn installed, that's what knocks, at the pin (its reach is
        // its own, out to 400 m); without it (a mod's), the ordinary knock, from no further than 45 m off the ear toward the gap.
        var b = new Bench(warn ? [Warn, Knock] : [Knock], Warn, Knock) { Speed = 0, EarCar = 0 };
        b.Run(0.5);
        var v = b.Train.Vehicles[4];
        v.Loose = SimConstants.TickSeconds;
        v.LooseCause = LooseCause.Working;
        var heard = b.Run(2);
        var pin = PinOf(b.Train, 4);
        var ear = b.Train.Frames[0].ToWorld(new Double3(4, 1.6, 0));
        Assert.True((pin - ear).Length > 50, $"the pin {(pin - ear).Length:0} m off");
        // About three knocks a second at the warning's start.
        var knocks = heard.Where(h => h.Name == (warn ? Warn : Knock)).ToList();
        Assert.InRange(knocks.Count, 4, 9);
        Assert.DoesNotContain(heard, h => h.Name == (warn ? Knock : Warn));
        if (warn)
            Assert.All(knocks, k => Assert.True((k.At - pin).Length < 1.5, $"{(k.At - pin).Length:0.0} m off the pin"));
        else
            Assert.All(knocks, k => Assert.InRange((k.At - ear).Length, 44, 46));
    }

    [Fact]
    public void ThePinsOwnWarningIsInstalledToCarryTheLengthOfTheTrain()
    {
        // Note 980: an alarm (tier 3, as the train's are), out to 400 m, so the cab hears a pin working loose at a twenty-car
        // rake's far end; and louder there than the ordinary knock would be pulled in to 45 m.
        var bank = new GameAudio(Content).Bank;
        var def = bank.Get(Warn)!;
        var knock = bank.Get(Knock)!;
        Assert.Equal(3, def.Tier);
        Assert.True(def.MaxDistance >= 300, $"reach {def.MaxDistance} m");
        // The mixer's distance gain (Mixer: (min/d)^rolloff past min), with the cue's own gain; the knock's pulled in at 1.2.
        static double At(SoundDef d, double m, double volume = 1) => volume * Math.Pow(Math.Min(1, d.MinDistance / m), d.Rolloff) * Math.Pow(10, d.GainDb / 20);
        Assert.True(At(def, 300) >= At(knock, 45, 1.2), $"{20 * Math.Log10(At(def, 300)):0.0} dB at 300 m against the pulled knock's {20 * Math.Log10(At(knock, 45, 1.2)):0.0} dB");
    }

    [Fact]
    public void TillEachCausesOwnSoundIsInTheTrainsOwnStandInAndTheAnswerIsStillTheCause()
    {
        List<string> Moment(LooseCause cause)
        {
            var b = new Bench([Knock], PinLift, RunIn, Frame);
            b.Run(0.5);
            b.Train.Vehicles[2].Loose = SimConstants.TickSeconds;
            b.Train.Vehicles[2].LooseCause = cause;
            return [.. b.Run(0.3).Select(h => h.Name)];
        }
        // A run-in: the slack slamming in at the gap and the pin lifting. A bend: the frame's creak and the pin. A knock: the
        // pin (the knuckles' clash is the coupling up's own).
        Assert.Equal([RunIn, PinLift], Moment(LooseCause.RunIn).Where(n => n != Frame).Order());
        Assert.Contains(Frame, Moment(LooseCause.Bend));
        Assert.Equal([PinLift], Moment(LooseCause.Knock));
    }

    [Fact]
    public void ALoosePinWithNoCauseOnItsRecordIsNoMomentOnlyTheKnock()
    {
        // A loose pin whose record says nothing of what loosed it (set by hand, as JobSoundTests does): no moment, the knock.
        var b = new Bench([Knock], PinLift, RunIn, Frame, Knock);
        b.Run(0.5);
        b.Train.Vehicles[2].Loose = 1;
        var heard = b.Run(2);
        Assert.DoesNotContain(heard, h => h.Name is PinLift or RunIn or Frame);
        Assert.Contains(heard, h => h.Name == Knock);
    }

    static WorldCue Made(World w, CueKind kind, Double3 at, byte of, int id) => new(id, w.Tick, kind, at, 7, of, 0.5);

    [Fact]
    public void EachCueInTheWorldIsHeardOnceWhereItIsByItsOwnSoundOrItsStandIn()
    {
        const string Creak = "tell-house.creak", Boards = "crew-footsteps.walk.wood", Chair = "cs-householder.creak", Shift = "tell-householder.shift",
            Bell = "world-bell.far", Dog = "world-dog.bark";
        var b = new Bench([Creak, Shift, Bell, Dog], Creak, Boards, Chair, Shift, Bell, Dog);
        b.Run(0.2);
        var at = b.Train.Frames[2].Origin + new Double3(15, 0, 0);
        void Say(WorldCue c)
        {
            b.World.Cues.Add(c);
            var heard = b.Run(0.5);
            var expected = c.Kind switch
            {
                CueKind.Creak when c.Of == (byte)EnemyKind.Householder => Chair,
                CueKind.Creak => Creak,
                CueKind.Shift => Shift,
                CueKind.Bell => Bell,
                _ => Dog,
            };
            var (name, where) = Assert.Single(heard);
            Assert.Equal(expected, name);
            Assert.True((where - c.At).Length < 0.5, $"{name} {(where - c.At).Length:0.0} m off");
        }
        Say(Made(b.World, CueKind.Creak, at, (byte)EnemyKind.Lodger, 900_001));
        Say(Made(b.World, CueKind.Creak, at, (byte)EnemyKind.Householder, 900_002));
        Say(Made(b.World, CueKind.Shift, at, (byte)EnemyKind.Householder, 900_003));
        Say(Made(b.World, CueKind.Bell, at + new Double3(300, 6, 0), (byte)BellFor.CutLost, 900_004));
        Say(Made(b.World, CueKind.Bark, at + new Double3(150, 0, 0), (byte)EnemyKind.Gaunt, 900_005));

        // Not installed yet: a board's creak is a step on wood, the Householder's shift its chair's creak; the bell and the dog
        // have nothing that's a bell or a dog to stand in, and stay quiet. (Each checked only while its own isn't in the content:
        // once its own is installed, the first half above is the test.)
        var bare = new Bench([], Creak, Boards, Chair, Shift, Bell, Dog);
        bare.Run(0.2);
        bare.World.Cues.Add(Made(bare.World, CueKind.Creak, at, (byte)EnemyKind.Hanger, 900_011));
        Assert.Equal([bare.Audio.HasCue(Creak) ? Creak : Boards], bare.Run(0.5).Select(h => h.Name));
        bare.World.Cues.Add(Made(bare.World, CueKind.Shift, at, (byte)EnemyKind.Householder, 900_012));
        Assert.Equal([bare.Audio.HasCue(Shift) ? Shift : Chair], bare.Run(0.5).Select(h => h.Name));
        bare.World.Cues.Add(Made(bare.World, CueKind.Bell, at, (byte)BellFor.HoldoutLeft, 900_013));
        bare.World.Cues.Add(Made(bare.World, CueKind.Bark, at, (byte)EnemyKind.Gaunt, 900_014));
        Assert.Equal(new[] { Bell, Dog }.Where(bare.Audio.HasCue), bare.Run(0.5).Select(h => h.Name).Order());
    }
}
