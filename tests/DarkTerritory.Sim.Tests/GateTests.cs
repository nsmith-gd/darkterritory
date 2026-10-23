using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Towns;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The fortress gates (queue #575, ARCHITECTURE §8 note 847; the director's playtest of 16 Oct, line 26: "The gates to the
/// fortress should close behind me"): each gatehouse has its leaves, shut across the passage but for the wicket, until the
/// train comes; they open as its engine comes up and shut once its last car's through, out of the departure and into the
/// arrival; and a shut gate stops a walker, but for its wicket.
/// </summary>
public class GateTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TownContent Towns = TownContent.Load(Content)!;
    static readonly IReadOnlyList<string> Identities = LineGenContent.Cached(Content).Config.Tiers.Fortress.Identities;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly GateTuning G = Tuning.Run.Gates;

    /// <summary>A night on <paramref name="spec"/> with both its towns (as EndTownTests' are), the train at the departure's gate.</summary>
    static (World World, Route.Route Route, double Gate) Night(string spec)
    {
        var route = Routes.Generate(Content, spec, 6);
        double gate = route.GateOr(RouteTuning.Load(Content).YardLength);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), route.Build(), gate - Tuning.Run.DepartShortOfGateM);
        var world = new World(train, Tuning.Combat);
        world.EnableRun(Tuning.Run, route, gate, authority: true);
        world.EnableTown(Towns, route, gate, [], arrival: TownAt.Terminus(route, Identities));
        return (world, route, gate);
    }

    /// <summary>The gates of a night with both its towns, along the main line: the departure's way in and out, the arrival's.</summary>
    static (double DepartIn, double DepartOut, double ArriveIn, double ArriveOut) Gates(World world, Route.Route route)
    {
        var depart = world.Town!.Plan.Bounds!;
        var arrive = world.Arrival!.Plan.Bounds!;
        double home = route.Plan!.Terminus.GateM;
        return (depart.Rear, depart.Gate, home, home + arrive.Gate - arrive.Rear);
    }

    static bool Open(World world, double along) => world.Train.Walls!.GateOpen(Fortresses.GateKey(along));

    [Fact]
    public void EveryGatehouseHasItsLeavesAndAWicketInOne()
    {
        var (world, route, gate) = Night("frontier:7");
        var walls = world.Train.Walls!;
        var (departIn, departOut, arriveIn, arriveOut) = Gates(world, route);
        Assert.Equal(gate, departOut, 6);
        // Both towns' two gates, each with its two leaves (the arrival's way on out stood too: it was a gap in its wall).
        Assert.Equal(new[] { departIn, departOut, arriveIn, arriveOut }.Select(Fortresses.GateKey).Order(), walls.Gates.Order());
        foreach (int key in walls.Gates)
        {
            var leaves = walls.All.Where(w => w.Gate == key).ToList();
            Assert.Equal(2, leaves.Count);
            Assert.All(leaves, l => Assert.True(l.Fort, "a gate's leaf is the fort's"));
            // Across the passage from wall to wall, but for the wicket: as wide as a door, and left of the line.
            double across = leaves.Sum(l => 2 * l.HalfWidth);
            Assert.Equal(2 * Fortresses.GateInner - (Fortresses.WicketHi - Fortresses.WicketLo), across, 6);
            Assert.True(Fortresses.WicketHi - Fortresses.WicketLo >= StopWalls.DoorWidth - 1e-9, "the wicket's narrower than a door");
        }
        // A fortress that isn't a town (a silent settlement's terminus) has its one gate's.
        var (silent, silentRoute, _) = Night("deadLines:3");
        Assert.Null(silent.Arrival);
        Assert.Contains(Fortresses.GateKey(silentRoute.Plan!.Terminus.GateM), silent.Train.Walls!.Gates);
    }

    [Fact]
    public void TheyOpenForTheTrainAndShutBehindItOutOfTheDepartureAndIntoTheArrival()
    {
        var (world, route, _) = Night("frontier:7");
        var walls = world.Train.Walls!;
        var (departIn, departOut, arriveIn, arriveOut) = Gates(world, route);
        double length = world.Train.Dynamics.Consist.LengthMetres;
        // The night starts with the engine just short of the departure's gate, so that gate's open (the train's going out
        // through it) and the rest shut: from the first, before any tick (note 869: never drawn swinging in toward an engine
        // already moving off), and on the first.
        // Note 998: the arrival's way in stands open for the train from the start, expecting it.
        Assert.True(Open(world, departOut), "the departure's gate is shut in front of the engine before the first tick");
        Assert.True(Open(world, arriveIn), "the arrival's gate is shut on a train it's expecting, before the first tick");
        Assert.False(Open(world, departIn) || Open(world, arriveOut));
        world.Step(new TrainControls { Brake = 1 });
        Assert.True(Open(world, departOut), "the departure's gate is shut in front of the engine");
        Assert.True(Open(world, arriveIn), "the arrival's gate is shut on a train it's expecting");
        Assert.False(Open(world, departIn) || Open(world, arriveOut));
        // Its last car through, the departure's gate shuts behind it. (The gates as World.StandGates stands them, with no Waker up.)
        bool At(double front) => walls.OpenGates(front, front - length, G,
            Fortresses.Expecting(arriveIn, front - length, double.NegativeInfinity, G) ? arriveIn : double.NaN);
        At(departOut + G.ShutBehindM + length - 0.5);
        Assert.True(Open(world, departOut), "shut on the last car");
        At(departOut + G.ShutBehindM + length + 0.5);
        Assert.False(Open(world, departOut), "the departure's gate open behind the train");
        // Down the line the arrival's way in stands open for it (note 998: never still swinging as an engine comes up at
        // speed), open while the train goes in, and shut behind it well before it's home.
        At(arriveIn - G.OpenAheadM - 1);
        Assert.True(Open(world, arriveIn), "shut before the train's there");
        At(arriveIn - G.OpenAheadM + 1);
        Assert.True(Open(world, arriveIn), "shut in front of the engine");
        At(arriveIn + length);
        Assert.True(Open(world, arriveIn), "shut on the train");
        double home = world.Run!.HomeFront(world.Train);
        At(home);
        Assert.False(Open(world, arriveIn), "open behind the train, home");
        // A town's other gates (the departure's way in, the arrival's way on out) no train comes to tonight: shut all night.
        for (double front = departOut - 8; front <= home; front += 25)
        {
            At(front);
            Assert.False(Open(world, departIn) || Open(world, arriveOut), $"a gate no train comes to open, the engine at {front:0}");
        }
    }

    /// <summary>A crewmate on the ground at <paramref name="along"/> the line, <paramref name="across"/> out from it, facing up it.</summary>
    static PlayerState Facing(TrainOnLine train, double along, double across)
    {
        var t = train.Line.Sample(along);
        var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
        var s = PlayerMotor.SpawnOnGround(t.Position + right * across, train.Line, along, P);
        s.Yaw = Math.Atan2(-t.Tangent.X, -t.Tangent.Z);
        return s;
    }

    /// <summary>How far along the line a point is, near <paramref name="hint"/>.</summary>
    static double Along(TrainOnLine train, double hint, Double3 p)
    {
        train.Line.Nearest(p, ref hint);
        return hint;
    }

    [Fact]
    public void AShutGateStopsAWalkerButItsWicketLetsThemIn()
    {
        // At the arrival's way in, the train still at the departure: shut.
        var (world, route, _) = Night("frontier:7");
        var train = world.Train;
        double gate = route.Plan!.Terminus.GateM;
        double length = train.Dynamics.Consist.LengthMetres;
        // (Shut as the old rule had it, the train at the departure: note 998 holds it open for the train, below.)
        train.Walls!.OpenGates(train.Dynamics.Distance, train.Dynamics.Distance - length, G);
        Assert.False(Open(world, gate));
        double Walk(double across)
        {
            var s = Facing(train, gate - 6, across);
            for (int i = 0; i < 5 * SimConstants.TickRate; i++)
                PlayerMotor.Step(ref s, new PlayerIntent { MoveZ = 1 }, train, P, Tuning.Train, SimConstants.TickSeconds);
            return Along(train, gate, s.Position) - gate;
        }
        // Up the line at either leaf: held at the gate.
        foreach (double across in new[] { -2.4, 0.6, 2.4 })
            Assert.True(Walk(across) < 0, $"walked through the shut gate {across:0.0} m off the line");
        // Through the wicket, in.
        Assert.True(Walk((Fortresses.WicketLo + Fortresses.WicketHi) / 2) > 3, "the wicket didn't let a walker in");
        // The train at it, the gate's open: through anywhere across the passage.
        train.Walls!.OpenGates(gate - 20, gate - 20 - length, G);
        Assert.True(Open(world, gate));
        foreach (double across in new[] { -2.4, 2.4 })
            Assert.True(Walk(across) > 3, $"the open gate held a walker {across:0.0} m off the line");
    }

    [Fact]
    public void TheArrivalsGateIsHeldOpenForTheTrainUntilItsInOrAWakersAtIt()
    {
        // Note 998 (the director's playtest of 23 Oct, line 19: "the gate at the beginning is open, and then it closes behind
        // us at dawn. And if we're not in by dawn ... it stays open expecting us until the waker ... gets too close").
        var (world, route, _) = Night("frontier:7");
        double gate = route.Plan!.Terminus.GateM;
        world.Step(new TrainControls { Brake = 1 });
        Assert.True(Open(world, gate), "shut on the train it's expecting, with no Waker up");
        // Past dawn, a Waker coming down the line behind the train: held open for it while he's far off ...
        var waker = world.AddEnemy(id => Sim.Enemies.Waker.Rise(id, world.Train.Dynamics.RearDistance, 0, Tuning.Enemies.Wakers));
        waker.LineDistance = gate - G.WakerShutM - 50;
        world.Step(new TrainControls { Brake = 1 });
        Assert.True(Open(world, gate), "shut with the Waker still far off");
        // ... and shut once he's too close to it, the train still out.
        waker.LineDistance = gate - G.WakerShutM + 1;
        world.Step(new TrainControls { Brake = 1 });
        Assert.False(Open(world, gate), "held open with the Waker at the gate");
        // The rule: open for a train not yet in, whatever the Waker, once it's in shut; and not held with expect off.
        Assert.True(Fortresses.Expecting(gate, gate - 100, double.NegativeInfinity, G));
        Assert.False(Fortresses.Expecting(gate, gate + G.ShutBehindM + 0.5, double.NegativeInfinity, G));
        Assert.False(Fortresses.Expecting(gate, gate - 100, double.PositiveInfinity, G), "held open on a train a Waker has");
        Assert.False(Fortresses.Expecting(gate, gate - 100, double.NegativeInfinity, G with { Expect = false }));
        // A train coming up to a gate shut for the Waker still has it open as it goes through (the old rule's): never clipped.
        var walls = world.Train.Walls!;
        double length = world.Train.Dynamics.Consist.LengthMetres;
        walls.OpenGates(gate - G.OpenAheadM + 1, gate - G.OpenAheadM + 1 - length, G);
        Assert.True(Open(world, gate));
    }
}
