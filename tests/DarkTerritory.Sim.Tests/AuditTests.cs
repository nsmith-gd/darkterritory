using Ballast;
using DarkTerritory.Sim.Audit;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// GDD §34's verification harness gaps (note 186): App. A.9 / B.10's per-tree GRAB check, §34's cascade audit, the
/// combination sweep's pieces, and degraded comms. Fast versions of `dt audit grabs`, `dt audit cascades` and
/// `dt balance --pairs`.
/// </summary>
public class AuditTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly BalanceTuning Balance = DataFile.Load<BalanceTuning>(Path.Combine(Content, BalanceTuning.File));
    static readonly AuditContent C = new(Tuning.Train, Tuning.Player, Tuning.Boiler, Tuning.Combat, Tuning.Enemies, Tuning.Run, Balance);

    // ---- App. A.9 / B.10: every GRAB is interruptible by the crew actually present, at every crew size.

    [Fact]
    public void EveryGrabIsBrokenByTheCrewPresentAtTheSmallestAndLargestCrews()
    {
        var report = GrabAudit.Run(C, crews: [2, 8]);
        Assert.True(report.Pass, string.Join("\n", report.Uninterruptible.Concat(report.NeverGrabbed)));
        // Every creature with a grab was checked, and each grabbed and let go at both sizes.
        Assert.Equal(GrabAudit.Grabbers.Count * 2, report.Checks.Count);
        Assert.All(report.Checks, c => Assert.True(c.Grabbed && c.Freed && c.HeldSeconds < c.WindowSeconds, $"{c.Kind} {c.Crew}: {c.Detail}"));
    }

    [Theory]
    [InlineData(EnemyKind.Dragger)]
    [InlineData(EnemyKind.Whistler)]
    [InlineData(EnemyKind.SootChildren)]
    public void WithFriendsWhoOnlyWatchTheGrabRunsItsCourse(EnemyKind kind)
    {
        // The control: the same rig with the friends standing by. The check isn't passed by the rig, only by the rescue.
        var check = GrabAudit.Check(C, kind, 3, friendsHelp: false);
        Assert.True(check.Grabbed, check.Detail);
        Assert.False(check.Freed, check.Detail);
    }

    // ---- §34 cascade audit: no failure chain is unrecoverable.

    static readonly AuditContent Quick = C with { Balance = Balance with { Cascades = Balance.Cascades with { FailAtSeconds = 8 } } };

    [Theory]
    [InlineData("rupture")]
    [InlineData("rupture-kit-in-car-four")]
    [InlineData("fire-out")]
    [InlineData("car-fire")]
    [InlineData("gun-fouled")]
    [InlineData("stoker")]
    [InlineData("uncoupled-car")]
    public void TheCrewComesBackFromIt(string chain)
    {
        // The kit on car 4's floor too: a walker gets down off the roofs and brings it up through the cars (KitCarry, note 186).
        var r = CascadeAudit.Scenario(Quick, chain);
        Assert.True(r.Recovered, $"{r.Name}: {r.Detail}");
    }

    [Fact]
    public void EveryChainIsNamedOnce() => Assert.Equal(CascadeAudit.Names.Count, CascadeAudit.Names.Distinct().Count());

    // ---- §34 combination fairness: the sweep's pieces.

    [Fact]
    public void EveryPairAndTripleOfTheRoster()
    {
        Assert.Equal(18, Combinations.Roster.Count);
        Assert.Equal(18 * 17 / 2, Combinations.Of(Combinations.Roster, 2).Count);
        var triples = Combinations.Of(Combinations.Roster, 3);
        Assert.Equal(18 * 17 * 16 / 6, triples.Count);
        Assert.Equal(triples.Count, triples.Select(t => string.Join("+", t)).Distinct().Count());
        // A sample is the same sample every time, and a sample of the whole.
        var a = Combinations.Of(Combinations.Roster, 3, sample: 40, seed: 5);
        var b = Combinations.Of(Combinations.Roster, 3, sample: 40, seed: 5);
        Assert.Equal(40, a.Count);
        Assert.Equal(a.Select(t => string.Join("+", t)), b.Select(t => string.Join("+", t)));
        Assert.All(a, t => Assert.Contains(triples, x => x.SequenceEqual(t)));
    }

    static CombinationRun Night(bool lost = false, int grabs = 0, int punishes = 0, int deaths = 0, string[]? engaged = null) =>
        new(1, 3000, lost ? "CrewLost" : "None", deaths, false, lost, grabs, punishes, 0, 0, engaged ?? []);

    [Fact]
    public void TheVerdicts()
    {
        var t = Balance.Combinations;
        EnemyKind[] pair = [EnemyKind.Dragger, EnemyKind.Climber];
        var clear = HazardSet.Clear;
        Assert.Equal("unwinnable", Combinations.Judge(pair, clear, [Night(lost: true, engaged: ["Dragger"]), Night(lost: true)], [], t).Verdict);
        Assert.Equal("fair", Combinations.Judge(pair, clear, [Night(lost: true, engaged: ["Dragger", "Climber"]), Night(grabs: 1, engaged: ["Dragger"])], [], t).Verdict);
        Assert.Equal("trivial", Combinations.Judge(pair, clear, [Night(engaged: ["Dragger", "Climber"])], [], t).Verdict);
        // Something that never came on isn't judged trivial: the night never tested it. Never put anywhere, it's unplaced;
        // put there and lying in wait all night, dormant.
        var row = Combinations.Judge(pair, clear, [Night(engaged: ["Dragger"])], [], t);
        Assert.Equal("unplaced", row.Verdict);
        Assert.Equal(["Climber"], row.Unexercised);
        Assert.Equal("dormant", Combinations.Judge(pair, clear, [Night(engaged: ["Dragger"]) with { Placed = ["Dragger", "Climber"] }], [], t).Verdict);
        var report = Combinations.Report(2, [row, Combinations.Judge(pair, clear, [Night(lost: true)], [], t)]);
        Assert.False(report.Pass);
        Assert.Single(report.Unwinnable);
    }

    static CombinationRun In(string route, int crew, int seed, bool lost, int grabs = 0) =>
        Night(lost: lost, grabs: grabs, engaged: ["Dragger", "Climber"]) with { Route = route, Crew = crew, Seed = seed };

    [Fact]
    public void TheGridRunsEveryCombinationOnEveryRouteWithEveryCrew()
    {
        // Note 204: combination by combination, then route, crew and seed; the hazard sets in turn down the combinations,
        // or every one of them.
        var pairs = Combinations.Of([EnemyKind.Dragger, EnemyKind.Climber, EnemyKind.Stoker], 2);
        var hazards = Balance.Combinations.HazardSets;
        var grid = new CombinationGrid(["frontier:7", "deadLines:2"], [2, 4, 8], 2);
        var nights = Combinations.Nights(pairs, hazards, everyHazard: false, grid);
        Assert.Equal(3 * 2 * 3 * 2, nights.Count);
        Assert.Equal(nights.Count, nights.Distinct().Count());
        Assert.All(Enumerable.Range(0, pairs.Count), i => Assert.All(nights.Where(n => n.Kinds == pairs[i]), n => Assert.Equal(hazards[i], n.Hazards)));
        Assert.Equal(("frontier:7", 2, 1), (nights[0].Route, nights[0].Crew, nights[0].Seed));
        Assert.Equal(("deadLines:2", 8, 2), (nights[11].Route, nights[11].Crew, nights[11].Seed));
        Assert.Equal(pairs[1], nights[12].Kinds);
        Assert.Equal(3 * hazards.Count * 2 * 3 * 2, Combinations.Nights(pairs, hazards, everyHazard: true, grid).Count);
        // The tuning's grids: the default one quick, the nightly's wider in routes, crews and seeds.
        var t = Balance.Combinations;
        Assert.NotEmpty(t.Routes);
        Assert.NotEmpty(t.Crews);
        Assert.True(t.Wide.Routes.Count > t.Routes.Count && t.Wide.Crews.Count > t.Crews.Count && t.Wide.Seeds >= 2);
        Assert.Equal(new CombinationGrid(t.Routes, t.Crews, t.Seeds), t.Grid);
    }

    [Fact]
    public void UnwinnableIsLostEveryNightInSomeCell()
    {
        var t = Balance.Combinations;
        EnemyKind[] pair = [EnemyKind.Dragger, EnemyKind.Climber];
        var clear = HazardSet.Clear;
        // Lost both seeds with a crew of two on dead lines, won elsewhere: unwinnable there, and the detail says where.
        var row = Combinations.Judge(pair, clear,
            [In("frontier:7", 2, 1, false, grabs: 1), In("frontier:7", 2, 2, false), In("deadLines:2", 2, 1, true), In("deadLines:2", 2, 2, true),
             In("frontier:7", 8, 1, false), In("frontier:7", 8, 2, true)], [], t);
        Assert.Equal("unwinnable", row.Verdict);
        Assert.Contains("deadLines:2 crew 2", row.Detail);
        Assert.Contains("2 of 3 cells", row.Detail);
        Assert.Equal([("frontier:7", 2), ("deadLines:2", 2), ("frontier:7", 8)], row.Cells.Select(c => (c.Route, c.Crew)));
        Assert.Equal([false, true, false], row.Cells.Select(c => c.Unwinnable));
        Assert.Equal([0, 2, 1], row.Cells.Select(c => c.Lost));
        // A night lost in every cell, but never every night in one: not unwinnable. Something landed: fair.
        var split = Combinations.Judge(pair, clear,
            [In("frontier:7", 2, 1, true), In("frontier:7", 2, 2, false, grabs: 1), In("deadLines:2", 2, 1, false), In("deadLines:2", 2, 2, true)], [], t);
        Assert.Equal("fair", split.Verdict);
        Assert.All(split.Cells, c => Assert.False(c.Unwinnable));
        // Trivial is nothing landed anywhere: one grab in one cell makes it fair.
        Assert.Equal("trivial", Combinations.Judge(pair, clear, [In("frontier:7", 2, 1, false), In("deadLines:2", 8, 1, false)], [], t).Verdict);
        Assert.Equal("fair", Combinations.Judge(pair, clear, [In("frontier:7", 2, 1, false), In("deadLines:2", 8, 1, false, grabs: 1)], [], t).Verdict);
        // The report's cells: every night of every row by (route, crew), and what was unwinnable in each.
        var report = Combinations.Report(2, [row, split]);
        Assert.Equal([("frontier:7", 2, 4, 1), ("deadLines:2", 2, 4, 3), ("frontier:7", 8, 2, 1)], report.ByCell.Select(c => (c.Route, c.Crew, c.Nights, c.Lost)));
        Assert.Equal(["Dragger+Climber / clear"], report.ByCell[1].Unwinnable);
        Assert.Empty(report.ByCell[0].Unwinnable);
        Assert.Equal(["Dragger+Climber / clear"], report.Unwinnable);
    }

    [Fact]
    public void AHazardSetTakesAwayButNeverGives()
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(5_000)]));
        var set = new HazardSet("test", Adhesion: 0.75, ColdStep: 2, Wind: 1);
        HazardConditions.Apply(line, set);
        var c = Assert.IsType<HazardConditions>(line.Conditions);
        Assert.Equal(0.75, c.Adhesion(RailLine.MainPath, 100));
        Assert.Equal(2, c.ColdStep(RailLine.MainPath, 100));
        Assert.Equal(1, c.Wind(RailLine.MainPath, 100));
        // A hand-laid line's ground is still its rail height.
        Assert.Equal(line.Sample(100).Position.Y, c.Ground(line.Sample(100).Position + new Double3(3, 0, 0)), 6);
        // Clear lays nothing over the line.
        var bare = new RailLine(new LineDefinition("t", [new TrackSegment(5_000)]));
        HazardConditions.Apply(bare, HazardSet.Clear);
        Assert.Null(bare.Conditions);
        Assert.Equal(["clear", "wet", "cold", "dark"], Balance.Combinations.HazardSets.Select(h => h.Name));
    }

    [Fact]
    public void InsistedOnOnlyThoseKindsComeAndTheLineBringsNothingOfItsOwn()
    {
        var route = LineGen.Routes.Generate(Content, "frontier:7", 6);
        var line = route.Build();
        EnemyKind[] kinds = [EnemyKind.Dragger, EnemyKind.FireFlies];
        var stage = Combinations.Stage(route, line, kinds, Tuning.Enemies, route.GateOr(Tuning.Route.YardLength), 20);
        Assert.Empty(stage.Unstaged);
        Assert.False(stage.AtStop);
        double start = stage.StartM;
        var report = Harness.Run(line, Tuning.Train, Tuning.Player, new HarnessOptions
        {
            Bots = 3,
            Cars = 6,
            Seconds = 30,
            Seed = 1,
            Link = new Ballast.Net.LinkConditions(0, 0, 0),
            StartDistance = start,
            Combat = Tuning.Combat,
            Enemies = Tuning.Enemies,
            Route = route,
            Insist = kinds,
            Hazards = Balance.Combinations.HazardSets[1],
        }, Tuning.Boiler);
        Assert.NotNull(report.Threats);
        Assert.All(report.Threats!.Spawned.Keys, k => Assert.Contains(k, kinds.Select(x => x.ToString())));
        Assert.All(report.Threats.Engaged.Keys, k => Assert.Contains(k, kinds.Select(x => x.ToString())));
        // Fire Flies come only to a lit car with a way in (a door or the hatch open: note 286), so whether they come here is
        // up to the crew's doors; the Dragger gets under a car at the stand it starts from.
        Assert.Contains("Dragger", report.Threats.Spawned.Keys);
    }

    [Fact]
    public void TheLookOutGoesAndLooksAtWhatLiesInWait()
    {
        // Note 209: without the look-out, the Gaunt asleep out on the ballast and a Dragger under a roof's lip lay there all
        // night (note 186's "dormant"); with it, both come on, by their own rules, at a crewmate's feet.
        var route = LineGen.Routes.Generate(Content, "frontier:7", 6);
        var line = route.Build();
        EnemyKind[] kinds = [EnemyKind.Gaunt, EnemyKind.Dragger];
        var stage = Combinations.Stage(route, line, kinds, Tuning.Enemies, route.GateOr(Tuning.Route.YardLength), 20, atStops: false);
        // Note 286: a Dragger that got on with nobody up top waits under any car, a longer walk for the look-out.
        HarnessReport Night(Bots.LookTuning? look) => Harness.Run(route.Build(), Tuning.Train, Tuning.Player, new HarnessOptions
        {
            Bots = 4,
            Cars = 6,
            Seconds = look is null ? 30 : 60,
            Seed = 1,
            Link = new Ballast.Net.LinkConditions(0, 0, 0),
            StartDistance = stage.StartM,
            Combat = Tuning.Combat,
            Enemies = Tuning.Enemies,
            Route = route,
            Run = Tuning.Run,
            Facilities = DataFile.Load<Run.FacilityTuning>(Path.Combine(Content, Run.FacilityTuning.File)),
            Sight = DataFile.Load<Route.SightTuning>(Path.Combine(Content, Route.SightTuning.File)),
            YardLength = route.GateOr(Tuning.Route.YardLength),
            Insist = kinds,
            Look = look,
        }, Tuning.Boiler);
        var without = Night(null).Threats!;
        Assert.Equal(["Dragger", "Gaunt"], without.Spawned.Keys.Order());
        Assert.Empty(without.Engaged);
        var with = Night(Balance.Combinations.Look).Threats!;
        Assert.Equal(["Dragger", "Gaunt"], with.Engaged.Keys.Order());
    }

    [Fact]
    public void ACrewOfTwosGunnerIsTheLookOut()
    {
        // Note 220: a crew of two is the driver and the gunner, no walker to send, so the Gaunt and a Dragger lay there all
        // night; the gunner goes and looks, off its gun while the gun can spare it, and both come on.
        var route = LineGen.Routes.Generate(Content, "frontier:7", 6);
        var line = route.Build();
        EnemyKind[] kinds = [EnemyKind.Gaunt, EnemyKind.Dragger];
        var stage = Combinations.Stage(route, line, kinds, Tuning.Enemies, route.GateOr(Tuning.Route.YardLength), 20, atStops: false);
        // Note 286: a Dragger that got on with nobody up top waits under any car, a longer walk for the look-out.
        HarnessReport Night(Bots.LookTuning? look) => Harness.Run(route.Build(), Tuning.Train, Tuning.Player, new HarnessOptions
        {
            Bots = 2,
            Cars = 6,
            Seconds = look is null ? 30 : 60,
            Seed = 1,
            Link = new Ballast.Net.LinkConditions(0, 0, 0),
            StartDistance = stage.StartM,
            Combat = Tuning.Combat,
            Enemies = Tuning.Enemies,
            Route = route,
            Run = Tuning.Run,
            Facilities = DataFile.Load<Run.FacilityTuning>(Path.Combine(Content, Run.FacilityTuning.File)),
            Sight = DataFile.Load<Route.SightTuning>(Path.Combine(Content, Route.SightTuning.File)),
            YardLength = route.GateOr(Tuning.Route.YardLength),
            Insist = kinds,
            Look = look,
        }, Tuning.Boiler);
        var without = Night(null);
        Assert.Equal(["Dragger", "Gaunt"], without.Threats!.Spawned.Keys.Order());
        Assert.Empty(without.Threats.Engaged);
        var with = Night(Balance.Combinations.Look);
        Assert.Equal(["Dragger", "Gaunt"], with.Threats!.Engaged.Keys.Order());
    }

    [Fact]
    public void TheCarHuggerIsStagedShortOfItsMarsh()
    {
        var route = LineGen.Routes.Generate(Content, "frontier:7", 6);
        var line = route.Build();
        var stage = Combinations.Stage(route, line, [EnemyKind.CarHugger], Tuning.Enemies, route.GateOr(Tuning.Route.YardLength), 20);
        Assert.Empty(stage.Unstaged);
        Assert.Contains(route.Features, f => f.Kind is Route.FeatureKind.Marsh or Route.FeatureKind.Bridge
            && (f.Start + f.End) / 2 - stage.StartM >= Tuning.Enemies.CarHugger.LurkAheadMin && (f.Start + f.End) / 2 - stage.StartM <= Tuning.Enemies.CarHugger.LurkAheadMax);
        // What comes at a stop starts short of a facility's spur, where the crew get down to work.
        var stop = Combinations.Stage(route, line, [EnemyKind.Ribbit, EnemyKind.CarHugger], Tuning.Enemies, route.GateOr(Tuning.Route.YardLength), 20);
        Assert.True(stop.AtStop);
        Assert.Contains(line.Branches, b => b.Kind == BranchKind.Spur && b.Toe - stop.StartM > 0 && b.Toe - stop.StartM < 1000);
        Assert.Equal(["CarHugger"], stop.Unstaged);
    }

    // ---- §34 degraded comms: the crew's calls over a lossy, laggy voice.

    static readonly PlayerState Aboard = new() { Parent = 2, Health = 100 };

    [Fact]
    public void WithoutAVoiceACallIsHeardAtOnce()
    {
        var calls = new CrewCalls();
        calls.Say(3, StopJob.Shunter, Aboard);
        Assert.True(calls.Has(StopJob.Shunter));
    }

    [Fact]
    public void OverALaggyVoiceACallIsHeardLate()
    {
        var calls = new CrewCalls { Voice = new CrewVoice(new VoiceConditions(LatencySeconds: 0.5), 1) };
        calls.Advance(0);
        calls.Say(3, StopJob.Shunter, Aboard);
        for (uint t = 1; t < 15; t++)
        {
            calls.Advance(t);
            Assert.False(calls.Has(StopJob.Shunter));
        }
        calls.Advance(15);
        Assert.True(calls.Has(StopJob.Shunter));
        var report = calls.Voice!.Report();
        Assert.Equal(1, report.Calls);
        Assert.Equal(0.5, report.MeanDelaySeconds, 3);
    }

    [Fact]
    public void OverADeadVoiceNothingIsHeard()
    {
        var calls = new CrewCalls { Voice = new CrewVoice(new VoiceConditions(Loss: 1), 1) };
        for (uint t = 0; t < 30; t++)
        {
            calls.Advance(t);
            calls.Leave(true);
        }
        Assert.False(calls.Leaving);
        Assert.Equal(30, calls.Voice!.Report().Lost);
    }

    [Fact]
    public void ShoutingOverEachOtherLosesCallsAndTheSameSeedLosesTheSameOnes()
    {
        VoiceReport Shout(ulong seed)
        {
            var calls = new CrewCalls { Voice = new CrewVoice(new VoiceConditions(TalkOver: 0.3), seed) };
            for (uint t = 0; t < 300; t++)
            {
                calls.Advance(t);
                // Four of them, each saying something new every few ticks.
                for (int m = 0; m < 4; m++)
                    calls.CarryingTo(m, (int)(t / (3 + m)) % 5);
            }
            return calls.Voice!.Report();
        }
        var a = Shout(7);
        Assert.True(a.TalkedOver > 0);
        Assert.Equal(a, Shout(7));
        Assert.NotEqual(a, Shout(8));
    }

    [Fact]
    public void LateNewsDoesntUndoNewer()
    {
        // Jitter can bring an older call in after a newer one: the newer stands.
        var voice = new CrewVoice(new VoiceConditions(LatencySeconds: 1, JitterSeconds: 1), 3);
        string heard = "";
        for (uint t = 0; t < 10; t++)
        {
            string said = $"car {t}";
            voice.Say(t, 1, "where", said, () => heard = said);
        }
        for (uint t = 0; t < 120; t++)
            voice.Deliver(t);
        Assert.Equal("car 9", heard);
    }

    [Fact]
    public void AHarnessNightReportsWhatGotThroughOnAPoorVoice()
    {
        var poor = Balance.Comms["poor"];
        Assert.True(poor.Loss > 0 && poor.LatencySeconds > 0);
        var route = LineGen.Routes.Generate(Content, "frontier:7", 6);
        var report = Harness.Run(route.Build(), Tuning.Train, Tuning.Player, new HarnessOptions
        {
            Bots = 5,
            Cars = 6,
            Seconds = 20,
            Seed = 2,
            Link = new Ballast.Net.LinkConditions(0, 0, 0),
            StartDistance = 400,
            Combat = Tuning.Combat,
            Route = route,
            Run = Tuning.Run,
            Facilities = DataFile.Load<Run.FacilityTuning>(Path.Combine(Content, Run.FacilityTuning.File)),
            YardLength = route.GateOr(Tuning.Route.YardLength),
            Voice = poor,
        }, Tuning.Boiler);
        Assert.NotNull(report.Voice);
        Assert.True(report.Voice!.Calls > 0);
        Assert.True(report.Voice.Lost > 0);
        Assert.True(report.Voice.MeanDelaySeconds > 0);
    }
}
