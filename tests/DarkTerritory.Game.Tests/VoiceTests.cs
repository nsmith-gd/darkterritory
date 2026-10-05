using Ballast;
using Ballast.Audio;
using Ballast.Net;
using Ballast.Voice;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>Spec A.5 end to end: microphone samples through host routing, Opus and the listener's mixer.</summary>
public class VoiceTests
{
    static readonly string Content = DataFile.FindContentRoot();

    [Fact]
    public void NearVoicesAreClearAndFallOffToNothingAt26Metres()
    {
        var near = VoiceBench.Run(Content, speakerCar: 3, speakerZ: 4, radio: false);
        var mid = VoiceBench.Run(Content, speakerCar: 4, speakerZ: 1, radio: false);
        var far = VoiceBench.Run(Content, speakerCar: 5, speakerZ: 0, radio: false);
        Assert.Equal(near.FramesSent, near.FramesHeard);
        Assert.True(near.NearDb > -20, $"4 m is {near.NearDb} dB");
        // Logarithmic 8–26 m: 16.5 m keeps 39% of full level, about −8 dB.
        Assert.InRange(near.NearDb - mid.NearDb, 6.5, 10);
        // Past the cutoff the host doesn't even send it.
        Assert.True(far.DistanceM > 26);
        Assert.Equal(0, far.FramesHeard);
    }

    [Fact]
    public void TheRadioCarriesTheLengthOfTheTrainBandLimited()
    {
        var r = VoiceBench.Run(Content, speakerCar: 9, speakerZ: 0, radio: true);
        Assert.True(r.DistanceM > 80);
        Assert.Equal(-180, r.NearDb);
        Assert.True(r.RadioBandDb > -15, $"radio band {r.RadioBandDb} dB");
        // 300 Hz–3 kHz: the low end of the voice is gone.
        Assert.True(r.RadioLowDb < r.RadioBandDb - 30, $"low {r.RadioLowDb} vs band {r.RadioBandDb}");
    }

    [Fact]
    public void TalkingDucksTheBedSixDecibels()
    {
        // Spec A.3 tier 2: "ducks bed −6 dB while active".
        Assert.InRange(VoiceBench.Run(Content, speakerCar: 3, speakerZ: 4, radio: false).BedDuckDb, -6.3, -5.7);
    }

    [Fact]
    public void InATunnelVoiceIsCompressedCloseAndRingsOn()
    {
        // GDD §22: "Tunnels: compressed proximity voice, no exterior reference." 4 m against 16.5 m is about 8 dB in the
        // open (spec A.5's curve); in the bore the far voice comes up close to the near one, and the tunnel answers both.
        var near = VoiceBench.Run(Content, speakerCar: 3, speakerZ: 4, radio: false);
        var mid = VoiceBench.Run(Content, speakerCar: 4, speakerZ: 1, radio: false);
        var tunnelNear = VoiceBench.Run(Content, speakerCar: 3, speakerZ: 4, radio: false, space: "tunnel");
        var tunnelMid = VoiceBench.Run(Content, speakerCar: 4, speakerZ: 1, radio: false, space: "tunnel");
        Assert.Equal(("outside", "tunnel"), (near.Space, tunnelNear.Space));
        double open = near.NearDb - mid.NearDb, bore = tunnelNear.NearDb - tunnelMid.NearDb;
        Assert.True(bore < open - 3, $"4 m against 16.5 m: {open} dB in the open, {bore} dB in the tunnel");
        Assert.True(tunnelMid.NearDb > mid.NearDb + 3, $"16.5 m: {mid.NearDb} dB in the open, {tunnelMid.NearDb} dB in the tunnel");
        // After they stop talking the tunnel still rings with it; the open night doesn't.
        Assert.Equal(-180, near.TailDb);
        Assert.True(tunnelNear.TailDb > -60, $"tail {tunnelNear.TailDb} dB");
        // Still a voice: talking ducks the bed as it always does.
        Assert.InRange(tunnelNear.BedDuckDb, -6.3, -5.7);
    }

    /// <summary>
    /// GDD App. D.7 end to end: a dead crewmate waiting in a halt's Holdout talks (Live Mic on or off), and a living one
    /// stands <paramref name="metres"/> from its door. How many frames reached the living, where their voice played from,
    /// the door, and how loud it was in the voice band.
    /// </summary>
    static (int Heard, Double3? From, Double3 Inside, double Db) LiveMic(bool on, double metres = 3)
    {
        var trains = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
        var players = DataFile.Load<PlayerTuning>(Path.Combine(Content, PlayerTuning.File));
        var boilers = DataFile.Load<BoilerTuning>(Path.Combine(Content, BoilerTuning.File));
        var holdouts = DataFile.Load<HoldoutTuning>(Path.Combine(Content, HoldoutTuning.File));
        var routes = RouteTuning.Load(Content);
        var (route, site) = Enumerable.Range(1, 60).Select(seed => RouteGenerator.Generate(routes, RouteTier.Frontier, (ulong)seed))
            .Select(r => (r, r.Features.FirstOrDefault(f => f.Kind == FeatureKind.Village && f.Stop is { Holdouts.Count: 1 })))
            .First(x => x.Item2 is not null);
        TrainOnLine Train() => new(new TrainDynamics(Consist.Uniform(trains, 3, 0)), route.Build(), site!.Start - 300, boilers);
        var net = new LoopbackNetwork(1, LinkConditions.Perfect);
        var host = new HostSession(net.CreateHost(), Train(), trains, players);
        var speaker = new ClientSession(net.CreateClient(), Train(), trains, players);
        var listener = new ClientSession(net.CreateClient(), Train(), trains, players);
        void Tick(PlayerIntent said = default)
        {
            net.Advance(SimConstants.TickSeconds);
            host.Step();
            speaker.Step(said);
            listener.Step(default);
        }
        for (int i = 0; i < 20 && (speaker.PlayerId is null || listener.PlayerId is null); i++)
            Tick();
        // Under way past the gate, with Holdouts (everyone's aboard first: a joiner now would wait in the queue).
        host.World.EnableRun(DataFile.Load<RunTuning>(Path.Combine(Content, RunTuning.File)), route, 600, authority: true);
        host.World.Run!.Resume(900, -1, host.Train.Boiler.Tender, 0);
        host.World.EnableHoldouts(holdouts, route);
        listener.World.EnableHoldouts(holdouts, route);
        byte dead = speaker.PlayerId!.Value, living = listener.PlayerId!.Value;
        host.SetPlayerState(dead, new PlayerState { Parent = PlayerState.World, Death = DeathCause.Mauled, LineHint = 100, Position = host.Train.Line.Sample(100).Position });
        for (int i = 0; i < 5; i++)
            Tick();
        var h = host.World.Holdouts!.All.Single(x => x.Occupant == dead);
        var offset = new Double3(metres, 0, 0);
        host.SetPlayerState(living, PlayerMotor.SpawnOnGround(h.Door + offset, host.Train.Line, h.LineHint, players));
        // D.7 (note 179): the dead's Jump at their Holdout is its Live Mic, on the press.
        if (on)
        {
            Tick(new PlayerIntent { Buttons = PlayerButtons.Jump });
            Tick();
        }
        for (int i = 0; i < 5; i++)
            Tick();
        Assert.Equal(on, host.World.Holdouts.LiveMicOf(dead) == h);
        Assert.Equal(on, listener.World.Holdouts!.All[h.Index].LiveMic);

        var audio = new GameAudio(Content);
        audio.Bank.Samples.InlineBytes = long.MaxValue;
        var ears = new VoiceChat(audio.Mixer);
        var mouth = new VoiceChat(new Mixer(new SoundBank(), audio.Mixer.Mix));
        var speech = SyntheticSpeech.Generate(1.5);
        int perTick = VoiceFormat.SampleRate / SimConstants.TickRate, ticks = (int)(2.0 * SimConstants.TickRate), heard = 0, spoken = 0;
        var tap = new MeterTap(ticks * perTick / Audio.Block * Audio.Block);
        audio.Mixer.Tap = tap;
        var block = new float[Audio.Block * 2];
        long rendered = 0;
        for (int tick = 0; tick < ticks; tick++)
        {
            if (spoken < speech.Length)
            {
                int n = Math.Min(perTick, speech.Length - spoken);
                mouth.Capture(speech.AsSpan(spoken, n), speaker);
                spoken += n;
            }
            Tick();
            // Voice, not the hard-cut's marker (note 172: the speaker died, a frame with no sound).
            heard += listener.VoiceFrames.Count(f => !f.Path.HasFlag(VoicePath.Cut));
            var frames = listener.Train.Frames;
            var crew = listener.RemoteIds.Select(id => listener.TryGetRemote(id, 1, out var s) ? (Crewmate?)Mate(id, s, frames) : null)
                .Where(c => c is not null).Select(c => c!.Value).ToList();
            var (feet, yaw) = Eyes.World(listener.Predicted, frames);
            audio.Update(listener.World, new TrainControls(), Listener.At(feet + Double3.Up * 1.65, yaw), exposed: true, SimConstants.TickSeconds);
            ears.Update(listener, crew, SimConstants.TickSeconds);
            for (long until = (tick + 1L) * perTick; rendered + Audio.Block <= until && tap.Written < tap.Total.Length; rendered += Audio.Block)
                audio.Mixer.Render(block);
        }
        var from = audio.Mixer.Voices.FirstOrDefault(v => v.Name == "voice")?.Position;
        double db = tap.Stems.TryGetValue("voice", out var stem) ? Meter.BandDb(stem, 300, 3000) : -180;
        return (heard, from, h.Inside, db);

        static Crewmate Mate(byte id, PlayerState s, IReadOnlyList<CarFrame> frames)
        {
            var (feet, yaw) = Eyes.World(s, frames);
            return new Crewmate(id, feet, yaw, s.Alive);
        }
    }

    [Fact]
    public void ALiveMicIsHeardFromTheHoldoutByTheRescuerAtItsDoor()
    {
        // Off (the default), the living hear nothing of the dead: the dead channel is theirs alone (spec C.1).
        var off = LiveMic(on: false);
        Assert.Equal(0, off.Heard);
        // On, the rescuer at the door hears them, clearly (3 m: inside the 8 m of full clarity), from behind the door.
        var on = LiveMic(on: true);
        Assert.True(on.Heard > 20, $"{on.Heard} frames heard");
        Assert.NotNull(on.From);
        // From inside the Holdout (note 179: the client places it by the replicated Holdout).
        Assert.True((on.From!.Value - (on.Inside + Double3.Up * 1.4)).Length < 0.01, $"voice from {on.From}, the Holdout's inside is {on.Inside}");
        Assert.True(on.Db > -25, $"{on.Db} dB at 3 m");
        // Past the 26 m cutoff, nobody: it only tells the rescuer at the door anything.
        Assert.Equal(0, LiveMic(on: true, metres: 40).Heard);
    }

    [Fact]
    public void VoiceSurvivesARoughLink()
    {
        var clean = VoiceBench.Run(Content, speakerCar: 3, speakerZ: 4, radio: false);
        var rough = VoiceBench.Run(Content, speakerCar: 3, speakerZ: 4, radio: false, link: new LinkConditions(0.09, 0.02, 0.05));
        Assert.True(rough.FramesHeard < rough.FramesSent);
        Assert.InRange(clean.NearDb - rough.NearDb, -1.5, 1.5);
    }

    /// <summary>
    /// GDD v1.4 App. D.2 and C.8, the hard-cut: on the tick of death the victim's voice stops mid-word, near and on the
    /// radio, with no fade and nothing buffered played out. The living hear the cut.
    /// </summary>
    [Fact]
    public void DeathCutsTheVoiceOffMidWord()
    {
        foreach (bool radio in new[] { false, true })
        {
            var r = VoiceBench.Run(Content, speakerCar: radio ? 9 : 3, speakerZ: radio ? 0 : 4, radio: radio, seconds: 2, dieAt: 1.0);
            Assert.True(r.FramesHeard > 0, "heard before the death");
            Assert.True(r.AfterCutDb < -150, $"{(radio ? "radio" : "near")}: {r.AfterCutDb} dB after the death");
        }
    }
}
