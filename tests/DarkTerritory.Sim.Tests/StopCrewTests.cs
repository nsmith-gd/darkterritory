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

    /// <summary>
    /// A route whose first facility (before any other kind of stop) has these modules down its spur: the facility's index
    /// and where the spur's points are.
    /// </summary>
    static (Route.Route Route, int Facility, double Toe) StopWith(params ModuleKind[] modules)
    {
        for (ulong seed = 1; seed < 200; seed++)
        {
            var route = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, seed);
            var facilities = route.Of(FeatureKind.Facility).ToList();
            int i = facilities.FindIndex(f => f.Facility is { } k && F.ModulesOf(k).Order().SequenceEqual(modules.Order()));
            if (i >= 0 && route.Branches.FirstOrDefault(b => b.Kind == BranchKind.Spur && facilities[i].Contains(b.Toe)) is { } spur)
                return (route, i, spur.Toe);
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
        public Night(int cars, int walkers = 1, bool winchPair = true, bool crateHands = false, bool coaling = false, bool deadLine = false,
            bool ids = false, params ModuleKind[] modules)
        {
            var (route, facility, toe) = deadLine ? DeadLine() : coaling ? CoalingTower() : StopWith(modules.Length > 0 ? modules : [ModuleKind.Winch]);
            var calls = new CrewCalls();
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, cars, Tuning.Run.DepartureLoad)), route.Build(), toe - 600, Tuning.Boiler);
            World = new World(train);
            World.EnableBodies();
            World.EnableSwitches(Tuning.Route.Junctions);
            World.EnableRun(Tuning.Run, route, Tuning.Route.YardLength, authority: true, F);
            Site = deadLine ? null! : World.Run!.Sites[facility]!;
            Branch = deadLine ? facility : -1;
            Driver = new ConductorBot(calls, 0);
            Add(Driver, PlayerMotor.SpawnInCab(train, P));
            Add(new RoofWalkerBot(11, P.Cold, new StopHand(StopJob.Shunter, calls, 1, P.Cold)), PlayerMotor.SpawnOnRoof(train, 1, 0, P));
            var (a, b) = winchPair ? (StopJob.Winch0, StopJob.Winch1) : (StopJob.None, StopJob.None);
            Add(new RoofWalkerBot(12, P.Cold, new StopHand(a, calls, 2, P.Cold)), PlayerMotor.SpawnOnRoof(train, 2, 0, P));
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
                _controls.Brake = 0;
                var intents = new PlayerIntent[Crew.Count];
                for (int i = 0; i < Crew.Count; i++)
                {
                    intents[i] = Bots[i].Decide(Crew[i], World, _tick, out _);
                    var s = Crew[i];
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
        Assert.Single(night.Train.Rakes);
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
        Assert.Single(train.Rakes);
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
        // The winch needs two (spec D.2): without them there's nothing to stop for, so the driver doesn't.
        var night = new Night(cars: 8, winchPair: false);
        var train = night.Train;
        var zone = night.Site.Feature;
        bool cut = false, diverged = false;
        night.Until(() => train.Dynamics.Distance > zone.End + 50, 300, () =>
        {
            cut |= train.Rakes.Count > 1;
            diverged |= train.Diverging(night.Site.Spur);
        });
        Assert.True(train.Dynamics.Distance > zone.End + 50, $"still at {train.Dynamics.Distance:0} (zone ends {zone.End:0})");
        Assert.False(cut);
        Assert.False(diverged);
        Assert.Empty(night.Driver.Stops!.Log);
        Assert.Equal(2, night.Site.SledsLeft);
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
        Assert.Single(night.Train.Rakes);
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
        Assert.Single(train.Rakes);
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
}

