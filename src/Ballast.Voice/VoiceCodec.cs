using Concentus;
using Concentus.Enums;

namespace Ballast.Voice;

public static class VoiceFormat
{
    public const int SampleRate = 48000;
    /// <summary>20 ms frames (ARCHITECTURE §6.3).</summary>
    public const int FrameSamples = 960;
    public const int MaxPacket = 400;
}

/// <summary>Mono 48 kHz Opus in VoIP mode, 20 ms frames.</summary>
public sealed class VoiceEncoder
{
    readonly IOpusEncoder _opus;

    public VoiceEncoder(int bitrate = 24000)
    {
        _opus = OpusCodecFactory.CreateEncoder(VoiceFormat.SampleRate, 1, OpusApplication.OPUS_APPLICATION_VOIP);
        _opus.Bitrate = bitrate;
    }

    /// <summary>Encodes one frame of <see cref="VoiceFormat.FrameSamples"/>; returns the packet length.</summary>
    public int Encode(ReadOnlySpan<float> frame, Span<byte> packet)
    {
        if (frame.Length != VoiceFormat.FrameSamples)
            throw new ArgumentException($"a frame is {VoiceFormat.FrameSamples} samples");
        return _opus.Encode(frame, VoiceFormat.FrameSamples, packet, packet.Length);
    }
}

/// <summary>
/// Decodes one speaker's packets as they arrive, in sequence order. A gap is filled with Opus packet-loss
/// concealment (up to a few frames); a late or repeated packet is dropped, since its moment has passed.
/// </summary>
public sealed class VoiceDecoder
{
    const int MaxConcealed = 3;
    readonly IOpusDecoder _opus = OpusCodecFactory.CreateDecoder(VoiceFormat.SampleRate, 1);
    readonly float[] _frame = new float[VoiceFormat.FrameSamples];
    ushort? _last;

    public int Concealed { get; private set; }
    public int Dropped { get; private set; }

    /// <summary>Decodes <paramref name="packet"/> (and conceals any gap before it) into <paramref name="output"/>.</summary>
    public void Decode(ushort sequence, ReadOnlySpan<byte> packet, Action<ReadOnlySpan<float>> output)
    {
        if (_last is { } last)
        {
            int gap = (ushort)(sequence - last);
            if (gap == 0 || gap > 0x8000)
            {
                Dropped++;
                return;
            }
            // A long gap is a new talk spurt, not loss: nothing to conceal.
            for (int i = 1; i < gap && gap <= MaxConcealed + 1; i++)
            {
                int n = _opus.Decode(ReadOnlySpan<byte>.Empty, _frame, VoiceFormat.FrameSamples, false);
                output(_frame.AsSpan(0, n));
                Concealed++;
            }
        }
        _last = sequence;
        int samples = _opus.Decode(packet, _frame, VoiceFormat.FrameSamples, false);
        output(_frame.AsSpan(0, samples));
    }
}

/// <summary>
/// Voice activity by level, with a hangover so word endings aren't clipped. Open-mic play (spec A.5 is a
/// mechanic, so everyone near hears you) with push-to-talk as the alternative.
/// </summary>
public sealed class VoiceActivity(double thresholdDb = -45, double hangoverSeconds = 0.35)
{
    int _hangover;

    public bool Speaking { get; private set; }

    public bool Update(ReadOnlySpan<float> frame)
    {
        double sum = 0;
        foreach (float s in frame)
            sum += s * s;
        double db = 10 * Math.Log10(Math.Max(sum / Math.Max(1, frame.Length), 1e-12));
        if (db > thresholdDb)
            _hangover = (int)Math.Ceiling(hangoverSeconds * VoiceFormat.SampleRate / VoiceFormat.FrameSamples);
        else if (_hangover > 0)
            _hangover--;
        Speaking = db > thresholdDb || _hangover > 0;
        return Speaking;
    }
}
