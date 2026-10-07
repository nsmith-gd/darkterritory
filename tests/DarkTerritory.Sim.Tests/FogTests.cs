using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Rail;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Fog that fills the low ground first (linegen plan §14, maritime-rules §4; ARCHITECTURE §8 note 313): each stretch's
/// fog factor reaches the eye, thicker in the low ground and by water, thinner on a crest, and it comes and goes along
/// the line rather than stepping.
/// </summary>
[Collection(nameof(LineGenTests))]
public class FogTests
{
    static readonly string Content = DataFile.FindContentRoot();

    [Theory]
    [InlineData("frontier:7")]
    [InlineData("deadLines:3")]
    public void TheFogThickensInTheLowGroundAndByWaterAndNeverSteps(string spec)
    {
        var route = Routes.Generate(Content, spec, 6);
        var plan = route.Plan!;
        var line = route.Build();
        var c = line.Conditions!;
        var w = LineGenConfig.Load(Content).Tiers.Weather;
        double blend = plan.Rules.FogBlendM;
        Assert.True(plan.Rules.FogAlongLine && blend > 0);

        // Deep in a stretch longer than the blend, the fog is that stretch's own.
        var main = plan.Exposure.Where(x => x.Edge == "main").ToList();
        int low = 0, water = 0;
        foreach (var x in main.Where(x => x.S1 - x.S0 > 2 * blend + 20))
        {
            double mid = (x.S0 + x.S1) / 2;
            Assert.Equal(x.Fog, c.Fog(RailLine.MainPath, mid), 9);
            low += x.Why.Contains("low") ? 1 : 0;
            water += x.Why.Contains("water") ? 1 : 0;
        }
        Assert.True(low + water > 0, $"{spec}: no low ground or water long enough to fill");

        // A shore's track is foggy like low ground.
        Assert.NotEmpty(plan.Shores);
        foreach (var sh in plan.Shores)
        {
            double mid = (sh.S0 + sh.S1) / 2;
            var x = main.Single(x => mid >= x.S0 && mid < x.S1);
            Assert.True(x.Fog >= w.FogWater - 1e-9, $"{spec} {sh.Id}: fog ×{x.Fog} at its middle ({x.Why})");
        }

        // Along the whole line, 10 m at a time: never more than one sample's share of the biggest jump.
        double jump = w.FogLowGround - w.FogCrest, was = c.Fog(RailLine.MainPath, plan.GateM);
        for (double s = plan.GateM + 10; s < plan.TerminusM; s += 10)
        {
            double now = c.Fog(RailLine.MainPath, s);
            Assert.InRange(now, w.FogCrest - 1e-9, Math.Max(w.FogLowGround, w.FogWater) + 1e-9);
            Assert.True(Math.Abs(now - was) <= jump / 9 + 1e-9, $"{spec}: the fog steps {was:0.###} → {now:0.###} at {s:0}");
            was = now;
        }
    }

    [Fact]
    public void FogIsTheSameOnEveryMachineAndOffThePlanItsTheNights()
    {
        var a = Routes.Generate(Content, "frontier:7", 6).Build().Conditions!;
        var b = Routes.Generate(Content, "frontier:7", 6).Build().Conditions!;
        for (double s = 0; s < 20000; s += 137)
            Assert.Equal(a.Fog(RailLine.MainPath, s), b.Fog(RailLine.MainPath, s));
        // Way past the terminus: no exposure there, the night's own fog.
        Assert.Equal(1, a.Fog(RailLine.MainPath, 1e7));
    }
}
