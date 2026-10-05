using Ballast;
using Ballast.Audio;
using Ballast.Net;
using Ballast.Voice;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Sound;

/// <param name="BedDuckDb">The deepest the bed (tier 5) was ducked while the voice played (spec A.3: −6 dB).</param>
/// <param name="Space">The space the listener heard it in (content/audio/spaces.json).</param>
/// <param name="ReverbDb">The space's reverb return, in the voice band, over the whole run.</param>
/// <param name="TailDb">The reverb return in the last quarter second, after the voice has stopped: the space ringing on.</param>
/// <param name="AfterCutDb">With a death mid-sentence (<c>dieAt</c>): how loud the speaker's voice still was, near and on the radio,
/// from a tick after the death on (a tick for the host's cut to arrive): the hard-cut's −180, silence (GDD v1.4 App. D.2).</param>
public sealed record VoiceBenchReport(double DistanceM, bool Radio, bool SpeakerInCab, int FramesSent, int FramesHeard, int Underruns, double NearDb, double RadioDb,
    double RadioLowDb, double RadioBandDb, double BedDuckDb, string Space = "outside", double ReverbDb = -180, double TailDb = -180, double AfterCutDb = 0);

/// <summary>
/// One speaker, one listener, a host between them, over the simulated network: the whole voice path from
/// microphone samples to what the listener's mixer plays. Measures spec A.5 end to end.
/// </summary>
public static class VoiceBench
{
    /// <param name="speakerCar">Car the speaker stands on (0 = in the cab); the listener is on car 3's roof.</param>
    /// <param name="space">Hear it as if in this space (a tunnel: GDD §22's compressed, close voice); null, where the listener is.</param>
    /// <param name="dieAt">Seconds into the speech the speaker dies, mid-word (the hard-cut's check).</param>
    public static VoiceBenchReport Run(string content, int speakerCar, double speakerZ, bool radio, double seconds = 2, LinkConditions? link = null,
        string? space = null, double? dieAt = null)
    {
        var trainTuning = DataFile.Load<TrainTuning>(Path.Combine(content, TrainTuning.File));
        var playerTuning = DataFile.Load<PlayerTuning>(Path.Combine(content, PlayerTuning.File));
        var line = RailLine.Load(Path.Combine(content, "lines", "test-loop.json"));
        TrainOnLine Train() => new(new TrainDynamics(Consist.Uniform(trainTuning, 10, 1)), line, 1200);
        var net = new LoopbackNetwork(1, link ?? LinkConditions.Perfect);
        var host = new HostSession(net.CreateHost(), Train(), trainTuning, playerTuning);
        var speaker = new ClientSession(net.CreateClient(), Train(), trainTuning, playerTuning);
        var listener = new ClientSession(net.CreateClient(), Train(), trainTuning, playerTuning);

        var audio = new GameAudio(content) { SpaceOverride = space };
        audio.Bank.Samples.InlineBytes = long.MaxValue; // offline: the same every run
        var ears = new VoiceChat(audio.Mixer);
        var mouth = new VoiceChat(new Mixer(new SoundBank(), audio.Mixer.Mix)) { RadioHeld = radio };
        var speech = SyntheticSpeech.Generate(seconds);
        int perTick = VoiceFormat.SampleRate / SimConstants.TickRate;
        int total = (int)(seconds * VoiceFormat.SampleRate) + VoiceFormat.SampleRate / 2;
        int blocks = total / Audio.Block;
        var tap = new MeterTap(blocks * Audio.Block);
        audio.Mixer.Tap = tap;
        var mix = new float[Audio.Block * 2];
        long rendered = 0;
        var controls = new TrainControls { Reverser = 1 };

        void Tick()
        {
            net.Advance(SimConstants.TickSeconds);
            host.Step();
            speaker.Step(default);
            listener.Step(default);
        }
        for (int i = 0; i < 20 && (speaker.PlayerId is null || listener.PlayerId is null); i++)
            Tick();
        var t = host.Train;
        host.SetPlayerState(speaker.PlayerId!.Value, speakerCar == 0 ? PlayerMotor.SpawnInCab(t, playerTuning) : PlayerMotor.SpawnOnRoof(t, speakerCar, speakerZ, playerTuning));
        host.SetPlayerState(listener.PlayerId!.Value, PlayerMotor.SpawnOnRoof(t, 3, 0, playerTuning));
        for (int i = 0; i < 10; i++)
            Tick();

        double distance = (PlayerMotor.WorldPosition(host.Players.First(p => p.Id == speaker.PlayerId).State, t)
            - PlayerMotor.WorldPosition(host.Players.First(p => p.Id == listener.PlayerId).State, t)).Length;
        int heard = 0, spoken = 0, ticks = total / perTick;
        float duck = 1;
        for (int tick = 0; tick < ticks; tick++)
        {
            if (dieAt is { } at && tick == (int)(at * SimConstants.TickRate))
            {
                var dying = host.Players.First(p => p.Id == speaker.PlayerId).State;
                host.SetPlayerState(speaker.PlayerId!.Value, dying with { Health = 0, Death = DeathCause.Eaten });
            }
            if (spoken < speech.Length)
            {
                int n = Math.Min(perTick, speech.Length - spoken);
                mouth.Capture(speech.AsSpan(spoken, n), speaker);
                spoken += n;
            }
            Tick();
            heard += listener.VoiceFrames.Count;
            var frames = listener.Train.Frames;
            var crew = listener.RemoteIds.Select(id => listener.TryGetRemote(id, 1, out var s) ? (Crewmate?)Make(id, s, frames) : null)
                .Where(c => c is not null).Select(c => c!.Value).ToList();
            var (feet, yaw) = Eyes.World(listener.Predicted, frames);
            audio.Update(listener.World, controls, Listener.At(feet + Double3.Up * 1.65, yaw), exposed: true, SimConstants.TickSeconds);
            ears.Update(listener, crew, SimConstants.TickSeconds);
            long target = (long)(tick + 1) * perTick;
            while (rendered + Audio.Block <= target && tap.Written < tap.Total.Length)
            {
                audio.Mixer.Render(mix);
                rendered += Audio.Block;
                duck = Math.Min(duck, audio.Mixer.TierGain(5));
            }
        }

        // Silence reads as −180 dB (the meter's floor), so the report always serialises.
        double Stem(string name, double low = 300, double high = 3000) =>
            tap.Stems.TryGetValue(name, out var s) ? Math.Round(Meter.BandDb(s, low, high), 1) : -180;
        int underruns = ears.Speakers.Sum(ears.Underruns);
        int last = Math.Min(tap.Total.Length, Audio.SampleRate / 4 * 2);
        double tail = tap.Stems.TryGetValue(Mixer.ReverbStem, out var wet) ? Math.Round(Meter.Db(wet.AsSpan(wet.Length - last)), 1) : -180;
        double afterCut = 0;
        if (dieAt is { } died)
        {
            int from = ((int)(died * SimConstants.TickRate) + 1) * perTick * 2;
            double Tail(string name) => tap.Stems.TryGetValue(name, out var st) && from < st.Length ? Meter.BandDb(st.AsSpan(from), 300, 3000) : -180;
            afterCut = Math.Round(Math.Max(Tail("voice"), Tail("voice-radio")), 1);
        }
        return new VoiceBenchReport(Math.Round(distance, 1), radio, speakerCar == 0, mouth.FramesSent, heard, underruns, Stem("voice"), Stem("voice-radio"),
            Stem("voice-radio", 40, 150), Stem("voice-radio", 500, 2500), Math.Round(Audio.GainToDb(duck), 1), audio.Space, Stem(Mixer.ReverbStem), tail, afterCut);

        static Crewmate Make(byte id, PlayerState s, IReadOnlyList<CarFrame> frames)
        {
            var (feet, yaw) = Eyes.World(s, frames);
            return new Crewmate(id, feet, yaw, s.Alive);
        }
    }
}
