using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The Brakeman (GDD §21, App. A.4, B.4; the director's brief of 8 Oct 2026; ARCHITECTURE §8 note 364). Rule: chase it alone,
/// catch it together. He winds the cars' handbrakes on car by car (drag that stalls a climb); a lone chaser sends him running
/// and never lands a blow; out of sight he comes up elsewhere; two closing from both sides corner him, and only then do blows
/// land; his lash never kills.
/// </summary>
public class BrakemanTests
{
    static readonly BrakemanTuning B = Tuning.Enemies.Brakeman;
    static readonly PlayerTuning P = Tuning.Player;

    static Brakeman AtTail(Night n) => n.World.AddEnemy(id =>
    {
        var last = n.Train.Cars[^1];
        return Brakeman.Up(id, n.Train, last.FrontDistance - last.Length + 0.5, 1, B);
    });

    static double Along(Night n, int car, double z) => n.Train.Cars[car].FrontDistance - z - n.Train.Cars[car].Length / 2;

    static int Wound(Night n) => n.Train.Vehicles.Count(v => v.Wound);

    [Fact]
    public void HeWindsTheBrakesOnCarByCarTowardTheEngine()
    {
        var n = new Night(6, speed: 10);
        var b = AtTail(n);
        // Up at the tail, along the last car's roof to its wheel, and wound.
        n.Run(B.ClimbSeconds + 14 / B.Walk + B.WindSeconds + 0.5);
        Assert.Equal(1, Wound(n));
        Assert.True(n.Train.Vehicles[6].Wound);
        n.Run(40);
        Assert.True(Wound(n) >= 3, $"{Wound(n)} wound");
        // Toward the engine: the tail's cars first.
        Assert.True(n.Train.Vehicles[5].Wound);
    }

    [Fact]
    public void WoundBrakesDragTheTrainAndStallItOnAClimb()
    {
        var free = new Night(8, speed: 12);
        var wound = new Night(8, speed: 12);
        for (int c = 1; c <= 8; c++)
            wound.Train.Vehicles[c].Wound = true;
        free.Run(20, holdSpeed: false);
        wound.Run(20, holdSpeed: false);
        Assert.True(wound.Train.Dynamics.Speed < free.Train.Dynamics.Speed - 3, $"{wound.Train.Dynamics.Speed:0.0} vs {free.Train.Dynamics.Speed:0.0}");
        // Unwound at its wheel (Use held on the roof by it), a car's drag's gone.
        var s = PlayerMotor.SpawnOnRoof(wound.Train, 3, wound.Train.Frames[3].Shape.HalfLength - 0.5, P);
        var wheel = wound.Train.Frames[3].Shape.Interactables.First(i => i.Kind == InteractableKind.Handbrake);
        wound.Crew[1] = s with { Position = wheel.Position with { Y = s.Position.Y } };
        wound.Run(Tuning.Train.Couplings.HandbrakeSeconds + 0.3, id => new PlayerIntent { Buttons = PlayerButtons.Use });
        Assert.False(wound.Train.Vehicles[3].Wound);
    }

    [Fact]
    public void ALoneChaserSendsHimRunningAndNeverLandsABlow()
    {
        var n = new Night(6, speed: 10);
        var b = AtTail(n);
        n.Run(B.ClimbSeconds + 1);
        int car = b.Attached;
        // Up on his roof, ahead of him, coming at him.
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, car, b.Local.Z - 6, P);
        n.Run(0.5);
        Assert.Equal(BrakemanMode.Flee, b.Mode);
        double health = b.Health;
        // Swinging as he runs: he ducks.
        n.Run(3, id => new PlayerIntent { Actions = PlayerActions.Swing });
        Assert.Equal(health, b.Health);
        // He gets clear and drops out of sight, to come up again elsewhere.
        n.Run(10);
        Assert.Equal(BrakemanMode.Hidden, b.Mode);
        Assert.False(b.Exposed);
        n.Run(B.Hide[1] + B.ClimbSeconds + 1);
        Assert.NotEqual(BrakemanMode.Hidden, b.Mode);
    }

    [Fact]
    public void TwoFromBothSidesCornerHimAndOnlyThenDoBlowsLand()
    {
        var n = new Night(6, speed: 10);
        var b = AtTail(n);
        n.Run(B.ClimbSeconds + 1);
        int car = b.Attached;
        double z = b.Local.Z;
        // One ahead of him and one behind, both within the span, on his roof.
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, car, z - 2.5, P);
        n.Crew[2] = PlayerMotor.SpawnOnRoof(n.Train, car, Math.Min(z + 2.5, n.Train.Frames[car].Shape.HalfLength - 0.3), P);
        n.Run(0.2);
        Assert.Equal(BrakemanMode.Cornered, b.Mode);
        // Facing him, they beat him down.
        var at = b.Local;
        for (int id = 1; id <= 2; id++)
        {
            var d = at - n.Crew[id].Position;
            n.Crew[id] = n.Crew[id] with { Yaw = DMath.Atan2(-d.X, -d.Z) };
        }
        for (int i = 0; i < 6 && !b.Gone; i++)
            n.Run(Tuning.Enemies.Melee.SwingSeconds + 0.05, id => new PlayerIntent { Actions = PlayerActions.Swing });
        Assert.True(b.Gone);
        Assert.Contains(EnemyKind.Brakeman, n.World.Slain);
        Assert.All(n.Crew.Values, s => Assert.True(s.Alive));
    }

    [Fact]
    public void CorneredHeLashesButNeverKills()
    {
        var n = new Night(6, speed: 10);
        var b = AtTail(n);
        n.Run(B.ClimbSeconds + 1);
        int car = b.Attached;
        double z = b.Local.Z;
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, car, z - 2, P) with { Health = 40 };
        n.Crew[2] = PlayerMotor.SpawnOnRoof(n.Train, car, Math.Min(z + 2, n.Train.Frames[car].Shape.HalfLength - 0.3), P) with { Health = 40 };
        n.Run(20);
        Assert.All(n.Crew.Values, s => Assert.True(s.Alive));
        Assert.Contains(n.Crew.Values, s => s.Health < 40);
        n.AssertFair();
    }
}
