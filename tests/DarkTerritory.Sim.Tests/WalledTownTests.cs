using Ballast;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Towns;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Walled towns (the director, 7 Oct 2026: "towns can go up to 3000 and ... fortresses aren't just some straight line
/// around the railroad, they should surround towns, towns should be explorable"; queue #74, ARCHITECTURE §8 note 335):
/// a big town's streets beside the line inside a wall that goes round it; a small one still the yard's corridor.
/// </summary>
public class WalledTownTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TownContent Towns = TownContent.Load(Content)!;
    static readonly PlayerTuning P = Tuning.Player;

    /// <summary>A night on <paramref name="spec"/> with a town of <paramref name="people"/>, as World.EnableTown stands it.</summary>
    static (World World, Town Town) Night(string spec, int people)
    {
        var content = Towns with { Tuning = Towns.Tuning with { Population = [people, people] } };
        var route = Routes.Generate(Content, spec, 6);
        double gate = route.GateOr(RouteTuning.Load(Content).YardLength);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), route.Build(), gate - 8);
        var world = new World(train, Tuning.Combat);
        world.EnableRun(Tuning.Run, route, gate, authority: true);
        world.EnableTown(content, route, gate, []);
        return (world, world.Town!);
    }

    [Fact]
    public void ABigTownHasStreetsInsideAWallRoundItAndASmallOneIsTheYard()
    {
        var (_, big) = Night("frontier:7", 3000);
        var b = Assert.IsType<TownBounds>(big.Plan.Bounds);
        Assert.True(b.Streets.Count >= 4, $"{b.Streets.Count} streets for 3000 people");
        Assert.NotEmpty(b.Lanes);
        Assert.InRange(Math.Max(b.Left, b.Right), 60, Towns.Tuning.Walled.First + Towns.Tuning.Walled.MaxStreets * Towns.Tuning.Walled.Every);
        // Its households all housed (more to a house only where there aren't houses enough).
        Assert.True(big.Plan.Houses.Count(h => h.Kind is HouseKind.Lived or HouseKind.Open) >= 3000 / 6);
        var (_, small) = Night("frontier:7", 40);
        Assert.Null(small.Plan.Bounds);
    }

    [Theory]
    [InlineData("frontier:7", 3000)]
    [InlineData("local:3", 1200)]
    [InlineData("deadLines:3", 600)]
    public void EveryHouseAndYardStandsInsideTheWallOffTheStreetsAndClearOfTheNext(string spec, int people)
    {
        var (_, town) = Night(spec, people);
        var plan = town.Plan;
        var b = plan.Bounds;
        var sq = plan.Square;
        // What stands of each house and its yard, in the rail frame.
        var boxes = plan.Houses.SelectMany(h => h.Solids().Select(p =>
        {
            var (s0, d0) = h.Rail(p.U0, p.V0);
            var (s1, d1) = h.Rail(p.U1, p.V1);
            return (h.Id, S0: Math.Min(s0, s1), S1: Math.Max(s0, s1), D0: Math.Min(d0, d1), D1: Math.Max(d0, d1));
        })).ToList();
        // Open houses' main blocks are their walls, not a part: add them.
        boxes.AddRange(plan.Houses.Where(h => h.Layout is not null).Select(h =>
            (h.Id, h.S - h.Width / 2, h.S + h.Width / 2, Math.Min(h.FrontD, h.FrontD + h.Side * h.Depth), Math.Max(h.FrontD, h.FrontD + h.Side * h.Depth))));
        foreach (var x in boxes)
        {
            if (b is not null)
                Assert.True(b.Holds(x.S0, x.D0) && b.Holds(x.S1, x.D1), $"house {x.Id} at ({x.S0:0}..{x.S1:0}, {x.D0:0}..{x.D1:0}) outside the wall");
            Assert.True(x.D0 > 3 || x.D1 < -3, $"house {x.Id} on the line");
            foreach (var st in b?.Streets ?? [])
            {
                // Where the street is along the house (it bends: note 353).
                double near = Math.Min(Math.Min(st.At(x.S0), st.At(x.S1)), st.At((x.S0 + x.S1) / 2)), far = Math.Max(Math.Max(st.At(x.S0), st.At(x.S1)), st.At((x.S0 + x.S1) / 2));
                Assert.False(x.D1 > near - st.Width / 2 && x.D0 < far + st.Width / 2 && x.S1 > st.S0 && x.S0 < st.S1, $"house {x.Id} in the street at {st.D:0}");
            }
            // Where the lane is across the house's depth (it runs crooked: note 353).
            foreach (var lane in b?.Lanes ?? [])
                Assert.False(lane.Span(x.D0, x.D1) is var (lo, hi) && x.S1 > lo && x.S0 < hi, $"house {x.Id} in the lane at {lane.S:0}");
            Assert.False(Math.Sign(x.D0) == sq.Side && x.S1 > sq.S0 && x.S0 < sq.S1 && Math.Min(Math.Abs(x.D0), Math.Abs(x.D1)) < Math.Abs(sq.WallD),
                $"house {x.Id} in the square");
        }
        // No two houses' parts overlap (a part's own house's other parts may meet it).
        for (int i = 0; i < boxes.Count; i++)
            for (int j = i + 1; j < boxes.Count; j++)
            {
                var (p, q) = (boxes[i], boxes[j]);
                if (p.Id == q.Id)
                    continue;
                bool overlap = p.S1 > q.S0 + 0.01 && q.S1 > p.S0 + 0.01 && p.D1 > q.D0 + 0.01 && q.D1 > p.D0 + 0.01;
                Assert.False(overlap, $"houses {p.Id} and {q.Id} overlap");
            }
    }

    [Theory]
    [InlineData("frontier:7", 3000)]
    [InlineData("local:3", 800)]
    public void AYardIsFencedAtTheStreetWithItsGateAtTheDoorAndKeepsItsThingsBehindTheHouse(string spec, int people)
    {
        // The director's references (note 335): the picket fence out front, the board fence and the yard's things behind.
        var (_, town) = Night(spec, people);
        var houses = town.Plan.Houses;
        Assert.True(houses.Count(h => h.Yard.Count > 0) > houses.Count / 2, $"{houses.Count(h => h.Yard.Count > 0)} of {houses.Count} houses with a yard");
        Assert.Contains(houses, h => h.Yard.Any(y => y.Kind == YardKind.Picket));
        Assert.Contains(houses, h => h.Yard.Any(y => y.Kind == YardKind.Boards));
        Assert.Contains(houses, h => h.Yard.Any(y => y.Kind is YardKind.Woodpile or YardKind.Shed or YardKind.Privy or YardKind.Traps or YardKind.Dory));
        double lot = Towns.Tuning.Walled.Lot[1] / 2;
        foreach (var h in houses)
        {
            double back = h.Parts().Select(p => p.V1).Append(h.Depth).Max();
            var pickets = h.Yard.Where(y => y.Kind == YardKind.Picket).ToList();
            foreach (var y in h.Yard)
            {
                Assert.True(y.U0 >= -lot && y.U1 <= lot && y.U0 < y.U1 && y.V0 < y.V1, $"house {h.Id}'s {y.Kind} out of its lot: u {y.U0:0.0}..{y.U1:0.0}");
                if (y.Kind is not (YardKind.Picket or YardKind.Boards))
                    Assert.True(y.V0 >= back + 0.4, $"house {h.Id}'s {y.Kind} at v {y.V0:0.0}, against the house (its back's at {back:0.0})");
            }
            // The fence out front leaves the gate in front of the door, a body and more wide.
            Assert.All(pickets, y => Assert.True(y.V1 < -2.5, $"house {h.Id}'s fence {y.V1:0.0} m out, on its step"));
            Assert.False(pickets.Any(y => y.U1 > h.Design.DoorU - 0.7 && y.U0 < h.Design.DoorU + 0.7), $"house {h.Id}'s fence across its gate");
        }
    }

    [Fact]
    public void YouWalkFromTheLineDownALaneToTheOutermostStreetAndTheWallStopsYou()
    {
        var (world, town) = Night("frontier:7", 3000);
        var b = town.Plan.Bounds!;
        var lane = b.Lanes[b.Lanes.Count / 2];
        var train = world.Train;
        // From beside the line, down the lane's middle at a run (it turns at each street: note 353), out past the last
        // street, for a minute (at a walk, the 180 m out to the wall is a minute and a quarter).
        var from = town.World(lane.S, 4.5);
        var s = PlayerMotor.SpawnOnGround(from, train.Line, lane.S, P);
        double furthest = 0, along = lane.S;
        for (int i = 0; i < 60 * SimConstants.TickRate; i++)
        {
            // Where they are across the line, and so where the lane's middle is a few steps on.
            train.Line.Nearest(s.Position, ref along);
            double d = Double3.Dot(s.Position - town.World(along, 0), town.Direction(along, 0, 1));
            var aim = town.World(lane.At(d + 6), d + 6) - s.Position;
            s.Yaw = Math.Atan2(-aim.X, -aim.Z);
            PlayerMotor.Step(ref s, new PlayerIntent { MoveZ = 1, Buttons = PlayerButtons.Run }, train, P, Tuning.Train, SimConstants.TickSeconds);
            furthest = Math.Max(furthest, d);
        }
        double outer = b.Streets.Max(st => st.D);
        Assert.True(furthest > outer, $"got {furthest:0.0} m out down the lane; the outermost street is {outer:0.0} m out");
        Assert.True(furthest < b.Right, $"walked {furthest:0.0} m out, through the wall at {b.Right:0.0}");
    }

    [Theory]
    [InlineData("frontier:7", 3000)]
    [InlineData("local:3", 1200)]
    public void ALaneRunsCrookedButNeverIntoTheSquareTheGreenOrTheNextLane(string spec, int people)
    {
        // Note 353 (the director, 8 Oct: "Towns dont feel like they have a natural layout to them"): a lane turns where
        // it meets each street, so the crossings don't line up down the town.
        var (_, town) = Night(spec, people);
        var plan = town.Plan;
        var b = plan.Bounds!;
        var wt = Towns.Tuning.Walled;
        Assert.All(b.Lanes, lane => Assert.Contains(lane.Kinks!, k => Math.Abs(k.S - lane.S) >= wt.LaneJog[0] - 1e-6));
        // No two lanes meet, anywhere across the town.
        var lanes = b.Lanes.OrderBy(l => l.S).ToList();
        for (int i = 1; i < lanes.Count; i++)
            for (double d = -b.Left; d <= b.Right; d += 1)
                Assert.True(lanes[i].At(d) - lanes[i - 1].At(d) > 2 * wt.LaneWidth, $"lanes at {lanes[i - 1].S:0} and {lanes[i].S:0} meet {d:0} m out");
        // Nor runs into the square (its buildings back onto where its far wall was) or across the green.
        var sq = plan.Square;
        double reach = plan.Green is { } g ? g.Far : Math.Abs(sq.WallD);
        foreach (var lane in b.Lanes)
        {
            var (lo, hi) = lane.Span(0, sq.Side * reach);
            Assert.False(hi > sq.S0 && lo < sq.S1, $"the lane at {lane.S:0} runs into the square ({lo:0}..{hi:0} along the line)");
        }
        // The yards beside it are fenced along it (a board fence down the lot's side), a lane between fences.
        Assert.Contains(plan.Houses, h => h.Yard.Any(y => y.Kind == YardKind.Boards && y.V1 - y.V0 > y.U1 - y.U0));
        // And it's still open down its middle from the line to the outermost street: nothing solid on it.
        double outer = b.Streets.Max(st => Math.Abs(st.D));
        foreach (var lane in b.Lanes)
            foreach (int sd in new[] { -1, 1 })
                for (double d = 6; d < outer; d += 2)
                    Assert.True(town.Free(town.World(lane.At(sd * d), sd * d)), $"something solid in the lane at {lane.S:0}, {sd * d:0} m out");
    }

    [Fact]
    public void TheWholeTownIsTheFortAndItsWallIsSolid()
    {
        var (world, town) = Night("frontier:7", 3000);
        var b = town.Plan.Bounds!;
        // Out at the far streets, still in the fort (no creature comes in, nobody's left behind there).
        foreach (var st in b.Streets)
            Assert.True(world.InFort(town.World((st.S0 + st.S1) / 2, st.D)), $"the street {st.D:0} m out isn't in the fort");
        // The wall round it stands in the train's walls: down both sides, across the front and the back.
        var walls = world.Train.Walls!.All;
        double Flat(Double3 a, Double3 c) => ((a - c) with { Y = 0 }).Length;
        foreach (var d in new[] { -b.Left, b.Right })
            Assert.Contains(walls, w => w.HalfWidth == Fortresses.WallHalf && Flat(town.World(b.Rear + 55, d), w.Centre) < 6);
        // The rear wall across the line (behind the yard's start, where the line runs straight on, so measured from it).
        var start = world.Train.Line.Sample(0);
        var back = start.Position + new Double3(start.Tangent.X, 0, start.Tangent.Z).Normalized * b.Rear;
        Assert.Contains(walls, w => Flat(back, w.Centre) < 5 && w.HalfLength > b.Left);
        // And no corridor wall down the yard any more.
        Assert.DoesNotContain(walls, w => w.HalfWidth == Fortresses.WallHalf && Flat(town.World(b.Gate - 205, 14.8), w.Centre) < 6);
    }
}
