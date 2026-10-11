using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.LineGen;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Monsters brought into town (queue #322, ARCHITECTURE §8 note 589; the director, 9 Oct 2026: "It also encourages players to
/// plan a little extra time at the end of the run to stop the train, clear the monsters, and then bring it in"): a creature
/// still aboard as its car comes in through a town terminus's gate is fought in the yard a while, and the night pays the
/// town's monster brigade for it; a train cleared before the gate pays nothing.
/// </summary>
public class BroughtInTests
{
    static readonly string Content = DataFile.FindContentRoot();

    sealed class Night
    {
        public readonly World World;
        public readonly Route.Route Route;
        public readonly PlayerState Driver;
        public readonly CinderHound? Hound;

        /// <summary>Frontier:7's train rolling in at yard speed, 60 m short of its town terminus's gate, a hound on car 2's roof or none.</summary>
        public Night(bool hound)
        {
            Route = Routes.Generate(Content, "frontier:7", 4);
            double gate = Route.Plan!.Terminus.GateM;
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 4, 1)), Route.Build(), gate - 60, Tuning.Boiler);
            train.Dynamics.Velocity = 2.5;
            World = new World(train, Tuning.Combat);
            World.EnableBodies();
            World.EnableRun(Tuning.Run, Route, Route.GateOr(Tuning.Route.YardLength), authority: true);
            World.Run!.Resume(1200, -1, train.Boiler.Tender, 0);
            World.EnableEnemies(HoundRunTests.Quiet, Route, 1, crew: 1, authority: true);
            Driver = PlayerMotor.SpawnInCab(train, Tuning.Player);
            if (!hound)
                return;
            int car = train.Dynamics.Consist.Vehicles[2].Id;
            var shape = train.Frames[car].Shape;
            Hound = World.AddEnemy(e => new CinderHound(e, 900) { Health = HoundRunTests.Quiet.CinderHounds.Health });
            Hound.Restore(SpinePhase.Telegraph, 0, HoundRunTests.Quiet.CinderHounds.Health, car, new Double3(0, shape.RoofHeight, 0), 0, 0, 0, 900, 0);
        }

        /// <summary>On in at yard speed until the whole train's home inside the gate, then stopped there, and the night over.</summary>
        public RunReport Bring(List<double>? houndSeen = null)
        {
            var run = World.Run!;
            for (int t = 0; t < 240 * SimConstants.TickRate && !run.Over; t++)
            {
                World.BeginTick();
                bool home = World.Train.Dynamics.Distance >= run.HomeFront(World.Train) + 2;
                if (!home && World.Train.Dynamics.Velocity < 2.5)
                    World.Train.Dynamics.Velocity = 2.5;
                World.Step(new TrainControls { Reverser = 1, Brake = home ? 1 : 0 });
                World.StepRun([Driver]);
                if (Hound is { Gone: false })
                    houndSeen?.Add(run.Seconds);
            }
            return Assert.IsType<RunReport>(run.Report);
        }
    }

    [Fact]
    public void ACreatureRiddenInIsFoughtInTheYardAndTheNightPaysTheTownsBrigade()
    {
        var clear = new Night(hound: false).Bring();
        var seen = new List<double>();
        var night = new Night(hound: true);
        var brought = night.Bring(seen);
        Assert.Equal(RunEnd.Delivered, clear.End);
        Assert.Equal(RunEnd.Delivered, brought.End);

        // Brought in once, off the car it rode in on, and fought in the yard about the tuned while before it's gone.
        var b = Assert.Single(night.World.Run!.BroughtIn);
        Assert.Equal(EnemyKind.CinderHound, b.Kind);
        Assert.True(night.Hound!.Gone);
        double fought = seen[^1] - b.Seconds;
        Assert.InRange(fought, Tuning.Run.BroughtIn.FightSeconds - 0.2, Tuning.Run.BroughtIn.FightSeconds + 0.2);

        // The brigade's fee off the night's pay, the town's hurt, and the clerk's line saying so; a cleared train pays none.
        double perCar = Tuning.Run.Economy.PerCar["frontier"];
        double fee = Math.Round(Tuning.Run.BroughtIn.Fee * perCar);
        Assert.True(fee > 0);
        Assert.Equal(1, brought.BroughtIn);
        Assert.Equal(fee, brought.BroughtInFees);
        Assert.Equal(Tuning.Run.BroughtIn.Hurt, brought.TownHurt);
        Assert.Equal(0, clear.BroughtIn);
        Assert.Equal(0, clear.BroughtInFees);
        var line = Assert.Single(brought.Lines, l => l.Kind == IncidentKind.BroughtIn);
        Assert.Contains($"brought into {night.Route.Plan!.Terminus.Name} on car {b.Car}", line.Text);
        Assert.Equal(fee, line.Fee);
        Assert.DoesNotContain(clear.Lines, l => l.Kind == IncidentKind.BroughtIn);
        // The fee comes straight off the net. (What the fight in the yard spoiled or broke is the night's, as anything's: here
        // some of car 2's freight.)
        Assert.Equal(brought.Gross - brought.CoalCost - brought.AmmoCost - brought.RepairCost - fee, brought.Net, 0);
        Assert.True(brought.Net <= clear.Net - fee);
    }

    [Fact]
    public void OnlyInThroughATownsGateIsACreatureBroughtIn()
    {
        // Short of the town's gate, a creature in a fort is driven off at once, as ever: nothing's brought in. Past it, it is.
        var night = new Night(hound: true);
        var run = night.World.Run!;
        Assert.Equal(0, run.BringIn(night.Hound!, night.Route.Plan!.Terminus.GateM - 1));
        Assert.Empty(run.BroughtIn);
        Assert.True(run.BringIn(night.Hound!, night.Route.Plan.Terminus.GateM + 1) > 0);
        Assert.Single(run.BroughtIn);
    }
}
