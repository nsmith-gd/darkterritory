using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 553 (queue #295, D1's falls audit after #294): the harness keeps every fall of a night, a crewmate come down out of the
/// air onto the ground well under the rails or hurt by the landing, and not the ordinary step down off the train at a stand.
/// </summary>
public class FallWatchTests
{
    static readonly PlayerTuning P = Tuning.Player;

    static (FallWatch Watch, Night Night, double Rail, double Along) Start()
    {
        var n = new Night(4, 6, enemies: HoundRunTests.Quiet);
        double along = n.Train.Cars[1].FrontDistance;
        double rail = n.Train.Line.Sample(RailLine.MainPath, along).Position.Y;
        return (new FallWatch(), n, rail, along);
    }

    /// <summary>Beside the track at <paramref name="along"/>, 2.5 m out, at height <paramref name="y"/>.</summary>
    static PlayerState At(Night n, double along, double y, Surface surface, int health)
    {
        var t = n.Train.Line.Sample(RailLine.MainPath, along);
        var beside = t.Position + Double3.Cross(t.Tangent, Double3.Up).Normalized * 2.5;
        return new() { Parent = PlayerState.World, Surface = surface, Position = beside with { Y = y }, Health = health, LineHint = along };
    }

    [Fact]
    public void DownOffABankBesideTheTrainIsAFall()
    {
        var (w, n, rail, along) = Start();
        w.Watch(0, n.Train, 1, At(n, along, rail, Surface.Ground, 71), null);
        w.Watch(1, n.Train, 1, At(n, along, rail - 0.5, Surface.Air, 71), null);
        w.Watch(2, n.Train, 1, At(n, along, rail - 8, Surface.Ground, 71), null);
        var f = Assert.Single(w.Falls);
        Assert.Equal(8, f.Below, 1);
        Assert.Equal(0, f.Hurt);
        Assert.False(f.Killed);
    }

    [Fact]
    public void AStepDownOntoTheBallastIsNot()
    {
        var (w, n, rail, along) = Start();
        w.Watch(0, n.Train, 1, PlayerMotor.SpawnOnRoof(n.Train, 1, 0, P), null);
        w.Watch(1, n.Train, 1, At(n, along, rail + 0.4, Surface.Air, P.Health), null);
        w.Watch(2, n.Train, 1, At(n, along, rail - 0.3, Surface.Ground, P.Health), null);
        Assert.Empty(w.Falls);
    }

    [Fact]
    public void AHurtingLandingIsOneWhereverItIs()
    {
        // Off a moving train onto the ballast (spec B.3): a roll's knock at the rail's own height still counts, and a death.
        var (w, n, rail, along) = Start();
        w.Watch(0, n.Train, 1, At(n, along, rail + 1, Surface.Air, 80), null);
        w.Watch(1, n.Train, 1, At(n, along, rail, Surface.Ground, 62), null);
        w.Watch(0, n.Train, 2, At(n, along, rail + 1, Surface.Air, 50), null);
        w.Watch(1, n.Train, 2, At(n, along, rail, Surface.Ground, 0) with { Death = DeathCause.JumpedAtSpeed }, null);
        Assert.Equal(2, w.Falls.Count);
        Assert.Equal(18, w.Falls[0].Hurt);
        Assert.True(w.Falls[1].Killed);
    }

    [Fact]
    public void WarmInTheLastCarOfARakeItComesOutTheEndThatHasAPlate()
    {
        // The commonest fall on frontier:7: warming in a car whose cars behind were left at a stop, out of its rear door onto
        // nothing. Its own rear door was the way in; with the cars behind it gone, the way out is the front door.
        var n = new Night(4, 6, enemies: HoundRunTests.Quiet);
        n.Crew[0] = PlayerMotor.SpawnInCab(n.Train, P);
        int car = n.Train.Dynamics.Consist.Vehicles[2].Id;
        var bot = new RoofWalkerBot(7, P.Cold) { Me = 1 };
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, car, 2, P) with { Cold = P.Cold.OnsetSeconds * 0.9 };
        for (int i = 0; i < 60 * 4 && bot.WarmUpStep is not "Warm/cold"; i++)
            n.Run(0.25, id => id == 1 ? bot.Decide(n.Crew[1], n.World, n.World.Tick, out _) : default);
        Assert.Equal("Warm/cold", bot.WarmUpStep);
        int warm = n.Crew[1].Parent;
        Assert.True(n.Train.Uncouple(warm));
        Assert.Equal(-1, n.Train.VehicleBehind(warm));
        var steps = new List<string>();
        var trail = new List<string>();
        for (int i = 0; i < 90 * 4; i++)
        {
            n.Run(0.25, id => id == 1 ? bot.Decide(n.Crew[1], n.World, n.World.Tick, out _) : default);
            if (bot.WarmUpStep is { } w && (steps.Count == 0 || steps[^1] != w)) steps.Add(w);
            var c = n.Crew[1];
            trail.Add($"{c.Surface}@{c.Parent} ({c.Position.X:0.0},{c.Position.Y:0.0},{c.Position.Z:0.0})");
            Assert.False(c.Parent == PlayerState.World && c.Surface == Surface.Ground, $"off onto the ground at {i / 4.0:0.0} s: {string.Join(" > ", steps)}; {string.Join(" | ", trail.TakeLast(12))}");
        }
        Assert.Contains("Out", steps);
        Assert.Equal(P.Health, n.Crew[1].Health);
    }

    [Fact]
    public void ColdOnASideDoorsLandingItStepsInBeforeItGoesAnywhere()
    {
        // deepTerritory:2's gunner, in a car to shut its doors and warm up, stood out on a side door's landing beyond the walls (its
        // side door shut), and the walk to the open end door went along the car's outside and off the landing's end. In first.
        var n = new Night(4, 6, enemies: HoundRunTests.Quiet);
        n.Crew[0] = PlayerMotor.SpawnInCab(n.Train, P);
        int car = n.Train.Dynamics.Consist.Vehicles[2].Id;
        var shape = n.Train.Frames[car].Shape;
        var walls = shape.Interior!.Value;
        var side = shape.DoorList.First(d => Math.Abs(d.Box.Centre.Z) < 1 && d.Box.Centre.X > 0.5);
        n.Train.Vehicles[car].ToggleDoor(1); // the rear end door open, the side door shut
        var bot = new RoofWalkerBot(7, P.Cold) { Me = 1 };
        n.Crew[1] = new PlayerState { Parent = car, Surface = Surface.Deck, Position = new Double3(walls.Max.X + 0.3, 0, side.Box.Centre.Z - 1.5),
            Health = P.Health, Cold = P.Cold.OnsetSeconds * 0.9, LineHint = n.Train.Cars[car].FrontDistance };
        n.Run(SimConstants.TickSeconds);
        bool inside = false;
        for (int i = 0; i < 20 * 4 && !inside; i++)
        {
            n.Run(0.25, id => id == 1 ? bot.Decide(n.Crew[1], n.World, n.World.Tick, out _) : default);
            var c = n.Crew[1];
            Assert.True(c.Parent == car, $"off the car at {i / 4.0:0.0} s: {c.Surface}@{c.Parent} ({c.Position.X:0.0},{c.Position.Z:0.0}), {bot.WarmUpStep}");
            inside = c.Position.X < walls.Max.X && c.Surface == Surface.Deck;
        }
        var e = n.Crew[1];
        Assert.True(inside, $"never in: {e.Surface}@{e.Parent} ({e.Position.X:0.00},{e.Position.Y:0.00},{e.Position.Z:0.00}), walls {walls.Min.X:0.00}..{walls.Max.X:0.00}, door z {side.Box.Min.Z:0.0}..{side.Box.Max.Z:0.0}, {bot.WarmUpStep}");
    }
}
