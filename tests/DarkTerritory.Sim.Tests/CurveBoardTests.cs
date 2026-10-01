using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Rail;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// T111 playtest ("curves in tracks should have signs that label their top speed they can have before they derail"): the
/// line speed isn't a governor (steam drives the speed, T97), so every curve that would derail the engine on full steam has
/// a board standing before it, at no more than the curve takes.
/// </summary>
public class CurveBoardTests
{
    [Theory]
    [InlineData("frontier:3")]
    [InlineData("frontier:7")]
    [InlineData("deepTerritory:1")]
    public void EveryCurveThatDerailsUnderTopSpeedHasAStandingBoard(string spec)
    {
        var content = DataFile.FindContentRoot();
        var t = LineGenConfig.Load(content).Tiers;
        var plan = Routes.Generate(content, spec, 10).Plan!;
        var main = plan.Edge("main");
        var line = new RailLine(new LineDefinition("main", main.Segments));
        var c = t.Curves;
        var boards = plan.Signage.Where(s => s.Edge == "main" && s.Type == "speedBoard" && s.State == SignState.Intact).ToList();
        double start = -1, minR = double.MaxValue;
        for (double s = 0; s <= line.Length + 5; s += 5)
        {
            double k = s <= line.Length ? Math.Abs(line.Sample(s).Curvature) : 0;
            if (k > 1e-9 && Math.Sqrt(c.ADerail / k) < c.BoardDerailBelow)
            {
                if (start < 0)
                    start = s;
                minR = Math.Min(minR, 1 / k);
                continue;
            }
            if (start < 0)
                continue;
            double derails = Math.Sqrt(c.ADerail * minR);
            double from = start - t.Authority.BoardBeforeM - 1500;
            Assert.True(boards.Any(b => b.S >= from && b.S <= start + 1 && b.Value is { } v && v < derails),
                $"no board before the R {minR:0} m curve at {start:0} (derails at {derails:0.0} m/s)");
            start = -1;
            minR = double.MaxValue;
        }
    }
}
