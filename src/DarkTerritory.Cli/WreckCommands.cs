using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

/// <summary>`dt wreck`: a derailment run to rest headless (T117), car by car: how far each went, how it lies, what snapped.</summary>
static class WreckCommands
{
    public static object Run(string content, string[] args)
    {
        double speed = Opt(args, "--speed", 22);
        int cars = (int)Opt(args, "--cars", 6);
        var tuning = DataFile.Load<WreckTuning>(Path.Combine(content, WreckTuning.File));
        var trainTuning = DataFile.Load<TrainTuning>(Path.Combine(content, TrainTuning.File));
        // Into a 300 m curve at speed: the engine goes off its outside, the rest after it.
        // --straight: on the level and straight (a Sleeper, the Switchman's points), nothing to throw it but the kick.
        var line = args.Contains("--straight") ? new RailLine(new LineDefinition("wreck", [new TrackSegment(5000)]))
            : new RailLine(new LineDefinition("wreck", [new TrackSegment(2000), new TrackSegment(600, 1 / 300.0), new TrackSegment(2000)]));
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

    static double Opt(string[] args, string name, double fallback)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length && double.TryParse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }
}
