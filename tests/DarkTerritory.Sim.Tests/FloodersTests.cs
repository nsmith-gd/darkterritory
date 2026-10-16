using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Towns;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The Flooders (queue #549, ARCHITECTURE §8 note 822; the World Building board's Cultures tab, the director: "Towns made
/// entirely of houseboats on stilts ... a cult who insist the comet that brought the pestilence ... was an act of God and
/// God will cleanse the Earth with a great flood once again"): a marsh or salt-flat town whose houses are the Flooders'
/// has every shut house a houseboat, its open ones on the Sim's floor as ever; its people keep the creed, heard and posted;
/// and no other town has either.
/// </summary>
public class FloodersTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TownContent Towns = TownContent.Load(Content)!;

    static TownPlan Night(string spec, int people, string form)
    {
        var content = Towns with { Tuning = Towns.Tuning with { Population = [people, people], Forms = Towns.Tuning.Forms with { Force = form } } };
        var route = Routes.Generate(Content, spec, 6);
        double gate = route.GateOr(RouteTuning.Load(Content).YardLength);
        var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), route.Build(), gate - 8), Tuning.Combat);
        world.EnableRun(Tuning.Run, route, gate, authority: true);
        world.EnableTown(content, route, gate, []);
        return world.Town!.Plan;
    }

    [Theory]
    [InlineData("local:1", 600)]
    [InlineData("local:2", 1500)]
    public void AFlooderTownsShutHousesAreHouseboatsAndItsPeopleKeepTheCreed(string spec, int people)
    {
        var plan = Night(spec, people, "dykeland");
        Assert.Equal("flooders", plan.Character);
        var creed = Towns.Writing.Creeds.Single(c => c.Id == "flooders");
        // Every shut house a houseboat; the open ones (their rooms on the Sim's floor) never.
        var shut = plan.Houses.Where(h => h.Layout is null && h.Kind != HouseKind.Burnt).ToList();
        Assert.NotEmpty(shut);
        Assert.All(shut, h => Assert.True(h.Design.Hull, $"house {h.Id} ({h.Kind}) isn't a houseboat"));
        Assert.All(plan.Houses.Where(h => h.Layout is not null), h => Assert.False(h.Design.Hull, $"open house {h.Id} on a hull"));
        // Its people say the creed, and its board posts the Water Book after the custom's order and the town's own.
        Assert.True(plan.People.Count(p => p.Lines.Any(l => creed.Lines.Contains(l))) >= Math.Min(4, creed.Lines.Length),
            "the creed hardly heard");
        var board = plan.Papers.Where(p => p.OnBoard).ToList();
        Assert.Equal(creed.Notes[0].Title, board[1 + Towns.Tuning.BoardOwn].Title);
    }

    [Theory]
    [InlineData("frontier:7", 600, "dykeland")]
    [InlineData("frontier:7", 600, "harbour")]
    [InlineData("local:1", 600, "")]
    public void NoOtherTownHasEither(string spec, int people, string form)
    {
        var plan = Night(spec, people, form);
        Assert.NotEqual("flooders", plan.Character);
        Assert.DoesNotContain(plan.Houses, h => h.Design.Hull);
        var creed = Towns.Writing.Creeds.Single(c => c.Id == "flooders");
        Assert.DoesNotContain(plan.People, p => p.Lines.Any(l => creed.Lines.Contains(l)));
        Assert.DoesNotContain(plan.Papers, p => creed.Notes.Any(n => n.Title == p.Title && n.Text == p.Text));
    }

    [Fact]
    public void TheFloodersAreTheSameEveryTime()
    {
        static string Of(TownPlan p) => string.Join(";", p.Houses.Select(h => $"{h.Id}:{h.Design.Hull}")) + "|"
            + string.Join(";", p.Papers.Select(x => x.Title));
        Assert.Equal(Of(Night("local:1", 600, "dykeland")), Of(Night("local:1", 600, "dykeland")));
    }
}
