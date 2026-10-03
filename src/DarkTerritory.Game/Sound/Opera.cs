using Ballast.Audio;
using DarkTerritory.Sim.Music;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// The derailment's opera on a client (GDD v1.4 App. E.6; ARCHITECTURE §8 note 174). Every track in the manifest is loaded
/// at startup. When the derailment sequence (note 170) reaches the replay, the host's draw (<see cref="Sim.World.DerailMusic"/>)
/// starts on the music bus from the point that puts its hit on the replay's moment of derailment, plays through the orbit,
/// and fades out by the sequence's end. Presentation only: it runs off the client's own count of the wreck's seconds,
/// like the sequence's beats.
/// </summary>
public sealed class Opera
{
    /// <summary>The sound (content/audio/sounds/music.json) a track plays through: a sample layer on the music bus.</summary>
    public const string Sound = "music";
    readonly Dictionary<string, AudioClip> _clips = [];
    SoundInstance? _playing;
    uint _started;

    public Opera(string content)
    {
        Manifest = MusicManifest.Load(content);
        foreach (var t in Manifest.Tracks)
        {
            var path = Path.Combine(content, "audio", "music", t.File);
            if (File.Exists(path))
                _clips[t.Id] = AudioClip.LoadWav(path);
        }
    }

    public MusicManifest Manifest { get; }
    public IReadOnlyDictionary<string, AudioClip> Clips => _clips;
    /// <summary>What's playing now, if anything.</summary>
    public MusicTrack? Playing { get; private set; }

    /// <summary>
    /// Where <paramref name="track"/> is at <paramref name="sequenceSeconds"/> into the derailment, and how loud: null before
    /// the replay, after the sequence, and past the track's out-point. Its start (the replay's first frame) is
    /// <see cref="MusicTrack.StartFor"/> the replay's lead, so its hit comes as the replay shows the train coming off; its
    /// gain is the manifest's (to −16 LUFS), faded over the last <see cref="MusicTuning.FadeOutSeconds"/> of the sequence
    /// (or of the track, if that runs out first).
    /// </summary>
    /// <param name="end">Where the music's over, in sequence seconds: by default the sequence's end; with the film, the cause
    /// card's start (E.5: the fade under the cause card, the clerk on the static alone).</param>
    public static (double ClipSeconds, float Gain)? Cue(MusicTrack track, double sequenceSeconds, WreckTuning t, double end = -1)
    {
        if (end < 0)
            end = t.SequenceSeconds;
        if (sequenceSeconds < t.FirstPersonSeconds || sequenceSeconds >= end)
            return null;
        double clip = track.StartFor(t.ReplayLeadSeconds) + (sequenceSeconds - t.FirstPersonSeconds);
        if (clip >= track.OutPoint)
            return null;
        double fade = Math.Max(1e-3, t.Music.FadeOutSeconds);
        double out_ = Math.Clamp((end - sequenceSeconds) / fade, 0, 1) * Math.Clamp((track.OutPoint - clip) / fade, 0, 1);
        return (clip, Audio.DbToGain(track.GainDb) * (float)out_);
    }

    /// <summary>
    /// Every frame: the host's draw (<paramref name="key"/>, 0 for none) at <paramref name="sequenceSeconds"/> into the
    /// derailment (negative when there isn't one). Starts the track once, keeps its gain, stops it when it's over.
    /// </summary>
    public void Update(Mixer mixer, uint key, double sequenceSeconds, WreckTuning t, double end = -1)
    {
        var track = Manifest.ByKey(key);
        var cue = track is null ? null : Cue(track, sequenceSeconds, t, end);
        if (cue is not { } c || !_clips.TryGetValue(track!.Id, out var clip))
        {
            Stop();
            // Ready for the next derailment once this one's behind us (a new night, or the sequence never reached).
            if (sequenceSeconds < t.FirstPersonSeconds)
                _started = 0;
            return;
        }
        if (_started != key)
        {
            Stop();
            _started = key;
            // Starting late (a frame, a hitch) starts further in: the hit stays where it belongs.
            _playing = mixer.Play(Sound)?.Also(v =>
            {
                v.Clip = clip;
                v.ClipSeconds = c.ClipSeconds;
            });
            Playing = _playing is null ? null : track;
        }
        if (_playing is { Finished: false } p)
            p.Volume = c.Gain;
    }

    public void Stop()
    {
        _playing?.Stop();
        _playing = null;
        Playing = null;
    }
}
