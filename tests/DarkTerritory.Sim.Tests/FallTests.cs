using Ballast;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// T128 (build 1121: "far too easy to fall off the train"; ARCHITECTURE §8 note 273). A careless roof walker, walked the
/// way people walk on a keyboard and mouse (a wandering aim, a glance to the side every few seconds with the key still
/// held, a correction only once they see the edge coming, half the cars at a run, the gaps jumped), up and down a moving
/// train on a line of bends in a normal night's wind. Falls come from real causes, not from ordinary walking.
/// </summary>
public class FallTests(ITestOutputHelper output)
{
    static readonly PlayerTuning P = Tuning.Player;

    sealed class Windy(double wind) : ITrackConditions
    {
        public double Ground(Double3 world) => 0;
        public double Adhesion(int path, double distance) => 1;
        public double Drag(int path, double distance, double speed) => 0;
        public double Wind(int path, double distance) => wind;
    }

    /// <summary>Bends both ways (R 500 m, under any board's throw at 16 m/s) between straights.</summary>
    static LineDefinition Bendy()
    {
        var segments = new List<TrackSegment>();
        for (int i = 0; i < 40; i++)
        {
            segments.Add(new TrackSegment(300));
            segments.Add(new TrackSegment(250, Radius: i % 2 == 0 ? 500 : -500));
        }
        return new LineDefinition("bendy", segments);
    }

    public sealed record Outing(int Falls, int Deaths, int Gaps, double Seconds, int Meant, int SteppedOff, int FromJumps);

    /// <summary>
    /// Walks the careless walker for <paramref name="seconds"/>; every fall puts them back on a roof. <paramref name="stepOff"/>:
    /// every so often they mean to walk off a roof's side (the deliberate step), and it's counted whether they could.
    /// </summary>
    public static Outing Careless(ulong seed, double speed, double wind, double seconds, PlayerTuning? tuning = null, bool stepOff = false)
    {
        var p = tuning ?? P;
        var line = new RailLine(Bendy()) { Conditions = new Windy(wind) };
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), line, 3_000);
        train.Dynamics.Velocity = speed;
        var world = new World(train);
        var rng = new Pcg32(seed, 0xFA11);
        int last = train.Dynamics.Consist.Vehicles[^1].Id;
        var s = PlayerMotor.SpawnOnRoof(train, 2, 0, p);
        int dir = -1, falls = 0, deaths = 0, gaps = 0, meant = 0, steppedOff = 0, fromJumps = 0;
        bool jumped = false;
        double wander = 0, glance = 0, glanceLeft = 0, nextGlance = rng.Range(5, 10), nextStep = rng.Range(20, 40), stepping = 0;
        bool run = false;
        int car = s.Parent;
        var lag = new Queue<double>();
        double dt = SimConstants.TickSeconds;
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
        {
            double t = i * dt;
            var intent = new PlayerIntent { MoveZ = 1 };
            if (s.Parent > 0)
            {
                var shape = train.Frames[s.Parent].Shape;
                if (s.Parent != car)
                {
                    car = s.Parent;
                    run = rng.NextDouble() < 0.5;
                    gaps++;
                }
                // Turn round at the ends of the cars (not onto the engine).
                double toEnd = dir < 0 ? s.Position.Z + shape.HalfLength : shape.HalfLength - s.Position.Z;
                bool beyond = dir < 0 ? s.Parent > 1 : s.Parent != last;
                if (!beyond && toEnd < 1.5)
                    dir = -dir;
                // The aim: along the car, wandering a few degrees, a glance aside now and then, and a correction from where
                // they were half a second ago once that was near the edge.
                wander = Math.Clamp(wander + rng.Range(-1, 1) * 0.02, -0.07, 0.07);
                // Not on the run up to a gap: you look where you're going to jump.
                if (t >= nextGlance && !(beyond && toEnd < 4))
                {
                    glance = rng.Range(25, 50) * Math.PI / 180 * (rng.NextDouble() < 0.5 ? -1 : 1);
                    glanceLeft = 0.8;
                    nextGlance = t + rng.Range(5, 10);
                }
                glanceLeft = beyond && toEnd < 2 ? 0 : glanceLeft - dt;
                lag.Enqueue(s.Position.X);
                double seen = lag.Count > 15 ? lag.Dequeue() : lag.Peek();
                double correct = Math.Abs(seen) > 0.7 ? Math.Sign(seen) * 10 * Math.PI / 180 * -dir : 0;
                double yaw = (dir < 0 ? 0 : Math.PI) + wander + (glanceLeft > 0 ? glance : 0) + correct;
                // Meaning to step off: straight out to the side, holding it.
                if (stepOff && t >= nextStep)
                {
                    stepping = 3;
                    nextStep = t + rng.Range(20, 40);
                    meant++;
                }
                if (stepping > 0)
                {
                    stepping -= dt;
                    yaw = -Math.PI / 2;
                }
                intent.LookYaw = (float)(yaw - s.Yaw);
                if (run)
                    intent.Buttons |= PlayerButtons.Run;
                if (beyond && toEnd < 0.9 && stepping <= 0)
                    intent.Buttons |= PlayerButtons.Jump | PlayerButtons.Run;
            }
            world.BeginTick();
            world.Step(new TrainControls { Reverser = 1 });
            train.Dynamics.Velocity = speed;
            bool wasUp = s.Surface != Surface.Air;
            PlayerMotor.Step(ref s, intent, train, p, Tuning.Train, dt, applyLook: true);
            if (wasUp && s.Surface == Surface.Air)
                jumped = intent.Has(PlayerButtons.Jump);
            if (!s.Alive || s.Parent == PlayerState.World && s.Surface == Surface.Ground)
            {
                falls++;
                if (!s.Alive)
                    deaths++;
                if (stepping > 0)
                    steppedOff++;
                if (jumped)
                    fromJumps++;
                jumped = false;
                stepping = 0;
                s = PlayerMotor.SpawnOnRoof(train, 1 + (int)(rng.NextDouble() * 5), 0, p);
                car = s.Parent;
                lag.Clear();
            }
        }
        return new Outing(falls, deaths, gaps, seconds, meant, steppedOff, fromJumps);
    }

    /// <summary>The old roofs: no lip (what build 1121 played).</summary>
    static readonly PlayerTuning NoLip = P with { Edge = P.Edge with { Lip = 0 } };

    [Fact]
    public void OrdinaryWalkingAlongTheRoofsOfAMovingTrainDoesntTakeYouOff()
    {
        int falls = 0, before = 0, gaps = 0;
        foreach (ulong seed in new ulong[] { 1, 2, 3, 4 })
        {
            var w = Careless(seed, 16, 1, 300);
            var old = Careless(seed, 16, 1, 300, NoLip);
            output.WriteLine($"seed {seed}: {w.Falls} falls ({w.Deaths} deaths, {w.FromJumps} off gap jumps) in {w.Seconds} s, {w.Gaps} cars crossed;"
                + $" without the lip {old.Falls} ({old.Deaths} deaths, {old.FromJumps} off gap jumps)");
            falls += w.Falls;
            before += old.Falls;
            gaps += w.Gaps;
        }
        output.WriteLine($"all: {falls} falls in 20 min ({before} without the lip), {gaps} cars crossed");
        Assert.True(gaps > 120, $"they walked the train ({gaps} cars)");
        Assert.True(before >= 30, $"the careless walker went off the old roofs {before} times: it's no measure if it doesn't");
        Assert.True(falls <= 2, $"{falls} falls in 20 minutes of ordinary roof walking");
    }

    [Fact]
    public void YouCanStillStepOffASideOnPurpose()
    {
        var w = Careless(5, 10, 0, 240, stepOff: true);
        output.WriteLine($"meant {w.Meant}, went over {w.SteppedOff}; {w.Falls} falls");
        Assert.True(w.Meant >= 4, $"meant to {w.Meant} times");
        Assert.Equal(w.Meant, w.SteppedOff);
    }

    static TrainOnLine Train(double speed, double wind = 0)
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(40_000)])) { Conditions = new Windy(wind) };
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 5, 1)), line, 3_000);
        train.Dynamics.Velocity = speed;
        return train;
    }

    static PlayerState Walk(TrainOnLine train, PlayerState s, PlayerIntent intent, double seconds, PlayerTuning? p = null)
    {
        double speed = train.Dynamics.Velocity;
        for (int i = 0; i < Math.Max(1, seconds * SimConstants.TickRate) && s.Alive && s.Parent != PlayerState.World; i++)
        {
            train.Step(SimConstants.TickSeconds, new TrainControls { Reverser = 1 });
            train.Dynamics.Velocity = speed;
            PlayerMotor.Step(ref s, intent, train, p ?? P, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
        }
        return s;
    }

    [Fact]
    public void AGlanceAsideWalksYouToTheLipAndAlongIt()
    {
        var train = Train(16);
        // Facing 40° off the car's length, walking on: along the edge, not over it.
        var s = PlayerMotor.SpawnOnRoof(train, 2, 4, P) with { Yaw = -40 * Math.PI / 180 };
        s = Walk(train, s, new PlayerIntent { MoveZ = 1, Buttons = PlayerButtons.Run }, 2);
        Assert.Equal(2, s.Parent);
        Assert.Equal(Surface.Roof, s.Surface);
        Assert.InRange(s.Position.X, 1.0, train.Frames[2].Shape.HalfWidth - P.Edge.Lip + 0.01);
        Assert.True(s.Position.Z < 0, $"it slid along the edge to {s.Position.Z:0.0}");
        // Without the lip that walk is over the side.
        var old = Walk(train, PlayerMotor.SpawnOnRoof(train, 2, 4, P) with { Yaw = -40 * Math.PI / 180 },
            new PlayerIntent { MoveZ = 1, Buttons = PlayerButtons.Run }, 2, NoLip);
        Assert.Equal(PlayerState.World, old.Parent);
    }

    [Fact]
    public void TheWindBringsYouToTheLipNotOverIt()
    {
        // The worst wind there is on a roof (exposed track at full speed), stood near the edge for half a minute of gusts.
        var train = Train(22, wind: 1.5);
        var s = Walk(train, PlayerMotor.SpawnOnRoof(train, 2, 0, P, localX: 1.0), default, 30);
        Assert.Equal(2, s.Parent);
        Assert.True(Math.Abs(s.Position.X) <= train.Frames[2].Shape.HalfWidth - P.Edge.Lip + 0.01, $"at {s.Position.X:0.00}");
        // windOverLip is the old wind: it takes you.
        var gusty = P with { Edge = P.Edge with { WindOverLip = true } };
        var t2 = Train(22, wind: 1.5);
        var g = Walk(t2, PlayerMotor.SpawnOnRoof(t2, 2, 0, gusty, localX: 1.0), default, 30, gusty);
        Assert.Equal(PlayerState.World, g.Parent);
    }

    [Fact]
    public void ARoofsEndHoldsYouUnlessThereIsTrainBelowIt()
    {
        var train = Train(12);
        var geometry = Tuning.Train.Geometry;
        double end = train.Frames[2].Shape.HalfLength;
        // Off the plate's line, walking at the back end: held at the lip.
        double side = Math.Abs(geometry.PlateX + 0.8) < Math.Abs(geometry.PlateX - 0.8) ? 0.8 : -0.8;
        var s = Walk(train, PlayerMotor.SpawnOnRoof(train, 2, end - 2, P, localX: geometry.PlateX + side) with { Yaw = Math.PI }, new PlayerIntent { MoveZ = 1 }, 3);
        Assert.Equal(2, s.Parent);
        Assert.Equal(Surface.Roof, s.Surface);
        Assert.InRange(s.Position.Z, end - P.Edge.Lip - 0.1, end - P.Edge.Lip + 0.01);
        // Squarely over the plate: down onto it, as the bots go in out of the cold.
        var d = PlayerMotor.SpawnOnRoof(train, 2, end - 2, P, localX: geometry.PlateX) with { Yaw = Math.PI };
        for (int i = 0; i < 3 * SimConstants.TickRate && d.Surface is not (Surface.Coupler or Surface.Ground); i++)
        {
            train.Step(SimConstants.TickSeconds, new TrainControls { Reverser = 1 });
            train.Dynamics.Velocity = 12;
            PlayerMotor.Step(ref d, new PlayerIntent { MoveZ = d.Surface == Surface.Air ? 0 : 1 }, train, P, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
        }
        Assert.Equal(Surface.Coupler, d.Surface);
    }
}
