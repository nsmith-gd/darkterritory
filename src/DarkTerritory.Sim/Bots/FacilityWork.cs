using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Bots;

/// <summary>What a bot crew made of one facility (<see cref="FacilityWork"/>), for <c>dt facility drill &lt;kind&gt;</c> and the tests.</summary>
/// <param name="Legs">Every leg of the driver's the stop went through, in order.</param>
/// <param name="Doing">Everything the hands said they were doing, in the order they first said it.</param>
/// <param name="Loads">Each cargo car's load and cargo afterwards, front to back.</param>
public sealed record FacilityWorkReport(string Facility, bool Departed, double Seconds, IReadOnlyList<string> Legs, IReadOnlyList<string> Doing,
    IReadOnlyList<(double Load, CargoKind Cargo, double CargoIntegrity, double Integrity)> Loads, double LoadedBefore, double LoadedAfter,
    double Bin, int Head, int HoseCar, bool Leaking, int Rakes, bool SwitchBack, int Alive, int Crew, IReadOnlyList<string> Deaths, StopRecord? Record);

/// <summary>
/// A bot crew works one facility stop from a standing start short of it, through intent alone (CLAUDE.md: bots use the
/// same path as players): the driver in the cab, the shunter, the winch pair and crate hands, until the train has left the
/// stop or the time's up. GDD §18's set pieces (note 185) are worked like everything else: the shunter on the grain
/// elevator's spout lever while the driver walks the cars under it, the pair driving the herd and minding the hose.
/// </summary>
public static class FacilityWork
{
    public static FacilityWorkReport Run(Route.Route route, int facility, TrainTuning t, PlayerTuning p, BoilerTuning? boiler, RunTuning run,
        FacilityTuning facilities, JunctionTuning junctions, int cars = 8, int hands = 2, double seconds = 1500, double yardLength = 600)
    {
        var features = route.Of(FeatureKind.Facility).ToList();
        var line = route.Build();
        var spur = line.Branches.First(b => b.Kind == BranchKind.Spur && features[facility].Contains(b.Toe));
        var calls = new CrewCalls();
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(t, cars, run.DepartureLoad)), line, spur.Toe - 600, boiler);
        var world = new World(train);
        world.EnableBodies();
        world.EnableSwitches(junctions);
        world.EnableRun(run, route, yardLength, authority: true, facilities);
        var site = world.Run!.Sites[facility]!;
        var bots = new List<IWorldBot>();
        var crew = new List<PlayerState>();
        var driver = new ConductorBot(calls, 0);
        bots.Add(driver);
        crew.Add(PlayerMotor.SpawnInCab(train, p));
        StopJob[] jobs = [StopJob.Shunter, StopJob.Winch0, StopJob.Winch1, .. Enumerable.Repeat(StopJob.Crates, hands)];
        for (int i = 0; i < jobs.Length; i++)
        {
            var hand = new StopHand(jobs[i], calls, i + 1, p.Cold) { PlayerId = i + 2 };
            bots.Add(new RoofWalkerBot(11 + i, p.Cold, hand));
            crew.Add(PlayerMotor.SpawnOnRoof(train, 1 + i % (cars - 1), i * 1.5 - 3, p));
        }
        double before = train.Vehicles.Where(v => v.Kind == VehicleKind.Cargo).Sum(v => v.Load);
        var controls = new TrainControls { Reverser = 1 };
        var legs = new List<string>();
        var doing = new List<string>();
        var deaths = new List<string>();
        int ticks = 0;
        for (uint tick = 0; ticks < seconds * SimConstants.TickRate && world.Run.Departures == 0 && !world.Run.Over; tick++, ticks++)
        {
            world.BeginTick();
            if (crew.Any(c => CabControls.CanDrive(c, train)) && CabControls.Clears(controls, train, false))
                controls.Brake = 0;
            var intents = new PlayerIntent[crew.Count];
            for (int i = 0; i < crew.Count; i++)
            {
                intents[i] = bots[i].Decide(crew[i], world, tick, out _);
                var s = crew[i];
                if (CabControls.ReleasesBrake(intents[i], s, train))
                    controls.Brake = 0;
                CabControls.Apply(ref controls, intents[i], s, train);
                world.CrewAct(ref s, intents[i], i + 1);
                crew[i] = s;
            }
            world.Step(controls);
            world.ApplyDamage(id => id >= 1 && id <= crew.Count ? crew[id - 1] : null, (id, s) => crew[id - 1] = s, Enumerable.Range(1, crew.Count));
            for (int i = 0; i < crew.Count; i++)
            {
                var s = crew[i];
                bool alive = s.Alive;
                PlayerMotor.Step(ref s, intents[i], train, p, t, SimConstants.TickSeconds, applyLook: false);
                crew[i] = s;
                if (!s.Alive && !deaths.Contains($"{i + 1}: {s.Death}"))
                    deaths.Add($"{i + 1}: {s.Death}");
            }
            world.StepBodies([.. crew.Select((c, i) => (i + 1, c))]);
            world.StepRun(crew);
            if (driver.Stops?.Doing.ToString() is { } leg && (legs.Count == 0 || legs[^1] != leg))
                legs.Add(leg);
            foreach (var b in bots)
                if (b is RoofWalkerBot { Job: { Doing.Length: > 0 } h } && !doing.Contains(h.Doing))
                    doing.Add(h.Doing);
        }
        var cargo = train.Vehicles.Where(v => v.Kind == VehicleKind.Cargo).ToList();
        return new FacilityWorkReport($"{features[facility].Facility} ({facility})", world.Run.Departures > 0, Math.Round(ticks * SimConstants.TickSeconds, 1),
            legs, doing, [.. cargo.Select(v => (Math.Round(v.Load, 3), v.Cargo, Math.Round(v.CargoIntegrity, 3), Math.Round(v.Integrity, 3)))],
            Math.Round(before, 3), Math.Round(cargo.Sum(v => v.Load), 3), Math.Round(site.Bin, 3), site.Head, site.HoseCar, site.Leaking,
            train.Rakes.Count, !train.Diverging(spur.Index), crew.Count(c => c.Alive), crew.Count, deaths, driver.Stops?.Log.LastOrDefault());
    }

    /// <summary>
    /// A route (from the hand-tuned generator) with a facility of this kind down a spur: the route and its index. The first
    /// frontier night's first live facility down a spur, made that kind (any spur facility's track and layout will do, and
    /// the deeper tiers' kinds don't come up on a frontier night).
    /// </summary>
    public static (Route.Route Route, int Facility)? Find(RouteTuning tuning, FacilityKind kind, int seeds = 200)
    {
        for (ulong seed = 1; seed < (ulong)seeds; seed++)
        {
            var route = RouteGenerator.Generate(tuning, RouteTier.Frontier, seed);
            var features = route.Features.ToList();
            int i = features.FindIndex(f => f.Kind == FeatureKind.Facility && f.Facility is not (null or FacilityKind.CoalingTower) && f.Modules is null
                && (f.Stop?.Power ?? Stops.PowerState.Live) == Stops.PowerState.Live && route.Branches.Any(b => b.Kind == BranchKind.Spur && f.Contains(b.Toe)));
            if (i < 0)
                continue;
            features[i] = features[i] with { Facility = kind };
            var made = route with { Features = features };
            return (made, made.Of(FeatureKind.Facility).ToList().IndexOf(features[i]));
        }
        return null;
    }
}
