namespace Ballast.Audio;

/// <summary>
/// The listener's head (<c>mix.json</c> "head"; spec A.4 "spatially precise: directional to within ~30°. Players must be
/// able to say 'car four, left side'", and VR). A spherical head's cues on every positioned voice, so where a sound is can
/// be told by ear, not only by which ear it's louder in:
/// <list type="bullet">
/// <item>The far ear hears it later: Woodworth's interaural delay, up to about 0.66 ms with the source at the side.</item>
/// <item>The far ear hears it duller: the head's shadow, a high shelf that comes down as the source goes round to the side.</item>
/// <item>Behind, both ears hear it duller: the pinna's shadow, which tells front from back.</item>
/// <item>Above, a little more air; below, a notch: the pinna's height cue, which tells the roof from the floor.</item>
/// </list>
/// A parametric head, not measured HRTFs (ARCHITECTURE §8 note 246): every cue is a number here, it costs a few filters
/// a voice, and it renders the same offline as on the device.
/// </summary>
/// <param name="Radius">The head's radius in metres (the delay's): 8.75 cm, the textbook head.</param>
/// <param name="ShadowHz">The corner of the far ear's shelf.</param>
/// <param name="ShadowDb">The far ear's shelf with the source straight to the side (none ahead or behind).</param>
/// <param name="RearHz">The corner of the pinna's shelf from behind.</param>
/// <param name="RearDb">That shelf, on both ears, with the source straight behind.</param>
/// <param name="HeightHz">The pinna's height cue: a peak there, boosted overhead and cut (a notch) underfoot.</param>
/// <param name="HeightDb">The cue's gain with the source straight up (its negative straight down).</param>
/// <param name="TellFloorDb">The most the head cuts a tell (tier 1), behind it, to its side or under it: spec A.3's tier 1 is
/// never masked, as the occlusion floor has it. Its cues still point to it; they just never cost it the bed.</param>
/// <param name="PanWidth">How far the level pan under it all goes, 0..1: 1 is a speaker pan, nothing in the far ear at the
/// side; a head lets the lows round to both ears, and the shadow takes the highs.</param>
public sealed record HeadDef(double Radius = 0.0875, double ShadowHz = 1800, double ShadowDb = -10, double RearHz = 3500,
    double RearDb = -6, double HeightHz = 8000, double HeightDb = 4, double PanWidth = 0.6, double TellFloorDb = -3)
{
    /// <summary>Speed of sound, m/s.</summary>
    public const double SpeedOfSound = 343;

    /// <summary>
    /// Woodworth's interaural delay in seconds for a source at <paramref name="lateral"/> (the sine of its angle off the
    /// median plane: 0 ahead or behind, 1 at the side).
    /// </summary>
    public double Delay(double lateral)
    {
        double theta = Math.Asin(Math.Clamp(Math.Abs(lateral), 0, 1));
        return Radius / SpeedOfSound * (theta + Math.Sin(theta));
    }
}

/// <summary>Where a voice is from the listener's head: side (−1 left … 1 right), behind (0 … 1) and up (−1 … 1).</summary>
public readonly record struct HeadDirection(double Lateral, double Behind, double Up);

/// <summary>One voice's state through the head: its delay line and the filters on each ear.</summary>
internal sealed class HeadState
{
    // Longer than the widest delay (a 0.66 ms head is 32 samples at 48 kHz) with room for a bigger one.
    const int Ring = 128;
    readonly float[] _ring = new float[Ring];
    readonly float[] _mono = new float[Audio.Block];
    int _write;
    float _delayLeft, _delayRight;
    Biquad _rear, _height, _shadowLeft, _shadowRight;

    /// <summary>
    /// A block of the voice, mono, through the head into each ear. The delays glide across the block from where the last
    /// one left them, so a source moving round the head doesn't click.
    /// </summary>
    public void Process(ReadOnlySpan<float> mono, Span<float> left, Span<float> right, HeadDef head, HeadDirection at, bool tell = false)
    {
        double rear = head.RearDb * at.Behind, height = head.HeightDb * at.Up;
        double shadowLeft = head.ShadowDb * Math.Max(0, at.Lateral), shadowRight = head.ShadowDb * Math.Max(0, -at.Lateral);
        // A tell's cuts, all together at the ear that loses most, stop at the floor: scaled down as one, so they still
        // point the same way. A boost (the height cue overhead) is its own.
        double worst = rear + Math.Min(0, height) + Math.Min(shadowLeft, shadowRight);
        if (tell && worst < head.TellFloorDb)
        {
            double k = head.TellFloorDb / worst;
            rear *= k;
            shadowLeft *= k;
            shadowRight *= k;
            if (height < 0)
                height *= k;
        }
        _rear.Set(FilterType.HighShelf, head.RearHz, 0.707, rear);
        _height.Set(FilterType.Peak, head.HeightHz, 1.4, height);
        _shadowLeft.Set(FilterType.HighShelf, head.ShadowHz, 0.707, shadowLeft);
        _shadowRight.Set(FilterType.HighShelf, head.ShadowHz, 0.707, shadowRight);
        float delay = (float)Math.Min(Ring - 2, head.Delay(at.Lateral) * Audio.SampleRate);
        // The ear on the far side is the late one: a source on the right reaches the left ear last.
        float leftTo = at.Lateral > 0 ? delay : 0, rightTo = at.Lateral < 0 ? delay : 0;
        float leftFrom = _delayLeft, rightFrom = _delayRight;
        var x = _mono.AsSpan(0, mono.Length);
        mono.CopyTo(x);
        _rear.Process(x);
        _height.Process(x);
        for (int s = 0; s < x.Length; s++)
        {
            _ring[_write] = x[s];
            float u = (s + 1f) / x.Length;
            left[s] = _shadowLeft.Process(Read(leftFrom + (leftTo - leftFrom) * u));
            right[s] = _shadowRight.Process(Read(rightFrom + (rightTo - rightFrom) * u));
            _write = (_write + 1) % Ring;
        }
        _delayLeft = leftTo;
        _delayRight = rightTo;
    }

    /// <summary>The sample <paramref name="delay"/> samples ago, between two by its fraction.</summary>
    float Read(float delay)
    {
        int whole = (int)delay;
        float frac = delay - whole;
        float a = _ring[(_write - whole + Ring) % Ring], b = _ring[(_write - whole - 1 + Ring) % Ring];
        return a + (b - a) * frac;
    }
}
