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
/// <param name="TubeDb">The speaking tube's voice (note 720), in the voice band; <paramref name="TubeLowDb"/> under 300 Hz and
/// <paramref name="TubeHighDb"/> over 4 kHz, where a pipe's horn has nothing; <paramref name="TubeBandDb"/> its own 800 Hz-1.6 kHz.</param>
/// <param name="RestDb">Note 855: everything else the listener heard in the voice band (300 Hz-3 kHz), the train's bed and the wind;
/// <paramref name="MaskedBy"/> the loudest of it by sound.</param>
/// <param name="NearHighDb">The voice in the air over 2 kHz (to 6 kHz), and <paramref name="RadioHighDb"/> the radio's 1.8-3 kHz: what a
/// hand over a held speaker's mouth takes away (note 749).</param>
public sealed record VoiceBenchReport(double DistanceM, bool Radio, bool SpeakerInCab, int FramesSent, int FramesHeard, int Underruns, double NearDb, double RadioDb,
    double RadioLowDb, double RadioBandDb, double BedDuckDb, string Space = "outside", double ReverbDb = -180, double TailDb = -180, double AfterCutDb = 0,
    double TubeDb = -180, double TubeLowDb = -180, double TubeHighDb = -180, double TubeBandDb = -180, double NearHighDb = -180, double RadioHighDb = -180,
    double RestDb = -180, IReadOnlyDictionary<string, double>? MaskedBy = null)
{
    /// <summary>Note 855: how far the voice in the air stands over everything else the listener hears in its band (dB).</summary>
    public double OverRestDb => Math.Round(NearDb - RestDb, 1);
}

/// <summary>
/// One speaker, one listener, a host between them, over the simulated network: the whole voice path from
/// microphone samples to what the listener's mixer plays. Measures spec A.5 end to end.
/// </summary>
public static class VoiceBench
{
    /// <param name="speakerCar">Car the speaker stands on (0 = in the cab); the listener is on car 3's roof.</param>
    /// <param name="space">Hear it as if in this space (a tunnel: GDD §22's compressed, close voice); null, where the listener is.</param>
    /// <param name="dieAt">Seconds into the speech the speaker dies, mid-word (the hard-cut's check).</param>
    /// <param name="muted">The listener has the speaker muted (note 724): nothing of theirs should be heard, on any path.</param>
    /// <param name="tube">The speaking tube (note 720): the speaker at the cab's mouthpiece, the listener in the guard van a few
    /// metres from its horn, the length of the train away (<paramref name="speakerCar"/> and <paramref name="speakerZ"/> unused).</param>
    /// <param name="held">Something has hold of the speaker the whole time (App. C.8: their radio keyed open; note 749: a hand
    /// over their mouth).</param>
    /// <param name="speed">Note 855: the train held at this speed (m/s), so the bed and the wind are what the voice is heard over.</param>
    /// <param name="listenerInCab">The listener in the cab, beside the driver's seat, not on a roof.</param>
    /// <param name="listenerCar">The listener's roof (<paramref name="listenerZ"/> along it), when not in the cab.</param>
    /// <param name="micDb">The microphone's own level against the synthetic talker's (a hot mic): -20 is a quiet headset.</param>
    /// <param name="speakerInCab">The speaker in the cab (by default, on car 0); false puts them on the engine's roof.</param>
    /// <param name="cabDoors">Note 862: the engine's doors held open (its <see cref="Vehicle.DoorsOpen"/> bits: 4 the cab's left side
    /// door, by the coal; 8 its right, note 856).</param>
    /// <param name="wav">Writes what the listener heard here, and its spectrogram beside it.</param>
    public static VoiceBenchReport Run(string content, int speakerCar, double speakerZ, bool radio, double seconds = 2, LinkConditions? link = null,
        string? space = null, double? dieAt = null, float micLevel = 1, bool tube = false, bool muted = false, bool held = false,
        double speed = 0, bool listenerInCab = false, int listenerCar = 3, double listenerZ = 0, double micDb = 0, string? wav = null, bool? speakerInCab = null, byte cabDoors = 0)
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
        // Note 724: the listener muted the speaker on their roster.
        var ears = new VoiceChat(audio.Mixer) { Muted = muted ? id => id == speaker.PlayerId : null };
        var mouth = new VoiceChat(new Mixer(new SoundBank(), audio.Mixer.Mix)) { RadioHeld = radio, MicLevel = micLevel };
        var speech = SyntheticSpeech.Generate(seconds);
        // Note 855: the microphone itself, before anything in the game: the synthetic talker is a hot mic (about -15 dBFS), and a
        // quiet Windows headset is 20 dB under that.
        float mic = (float)Math.Pow(10, micDb / 20);
        for (int i = 0; i < speech.Length; i++)
            speech[i] *= mic;
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
        if (tube)
        {
            host.SetPlayerState(speaker.PlayerId!.Value, SpeakingTube.AtMouth(t, 0, playerTuning));
            host.SetPlayerState(listener.PlayerId!.Value, SpeakingTube.AtMouth(t, 1, playerTuning, off: 1.2, along: 2.5));
        }
        else
        {
            host.SetPlayerState(speaker.PlayerId!.Value, speakerInCab ?? speakerCar == 0 ? PlayerMotor.SpawnInCab(t, playerTuning) : PlayerMotor.SpawnOnRoof(t, speakerCar, speakerZ, playerTuning));
            host.SetPlayerState(listener.PlayerId!.Value, listenerInCab ? PlayerMotor.SpawnInCab(t, playerTuning, 0.4) : PlayerMotor.SpawnOnRoof(t, listenerCar, listenerZ, playerTuning));
        }
        if (held)
        {
            var them = host.Players.First(p => p.Id == speaker.PlayerId).State;
            host.SetPlayerState(speaker.PlayerId!.Value, them with { Flags = them.Flags | PlayerFlags.Held });
        }
        if (cabDoors != 0)
            t.Vehicles[0].DoorsOpen |= cabDoors;
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
            // Note 855: under way, the bed and the wind the crew have to be heard over (held still in speed, as AudioBench does).
            if (speed > 0)
                host.Train.Dynamics.Velocity = speed;
            Tick();
            heard += listener.VoiceFrames.Count;
            var frames = listener.Train.Frames;
            var crew = listener.RemoteIds.Select(id => listener.TryGetRemote(id, 1, out var s) ? (Crewmate?)Make(id, s, frames) : null)
                .Where(c => c is not null).Select(c => c!.Value).ToList();
            var (feet, yaw) = Eyes.World(listener.Predicted, frames);
            audio.Update(listener.World, controls, Listener.At(feet + Double3.Up * 1.65, yaw), exposed: !PlayerMotor.Indoors(listener.Predicted, listener.Train),
                SimConstants.TickSeconds, PlayerMotor.Space(listener.Predicted, listener.Train));
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
        // Note 855: what the listener heard, to listen to and to look at (the spectrogram beside it).
        if (wav is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(wav))!);
            Wav.Write(wav, tap.Total);
            Ballast.Render.PngWriter.Write(Path.ChangeExtension(wav, ".png"), Spectrogram.Render(tap.Total, 800, 300), 800, 300, 1);
        }
        return new VoiceBenchReport(Math.Round(distance, 1), radio, (speakerInCab ?? speakerCar == 0) || tube, mouth.FramesSent, heard, underruns, Stem("voice"), Stem("voice-radio"),
            Stem("voice-radio", 40, 150), Stem("voice-radio", 500, 2500), Math.Round(Audio.GainToDb(duck), 1), audio.Space, Stem(Mixer.ReverbStem), tail, afterCut,
            Stem("voice-tube"), Stem("voice-tube", 40, 300), Stem("voice-tube", 4000, 12000), Stem("voice-tube", 800, 1600),
            Stem("voice", 2000, 6000), Stem("voice-radio", 1800, 3000), Math.Round(Meter.BandDb(tap.AllBut("voice"), 300, 3000), 1),
            // The loudest of the rest in the voice band: what a voice under it is lost in.
            tap.Stems.Where(kv => !kv.Key.StartsWith("voice", StringComparison.Ordinal)).Select(kv => (kv.Key, Db: Math.Round(Meter.BandDb(kv.Value, 300, 3000), 1)))
                .Where(x => x.Db > -60).OrderByDescending(x => x.Db).Take(6).ToDictionary(x => x.Key, x => x.Db));

        static Crewmate Make(byte id, PlayerState s, IReadOnlyList<CarFrame> frames)
        {
            var (feet, yaw) = Eyes.World(s, frames);
            return new Crewmate(id, feet, yaw, s.Alive);
        }
    }
}
