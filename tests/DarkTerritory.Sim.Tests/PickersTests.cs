using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The Pickers (GDD §21, App. A.6, B.6; the director's pick to prototype, 9 Oct 2026; ARCHITECTURE §8 note 574). Rule: get
/// there first. Each takes the nearest crate nobody's standing by and carries it down its drain; a guarded crate is never
/// taken; a heavy crate takes two; come right up to one carrying and it bites (never a kill) and drops it; a blow kills one
/// and scatters the rest near it; the train going sends them down.
/// </summary>
public class PickersTests
{
    static readonly PickersTuning K = Tuning.Enemies.Pickers;
    static readonly PlayerTuning P = Tuning.Player;

    static Double3 Beside(Night n, int car, double lateral, double along = 0)
    {
        var at = n.Train.Frames[car].ToWorld(new Double3(lateral, 0, along));
        double hint = n.Train.Dynamics.Distance;
        return at with { Y = PlayerMotor.GroundAt(at, n.Train.Line, ref hint) };
    }

    static PlayerState Ground(Night n, Double3 at, Double3? toward = null)
    {
        var s = PlayerMotor.SpawnOnGround(at, n.Train.Line, n.Train.Dynamics.Distance, P);
        if (toward is { } to)
        {
            var d = to - at;
            s.Yaw = DMath.Atan2(-d.X, -d.Z);
        }
        return s;
    }

    /// <summary>The night with its bodies stepped too (what's carried and dropped).</summary>
    static void Run(Night n, double seconds, Func<int, PlayerIntent>? intent = null)
    {
        for (int i = 0; i < seconds * SimConstants.TickRate; i++)
        {
            n.Run(SimConstants.TickSeconds, intent);
            n.World.StepBodies([.. n.Crew.Select(c => (c.Key, c.Value))]);
        }
    }

    /// <summary>A crate 14 m off car 2, and <paramref name="count"/> Pickers come up at a drain 12 m on along the line from it.</summary>
    static (Body Crate, List<Picker> Pickers, Double3 Drain) Stage(Night n, int count = 1, double? heavy = null)
    {
        var at = Beside(n, 2, 14);
        var crate = n.World.Bodies.SpawnCargo(at, n.Train.Dynamics.Distance, heavy);
        var drain = Beside(n, 2, 14, 12);
        var pickers = new List<Picker>();
        for (int i = 0; i < count; i++)
            pickers.Add(n.World.AddEnemy(id => Picker.Up(id, drain, 0, n.Train.Dynamics.Distance, 0, K)));
        return (crate, pickers, drain);
    }

    static bool Present(Night n, Body b) => n.World.Bodies.All.Any(x => x.Id == b.Id);

    static double Flat(Double3 v) => (v with { Y = 0 }).Length;

    [Fact]
    public void OneTakesTheNearestCrateNobodysByDownItsDrain()
    {
        var n = new Night(4, speed: 0);
        var (crate, pickers, _) = Stage(n);
        Run(n, 4);
        Assert.Equal(crate.Id, crate.TakenBy == pickers[0].Id ? crate.Id : -1);
        Assert.Equal(PickerMode.Carry, pickers[0].Mode);
        // 12 m to its drain at a carry's pace: there, and down with it.
        Run(n, 12 / K.Carry + 1);
        Assert.False(Present(n, crate));
        Assert.Equal(1, n.World.PickersTook);
        // Down with it, and up again a little after.
        Assert.Equal(PickerMode.Down, pickers[0].Mode);
        Run(n, K.DownSeconds + K.EmergeSeconds + 0.5);
        Assert.NotEqual(PickerMode.Down, pickers[0].Mode);
    }

    [Fact]
    public void ACrateWithSomeoneStandingByItIsNeverTaken()
    {
        var n = new Night(4, speed: 0);
        var (crate, pickers, _) = Stage(n, count: 3);
        n.Crew[1] = Ground(n, crate.Centre + new Double3(2.5, 0, 0), toward: crate.Centre);
        Run(n, 30);
        Assert.True(Present(n, crate));
        Assert.Equal(-1, crate.TakenBy);
        Assert.Equal(0, n.World.PickersTook);
        Assert.All(pickers, p => Assert.Null(p.Crate));
    }

    [Fact]
    public void AHeavyCrateTakesTwo()
    {
        var n = new Night(4, speed: 0);
        var (crate, pickers, drain) = Stage(n, heavy: 0.6);
        var was = crate.Centre;
        Run(n, 10);
        // One alone has it up, but goes nowhere with it.
        Assert.Equal(pickers[0].Id, crate.TakenBy);
        Assert.True(Flat(crate.Centre - was) < 1.5, $"moved {Flat(crate.Centre - was):0.0} m alone");
        // A second comes up: the two of them carry it off down the drain.
        n.World.AddEnemy(id => Picker.Up(id, drain, 0, n.Train.Dynamics.Distance, 0, K));
        Run(n, 25);
        Assert.False(Present(n, crate));
        Assert.Equal(1, n.World.PickersTook);
    }

    [Fact]
    public void ComeRightUpToOneCarryingAndItBitesNeverKillsAndDropsIt()
    {
        var n = new Night(4, speed: 0);
        var (crate, pickers, _) = Stage(n);
        Run(n, 4);
        var picker = pickers[0];
        Assert.Equal(picker.Id, crate.TakenBy);
        // Someone steps right up to it: bitten, and it lets go.
        var facing = new Double3(-DMath.Sin(picker.Lateral), 0, -DMath.Cos(picker.Lateral));
        n.Crew[1] = Ground(n, picker.Local + facing * 0.9, toward: picker.Local);
        int health = n.Crew[1].Health;
        Run(n, 0.5);
        Assert.Equal(-1, crate.TakenBy);
        Assert.True(n.Crew[1].Health < health, "bitten");
        Assert.True(n.Crew[1].Alive);
        Assert.Contains(picker.Mode, new[] { PickerMode.Bite, PickerMode.Scatter, PickerMode.Down });
        // However long they stand there, a bite never kills.
        n.Crew[1] = n.Crew[1] with { Health = 1 };
        Run(n, 20);
        Assert.True(n.Crew[1].Alive);
    }

    [Fact]
    public void ABlowKillsOneAndTheRestNearScatterToTheirDrains()
    {
        var n = new Night(4, speed: 0);
        var (crate, pickers, _) = Stage(n, count: 3);
        n.World.Bodies.SpawnCargo(Beside(n, 2, 15, 3), n.Train.Dynamics.Distance);
        Run(n, 4);
        var carrier = pickers.First(p => p.Crate is { } c && n.World.Bodies.All.Any(b => b.Id == c && b.TakenBy == p.Id));
        // A crewmate at its back, out of its bite, swings.
        var at = carrier.Local;
        var facing = new Double3(-DMath.Sin(carrier.Lateral), 0, -DMath.Cos(carrier.Lateral));
        n.Crew[2] = Ground(n, at - facing * 1.4, toward: at);
        Run(n, 0.3, id => id == 2 ? new PlayerIntent { Actions = PlayerActions.Swing } : default);
        Assert.True(carrier.Gone);
        Assert.All(n.World.Bodies.All.Where(b => b.Kind == BodyKind.Cargo), b => Assert.Equal(-1, b.TakenBy));
        Assert.All(pickers.Where(p => !p.Gone && Flat(p.Local - at) <= K.ScatterWithin),
            p => Assert.Contains(p.Mode, new[] { PickerMode.Scatter, PickerMode.Down }));
    }

    [Fact]
    public void TheTrainGoingSendsThemAllDown()
    {
        var n = new Night(4, speed: 0);
        var (_, pickers, _) = Stage(n, count: 2);
        Run(n, 2);
        foreach (var p in pickers)
            p.Leave();
        Run(n, 10);
        Assert.All(pickers, p => Assert.True(p.Gone));
    }

    [Fact]
    public void TheSameNightTwiceIsTheSame()
    {
        static string Play()
        {
            var n = new Night(4, speed: 0);
            var (crate, pickers, _) = Stage(n, count: 3);
            n.World.Bodies.SpawnCargo(Beside(n, 1, 13), n.Train.Dynamics.Distance);
            n.World.Bodies.SpawnCargo(Beside(n, 3, 16), n.Train.Dynamics.Distance, 0.6);
            Run(n, 25);
            return string.Join(";", pickers.Select(p => $"{p.Mode}:{p.Local.X:0.000},{p.Local.Z:0.000}")) + $"|{n.World.PickersTook}";
        }
        Assert.Equal(Play(), Play());
    }
}
