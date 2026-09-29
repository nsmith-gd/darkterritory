using Ballast;
using Ballast.Audio;
using Ballast.Voice;
using DarkTerritory.Sim.Net;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// Proximity voice and the walkie-talkie (spec A.5), client side. Outgoing: microphone → voice activity (or
/// push-to-talk) → Opus in 20 ms frames → the host. Incoming: each speaker's frames are decoded once and fed to
/// the path the host said they came by: a positional voice at the speaker (26 m log falloff, occluded through
/// the cab walls), a flat band-limited radio, or the flat dead channel. All of it plays on tier 2 of the mixer,
/// so talking ducks the bed exactly as spec A.3 says.
/// </summary>
public sealed class VoiceChat
{
    readonly Mixer _mixer;
    readonly VoiceEncoder _encoder = new();
    readonly VoiceActivity _activity = new();
    readonly float[] _frame = new float[VoiceFormat.FrameSamples];
    readonly byte[] _packet = new byte[VoiceFormat.MaxPacket];
    readonly Dictionary<byte, Speaker> _speakers = new();
    int _filled;
    ushort _sequence;
    double _clock;

    public VoiceChat(Mixer mixer) => _mixer = mixer;

    /// <summary>Talk only while <see cref="TalkHeld"/>; otherwise voice activity decides.</summary>
    public bool PushToTalk { get; set; }
    public bool TalkHeld { get; set; }
    /// <summary>Holding the radio's button: this also goes out over the walkie-talkie.</summary>
    public bool RadioHeld { get; set; }
    public bool Transmitting { get; private set; }
    public int FramesSent { get; private set; }

    sealed class Speaker(byte id)
    {
        public readonly byte Id = id;
        public readonly VoiceDecoder Decoder = new();
        public readonly JitterBuffer<VoiceFrame> Order = new();
        public readonly StreamBuffer Near = new(), Radio = new(), Dead = new();
        public SoundInstance? NearVoice, RadioVoice, DeadVoice;
        public double RadioKeyed;
    }

    public IEnumerable<byte> Speakers => _speakers.Keys;
    public int Underruns(byte speaker) => _speakers.TryGetValue(speaker, out var s) ? s.Near.Underruns : 0;

    /// <summary>Microphone samples (mono, 48 kHz), any amount; sends whole frames as they fill.</summary>
    public void Capture(ReadOnlySpan<float> samples, ClientSession client)
    {
        while (samples.Length > 0)
        {
            int n = Math.Min(samples.Length, _frame.Length - _filled);
            samples[..n].CopyTo(_frame.AsSpan(_filled));
            _filled += n;
            samples = samples[n..];
            if (_filled < _frame.Length)
                continue;
            _filled = 0;
            bool speaking = _activity.Update(_frame);
            Transmitting = PushToTalk ? TalkHeld || RadioHeld : speaking || RadioHeld;
            // The sequence advances even in silence, so a receiver can tell loss (conceal) from a pause (don't).
            _sequence++;
            if (!Transmitting)
                continue;
            int bytes = _encoder.Encode(_frame, _packet);
            client.SendVoice(_sequence, RadioHeld, _packet.AsSpan(0, bytes));
            FramesSent++;
        }
    }

    /// <summary>Decodes what's arrived and keeps each speaker's voice where they are.</summary>
    public void Update(ClientSession client, IReadOnlyList<Crewmate> crew, double dt)
    {
        _clock += dt;
        while (client.VoiceFrames.TryDequeue(out var f))
        {
            if (!_speakers.TryGetValue(f.Speaker, out var s))
                _speakers[f.Speaker] = s = new Speaker(f.Speaker);
            s.Order.Push(f.Sequence, f, _clock);
        }
        foreach (var s in _speakers.Values)
            s.Order.Drain(_clock, (_, f) => Decode(s, f));
        foreach (var s in _speakers.Values)
        {
            s.NearVoice ??= Stream("voice", s.Near);
            s.RadioVoice ??= Stream("voice-radio", s.Radio);
            s.DeadVoice ??= Stream("voice-dead", s.Dead);
            foreach (var c in crew)
                if (c.Id == s.Id && s.NearVoice is not null)
                    s.NearVoice.Position = c.Feet + Double3.Up * 1.6;
            s.RadioKeyed = Math.Max(0, s.RadioKeyed - dt);
            s.RadioVoice?.Params.Set("keyed", s.RadioKeyed > 0 || s.Radio.Playing ? 1 : 0);
        }
        // Whoever's left the session goes quiet.
        foreach (var id in _speakers.Keys.Where(id => client.RemoteIds.All(r => r != id)).ToList())
        {
            var s = _speakers[id];
            s.NearVoice?.Stop();
            s.RadioVoice?.Stop();
            s.DeadVoice?.Stop();
            _speakers.Remove(id);
        }
    }

    static void Decode(Speaker s, VoiceFrame f)
    {
        if (f.Path.HasFlag(VoicePath.Radio))
            s.RadioKeyed = 0.15;
        if (s.NearVoice is not null)
            s.NearVoice.Occlusion = f.Path.HasFlag(VoicePath.Occluded) ? 1 : 0;
        s.Decoder.Decode(f.Sequence, f.Opus, pcm =>
        {
            if (f.Path.HasFlag(VoicePath.Proximity))
                s.Near.Write(pcm);
            if (f.Path.HasFlag(VoicePath.Radio))
                s.Radio.Write(pcm);
            if (f.Path.HasFlag(VoicePath.Dead))
                s.Dead.Write(pcm);
        });
    }

    SoundInstance? Stream(string sound, StreamBuffer buffer)
    {
        var v = _mixer.Play(sound);
        if (v is not null)
            v.Stream = buffer;
        return v;
    }
}
