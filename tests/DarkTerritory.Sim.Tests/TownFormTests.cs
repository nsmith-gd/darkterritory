using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Towns;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The town types (queue #424, ARCHITECTURE §8 note 692; the director, 13 Oct 2026: "multiple different town types",
/// "they still need to be able to fit the twenty-car maximum train length for the yard", "a town needs a gate that is the
/// entrance from the previous run, and then it also needs a gate that is the exit to the next run", "it needs to be the
/// exact same town with the exact same layout, so seed-based"). Every form on a few routes and sizes.
/// </summary>
/// <remarks>
/// In LineGenTests' collection, after the parallel classes: sixty whole nights with a full town each, run beside the rest,
/// held the suite's memory 0.7 GB over main's and the CI machine ran out with the Game tests beside it.
/// </remarks>
[Collection(nameof(LineGenTests))]
public class TownFormTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TownContent Towns = TownContent.Load(Content)!;

    public static TheoryData<string, string, int> Cases()
    {
        var data = new TheoryData<string, string, int>();
        foreach (var form in TownForms.All)
            foreach (var (spec, people) in new[] { ("frontier:7", 140), ("local:5", 2900) })
                data.Add(form, spec, people);
        return data;
    }

    static TownContent As(string form, int people) =>
        Towns with { Tuning = Towns.Tuning with { Population = [people, people], Forms = Towns.Tuning.Forms with { Force = form } } };

    static (World World, Town Town) Night(string form, string spec, int people)
    {
        var route = Routes.Generate(Content, spec, 6);
        double gate = route.GateOr(RouteTuning.Load(Content).YardLength);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), route.Build(), gate - 8);
        var world = new World(train, Tuning.Combat);
        world.EnableRun(Tuning.Run, route, gate, authority: true);
        world.EnableTown(As(form, people), route, gate, []);
        return (world, world.Town!);
    }

    /// <summary>
    /// The yard holds the longest consist inside the town, between its two gates: the way in from the last night at the
    /// rear wall, the way out to the next at the gate, the wall open at both (and only there) where the line runs through.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void ItsYardHoldsTheLongestTrainBetweenItsTwoGates(string form, string spec, int people)
    {
        var (_, town) = Night(form, spec, people);
        var plan = town.Plan;
        Assert.Equal(form, plan.Form);
        var b = Assert.IsType<TownBounds>(plan.Bounds);
        var f = plan.Frame;
        Assert.Equal(b.Rear, f.Entry);
        Assert.Equal(b.Gate, f.Exit);
        // 20 cars of 14 m, an engine of 20, 1.5 m between: 330 m, back from the engine's place short of the way out.
        Assert.True(f.Yard1 - f.Yard0 >= Towns.Tuning.Forms.YardM - 1e-6, $"a yard of {f.Yard1 - f.Yard0:0} m");
        Assert.True(f.Yard0 > f.Entry + 20, $"the yard starts {f.Yard0 - f.Entry:0} m inside the way in");
        // The wall's runs leave the line open at the two gates and nowhere else.
        var runs = Fortresses.Runs(b).ToList();
        foreach (double s in new[] { b.Rear, b.Gate })
            Assert.DoesNotContain(runs, r => Crosses(r, s, 0, Fortresses.GateInner));
        Assert.All(runs, r => Assert.False(Math.Min(Math.Abs(r.A.D), Math.Abs(r.B.D)) < Fortresses.GateOuter - 0.01 && Math.Sign(r.A.D) != Math.Sign(r.B.D)));
        // And nothing of the town stands on the line or its platform's edge through the yard.
        foreach (var h in plan.Houses)
            Assert.True(Math.Abs(h.D) - h.Depth > 3.5 || h.S < f.Entry || h.S > f.Exit, $"house {h.Id} stands {h.D:0.0} m off the line");
    }

    static bool Crosses(((double S, double D) A, (double S, double D) B) r, double s, double d, double pad) =>
        Math.Abs((r.A.S + r.B.S) / 2 - s) < 1 && Math.Min(r.A.D, r.B.D) < d + pad && Math.Max(r.A.D, r.B.D) > d - pad;

    /// <summary>
    /// Every house stands inside its wall on dry ground (land, a terrace, a hummock; never in the water or on a deck, but
    /// a drowned ward's lost), clear of every other house and of the ways and streets; and its open houses stand square,
    /// their rooms their walls.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryHouseStandsOnItsOwnGroundInsideTheWall(string form, string spec, int people)
    {
        var (_, town) = Night(form, spec, people);
        var plan = town.Plan;
        var b = plan.Bounds!;
        var g = town.Ground;
        Assert.NotEmpty(plan.Houses);
        var bodies = new List<(TownHouse House, (double S, double D)[] Corners)>();
        foreach (var h in plan.Houses)
        {
            var corners = new[] { h.Body(-h.Width / 2, 0), h.Body(h.Width / 2, 0), h.Body(h.Width / 2, h.Depth), h.Body(-h.Width / 2, h.Depth) };
            foreach (var (s, d) in corners)
                Assert.True(b.Holds(s, d), $"house {h.Id}'s corner ({s - b.Gate:0}, {d:0}) is outside the wall");
            // Lived in: on dry ground, or on a stage built to bear it over the water (the director, 14 Oct: a drowned town's
            // and a fen town's houses on their platforms, "not just these isolated things"), never in the water or on a walk.
            if (h.Kind is HouseKind.Lived or HouseKind.Open && !plan.Districts.Any(x => x.Kind == DistrictKind.Stage && x.Holds(h.S, h.D)))
            {
                Assert.Null(g.Water(h.S, h.D));
                Assert.False(g.Deck(h.S, h.D), $"house {h.Id} stands on a deck");
            }
            if (h.Layout is not null)
                Assert.Equal(0, h.Turn);
            Assert.True(Math.Abs(h.Level - g.Level(h.S, h.D)) < 1e-3, $"house {h.Id} ({h.Kind}) at ({h.S - b.Gate:0.0}, {h.D:0.0}) stands at {h.Level} on ground at {g.Level(h.S, h.D)}");
            bodies.Add((h, corners));
        }
        // No two houses' middles inside each other's body (their lots are kept apart by the form's grid).
        foreach (var (h, corners) in bodies)
            foreach (var (o, _) in bodies)
                if (o != h && Math.Abs(o.S - h.S) < 20 && Math.Abs(o.D - h.D) < 20)
                    Assert.False(Inside(corners, o.S, o.D), $"house {o.Id} stands inside house {h.Id}");
        // Off the ways: no way's middle runs through a lived-in house's middle.
        foreach (var w in plan.Ways)
            for (int i = 1; i < w.Points.Count; i++)
                foreach (var h in plan.Houses.Where(h => h.Kind is HouseKind.Lived or HouseKind.Open))
                    Assert.True(SegDist(h.S, h.D, w.Points[i - 1], w.Points[i]) > w.Width / 2 + 1, $"a way runs through house {h.Id}");
    }

    static bool Inside((double S, double D)[] poly, double s, double d)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            if ((poly[i].D > d) != (poly[j].D > d) && s < (poly[j].S - poly[i].S) * (d - poly[i].D) / (poly[j].D - poly[i].D) + poly[i].S)
                inside = !inside;
        return inside;
    }

    static double SegDist(double s, double d, (double S, double D) a, (double S, double D) b)
    {
        double es = b.S - a.S, ed = b.D - a.D, len2 = es * es + ed * ed;
        double k = len2 < 1e-9 ? 0 : Math.Clamp(((s - a.S) * es + (d - a.D) * ed) / len2, 0, 1);
        double x = a.S + es * k - s, y = a.D + ed * k - d;
        return Math.Sqrt(x * x + y * y);
    }

    /// <summary>
    /// Every lived-in house's door is walked to from the yard (note 692's verticality; the director, 13 Oct: "make sure that
    /// there's always walkable paths for the player"): over the town's ground, up its stair streets and stairs, steps no
    /// taller than a stride, no water too deep to wade; and its tiers and benches give it some height to climb.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryDoorIsWalkedToFromTheYard(string form, string spec, int people)
    {
        var (_, town) = Night(form, spec, people);
        var plan = town.Plan;
        var walk = town.Ground.Walk(plan.Gate - 60, 6);
        foreach (var h in plan.Houses.Where(h => h.Kind is HouseKind.Lived or HouseKind.Open))
        {
            var (s, d) = h.Body(h.Design.DoorU, -1.6);
            Assert.True(walk(s, d), $"{form}: house {h.Id}'s door at ({s - plan.Gate:0.0}, {d:0.0}), {h.Level} m up, isn't walked to from the yard");
        }
        // (A big town reaches up its tiers; a small one is round its square.)
        if (form is "harbour" or "tidewater" or "quarry" && people >= 1000)
            Assert.True(plan.Houses.Max(h => h.Level) - plan.Houses.Where(h => !h.Kind.Equals(HouseKind.Burnt)).Min(h => h.Level) >= 6,
                $"{form}: its houses stand within {plan.Houses.Max(h => h.Level) - plan.Houses.Min(h => h.Level):0.0} m of each other");
    }

    /// <summary>
    /// Its ground is what's stood on (TownGround laid over the land): a quarry's floor well under its rim, a harbour's
    /// deck over its water and its water railed off, a dykeland's marsh waded; every step of the ground past a stride is
    /// walled, and a ramp goes down a quarry's benches a stride at a time.
    /// </summary>
    [Theory]
    [InlineData("quarry")]
    [InlineData("harbour")]
    [InlineData("dykeland")]
    [InlineData("tidewater")]
    public void ItsGroundIsWhatsStoodOnAndItsDropsAreWalled(string form)
    {
        var (world, town) = Night(form, "frontier:7", 600);
        var plan = town.Plan;
        var g = town.Ground;
        Assert.False(g.Flat);
        var terrain = ((PlanConditions)world.Train.Line.Conditions!).Terrain;
        // The terrain asks the town's ground where it's shaped.
        // (Inside its wall: past it, out in its margin, the town's land eases into the plan's.)
        var shaped = Enumerable.Range(0, g.Ns * g.Nd).Select(k => (S: g.S0 + (k / g.Nd + 0.5), D: g.D0 + (k % g.Nd + 0.5)))
            .Where(p => g.Shaped(p.S, p.D) && g.Inside(p.S, p.D)).ToList();
        Assert.NotEmpty(shaped);
        double rail = world.Train.Line.Sample(plan.Gate - 200).Position.Y;
        foreach (var (s, d) in shaped.Where((_, i) => i % 97 == 0))
            Assert.Equal(rail + g.Level(s, d), terrain.Ground(town.World(s, d) with { Y = rail + 30 }), 3);
        switch (form)
        {
            case "quarry":
                var floor = plan.Districts.Last(x => x.Id == "floor");
                Assert.True(floor.Level <= -10, $"the pit's floor is {floor.Level} m down");
                // The ramp goes down from the rim to the floor a stride at a time.
                var ramp = Assert.Single(plan.Ways, w => w.Levels is not null && w.Levels.Min() <= -10);
                Assert.Equal(0, ramp.Levels![0], 3);
                for (int i = 1; i < ramp.Points.Count; i++)
                {
                    double len = Math.Sqrt(Math.Pow(ramp.Points[i].S - ramp.Points[i - 1].S, 2) + Math.Pow(ramp.Points[i].D - ramp.Points[i - 1].D, 2));
                    Assert.True(Math.Abs(ramp.Levels[i] - ramp.Levels[i - 1]) <= len * 0.25 + 1e-6, $"the ramp drops {ramp.Levels[i - 1] - ramp.Levels[i]:0.0} m in {len:0.0} m");
                }
                break;
            case "harbour":
                Assert.Contains(plan.Ways, w => w.Deck);
                var pier = plan.Ways.First(w => w.Deck);
                var (ps, pd) = pier.Points[^1];
                Assert.True(g.Deck(ps, pd));
                Assert.NotNull(g.Water(ps, pd));
                Assert.True(g.Level(ps, pd) > g.Water(ps, pd)!.Value, "the pier's planks stand over the water");
                break;
            case "dykeland":
                // The marsh is waded: its water no deeper than TownGround.Wade, so it isn't railed off.
                var marsh = plan.Districts.First(x => x.Kind == DistrictKind.Water);
                Assert.True(marsh.Depth <= TownGround.Wade, $"the marsh is {marsh.Depth} m deep");
                break;
        }
        // Every step past a stride, and every edge of water too deep to wade, has its wall (TownGround.Edges) in the world.
        var edges = g.Edges();
        if (form != "dykeland")
            Assert.NotEmpty(edges);
        var walls = world.Train.Walls!.All;
        foreach (var (s0, d0, s1, d1, bottom, top) in edges.Where((_, i) => i % 23 == 0))
        {
            var mid = town.World((s0 + s1) / 2, (d0 + d1) / 2) with { Y = 0 };
            Assert.Contains(walls, w => ((w.Centre - mid) with { Y = 0 }).Length < 0.6 && w.Top > rail + bottom + 0.5);
        }
    }

    /// <summary>
    /// The same town from the same seed, to the last lot and level (note 591: "the exact same town with the exact same
    /// layout"), whichever night makes it; and towns.json's trade weights give every form somewhere.
    /// </summary>
    [Fact]
    public void TheSameSeedIsTheSameTownAndEveryFormTurnsUp()
    {
        foreach (var form in TownForms.All)
        {
            var site = new TownSite("Fort Boudreau", "coal", 1180, 1234, []) { Form = form };
            var a = TownGenerator.Generate(As(form, 400), site);
            var b = TownGenerator.Generate(As(form, 400), site);
            Assert.Equal(a.Houses.Select(h => (h.S, h.D, h.Turn, h.Level)), b.Houses.Select(h => (h.S, h.D, h.Turn, h.Level)));
            Assert.Equal(a.Districts.Select(d => (d.Kind, d.Id, d.Name, d.Level, d.Outline.Count)), b.Districts.Select(d => (d.Kind, d.Id, d.Name, d.Level, d.Outline.Count)));
            Assert.Equal(TownGround.Of(a).Edges(), TownGround.Of(b).Edges());
        }
        // (With the forms in the trades' rotation, as towns.json forms.enabled puts them.)
        var rotation = Towns with { Tuning = Towns.Tuning with { Forms = Towns.Tuning.Forms with { Enabled = true } } };
        var seen = new HashSet<string>();
        string[] trades = ["coal", "farm", "foundry"];
        for (ulong seed = 1; seed <= 200; seed++)
            seen.Add(TownGenerator.Generate(rotation, new TownSite("Fort Boudreau", trades[(int)(seed % 3)], 1180, seed, [])).Form);
        foreach (var form in TownForms.All.Append(""))
            Assert.Contains(form, seen);
    }
}
