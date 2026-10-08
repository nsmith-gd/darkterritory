using Ballast;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>Branches, switches and the rakes that run through them (T27, GDD §17, App. A.7).</summary>
public class SwitchTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly JunctionTuning J = Tuning.Route.Junctions;
    const double Toe = 1000;

    /// <summary>A straight main line with one dead line off to the right at 1 km, as the generator lays one.</summary>
    static RailLine Line(double branchGrade = 0) => new(new LineDefinition("switch", [new TrackSegment(4000)]),
        [new BranchDefinition(BranchKind.DeadLine, Toe, +1,
            [new TrackSegment(J.DivergeLength, -J.DivergeRadius), new TrackSegment(J.DivergeLength, J.DivergeRadius), new TrackSegment(500, 0, branchGrade)])]);

    static double Offset => 2 * J.DivergeRadius * (1 - Math.Cos(J.DivergeLength / J.DivergeRadius));

    /// <summary>How far right of the main line a point is (it runs along −Z from the origin).</summary>
    static double Right(Double3 p) => p.X;

    static TrainOnLine Train(RailLine line, int cars = 4, double front = Toe - 60) =>
        new(new TrainDynamics(Consist.Uniform(T, cars, 1)), line, front);

    static void Drive(TrainOnLine train, double seconds, double throttle, int reverser = 1, double brake = 0)
    {
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
            train.Step(SimConstants.TickSeconds, new TrainControls { Throttle = throttle, Brake = brake, Reverser = reverser });
    }

    /// <summary>Runs the train forward at a walking pace until its front is past <paramref name="distance"/>, then stops it.</summary>
    static void RunTo(TrainOnLine train, double distance, double speed = 4)
    {
        for (int i = 0; i < SimConstants.TickRate * 120 && train.Dynamics.Distance < distance; i++)
            train.Step(SimConstants.TickSeconds, new TrainControls { Throttle = train.Dynamics.Speed < speed ? 0.5 : 0, Reverser = 1 });
        for (int i = 0; i < SimConstants.TickRate * 30 && train.Dynamics.Speed > 0.01; i++)
            train.Step(SimConstants.TickSeconds, new TrainControls { Brake = 1, Reverser = 1 });
    }

    [Fact]
    public void ABranchLeavesAtItsPointsAndRunsAlongside()
    {
        var line = Line();
        var b = Assert.Single(line.Branches);
        Assert.Equal(Toe + 580, b.End);
        Assert.Equal(b.End, line.PathLength(b.Index));
        // One track up to the points.
        foreach (double d in new[] { 0.0, 500, Toe - 1 })
            Assert.Equal(line.Sample(d).Position, line.Sample(b.Index, d).Position);
        // Then out through the turnout and alongside, on the right.
        Assert.True(Right(line.Sample(b.Index, Toe + 40).Position) > 1);
        foreach (double d in new[] { Toe + 100, Toe + 300, Toe + 570 })
            Assert.Equal(Offset, Right(line.Sample(b.Index, d).Position), 2);
        Assert.InRange(Offset, 8, 9);
    }

    [Fact]
    public void ATrainGoesWhereTheSwitchIsSet()
    {
        var line = Line();
        var straight = Train(line);
        RunTo(straight, Toe + 250);
        Assert.Equal(RailLine.MainPath, straight.Dynamics.Path);
        Assert.All(straight.Cars, c => Assert.Equal(0, Right(c.Centre), 3));

        var diverging = Train(line);
        Assert.True(diverging.ThrowSwitch(0, true, J.PointsLength));
        RunTo(diverging, Toe + 250);
        Assert.Equal(0, diverging.Dynamics.Path);
        // Every car followed it through the turnout.
        Assert.All(diverging.Cars, c => Assert.Equal(Offset, Right(c.Centre), 1));
        Assert.False(diverging.OnMain);
    }

    [Fact]
    public void BackingOutComesBackOntoTheMainLine()
    {
        var train = Train(Line());
        train.ThrowSwitch(0, true, J.PointsLength);
        RunTo(train, Toe + 250);
        Assert.Equal(0, train.Dynamics.Path);
        // Reverse all the way back through the points (no choice to make backing out), set it for the main, go on.
        for (int i = 0; i < SimConstants.TickRate * 180 && train.Dynamics.Distance > Toe - 20; i++)
            train.Step(SimConstants.TickSeconds, new TrainControls { Throttle = train.Dynamics.Speed < 3 ? 0.5 : 0, Reverser = -1 });
        Drive(train, 20, 0, brake: 1);
        Assert.Equal(RailLine.MainPath, train.Dynamics.Path);
        Assert.True(train.ThrowSwitch(0, false, J.PointsLength));
        RunTo(train, Toe + 250);
        Assert.Equal(RailLine.MainPath, train.Dynamics.Path);
    }

    [Fact]
    public void ThePointsWontMoveUnderAWheel()
    {
        var train = Train(Line(), front: Toe + 20);
        Assert.True(train.PointsOccupied(0, J.PointsLength));
        Assert.False(train.ThrowSwitch(0, true, J.PointsLength));
        Assert.False(train.Diverging(0));
        RunTo(train, Toe + 150 + train.Dynamics.Consist.LengthMetres);
        Assert.False(train.PointsOccupied(0, J.PointsLength));
        Assert.True(train.ThrowSwitch(0, true, J.PointsLength));
    }

    [Fact]
    public void ADeadLineEndsAtABufferStop()
    {
        RakeContact Hit(double speed)
        {
            var train = Train(Line(), front: Toe + 575);
            train.ThrowSwitch(0, true, 0);
            train.Restore(train.Capture() with { Rakes = [train.Capture().Rakes[0] with { Path = 0, Velocity = speed }] });
            for (int i = 0; i < SimConstants.TickRate * 60 && !train.ContactsThisTick.Any(); i++)
                train.Step(SimConstants.TickSeconds, new TrainControls { Reverser = 1 });
            Assert.True(train.AtEndOfLine);
            Assert.Equal(0, train.Dynamics.Velocity);
            return Assert.Single(train.ContactsThisTick);
        }
        var gentle = Hit(0.8);
        Assert.Equal(-1, gentle.Rear);
        Assert.Equal(0, gentle.Damage);
        var hard = Hit(9);
        Assert.True(hard.Damage > 0.05, $"running into the stop at 9 m/s did {hard.Damage}");
    }

    [Fact]
    public void RakesOnTheBranchAndTheMainOnlyMeetAtThePoints()
    {
        // A rake parked down the dead line, clear of the points; the train runs by on the main line.
        var line = Line();
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 6, 1)), line, Toe - 40);
        Assert.True(train.Uncouple(3));
        var state = train.Capture();
        var parked = state.Rakes.Single(r => !r.Vehicles.Contains(0));
        train.Restore(state with { Rakes = [.. state.Rakes.Select(r => r.Vehicles.Contains(0) ? r : parked with { Path = 0, Distance = Toe + 300 })] });
        RunTo(train, Toe + 450);
        Assert.Equal(2, train.Rakes.Count);
        Assert.Equal(RailLine.MainPath, train.Dynamics.Path);
        Assert.Equal(Toe + 300, train.Rakes.Single(r => r != train.Dynamics).Distance, 3);

        // Standing over the points it's in the way: the train runs into its back there, and couples on.
        var fouling = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 6, 1)), line, Toe - 40);
        fouling.Uncouple(3);
        state = fouling.Capture();
        parked = state.Rakes.Single(r => !r.Vehicles.Contains(0));
        double parkedLength = fouling.Rakes.Single(r => !r.Consist.HasEngine).Consist.LengthMetres;
        var rear = parked with { Path = 0, Distance = Toe + 30 };
        Assert.True(Toe + 30 - parkedLength < Toe - J.PointsLength, "the parked rake's tail should be over the points");
        var engine = state.Rakes.Single(r => r.Vehicles.Contains(0)) with { Distance = Toe + 30 - parkedLength - T.Geometry.CouplingGap - 20 };
        fouling.Restore(state with { Rakes = [rear, engine] });
        RunTo(fouling, Toe + 100, speed: 1);
        Assert.Single(fouling.Rakes);
        Assert.Equal(0, fouling.Dynamics.Path);
    }

    [Fact]
    public void BackingOutIntoATrainAcrossThePointsIsASideswipe()
    {
        // The engine stands across the points on the main line; a rake backs out of the dead line into its side.
        var line = Line();
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 6, 1)), line, Toe);
        train.Uncouple(3);
        var state = train.Capture();
        var engine = state.Rakes.Single(r => r.Vehicles.Contains(0)) with { Distance = Toe + 40 };
        double length = train.Rakes.Single(r => !r.Consist.HasEngine).Consist.LengthMetres;
        var cars = state.Rakes.Single(r => !r.Vehicles.Contains(0)) with { Path = 0, Distance = Toe + length + 6, Velocity = -1.2, Handbrake = false };
        train.Restore(state with { Rakes = [engine, cars] });
        var contacts = new List<RakeContact>();
        for (int i = 0; i < SimConstants.TickRate * 60 && contacts.Count == 0; i++)
        {
            train.Step(SimConstants.TickSeconds, new TrainControls { Brake = 1, Reverser = 1 });
            contacts.AddRange(train.ContactsThisTick);
        }
        var hit = Assert.Single(contacts);
        Assert.False(hit.Coupled);
        Assert.Equal(2, train.Rakes.Count);
    }

    [Fact]
    public void AHandOnTheStandThrowsIt()
    {
        var train = Train(Line(), front: Toe - 300);
        var stands = new SwitchStands(J);
        var lever = stands.LeverAt(train.Line, 0);
        Assert.Equal(J.LeverOffset, Right(lever), 6);
        double hint = Toe;
        var s = PlayerMotor.SpawnOnGround(lever with { X = lever.X + 0.8 } - Double3.Up * 0.9, train.Line, hint, Tuning.Player);
        Assert.Equal(0, stands.InReach(s, train));
        var hold = new PlayerIntent { Buttons = PlayerButtons.Use };
        var throws = new List<SwitchThrow>();
        void Hold(double seconds, PlayerIntent intent)
        {
            for (int i = 0; i < seconds * SimConstants.TickRate; i++)
                if (stands.CrewAct(s, intent, 1, train) is { } t)
                    throws.Add(t);
        }
        Hold(J.ThrowSeconds - 0.1, hold);
        Assert.Empty(throws);
        // Held long enough it goes over, once; let go and hold again to throw it back.
        Hold(1.0, hold);
        Assert.Equal(new SwitchThrow(0, 1, true, true), Assert.Single(throws));
        Assert.True(train.Diverging(0));
        Hold(0.1, default);
        Hold(J.ThrowSeconds + 0.1, hold);
        Assert.False(train.Diverging(0));

        // Walked away, nothing.
        var away = s with { Position = s.Position + new Double3(4, 0, 0) };
        Assert.Null(stands.InReach(away, train));

        // With a wheel on the points it won't go, and says so.
        var over = Train(Line(), front: Toe + 10);
        stands.CrewAct(s, default, 1, over);
        throws.Clear();
        for (int i = 0; i < (J.ThrowSeconds + 0.1) * SimConstants.TickRate; i++)
            if (stands.CrewAct(s, hold, 1, over) is { } t)
                throws.Add(t);
        Assert.False(Assert.Single(throws).Moved);
        Assert.False(over.Diverging(0));
    }

    [Fact]
    public void AtAStandUseIsTheLeversNotTheHands()
    {
        // Queue #94 (note 357): holding Use at a stand threw it and put down the lamp you'd carried out to it on the press,
        // or picked up whatever lay by the stand.
        var world = new World(Train(Line(), front: Toe - 300));
        world.EnableSwitches(J);
        world.EnableBodies();
        var lever = world.Switches!.LeverAt(world.Train.Line, 0);
        var s = PlayerMotor.SpawnOnGround(lever with { X = lever.X + 0.8 } - Double3.Up * 0.9, world.Train.Line, Toe, Tuning.Player);
        var feet = PlayerMotor.WorldPosition(s, world.Train);
        void Hold(double seconds, PlayerIntent intent)
        {
            for (int i = 0; i < seconds * SimConstants.TickRate; i++)
                world.CrewAct(ref s, intent, 1);
        }
        var use = new PlayerIntent { Buttons = PlayerButtons.Use };

        // A lamp in hand stays in hand, and the switch goes over.
        var lamp = world.Bodies.SpawnItem(feet, Toe, BodyKind.Lamp);
        lamp.Carrier = 1;
        Hold(J.ThrowSeconds + 0.1, use);
        Assert.True(world.Train.Diverging(0));
        Assert.Equal(1, lamp.Carrier);
        Hold(0.1, default);

        // Empty-handed, a crate lying by the stand stays down, and the switch goes back.
        lamp.Carrier = -1;
        lamp.Pbd.Particles[0].Position = feet + new Double3(30, 0, 0);
        var crate = world.Bodies.SpawnCargo(feet + new Double3(0, 0, -0.6), Toe);
        Assert.Same(crate, world.Bodies.InReach(s, world.Train));
        Hold(J.ThrowSeconds + 0.1, use);
        Assert.False(world.Train.Diverging(0));
        Assert.Equal(-1, crate.Carrier);
        Hold(0.1, default);

        // Throw still lets go of what's carried there.
        crate.Carrier = 1;
        Hold(2 * SimConstants.TickSeconds, new PlayerIntent { Buttons = PlayerButtons.Throw });
        Assert.Equal(-1, crate.Carrier);

        // And away from the stand, Use picks it up as it always did.
        s = s with { Position = s.Position + new Double3(4, 0, 0) };
        Assert.Null(world.Switches.InReach(s, world.Train));
        var far = world.Bodies.SpawnCargo(PlayerMotor.WorldPosition(s, world.Train) + new Double3(0, 0, -0.6), Toe);
        Assert.Same(far, world.Bodies.InReach(s, world.Train));
        Hold(2 * SimConstants.TickSeconds, use);
        Assert.Equal(1, far.Carrier);
    }

    [Fact]
    public void ClientsSeeTheSwitchAndWhichWayEachRakeWent()
    {
        var host = new World(Train(Line()));
        host.Train.ThrowSwitch(0, true, J.PointsLength);
        RunTo(host.Train, Toe + 250);
        var client = new World(Train(Line()));
        var controls = default(TrainControls);
        WorldRecords.Apply(WorldRecords.Capture(host, default, []), client, ref controls, []);
        Assert.True(client.Train.Diverging(0));
        Assert.Equal(0, client.Train.Dynamics.Path);
        Assert.Equal(host.Train.Cars[0].Centre.X, client.Train.Cars[0].Centre.X, 3);
    }

    [Fact]
    public void TheGroundBesideABranchIsAtItsHeight()
    {
        // A dead line climbing away from a level main: someone standing on it is up there with it.
        var line = Line(branchGrade: 4);
        var onBranch = line.Sample(0, Toe + 400).Position;
        double hint = Toe + 380;
        double ground = PlayerMotor.GroundAt(onBranch + new Double3(1.0, 0, 0), line, ref hint);
        Assert.Equal(onBranch.Y, ground, 2);
        Assert.True(ground > 10);
        // On the main line's side of the gap, the main line's ground.
        hint = Toe + 400;
        Assert.Equal(0, PlayerMotor.GroundAt(line.Sample(Toe + 400).Position + new Double3(-1.0, 0, 0), line, ref hint), 6);
    }

    [Fact]
    public void EveryJunctionHasADeadLineRunningAlongside()
    {
        int seen = 0;
        foreach (var tier in Enum.GetValues<RouteTier>())
            for (ulong seed = 1; seed <= 6; seed++)
            {
                var route = RouteGenerator.Generate(Tuning.Route, tier, seed);
                var junctions = route.Of(FeatureKind.Junction).ToList();
                var tt = Tuning.Route.Tiers[tier];
                Assert.InRange(junctions.Count, 0, tt.Junctions[1]);
                Assert.Equal(junctions.Select(j => j.Start), route.Branches.Where(b => b.Kind == BranchKind.DeadLine).Select(b => b.Toe));
                var line = route.Build();
                foreach (var b in line.Branches.Where(b => b.Kind == BranchKind.DeadLine))
                {
                    seen++;
                    Assert.InRange(b.Local.Length, J.DeadLineLength[0] - 1, J.DeadLineLength[1] + 1);
                    Assert.Equal(junctions.Single(j => j.Start == b.Toe).Side, b.Side);
                    // Nothing else within a dead line's reach of it: no tunnel or bridge to run into alongside.
                    Assert.DoesNotContain(route.Features, f => f.Kind is FeatureKind.Tunnel or FeatureKind.Bridge or FeatureKind.Facility
                        && f.Start < b.End + 150 && f.End > b.Toe - 150);
                    // Past the turnout it keeps its distance and its height beside the main line.
                    for (double s = 100; s < b.Local.Length; s += 25)
                    {
                        var p = b.Local.Sample(s).Position;
                        double h = b.Toe + s;
                        var (path, along) = line.Nearest(p + Double3.Up, ref h);
                        Assert.Equal(b.Index, path);
                        var m = line.Sample(h);
                        double beside = Math.Sqrt(Math.Pow(p.X - m.Position.X, 2) + Math.Pow(p.Z - m.Position.Z, 2));
                        Assert.True(Math.Abs(Offset - beside) < 0.1, $"{beside:0.00} m beside the main line, not {Offset:0.00}");
                        Assert.True(Math.Abs(m.Position.Y - p.Y) < 0.05, $"{p.Y - m.Position.Y:0.000} m off the main line's height");
                        var right = Double3.Cross(m.Tangent, Double3.Up).Normalized;
                        Assert.Equal(b.Side, Math.Sign(Double3.Dot(p - m.Position, right)));
                    }
                }
            }
        Assert.True(seen > 40, $"only {seen} junctions in 24 routes");
    }

    [Fact]
    public void AStandingTrainWithACarCutLooseBehindIsStillStandingAtTheSwitch()
    {
        // T106: frontier:7 stood at a dead line's switch till dawn. A car had been cut loose miles back and run on buffered up
        // behind, so the train was two rakes, and the shunter waited for it to stand there as one.
        var train = Train(Line(), front: Toe - Tuning.Route.Junctions.PointsLength - 2);
        var world = new World(train);
        world.SetSwitch(0, true);
        Assert.True(train.Uncouple(train.Dynamics.Consist.Vehicles[^2].Id));
        Assert.Equal(2, train.Rakes.Count);
        var plan = Bots.SwitchPlan.Ahead(world);
        Assert.NotNull(plan);
        Assert.True(plan.StandingAt(train));
        // But not with the cut-off car rolling.
        train.Rakes[1].Velocity = -0.5;
        Assert.False(plan.StandingAt(train));
    }
}
