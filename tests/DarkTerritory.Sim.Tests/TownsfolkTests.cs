using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Towns;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// A town's people (queue #90, ARCHITECTURE §8 note 353). The director, 8 Oct 2026: "We need townsfolk models who wear some
/// sort of respirator mask or oxygen mask or other breathing apparatuses to indicate the air is foul": each breathes
/// through gear of the kinds their town's character keeps (houses.json <c>gear</c>), drawn from who they are. And "There
/// needs to be a behaviour loop for all the NPCs, its weird that so many of them are just standing around doing nothing":
/// each goes about a round on the night's clock, clear of what's solid, never two in one place, stopping for whoever
/// talks to them.
/// </summary>
public class TownsfolkTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TownContent Towns = TownContent.Load(Content)!;

    /// <summary>The town on <paramref name="spec"/>'s departure fortress, of <paramref name="people"/>, its character <paramref name="character"/> (any when null).</summary>
    static Town Plan(string spec, int people, string? character = null) => Night(spec, people, character).Town;

    static (World World, Town Town) Night(string spec, int people, string? character = null)
    {
        var looks = character is null ? Towns.Looks
            : Towns.Looks with { Characters = [.. Towns.Looks.Characters.Where(c => c.Id == character)], ByTrade = [] };
        var content = Towns with { Tuning = Towns.Tuning with { Population = [people, people] }, Looks = looks };
        var route = Routes.Generate(Content, spec, 6);
        double gate = route.GateOr(RouteTuning.Load(Content).YardLength);
        var world = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), route.Build(), gate - 8), Tuning.Combat);
        world.EnableRun(Tuning.Run, route, gate, authority: true);
        world.EnableTown(content, route, gate, []);
        return (world, world.Town!);
    }

    [Fact]
    public void EveryoneBreathesThroughGearOfTheKindsTheirTownKeeps()
    {
        var town = Plan("frontier:7", 900);
        Assert.All(town.Plan.People, p => Assert.Contains(p.Gear, TownGear.Kinds));
        // More than one kind in a town of all sorts.
        Assert.True(town.Plan.People.Select(p => p.Gear).Distinct().Count() >= 2, string.Join(", ", town.Plan.People.Select(p => p.Gear)));

        // A company issues its masks: only the respirator and the mine's rebreather.
        var company = Plan("local:3", 900, "company");
        Assert.Equal("company", company.Plan.Character);
        Assert.All(company.Plan.People, p => Assert.Contains(p.Gear, (string[])["respirator", "rebreather"]));
        // A cove makes its own: wraps the most.
        var cove = Plan("local:3", 900, "cove");
        var kinds = cove.Plan.People.GroupBy(p => p.Gear).OrderByDescending(g => g.Count()).ToList();
        Assert.Equal("wrap", kinds[0].Key);
    }

    [Fact]
    public void WhatSomebodyWearsIsTheirsAndTheSameEveryTime()
    {
        var a = Plan("frontier:7", 600);
        var b = Plan("frontier:7", 600);
        Assert.Equal(a.Plan.People.Select(p => (p.Name, p.Gear)), b.Plan.People.Select(p => (p.Name, p.Gear)));
        // The pick runs in the kinds' order, whatever order the file lists them in.
        var weights = new Dictionary<string, double> { ["wrap"] = 1, ["respirator"] = 1 };
        var flipped = new Dictionary<string, double> { ["respirator"] = 1, ["wrap"] = 1 };
        for (int i = 0; i < 200; i++)
        {
            ulong h = Streams.Mix(7, "gear", "", i);
            Assert.Equal(TownGear.Pick(weights, h), TownGear.Pick(flipped, h));
        }
        Assert.Equal("respirator", TownGear.Pick(new Dictionary<string, double>(), 12345));
    }

    [Theory]
    [InlineData("frontier:7", 300)]
    [InlineData("local:3", 2000)]
    public void EverybodyGoesAboutARoundAndIsBackAtTheirPostWhenItComesRound(string spec, int people)
    {
        var town = Plan(spec, people);
        var plan = town.Plan;
        Assert.All(plan.People, p => Assert.NotNull(town.Rounds[p.Id]));
        town.Clock = 0;
        // Out of doors, everyone starts the night at their post.
        foreach (var p in plan.People.Where(p => p.House < 0))
            Assert.True((town.Feet(p) - town.World(p.S, p.D, p.Up)).Length < 1e-6, $"{p.Title} isn't at their post at the start");
        // Most go somewhere: nobody's just standing around all night.
        int moved = plan.People.Count(p => town.Rounds[p.Id]!.Stops.Select(x => (Math.Round(x.S, 1), Math.Round(x.D, 1))).Distinct().Count() > 1);
        Assert.True(moved >= plan.People.Count * 0.8, $"only {moved} of {plan.People.Count} go anywhere");
        // Round again, back where they began.
        foreach (var p in plan.People)
        {
            var r = town.Rounds[p.Id]!;
            town.Clock = 0;
            var start = town.Feet(p);
            town.Clock = r.Period;
            Assert.True((town.Feet(p) - start).Length < 1e-6, $"{p.Title}'s round doesn't come round");
        }
    }

    [Theory]
    [InlineData("frontier:7", 300)]
    [InlineData("local:3", 2000)]
    [InlineData("deadLines:3", 900)]
    public void ARoundKeepsClearOfWhatsSolidAndNeverPutsTwoInOnePlace(string spec, int people)
    {
        var town = Plan(spec, people);
        var plan = town.Plan;
        double longest = town.Rounds.Max(r => r?.Period ?? 0);
        for (double clock = 0; clock < longest * 2; clock += 0.5)
        {
            town.Clock = clock;
            var stood = new List<(Townsperson P, Double3 Feet)>();
            foreach (var p in plan.People)
            {
                var now = town.Now(p);
                // Sat on a bench is sat in its box, and getting up out of it a step: everywhere else, in no wall of the town's.
                bool bench = p.House < 0 && town.Rounds[p.Id]!.Stops.Any(x => x.Act == "seated" && (town.World(x.S, x.D, p.Up) - now.Feet).Length < 0.45);
                if (!bench)
                {
                    var inside = town.Walls.Where(w =>
                    {
                        var l = w.ToLocal(now.Feet + Double3.Up * 0.6);
                        return Math.Abs(l.X) < w.HalfLength - 0.02 && Math.Abs(l.Z) < w.HalfWidth - 0.02 && l.Y > w.Bottom && l.Y < w.Top;
                    }).ToList();
                    Assert.True(inside.Count == 0, $"{p.Title} ({now.Act}) at {clock:0.0} s is inside a wall");
                }
                if (!now.Walking)
                    stood.Add((p, now.Feet));
            }
            for (int i = 0; i < stood.Count; i++)
                for (int j = i + 1; j < stood.Count; j++)
                    Assert.True((stood[i].Feet - stood[j].Feet).Length > 0.5,
                        $"{stood[i].P.Title} and {stood[j].P.Title} stand in one place at {clock:0.0} s");
        }
    }

    [Fact]
    public void WhoeverYoureTalkingToStopsForYouAndGoesOnAfter()
    {
        var (world, town) = Night("frontier:7", 300);
        // The night's clock runs the rounds.
        world.Step(default);
        Assert.Equal(world.Tick * SimConstants.TickSeconds, town.Clock, 9);
        // Somebody out walking.
        double clock = 0;
        var walker = town.Plan.People.First(p =>
        {
            for (clock = 0; clock < town.Rounds[p.Id]!.Period; clock += 1)
            {
                town.Clock = clock;
                if (town.Now(p).Walking)
                    return true;
            }
            return false;
        });
        town.Clock = clock;
        var held = town.Feet(walker);
        town.Hold(walker.Id);
        town.Clock = clock + 20;
        Assert.True((town.Feet(walker) - held).Length < 1e-9, "walked on while being talked to");
        town.Hold(-1);
        town.Clock = clock + 21;
        double on = (town.Feet(walker) - held).Length;
        Assert.InRange(on, 0.5 * town.Tuning.Rounds.Walk, 1.05 * town.Tuning.Rounds.Walk);
    }

    [Fact]
    public void AWalledTownHasAGreenItsPeoplePutUpAndTheDayPaintedOnItsWall()
    {
        // The director, 8 Oct 2026: "These towns need layouts, parks, signs of governance, signs of culture, statues".
        var town = Plan("local:3", 2500);
        var plan = town.Plan;
        var green = Assert.IsType<TownGreen>(plan.Green);
        Assert.Equal(plan.Square.Side, green.Side);
        Assert.True(green.S1 - green.S0 > 30 && green.Far - green.Near > 10, $"a green {green.S1 - green.S0:0} by {green.Far - green.Near:0} m");
        // No house on it.
        Assert.DoesNotContain(plan.Houses, h => h.Solids().Any(p =>
        {
            var (sa, da) = h.Rail(p.U0, p.V0);
            var (sb, db) = h.Rail(p.U1, p.V1);
            return green.Holds((sa + sb) / 2, (da + db) / 2, -0.5);
        }));
        string[] kinds = ["statue", "memorial", "bandstand", "garden", "tree"];
        foreach (string kind in kinds)
            Assert.Contains(plan.Fixtures, f => f.Kind == kind && green.Holds(f.S, f.D) && f.Text.Length > 0);
        // The council's laws by the clerk's door, four of them; the town's flag in the square; the day on the back wall.
        var laws = Assert.Single(plan.Fixtures, f => f.Kind == "laws");
        Assert.Contains(" IV. ", laws.Text);
        Assert.Single(plan.Fixtures, f => f.Kind == "flag");
        var mural = Assert.Single(plan.Fixtures.Where(f => f.Kind == "mural").Take(1));
        Assert.True(Math.Abs(mural.S - plan.Bounds!.Rear) < 1.2, "the mural isn't on the back wall");
        // Each one looked at from in front of it says what it is.
        foreach (var f in plan.Fixtures.Where(f => kinds.Contains(f.Kind) || f.Kind is "laws" or "mural"))
        {
            var at = town.LookAt(f);
            var face = town.Direction(f.S, f.FaceS, f.FaceD);
            var stand = at + face * (f.Kind == "mural" ? 3.0 : Math.Max(f.SolidS, f.SolidD) + 1.0);
            var eye = new Double3(stand.X, town.World(f.S, f.D).Y + 1.6, stand.Z);
            var target = town.Target(eye, (at - eye).Normalized);
            Assert.True(target is { Kind: TownTargetKind.Fixture } t && t.Index == f.Id, $"{f.Name} ({f.Kind}) can't be looked at");
        }
        // The council keeps its house; the quiet house stands by the gate, inside the wall, clear of every house and street,
        // and can be knocked at.
        Assert.Equal("the council house", plan.Buildings[0].Name);
        var quiet = Assert.Single(plan.Buildings, b => b.Kind == "quiet");
        Assert.True(plan.Bounds.Holds(quiet.S, quiet.D, -3), "the quiet house isn't inside the wall");
        Assert.DoesNotContain(plan.Houses, h => Math.Abs(h.S - quiet.S) < (h.Width + quiet.Length) / 2 && Math.Abs(h.D - quiet.D) < (h.Depth + quiet.Depth) / 2 + 3);
        Assert.DoesNotContain(plan.Bounds.Streets, st => Math.Abs(st.At(quiet.S) - quiet.D) < st.Width / 2 + quiet.Depth / 2 && quiet.S > st.S0 && quiet.S < st.S1);
        var door = town.Door(quiet);
        var knock = door + (door - town.World(quiet.S, quiet.D, 1.2)).Normalized * 1.6;
        Assert.Equal(new TownTarget(TownTargetKind.Door, plan.Buildings.ToList().IndexOf(quiet)), town.Target(knock with { Y = door.Y + 0.4 }, (door - knock).Normalized));

        // A small town keeps the yard: no green, but its laws and its flag, and its dead and its day on the square's far wall.
        var small = Plan("frontier:7", 60);
        Assert.Null(small.Plan.Green);
        Assert.Contains(small.Plan.Fixtures, f => f.Kind == "laws");
        Assert.DoesNotContain(small.Plan.Buildings, b => b.Kind == "quiet");
        foreach (string kind in (string[])["memorial", "mural"])
        {
            var f = Assert.Single(small.Plan.Fixtures, x => x.Kind == kind);
            Assert.True(small.Plan.Square.Holds(f.S, Math.Sign(f.D)), $"the small town's {kind} isn't in its square");
            var face = small.Direction(f.S, f.FaceS, f.FaceD);
            var lookAt = small.LookAt(f);
            var stand = lookAt + face * (kind == "mural" ? 3.0 : f.SolidD + 1.0);
            var eye = new Double3(stand.X, small.World(f.S, f.D).Y + 1.6, stand.Z);
            Assert.Equal(new TownTarget(TownTargetKind.Fixture, f.Id), small.Target(eye, (lookAt - eye).Normalized));
        }
    }
}
