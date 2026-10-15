using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Towns;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// A harbour town's Hope Tower (queue #547, ARCHITECTURE §8 note 819; the World Building board's Cultures tab, the director:
/// "Extremely large lighthouses with bright burning flames above that people of the town call Flames of Hope for the world
/// to find in the darkness"): the harbour's light on its head is the tower, its size what's drawn, its door into the town,
/// no house in it, its words the town's own, and its keeper at its door all night.
/// </summary>
public class HopeTowerTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TownContent Towns = TownContent.Load(Content)!;

    static Town Night(string spec, int people, string form)
    {
        var content = Towns with { Tuning = Towns.Tuning with { Population = [people, people], Forms = Towns.Tuning.Forms with { Force = form } } };
        var route = Routes.Generate(Content, spec, 6);
        double gate = route.GateOr(RouteTuning.Load(Content).YardLength);
        var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), route.Build(), gate - 8), Tuning.Combat);
        world.EnableRun(Tuning.Run, route, gate, authority: true);
        world.EnableTown(content, route, gate, []);
        return world.Town!;
    }

    [Theory]
    [InlineData("frontier:7", 140)]
    [InlineData("local:5", 2900)]
    public void AHarbourTownsLightIsItsHopeTowerWithItsKeeperAtTheDoor(string spec, int people)
    {
        var plan = Night(spec, people, "harbour").Plan;
        var tower = Assert.Single(plan.Fixtures, f => f.Kind == "lighthouse");
        // What it's called, and its words, the town's own.
        Assert.Equal("the Hope Tower", tower.Name);
        Assert.Contains("the Flame of Hope", tower.Text);
        Assert.Contains($"{plan.Name} calls it", tower.Text);
        // As big as it's drawn (HopeKit: its plinth and the coal bay under its hoist, up to its basket's brim).
        Assert.Equal((8.4, 8.4, 84.0), (tower.SolidS, tower.SolidD, tower.Height));
        // On the head, inside the wall; its door into the town.
        Assert.True(plan.Bounds!.Holds(tower.S, tower.D), $"the tower at ({tower.S:0}, {tower.D:0}) outside the wall");
        Assert.Equal(-Math.Sign(tower.D), Math.Sign(tower.FaceD));
        // No house stands in it.
        foreach (var h in plan.Houses)
            foreach (var p in h.Solids())
            {
                var (s0, d0) = h.Rail(p.U0, p.V0);
                var (s1, d1) = h.Rail(p.U1, p.V1);
                bool inside = Math.Max(s0, s1) > tower.S - tower.SolidS && Math.Min(s0, s1) < tower.S + tower.SolidS
                    && Math.Max(d0, d1) > tower.D - tower.SolidD && Math.Min(d0, d1) < tower.D + tower.SolidD;
                Assert.False(inside, $"house {h.Id} stands in the Hope Tower");
            }
        // Its keeper: at its door, off its plinth, looking out over the town, with the Flame's lines.
        var keeper = Assert.Single(plan.People, p => p.Role == "flamekeeper");
        Assert.Equal("keeper of the Flame", keeper.Title);
        Assert.True(Math.Abs(keeper.D - tower.D) > tower.SolidD && Math.Abs(keeper.D - tower.D) < tower.SolidD + 3,
            $"the keeper {Math.Abs(keeper.D - tower.D):0.0} m out from the tower's middle");
        Assert.InRange(keeper.S - tower.S, -1, 1);
        Assert.Equal(Math.Sign(tower.FaceD), Math.Sign(keeper.FaceD));
        Assert.NotEmpty(keeper.Lines);
        Assert.Contains(keeper.Lines, l => Towns.Writing.Roles["flamekeeper"].Lines.Contains(l));
    }

    [Theory]
    [InlineData("")]
    [InlineData("quarry")]
    [InlineData("dykeland")]
    public void OnlyAHarbourTownHasOne(string form)
    {
        var plan = Night("frontier:7", 140, form).Plan;
        Assert.DoesNotContain(plan.Fixtures, f => f.Kind == "lighthouse");
        Assert.DoesNotContain(plan.People, p => p.Role == "flamekeeper");
    }

    [Fact]
    public void ItsTheSameTowerEveryTime()
    {
        static string Of(Town t) => string.Join(";", t.Plan.Fixtures.Where(f => f.Kind == "lighthouse").Select(f => $"{f.S:0.00},{f.D:0.00}")
            .Concat(t.Plan.People.Where(p => p.Role == "flamekeeper").Select(p => $"{p.Name}@{p.S:0.00},{p.D:0.00}")));
        Assert.Equal(Of(Night("frontier:7", 140, "harbour")), Of(Night("frontier:7", 140, "harbour")));
    }
}
