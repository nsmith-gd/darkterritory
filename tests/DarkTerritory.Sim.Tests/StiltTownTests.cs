using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Towns;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The stilt town lived in and solid (queue #569, ARCHITECTURE §8 note 841; the director's playtest of 16 Oct, line 28, in
/// a Flooder town on arrival: "I can sort of clip through the corners of some of them and just see inside that there's
/// nothing. There's also nobody walking around. There's no kind of like culture going on"): a houseboat is solid out to its
/// hull; the Flooders are out among their houseboats all night, through the whole town, at their work or walking with a
/// lamp, never in the water.
/// </summary>
public class StiltTownTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TownContent Towns = TownContent.Load(Content)!;

    static Town Night(string spec, int people, string form = "dykeland")
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
    [InlineData("local:1", 600)]
    [InlineData("local:2", 1500)]
    public void AHouseboatIsSolidOutToItsHull(string spec, int people)
    {
        var town = Night(spec, people);
        var boats = town.Plan.Houses.Where(h => h.Design.Hull && h.Kind != HouseKind.Burnt).ToList();
        Assert.NotEmpty(boats);
        foreach (var h in boats)
        {
            // Just inside each corner of the hull as it's drawn (MaritimeKit.Hull), at the deck: solid, so nobody's eye gets in.
            double u = h.Width / 2 + HouseDesign.HullBeam - 0.05;
            foreach (var (cu, cv) in new[] { (-u, -HouseDesign.HullEnds + 0.05), (u, -HouseDesign.HullEnds + 0.05), (-u, h.Depth + HouseDesign.HullEnds - 0.05), (u, h.Depth + HouseDesign.HullEnds - 0.05) })
            {
                var (s, d) = h.Body(cu, cv);
                Assert.False(town.Free(town.World(s, d, HouseDesign.HullLift - 1.2)), $"{spec}: house {h.Id}'s hull open at ({cu:0.0}, {cv:0.0})");
            }
        }
    }

    [Theory]
    [InlineData("local:1", 600)]
    [InlineData("local:2", 1500)]
    public void TheFloodersAreOutAmongTheirHouseboatsAllNightNeverInTheWater(string spec, int people)
    {
        var town = Night(spec, people);
        var plan = town.Plan;
        var ground = town.Ground;
        bool Dry(double s, double d) => ground.Water(s, d) is not { } w || w - ground.Level(s, d) <= TownGround.Wade;
        var character = HouseCharacters().Single(c => c.Id == "flooders");
        // Out at their work: the character's own, through the town, not only by the square.
        var working = plan.People.Where(p => p.House < 0 && p.Pose is "mend" or "crouch" or "work").ToList();
        var walking = plan.People.Where(p => p.House < 0 && p.Pose == "lantern").ToList();
        Assert.True(working.Count >= 5, $"{spec}: {working.Count} out at their work");
        Assert.True(walking.Count >= 20, $"{spec}: {walking.Count} out with a lamp");
        Assert.True(working.Count + walking.Count <= Towns.Tuning.OutdoorsMax + character.Out[1], "more out than the town has");
        double span(IEnumerable<double> xs) => xs.Max() - xs.Min();
        Assert.True(span(working.Concat(walking).Select(p => p.S)) > 0.5 * span(plan.Houses.Select(h => h.S)),
            $"{spec}: they're all in one end of the town");
        // Where they stand, and everywhere their rounds take them all night: dry, and room to stand.
        foreach (var p in plan.People.Where(p => p.House < 0))
        {
            Assert.True(Dry(p.S, p.D), $"{spec}: {p.Name} ({p.Role}{p.Pose}) stands in the water at ({p.S:0}, {p.D:0})");
            Assert.True(town.Free(town.World(p.S, p.D, p.Up)), $"{spec}: {p.Name} ({p.Role}{p.Pose}) stands in something at ({p.S:0}, {p.D:0})");
        }
        var rounds = TownRounds.Plan(town);
        for (int i = 0; i < rounds.Length; i++)
            if (rounds[i] is { } r && plan.People[i].House < 0)
                foreach (var stop in r.Stops)
                    Assert.True(Dry(stop.S, stop.D), $"{spec}: {plan.People[i].Name}'s round goes into the water at ({stop.S:0}, {stop.D:0})");
    }

    [Fact]
    public void NoOtherTownChanges()
    {
        // Only a character with its people out (houses.json out) has them; the others' are as they were.
        Assert.All(HouseCharacters().Where(c => c.Id != "flooders"), c => Assert.Equal([0, 0], c.Out));
        var plan = Night("frontier:7", 97, "").Plan;
        Assert.DoesNotContain(plan.People, p => p.Pose is "mend" or "work" && p.House < 0);
    }

    static IReadOnlyList<HouseCharacter> HouseCharacters() => HouseLooks.Load(Content).Characters;
}
