using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 437 (queue #173): burns on a hot run. A boarded pack keeps setting its car alight (note 269), and on frontier:7's
/// 8-bot express night three walkers burned with no hot box ever caught: sent in to fight car 10's fire while the pack on its
/// roof kept it going. A fire under a pack isn't one to fight from inside; the fit go at the pack.
/// </summary>
public class PackFireTests
{
    static readonly PlayerTuning P = Tuning.Player;

    static (bool In, PlayerState Walker) Night(bool pack, int health, bool onIt = false)
    {
        var n = new Night(4, speed: 8);
        int rear = n.Train.Dynamics.Consist.Vehicles[^1].Id;
        var fire = n.World.AddEnemy(id => CarFire.In(id, n.Train, rear, 0, Tuning.Enemies.CarFire).Ablaze(0.3, 2));
        if (pack)
        {
            var shape = n.Train.Frames[rear].Shape;
            for (int i = 0; i < 2; i++)
            {
                var h = n.World.AddEnemy(e => new CinderHound(e, 900) { Health = Tuning.Enemies.CinderHounds.Health });
                h.Restore(SpinePhase.Commit, 0, Tuning.Enemies.CinderHounds.Health, rear, new Double3(i == 0 ? 0.6 : -0.6, shape.RoofHeight, shape.HalfLength - 2.5), 0, 0, 0, 900, 0);
            }
        }
        var bot = new RoofWalkerBot(3, P.Cold) { Me = 1 };
        // Cold, as everyone was on frontier:7 that night; onIt: already on the burning car's roof, at its front end.
        n.Crew[1] = (onIt ? PlayerMotor.SpawnOnRoof(n.Train, rear, -n.Train.Frames[rear].Shape.HalfLength + 1, P)
            : PlayerMotor.SpawnOnRoof(n.Train, n.Train.VehicleAhead(rear), 0, P)) with { Health = health, Cold = P.Cold.OnsetSeconds };
        bool inside = false;
        for (int i = 0; i < 40 && !inside; i++)
        {
            bot.Crew = [(1, n.Crew[1])];
            n.Run(0.5, id => bot.Decide(n.Crew[id], n.World, n.World.Tick, out _));
            inside |= n.Crew[1].Parent == rear && n.Crew[1].Surface == Surface.Deck;
        }
        return (inside, n.Crew[1]);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(50)] // hurt past the pack fight (Heed.PackFightHealth), still fit to fight a fire
    public void AFireUnderABoardedPackIsntFoughtFromInside(int health)
    {
        var (inside, s) = Night(pack: true, health);
        Assert.False(inside, $"in under the pack to fight its fire (walker {s.Parent} {s.Surface} hp {s.Health})");
    }

    [Theory]
    [InlineData(100)]
    [InlineData(50)]
    public void OnTheBurningCarsRoofAlreadyAWalkerStillDoesntGoInUnderThePack(int health)
    {
        // The frontier:7 walker: on car 10's roof, cold, the pack at its far end; down onto the plate and in to the fire.
        var (inside, s) = Night(pack: true, health, onIt: true);
        Assert.False(inside, $"in under the pack to fight its fire (walker {s.Parent} {s.Surface} hp {s.Health})");
    }

    [Fact]
    public void AFireWithNoPackAboardIsFoughtFromInside()
    {
        var (inside, s) = Night(pack: false, 50, onIt: true);
        Assert.True(inside, $"never went in to fight it (walker {s.Parent} {s.Surface} hp {s.Health})");
    }
}
