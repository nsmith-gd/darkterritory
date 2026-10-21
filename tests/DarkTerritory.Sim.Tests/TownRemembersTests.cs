using Ballast;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Towns;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The town remembers the creature brought in (queue #340, ARCHITECTURE §8 note 608; note 589's not-yets): its guards turn
/// out to the yard while it's fought, the houses nearest where the train come in stands burn (note 966), and the campaign carries them burnt to the next
/// visit, where the town's people say so.
/// </summary>
public class TownRemembersTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TownContent Towns = TownContent.Load(Content)!;
    static readonly IReadOnlyList<string> Identities = LineGenContent.Cached(Content).Config.Tiers.Fortress.Identities;

    static readonly CampaignTuning C = DataFile.Load<CampaignTuning>(Path.Combine(Content, CampaignTuning.File));

    static RunReport Report(RunEnd end, int burnt) => new(end, 1800, 27, 2, 0, 2, 900, 10, 0, 0, 890, 1, 0) { TownBurnt = burnt };

    static TownPlan Plan(int burnt) => TownGenerator.Generate(Towns, new TownSite("Fort Boudreau", "coal", 300, 1234, [], null, burnt));

    [Fact]
    public void ATownCarriesItsBurntHousesInTheSpec()
    {
        var town = new TownAt("Fort Boudreau", "coal", 1234);
        Assert.Equal("Fort Boudreau/coal/1234", town.Spec);
        Assert.Equal(town, TownAt.Parse(town.Spec));
        var burnt = town with { Burnt = 2 };
        Assert.Equal("Fort Boudreau/coal/1234/2", burnt.Spec);
        Assert.Equal(burnt, TownAt.Parse(burnt.Spec));
        Assert.Equal(town.Key, burnt.Key);
        Assert.Null(TownAt.Parse("Fort Boudreau/coal/1234/x"));
        // Through a route spec to the departure fortress, and so its town.
        var route = Routes.Generate(Content, TownAt.With("frontier:3", burnt), 4);
        Assert.Equal(2, route.Plan!.Fortress.TownBurnt);
        Assert.Equal(2, TownSite.Of(route, 300, [], Towns).Burnt);
    }

    [Fact]
    public void TheHousesBurntAreTheLivedInOnesNearestTheTrainAndStayTheSame()
    {
        var whole = Plan(0);
        var harmed = Plan(2);
        Assert.Equal(300, whole.Gate);
        Assert.Equal(0, whole.Burnt);
        Assert.Equal(2, harmed.Burnt);
        var gone = TownGenerator.Harmed(whole, 2);
        Assert.Equal(2, gone.Count);
        Assert.All(gone, h => Assert.Equal(HouseKind.Lived, h.Kind));
        // No lived-in house nearer where the train come in stands is left standing.
        double farthest = gone.Max(h => TownGenerator.FromWayIn(whole, h));
        Assert.DoesNotContain(whole.Houses, h => h.Kind == HouseKind.Lived && TownGenerator.FromWayIn(whole, h) < farthest && !gone.Contains(h));
        // Burnt out, shut, nobody home, and the look says whose it was; everything else as it was.
        foreach (var h in harmed.Houses)
        {
            var was = whole.Houses[h.Id];
            if (gone.Any(g => g.Id == h.Id))
            {
                Assert.Equal(HouseKind.Burnt, h.Kind);
                Assert.Null(h.Layout);
                Assert.Contains(h.Family, h.Text);
            }
            else
                Assert.Equal(System.Text.Json.JsonSerializer.Serialize(was), System.Text.Json.JsonSerializer.Serialize(h));
        }
        // The next burnt is the next nearest: tonight's fires (on a town with some already) and the town come back to agree.
        Assert.Equal(TownGenerator.Harmed(whole, 3)[2].Id, Assert.Single(TownGenerator.Harmed(harmed, 1)).Id);
        Assert.Empty(TownGenerator.Harmed(whole, 0));
    }

    [Fact]
    public void ItsGatekeeperAndGuardsSaySoFirst()
    {
        var whole = Plan(0);
        var harmed = Plan(1);
        var said = Towns.Writing.BroughtIn!;
        foreach (var p in harmed.People.Where(Town.Guards))
        {
            var before = whole.People[p.Id].Lines;
            Assert.Equal(before.Count + 1, p.Lines.Count);
            int at = p.Role == "gatekeeper" ? 1 : 0;
            var options = (p.Role == "gatekeeper" ? said.Gate : said.Guard).Select(l => l.Replace("{town}", harmed.Name));
            Assert.Contains(p.Lines[at], options);
            // The law first at the gate, as ever.
            if (p.Role == "gatekeeper")
                Assert.Equal(before[0], p.Lines[0]);
        }
        Assert.Contains(harmed.People, p => p.Role == "gatekeeper");
        Assert.All(harmed.People.Where(p => !Town.Guards(p)), p => Assert.Equal(whole.People[p.Id].Lines, p.Lines));
    }

    [Fact]
    public void TheCampaignRemembersTheHarmAgainstTheTown()
    {
        var town = new TownAt("Fort Boudreau", "coal", 1234);
        var s = Campaign.Campaign.New(C, 1, "test", 7);
        var delivered = Report(RunEnd.Delivered, 1);
        var first = Campaign.Campaign.Arrived(s, delivered, town);
        Assert.Equal(town with { Burnt = 1 }, first.Town);
        Assert.Equal(1, first.Burnt[town.Key]);
        // Elsewhere and back: still missing it, and another.
        var elsewhere = Campaign.Campaign.Arrived(first, Report(RunEnd.Delivered, 0), new TownAt("Grieve", "farm", 9));
        Assert.Equal(0, elsewhere.Town!.Burnt);
        var back = Campaign.Campaign.Arrived(elsewhere, delivered, town);
        Assert.Equal(2, back.Town!.Burnt);
        Assert.Equal(2, back.Burnt[town.Key]);
        // The next night from it departs from the town with them burnt.
        Assert.EndsWith("@Fort Boudreau/coal/1234/2", Campaign.Campaign.RouteOf(back, new Contract(RouteTier.Frontier, 3, 100)));
        // A night that didn't arrive leaves it all as it was.
        Assert.Same(back, Campaign.Campaign.Arrived(back, Report(RunEnd.Derailed, 1), town));
    }

    [Fact]
    public void TheGuardsTurnOutToTheYardWhileTheFightLasts()
    {
        var route = Routes.Generate(Content, "frontier:7", 6);
        double gate = route.GateOr(RouteTuning.Load(Content).YardLength);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), route.Build(), gate - 8);
        var world = new World(train, Tuning.Combat);
        world.EnableRun(Tuning.Run, route, gate, authority: true);
        world.EnableTown(Towns, route, gate, [], arrival: TownAt.Terminus(route, Identities));
        var town = world.Arrival!;
        var guards = town.Plan.People.Where(Town.Guards).ToList();
        Assert.NotEmpty(guards);
        double start = town.Clock;
        var posted = guards.Select(town.Now).ToList();
        // (Where their rounds would have them a minute on, with no fight.)
        town.Clock = start + 60;
        var later = guards.Select(town.Now).ToList();
        town.Clock = start;

        // A creature fought 40 m inside the gate. Note 951: at first they're where they stood, looking to it; then they run out
        // to it, each in turn; and a minute on, every guard's out on the yard round it, facing it, their pieces up.
        double along = route.Plan!.Terminus.GateM + 40;
        town.Fight = town.Along(along);
        var fight = town.World(town.Fight.Value, 0);
        Assert.Equal(train.Line.Sample(along).Position.X, fight.X, 3);
        Assert.Equal(train.Line.Sample(along).Position.Z, fight.Z, 3);
        for (int i = 0; i < guards.Count; i++)
        {
            // (Where they stood; or, posted far off, on their way in already, no more than 20 m out from their place.)
            var now = town.Now(guards[i]);
            var place = town.OnTheYard(i, town.Fight.Value, guards[i].Up).Feet;
            Assert.True(now.Feet == posted[i].Feet || ((place - now.Feet) with { Y = 0 }).Length <= 20.01);
            Assert.Equal("idle", now.Act);
            var look = (fight - now.Feet) with { Y = 0 };
            Assert.True(look.Length < 1e-6 || Double3.Dot(look.Normalized, now.Facing) > 0.99, "looking to it");
        }
        town.Clock = start + 2.5;
        var running = guards.Select(town.Now).Where(p => p.Act == "run").ToList();
        Assert.NotEmpty(running);
        Assert.All(running, r => Assert.True(r.Walking));
        // Running toward it: nearer it than where they set off from, and faced the way they're going.
        for (int i = 0; i < guards.Count; i++)
            if (town.Now(guards[i]) is { Act: "run" } r)
            {
                var there = town.OnTheYard(guards.Take(i).Count(), town.Fight.Value, guards[i].Up).Feet;
                Assert.True(((there - r.Feet) with { Y = 0 }).Length < Math.Min(20, ((there - posted[i].Feet) with { Y = 0 }).Length));
                Assert.True(Double3.Dot(((there - r.Feet) with { Y = 0 }).Normalized, r.Facing) > 0.99);
            }
        // All there well within the fight (run.json broughtIn.fightSeconds), and a minute on still.
        foreach (double at in new[] { Tuning.Run.BroughtIn.FightSeconds - 1.5, 60 })
        {
            town.Clock = start + at;
            Assert.All(guards, g => Assert.Equal("guard", town.Now(g).Act));
        }
        foreach (var g in guards)
        {
            var now = town.Now(g);
            Assert.Equal("guard", now.Act);
            var to = (fight - now.Feet) with { Y = 0 };
            Assert.InRange(to.Length, 4, 20);
            Assert.True(Double3.Dot(to.Normalized, now.Facing) > 0.99);
        }
        // Each in a place of their own.
        var feet = guards.Select(g => town.Now(g).Feet).ToList();
        for (int i = 0; i < feet.Count; i++)
            for (int j = i + 1; j < feet.Count; j++)
                Assert.True((feet[i] - feet[j]).Length > 2);
        // Everyone else on their round as before; and the fight over, the guards back to theirs.
        Assert.All(town.Plan.People.Where(p => !Town.Guards(p)), p => Assert.NotEqual("guard", town.Now(p).Act));
        town.Fight = null;
        Assert.Equal(later, guards.Select(town.Now).ToList());
    }
}
