using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using Xunit;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 634 (the director, the inbox, 12 Oct: `D1.4-burning-car-beatable`, "b": "72 patches of fire per car is far too many,
/// make it 15 and have the extinguisher extinguish far more of the fire. A car should be extinguished from full blaze within
/// 20 seconds."): a car is 16 cells, and one extinguisher puts out a car at full blaze in well under 20 s.
/// </summary>
public class FireCellsTests
{
    [Fact]
    public void OneExtinguisherPutsOutACarAtFullBlazeWithinTwentySeconds()
    {
        var r = OneExtinguisher(Tuning.Enemies);
        Assert.Equal(16, r.Cells);
        Assert.True(r.Out && r.Seconds <= 20, r.ToString());
        Assert.True(r.Health > 50, r.ToString());
    }

    internal record Result(int Cells, bool Out, double Seconds, int Health, double Charge);

    /// <summary>One crewmate in car 3, an extinguisher in hand, the whole car at full blaze: the nearest burning cell in reach sprayed, walking to the next.</summary>
    internal static Result OneExtinguisher(EnemyTuning en, double seconds = 40)
    {
        var n = new Night(5, speed: 0, enemies: en);
        var t = en.CarFire;
        int car = 3;
        var room = n.Train.Frames[car].Shape.Interior!.Value;
        double doorX = Tuning.Train.Geometry.Interior!.DoorX;
        n.Crew[1] = new PlayerState { Parent = car, Position = new Double3(doorX, room.Min.Y, room.Max.Z - 0.6), Yaw = 0, Surface = Surface.Deck, Health = Tuning.Player.Health };
        var ext = n.World.Bodies.SpawnCrate(n.Train, car, n.Crew[1].Position with { Y = room.Min.Y + 0.1 }, Physics.BodyKind.Extinguisher);
        ext.Carrier = 1;
        ext.Claimed = true;
        var fire = (CarFire)n.World.AddEnemy(id => CarFire.In(id, n.Train, car, 0, t).Ablaze(1.0));
        n.Run(SimConstants.TickSeconds);
        var grid = FireGrid.Of(n.Train, car, t.CellSize)!;
        double eye = Tuning.Train.Pick.EyeHeight;
        uint t0 = n.World.Tick;
        for (int i = 0; i < seconds * SimConstants.TickRate && !fire.Gone; i++)
        {
            n.Run(SimConstants.TickSeconds, id =>
            {
                var s = n.Crew[id];
                var at = s.Position + Double3.Up * eye;
                int near = -1;
                double best = double.MaxValue;
                for (int c = 0; c < grid.Count && c < fire.Heat.Length; c++)
                    if (fire.Heat[c] > 0 && (grid.Centre[c] - at).Length is var d && d < best)
                        (best, near) = (d, c);
                if (near < 0)
                    return new PlayerIntent();
                var to = grid.Centre[near] - at;
                double yaw = DMath.Atan2(-to.X, -to.Z), pitch = DMath.Atan2(to.Y, Math.Sqrt(to.X * to.X + to.Z * to.Z));
                var look = new PlayerIntent
                {
                    LookYaw = (float)Math.Clamp(Math.IEEERemainder(yaw - s.Yaw, 2 * Math.PI), -0.5, 0.5),
                    LookPitch = (float)Math.Clamp(pitch - s.Pitch, -0.5, 0.5),
                };
                if (best < t.SprayReach - 0.3)
                    return look with { Buttons = PlayerButtons.Fire };
                // Along the aisle toward it.
                var (step, _) = Bots.WarmUp.Steer(s, new Double3(doorX, 0, grid.Centre[near].Z), s.Yaw);
                return step with { LookYaw = look.LookYaw, LookPitch = look.LookPitch };
            });
        }
        return new(grid.Count, fire.Gone, (n.World.Tick - t0) / (double)SimConstants.TickRate, n.Crew[1].Health, ext.Charge);
    }
}
