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

    sealed class Night
    {
        public readonly World World;
        public readonly List<IWorldBot> Bots = [];
        public readonly List<PlayerState> Crew = [];
        public readonly ConductorBot Driver;
        public readonly Site Site;
        TrainControls _controls = new() { Reverser = 1 };
        uint _tick;

        /// <summary>
        /// Running up to the stop from a standing start 600 m short of it: a crew of the driver, a shunter, the winch pair
        /// (unless <paramref name="winchPair"/> is false) and <paramref name="walkers"/> more on the roofs.
        /// </summary>
        public Night(int cars, int walkers = 1, bool winchPair = true, bool crateHands = false, params ModuleKind[] modules)
        {
            var (route, facility, toe) = StopWith(modules.Length > 0 ? modules : [ModuleKind.Winch]);
            var calls = new CrewCalls();
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, cars, Tuning.Run.DepartureLoad)), route.Build(), toe - 600, Tuning.Boiler);
            World = new World(train);
            World.EnableBodies();
            World.EnableSwitches(Tuning.Route.Junctions);
            World.EnableRun(Tuning.Run, route, Tuning.Route.YardLength, authority: true, F);
            Site = World.Run!.Sites[facility]!;
            Driver = new ConductorBot(calls, 0);
            Add(Driver, PlayerMotor.SpawnInCab(train, P));
            Add(new RoofWalkerBot(11, P.Cold, new StopHand(StopJob.Shunter, calls, 1, P.Cold)), PlayerMotor.SpawnOnRoof(train, 1, 0, P));
            var (a, b) = winchPair ? (StopJob.Winch0, StopJob.Winch1) : (StopJob.None, StopJob.None);
            Add(new RoofWalkerBot(12, P.Cold, new StopHand(a, calls, 2, P.Cold)), PlayerMotor.SpawnOnRoof(train, 2, 0, P));
            Add(new RoofWalkerBot(13, P.Cold, new StopHand(b, calls, 3, P.Cold)), PlayerMotor.SpawnOnRoof(train, cars - 1, 2, P));
            for (int w = 0; w < walkers; w++)
                Add(new RoofWalkerBot(20 + w, P.Cold, new StopHand(crateHands ? StopJob.Crates : StopJob.None, calls, 4 + w, P.Cold)),
                    PlayerMotor.SpawnOnRoof(train, 3 + w % (cars - 3), -3, P));
        }

        void Add(IWorldBot bot, PlayerState s)
        {
            Bots.Add(bot);
            Crew.Add(s);
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
        Assert.Superset(new HashSet<StopDriver.Leg>(Enum.GetValues<StopDriver.Leg>()), seen);
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

    static List<int> OpenSideDoors(TrainOnLine train, int side) =>
        [.. train.Vehicles.Where(v => v.Kind == VehicleKind.Cargo && StopHand.SideDoor(train.Frames[v.Id].Shape, side) is { } d && v.DoorOpen(d)).Select(v => v.Id)];
}
