using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 590 (queue #323, the director, 9 Oct: "we're going to need to add a no monster spawner on the train if the train is
/// right outside of the town doors ... so players can actually clear the train and not feel like they're in some infinite
/// loop"): a train stood short of the terminus' gate, its crew clearing it, has nothing new come for it.
/// </summary>
public class QuietDoorsTests
{
    static readonly string Content = DataFile.FindContentRoot();

    /// <summary>
    /// A four-bot crew's night on frontier:(6 + seed) with the train held <paramref name="short_"/> m short of the terminus' gate for
    /// <paramref name="seconds"/>: every creature that came after the director's grace, by kind and when.
    /// </summary>
    static List<(double At, EnemyKind Kind, double Along)> Held(double short_, double seconds, int seed, double crawl = 0)
    {
        var route = LineGen.Routes.Generate(Content, $"frontier:{6 + seed}", 6);
        var line = route.Build();
        double gate = route.Plan!.Terminus.GateM;
        var seen = new HashSet<int>();
        var boarded = new HashSet<int>();
        var came = new List<(double, EnemyKind, double)>();
        Harness.Run(line, Tuning.Train, Tuning.Player, new HarnessOptions
        {
            Bots = 4,
            Cars = 6,
            Seconds = seconds,
            Seed = seed,
            Link = Ballast.Net.LinkConditions.Perfect,
            StartDistance = gate - short_,
            WalkAboard = false,
            Combat = Tuning.Combat,
            Enemies = Tuning.Enemies,
            Route = route,
            Run = Tuning.Run,
            YardLength = route.GateOr(Tuning.Route.YardLength),
            Holdouts = Tuning.Holdouts,
            // Held there (or crawling in at the yard limit's pace): the driver doesn't take it in.
            Script = (_, w) => w.Train.Dynamics.Velocity = w.Train.Dynamics.Distance < gate - 50 ? crawl : 0,
            Observe = (t, _, w) =>
            {
                double at = t * SimConstants.TickSeconds;
                foreach (var e in w.ActiveEnemies)
                {
                    if (seen.Add(e.Id) && at > 100 && !e.Hazard)
                        came.Add((Math.Round(at, 1), e.Kind, Math.Round(w.Train.Dynamics.Distance)));
                    // Anything boarding the train after the grace, new or not.
                    if (at > 100 && !e.Hazard && e.Attached >= 0 && boarded.Add(e.Id) && !came.Any(c => c.Item2 == e.Kind && c.Item1 == Math.Round(at, 1)))
                        came.Add((Math.Round(at, 1), e.Kind, -1));
                }
            },
        });
        return came;
    }

    [Theory]
    [InlineData(1000, 1)]
    [InlineData(800, 2)]
    [InlineData(1000, 3)]
    [InlineData(200, 1)]
    [InlineData(450, 3)]
    public void StoodOutsideTheTownDoorsNothingNewComes(double short_, int seed)
    {
        // At the yard-limit board (1000 m out, the crawl in from there), the 500 m ban had a creature come every 30-40 s.
        var came = Held(short_, 300, seed);
        Assert.True(came.Count == 0, string.Join("; ", came.Select(c => $"{c.Kind} at {c.At} s")));
    }

    [Fact]
    public void CrawlingInFromTheYardLimitNothingNewComes()
    {
        var came = Held(1100, 300, 2, crawl: 2.5);
        Assert.True(came.Count == 0, string.Join("; ", came.Select(c => $"{c.Kind} at {c.At} s")));
    }
}
