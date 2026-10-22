using System.Numerics;
using Ballast.Render;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The frame's light budget (<see cref="LightBudget"/>, note 762; the director, 13 Oct evening, `docs/playtest/2026-10-13-evening.md`
/// line 25: "We're getting a lot of flashing in the cab from all the lights"). A town's lamps outnumber the budget from the cab;
/// as the train moves the one at the budget's edge and the first left out trade places. Cut hard, one went dark in a frame and
/// the other came on full. Now no light's strength jumps as the eye moves.
/// </summary>
public class LightBudgetTests
{
    const int Max = 32;

    /// <summary>A street of lamps both sides, more than the budget, each its own reach.</summary>
    static List<(Vector3 At, float Range)> Street()
    {
        var lamps = new List<(Vector3, float)>();
        for (int i = 0; i < 40; i++)
            foreach (float side in new[] { -4f, 5f })
                lamps.Add((new Vector3(side, 3 + (i % 3) * 0.4f, -6f * i - (side > 0 ? 2.5f : 0)), 10 + (i * 7 % 5) * 2));
        return lamps;
    }

    /// <summary>Each lamp's strength this frame (0 cut, 1 full), the eye at <paramref name="eye"/>, cut by <paramref name="fit"/>.</summary>
    static float[] Strengths(List<(Vector3 At, float Range)> lamps, Vector3 eye, Action<List<PointLight>, int> fit)
    {
        var lights = lamps.Select(l => new PointLight(l.At - eye, Vector3.One, l.Range)).ToList();
        fit(lights, Max);
        var strength = new float[lamps.Count];
        foreach (var l in lights)
        {
            int i = lamps.FindIndex(m => Vector3.DistanceSquared(m.At - eye, l.Position) < 1e-6f);
            strength[i] = l.Colour.X;
        }
        return strength;
    }

    // The renderer's cut before note 762: the nearest by claim kept, the rest dropped.
    static void Hard(List<PointLight> lights, int max)
    {
        lights.Sort((a, b) => LightBudget.Claim(a).CompareTo(LightBudget.Claim(b)));
        lights.RemoveRange(max, lights.Count - max);
    }

    /// <summary>The largest change in any lamp's strength from one step to the next, the eye going down the street 2 cm a step.</summary>
    static float WorstStep(Action<List<PointLight>, int> fit)
    {
        var lamps = Street();
        float worst = 0;
        float[]? was = null;
        for (float z = 0; z > -120; z -= 0.02f)
        {
            var now = Strengths(lamps, new Vector3(0.6f, 2.8f, z), fit);
            if (was is not null)
                for (int i = 0; i < now.Length; i++)
                    worst = Math.Max(worst, Math.Abs(now[i] - was[i]));
            was = now;
        }
        return worst;
    }

    [Fact]
    public void GoingDownAStreetOfLampsNoneComesOnOrGoesOutInAFrame()
    {
        // Cut hard, a lamp went from full to nothing in a step: that's the flash.
        Assert.Equal(1, WorstStep(Hard), 3);
        // Faded toward the cut, 2 cm (a frame at 1.2 m/s, the train's walking pace) moves a lamp's strength a few percent at most.
        float worst = WorstStep(LightBudget.Fit);
        Assert.True(worst < 0.05f, $"a lamp's strength jumped {worst:0.000} in a step");
    }

    [Fact]
    public void TheNearestAreFullAndTheBudgetHeld()
    {
        var lamps = Street();
        var eye = new Vector3(0, 2.8f, -60);
        var lights = lamps.Select(l => new PointLight(l.At - eye, Vector3.One, l.Range)).ToList();
        LightBudget.Fit(lights, Max);
        Assert.Equal(Max, lights.Count);
        // The nearest by their claim, those clear of the fade, at full strength; dimmer the nearer the cut.
        Assert.All(lights.Take(Max - LightBudget.Fade + 1), l => Assert.Equal(1, l.Colour.X));
        for (int i = Max - LightBudget.Fade + 1; i < Max; i++)
            Assert.True(lights[i].Colour.X <= lights[i - 1].Colour.X + 1e-6f);
        Assert.True(lights[^1].Colour.X < 0.5f);
    }

    [Fact]
    public void UnderTheBudgetEveryLightIsLeftAlone()
    {
        var lights = Street().Take(Max).Select(l => new PointLight(l.At, new Vector3(1, 0.7f, 0.4f), l.Range)).ToList();
        var before = lights.ToList();
        LightBudget.Fit(lights, Max);
        Assert.Equal(before, lights);
    }
}
