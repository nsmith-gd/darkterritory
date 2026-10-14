using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 653 (queue #383, D1's ask after #371): the cars a dropped pin parts from the train (note 356) are gone for and
/// coupled back on, ahead of the engine or behind it. On note 634's seed 7 the pin behind a switchyard's pair, carried ahead
/// of the engine (note 187), dropped at 14.5 m/s; the pair ran on ahead, nobody went after them, and they were lost.
/// </summary>
public class PartedCarsTests
{
    static readonly EnemyTuning E = Tuning.Enemies;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly LooseTuning L = DataFile.Load<UpkeepTuning>(Path.Combine(DataFile.FindContentRoot(), UpkeepTuning.File)).Coupling;
    /// <summary>The director quiet (its grace never over): nothing comes.</summary>
    static readonly EnemyTuning Quiet = E with { Director = E.Director with { GraceMinSeconds = 1e9, GraceMaxSeconds = 1e9 } };

    /// <summary>A driver in the cab of six cars at <paramref name="speed"/>, the couplings working; <paramref name="order"/> the engine's rake front to back.</summary>
    static (Night N, ConductorBot Driver) Running(double speed, int[]? order = null)
    {
        var n = new Night(6, speed: 0, enemies: Quiet);
        if (order is not null)
            Assert.True(n.Train.Resume([new RakeState(order, n.Train.Dynamics.Distance, 0, 1, false, false, RailLine.MainPath)]));
        n.Train.Dynamics.Velocity = speed;
        n.Train.Loose = L;
        n.Crew[1] = PlayerMotor.SpawnInCab(n.Train, P);
        return (n, new ConductorBot(null, 0));
    }

    /// <summary>The driver at the controls as the host works them from its intent; stops early once <paramref name="until"/>.</summary>
    static void Drive(Night n, ConductorBot driver, double seconds, Func<bool>? until = null)
    {
        for (int i = 0; i < seconds * SimConstants.TickRate && !(until?.Invoke() ?? false); i++)
        {
            driver.Crewmates = [];
            var intent = driver.Decide(n.Crew[1], n.World, n.World.Tick, out _);
            if (CabControls.Clears(n.Controls, n.Train, CabControls.ReleasesBrake(intent, n.Crew[1], n.Train)))
                n.Controls.Brake = 0;
            CabControls.Apply(ref n.Controls, intent, n.Crew[1], n.Train);
            n.Run(SimConstants.TickSeconds, _ => intent, holdSpeed: false);
        }
    }

    /// <summary>Drops the pin behind <paramref name="car"/> on the next steps, and runs till it has.</summary>
    static void Drop(Night n, ConductorBot driver, int car)
    {
        n.Train.Vehicles[car].Loose = L.PartAfter - 0.05;
        Drive(n, driver, 5, until: () => n.Train.TrainRakes == 2);
        Assert.Equal(2, n.Train.TrainRakes);
    }

    static string Where(Night n, ConductorBot driver, string step) =>
        $"step {driver.PinStep ?? step}, rev {n.Controls.Reverser}, rakes " + string.Join(" ", n.Train.Rakes.Select(r => $"({string.Join(",", r.Consist.Vehicles.Select(v => v.Id))})@{r.Distance:0}v{r.Velocity:0.0}"));

    [Fact]
    public void CarsAheadOfTheEngineThatAPinPartsAreRunAfterAndCoupledOn()
    {
        // Seed 7's: a switchyard's pair ahead of the engine, the pin behind them dropped at speed. They roll on ahead; the
        // engine goes after them and couples on, and runs on with them.
        var (n, driver) = Running(14, [5, 6, 0, 1, 2, 3, 4]);
        Drive(n, driver, 20);
        Drop(n, driver, 6);
        Assert.True(n.Train.RakeOf(5).Distance > n.Train.Dynamics.Distance);
        string step = "";
        Drive(n, driver, 240, until: () =>
        {
            step = driver.PinStep ?? step;
            return n.Train.TrainRakes == 1 && n.Train.Dynamics.Speed > 4;
        });
        Assert.True(n.Train.TrainRakes == 1, Where(n, driver, step));
        Assert.Equal([5, 6, 0, 1, 2, 3, 4], n.Train.Dynamics.Consist.Vehicles.Select(v => v.Id));
        Assert.True(n.Train.Dynamics.Velocity > 4, $"at {n.Train.Dynamics.Velocity:0.0} m/s ({step})");
        Assert.Equal("after the cars ahead", step);
    }

    [Fact]
    public void CarsBehindThatAPinPartsAreBackedOntoAndTheTrainRunsOnWithThem()
    {
        // A pin in the train's own cars, dropped at speed: the cars behind it are left. The driver stands, backs onto them,
        // and goes on with them, forward.
        var (n, driver) = Running(12);
        Drive(n, driver, 20);
        Drop(n, driver, 3);
        bool backed = false;
        Drive(n, driver, 240, until: () =>
        {
            backed |= driver.PinStep == "back for the cars behind";
            return n.Train.TrainRakes == 1 && n.Train.Dynamics.Velocity > 4;
        });
        Assert.True(backed, "never went back for them: " + Where(n, driver, ""));
        Assert.True(n.Train.TrainRakes == 1, Where(n, driver, ""));
        Assert.Equal(7, n.Train.Dynamics.Consist.Vehicles.Count);
        Assert.True(n.Train.Dynamics.Velocity > 4, $"at {n.Train.Dynamics.Velocity:0.0} m/s");
    }
}
