using System.Text.Json;
using Ballast;
using DarkTerritory.Sim.Campaign;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Towns;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// A town's standing with the crew (queue #652, ARCHITECTURE §8 note 1024; GDD §3.1 "A town remembers"): a creature brought
/// in through its gate makes it colder to them, kept per town in the campaign save and easing night by night. While it's cold
/// its gatekeeper says why, its people are wary, its doors stay shut, its blacksmith asks more (that town's only), and with
/// fewer guards on its yard fights the next one worse. Off, it remembers only its burnt houses (note 608).
/// </summary>
public class TownStandingTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TownContent Towns = TownContent.Load(Content)!;
    static readonly StandingTuning S = Towns.Tuning.Standing;
    static readonly CampaignTuning C = DataFile.Load<CampaignTuning>(Path.Combine(Content, CampaignTuning.File));
    static readonly TownAt Boudreau = new("Fort Boudreau", "coal", 1234);

    static RunReport Night(RunEnd end, int brought) => new(end, 1800, 27, 2, 0, 2, 900, 10, 0, 0, 890, 1, 0)
    {
        BroughtIn = brought,
        TownBurnt = brought * Tuning.Run.BroughtIn.Burns,
    };

    static TownPlan Plan(int cold, StandingTuning? standing = null, int burnt = 0) => TownGenerator.Generate(
        Towns with { Tuning = Towns.Tuning with { Standing = standing ?? S } },
        new TownSite(Boudreau.Name, Boudreau.Industry, 300, Boudreau.Seed, [], null, burnt) { Cold = cold });

    /// <summary>A night gone by elsewhere, settled as the app settles it.</summary>
    static CampaignState Elsewhere(CampaignState s, StandingTuning standing) =>
        Campaign.Campaign.Arrived(Campaign.Campaign.Settle(s, Night(RunEnd.Delivered, 0), C, standing), Night(RunEnd.Delivered, 0),
            new TownAt("Grieve", "farm", 9), standing);

    [Fact]
    public void TheDefaultIsAColderWelcomeAndDearerUpgrades()
    {
        Assert.Equal(StandingMode.Welcome, S.Mode);
        Assert.True(S.PerCreature > 0 && S.EasePerNight > 0 && S.Dearer > 0 && S.Wary > 0 && S.Doors > 0 && S.Waves > 0);
        Assert.Equal(1, S.Fight(100));
        Assert.Equal(0, S.Burns(100));
    }

    [Fact]
    public void TheStandingFallsWithACreatureBroughtInAndEasesNightByNight()
    {
        var s = Campaign.Campaign.New(C, 1, "test", 7);
        Assert.Empty(s.Standing);
        var brought = Campaign.Campaign.Arrived(s, Night(RunEnd.Delivered, 1), Boudreau, S);
        Assert.Equal(S.PerCreature, brought.Standing[Boudreau.Key]);
        Assert.Equal(S.PerCreature, brought.Town!.Cold);
        // The next night departs from it as cold as that: every machine makes the same wary town from the spec.
        string spec = Campaign.Campaign.RouteOf(brought, new Contract(RouteTier.Frontier, 3, 100));
        Assert.EndsWith($"@Fort Boudreau/coal/1234/{Tuning.Run.BroughtIn.Burns}/{S.PerCreature}", spec);
        var route = Routes.Generate(Content, spec, 4);
        Assert.Equal(S.PerCreature, route.Plan!.Fortress.TownCold);
        Assert.Equal(S.PerCreature, TownSite.Of(route, 300, [], Towns).Cold);

        // Night by night, wherever the crew is, it eases; gone, it's off the save.
        var now = brought;
        int was = S.PerCreature, nights = 0;
        while (now.Standing.ContainsKey(Boudreau.Key))
        {
            now = Elsewhere(now, S);
            nights++;
            int cold = now.Standing.GetValueOrDefault(Boudreau.Key);
            Assert.Equal(Math.Max(0, was - S.EasePerNight), cold);
            was = cold;
            Assert.True(nights < 50);
        }
        Assert.Equal((S.PerCreature + S.EasePerNight - 1) / S.EasePerNight, nights);

        // Two brought in at once is colder, never past its most; and another on a cold town adds to what's left.
        var two = Campaign.Campaign.Arrived(s, Night(RunEnd.Delivered, 2), Boudreau, S);
        Assert.Equal(Math.Min(S.Most, 2 * S.PerCreature), two.Standing[Boudreau.Key]);
        var eased = Elsewhere(brought, S);
        var again = Campaign.Campaign.Arrived(eased, Night(RunEnd.Delivered, 1), Boudreau, S);
        Assert.Equal(Math.Min(S.Most, S.PerCreature - S.EasePerNight + S.PerCreature), again.Standing[Boudreau.Key]);
        // Coming back with nothing on the cars, it's as cold as it still is.
        var back = Campaign.Campaign.Arrived(eased, Night(RunEnd.Delivered, 0), Boudreau, S);
        Assert.Equal(S.PerCreature - S.EasePerNight, back.Town!.Cold);
        // A night that didn't arrive brings nothing in.
        Assert.Same(s, Campaign.Campaign.Arrived(s, Night(RunEnd.Derailed, 1), Boudreau, S));
    }

    [Fact]
    public void ItsBlacksmithAsksMoreThereAndNowhereElse()
    {
        var s = Campaign.Campaign.New(C, 1, "test", 7) with { Scrip = 100000 };
        var u = C.Upgrades.First(x => x.Effect.Count > 0);
        double list = Campaign.Campaign.UpgradeCost(C, s with { Town = Boudreau }, u, S);
        Assert.Equal(Campaign.Campaign.UpgradeCost(C, s, u), list);

        var cold = Campaign.Campaign.Arrived(s, Night(RunEnd.Delivered, 1), Boudreau, S);
        double dearer = Campaign.Campaign.UpgradeCost(C, cold, u, S);
        Assert.Equal(Math.Round(u.CostShare * Campaign.Campaign.NextCarCost(C, s) * (1 + S.Dearer * S.PerCreature / 100.0)), dearer);
        Assert.True(dearer > list);
        // Bought at that price.
        var bought = Campaign.Campaign.BuyUpgrade(C, cold, u.Id, S);
        Assert.True(bought.Ok);
        Assert.Equal(cold.Scrip - dearer, bought.State.Scrip);
        // In another town the same thing's the list price, though the crew's still owed a cold welcome back at Boudreau.
        var away = Elsewhere(cold, S);
        Assert.True(away.Standing.ContainsKey(Boudreau.Key));
        Assert.Equal(list, Campaign.Campaign.UpgradeCost(C, away, u, S));
        // And easing, it comes back down to the list price.
        var back = Campaign.Campaign.Arrived(away, Night(RunEnd.Delivered, 0), Boudreau, S);
        double eased = Campaign.Campaign.UpgradeCost(C, back, u, S);
        Assert.InRange(eased, list + 1, dearer - 1);
    }

    [Fact]
    public void ItsLinesGoColdItsGatekeeperSaysWhyAndItsDoorsStayShut()
    {
        var warm = Plan(0);
        var cold = Plan(80);
        var said = Towns.Writing.BroughtIn!;
        Assert.Equal(0, warm.Cold);
        Assert.Equal(80, cold.Cold);

        // The gatekeeper: the gate's law first, as ever, then why the town's cold to them.
        var gate = cold.People.Single(p => p.Role == "gatekeeper");
        Assert.Equal(warm.People[gate.Id].Lines[0], gate.Lines[0]);
        Assert.Contains(gate.Lines[1], said.Standing.Select(l => l.Replace("{town}", cold.Name)));
        Assert.Contains("brought one", gate.Lines[1]);
        // The blacksmith says what it costs now.
        var smith = cold.People.Single(p => p.Role == "blacksmith");
        Assert.Contains(smith.Lines[0], said.Smith);
        // And the buyer what they pay now (note 1027).
        Assert.Contains(cold.People.Single(p => p.Role == "buyer").Lines[0], said.Buyer.Select(l => l.Replace("{town}", cold.Name)));

        // Of everyone else, about wary x cold open on a wary line, and the rest say what they said.
        var wary = said.Wary.Select(l => l.Replace("{town}", cold.Name)).ToHashSet();
        var folk = cold.People.Where(p => p.Role is not ("gatekeeper" or "guard" or "blacksmith" or "buyer")).ToList();
        int opened = folk.Count(p => wary.Contains(p.Lines[0]));
        Assert.InRange(opened, (int)(folk.Count * S.Wary * 0.8 * 0.6), (int)Math.Ceiling(folk.Count * Math.Min(1, S.Wary * 0.8 * 1.4)));
        foreach (var p in folk)
            Assert.Equal(warm.People[p.Id].Lines, wary.Contains(p.Lines[0]) ? p.Lines.Skip(1) : p.Lines);

        // Doors knocked on: about doors x cold kept shut, saying so; the open houses still open, nothing else moved.
        var knocked = warm.Houses.Where(h => h.Kind == HouseKind.Lived && h.Layout is null).ToList();
        int shut = cold.Houses.Count(h => h.Text != warm.Houses[h.Id].Text);
        Assert.InRange(shut, (int)(knocked.Count * S.Doors * 0.8 * 0.6), (int)Math.Ceiling(knocked.Count * Math.Min(1, S.Doors * 0.8 * 1.4)));
        foreach (var h in cold.Houses)
        {
            var was = warm.Houses[h.Id];
            Assert.Equal(JsonSerializer.Serialize(was with { Text = h.Text }), JsonSerializer.Serialize(h));
            if (h.Text != was.Text)
            {
                Assert.True(was.Kind == HouseKind.Lived && was.Layout is null);
                Assert.Contains(h.Family, h.Text);
            }
        }
        // Warmer, fewer; warm, none.
        Assert.True(Plan(20).People.Count(p => p.Lines.Count > 0 && wary.Contains(p.Lines[0])) < opened);
        Assert.Equal(JsonSerializer.Serialize(warm), JsonSerializer.Serialize(Plan(0)));
    }

    [Fact]
    public void ItsTheSameTownOnEveryMachine()
    {
        Assert.Equal(JsonSerializer.Serialize(Plan(40)), JsonSerializer.Serialize(Plan(40)));
        // And everything the warm town has (but the lines, doors and the cold itself) is where it was: names, places, houses.
        var warm = Plan(0);
        var cold = Plan(40);
        Assert.Equal(warm.People.Select(p => (p.Id, p.Name, p.Role, p.S, p.D)), cold.People.Select(p => (p.Id, p.Name, p.Role, p.S, p.D)));
        Assert.Equal(warm.Houses.Select(h => (h.Id, h.Kind, h.Family)), cold.Houses.Select(h => (h.Id, h.Kind, h.Family)));
        Assert.Equal(JsonSerializer.Serialize(warm.Fixtures), JsonSerializer.Serialize(cold.Fixtures));
    }

    [Fact]
    public void WithItOffTheTownRemembersOnlyItsBurntHouses()
    {
        var off = S with { Mode = StandingMode.Off };
        var s = Campaign.Campaign.Arrived(Campaign.Campaign.New(C, 1, "test", 7), Night(RunEnd.Delivered, 1), Boudreau, off);
        Assert.Empty(s.Standing);
        Assert.Equal(0, s.Town!.Cold);
        Assert.Equal(Tuning.Run.BroughtIn.Burns, s.Town.Burnt);
        var u = C.Upgrades.First(x => x.Effect.Count > 0);
        Assert.Equal(Campaign.Campaign.UpgradeCost(C, s, u), Campaign.Campaign.UpgradeCost(C, s with { Town = Boudreau with { Cold = 60 } }, u, off));
        // A town built cold with it off is the warm one.
        Assert.Equal(JsonSerializer.Serialize(Plan(0, off)), JsonSerializer.Serialize(Plan(60, off) with { Cold = 0 }));
        Assert.Equal(0, Plan(60, off).Cold);
    }

    [Fact]
    public void WithFewerGuardsItsYardFightsTheNextOneWorse()
    {
        var guards = S with { Mode = StandingMode.Guards };
        Assert.True(guards.Fight(80) > 1);
        Assert.True(guards.Burns(80) > 0);
        Assert.Equal(1, guards.Fight(0));
        Assert.Equal(0, guards.Burns(0));

        var warm = Plan(0, guards);
        var cold = Plan(80, guards);
        int all = warm.People.Count(p => Town.TurnsOut(p, warm, guards));
        int out80 = cold.People.Count(p => Town.TurnsOut(p, cold, guards));
        Assert.True(out80 < all);
        // The gatekeeper always turns out; the welcome's default keeps every guard.
        Assert.True(Town.TurnsOut(cold.People.Single(p => p.Role == "gatekeeper"), cold, guards));
        Assert.Equal(all, Plan(80).People.Count(p => Town.TurnsOut(p, Plan(80), S)));

        // In a night: the arrival town's cold sets how long the yard fights what's brought in and how much it burns.
        var route = Routes.Generate(Content, "frontier:7", 6);
        double gate = route.GateOr(RouteTuning.Load(Content).YardLength);
        var arrival = TownAt.Terminus(route, LineGenContent.Cached(Content).Config.Tiers.Fortress.Identities)! with { Cold = 80 };
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), route.Build(), gate - 8);
        var world = new World(train, Tuning.Combat);
        world.EnableRun(Tuning.Run, route, gate, authority: true);
        world.EnableTown(Towns with { Tuning = Towns.Tuning with { Standing = guards } }, route, gate, [], arrival: arrival);
        Assert.Equal(80, world.Arrival!.Plan.Cold);
        Assert.Equal(Tuning.Run.BroughtIn.FightSeconds * guards.Fight(80), world.Run!.FightSeconds, 6);
        Assert.Equal(Tuning.Run.BroughtIn.Burns + guards.Burns(80), world.Run.BurnsEach);
        Assert.True(world.Arrival.Plan.People.Count(world.Arrival.TurnsOut) < world.Arrival.Plan.People.Count(Town.Guards));
    }

    [Fact]
    public void TheStandingRoundTripsTheSaveAndAnOldSaveIsWarm()
    {
        var s = Campaign.Campaign.Arrived(Campaign.Campaign.New(C, 1, "test", 7), Night(RunEnd.Delivered, 1), Boudreau, S);
        var back = JsonSerializer.Deserialize<CampaignState>(JsonSerializer.SerializeToUtf8Bytes(s, DataFile.Options), DataFile.Options)!;
        Assert.Equal(s.Standing, back.Standing);
        Assert.Equal(s.Town, back.Town);
        Assert.Equal(TownAt.Parse(s.Town!.Spec), back.Town);

        // A save from before it: no standing, its town warm.
        var json = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(s, DataFile.Options))!.AsObject();
        json.Remove("standing");
        json["town"]!.AsObject().Remove("cold");
        var old = JsonSerializer.Deserialize<CampaignState>(json.ToJsonString(), DataFile.Options)!;
        Assert.Empty(old.Standing);
        Assert.Equal(0, old.Town!.Cold);
        var u = C.Upgrades.First(x => x.Effect.Count > 0);
        Assert.Equal(Campaign.Campaign.UpgradeCost(C, old, u), Campaign.Campaign.UpgradeCost(C, old, u, S));
        // And an older spec reads as warm.
        Assert.Equal(0, TownAt.Parse("Fort Boudreau/coal/1234/2")!.Cold);
        Assert.Null(TownAt.Parse("Fort Boudreau/coal/1234/2/x"));
    }
}
