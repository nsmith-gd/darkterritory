using Ballast;
using Ballast.Audio;

namespace DarkTerritory.Game.Sound;

/// <summary>One recorded note of a melodic instrument (content/audio/samples/band/manifest.json): its file and its pitch.</summary>
public sealed record BandSampleNote(string File, int Midi);

/// <summary>An instrument's recordings: a melodic one's notes, or a percussion one's strokes and their takes; its level trim.</summary>
public sealed record BandSampleInstrument(BandSampleNote[]? Notes = null, Dictionary<string, string[]>? Strokes = null, double GainDb = 0);

/// <summary>The band's recordings (tools/audio/band_intake.py; ARCHITECTURE §8 note 711). Field docs live in the manifest.</summary>
public sealed record BandSampleManifest(Dictionary<string, BandSampleInstrument> Instruments)
{
    public const string File = "audio/samples/band/manifest.json";
}

/// <summary>
/// The band played on recordings (ARCHITECTURE §8 note 711; the director, 14 Oct 2026: "let's get recorded instruments in place
/// instead of synthesized ones"): every note of a part made from the nearest recorded note of its instrument, resampled to its
/// pitch (all CC0, content/audio/samples/band). The bowed and blown ones (fiddle, whistle, accordion) held for the note and
/// let go; the plucked ones (banjo, guitar) left to ring a little past it and damped; the percussion a take of its stroke.
/// An instrument with no recordings falls back on <see cref="BandSynth"/>.
/// </summary>
public sealed class BandSamples(BandSampleManifest? manifest, SampleLibrary samples)
{
    const string Folder = "band";
    static readonly HashSet<string> Plucked = ["banjo", "guitar"];

    /// <summary>The manifest from the content (null if there's none: the synth plays).</summary>
    public static BandSampleManifest? Load(string content)
    {
        string path = Path.Combine(content, BandSampleManifest.File);
        return System.IO.File.Exists(path) ? DataFile.Load<BandSampleManifest>(path) : null;
    }

    /// <summary>Whether <paramref name="instrument"/> has recordings.</summary>
    public bool Recorded(string instrument) => manifest?.Instruments.ContainsKey(instrument) == true;

    /// <summary>
    /// A note's clip: <paramref name="pitch"/> (MIDI; percussion: 0 its heavy stroke, 1 its light one) held
    /// <paramref name="seconds"/>, at <paramref name="velocity"/>; <paramref name="take"/> picks among a stroke's takes.
    /// </summary>
    public float[] Render(string instrument, int pitch, double seconds, double velocity, int take = 0)
    {
        if (manifest?.Instruments.GetValueOrDefault(instrument) is not { } inst)
            return BandSynth.Render(instrument, pitch, seconds, velocity);
        float gain = (float)(Math.Clamp(velocity, 0, 1) * Math.Pow(10, inst.GainDb / 20));
        if (inst.Strokes is { Count: > 0 } strokes)
        {
            // The strokes in the manifest's order: the first the heavy one (the bodhrán's down, the spoons' clack, the boot).
            var names = strokes.Keys.ToList();
            var takes = strokes[names[Math.Clamp(pitch, 0, names.Count - 1)]];
            var clip = samples.Clip($"{Folder}/{takes[Math.Abs(take) % takes.Length]}");
            var o = new float[clip.Length];
            for (int i = 0; i < o.Length; i++)
                o[i] = clip.Pcm[i] * clip.Scale * gain;
            return o;
        }
        if (inst.Notes is not { Length: > 0 } notes)
            return BandSynth.Render(instrument, pitch, seconds, velocity);
        var near = notes.MinBy(n => Math.Abs(n.Midi - pitch))!;
        var source = samples.Clip($"{Folder}/{near.File}");
        double ratio = Math.Pow(2, (pitch - near.Midi) / 12.0);
        bool plucked = Plucked.Contains(instrument);
        // Held: for the note and a breath after; plucked: ringing on a little past it. Never past the recording's end.
        double release = plucked ? 0.08 : 0.09, ring = plucked ? seconds + 0.3 : seconds;
        int length = (int)Math.Min((ring + release) * Audio.SampleRate, source.Length / ratio);
        var output = new float[Math.Max(1, length)];
        int fadeFrom = (int)(ring * Audio.SampleRate), fade = Math.Max(1, (int)(release * Audio.SampleRate));
        // And the recording's own end, if the note outlasts it: let go over its last release.
        fadeFrom = Math.Min(fadeFrom, Math.Max(0, output.Length - fade));
        for (int i = 0; i < output.Length; i++)
        {
            double at = i * ratio;
            int a = (int)at;
            if (a + 1 >= source.Length)
                break;
            double f = at - a;
            float x = (float)((source.Pcm[a] * (1 - f) + source.Pcm[a + 1] * f) * source.Scale);
            float env = i < fadeFrom ? 1 : Math.Max(0, 1 - (float)(i - fadeFrom) / fade);
            output[i] = x * env * gain;
        }
        return output;
    }
}
