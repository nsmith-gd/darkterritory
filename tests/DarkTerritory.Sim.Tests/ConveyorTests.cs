using Ballast;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The grain elevator's conveyor line (queue #136, ARCHITECTURE §8 note 400; spec D.2: "start machinery at a powerhouse, then
/// clear jams as they occur. 1 + 1 roaming. Jams every 30–60s; unattended jam stops the line"; D.3: "conveyor fails through
/// attention split — someone has to roam").
/// </summary>
public class ConveyorTests
{
    static readonly FacilityTuning F = FacilityTests.F;
    static readonly PlayerIntent Hold = new() { Buttons = PlayerButtons.Use };

    [Fact]
    public void TheGrainElevatorHasAConveyorBehindItsSpout()
    {
        Assert.Equal([ModuleKind.Spout, ModuleKind.Conveyor], F.ModulesOf(FacilityKind.GrainElevator));
        var stop = new FacilityTests.Stop(FacilityKind.GrainElevator);
        var site = stop.Site;
        Assert.True(site.Has(ModuleKind.Conveyor));
        Assert.Equal(F.Conveyor.Grain, site.Grain, 6);
        Assert.False(site.Running);
        Assert.Equal(-1, site.Jam);
        // Its head over the track a car ahead of the spout, so that with a car under the spout the one ahead of it is under the
        // head: the two load at once.
        var spur = stop.Train.Line.Branches[site.Spur];
        Assert.InRange(site.ConveyorAlong, site.SpoutAlong + 10, spur.End - spur.Toe);
        var cars = stop.Train.Dynamics.Consist.Vehicles.Where(v => v.Kind == VehicleKind.Cargo).ToList();
        stop.StandAt(stop.FrontFor(cars[1].Id, site.SpoutAlong));
        Assert.Same(cars[1], stop.World.Run!.CarUnderSpout(stop.Train, site));
        Assert.Same(cars[0], stop.World.Run.CarUnderHead(stop.Train, site));
        // The belt's low run and its drive house are off the track, on the site's side, clear of the spout's lever.
        foreach (var p in new[] { site.ConveyorTail, site.ConveyorKnee, site.ConveyorStarter })
        {
            Assert.True(Lateral(stop, p) > 3.5, $"{p} is {Lateral(stop, p):0.0} m off the track");
            Assert.True(((p - site.SpoutLever) with { Y = 0 }).Length > 3, $"{p} is on the spout's lever");
        }
        Assert.InRange(site.ConveyorHead.Y - site.ConveyorKnee.Y, 3, 5);
        _ = spur;
    }

    /// <summary>A grain elevator with the first cargo car under the conveyor's head, a hand at the drive house's starter.</summary>
    static (FacilityTests.Stop Stop, List<Vehicle> Cars) AtTheConveyor(int under = 0, Stops.PowerState? power = null)
    {
        var stop = power is { } p ? new FacilityTests.Stop(ModuleKind.Conveyor, p) : new FacilityTests.Stop(FacilityKind.GrainElevator);
        var cars = stop.Train.Dynamics.Consist.Vehicles.Where(v => v.Kind == VehicleKind.Cargo).ToList();
        foreach (var c in cars)
            c.Load = 0;
        stop.StandAt(stop.FrontFor(cars[under].Id, stop.Site.ConveyorAlong));
        stop.Crew.Add(stop.OnTheGround(stop.Site.ConveyorStarter - Double3.Up * 0.9));
        return (stop, cars);
    }

    [Fact]
    public void ItStartsAtItsDriveHouseAndCarriesIntoTheCarUnderItsHead()
    {
        var (stop, cars) = AtTheConveyor();
        var run = stop.World.Run!;
        var site = stop.Site;
        Assert.Same(site, run.StarterInReach(stop.Crew[0], stop.Train));
        // Let go short of starting it, and it starts over.
        stop.Step(F.Conveyor.StartSeconds * 0.6, [Hold]);
        Assert.False(site.Running);
        Assert.True(site.Start > 0);
        stop.Step(0.2, [default]);
        Assert.Equal(0, site.Start);
        stop.Step(F.Conveyor.StartSeconds * 0.6, [Hold]);
        Assert.False(site.Running);
        // Held long enough, it runs: loud, and carrying into the car under its head, nobody needed at it now.
        stop.Step(F.Conveyor.StartSeconds * 0.5, [Hold]);
        Assert.True(site.Running);
        Assert.Null(run.StarterInReach(stop.Crew[0], stop.Train));
        stop.Step(5, [default]);
        Assert.True(site.Carrying);
        Assert.True(run.Machinery);
        Assert.Equal(F.CargoOf(FacilityKind.GrainElevator), cars[0].Cargo);
        Assert.InRange(cars[0].Load, F.Conveyor.PerSecond * 4.5, F.Conveyor.PerSecond * 5.5);
        Assert.Equal(F.Conveyor.Grain - cars[0].Load, site.Grain, 6);
        Assert.All(cars.Skip(1), c => Assert.Equal(0, c.Load));
    }

    [Fact]
    public void WithNoCarWithRoomUnderItsHeadItCarriesNothing()
    {
        var (stop, cars) = AtTheConveyor();
        var site = stop.Site;
        stop.Step(F.Conveyor.StartSeconds + 0.1, [Hold]);
        Assert.True(site.Running);
        // Moved off, nothing comes: the head's gate opens only over a car.
        stop.StandAt(stop.FrontFor(cars[0].Id, site.ConveyorAlong) + 7);
        Assert.Null(stop.World.Run!.CarUnderHead(stop.Train, site));
        double grain = site.Grain;
        stop.Step(10, [default]);
        Assert.False(site.Carrying);
        Assert.Equal(grain, site.Grain, 9);
        Assert.True(site.Running);
        // A full car under it takes nothing either, and isn't strained.
        stop.StandAt(stop.FrontFor(cars[0].Id, site.ConveyorAlong));
        cars[0].Load = 1;
        double integrity = cars[0].Integrity;
        stop.Step(5, [default]);
        Assert.Equal(grain, site.Grain, 9);
        Assert.Equal(integrity, cars[0].Integrity, 9);
    }

    /// <summary>
    /// Starts it and runs it till it jams (as long as the longest wait for one, and a little more), the car under its head kept
    /// empty so it's carrying all the while (a car fills in 25 s, and it only jams while it's carrying).
    /// </summary>
    static void RunTillItJams(FacilityTests.Stop stop)
    {
        stop.Step(F.Conveyor.StartSeconds + 0.1, [Hold]);
        Assert.True(stop.Site.Running);
        var car = stop.World.Run!.CarUnderHead(stop.Train, stop.Site)!;
        for (int i = 0; i < (F.Conveyor.JamEvery[^1] + 2) * SimConstants.TickRate && stop.Site.Jam < 0; i++)
        {
            car.Load = 0;
            stop.Step(SimConstants.TickSeconds, [default]);
        }
        Assert.True(stop.Site.Jam >= 0, "it never jammed");
    }

    [Fact]
    public void AJamStopsItTillSomeoneClearsItBesideIt()
    {
        var (stop, cars) = AtTheConveyor();
        var site = stop.Site;
        RunTillItJams(stop);
        Assert.InRange(site.Jam, F.Conveyor.JamFrom, F.Conveyor.JamTo);
        Assert.True(site.Running);
        // Jammed, it carries nothing.
        double load = cars[0].Load;
        stop.Step(3, [default]);
        Assert.False(site.Carrying);
        Assert.Equal(load, cars[0].Load, 9);
        // Someone beside the jam holding Use clears it, and it carries again.
        stop.Crew.Add(stop.OnTheGround(site.JamAt with { Y = site.JamAt.Y - F.Conveyor.BeltHeight } + Across(stop, site.JamAt) * 0.8));
        Assert.Same(site, stop.World.Run!.JamInReach(stop.Crew[1], stop.Train));
        stop.Step(F.Conveyor.ClearSeconds * 0.5, [default, Hold]);
        Assert.True(site.Jam >= 0);
        stop.Step(F.Conveyor.ClearSeconds * 0.6, [default, Hold]);
        Assert.Equal(-1, site.Jam);
        stop.Step(1, [default, default]);
        Assert.True(site.Carrying);
        Assert.True(cars[0].Load > load);
    }

    [Fact]
    public void AJamLeftTooLongStallsTheDriveAndItHasToBeStartedAgain()
    {
        var (stop, _) = AtTheConveyor();
        var site = stop.Site;
        RunTillItJams(stop);
        stop.Step(F.Conveyor.StallSeconds * 0.9, [default]);
        Assert.True(site.Running);
        stop.Step(F.Conveyor.StallSeconds * 0.2, [default]);
        Assert.False(site.Running);
        Assert.False(stop.World.Run!.Machinery);
        // Cleared, it's still stopped: someone has to go back to the drive house.
        stop.Crew.Add(stop.OnTheGround(site.JamAt with { Y = site.JamAt.Y - F.Conveyor.BeltHeight } + Across(stop, site.JamAt) * 0.8));
        stop.Step(F.Conveyor.ClearSeconds + 0.1, [default, Hold]);
        Assert.Equal(-1, site.Jam);
        stop.Step(2, [default, default]);
        Assert.False(site.Running);
        Assert.False(site.Carrying);
        stop.Step(F.Conveyor.StartSeconds + 0.1, [Hold, default]);
        Assert.True(site.Running);
        stop.Step(0.5, [default, default]);
        Assert.True(site.Carrying);
    }

    /// <summary>
    /// Runs it a while with grain enough for it, an empty car always under its head and a roamer who goes to each jam the moment
    /// it comes and clears it: when each came, in seconds of carrying since the last.
    /// </summary>
    static List<double> JamTimes(int jams)
    {
        var stop = new FacilityTests.Stop(FacilityKind.GrainElevator, F with { Conveyor = F.Conveyor with { Grain = 100 } });
        var site = stop.Site;
        var car = stop.Train.Dynamics.Consist.Vehicles.First(v => v.Kind == VehicleKind.Cargo);
        stop.StandAt(stop.FrontFor(car.Id, site.ConveyorAlong));
        stop.Crew.Add(stop.OnTheGround(site.ConveyorStarter - Double3.Up * 0.9));
        stop.Crew.Add(stop.OnTheGround(site.ConveyorStarter - Double3.Up * 0.9));
        stop.Step(F.Conveyor.StartSeconds + 0.1, [Hold, default]);
        var times = new List<double>();
        double carrying = 0;
        for (int i = 0; i < 600 * SimConstants.TickRate && times.Count < jams; i++)
        {
            car.Load = 0;
            bool jammed = site.Jam >= 0;
            stop.Step(SimConstants.TickSeconds, [default, jammed ? Hold : default]);
            if (site.Carrying)
                carrying += SimConstants.TickSeconds;
            if (!jammed && site.Jam >= 0)
            {
                times.Add(carrying);
                carrying = 0;
                stop.Crew[1] = stop.OnTheGround(site.JamAt with { Y = site.JamAt.Y - F.Conveyor.BeltHeight } + Across(stop, site.JamAt) * 0.8);
            }
        }
        Assert.True(site.Running, "a jam stalled it with the roamer on it");
        return times;
    }

    [Fact]
    public void ItJamsEveryThirtyToSixtySecondsOfCarryingTheSameOnEveryRun()
    {
        var times = JamTimes(6);
        Assert.Equal(6, times.Count);
        Assert.All(times, t => Assert.InRange(t, F.Conveyor.JamEvery[0] - 0.1, F.Conveyor.JamEvery[^1] + 0.1));
        Assert.True(times.Distinct().Count() > 3, "every jam came at the same interval");
        Assert.Equal(times, JamTimes(6));
    }

    [Fact]
    public void ADeadYardStartsNothingTillItsPowerhouseIsGoingAndLowPowerRunsItSlow()
    {
        var (dead, _) = AtTheConveyor(power: Stops.PowerState.Dead);
        dead.Step(F.Conveyor.StartSeconds * 2, [Hold]);
        Assert.False(dead.Site.Running);
        var (low, cars) = AtTheConveyor(power: Stops.PowerState.Low);
        low.Step(F.Conveyor.StartSeconds + 0.1, [Hold]);
        Assert.True(low.Site.Running);
        low.Step(10, [default]);
        Assert.InRange(cars[0].Load, F.Conveyor.PerSecond * F.Power.LowSpeed * 9, F.Conveyor.PerSecond * F.Power.LowSpeed * 11);
    }

    [Fact]
    public void AClientSeesTheConveyor()
    {
        var (stop, _) = AtTheConveyor();
        RunTillItJams(stop);
        var site = stop.Site;
        var client = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 0)), stop.Train.Line, 1000, Tuning.Boiler));
        client.EnableRun(Tuning.Run, stop.World.Run!.Route, 600, authority: false, F);
        var controls = new TrainControls();
        WorldRecords.Apply(WorldRecords.Capture(stop.World, controls, []), client, ref controls, []);
        var mirrored = client.Run!.Sites[site.Index]!;
        Assert.True(mirrored.Running);
        Assert.Equal(site.Grain, mirrored.Grain, 3);
        Assert.Equal(site.Jam, mirrored.Jam, 3);
        Assert.Equal(site.JamAt.X, mirrored.JamAt.X, 2);
        Assert.Equal(site.ConveyorHead, mirrored.ConveyorHead);
    }

    /// <summary>How far a point is off the facility's track (m, flat).</summary>
    static double Lateral(FacilityTests.Stop stop, Double3 p)
    {
        var track = stop.Site.Track;
        double best = double.MaxValue;
        for (double d = 0; d <= track.Length; d += 0.5)
            best = Math.Min(best, ((track.Sample(d).Position - p) with { Y = 0 }).Length);
        return best;
    }

    /// <summary>The flat direction from the jam toward the track: where the roamer stands to clear it.</summary>
    static Double3 Across(FacilityTests.Stop stop, Double3 p)
    {
        var track = stop.Site.Track;
        Double3 nearest = track.Sample(0).Position;
        for (double d = 0; d <= track.Length; d += 0.5)
            if (((track.Sample(d).Position - p) with { Y = 0 }).Length < ((nearest - p) with { Y = 0 }).Length)
                nearest = track.Sample(d).Position;
        return ((nearest - p) with { Y = 0 }).Normalized;
    }
}
