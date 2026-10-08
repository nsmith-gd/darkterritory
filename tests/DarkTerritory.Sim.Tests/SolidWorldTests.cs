using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Stops;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The world is solid (the director, 6 Oct 2026: "the carry that clipped straight through the mountain made everything feel
/// like 2D billboards. Creatures, carries and players must respect the terrain and geometry"; ARCHITECTURE §8 note 279).
/// A yard's sheds and its powerhouse are walls the crew goes round or in by the door; a tunnel's lining holds whoever's in
/// it; nobody walks up a cutting's wall; what runs beside the train runs inside the bore, on the deck and on the land; what's
/// loose in the world is out of the buildings and on the ground; the Whistler's run goes through no building.
/// </summary>
[Collection(nameof(LineGenTests))]
public class SolidWorldTests
{
    static readonly PlayerTuning P = Tuning.Player;
    static readonly string Content = DataFile.FindContentRoot();

    static (Route.Route Route, TrainOnLine Train) Night(string spec, double at = 600)
    {
        var route = Routes.Generate(Content, spec, 6);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), route.Build(), at);
        train.Walls = StopWalls.Of(route, train.Line, Fortresses.Of(route, train.Line, 600, Tuning.Run.TerminusZone), Tuning.Run.Walls);
        return (route, train);
    }

    static bool InAnyWall(StopWalls walls, Double3 p, double pad) => walls.Near(p).Any(w =>
    {
        var l = w.ToLocal(p);
        return p.Y < w.Top && p.Y > w.Bottom && Math.Abs(l.X) < w.HalfLength + pad && Math.Abs(l.Z) < w.HalfWidth + pad;
    });

    /// <summary>Walks a player from <paramref name="from"/> towards <paramref name="to"/> for <paramref name="seconds"/>, never letting it be in a wall.</summary>
    static PlayerState Walk(TrainOnLine train, Double3 from, Double3 to, double seconds, double along)
    {
        var s = PlayerMotor.SpawnOnGround(from, train.Line, along, P);
        var d = to - from;
        s.Yaw = Math.Atan2(-d.X, -d.Z);
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
        {
            PlayerMotor.Step(ref s, new PlayerIntent { MoveZ = 1 }, train, P, Tuning.Train, SimConstants.TickSeconds);
            Assert.False(InAnyWall(train.Walls!, s.Position, P.Radius - 0.02), $"inside a wall at {s.Position}, tick {i}");
        }
        return s;
    }

    static IEnumerable<(Route.RouteFeature Feature, StopLayout Stop, int Index)> Buildings(Route.Route route, Func<StopLayout, int, bool> which) =>
        route.Features.Where(f => f.Stop is not null).SelectMany(f => Enumerable.Range(0, f.Stop!.Buildings.Count).Where(i => which(f.Stop!, i)).Select(i => (f, f.Stop!, i)));

    [Fact]
    public void AShedIsWallsTheCrewGoesInByItsDoorNotThroughItsBack()
    {
        var (route, train) = Night("frontier:7");
        var (f, stop, i) = Buildings(route, (s, i) => s.Buildings[i].Kind == BuildingKind.Shed && StopWalls.Doors(s, i, Tuning.Run.Walls).Any()).First();
        var b = stop.Buildings[i];
        var door = StopWalls.Doors(stop, i, Tuning.Run.Walls).First();
        Pt Frame(double x, double y) => World(b, x, y);
        // At its back, from 10 m behind it towards its middle: stopped at the wall.
        var back = Run.Run.StopWorld(train.Line, f, Frame(door.At, -door.Side * (b.Width / 2 + 10)));
        var middle = Run.Run.StopWorld(train.Line, f, Frame(door.At, 0));
        var held = Walk(train, back, middle, 8, f.Start + b.S);
        Assert.True(Math.Abs(Local(Pt(train, f, held.Position), b).Y) >= b.Width / 2 - 0.05, "it stopped at the back wall");
        // At its door, from 8 m out in front of it: in, as far as its middle.
        var front = Run.Run.StopWorld(train.Line, f, Frame(door.At, door.Side * (b.Width / 2 + 8)));
        var inside = Walk(train, front, middle, 8, f.Start + b.S);
        var l = Local(Pt(train, f, inside.Position), b);
        Assert.True(Math.Abs(l.Y) < b.Width / 2 - 0.3 && Math.Abs(l.X) < b.Length / 2, $"in through the door ({l.X:0.0}, {l.Y:0.0})");
    }

    /// <summary>A building's own frame (x along its axis, y across) in its stop's (S, D): axis (cos, sin), across (−sin, cos).</summary>
    static Pt World(StopBuilding b, double x, double y) =>
        new(b.S + x * Math.Cos(b.Yaw) - y * Math.Sin(b.Yaw), b.D + x * Math.Sin(b.Yaw) + y * Math.Cos(b.Yaw));

    static (double X, double Y) Local(Pt p, StopBuilding b)
    {
        double ds = p.S - b.S, dd = p.D - b.D;
        return (ds * Math.Cos(b.Yaw) + dd * Math.Sin(b.Yaw), -ds * Math.Sin(b.Yaw) + dd * Math.Cos(b.Yaw));
    }

    /// <summary>A world point in a stop's rail frame.</summary>
    static Pt Pt(TrainOnLine train, Route.RouteFeature f, Double3 p)
    {
        double hint = f.Start;
        var (_, along) = train.Line.Nearest(p, ref hint);
        var t = train.Line.Sample(along);
        var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
        return new Pt(along - f.Start, Double3.Dot(p - t.Position, right));
    }

    [Fact]
    public void TheYardsShutBuildingsAndWellsAreWalls()
    {
        var (route, train) = Night("frontier:7");
        var kinds = new[] { BuildingKind.Powerhouse, BuildingKind.Well, BuildingKind.SignalBox, BuildingKind.WaterTower, BuildingKind.LampRoom, BuildingKind.Lockup };
        int walked = 0;
        foreach (var (f, stop, i) in Buildings(route, (s, i) => kinds.Contains(s.Buildings[i].Kind) && StopWalls.Walled(s, i)).Take(4))
        {
            var b = stop.Buildings[i];
            var centre = Run.Run.StopWorld(train.Line, f, b.Centre);
            var from = Run.Run.StopWorld(train.Line, f, new Pt(b.S, b.D - Math.Sign(b.D == 0 ? 1 : b.D) * (Math.Max(b.Length, b.Width) / 2 + 8)));
            var end = Walk(train, from, centre, 6, f.Start + b.S);
            Assert.True(((end.Position - centre) with { Y = 0 }).Length > Math.Min(b.Length, b.Width) / 2, $"{b.Kind}: walked into it");
            walked++;
        }
        Assert.True(walked > 0, "frontier:7's stops have a powerhouse, a well or a signal box");
    }

    [Fact]
    public void ATunnelsLiningHoldsWhoeverIsInIt()
    {
        var (route, train) = Night("deepTerritory:2");
        var bore = route.Plan!.Structures.First(s => s.Type == StructureType.Tunnel && s.Edge == "main");
        double s = (bore.S0 + bore.S1) / 2;
        var t = train.Line.Sample(s);
        var right = Double3.Cross(t.Tangent, Double3.Up).Normalized;
        var from = t.Position + right * 2;
        var p = Walk(train, from, from + right * 20, 5, s);
        double lateral = Double3.Dot(p.Position - t.Position, right);
        double room = route.Plan.Rules.Terrain.BoreHalfM - P.Radius;
        Assert.True(lateral <= room + 0.05, $"through the lining: {lateral:0.00} m out of a {room:0.00} m bore");
        // On the bore's floor, not stood on the hill over it.
        Assert.True(p.Position.Y - t.Position.Y < 1, $"lifted {p.Position.Y - t.Position.Y:0.0} m");
    }

    /// <summary>Nights deep enough for cuttings, tunnels and decks: the first with what a test needs is used, so a change to the line
    /// generator moves the test with it (note 278's bends took deepTerritory:2's tall cutting).</summary>
    static readonly string[] Deep = ["deepTerritory:2", "deepTerritory:4", "deadLines:3", "deepTerritory:1", "deadLines:2"];

    /// <summary>The first of <see cref="Deep"/> with a main-line cutting at least <paramref name="high"/> m on one side over more than
    /// <paramref name="longer"/> m, no structure within <paramref name="clear"/> of it: its middle and side (+1 right).</summary>
    static (Route.Route Route, TrainOnLine Train, double S, int Side, double High) Cutting(double high, double longer, double clear)
    {
        foreach (var spec in Deep)
        {
            var (route, train) = Night(spec);
            var plan = route.Plan!;
            foreach (var i in plan.Intents.Where(i => i.Edge == "main" && i.S1 - i.S0 > longer
                && !plan.Structures.Any(st => st.Edge == "main" && st.S1 > i.S0 - clear && st.S0 < i.S1 + clear)))
            {
                if (i.Left.T == IntentType.Cutting && i.Left.H >= high)
                    return (route, train, (i.S0 + i.S1) / 2, -1, i.Left.H);
                if (i.Right.T == IntentType.Cutting && i.Right.H >= high)
                    return (route, train, (i.S0 + i.S1) / 2, 1, i.Right.H);
            }
        }
        throw new InvalidOperationException($"no night with a {high} m cutting");
    }

    [Fact]
    public void NobodyWalksUpACuttingsWallButTheFormationIsWalked()
    {
        var (_, train, s, side, high) = Cutting(8, 200, 100);
        var t = train.Line.Sample(s);
        var out_ = Double3.Cross(t.Tangent, Double3.Up).Normalized * side;
        var from = t.Position + out_ * 4;
        var p = Walk(train, from, from + out_ * 30, 6, s);
        Assert.True(p.Position.Y - t.Position.Y < 1.5, $"up the cutting's {high:0} m wall to {p.Position.Y - t.Position.Y:0.0} m");
        // Along it on the formation, as ever.
        var along = Walk(train, from, from + t.Tangent * 30, 4, s);
        Assert.True((along.Position - from).Length > 4, "walked along the formation");
    }

    [Fact]
    public void WhatRunsBesideTheTrainIsInTheBoreOnTheDeckAndOnTheLand()
    {
        var (route, train) = Deep.Select(spec => Night(spec)).First(n => n.Route.Plan!.Structures.Any(s => s.Type == StructureType.Tunnel && s.Edge == "main")
            && n.Route.Plan.Structures.Any(s => s.Type is StructureType.Trestle or StructureType.Girder or StructureType.Truss or StructureType.Viaduct && s.Edge == "main"));
        var plan = route.Plan!;
        var r = plan.Rules.Terrain;
        var bore = plan.Structures.First(s => s.Type == StructureType.Tunnel && s.Edge == "main");
        var hound = new CinderHound(1, 0) { LineDistance = (bore.S0 + bore.S1) / 2, Lateral = 5, Height = 0.6 };
        Assert.InRange(Lateral(train, hound), 0, r.BoreHalfM - r.BesideClearM + 1e-6);
        var deck = plan.Structures.First(s => s.Type is StructureType.Trestle or StructureType.Girder or StructureType.Truss or StructureType.Viaduct && s.Edge == "main");
        hound.LineDistance = (deck.S0 + deck.S1) / 2;
        hound.Lateral = -5.5;
        Assert.InRange(Lateral(train, hound), -(r.DeckHalfM - r.BesideClearM) - 1e-6, 0);
        // Out past the formation in a cutting: on the land, its own height over it.
        var (_, cutTrain, s, side, _) = Cutting(6, 0, 50);
        hound.LineDistance = s;
        hound.Lateral = 6 * side;
        var at = hound.WorldPosition(cutTrain);
        Assert.Equal(cutTrain.Line.Conditions!.Ground(at) + 0.6, at.Y, 2);
    }

    static double Lateral(TrainOnLine train, Enemy e)
    {
        var t = train.Line.Sample(e.LineDistance);
        return Double3.Dot(e.WorldPosition(train) - t.Position, Double3.Cross(t.Tangent, Double3.Up).Normalized);
    }

    [Fact]
    public void WhatsLooseIsOutOfTheBuildingsAndOnTheLand()
    {
        var (route, train) = Night("frontier:7");
        var (f, stop, i) = Buildings(route, (s, i) => StopWalls.Walled(s, i) && !s.Buildings[i].Open && s.Buildings[i].Kind != BuildingKind.Well).First();
        var centre = Run.Run.StopWorld(train.Line, f, stop.Buildings[i].Centre);
        var ribbit = Ribbit.At(1, 1, centre + Double3.Up * 6, Tuning.Enemies.Ribbits);
        var choir = ChoirGhost.Around(2, centre + Double3.Up * 2, Tuning.Enemies.Choir);
        Solidity.Settle([ribbit, choir], train, Tuning.Enemies);
        // The Ribbit out of the house and down on the ground; the Choir out of it, still in the air.
        Assert.False(InAnyWall(train.Walls!, ribbit.Local, 0.3), $"a Ribbit in a house at {ribbit.Local}");
        Assert.Equal(train.Line.Conditions!.Ground(ribbit.Local), ribbit.Local.Y, 3);
        Assert.False(InAnyWall(train.Walls!, choir.Local, 0.3), $"the Choir in a house at {choir.Local}");
        Assert.True(choir.Local.Y > train.Line.Conditions.Ground(choir.Local) + 1);
    }

    [Fact]
    public void AGauntSleepsInItsRoostAndWalksOutOfItAwake()
    {
        // B4's roost (note 309): asleep in the building, curled up where the stop put it. Awake, it's held out of the walls
        // like anything else that walks: put down in a shut building (every house stands open since note 326; a barn doesn't).
        var (route, train) = Night("frontier:7");
        var (f, stop, i) = Buildings(route, (s, i) => StopWalls.Walled(s, i) && !s.Buildings[i].Open && s.Buildings[i].Kind != BuildingKind.Well).First();
        var centre = Run.Run.StopWorld(train.Line, f, stop.Buildings[i].Centre);
        var asleep = Gaunt.Asleep(1, centre, Tuning.Enemies.Gaunt);
        var awake = Gaunt.WokenBy(2, centre, 1, Tuning.Enemies.Gaunt);
        Solidity.Settle([asleep, awake], train, Tuning.Enemies);
        Assert.Equal(centre, asleep.Local);
        Assert.False(InAnyWall(train.Walls!, awake.Local, 0.3), $"an awake Gaunt in a house at {awake.Local}");
    }

    [Fact]
    public void TheWhistlersRunToItsNestGoesThroughNoBuilding()
    {
        var (route, train) = Night("frontier:7", 3000);
        var gap = train.Frames[1].ToWorld(new Double3(0, 0.6, train.Frames[1].Shape.HalfLength + 0.5));
        var right = train.Frames[1].Right;
        var t = Tuning.Enemies.Whistler;
        var (open, _) = Whistler.NestSite(train, gap, right, 1, t);
        // A wall across the way it would have run: it nests the other side, or short of the wall, but never through it.
        var across = (right with { Y = 0 }).Normalized;
        var mid = gap + across * (t.NestDistance * 0.5);
        var wall = new Wall(mid with { Y = 0 }, new Double3(across.Z, 0, -across.X), 12, 1, gap.Y - 5, gap.Y + 9);
        train.Walls = StopWalls.Of([wall]);
        var (nest, run) = Whistler.NestSite(train, gap, right, 1, t);
        Assert.NotEqual(open, nest);
        for (double x = 0; x <= run; x += 0.5)
        {
            var at = gap + ((nest - gap) with { Y = 0 }).Normalized * x;
            Assert.False(InAnyWall(train.Walls, at with { Y = gap.Y }, 0), $"its run goes through the wall {x:0.0} m out");
        }
    }
}
