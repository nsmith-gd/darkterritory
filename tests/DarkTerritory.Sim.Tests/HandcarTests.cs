using Ballast;
using Ballast.Net;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The handcar (GDD §24.1; the director, 14 Oct 2026, App. F.6; ARCHITECTURE §8 note 729): a pump trolley hung on the back of
/// the guard van, lifted onto the rails at a stand and pumped down the line in time with its handle, through the points as
/// they're set, its riders in its frame, stopped by its brake.
/// </summary>
public class HandcarTests
{
    static readonly TrainTuning T = Tuning.Train with { Handcar = Tuning.Train.Handcar with { Always = true } };
    static readonly HandcarTuning H = T.Handcar;
    static readonly PlayerTuning P = Tuning.Player;
    const double Dt = SimConstants.TickSeconds;

    /// <summary>A night's crew stepped as the host steps them (HostSession.Step): their acts, the world, then their feet.</summary>
    sealed class Crew(World world)
    {
        public readonly World World = world;
        public readonly Dictionary<int, PlayerState> Players = [];
        public double? Speed;
        public TrainOnLine Train => World.Train;
        public Handcar Car => Train.Handcar!;
        public TrainDynamics Rake => Train.HandcarRake!;

        public void Run(double seconds, Func<int, PlayerIntent>? intent = null)
        {
            World.Movement = P;
            int ticks = (int)Math.Round(seconds * SimConstants.TickRate);
            for (int i = 0; i < ticks; i++)
            {
                if (Speed is { } v)
                    Train.Dynamics.Velocity = v;
                var ids = Players.Keys.Order().ToList();
                World.Mates = [.. ids.Select(id => (id, Players[id]))];
                var intents = ids.ToDictionary(id => id, id => intent?.Invoke(id) ?? default);
                World.BeginTick();
                foreach (var id in ids)
                {
                    var s = Players[id];
                    World.CrewAct(ref s, intents[id], id);
                    Players[id] = s;
                }
                World.Step(new TrainControls { Reverser = 1, Brake = Speed is 0 ? 1 : 0 });
                foreach (var id in ids)
                {
                    var s = Players[id];
                    PlayerMotor.Step(ref s, intents[id], Train, P, T, Dt, applyLook: false, id);
                    Players[id] = s;
                }
                World.StepBodies([.. ids.Select(id => (id, Players[id]))]);
            }
        }

        public Double3 At(int id) => PlayerMotor.WorldPosition(Players[id], Train);

        /// <summary>A crewmate stood on the van's rear platform.</summary>
        public void OnPlatform(int id, double x)
        {
            int van = Train.HandcarHost;
            var f = Train.Frames[van];
            var p = f.Shape.Platform!.Value;
            Players[id] = PlayerMotor.StandUp(Train, van, new Double3(x, p.Max.Y, (p.Min.Z + p.Max.Z) / 2), Train.Cars[van].FrontDistance, P);
        }

        /// <summary>A crewmate stood on the handcar's deck at an end of its handle.</summary>
        public void AtEnd(int id, int end) =>
            Players[id] = PlayerMotor.StandUp(Train, Car.Id, Handcar.Place(H, end), Train.Cars[Car.Id].FrontDistance, P);

        /// <summary>Its handle taken by everyone at it (the press of Use, as at the crane's stand).</summary>
        public void TakeHandle() => Run(Dt, _ => new PlayerIntent { Actions = PlayerActions.Seat });
    }

    static Crew Night(double speed = 0, RailLine? line = null, double at = 2_000)
    {
        line ??= new RailLine(new LineDefinition("t", [new TrackSegment(40_000)]));
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 4, 1)), line, at);
        train.Dynamics.Velocity = speed;
        var world = new World(train, Tuning.Combat);
        // The host's: the strokes, the brake and the lifts are its to gather (World.Authority).
        world.EnableBodies();
        return new Crew(world) { Speed = speed };
    }

    /// <summary>A night with the handcar already on the rails behind the van and the train standing.</summary>
    static Crew OnTheRails(RailLine? line = null, double at = 2_000)
    {
        var n = Night(0, line, at);
        Assert.True(n.Train.SetHandcarOnRails());
        return n;
    }

    static readonly PlayerIntent Lift = new() { Buttons = PlayerButtons.Use };

    /// <summary>A pumper's stroke in time with the handle to drive the car <paramref name="way"/>, from the end they're at.</summary>
    static PlayerIntent InTime(Crew n, int id, int way, double lag = 0) =>
        new() { MoveZ = (float)Handcar.InTime(n.Car.Phase + lag, Handcar.EndAt(n.Players[id].Position.Z), way) };

    // ---- What it is, and getting it on the rails.

    [Fact]
    public void ANightWithoutItHasNoHandcarAndTheUpgradeOrTheFlagGivesOne()
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(5_000)]));
        var plain = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 1)), line, 2_000);
        Assert.Null(plain.Handcar);
        Assert.Equal(plain.OwnVehicles, plain.Vehicles.Count);
        var bought = Tuning.Train with { Composition = Tuning.Train.Composition with { Handcar = true } };
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(bought, 4, 1)), line, 2_000);
        Assert.NotNull(train.Handcar);
        // Past the train's own vehicles, never one of its cars: not counted among its rakes, nor the kit's car.
        Assert.Equal(train.OwnVehicles, train.Handcar!.Id);
        Assert.Equal(VehicleKind.Handcar, train.Vehicles[train.Handcar.Id].Kind);
        Assert.Equal(1, train.TrainRakes);
        Assert.True(train.Handcar.Carried);
        Assert.NotEqual(train.Handcar.Id, train.KitCar);
        // Hung on the guard van's back, behind its rear platform, going where the van goes.
        var van = train.Frames[train.HandcarHost];
        Assert.Equal(VehicleKind.Guard, train.Vehicles[train.HandcarHost].Kind);
        var hung = van.ToLocal(train.Frames[train.Handcar.Id].Origin);
        Assert.True(hung.Z > van.Shape.Platform!.Value.Max.Z, $"hung at {hung.Z:0.00} m");
        Assert.Empty(train.Frames[train.Handcar.Id].Shape.Solids);
    }

    [Fact]
    public void TwoLiftItOntoTheRailsAtAStandAndNotAtSpeed()
    {
        var n = Night(speed: 10);
        n.OnPlatform(1, 0.9);
        n.OnPlatform(2, -0.9);
        // At speed: however long they heave, it stays on the van.
        n.Run(8, _ => Lift);
        Assert.True(n.Car.Carried);
        Assert.Equal(0, n.Car.Lift);
        // Standing: down in liftSeconds, behind the van on the same track.
        n.Speed = 0;
        n.Train.Dynamics.Velocity = 0;
        n.Run(H.LiftSeconds * 0.8, _ => Lift);
        Assert.True(n.Car.Carried);
        Assert.True(n.Car.Lift > 0.5);
        n.Run(H.LiftSeconds * 0.3, _ => Lift);
        Assert.False(n.Car.Carried);
        var van = n.Train.RakeOf(n.Train.HandcarHost);
        Assert.Equal(van.Path, n.Rake.Path);
        Assert.InRange(van.RearDistance - n.Rake.Distance, T.Geometry.PlatformDepth + H.SetBehind - 0.01, T.Geometry.PlatformDepth + H.SetBehind + 0.01);
        Assert.Equal(H.Length, n.Train.Cars[n.Car.Id].Length);
        // On the rails its deck is something to stand on.
        Assert.NotEmpty(n.Train.Frames[n.Car.Id].Shape.Solids);
    }

    [Fact]
    public void OneAloneLiftsItSlowly()
    {
        var n = Night();
        n.OnPlatform(1, 0.9);
        n.Run(H.LiftSeconds * 1.5, _ => Lift);
        Assert.True(n.Car.Carried);
        n.Run(H.LiftSeconds * H.SoloLiftScale - H.LiftSeconds * 1.5 + 0.3, _ => Lift);
        Assert.False(n.Car.Carried);
    }

    [Fact]
    public void ItGoesBackOnTheVanFromBesideItAndNotWithSomeoneAboard()
    {
        var n = OnTheRails();
        n.AtEnd(1, 1);
        n.OnPlatform(2, 0.9);
        n.Run(H.LiftSeconds * 1.5, id => id == 2 ? Lift : default);
        Assert.False(n.Car.Carried); // someone's stood on it
        n.OnPlatform(1, -0.9);
        n.Run(H.LiftSeconds + 0.3, _ => Lift);
        Assert.True(n.Car.Carried);
        Assert.Equal(1, n.Train.TrainRakes);
    }

    // ---- Pumping.

    /// <summary>The speed after <paramref name="seconds"/> of pumping down the line, each pumper with their own stroke.</summary>
    static double Pumped(int[] ends, Func<Crew, int, PlayerIntent> stroke, double seconds = 40)
    {
        var n = OnTheRails();
        for (int i = 0; i < ends.Length; i++)
            n.AtEnd(i + 1, ends[i]);
        n.TakeHandle();
        Assert.All(n.Players.Values, s => Assert.True(s.Has(PlayerFlags.Pumping)));
        n.Run(seconds, id => stroke(n, id));
        Assert.All(n.Players.Values, s => Assert.Equal(n.Car.Id, s.Parent));
        return -n.Rake.Velocity;
    }

    [Fact]
    public void TwoInTimeGoFasterThanOneAndTwoOutOfTimeSlower()
    {
        double two = Pumped([-1, 1], (n, id) => InTime(n, id, -1));
        double one = Pumped([1], (n, id) => InTime(n, id, -1));
        // The back end's pumper a quarter of a swing late: half of each stroke fights the car.
        double ragged = Pumped([-1, 1], (n, id) => InTime(n, id, -1, id == 2 ? -Math.PI / 2 : 0));
        // Holding W gets nowhere: half of every swing it's against the handle.
        double held = Pumped([-1, 1], (_, _) => new PlayerIntent { MoveZ = 1 });
        Assert.InRange(two, 7.3, 8.7);
        Assert.InRange(one, 5.0, 6.8);
        Assert.True(ragged < two - 1, $"ragged {ragged:0.00} m/s, two in time {two:0.00}");
        Assert.True(held < 1.5, $"held {held:0.00} m/s");
    }

    [Fact]
    public void TheyStandStillAtTheHandleAndJumpLetsGo()
    {
        var n = OnTheRails();
        n.AtEnd(1, -1);
        n.TakeHandle();
        var at = n.Players[1].Position;
        n.Run(3, id => InTime(n, id, -1) with { MoveX = 1 });
        Assert.Equal(at.X, n.Players[1].Position.X, 6);
        Assert.Equal(at.Z, n.Players[1].Position.Z, 6);
        n.Run(Dt, _ => new PlayerIntent { Buttons = PlayerButtons.Jump });
        Assert.False(n.Players[1].Has(PlayerFlags.Pumping));
    }

    [Fact]
    public void UphillIsHardAndDownhillRunsAway()
    {
        // A 2% bank, climbing up the line: down it from the train is downhill.
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(40_000, 0, 2)]));
        Crew Pumping(int way, bool pump)
        {
            var n = OnTheRails(line, 20_000);
            // Up the line is into the train's back: start that run well behind it.
            if (way > 0)
                n.Rake.Distance = n.Rake.PreviousDistance = n.Rake.Distance - 500;
            n.AtEnd(1, -1);
            n.AtEnd(2, 1);
            n.TakeHandle();
            n.Run(30, id => pump ? InTime(n, id, way) : default);
            return n;
        }
        double climb = Pumping(1, pump: true).Rake.Velocity;
        double rolled = -Pumping(-1, pump: false).Rake.Velocity;
        double ran = -Pumping(-1, pump: true).Rake.Velocity;
        Assert.InRange(climb, 5.0, 7.3);
        Assert.True(rolled > 3, $"rolled away to {rolled:0.00} m/s with nobody pumping");
        Assert.True(ran > 9, $"pumped down the bank to {ran:0.00} m/s");
    }

    [Fact]
    public void TheBrakeStopsIt()
    {
        var n = OnTheRails();
        n.AtEnd(1, -1);
        n.AtEnd(2, 1);
        n.TakeHandle();
        n.Run(30, id => InTime(n, id, -1));
        Assert.True(n.Rake.Speed > 7);
        n.Run(4, _ => new PlayerIntent { Buttons = PlayerButtons.Brake });
        Assert.Equal(0, n.Rake.Speed);
        Assert.Equal(1, n.Car.Brake);
        n.Run(2);
        Assert.Equal(0, n.Rake.Speed);
    }

    // ---- On the line.

    [Fact]
    public void ItFollowsThePointsAsTheyreSet()
    {
        var main = new LineDefinition("t", [new TrackSegment(6_000)]);
        var branch = new BranchDefinition(BranchKind.DeadLine, 1_800, 1, [new TrackSegment(60, 300), new TrackSegment(60, -300), new TrackSegment(400)]);
        var line = new RailLine(main, [branch]);
        foreach (bool diverge in new[] { false, true })
        {
            var n = OnTheRails(line, 2_100);
            // Backed down past the points (no choice going that way), then run up through them, someone aboard (left alone,
            // its brake's latched on: handcar.parked).
            n.Rake.Distance = n.Rake.PreviousDistance = 1_780;
            n.Train.RefreshFrames();
            n.AtEnd(1, 1);
            Assert.True(n.Train.ThrowSwitch(0, diverge, 10) || !diverge);
            n.Rake.Velocity = 5;
            n.Run(6);
            Assert.Equal(diverge ? 0 : RailLine.MainPath, n.Rake.Path);
            Assert.True(n.Rake.Distance > 1_800);
            if (diverge)
            {
                // Off on the branch's own track, beside where the main line runs.
                var on = n.Train.Frames[n.Car.Id].Origin;
                var mainThere = line.Sample(n.Rake.Distance).Position;
                Assert.True(((on - mainThere) with { Y = 0 }).Length > 0.5);
            }
        }
    }

    [Fact]
    public void LeftStandingOnABankItStaysPut()
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(40_000, 0, 2)]));
        var n = OnTheRails(line, 20_000);
        double at = n.Rake.Distance;
        n.Run(20);
        Assert.Equal(at, n.Rake.Distance, 6);
        Assert.Equal(1, n.Car.Brake);
    }

    [Fact]
    public void ItCouplesToNothingAndCantPassTheTrain()
    {
        var n = OnTheRails();
        double integrity = n.Train.Vehicles[n.Train.HandcarHost].Integrity;
        n.Rake.Velocity = 8; // up the line, into the van's back
        n.Run(4);
        Assert.True(n.Rake.Distance <= n.Train.RakeOf(n.Train.HandcarHost).RearDistance - T.Geometry.CouplingGap + 1e-6);
        Assert.Equal(2, n.Train.Rakes.Count);
        Assert.Equal(integrity, n.Train.Vehicles[n.Train.HandcarHost].Integrity);
        Assert.Equal(1, n.Train.TrainRakes);
    }

    [Fact]
    public void ItsRidersRideInItsFrame()
    {
        var n = OnTheRails();
        n.AtEnd(1, -1);
        n.AtEnd(2, 1);
        n.Players[3] = PlayerMotor.StandUp(n.Train, n.Car.Id, new Double3(0.5, H.DeckHeight, 0.2), n.Train.Cars[n.Car.Id].FrontDistance, P);
        n.TakeHandle();
        var local = n.Players[3].Position;
        var before = n.At(3);
        n.Run(20, id => id == 3 ? default : InTime(n, id, -1));
        Assert.Equal(n.Car.Id, n.Players[3].Parent);
        Assert.Equal(local.X, n.Players[3].Position.X, 6);
        Assert.Equal(local.Z, n.Players[3].Position.Z, 6);
        Assert.True((n.At(3) - before).Length > 50, $"{(n.At(3) - before).Length:0} m");
        // Out on the open line, not aboard the train.
        Assert.True(n.Train.OnHandcar(n.Players[3].Parent));
        Assert.False(Enemies.CrewSense.OnGround(n.Players[3]));
        Assert.True(Enemies.CrewSense.Afoot(n.Players[3], n.Train));
    }

    // ---- Bots.

    [Fact]
    public void ABotComesToTheOtherEndOfTheHandleAndPumpsAlong()
    {
        var n = OnTheRails();
        n.AtEnd(1, -1);
        // A bot on the ballast beside the train, a few metres off.
        var car = n.Train.Frames[n.Car.Id];
        n.Players[2] = PlayerMotor.SpawnOnGround(car.ToWorld(new Double3(3.5, 0, -4)), n.Train.Line, n.Train.Cars[n.Car.Id].FrontDistance, P);
        PlayerIntent Bot() => Bots.Heed.Handcar(default, n.Players[2], n.World, 2, [(1, n.Players[1])], null);
        n.TakeHandle();
        Assert.True(n.Players[1].Has(PlayerFlags.Pumping));
        n.Run(12, id => id == 2 ? Bot() : default);
        Assert.Equal(n.Car.Id, n.Players[2].Parent);
        Assert.True(n.Players[2].Has(PlayerFlags.Pumping));
        Assert.Equal(1, Handcar.EndAt(n.Players[2].Position.Z));
        // Its mate pumps the car off down the line; the bot keeps time with them.
        n.Run(40, id => id == 2 ? Bot() : InTime(n, id, -1));
        Assert.True(-n.Rake.Velocity > 7.3, $"{-n.Rake.Velocity:0.00} m/s");
        // Its mate lets go: so does it.
        n.Run(Dt, id => id == 2 ? Bot() : new PlayerIntent { Buttons = PlayerButtons.Jump });
        n.Run(Dt, id => id == 2 ? Bot() : default);
        Assert.False(n.Players[2].Has(PlayerFlags.Pumping));
    }

    // ---- Netcode.

    [Fact]
    public void ClientsPredictTheirPumpersExactly()
    {
        var line = new RailLine(new LineDefinition("t", [new TrackSegment(40_000)]));
        TrainOnLine Train() => new(new TrainDynamics(Consist.Uniform(T, 4, 1)), line, 2_000);
        var net = new LoopbackNetwork();
        var host = new HostSession(net.CreateHost(), Train(), T, P);
        host.World.EnableBodies();
        var clients = Enumerable.Range(0, 2).Select(_ => new ClientSession(net.CreateClient(), Train(), T, P)).ToArray();
        void Step(int ticks, Func<int, PlayerIntent> intent)
        {
            for (int t = 0; t < ticks; t++)
            {
                net.Advance(Dt);
                host.Controls = new TrainControls { Reverser = 1, Brake = 1 };
                host.Step();
                for (int i = 0; i < clients.Length; i++)
                    clients[i].Step(intent(i));
            }
        }
        Step(30, _ => default);
        Assert.True(host.Train.SetHandcarOnRails());
        Step(3, _ => default);
        var car = host.Train.Handcar!;
        for (int i = 0; i < clients.Length; i++)
        {
            byte id = clients[i].PlayerId!.Value;
            var was = host.Players.First(p => p.Id == id).State;
            var s = PlayerMotor.StandUp(host.Train, car.Id, Handcar.Place(H, i == 0 ? -1 : 1), host.Train.Cars[car.Id].FrontDistance, P);
            host.Stand(id, s with { Placed = (byte)(was.Placed + 1) });
        }
        Step(10, _ => default);
        Step(1, _ => new PlayerIntent { Actions = PlayerActions.Seat });
        foreach (var c in clients)
            c.ResetStats();
        Step(30 * 20, i =>
        {
            var me = clients[i].Predicted;
            return new PlayerIntent { MoveZ = (float)Handcar.InTime(clients[i].Train.Handcar!.Phase, Handcar.EndAt(me.Position.Z), -1) };
        });
        Assert.All(clients, c => Assert.Equal(car.Id, c.Predicted.Parent));
        Assert.All(clients, c => Assert.True(c.Predicted.Has(PlayerFlags.Pumping)));
        Assert.True(host.Train.HandcarRake!.Speed > 5, $"{host.Train.HandcarRake!.Speed:0.00} m/s");
        Assert.All(clients, c => Assert.True(c.MaxCorrection < 0.001, $"corrected by {c.MaxCorrection} m"));
        // And every machine has it where the host does, a snapshot behind at most.
        Assert.All(clients, c => Assert.InRange(c.Train.HandcarRake!.Distance - host.Train.HandcarRake!.Distance, -0.5, 0.5));
    }
}
