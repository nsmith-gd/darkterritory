namespace Ballast.Render;

/// <summary>
/// The practical lights a frame can carry (<see cref="FrameData.MaxLights"/>): the nearest win, as ever, and past the budget a
/// light far off lights little you can see. But a hard cut pops: as the eye moves, the light at the budget's edge and the
/// first left out trade places, and one goes dark in a frame, the other bright (the director, 13 Oct evening: "We're getting a
/// lot of flashing in the cab from all the lights", in a town with more lamps about than the budget; note 762). So the last
/// few kept fade by how near they are to being cut: the one at the edge is already dark when it trades places, and a light
/// coming in comes up from nothing. Continuous in the lights' places, with no state from frame to frame.
/// </summary>
public static class LightBudget
{
    /// <summary>How many of the kept lights, the furthest in, fade toward the cut.</summary>
    public const int Fade = 8;

    /// <summary>A light's claim on the budget: how near it is, less how far it reaches (m²; the lower, the stronger).</summary>
    public static float Claim(in PointLight l) => l.Position.LengthSquared() - l.Range * l.Range * 0.25f;

    /// <summary>
    /// <paramref name="lights"/> (eye-relative) cut to <paramref name="max"/>, the nearest kept; the last <see cref="Fade"/>
    /// of them dimmed by how near their claim is to the first one cut. Untouched when they all fit.
    /// </summary>
    public static void Fit(List<PointLight> lights, int max)
    {
        if (lights.Count <= max)
            return;
        lights.Sort((a, b) => Claim(a).CompareTo(Claim(b)));
        float cut = Claim(lights[max]);
        int from = Math.Max(0, max - Fade);
        float full = Claim(lights[from]);
        float band = cut - full;
        for (int i = from + 1; i < max; i++)
        {
            float k = band > 1e-3f ? Math.Clamp((cut - Claim(lights[i])) / band, 0, 1) : 0;
            var l = lights[i];
            lights[i] = l with { Colour = l.Colour * (k * k * (3 - 2 * k)) };
        }
        lights.RemoveRange(max, lights.Count - max);
    }
}
