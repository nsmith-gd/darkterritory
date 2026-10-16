using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The ruins of fortress towns begun and stamped flat by the Wakers (queue #551, ARCHITECTURE §8 note 824; the World
/// Building board's Cultures tab, the director: "Ruins of fortress towns that once were. They're always small beginnings of
/// a fortress that were destroyed by the Wakers"): out beside the line in every country, now and then, never on a stop's
/// ground, standing solid where their walls stand, open where they're trodden; the same every time.
/// </summary>
[Collection(nameof(LineGenTests))]
public class FortRuinTests
{
    static readonly string Content = DataFile.FindContentRoot();

    static (Route.Route Route, LinesideProps Side, IReadOnlyList<Fort> Forts) Night(string spec)
    {
        var route = Routes.Generate(Content, spec, 6);
        var line = route.Build();
        var forts = Fortresses.Of(route, line, 600, Tuning.Run.TerminusZone);
        return (route, LinesideProps.Of(route, line)!, forts);
    }

    static readonly string[] Nights = ["local:1", "local:2", "local:3", "local:5", "frontier:2", "frontier:3", "frontier:5", "frontier:7", "deadLines:3"];

    [Fact]
    public void NowAndThenARuinStandsBesideTheLineNeverOnAStopsGround()
    {
        int ruins = 0;
        double km = 0;
        foreach (var spec in Nights)
        {
            var (route, side, forts) = Night(spec);
            var line = route.Build();
            km += line.Length / 1000;
            foreach (var p in side.Props(0, line.Length).Where(p => p.Species == "fortRuin"))
            {
                ruins++;
                // Out off the line, in the biome's reach, and its walls stand (it's solid where it stands).
                Assert.InRange(Math.Abs(p.Lateral), 27.9, 48.1);
                Assert.False(side.InClearing(p.Along, p.Lateral), $"{spec}: a ruin on a stop's ground at {p.Along:0}");
                Assert.False(LinesideProps.InsideAFort(forts, p.Along, p.Lateral), $"{spec}: a ruin inside a fortress at {p.Along:0}");
                Assert.True(side.WallsOf(p).Count() > 20, $"{spec}: a ruin at {p.Along:0} with {side.WallsOf(p).Count()} walls");
            }
        }
        // Two nights in three or so (biomes.json: 0.025 a 150 m block, where there's room), not one a stop.
        Assert.True(ruins >= 2, $"{ruins} ruins in {km:0} km");
        Assert.True(ruins <= km / 4, $"{ruins} ruins in {km:0} km");
    }

    [Fact]
    public void TheRuinsAreTheSameEveryTime()
    {
        static string Of(LinesideProps side, double length) => string.Join(";", side.Props(0, length).Where(p => p.Species == "fortRuin")
            .Select(p => $"{p.Along:0.0},{p.Lateral:0.0},{p.Variant},{p.Yaw:0.000}"));
        var (route, a, _) = Night("frontier:3");
        var (_, b, _) = Night("frontier:3");
        double length = route.Build().Length;
        Assert.Equal(Of(a, length), Of(b, length));
    }
}
