using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The spawn director overhaul's guard tests (queue #602, note 887; the director's brief, docs/design/spawn-director-overhaul.md
/// §8 "Required assertions", §9): today's protections, locked before the overhaul (#603–#613) changes the director, so it can't
/// quietly break them. Each test presses the director as hard as the tuning lets it (the pressure at its most, past its
/// threshold from the first second) and then asserts that nothing it sends gets through a protection, by any of its channels:
/// the pressure's spend, the free spawns, the stops' own (the <see cref="Director.Log"/>), the hound runs, the left-behind's
/// hunts and the truss drops. Each has a contrast (the same night outside the protection sends something), so a guard
/// can't pass by the director sending nothing at all. Safe zones, the grace and the terminus here; determinism and host
/// authority in <see cref="DirectorGuardNetTests"/>; each creature's own structural rules in <see cref="CreatureRestrictionGuardTests"/>.
/// </summary>
public class DirectorGuardTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly EnemyTuning E = Tuning.Enemies;
    static readonly DirectorTuning D = E.Director;
    static readonly PlayerTuning P = Tuning.Player;
    internal static readonly Lazy<Route.Route> Frontier7 = new(() => Routes.Generate(Content, "frontier:7", 6));

    /// <summary>
    /// A director pressed from its first second past the grace: the pressure at 18 and held there (spending relieves none of
    /// it), its first threat not waiting on a draw, every kind about tonight; the night's curve (its free spawns and fill) as
    /// the game has it. <paramref name="grace"/>: the night's grace (null: the game's).
    /// </summary>
    internal static EnemyTuning Pressed(string[]? roster = null, double? grace = 5) => E with
    {
        Director = D with
        {
            Roster = roster ?? D.Roster,
            NightChance = new Dictionary<string, double>(),
            GraceMinSeconds = grace ?? D.GraceMinSeconds,
            GraceMaxSeconds = grace ?? D.GraceMaxSeconds,
            Pressure = D.Pressure with { Threshold = 1, PressAt = 1, Start = 18, Max = 18, ReliefPerCost = 0 },
            Draw = D.Draw with { Enabled = false },
        },
    };

    [Fact]
    public void TheGuardsPressTheGamesOwnProtections()
    {
        // What these tests lock, as the tuning has it today: if one of these moves, the guard it backs is to be re-read.
        Assert.True(Tuning.Run.YardIsSafe);
        Assert.True(Tuning.Run.Forts.Safe);
        Assert.True(D.Aftermath.On);
        Assert.True(D.Aftermath.Seconds > 0);
        Assert.True(D.FacilityLullSeconds > 0);
        Assert.True(D.GraceMinSeconds > 0 && D.GraceMaxSeconds >= D.GraceMinSeconds);
        Assert.True(RouteTuning.Load(Content).NoSpawnFinalApproach > 0);
    }

    /// <summary>A crew of four (the cab, two roofs, the van's), on a train held at <paramref name="speed"/>, a run under way past its start.</summary>
    internal static GuardNight Moving(EnemyTuning enemies, double front, double speed, Route.Route? route = null, ulong seed = 1)
    {
        var n = new GuardNight(enemies, route, front, speed, run: true, seed: seed);
        n.Underway(900);
        n.Aboard(4);
        return n;
    }

    // ---- Safe zones ----

    [Fact]
    public void NothingAtAllComesInTheSafeYardHoweverLongTheCrewWaits()
    {
        // The crew standing about the yard and the train for ten minutes before the gates: nothing is sent and nothing comes
        // (note 263: the director isn't consulted while the yard's safe; nor is anything else that spawns).
        var route = Frontier7.Value;
        var n = new GuardNight(Pressed(), route, front: 300, speed: 0, run: true);
        n.Controls = new TrainControls { Reverser = 1, Brake = 1 };
        n.Aboard(1);
        for (int i = 2; i <= 4; i++)
            n.Crew[i] = n.Ground(300 - 20 * i, lateral: 4);
        n.Run(600, each: g => Assert.True(g.World.SafeYard, "left the safe yard"));
        Assert.Equal(RunPhase.Yard, n.World.Run!.Phase);
        Assert.Empty(n.Arrivals);
        Assert.Equal(0, n.DirectorSent);
    }

    [Fact]
    public void NothingOfTheDirectorsComesWhileTheTrainIsInAFort()
    {
        // GDD §9, T128 (note 273): stood inside the start's fort, under way, the crew afoot by it: the director holds, by every
        // channel, and nothing at all comes into the fort.
        double yard = RouteTuning.Load(DataFile.FindContentRoot()).YardLength;
        var n = new GuardNight(Pressed(), null, front: yard - 50, speed: 0, run: true);
        n.Underway(900);
        n.Aboard(1);
        for (int i = 2; i <= 4; i++)
            n.Crew[i] = n.Ground(yard - 60 - 10 * i, lateral: 4);
        n.Run(120, each: g => Assert.True(g.World.TrainInFort, "the train left the fort"));
        Assert.Equal(0, n.DirectorSent);
        Assert.DoesNotContain(n.Arrivals, a => a.InFort);
        Assert.Equal("in a fort", n.Director.HeldBecause);
        // The contrast: stood out on the line instead, the same director sends.
        var out_ = new GuardNight(Pressed(), null, front: yard + 3_000, speed: 0, run: true);
        out_.Underway(900);
        out_.Aboard(1);
        for (int i = 2; i <= 4; i++)
            out_.Crew[i] = out_.Ground(yard + 2_990 - 10 * i, lateral: 4);
        out_.Run(120);
        Assert.True(out_.DirectorSent > 0, "the contrast night sent nothing");
    }

    [Fact]
    public void ACreatureThatGetsIntoAFortIsGoneAndNoneStaysInOne()
    {
        // Whatever comes for a crew standing at the fort's gate (sent outside it, chasing them in) doesn't stay inside.
        double yard = RouteTuning.Load(DataFile.FindContentRoot()).YardLength;
        var n = new GuardNight(Pressed(), null, front: yard + 400, speed: 0, run: true);
        n.Underway(900);
        n.Aboard(1);
        for (int i = 2; i <= 4; i++)
            n.Crew[i] = n.Ground(yard - 5 - 3 * i, lateral: 3);
        int insideTicks = 0;
        n.Run(180, eachTick: g => insideTicks += g.World.ActiveEnemies.Count(e => !e.Gone && !e.Hazard && e is not Enemies.Incident && g.World.InFort(e.WorldPosition(g.Train))) > 0 ? 1 : 0);
        // A creature may be seen inside for the tick it crossed the line; never longer.
        Assert.True(insideTicks <= 2, $"a creature stood in the fort for {insideTicks} ticks");
    }

    // ---- The grace ----

    [Fact]
    public void TheDirectorSendsNothingOfItsOwnBeforeTheNightsGrace()
    {
        // The game's grace (20–90 s by the seed, shorter on harder tiers), the train run hot and fast from the first second:
        // nothing by any channel until it's over, and something soon after.
        var n = new GuardNight(Pressed(grace: null), null, front: 2_000, speed: 21);
        n.Aboard(4);
        var d = n.Director;
        Assert.InRange(d.Grace, D.GraceMinSeconds, D.GraceMaxSeconds);
        n.Run(Math.Floor(d.Grace) - 1, each: g =>
        {
            Assert.Equal(0, g.DirectorSent);
            Assert.Empty(g.Arrivals);
            Assert.Equal("grace", g.Director.HeldBecause);
        });
        n.Run(60);
        Assert.True(n.DirectorSent > 0, "nothing came after the grace");
        Assert.All(n.Director.Log, l => Assert.True(l.Tick * SimConstants.TickSeconds >= n.Director.Grace, $"{l.Kind} at {l.Tick * SimConstants.TickSeconds:0.0} s"));
    }

    [Fact]
    public void TheGraceCountsFromTheGatesNotFromTheYard()
    {
        // Note 263: a crew who waited at the gate half the night haven't been out in the Territory for it. Ten minutes in the
        // safe yard, then out through the gates: the grace starts there.
        var route = Frontier7.Value;
        var n = new GuardNight(Pressed(grace: null), route, front: 300, speed: 0, run: true);
        n.Controls = new TrainControls { Reverser = 1, Brake = 1 };
        n.Aboard(4);
        n.Run(600);
        Assert.Equal(RunPhase.Yard, n.World.Run!.Phase);
        n.Controls = new TrainControls { Reverser = 1 };
        n.Speed = 12;
        double grace = n.Director.Grace;
        for (int s = 0; s < 400 && (n.World.Run.Phase != RunPhase.Underway || n.World.Run.Seconds < grace); s++)
        {
            n.Run(1);
            Assert.Equal(0, n.DirectorSent);
        }
        Assert.True(n.World.Run.Seconds >= grace, "never got out through the gates");
    }

    // ---- The terminus ----

    [Fact]
    public void NothingNewComesForTheTrainOnItsRunInToTheTerminus()
    {
        // The final approach (route.json noSpawnFinalApproach) and the line's terminus_safe stretch: the train on its way in
        // at speed, the crew aboard, the director pressed: nothing new of the director's, the Stoker's, the marsh's or the
        // lineside's, and nothing at all comes.
        var route = Frontier7.Value;
        double final = RouteTuning.Load(Content).NoSpawnFinalApproach;
        var n = Moving(Pressed(), route.RunLength - final + 20, 14, route);
        // The sim's ban is the route's, as route.json set it when the line was laid (note 912; note 887 found it a constant).
        Assert.Equal(final, n.World.NoSpawnFinalApproach);
        Assert.Equal(final, route.NoSpawnFinalApproach);
        n.Run(30, each: g => Assert.True(g.World.HomeStretch, "out of the home stretch"));
        Assert.Equal(0, n.DirectorSent);
        Assert.Empty(n.Arrivals);
        // The contrast: three kilometres back, the same.
        var back = Moving(Pressed(), route.RunLength - 3_000, 14, route);
        back.Run(30);
        Assert.True(back.DirectorSent > 0, "the contrast night sent nothing");
    }

    [Fact]
    public void TheLinesTerminusSafeStretchHoldsTheDirectorShortOfTheFinalApproach()
    {
        var route = Frontier7.Value;
        double final = route.RunLength - RouteTuning.Load(Content).NoSpawnFinalApproach;
        var safe = route.Plan!.Director.Tags.Where(t => t.Edge == "main" && t.Tag == "terminus_safe" && t.S0 < final - 200).OrderBy(t => t.S0).FirstOrDefault();
        if (safe is null)
        {
            // frontier:7's terminus_safe lies inside the final approach; the guard above holds it.
            Assert.DoesNotContain(route.Plan.Director.Tags, t => t.Edge == "main" && t.Tag == "terminus_safe" && t.S0 < final - 200);
            return;
        }
        double seconds = Math.Min(30, (Math.Min(safe.S1, final) - safe.S0 - 20) / 14);
        var n = Moving(Pressed(), safe.S0 + 10, 14, route);
        n.Run(seconds, each: g => Assert.True(g.World.HomeStretch));
        Assert.Equal(0, n.DirectorSent);
        // The houses' own (notes 583–586) of the yard behind (6130–6830 m, short of the stretch at 6980 m): the test puts the
        // train down within reach of it, where on a night they'd have come as the train came up to it. A stop in the home
        // stretch itself keeps its houses empty (note 912, ProtectionGapTests). Nothing else comes.
        Assert.All(n.Arrivals, a => Assert.Contains(a.Kind, Dwellers));
    }

    /// <summary>What the stops' houses put in their houses (<see cref="Dwellings"/>), not sent at the crew.</summary>
    static readonly EnemyKind[] Dwellers = [EnemyKind.Hanger, EnemyKind.HollowHouse, EnemyKind.Householder, EnemyKind.Lodger];

    // ---- Post-crisis recovery and the facility lull ----

    [Fact]
    public void AfterACrisisTheDirectorsOwnDecisionsHoldForTheAftermath()
    {
        // GDD App. B.1 "Aftermath" (note 751): a crewmate dead, and for the aftermath nothing of the director's own decisions
        // (its spend, its free spawns, the stops' own) comes, pressed as it is. Its specials (the hound run, the truss drops) wait
        // it out too (note 912, aftermath.specials; ProtectionGapTests); a left-behind's hunts are theirs and keep their own.
        var n = Moving(Pressed(), 3_000, 14);
        n.Run(20);
        var d = n.Director;
        Assert.Equal(0, d.AftermathLeft);
        n.Crew[2] = n.Crew[2] with { Health = 0, Death = DeathCause.Mauled };
        for (int i = 0; i < 3 && d.AftermathLeft == 0; i++)
            n.Run(0.5);
        Assert.Equal("death", d.AftermathCause);
        int logged = d.Log.Count;
        while (d.AftermathLeft > 0)
        {
            n.Run(1);
            Assert.Equal(logged, d.Log.Count);
        }
        // The contrast: pressed, it had been sending before the death.
        Assert.True(logged > 0, "the director sent nothing before the crisis");
    }

    [Fact]
    public void InTheFacilityLullOnlyWhatLivesAtTheStopIsSent()
    {
        // App. B.1 "Facility lull": stood at a facility, the director's spend and its free spawns wait for the lull; only the
        // stop's own residents (note 860) are there as the train comes in.
        var stop = new FacilityTests.Stop(FacilityKind.ChemicalWorks);
        stop.World.EnableEnemies(Pressed(), null, 1, crew: 4, authority: true);
        var d = stop.World.Director!;
        Assert.Equal(RunPhase.AtFacility, stop.World.Run!.Phase);
        for (int i = 0; i < D.FacilityLullSeconds; i++)
        {
            int before = d.Log.Count;
            var kind = d.Decide(stop.World, 1_000 + i, stop.World.ActiveEnemies, 500);
            if (kind is not null)
            {
                Assert.True(d.Log.Count > before);
                Assert.True(d.Log[^1].Stop, $"{kind} sent in the lull, not the stop's own");
            }
            else
                Assert.Equal(before, d.Log.Count);
        }
    }
}

/// <summary>A host night for the guards: a crew put where a test says, the train held at a speed, everything that comes noted.</summary>
sealed class GuardNight
{
    static readonly PlayerTuning P = Tuning.Player;

    public readonly World World;
    public readonly Dictionary<int, PlayerState> Crew = new();
    /// <summary>The train held at this speed each tick; null, the controls drive it.</summary>
    public double? Speed;
    public TrainControls Controls = new() { Reverser = 1 };
    /// <summary>Every enemy that came into the world, the first tick it was there, and where the train was.</summary>
    public readonly List<Came> Arrivals = new();
    readonly HashSet<int> _seen = new();

    public readonly record struct Came(int Id, EnemyKind Kind, uint Tick, double Front, bool SafeYard, bool InFort, bool HomeStretch);

    public GuardNight(EnemyTuning enemies, Route.Route? route, double front, double speed, int cars = 6, bool run = false, ulong seed = 1, int crew = 4)
    {
        route ??= new Route.Route("guard", RouteTier.Frontier, 1, new LineDefinition("guard", [new TrackSegment(40_000)]), [], new RouteWeather(0.01, false, 0, 0), 3600);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, cars, 1)), route.Build(), front);
        train.Dynamics.Velocity = speed;
        Speed = speed;
        World = new World(train, Tuning.Combat);
        World.EnableEnemies(enemies, route, seed, crew, authority: true);
        if (run)
            World.EnableRun(Tuning.Run, route, route.Plan is null ? RouteTuning.Load(DataFile.FindContentRoot()).YardLength : route.GateOr(RouteTuning.Load(DataFile.FindContentRoot()).YardLength), authority: true);
        // What's in the world before the night begins (the Sleepers, if a mod has them) didn't come.
        foreach (var e in World.ActiveEnemies)
            _seen.Add(e.Id);
    }

    public TrainOnLine Train => World.Train;
    public Director Director => World.Director!;

    /// <summary>Every send of the director's, by all its channels: its log (spend, free, stops'), the hound runs, the hunts, the drops.</summary>
    public int DirectorSent => Director.Log.Count + Director.HoundRuns.Count + Director.Hunts.Count + Director.TrussDraggers;

    /// <summary>The run under way, <paramref name="seconds"/> in (past any grace).</summary>
    public void Underway(double seconds) => World.Run!.Resume(seconds, -1, 0, 0);

    /// <summary><paramref name="count"/> aboard: the cab, then the roofs from the second car back.</summary>
    public void Aboard(int count)
    {
        Crew[1] = PlayerMotor.SpawnInCab(Train, P);
        var cars = Train.Dynamics.Consist.Vehicles;
        for (int i = 2; i <= count; i++)
            Crew[i] = PlayerMotor.SpawnOnRoof(Train, cars[Math.Min(cars.Count - 1, i)].Id, 0, P);
    }

    public PlayerState Ground(double along, double lateral)
    {
        var rail = Train.Line.Sample(RailLine.MainPath, along);
        var right = Double3.Cross(rail.Tangent, Double3.Up).Normalized;
        return PlayerMotor.SpawnOnGround(rail.Position + right * lateral, Train.Line, along, P);
    }

    public void Run(double seconds, Action<GuardNight>? each = null, Action<GuardNight>? eachTick = null)
    {
        int ticks = (int)Math.Round(seconds * SimConstants.TickRate);
        for (int i = 0; i < ticks; i++)
        {
            if (Speed is { } v && !World.Derailed)
                Train.Dynamics.Velocity = v;
            World.BeginTick();
            foreach (var id in Crew.Keys.ToList())
            {
                var s = Crew[id];
                World.CrewAct(ref s, default, id);
                Crew[id] = s;
            }
            World.Step(Controls);
            World.ApplyDamage(id => Crew.TryGetValue(id, out var s) ? s : null, (id, s) => Crew[id] = s, Crew.Keys);
            foreach (var id in Crew.Keys.ToList())
            {
                var s = Crew[id];
                PlayerMotor.Step(ref s, default, Train, P, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
                Crew[id] = s;
            }
            if (World.Run is not null)
                World.StepRun([.. Crew.Values]);
            foreach (var e in World.ActiveEnemies)
                if (_seen.Add(e.Id))
                    Arrivals.Add(new Came(e.Id, e.Kind, World.Tick, Train.Dynamics.Distance, World.SafeYard, World.InFort(e.WorldPosition(Train)), World.HomeStretch));
            eachTick?.Invoke(this);
            if (each is not null && (i + 1) % SimConstants.TickRate == 0)
                each(this);
        }
    }
}
