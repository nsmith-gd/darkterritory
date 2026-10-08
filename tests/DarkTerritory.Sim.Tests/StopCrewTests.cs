using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// A crew of bots works a facility stop through intent alone (T32, GDD §17): the driver on the regulator, the shunter on
/// the coupler and the switch, two on the winch; nobody set anything but their own hands and the cab's controls.
/// </summary>
public class StopCrewTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly FacilityTuning F = DataFile.Load<FacilityTuning>(Path.Combine(DataFile.FindContentRoot(), FacilityTuning.File));
    static readonly Stops.LootTuning L = DataFile.Load<Stops.LootTuning>(Path.Combine(DataFile.FindContentRoot(), Stops.LootTuning.File));

    /// <summary>
    /// A route whose first facility (before any other kind of stop) has these modules down its spur: the facility's index
    /// and where the spur's points are.
    /// </summary>
    static (Route.Route Route, int Facility, double Toe) StopWith(params ModuleKind[] modules) => StopWith(null, modules);

    /// <param name="village">The facility's stop must also have this (a village with open houses, say).</param>
    static (Route.Route Route, int Facility, double Toe) StopWith(Func<RouteFeature, bool>? village, params ModuleKind[] modules)
    {
        for (ulong seed = 1; seed < 200; seed++)
        {
            var route = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, seed);
            var facilities = route.Of(FeatureKind.Facility).ToList();
            int i = facilities.FindIndex(f => f.Facility is { } k && F.ModulesOf(k).Order().SequenceEqual(modules.Order()) && (village is null || village(f)));
            // Or one offering those and more, given just those (a route's own modules, T44): the switchyard's crates without its
            // standing cars, the wreck yard's winch without its wreck (note 187).
            bool own = i < 0;
            if (own)
                i = facilities.FindIndex(f => f.Facility is { } k && f.Modules is null && modules.All(F.ModulesOf(k).Contains) && (village is null || village(f)));
            if (i >= 0 && route.Branches.FirstOrDefault(b => b.Kind == BranchKind.Spur && facilities[i].Contains(b.Toe)) is { } spur)
            {
                if (!own)
                    return (route, i, spur.Toe);
                var features = route.Features.ToList();
                int at = features.IndexOf(facilities[i]);
                features[at] = features[at] with { Modules = [.. modules.Select(m => m.ToString())] };
                return (route with { Features = features }, i, spur.Toe);
            }
        }
        throw new InvalidOperationException($"no frontier route has a stop with {string.Join(", ", modules)}");
    }

    /// <summary>A route with a dead line: its branch index, and where its points are.</summary>
    static (Route.Route Route, int Branch, double Toe) DeadLine()
    {
        for (ulong seed = 1; seed < 200; seed++)
        {
            var route = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, seed);
            int i = route.Branches.ToList().FindIndex(b => b.Kind == BranchKind.DeadLine);
            if (i >= 0)
                return (route, i, route.Branches[i].Toe);
        }
        throw new InvalidOperationException("no frontier route has a dead line");
    }

    /// <summary>A route with a coaling tower: its facility index, and where along the line its spout is.</summary>
    static (Route.Route Route, int Facility, double Spout) CoalingTower()
    {
        for (ulong seed = 1; seed < 200; seed++)
        {
            var route = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, seed);
            var facilities = route.Of(FeatureKind.Facility).ToList();
            int i = facilities.FindIndex(f => f.Facility == FacilityKind.CoalingTower);
            if (i >= 0)
                return (route, i, (facilities[i].Start + facilities[i].End) / 2);
        }
        throw new InvalidOperationException("no frontier route has a coaling tower");
    }

    sealed class Night
    {
        public readonly World World;
        public readonly List<IWorldBot> Bots = [];
        public readonly List<PlayerState> Crew = [];
        public readonly ConductorBot Driver;
        public readonly CrewCalls Calls;
        public readonly Site Site;
        /// <summary>The dead line it runs up to, on a <c>deadLine</c> night.</summary>
        public readonly int Branch;
        TrainControls _controls = new() { Reverser = 1 };
        uint _tick;

        /// <summary>
        /// Running up to the stop from a standing start 600 m short of it: a crew of the driver, a shunter, the winch pair
        /// (unless <paramref name="winchPair"/> is false) and <paramref name="walkers"/> more on the roofs.
        /// </summary>
        /// <param name="ids">Each hand knows its own player id (as the harness tells them), so they take heavy crates (T45).</param>
        /// <param name="hands">Of the shunter and the winch pair, how many there are (note 261: a crew of two is the driver and a shunter).</param>
        /// <param name="facilities">The facilities' tuning, if not the game's (note 261's crew flags).</param>
        public Night(int cars, int walkers = 1, bool winchPair = true, bool crateHands = false, bool coaling = false, bool deadLine = false,
            bool ids = false, int hands = 3, FacilityTuning? facilities = null, bool loot = false, params ModuleKind[] modules)
        {
            // A crew that searches the village (note 326) needs a stop with one: open houses with something kept in them.
            Func<RouteFeature, bool>? village = loot
                ? f => f.Stop is { } st && st.Containers.Any(c => c.Zone == Stops.StopZone.Village && c.Building >= 0 && st.Buildings[c.Building].Open)
                : null;
            var (route, facility, toe) = deadLine ? DeadLine() : coaling ? CoalingTower() : StopWith(village, modules.Length > 0 ? modules : [ModuleKind.Winch]);
            Calls = new CrewCalls();
            var calls = Calls;
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, cars, Tuning.Run.DepartureLoad)), route.Build(), toe - 600, Tuning.Boiler);
            World = new World(train);
            World.EnableBodies();
            World.EnableSwitches(Tuning.Route.Junctions);
            World.EnableRun(Tuning.Run, route, Tuning.Route.YardLength, authority: true, facilities ?? F, loot ? L : null);
            // The stops' finds and their houses' walls (note 326), for a crew that searches the village.
            if (loot)
                train.Walls = StopWalls.Of(route, train.Line);
            Site = deadLine ? null! : World.Run!.Sites[facility]!;
            Branch = deadLine ? facility : -1;
            Driver = new ConductorBot(calls, 0);
            Add(Driver, PlayerMotor.SpawnInCab(train, P));
            if (hands >= 1)
                Add(new RoofWalkerBot(11, P.Cold, new StopHand(StopJob.Shunter, calls, 1, P.Cold)), PlayerMotor.SpawnOnRoof(train, 1, 0, P));
            var (a, b) = winchPair ? (StopJob.Winch0, StopJob.Winch1) : (StopJob.None, StopJob.None);
            if (hands >= 2)
                Add(new RoofWalkerBot(12, P.Cold, new StopHand(a, calls, 2, P.Cold)), PlayerMotor.SpawnOnRoof(train, 2, 0, P));
            if (hands >= 3)
                Add(new RoofWalkerBot(13, P.Cold, new StopHand(b, calls, 3, P.Cold)), PlayerMotor.SpawnOnRoof(train, cars - 1, 2, P));
            for (int w = 0; w < walkers; w++)
                Add(new RoofWalkerBot(20 + w, P.Cold, new StopHand(crateHands ? StopJob.Crates : StopJob.None, calls, 4 + w, P.Cold)),
                    PlayerMotor.SpawnOnRoof(train, 3 + w % (cars - 3), -3, P));
            if (ids)
                for (int i = 0; i < Bots.Count; i++)
                    if (Bots[i] is RoofWalkerBot { Job: { } hand })
                        hand.PlayerId = i + 1;
        }

        /// <summary>Adds someone to the crew; their player id is their place in it, from 1.</summary>
        public int Add(IWorldBot bot, PlayerState s)
        {
            Bots.Add(bot);
            Crew.Add(s);
            return Crew.Count;
        }

        public TrainOnLine Train => World.Train;

        /// <summary>Steps the night as the host does (intent in, then the world, then everyone's feet) until it's done.</summary>
        public void Until(Func<bool> done, double seconds, Action? each = null)
        {
            for (int t = 0; t < seconds * SimConstants.TickRate && !done(); t++)
            {
                World.BeginTick();
                // As the host clears the brake (T97): a standing train stays on it till the driver lets it off.
                if (Crew.Any(c => CabControls.CanDrive(c, Train)) && CabControls.Clears(_controls, Train, false))
                    _controls.Brake = 0;
                var intents = new PlayerIntent[Crew.Count];
                for (int i = 0; i < Crew.Count; i++)
                {
                    intents[i] = Bots[i].Decide(Crew[i], World, _tick, out _);
                    var s = Crew[i];
                    if (CabControls.ReleasesBrake(intents[i], s, Train))
                        _controls.Brake = 0;
                    CabControls.Apply(ref _controls, intents[i], s, Train);
                    World.CrewAct(ref s, intents[i], i + 1);
                    Crew[i] = s;
                }
                World.Step(_controls);
                for (int i = 0; i < Crew.Count; i++)
                {
                    var s = Crew[i];
                    PlayerMotor.Step(ref s, intents[i], Train, P, T, SimConstants.TickSeconds, applyLook: false);
                    Crew[i] = s;
                }
                World.StepBodies([.. Crew.Select((c, i) => (i + 1, c))]);
                World.StepRun(Crew);
                _tick++;
                each?.Invoke();
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(250)]
    public void AWalkerLeftOnTheBallastWalksBackToTheStandingTrainAndClimbsAboard(double behind)
    {
        // T121 (the brakes cut): a crate hand, its part at the Foundry done, came off the roofs as the train backed out of the
        // spur, and the train went on past it to stand on the main line more than 60 m off. Its part done, the stop had nothing for
        // it, and the walker's way aboard only looked 60 m for a ladder: it stood on the ballast until the driver gave up
        // waiting and left it. A standing train is walked back to from however far.
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 1)), new RailLine(new LineDefinition("t", [new TrackSegment(4000)])), 1200);
        var world = new World(train, Tuning.Combat);
        world.EnableBodies();
        var calls = new CrewCalls();
        var bot = new RoofWalkerBot(21, P.Cold, new StopHand(StopJob.Crates, calls, 1, P.Cold)) { Me = 1 };
        int last = train.Vehicles[^1].Id;
        var shape = train.Frames[last].Shape;
        // Beside the last car, or that far back down the line from it, off to its side.
        var at = train.Frames[last].ToWorld(new Ballast.Double3(shape.HalfWidth + 1.2, 0, behind > 0 ? shape.HalfLength + behind : 0));
        var s = PlayerMotor.SpawnOnGround(at, train.Line, train.Cars[last].FrontDistance - behind, P);
        double bound = 20 + behind / P.Run * 1.5;
        double t = 0;
        for (uint tick = 0; t < bound && s.Parent == PlayerState.World; tick++, t += SimConstants.TickSeconds)
        {
            world.BeginTick();
            var intent = bot.Decide(s, world, tick, out _);
            world.CrewAct(ref s, intent, 1);
            world.Step(new TrainControls());
            PlayerMotor.Step(ref s, intent, train, P, T, SimConstants.TickSeconds, applyLook: false);
            Assert.True(s.Alive, $"died of {s.Death}");
        }
        Assert.True(s.Parent != PlayerState.World, $"still on the ballast after {bound:0} s, {(PlayerMotor.WorldPosition(s, train) - at).Length:0.0} m from where it started");
        Assert.Equal(0, train.Dynamics.Velocity);
    }

    [Fact]
    public void AGunnerLeftOnTheBallastClimbsBackAboardAndADeadOneIsNotWaitedFor()
    {
        // The 100-night rerun: a gunner down on the ballast after a stop only ever borrowed the walker's legs on a car, so it
        // stood there (or on the engine's ladder, on and off it) while the driver waited out its give-ups and left it. And
        // dead, it stopped saying so: the driver held at the next switch all night for it to get aboard.
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 1)), new RailLine(new LineDefinition("t", [new TrackSegment(4000)])), 600);
        var world = new World(train, Tuning.Combat);
        world.EnableBodies();
        var calls = new CrewCalls();
        var bot = new GunnerBot(Tuning.Combat.Guns, cold: P.Cold, job: new StopHand(StopJob.Winch0, calls, 1, P.Cold));
        var shape = train.Frames[2].Shape;
        var s = PlayerMotor.SpawnOnGround(train.Frames[2].ToWorld(new Ballast.Double3(shape.HalfWidth + 1.2, 0, 0)), train.Line, train.Cars[2].FrontDistance, P);
        for (uint tick = 0; tick < 40 * SimConstants.TickRate && s.Parent == PlayerState.World; tick++)
        {
            world.BeginTick();
            var intent = bot.Decide(s, world, tick, out _);
            world.CrewAct(ref s, intent, 1);
            world.Step(new TrainControls());
            PlayerMotor.Step(ref s, intent, train, P, T, SimConstants.TickSeconds, applyLook: false);
        }
        Assert.NotEqual(PlayerState.World, s.Parent);
        world.Tick++;
        bot.Decide(s, world, 0, out _); // it says where it is as it decides (once a tick)
        Assert.True(calls.AllAboard);

        var down = PlayerMotor.SpawnOnGround(train.Frames[2].ToWorld(new Ballast.Double3(shape.HalfWidth + 1.2, 0, 0)), train.Line, train.Cars[2].FrontDistance, P);
        world.Tick++;
        bot.Decide(down, world, 0, out _);
        Assert.False(calls.AllAboard);
        world.Tick++;
        bot.Decide(down with { Health = 0, Death = DeathCause.Mauled }, world, 1, out _);
        Assert.True(calls.AllAboard);
    }

    [Fact]
    public void WithTheShunterDeadTheFirstHandLeftTakesItOver()
    {
        var calls = new CrewCalls();
        var alive = new PlayerState { Health = 100, Parent = 3 };
        calls.Say(0, StopJob.Driver, alive);
        calls.Say(1, StopJob.None, alive);
        calls.Say(3, StopJob.Winch0, alive);
        calls.Say(4, StopJob.Crates, alive);
        // Nobody's said they're the shunter yet: that's not a dead shunter, so nobody takes it.
        Assert.False(calls.StandIn(3, StopJob.Shunter));
        calls.Say(2, StopJob.Shunter, alive);
        Assert.False(calls.StandIn(3, StopJob.Shunter));
        // Mauled: the first of the rest with a part of their own steps in (not the driver, not the gunner), and only them.
        calls.Say(2, StopJob.Shunter, alive with { Health = 0, Death = DeathCause.Mauled });
        Assert.True(calls.StandIn(3, StopJob.Shunter));
        Assert.False(calls.StandIn(4, StopJob.Shunter));
        calls.Say(3, StopJob.Shunter, alive);
        Assert.False(calls.StandIn(4, StopJob.Shunter));
        // And the part they left (the crane's operator): the first crate hand takes it, and nobody else.
        Assert.True(calls.StandIn(4, StopJob.Winch0));
        Assert.False(calls.StandIn(3, StopJob.Winch0));
        Assert.False(calls.StandIn(4, StopJob.Winch1)); // nobody's ever had that
        calls.Say(4, StopJob.Winch0, alive);
        Assert.False(calls.StandIn(4, StopJob.Winch0));
    }

    [Fact]
    public void AtAStopOnlyTheFirstTwoCrateHandsLeaveTheCratesForTrouble()
    {
        // T70: with Gnawers and loose loads one after another, every crate hand going to each loaded nothing all stop.
        var calls = new CrewCalls();
        var alive = new PlayerState { Health = 100, Parent = 3 };
        calls.Say(1, StopJob.Winch0, alive);
        for (int m = 2; m <= 5; m++)
            calls.Say(m, StopJob.Crates, alive);
        Assert.True(new StopHand(StopJob.Crates, calls, 2).TakesTrouble);
        Assert.True(new StopHand(StopJob.Crates, calls, 3).TakesTrouble);
        Assert.False(new StopHand(StopJob.Crates, calls, 4).TakesTrouble);
        Assert.False(new StopHand(StopJob.Crates, calls, 5).TakesTrouble);
        // The winch pair decide for themselves (a fire alight, a load loose), as before.
        Assert.True(new StopHand(StopJob.Winch0, calls, 1).TakesTrouble);
        // One of the two dies: the next hand takes their place at it.
        calls.Say(2, StopJob.Crates, alive with { Health = 0, Death = DeathCause.Gnawed });
        Assert.True(new StopHand(StopJob.Crates, calls, 4).TakesTrouble);
    }

    [Fact]
    public void AtTheFoundryThePairRunTheCraneAndTheCastingsGoOnTheRoofs()
    {
        // The foundry: a winch, crates and the gantry crane (spec D.2). The pair take the crane first, one up at the controls
        // and the other rigging on the ground, and the castings go onto cars' roofs; then the winch. No crate hands here, so
        // the crates don't take the room under the gantry first. Nobody's under a falling load.
        var night = new Night(cars: 8, walkers: 0, modules: [ModuleKind.Crane, ModuleKind.Winch, ModuleKind.Crates]);
        var run = night.World.Run!;
        var crane = Assert.IsType<Crane>(night.Site.Crane);
        var doing = new HashSet<string>();
        night.Until(() => run.Departures > 0, 1500, () =>
        {
            foreach (var b in night.Bots)
                if (b is RoofWalkerBot { Job: { } hand } && hand.Doing.Length > 0)
                    doing.Add(hand.Doing);
        });
        string where = string.Join(", ", night.Crew.Select((c, i) => $"{i}: {c.Surface} on {c.Parent}"));
        Assert.True(run.Departures > 0, $"never left the stop: driver {night.Driver.Stops!.Doing}; crew {where}; did {string.Join(", ", doing)}");
        Assert.True(doing.Contains("at the crane"), $"did {string.Join(", ", doing)}; target {StopHand.CraneTarget(crane, night.Train)}; loads {string.Join(",", night.Train.Vehicles.Select(v => v.Load.ToString("0.00")))}");
        Assert.Contains("rigging", doing);
        Assert.True(crane.Castings.All(c => c.State == CastingState.Loaded),
            $"castings {string.Join(", ", crane.Castings.Select(c => $"{c.State} car {c.Car}"))}; loads {string.Join(",", night.Train.Vehicles.Select(v => v.Load.ToString("0.00")))}; did {string.Join(", ", doing)}");
        Assert.All(night.Crew, c => Assert.True(c.Alive, $"died of {c.Death}; crew {where}"));
        Assert.Equal(1, night.Train.TrainRakes);
    }

    [Fact]
    public void ACrewWorksAWinchStopAndGoesOn()
    {
        var night = new Night(cars: 8);
        var train = night.Train;
        var run = night.World.Run!;
        double loadBefore = train.Vehicles.Where(v => v.Kind == VehicleKind.Cargo).Sum(v => v.Load);
        var seen = new HashSet<StopDriver.Leg>();
        night.Until(() => run.Departures > 0, 900, () => seen.Add(night.Driver.Stops!.Doing));
        string where = string.Join(", ", night.Crew.Select((c, i) => $"{i}: {c.Surface} on {c.Parent} cold {c.Cold:0}"));

        Assert.True(run.Departures > 0, $"never left the stop: driver {night.Driver.Stops!.Doing}; crew {where}");
        Assert.Equal(night.Site.Index, run.Departed);
        // Every leg of GDD §17's sequence, driven from the cab.
        StopDriver.Leg[] spur = [StopDriver.Leg.Cruise, StopDriver.Leg.Approach, StopDriver.Leg.Held, StopDriver.Leg.SpurIn, StopDriver.Leg.Loading,
            StopDriver.Leg.BackOut, StopDriver.Leg.Clear, StopDriver.Leg.Depart];
        Assert.Superset(new HashSet<StopDriver.Leg>(spur), seen);
        // Both sleds in: the cars by the winch took them.
        Assert.Equal(0, night.Site.SledsLeft);
        double loadAfter = train.Vehicles.Where(v => v.Kind == VehicleKind.Cargo).Sum(v => v.Load);
        Assert.Equal(loadBefore + F.Winch.Sleds * F.Winch.LoadPerSled, loadAfter, 6);
        // Back together, the switch set for the main line, and everyone alive and aboard.
        Assert.Equal(1, train.TrainRakes);
        Assert.False(train.Diverging(night.Site.Spur));
        Assert.All(night.Crew, c => Assert.True(c.Alive, $"died of {c.Death}; crew {where}"));
        Assert.All(night.Crew, c => Assert.NotEqual(Surface.Ground, c.Surface));
        var record = Assert.Single(night.Driver.Stops!.Log);
        Assert.Equal(F.Winch.Sleds, record.SledsHauled);
    }

    [Fact]
    public void WithTheCarsAtTheWinchFullThePairDontCrankForNothing()
    {
        // T66: on a frontier:7 night the crane's castings filled the cars the sleds load, and the pair cranked until the
        // loading leg gave up (540 s). A sled with nowhere to go isn't hauled: loading's done, and they come aboard.
        var night = new Night(cars: 8);
        var run = night.World.Run!;
        double loading = 0;
        night.Until(() => run.Departures > 0, 900, () =>
        {
            if (night.Driver.Stops!.Doing != StopDriver.Leg.Loading)
                return;
            // In to load, and the cars are full already (as the crane's castings left them).
            if (loading == 0)
                foreach (var v in night.Train.Vehicles.Where(v => v.Kind == VehicleKind.Cargo))
                    v.Load = 1;
            loading += SimConstants.TickSeconds;
        });
        Assert.True(run.Departures > 0, $"never left the stop: driver {night.Driver.Stops!.Doing}");
        Assert.True(loading < 60, $"{loading:0} s loading with nowhere for a sled to go");
        Assert.Equal(0, Assert.Single(night.Driver.Stops!.Log).SledsHauled);
        Assert.All(night.Crew, c => Assert.True(c.Alive, $"died of {c.Death}"));
    }

    [Fact]
    public void ACrewWithoutTheWinchPairRunsStraightPast()
    {
        // The winch needs two (spec D.2): without them there's nothing to stop for, so the driver doesn't. Note 261: the
        // shunter's one of them once the train's in, and the driver another unless it keeps to the cab (crew.driverWorks off).
        var night = new Night(cars: 8, winchPair: false, facilities: F with { Crew = new() { DriverWorks = false } });
        var train = night.Train;
        var zone = night.Site.Feature;
        bool cut = false, diverged = false;
        night.Until(() => train.Dynamics.Distance > zone.End + 50, 300, () =>
        {
            cut |= train.TrainRakes > 1;
            diverged |= train.Diverging(night.Site.Spur);
        });
        Assert.True(train.Dynamics.Distance > zone.End + 50, $"still at {train.Dynamics.Distance:0} (zone ends {zone.End:0})");
        Assert.False(cut);
        Assert.False(diverged);
        Assert.Empty(night.Driver.Stops!.Log);
        Assert.Equal(2, night.Site.SledsLeft);
    }

    // Note 261 (T114 "cargo stops"): a small crew works the stop it can. A crew-of-2 harness night covered 13.5 km and
    // stopped at nothing: the stops all wanted a shunter and both winch hands, or a crate hand besides the shunter.

    [Fact]
    public void ACrewOfTwoStopsAndTheShunterCarriesTheCratesIn()
    {
        // The driver and one more (the gunner, in the harness's crew of two): the shunter cuts and throws as ever, and once
        // the train's in, with no crate hand, carries the crates itself.
        var night = new Night(cars: 8, walkers: 0, hands: 1, ids: true, modules: ModuleKind.Crates);
        var train = night.Train;
        var run = night.World.Run!;
        double before = train.Vehicles.Sum(v => v.Load);
        var doing = new HashSet<string>();
        night.Until(() => run.Departures > 0, 1500, () =>
        {
            if (night.Bots[1] is RoofWalkerBot { Job.Doing: { Length: > 0 } d })
                doing.Add(d);
        });
        string where = string.Join(", ", night.Crew.Select((c, i) => $"{i}: {c.Surface} on {c.Parent}"));

        Assert.True(run.Departures > 0, $"never left: driver {night.Driver.Stops!.Doing}; crew {where}; did {string.Join(", ", doing)}");
        var record = Assert.Single(night.Driver.Stops!.Log);
        Assert.Equal(night.Site.Index, record.Facility);
        Assert.True(train.Vehicles.Sum(v => v.Load) - before >= F.Crates.LoadPerCrate - 1e-6, $"loaded nothing; did {string.Join(", ", doing)}");
        Assert.Contains("picking one up", doing);
        // Back together, the switch set back by the shunter, the doors shut, everyone aboard and the driver at the controls.
        Assert.Equal(1, train.TrainRakes);
        Assert.False(train.Diverging(night.Site.Spur));
        Assert.Empty(OpenSideDoors(train, night.Site.Side));
        Assert.All(night.Crew, c => Assert.True(c.Alive, $"died of {c.Death}; crew {where}"));
        Assert.All(night.Crew, c => Assert.NotEqual(Surface.Ground, c.Surface));
        Assert.True(PlayerMotor.InCab(night.Crew[0], train));
    }

    [Fact]
    public void ACrewOfTwoWorksTheWinchTheShunterAndTheDriverOnItsHandles()
    {
        // D.2's capstan winch, "2 mandatory": the shunter takes the first handle once the train's in, and the driver gets down
        // for the second (the train on its brake, fired up and vented), and climbs back up to drive it out.
        var night = new Night(cars: 8, walkers: 0, hands: 1);
        var train = night.Train;
        var run = night.World.Run!;
        var steps = new HashSet<string>();
        bool driverOut = false;
        night.Until(() => run.Departures > 0, 1500, () =>
        {
            driverOut |= night.Driver.WorkingOut;
            if (night.Driver.WorkStep is { Length: > 0 } d)
                steps.Add(d);
        });
        string where = string.Join(", ", night.Crew.Select((c, i) => $"{i}: {c.Surface} on {c.Parent}"));

        Assert.True(run.Departures > 0, $"never left: driver {night.Driver.Stops!.Doing}; crew {where}; driver did {string.Join(", ", steps)}");
        Assert.True(driverOut, "the driver never got down");
        Assert.Contains("cranking", steps);
        Assert.Equal(0, night.Site.SledsLeft);
        Assert.Equal(F.Winch.Sleds, Assert.Single(night.Driver.Stops!.Log).SledsHauled);
        Assert.Equal(1, train.TrainRakes);
        Assert.False(train.Diverging(night.Site.Spur));
        Assert.All(night.Crew, c => Assert.True(c.Alive, $"died of {c.Death}; crew {where}"));
        Assert.True(PlayerMotor.InCab(night.Crew[0], train), $"the driver's {night.Crew[0].Surface} on {night.Crew[0].Parent}");
    }

    [Fact]
    public void OneBotAndSomeonePlayingStillStopAndTheBotWorksIt()
    {
        // The default way to play: alone with a bot or two. Here one, the driver, and someone playing who stands on a roof the
        // whole time. The person's a hand, so the stop's made; the driver does what the bots would: down to throw the switch,
        // in, down to carry the crates, back up, out, down to set the switch back, and on. A train that goes in whole: the
        // driver can't get between the cars to cut them.
        var (route, _, toe) = StopWith(ModuleKind.Crates);
        var spur = route.Build().Branches.First(b => b.Kind == BranchKind.Spur && Math.Abs(b.Toe - toe) < 1e-6);
        int fit = SpurDrill.Capacity(T.Geometry, spur, Tuning.Route.Junctions.PointsLength);
        var night = new Night(cars: Math.Clamp(fit, 3, 5), walkers: 0, hands: 0, modules: ModuleKind.Crates);
        var train = night.Train;
        var run = night.World.Run!;
        int person = night.Add(new Waiting(), PlayerMotor.SpawnOnRoof(train, 1, 0, P)) - 1;
        var standing = night.Crew[person];
        double before = train.Vehicles.Sum(v => v.Load);
        var steps = new HashSet<string>();
        var legs = new HashSet<StopDriver.Leg>();
        night.Driver.Players = [night.Crew[person]];
        night.Until(() => run.Departures > 0, 1800, () =>
        {
            night.Driver.Players = [night.Crew[person]];
            legs.Add(night.Driver.Stops!.Doing);
            if (night.Driver.WorkStep is { Length: > 0 } d)
                steps.Add(d);
        });
        string where = string.Join(", ", night.Crew.Select((c, i) => $"{i}: {c.Surface} on {c.Parent}"));

        Assert.True(night.Train.Vehicles.Count - 1 <= fit, "the test's train goes in whole");
        Assert.Equal(1, night.Calls.People);
        Assert.True(run.Departures > 0, $"never left: driver {night.Driver.Stops!.Doing}; crew {where}; legs {string.Join(", ", legs)}; did {string.Join(", ", steps)}");
        Assert.Equal(night.Site.Index, Assert.Single(night.Driver.Stops!.Log).Facility);
        Assert.Contains(StopDriver.Leg.Loading, legs);
        Assert.Contains("picking one up", steps);
        Assert.True(train.Vehicles.Sum(v => v.Load) - before >= F.Crates.LoadPerCrate - 1e-6, "loaded nothing");
        Assert.Equal(1, train.TrainRakes);
        Assert.False(train.Diverging(night.Site.Spur));
        Assert.True(OpenSideDoors(train, night.Site.Side).Count == 0, $"doors open {string.Join(",", OpenSideDoors(train, night.Site.Side))}; did {string.Join(", ", steps)}; legs {string.Join(", ", night.Driver.Stops!.Log[0].Legs)}; loads {string.Join(",", train.Vehicles.Select(v => $"{v.Kind}:{v.Load:0.00}"))}");
        Assert.True(PlayerMotor.InCab(night.Crew[0], train), $"the driver's {night.Crew[0].Surface} on {night.Crew[0].Parent}");
        // The person never moved off their roof, and nobody waited on them for it.
        Assert.Equal(standing.Parent, night.Crew[person].Parent);
        Assert.Equal(Surface.Roof, night.Crew[person].Surface);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void ADriverAloneOnACarsLandingGetsDownAndBackUpIntoTheCab(int side)
    {
        // Note 300 (#38, a crew of one): the last door it shut at a stop left the driver on that car's landing, and the walk back
        // to the cab is on the ground: from a car's deck it went nowhere. Solo Frontier nights stood at the stop till the fire
        // died. Off the car by the side it's on, then round to the cab's steps and up.
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 1)), new RailLine(new LineDefinition("t", [new TrackSegment(4000)])), 600);
        var world = new World(train, Tuning.Combat);
        world.EnableBodies();
        var hand = new StopHand(StopJob.Driver, new CrewCalls(), 0);
        var layout = T.Geometry.Interior!;
        double w = train.Frames[1].Shape.Bounds.Max.X;
        var s = PlayerMotor.SpawnOnRoof(train, 1, 0, P) with
        {
            Surface = Surface.Deck,
            Position = new Ballast.Double3(side * (w + layout.StepWidth / 2), layout.FloorHeight, 0),
        };
        var doing = new HashSet<string>();
        for (uint tick = 0; tick < 60 * SimConstants.TickRate; tick++)
        {
            world.BeginTick();
            if (hand.SetBackAlone(s, world, null) is not { } intent)
                break;
            doing.Add(hand.Doing);
            world.CrewAct(ref s, intent, 1);
            world.Step(new TrainControls { Brake = 1 });
            PlayerMotor.Step(ref s, intent, train, P, T, SimConstants.TickSeconds, applyLook: false);
            Assert.True(s.Alive, $"died of {s.Death}");
        }
        Assert.True(PlayerMotor.InCab(s, train), $"{s.Surface} on {s.Parent} after a minute; did {string.Join(", ", doing)}");
    }

    [Fact]
    public void ATrainStoodAsFarPastItsHoldAsItMayLeavesThePointsFree()
    {
        // Note 300: the hold is two metres short of the points, and the driver took a stand up to three past it as there. Its
        // front on the points, they wouldn't go over, and a crew of one stood at the lever till the cold took it.
        var night = new Night(cars: 3, walkers: 0, hands: 0, modules: ModuleKind.Crates);
        night.Until(() => false, 1); // the driver says it'll work the stop itself
        var plan = StopPlan.Ahead(night.World, night.Train.Dynamics.Distance, new HashSet<int>(), night.Calls);
        Assert.NotNull(plan);
        var standing = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 3, 1)), night.Train.Line, plan.Hold + StopDriver.HoldOver, Tuning.Boiler);
        Assert.False(standing.PointsOccupied(plan.Spur.Index, Tuning.Route.Junctions.PointsLength));
        Assert.True(plan.StandingAt(standing));
    }

    [Fact]
    public void WithTheDawnCloseItRunsPast()
    {
        // Every stop is optional (GDD §18): with no time for one before the line goes live, the driver doesn't make it.
        var night = new Night(cars: 8);
        var run = night.World.Run!;
        run.Resume(run.Route.DawnSeconds - (run.Route.Length - night.Train.Dynamics.Distance) / 14 - 300, -1, night.Train.Boiler.Tender, 0);
        var zone = night.Site.Feature;
        night.Until(() => night.Train.Dynamics.Distance > zone.End + 50, 300);
        Assert.True(night.Train.Dynamics.Distance > zone.End + 50);
        Assert.Empty(night.Driver.Stops!.Log);
        Assert.Equal(2, night.Site.SledsLeft);
    }

    [Fact]
    public void AFullTrainDoesntStop()
    {
        // Whatever the crew, there's nothing to load into cars with no room.
        var night = new Night(cars: 8, walkers: 1, crateHands: true, modules: ModuleKind.Crates);
        foreach (var v in night.Train.Vehicles.Where(v => v.Kind == VehicleKind.Cargo))
            v.Load = 1;
        var zone = night.Site.Feature;
        night.Until(() => night.Train.Dynamics.Distance > zone.End + 50, 300);
        Assert.True(night.Train.Dynamics.Distance > zone.End + 50);
        Assert.Empty(night.Driver.Stops!.Log);
        Assert.Equal(1, night.Train.TrainRakes);
    }

    [Fact]
    public void ACrewCarriesTheCratesIntoTheCarsUpTheirSteps()
    {
        // A crates-only stop: the winch pair carry too, with one more hand. They go up each car's side steps and in.
        var night = new Night(cars: 8, walkers: 1, crateHands: true, modules: ModuleKind.Crates);
        var train = night.Train;
        var run = night.World.Run!;
        var cargoCars = train.Vehicles.Where(v => v.Kind == VehicleKind.Cargo).ToList();
        double before = cargoCars.Sum(v => v.Load);
        night.Until(() => run.Departures > 0, 1200);
        string where = string.Join(", ", night.Crew.Select((c, i) => $"{i}: {c.Surface} on {c.Parent} cold {c.Cold:0}"));

        Assert.True(run.Departures > 0, $"never left: driver {night.Driver.Stops!.Doing}; crew {where}");
        // Every crate that had a car with room went in: the cars that went down the spur leave the fortress part loaded.
        int fit = SpurDrill.Capacity(T.Geometry, train.Line.Branches[night.Site.Spur], Tuning.Route.Junctions.PointsLength);
        int room = (int)Math.Round(fit * (1 - Tuning.Run.DepartureLoad) / F.Crates.LoadPerCrate);
        double added = cargoCars.Sum(v => v.Load) - before;
        Assert.Equal(Math.Min(night.Site.CrateCount, room) * F.Crates.LoadPerCrate, added, 6);
        Assert.All(night.Crew, c => Assert.True(c.Alive, $"died of {c.Death}; crew {where}"));
        // Nobody left behind on the ballast (in the air over a gap is aboard), and the doors shut before it moved.
        Assert.All(night.Crew, c => Assert.NotEqual(Surface.Ground, c.Surface));
        Assert.Empty(OpenSideDoors(train, night.Site.Side));
        Assert.Equal(1, train.TrainRakes);
    }

    [Fact]
    public void WithTheCratesInHalfTheHandsSearchTheVillageAndBringTheFindsAboard()
    {
        // Note 326 (GDD App. F.3: "split up for more loot, or work the yard together, or the village together"): the crates
        // loaded, a share of the crate hands go round the walls into the village's open houses, search the hiding spots
        // nearest them, and carry what they find back into the cars; the driver waits for them, and they all leave together.
        var night = new Night(cars: 8, walkers: 3, crateHands: true, ids: true, loot: true, modules: ModuleKind.Crates);
        var train = night.Train;
        var run = night.World.Run!;
        int k = run.Stops.ToList().IndexOf(night.Site.Feature);
        Assert.Contains(run.HidingSpots, h => h.Stop == k);
        var hands = night.Bots.OfType<RoofWalkerBot>().Select(b => b.Job).OfType<StopHand>().Where(h => h.Job == StopJob.Crates).ToList();
        int most = 0;
        var doing = new List<string>();
        night.Until(() => run.Departures > 0, 1800, () =>
        {
            most = Math.Max(most, hands.Count(h => h.Searching is not null));
            foreach (var h in hands)
                if (h.Doing.Length > 0 && !doing.Contains(h.Doing))
                    doing.Add(h.Doing);
        });
        string trace = $"spots {run.HidingSpots.Count(h => h.Stop == k)}, searched {run.HidingSpots.Count(h => h.Stop == k && run.Searched(k, h.Container.Index))}, most out {most}, stowed {run.Stowed.Count}; the hands were {string.Join(", ", doing)}";
        string where = string.Join(", ", night.Crew.Select((c, i) => $"{i}: {c.Surface} on {c.Parent}"));
        Assert.True(run.Departures > 0, $"never left: driver {night.Driver.Stops!.Doing}; hands {string.Join(", ", hands.Select(h => h.Doing))}; crew {where}");
        // Some of the village searched, its finds stowed aboard, by no more than half the crate hands at once.
        Assert.True(run.HidingSpots.Any(h => h.Stop == k && run.Searched(k, h.Container.Index)), trace);
        Assert.True(run.Stowed.Any(f => run.HidingSpots.Any(h => h.Stop == k && h.Container.Index == f.Container)), trace);
        Assert.InRange(most, 1, (int)Math.Ceiling(hands.Count * F.Crew.VillageShare));
        // Nobody left behind, nobody dead, the train whole.
        Assert.All(night.Crew, c => Assert.True(c.Alive, $"died of {c.Death}; crew {where}"));
        Assert.All(night.Crew, c => Assert.NotEqual(Surface.Ground, c.Surface));
        Assert.Equal(1, train.TrainRakes);
    }

    [Fact]
    public void TwoHandsCarryTheHeavyCratesInTogether()
    {
        // As above, but the hands know who they are: the light crates in, then the heavy ones between two (T45).
        var night = new Night(cars: 8, walkers: 1, crateHands: true, ids: true, modules: ModuleKind.Crates);
        var train = night.Train;
        var run = night.World.Run!;
        var cargoCars = train.Vehicles.Where(v => v.Kind == VehicleKind.Cargo).ToList();
        double before = cargoCars.Sum(v => v.Load);
        var doing = new HashSet<string>();
        night.Until(() => run.Departures > 0, 1500, () =>
        {
            foreach (var b in night.Bots)
                if (b is RoofWalkerBot { Job.Doing: { Length: > 0 } d })
                    doing.Add(d);
        });
        string where = string.Join(", ", night.Crew.Select((c, i) => $"{i}: {c.Surface} on {c.Parent}"));
        Assert.True(run.Departures > 0, $"never left: driver {night.Driver.Stops!.Doing}; crew {where}; did {string.Join(", ", doing)}");
        Assert.Contains("carrying the back end", doing);
        int fit = SpurDrill.Capacity(T.Geometry, train.Line.Branches[night.Site.Spur], Tuning.Route.Junctions.PointsLength);
        double room = fit * (1 - Tuning.Run.DepartureLoad);
        double offered = night.Site.CrateCount * F.Crates.LoadPerCrate + night.Site.HeavyStack.Length * F.Crates.Heavy.LoadPerCrate;
        Assert.Equal(Math.Min(offered, room), cargoCars.Sum(v => v.Load) - before, 6);
        // None left lying at the site if there was room for it.
        if (offered <= room)
            Assert.DoesNotContain(night.World.Bodies.All, b => b.Kind == Physics.BodyKind.Heavy);
        Assert.All(night.Crew, c => Assert.True(c.Alive, $"died of {c.Death}; crew {where}"));
        Assert.All(night.Crew, c => Assert.NotEqual(Surface.Ground, c.Surface));
        Assert.True(OpenSideDoors(train, night.Site.Side).Count == 0,
            $"doors open at {night.World.Run!.Seconds:0}s: {string.Join(", ", night.Bots.OfType<RoofWalkerBot>().Select(b => b.Job?.Doing))}; driver {night.Driver.Stops!.Doing}");
    }

    /// <summary>Someone who stands still: a player holding a heavy crate's end, waiting.</summary>
    sealed class Waiting : IWorldBot
    {
        public string Name => "waiting";
        public PlayerIntent Decide(in PlayerState self, TrainOnLine train, uint tick) => default;
        public PlayerIntent Decide(in PlayerState self, World world, uint tick, out PlayerState aimed)
        {
            aimed = self;
            return default;
        }
    }

    [Fact]
    public void AHandComesToHelpAPlayerHoldingAHeavyCrate()
    {
        var night = new Night(cars: 8, walkers: 1, crateHands: true, ids: true, modules: ModuleKind.Crates);
        var run = night.World.Run!;
        night.Until(() => night.Site.Stocked, 900);
        Assert.True(night.Site.Stocked);
        // A player (not a bot) takes an end of one and waits.
        var heavy = night.World.Bodies.All.First(b => b.Kind == Physics.BodyKind.Heavy);
        var at = heavy.Centre;
        var player = PlayerMotor.SpawnOnGround(at + new Double3(0, 0, 1.0), night.Train.Line, night.Site.CrateLineHint, P);
        int id = night.Add(new Waiting(), player);
        heavy.Carrier = id;
        night.Until(() => heavy.Lifted, 600);
        Assert.True(heavy.Lifted, $"nobody came: {string.Join(", ", night.Bots.OfType<RoofWalkerBot>().Select(b => b.Job?.Doing))}");
        Assert.NotEqual(id, heavy.Second);
        Assert.Equal(id, heavy.Carrier);
    }

    [Fact]
    public void EachOpenDoorIsShutByOneHandThatsStillAtIt()
    {
        // T50: doors used to be dealt out by position, and one dealt to a hand that had gone aboard stayed open.
        var calls = new CrewCalls();
        var s = new PlayerState { Health = P.Health };
        foreach (int member in new[] { 1, 2, 3 })
            calls.Say(member, StopJob.Crates, s);
        double Near1(int car) => car == 10 ? 0 : 5;
        Assert.Equal(10, calls.ClaimDoor(1, [10, 11], Near1));
        // The next hand takes the other door, not the claimed one, and a third finds none left for it.
        Assert.Equal(11, calls.ClaimDoor(2, [10, 11], Near1));
        Assert.Null(calls.ClaimDoor(3, [10, 11], Near1));
        // A hand keeps its claim while that door's open, and loses it once it's shut.
        Assert.Equal(10, calls.ClaimDoor(1, [10, 11], car => 0));
        Assert.Null(calls.ClaimDoor(3, [11], Near1));
        // One that's dead (or gone off to warm) gives its door up to whoever's still at it.
        calls.Say(2, StopJob.Crates, s with { Health = 0, Death = DeathCause.Mauled });
        Assert.Equal(11, calls.ClaimDoor(3, [11], Near1));
        calls.Unclaim(3);
        Assert.Equal(11, calls.ClaimDoor(1, [11], Near1));
    }

    static List<int> OpenSideDoors(TrainOnLine train, int side) =>
        [.. train.Vehicles.Where(v => v.Kind == VehicleKind.Cargo && StopHand.SideDoor(train.Frames[v.Id].Shape, side) is { } d && v.DoorOpen(d)).Select(v => v.Id)];

    [Fact]
    public void ACrewCoalsUpAtTheTowerAndShutsTheChuteInTime()
    {
        // Spec B.6's tender runs down over a night; a coaling tower fills it (D.2 gravity chute). The driver stops with the
        // tender under the spout, the shunter works the lever, and shuts it before the tender overflows.
        var night = new Night(cars: 8, coaling: true);
        var train = night.Train;
        var bt = train.BoilerTuning!;
        train.Boiler.Tender = bt.TenderCapacity * 0.4;
        double before = train.Boiler.Tender;
        var zone = night.World.Run!.Route.Of(FeatureKind.Facility).First(f => f.Facility == FacilityKind.CoalingTower);
        night.Until(() => train.Dynamics.Distance > zone.End + 50, 600);
        string where = string.Join(", ", night.Crew.Select((c, i) => $"{i}: {c.Surface} on {c.Parent}"));

        Assert.True(train.Dynamics.Distance > zone.End + 50, $"stuck: driver {night.Driver.Stops!.Doing}; crew {where}");
        var stop = Assert.Single(night.Driver.Stops!.Log);
        Assert.Equal(nameof(FacilityKind.CoalingTower), stop.Kind);
        Assert.True(stop.Coal > 100, $"took {stop.Coal}");
        Assert.True(train.Boiler.Tender > before + 100);
        // Shut in time: no overflow onto the engine, and the chute's shut as it leaves.
        Assert.Equal(1, train.Vehicles[0].Integrity, 6);
        Assert.False(night.World.Run.ChuteOpen);
        Assert.All(night.Crew, c => Assert.True(c.Alive));
        Assert.All(night.Crew, c => Assert.NotEqual(Surface.Ground, c.Surface));
    }

    [Fact]
    public void ASwitchSetWrongAheadIsSetBackOnTheGround()
    {
        // App. A.7 "verify every switch on the ground": the lamp reads wrong from the cab, the driver stops short of the
        // points, the shunter sets it back, and the train goes on down the main line.
        var night = new Night(cars: 8, deadLine: true);
        var train = night.Train;
        var toe = train.Line.Branches[night.Branch].Toe;
        night.World.SetSwitch(night.Branch, true);
        bool tookIt = false;
        night.Until(() => train.Dynamics.Distance > toe + 150, 600, () => tookIt |= train.Dynamics.Path == night.Branch && train.Dynamics.Distance > toe);
        string where = string.Join(", ", night.Crew.Select((c, i) => $"{i}: {c.Surface} on {c.Parent}"));

        Assert.True(train.Dynamics.Distance > toe + 150, $"stuck: driver {night.Driver.Stops!.Doing}; crew {where}");
        Assert.False(tookIt);
        Assert.False(train.Diverging(night.Branch));
        Assert.Equal(RailLine.MainPath, train.Dynamics.Path);
        Assert.Contains(night.Driver.Stops!.Log, r => r.Kind == "SwitchSetBack");
        Assert.All(night.Crew, c => Assert.NotEqual(Surface.Ground, c.Surface));
    }

    [Fact]
    public void DownADeadLineItBacksOutAndGoesOn()
    {
        // Taken: stop on the dead line, back out past the points, set the switch back, and go on down the main line.
        var night = new Night(cars: 8, deadLine: true);
        var train = night.Train;
        var toe = train.Line.Branches[night.Branch].Toe;
        night.World.SetSwitch(night.Branch, true);
        var state = train.Capture();
        train.Restore(state with { Rakes = [state.Rakes[0] with { Path = night.Branch, Distance = toe + 120, Velocity = 0 }] });
        night.Until(() => train.OnMain && train.Dynamics.Distance > toe + 150, 900);
        string where = string.Join(", ", night.Crew.Select((c, i) => $"{i}: {c.Surface} on {c.Parent}"));

        Assert.True(train.OnMain && train.Dynamics.Distance > toe + 150, $"stuck: driver {night.Driver.Stops!.Doing} at {train.Dynamics.Distance:0} on {train.Dynamics.Path}; crew {where}");
        Assert.False(train.Diverging(night.Branch));
        Assert.All(night.Crew, c => Assert.True(c.Alive));
        Assert.All(night.Crew, c => Assert.NotEqual(Surface.Ground, c.Surface));
    }

    /// <summary>A shunter who says it's the shunter and never comes (on the guard van's roof).</summary>
    sealed class Idle(IWorldBot bot) : IWorldBot
    {
        public string Name => bot.Name;
        public PlayerIntent Decide(in PlayerState self, TrainOnLine train, uint tick) => default;
        public PlayerIntent Decide(in PlayerState self, World world, uint tick, out PlayerState aimed)
        {
            bot.Decide(self, world, tick, out aimed);
            return default;
        }
    }

    [Fact]
    public void WithAShunterWhoNeverComesTheDriverSetsTheSwitchBackItself()
    {
        // T106: a deepTerritory:1 crew of two stood at a switch from 1914 s till dawn, the shunter alive on the guard van's roof.
        // Down to set it back itself, the driver took hold of the cab's steps to climb back and let go again, all night.
        var night = new Night(cars: 8, deadLine: true);
        night.Bots[1] = new Idle(night.Bots[1]);
        var train = night.Train;
        var toe = train.Line.Branches[night.Branch].Toe;
        night.World.SetSwitch(night.Branch, true);
        night.Until(() => train.OnMain && train.Dynamics.Distance > toe + 150, 900);
        Assert.True(train.OnMain && train.Dynamics.Distance > toe + 150, $"stuck: driver {night.Driver.Stops!.Doing} at {train.Dynamics.Distance:0} on {train.Dynamics.Path}, the driver {night.Crew[0].Surface} on {night.Crew[0].Parent}");
        Assert.False(train.Diverging(night.Branch));
        Assert.True(PlayerMotor.InCab(night.Crew[0], train), "and the driver's back at the controls");
    }

    // GDD §18's set pieces (WP15, ARCHITECTURE §8 note 185): a bot crew works each, through intent alone.

    static FacilityWorkReport Work(FacilityKind kind)
    {
        var (route, facility) = FacilityWork.Find(Tuning.Route, kind)!.Value;
        return FacilityWork.Run(route, facility, T, P, Tuning.Boiler, Tuning.Run, F, Tuning.Route.Junctions, cars: 8, hands: 2, seconds: 1200,
            yardLength: Tuning.Route.YardLength);
    }

    static void LeftWellAndWhole(FacilityWorkReport r)
    {
        string said = $"legs {string.Join(">", r.Legs)}; did {string.Join(", ", r.Doing)}; deaths {string.Join(", ", r.Deaths)}";
        Assert.True(r.Departed, $"never left: {said}");
        Assert.True(r.Alive == r.Crew, said);
        Assert.Equal(1, r.Rakes);
        Assert.True(r.SwitchBack, said);
    }

    [Fact]
    public void AtTheGrainElevatorTheDriverWalksEachCarUnderTheSpoutWhileTheShunterPours()
    {
        var r = Work(FacilityKind.GrainElevator);
        LeftWellAndWhole(r);
        Assert.Contains("Spouting", r.Legs);
        Assert.Contains("pouring", r.Doing);
        // Every car that went down the spur (the engine and four) came back full of grain; the rest weren't touched.
        var spurCars = r.Loads.Take(4).ToList();
        Assert.All(spurCars, c => Assert.Equal(1, c.Load, 3));
        Assert.All(spurCars, c => Assert.Equal(CargoKind.Food, c.Cargo));
        // Let go as each filled: at most a tick or two's overflow, nothing that strains a car beyond the stop's own knocks.
        Assert.All(spurCars, c => Assert.True(c.Integrity > 0.95, $"integrity {c.Integrity}"));
        Assert.Equal(F.Spout.Bin - spurCars.Count * (1 - Tuning.Run.DepartureLoad), r.Bin, 1);
    }

    [Fact]
    public void AtTheSlaughterhouseThePairDriveTheHerdUpTheRamp()
    {
        var r = Work(FacilityKind.Slaughterhouse);
        LeftWellAndWhole(r);
        Assert.Contains("driving the herd", r.Doing);
        Assert.Contains(r.Loads, c => c.Cargo == CargoKind.Livestock && c.Load >= 1 - 1e-6);
    }

    [Fact]
    public void AtTheChemicalWorksTheHoseGoesOnIsMindedAndComesOffBeforeTheTrainMoves()
    {
        var r = Work(FacilityKind.ChemicalWorks);
        LeftWellAndWhole(r);
        Assert.Contains("putting the hose on", r.Doing);
        Assert.Contains("minding the hose", r.Doing);
        Assert.Contains("taking the hose off", r.Doing);
        // Taken off cleanly: never leaked, never torn, nothing spoiled by it.
        Assert.Equal(-1, r.HoseCar);
        Assert.False(r.Leaking);
        Assert.Contains(r.Loads, c => c.Cargo == CargoKind.Chemicals && c.Load >= 1 - 1e-6);
        Assert.All(r.Loads, c => Assert.True(c.CargoIntegrity > 1 - F.Hose.TornSpoil / 2, $"spoiled to {c.CargoIntegrity}"));
    }

    [Fact]
    public void AtTheMilitaryDepotTheCrateHandsSetThePowderDownAndNothingGoesUp()
    {
        var r = Work(FacilityKind.MilitaryDepot);
        LeftWellAndWhole(r);
        Assert.Contains("putting it down", r.Doing);
        Assert.Contains(r.Loads, c => c.Cargo == CargoKind.Ammunition);
        Assert.DoesNotContain(r.Deaths, d => d.Contains(nameof(DeathCause.Keg)));
        Assert.All(r.Loads, c => Assert.True(c.Integrity > 1 - F.Kegs.CarDamage / 2, $"integrity {c.Integrity}"));
    }

    // GDD §18's switchyard and wreck yard (WP15b, ARCHITECTURE §8 note 187).

    [Fact]
    public void AtTheSwitchyardTheCrewFetchTheStandingCarsASidingAtATime()
    {
        var r = Work(FacilityKind.Switchyard);
        LeftWellAndWhole(r);
        // Its own track's crates loaded, then a pick-up for each siding with cars standing on it: every one of them brought
        // away, ahead of the engine, still carrying what they stood with.
        Assert.Contains(r.Stops, x => x.Kind == nameof(FacilityKind.Switchyard));
        Assert.Contains(r.Stops, x => x.Kind.StartsWith(nameof(FacilityKind.Switchyard) + "PickUp"));
        Assert.Equal(0, r.StillStanding);
        Assert.NotEmpty(r.PickedUp);
        Assert.Equal(r.PickedUp.Select(c => c.Id), r.Order.Take(r.PickedUp.Count));
        Assert.All(r.PickedUp, c => Assert.True(c.Load > 0 && c.Cargo != CargoKind.None));
        Assert.Contains("cutting", r.Doing);
    }

    [Fact]
    public void AtTheWreckYardTheHandsCarryOutWhatTheHeadlampFindsAndKeepClearWhenItGroans()
    {
        var r = Work(FacilityKind.WreckYard);
        LeftWellAndWhole(r);
        Assert.Contains("picking one up", r.Doing);
        Assert.Contains(r.Loads, c => c.Cargo == CargoKind.Salvage);
        // The headlamp found some, not all: the rest wait for a lamp (bots carry none).
        Assert.Contains(r.Heaps, h => h.Found);
        Assert.Contains(r.Heaps, h => !h.Found && h.Unfound > 0);
        Assert.DoesNotContain(r.Deaths, d => d.Contains(nameof(DeathCause.Wreckage)));
    }
}
