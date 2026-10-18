using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Towns;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// A town's houses don't all look alike (queue #543, ARCHITECTURE §8 note 815; the director's playtest of 13 Oct, in
/// frontier:7's town, a company town: "A lot of the house models are too same same"). Each house's silhouette (its shape:
/// gable or eaves to the street, storeys, roof, dormers, a wing, its porch, its chimney, its size; not its paint) beside its
/// neighbours' down its row: few twins next door, never three alike in a row, and a town of many kinds of house.
/// </summary>
public class TownHouseVarietyTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TownContent Towns = TownContent.Load(Content)!;

    static Town Plan(string spec, int people)
    {
        var content = Towns with { Tuning = Towns.Tuning with { Population = [people, people] } };
        var route = Routes.Generate(Content, spec, 6);
        double gate = route.GateOr(RouteTuning.Load(Content).YardLength);
        var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), route.Build(), gate - 8), Tuning.Combat);
        world.EnableRun(Tuning.Run, route, gate, authority: true);
        world.EnableTown(content, route, gate, []);
        return world.Town!;
    }

    /// <summary>What a house looks like from the street, its colours aside.</summary>
    static string Silhouette(TownHouse h)
    {
        var d = h.Design;
        return $"{d.GableFront}|{d.Storeys}|{d.Roof}|{d.Dormer}x{d.Dormers}|{Math.Sign(d.Ell)}{(d.EllTall ? "t" : "")}|{d.Porch}|{d.Chimney == HouseChimney.Pipe}"
            + $"|{Math.Round(h.Width * 2) / 2}|{Math.Round(h.Depth * 2) / 2}";
    }

    /// <summary>
    /// Each row's shut and lived houses in order along it: those on one side of the line, facing the same way, within a few
    /// metres of each other across it (a street's row bends with it), each beside the next one along.
    /// </summary>
    static List<List<TownHouse>> Rows(TownPlan plan)
    {
        var rows = new List<List<TownHouse>>();
        foreach (var h in plan.Houses.Where(h => h.Kind is HouseKind.Lived or HouseKind.Boarded or HouseKind.Empty).OrderBy(h => h.S))
        {
            var row = rows.FirstOrDefault(r => r[^1].Side == h.Side && Math.Sign(r[^1].D) == Math.Sign(h.D)
                && Math.Abs(r[^1].D - h.D) < 6 && h.S - r[^1].S < 25);
            if (row is null)
                rows.Add([h]);
            else
                row.Add(h);
        }
        return rows;
    }

    [Theory]
    [InlineData("frontier:7", 97)]
    [InlineData("local:3", 1200)]
    [InlineData("local:5", 1200)]
    [InlineData("frontier:3", 1200)]
    public void NoTwinsNextDoorAndNoTownOfOneHouse(string spec, int people)
    {
        var plan = Plan(spec, people).Plan;
        var rows = Rows(plan);
        int pairs = 0, twins = 0;
        foreach (var row in rows)
            for (int i = 1; i < row.Count; i++)
            {
                pairs++;
                if (Silhouette(row[i]) == Silhouette(row[i - 1]))
                    twins++;
                // Never three alike in a row.
                if (i >= 2)
                    Assert.False(Silhouette(row[i]) == Silhouette(row[i - 1]) && Silhouette(row[i - 1]) == Silhouette(row[i - 2]),
                        $"{spec} ({plan.Character}): three alike in a row at {row[i - 2].S:0}..{row[i].S:0}, {row[i].D:0} out: {Silhouette(row[i])}");
            }
        Assert.True(pairs >= 8, $"{spec}: only {pairs} pairs of neighbours");
        // Few twins next door.
        Assert.True(twins <= pairs * 0.25, $"{spec} ({plan.Character}): {twins} of {pairs} neighbours are twins");
        // And many kinds of house: a third as many as there are, up to a dozen.
        var standing = rows.SelectMany(r => r).ToList();
        int kinds = standing.Select(Silhouette).Distinct().Count();
        Assert.True(kinds >= Math.Min(12, standing.Count / 3), $"{spec} ({plan.Character}): {kinds} kinds of house in {standing.Count}");
    }

    [Fact]
    public void ACompanyTownIsStillItsPlansInRuns()
    {
        // frontier:7's town is a company town: its houses built to a few plans, in runs down its streets, and changed since by
        // the families in them; most of them are, not one or two.
        var plan = Plan("frontier:7", 97).Plan;
        Assert.Equal("company", plan.Character);
        var standing = Rows(plan).SelectMany(r => r).ToList();
        // A plan's shape as built: gable or eaves, storeys, roof (what an alteration never changes but for a storey built up).
        var built = standing.Select(h => (h.Design.GableFront, h.Design.Roof, Math.Round(h.Width, 2), Math.Round(h.Depth, 2))).Distinct().Count();
        Assert.InRange(built, 2, Towns.Looks.Characters.Single(c => c.Id == "company").Plans);
        int altered = standing.Count(h => h.Design.Chimney == HouseChimney.Pipe || h.Design.Ell != 0 || h.Design.Shutters >= 0
            || h.Design.Dormer != HouseDormer.None || h.Design.Shingle);
        Assert.True(altered >= standing.Count / 3, $"{altered} of {standing.Count} changed by their families");
    }

    [Fact]
    public void TheHousesAreTheSameEveryTime()
    {
        static string All(Town t) => string.Join(";", t.Plan.Houses.Select(h => Silhouette(h) + h.Design.Paint));
        Assert.Equal(All(Plan("frontier:7", 97)), All(Plan("frontier:7", 97)));
    }
}
