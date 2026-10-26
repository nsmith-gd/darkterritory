using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

/// <summary>`dt wreck`: a derailment run to rest headless (T117), car by car: how far each went, how it lies, what snapped.</summary>
static class WreckCommands
{
    public static object Run(string content, string[] args)
    {
        if (args.Contains("--film"))
            return Film(content, args);
        double speed = Opt(args, "--speed", 22);
        int cars = (int)Opt(args, "--cars", 6);
        var tuning = DataFile.Load<WreckTuning>(Path.Combine(content, WreckTuning.File));
        var trainTuning = DataFile.Load<TrainTuning>(Path.Combine(content, TrainTuning.File));
        // Into a 300 m curve at speed: the engine goes off its outside, the rest after it.
        // --straight: on the level and straight (a Sleeper, points run through), nothing to throw it but the kick.
        var line = args.Contains("--straight") ? new RailLine(new LineDefinition("wreck", [new TrackSegment(5000)]))
            : new RailLine(new LineDefinition("wreck", [new TrackSegment(2000), new TrackSegment(600, 300), new TrackSegment(2000)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(trainTuning, cars, 1)), line, 2250);
        train.Dynamics.Velocity = speed;
        var world = new World(train) { WreckTuning = tuning };
        world.Derail("dt wreck");
        var wreck = train.Wreck!;
        var start = wreck.Bodies.ToDictionary(b => b.Vehicle, b => b.Origin);
        var timeline = new List<object>();
        int impacts = 0;
        double maxRoll = 0;
        for (int i = 0; i < tuning.MaxSeconds * 4 * SimConstants.TickRate && !wreck.Settled; i++)
        {
            world.Step(default);
            impacts += wreck.Impacts.Count;
            foreach (var b in wreck.Bodies)
                maxRoll = Math.Max(maxRoll, Math.Acos(Math.Clamp(Double3.Dot(b.Up, Double3.Up), -1, 1)) * 180 / Math.PI);
            if (i % SimConstants.TickRate == 0)
                timeline.Add(new { t = Math.Round(wreck.Seconds, 1), speeds = wreck.Bodies.Select(b => Math.Round(b.Velocity.Length, 1)).ToArray(), snapped = wreck.Snapped });
        }
        return new
        {
            speed,
            cars,
            settled = wreck.Settled,
            seconds = Math.Round(wreck.Seconds, 1),
            realSeconds = Math.Round(wreck.RealSeconds, 1),
            snapped = wreck.Snapped,
            impacts,
            maxRollDegrees = Math.Round(maxRoll),
            bodies = wreck.Bodies.Select(b => new
            {
                vehicle = b.Vehicle,
                travelled = Math.Round((b.Origin - start[b.Vehicle]).Length, 1),
                height = Math.Round(b.Origin.Y, 2),
                rollDegrees = Math.Round(Math.Acos(Math.Clamp(Double3.Dot(b.Up, Double3.Up), -1, 1)) * 180 / Math.PI),
                headingDegrees = Math.Round(Math.Atan2(b.Back.X, b.Back.Z) * 180 / Math.PI),
            }),
            timeline,
        };
    }

    /// <summary>
    /// dt wreck --film [--crews 1,4,8] [--speeds 8,20,30] [--cars 6] [--straight]: GDD v1.4 App. E.11's checks of the film
    /// (note 1032), for each crew size at each speed: a crew spread down the train (the cab, then the roofs), derailed, the
    /// film recorded with every particle watched at the end of every substep ("no interpenetration": none more than
    /// restInside, 2 cm, inside a solid) and every finished shot's camera checked ("clean camera", E.4 O3 and O4). The
    /// verdict's in "pass"; FilmTests holds a few of these. --route tier:seed [--at tunnel|bridge|metres] (note 1055): on that
    /// night's line instead, its walls (the lineside's trees, the stops') and its tunnels' rock standing, the engine come off
    /// there: in its first tunnel, on its first bridge, or that far along.
    /// </summary>
    static object Film(string content, string[] args)
    {
        var tuning = DataFile.Load<WreckTuning>(Path.Combine(content, WreckTuning.File));
        var trainTuning = DataFile.Load<TrainTuning>(Path.Combine(content, TrainTuning.File));
        var player = DataFile.Load<DarkTerritory.Sim.Player.PlayerTuning>(Path.Combine(content, DarkTerritory.Sim.Player.PlayerTuning.File));
        int cars = (int)Opt(args, "--cars", 6);
        var crews = Str(args, "--crews", "1,2,4,6,8").Split(',').Select(int.Parse).ToList();
        var speeds = Str(args, "--speeds", "8,14,20,26,30").Split(',').Select(x => double.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        var route = Str(args, "--route", "") is { Length: > 0 } spec ? DarkTerritory.Sim.LineGen.Routes.Generate(content, spec, cars) : null;
        var run = route is null ? null : DataFile.Load<DarkTerritory.Sim.Run.RunTuning>(Path.Combine(content, DarkTerritory.Sim.Run.RunTuning.File));
        double front = route is null ? 2250 : At(route, Str(args, "--at", "tunnel"));
        var rows = new List<object>();
        bool pass = true;
        foreach (int crew in crews)
            foreach (double speed in speeds)
            {
                var line = route?.Build() ?? (args.Contains("--straight") ? new RailLine(new LineDefinition("wreck", [new TrackSegment(5000)]))
                    : new RailLine(new LineDefinition("wreck", [new TrackSegment(2000), new TrackSegment(600, 300), new TrackSegment(2000)])));
                var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(trainTuning, cars, 1)), line, front);
                train.Dynamics.Velocity = speed;
                train.RefreshFrames();
                var world = new World(train) { WreckTuning = tuning };
                world.EnableBodies();
                if (route is not null)
                    world.EnableRun(run!, route, route.GateOr(DarkTerritory.Sim.Route.RouteTuning.Load(content).YardLength), authority: true);
                world.BeginTick();
                // As FilmTests' crew, apart: the cab, then a roof each down the train, 3 m back and forth, a round further back past it.
                for (int i = 0; i < crew; i++)
                {
                    var s = i == 0 ? DarkTerritory.Sim.Player.PlayerMotor.SpawnInCab(train, player)
                        : DarkTerritory.Sim.Player.PlayerMotor.SpawnOnRoof(train, 1 + i % cars, (i % 3 - 1) * 3.0 + i / cars * 1.5, player);
                    world.CrewAct(ref s, default, i);
                }
                world.Derail("dt wreck --film");
                var start = world.Film!;
                double Ground(double x, double z)
                {
                    double hint = start.Along;
                    return DarkTerritory.Sim.Player.PlayerMotor.GroundAt(new Double3(x, 0, z), train.Line, ref hint);
                }
                // --no-world: the film as it was before note 1055, the world's solids only looked for.
                var solids = route is null ? null : world.FilmWorld();
                bool met = !args.Contains("--no-world");
                var probe = new FilmProbe { Unmet = met ? null : solids };
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var recording = WreckFilm.Record(tuning, start, Ground, probe: probe, world: met ? solids : null);
                double recordMs = clock.Elapsed.TotalMilliseconds;
                var film = WreckFilm.Shoot(tuning, start, Ground, recording, world: met ? solids : null);
                var faults = film.CameraFaults(Ground);
                bool ok = probe.Over == 0 && probe.CarsInside == 0 && faults.Count == 0;
                pass &= ok;
                rows.Add(new
                {
                    crew,
                    speed,
                    pass = ok,
                    recordMs = Math.Round(recordMs),
                    interpenetration = new
                    {
                        over = probe.Over,
                        inside = probe.Inside,
                        seen = probe.Seen,
                        deepest = Math.Round(probe.Deepest, 4),
                        at = Math.Round(probe.At, 3),
                        what = probe.What,
                        worst = probe.Worst.Select(w => new { depth = Math.Round(w.Depth, 4), at = Math.Round(w.At, 3), w.What }),
                        carsInside = probe.CarsInside,
                        carDeepest = Math.Round(probe.CarDeepest, 3)
                    },
                    camera = faults,
                });
            }
        return new { route = route?.Name, at = route is null ? (double?)null : Math.Round(front), restInside = tuning.Film.RestInside, lensClearance = tuning.Film.LensClearance, pass, rows };
    }

    /// <summary>
    /// Where on a night's line the engine comes off (note 1055): 60 m into its first tunnel (or half way, a short one), the
    /// middle of its first bridge, or a distance along.
    /// </summary>
    public static double At(DarkTerritory.Sim.Route.Route route, string at)
    {
        if (double.TryParse(at, System.Globalization.CultureInfo.InvariantCulture, out double m))
            return m;
        var kind = at == "bridge" ? DarkTerritory.Sim.Route.FeatureKind.Bridge : DarkTerritory.Sim.Route.FeatureKind.Tunnel;
        var f = route.Features.FirstOrDefault(x => x.Kind == kind) ?? throw new ArgumentException($"{route.Name} has no {at}");
        return kind == DarkTerritory.Sim.Route.FeatureKind.Bridge ? (f.Start + f.End) / 2 : f.Start + Math.Min(60, (f.End - f.Start) / 2);
    }

    static string Str(string[] args, string name, string fallback)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
    }

    static double Opt(string[] args, string name, double fallback)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length && double.TryParse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }
}
