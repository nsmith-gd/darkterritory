namespace Ballast.Voice;

/// <summary>
/// The leveller's settings (the game's player.json <c>voices.leveller</c>): the speech level every microphone is brought to,
/// how far it may be lifted or cut to get there, and how quickly it follows.
/// </summary>
public sealed record LevellerSettings
{
    public bool On { get; init; } = true;
    /// <summary>The speech level sent (dBFS, the RMS of a 20 ms frame while talking).</summary>
    public double TargetDb { get; init; } = -14;
    /// <summary>The most a quiet microphone is lifted (dB).</summary>
    public double MaxBoostDb { get; init; } = 24;
    /// <summary>The most a hot one is cut (dB, as a positive number).</summary>
    public double MaxCutDb { get; init; } = 12;
    /// <summary>How quickly the estimate of the talker's level comes up to louder speech (s) and down to quieter (s).</summary>
    public double RiseSeconds { get; init; } = 0.15;
    public double FallSeconds { get; init; } = 2.5;
    /// <summary>A frame this far under the estimate is a pause or a breath, not speech: it doesn't pull the level down (dB).</summary>
    public double IgnoreBelowDb { get; init; } = 20;
    /// <summary>Where a lifted frame's peak is held to (full scale is 1): a shout isn't clipped by the lift.</summary>
    public double Ceiling { get; init; } = 0.95;
}

/// <summary>
/// Automatic gain on what a microphone captures, before it's encoded (note 855): microphones differ by 20 dB and more out
/// of the box (a Windows headset's speech can sit near −35 dBFS, a Linux desktop's PulseAudio near −15), and with nothing
/// levelling them the quiet one is drowned by the train where the loud one carries. It learns the talker's level from the
/// frames they're talking in (<paramref name="speaking"/>), quickly up and slowly down, and brings it to
/// <see cref="LevellerSettings.TargetDb"/>, within its limits; the gain glides across a frame so it never clicks, and a
/// frame's peak is held under the ceiling. Silence and noise between words leave the estimate alone.
/// </summary>
public sealed class VoiceLeveller(LevellerSettings settings)
{
    double? _levelDb;
    double _gain = 1;

    public LevellerSettings Settings { get; } = settings;
    /// <summary>The gain the last frame ended on (dB).</summary>
    public double GainDb => 20 * Math.Log10(_gain);

    /// <summary>Levels one frame in place; <paramref name="speaking"/> is whether the voice activity heard talk in it.</summary>
    public void Process(Span<float> frame, bool speaking)
    {
        if (!Settings.On || frame.Length == 0)
            return;
        double sum = 0, peak = 0;
        foreach (float s in frame)
        {
            sum += s * s;
            peak = Math.Max(peak, Math.Abs(s));
        }
        double db = 10 * Math.Log10(Math.Max(sum / frame.Length, 1e-12));
        double seconds = (double)frame.Length / VoiceFormat.SampleRate;
        if (speaking)
        {
            if (_levelDb is not { } level)
                _levelDb = db;
            else if (db > level)
                _levelDb = level + (db - level) * (1 - Math.Exp(-seconds / Settings.RiseSeconds));
            else if (db > level - Settings.IgnoreBelowDb)
                _levelDb = level + (db - level) * (1 - Math.Exp(-seconds / Settings.FallSeconds));
        }
        double wantDb = _levelDb is { } l ? Math.Clamp(Settings.TargetDb - l, -Settings.MaxCutDb, Settings.MaxBoostDb) : 0;
        double want = Math.Pow(10, wantDb / 20);
        // Never past the ceiling at this frame's peak: the lift gives way to a shout rather than clip it.
        if (peak * want > Settings.Ceiling)
            want = Settings.Ceiling / peak;
        double from = _gain;
        for (int i = 0; i < frame.Length; i++)
            frame[i] = (float)(frame[i] * (from + (want - from) * (i + 1) / frame.Length));
        _gain = want;
    }
}
