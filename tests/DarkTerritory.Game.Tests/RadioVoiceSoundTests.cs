using Ballast;
using Ballast.Audio;
using Ballast.Net;
using Ballast.Voice;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// A corrupted human on the radio, heard (note 1040; GDD Part Eleven q4): the host's <c>Mimic | Radio</c> frames play as the
/// walkie-talkie plays a crewmate (voice-radio, the set's click as it's keyed), never as a Soot Child's call from a place.
/// </summary>
public class RadioVoiceSoundTests
{
    static readonly string Content = DataFile.FindContentRoot();

    [Fact]
    public void ItsOnTheRadioLikeAnyoneOnItAndNotFromAPlace()
    {
        var trains = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
        var players = DataFile.Load<PlayerTuning>(Path.Combine(Content, PlayerTuning.File));
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(10_000)]));
        var client = new ClientSession(new LoopbackNetwork().CreateClient(), new TrainOnLine(new TrainDynamics(Consist.Uniform(trains, 3, 0)), line, 2_000), trains, players);
        var audio = new GameAudio(Content);
        audio.Bank.Samples.InlineBytes = long.MaxValue;
        var ears = new VoiceChat(audio.Mixer);
        // Two seconds of speech, as the host would replay a crewmate's words: their speaker byte, the Brakeman's id (77).
        var speech = SyntheticSpeech.Generate(2);
        var encoder = new VoiceEncoder();
        var packet = new byte[VoiceFormat.MaxPacket];
        ushort seq = 0;
        var started = new List<string>();
        var seen = new HashSet<int>();
        var block = new float[Audio.Block * 2];
        int perTick = VoiceFormat.SampleRate / SimConstants.TickRate, at = 0;
        for (int tick = 0; tick < 3 * SimConstants.TickRate; tick++)
        {
            for (int f = 0; f < 2 && at + VoiceFormat.FrameSamples <= speech.Length; f++, at += VoiceFormat.FrameSamples)
            {
                int bytes = encoder.Encode(speech.AsSpan(at, VoiceFormat.FrameSamples), packet);
                client.VoiceFrames.Enqueue(new VoiceFrame(2, seq++, VoicePath.Mimic | VoicePath.Radio, packet[..bytes], 77));
            }
            ears.Update(client, [], SimConstants.TickSeconds);
            for (int r = 0; r < perTick / Audio.Block + 1; r++)
                audio.Mixer.Render(block);
            started.AddRange(audio.Mixer.Voices.Where(v => seen.Add(v.Id)).Select(v => v.Name));
        }
        // The radio's own stream, keyed with the set's click; nothing from a place, and nothing under the crewmate's own voice.
        Assert.Contains("voice-radio", started);
        Assert.Contains("voice-radio-sfx.key-down", started);
        Assert.DoesNotContain("voice-mimic", started);
        Assert.DoesNotContain("voice", started);
        Assert.Empty(ears.Speakers);
    }
}
