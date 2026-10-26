using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;
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

    [Fact]
    public void TwoOnTheExtinguishersPutARearCarFireOutWellWithinTheOnesTime()
    {
        // Note 851 (the director's playtest of 16 Oct evening, line 5: "The fire in the rear car didn't go out ever despite both
        // of us fighting it"; GDD §12 "one can, two do it faster", note 752): the last car at full blaze, two inside on the
        // extinguishers. Each cools the stretch it's aimed at by the whole rate, so two have it out in about two thirds of one's
        // time (B.11's "Two extinguishers" row), both unburned.
        var one = OneExtinguisher(Tuning.Enemies, rear: true);
        var two = OneExtinguisher(Tuning.Enemies, rear: true, crew: 2);
        Assert.True(one.Out && one.Seconds <= 20, one.ToString());
        Assert.True(two.Out && two.Seconds <= 10, two.ToString());
        Assert.True(two.Seconds <= one.Seconds * 0.8, $"two: {two.Seconds:0.0} s, one: {one.Seconds:0.0} s");
        Assert.True(two.Health > 50, two.ToString());
    }

    /// <summary>
    /// Spec B.11's "Left alone (goods)" row timed again on the 3.5 m cells (note 1032; it was measured on note 267's 1.5 m
    /// cells): a fire set on car 2's floor and left alone, on a 5-car train running at 8 m/s.
    /// </summary>
    [Fact]
    public void LeftAloneAGoodsCarBurnsOnTheSpecsClock()
    {
        // Measured 24 / 47 / 51 / 68 s and burnt out at 181 s, all 16 cells charred (B.11's row).
        var goods = LeftAlone(CargoKind.Goods);
        Assert.True(goods.Alight is >= 20 and <= 30, goods.ToString());
        Assert.True(goods.Roof is >= 40 and <= 55, goods.ToString());
        Assert.True(goods.Half is >= 44 and <= 60, goods.ToString());
        Assert.True(goods.NextCar is >= 58 and <= 80, goods.ToString());
        Assert.True(goods.BurntOut is >= 150 and <= 220, goods.ToString());
        Assert.Equal(goods.Cells, goods.Charred);
        // B.9: coal and chemicals run ahead of it at every step.
        foreach (var cargo in new[] { CargoKind.Coal, CargoKind.Chemicals })
        {
            var faster = LeftAlone(cargo);
            Assert.True(faster.Alight <= goods.Alight && faster.Half <= goods.Half && faster.NextCar <= goods.NextCar, $"{cargo}: {faster}; goods: {goods}");
        }
    }

    internal record Clock(double Alight, double Roof, double Half, double NextCar, double BurntOut, int Charred, int Cells);

    /// <summary>
    /// The seconds from the fire's being set to: alight (a cell at <c>burnFrom</c>), the roof caught (a roof cell at it), half
    /// the car alight at once, the next car taken, and the car burnt out (its fire gone); and the cells it charred.
    /// </summary>
    internal static Clock LeftAlone(CargoKind cargo, double most = 600)
    {
        var n = new Night(5, speed: 8);
        var t = Tuning.Enemies.CarFire;
        n.Train.Vehicles[2].Cargo = cargo;
        var fire = (CarFire)n.World.AddEnemy(id => CarFire.In(id, n.Train, 2, 0, t, FireCause.Set));
        var grid = FireGrid.Of(n.Train, 2, t.CellSize)!;
        double alight = double.NaN, roof = double.NaN, half = double.NaN, next = double.NaN, burnt = double.NaN;
        for (int i = 1; i <= most * SimConstants.TickRate && double.IsNaN(burnt); i++)
        {
            n.Run(SimConstants.TickSeconds);
            double now = i * SimConstants.TickSeconds;
            var heat = fire.Heat;
            if (double.IsNaN(alight) && heat.Any(h => h >= t.BurnFrom))
                alight = now;
            if (double.IsNaN(roof) && Enumerable.Range(0, Math.Min(grid.Count, heat.Length)).Any(c => grid.Face[c] == FireFace.Ceiling && heat[c] >= t.BurnFrom))
                roof = now;
            if (double.IsNaN(half) && heat.Count(h => h >= t.BurnFrom) * 2 >= grid.Count)
                half = now;
            if (double.IsNaN(next) && n.World.ActiveEnemies.OfType<CarFire>().Any(e => e.Attached != 2 && !e.Gone))
                next = now;
            if (fire.Gone)
                burnt = now;
        }
        return new(alight, roof, half, next, burnt, n.Train.Vehicles[2].Char.Count(c => c > 0), grid.Count);
    }

    internal record Result(int Cells, bool Out, double Seconds, int Health, double Charge);

    /// <summary>
    /// One crewmate in car 3 (<paramref name="rear"/>: the last car), an extinguisher in hand, the whole car at full blaze: the
    /// nearest burning cell in reach sprayed, walking to the next. <paramref name="crew"/> more than one: each with their own,
    /// side by side at the car's rear end (note 851).
    /// </summary>
    internal static Result OneExtinguisher(EnemyTuning en, double seconds = 40, bool rear = false, int crew = 1)
    {
        var n = new Night(5, speed: 0, enemies: en);
        var t = en.CarFire;
        int car = rear ? n.Train.Dynamics.Consist.Vehicles[^1].Id : 3;
        var room = n.Train.Frames[car].Shape.Interior!.Value;
        double doorX = Tuning.Train.Geometry.Interior!.DoorX;
        Physics.Body ext = null!;
        for (int k = 1; k <= crew; k++)
        {
            var feet = new Double3(doorX + (k - 1) * 0.5, room.Min.Y, room.Max.Z - 0.6 - (k - 1) * 0.6);
            n.Crew[k] = new PlayerState { Parent = car, Position = feet, Yaw = 0, Surface = Surface.Deck, Health = Tuning.Player.Health };
            var mine = n.World.Bodies.SpawnCrate(n.Train, car, feet with { Y = room.Min.Y + 0.1 }, Physics.BodyKind.Extinguisher);
            mine.Carrier = k;
            mine.Claimed = true;
            ext ??= mine;
        }
        var fire = (CarFire)n.World.AddEnemy(id => CarFire.In(id, n.Train, car, 0, t, FireCause.Set).Ablaze(1.0));
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
        return new(grid.Count, fire.Gone, (n.World.Tick - t0) / (double)SimConstants.TickRate, n.Crew.Values.Min(s => s.Health), ext.Charge);
    }
}
