using Ballast;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The mine head's steam lift (queue #105, ARCHITECTURE §8 note 368; spec D.2: "requires the locomotive coupled nearby and
/// venting pressure to power it. 2 crew. Ties loading directly to the boiler; you're at zero pressure while it runs").
/// </summary>
public class SteamLiftTests
{
    static readonly FacilityTuning F = FacilityTests.F;
    static readonly PlayerIntent Hold = new() { Buttons = PlayerButtons.Use };
    static readonly PlayerIntent Vent = new() { Actions = PlayerActions.Vent };

    [Fact]
    public void TheMineHeadHasASteamLiftBesideItsWinch()
    {
        // Two 2-person modules at one stop (spec D.3: "someone is alone somewhere"), the tipple (note 423), and the crates for
        // when nothing runs.
        Assert.Equal([ModuleKind.Lift, ModuleKind.Tipple, ModuleKind.Winch, ModuleKind.Crates], F.ModulesOf(FacilityKind.MineHead));
        var stop = new FacilityTests.Stop(FacilityKind.MineHead);
        Assert.True(stop.Site.Has(ModuleKind.Lift));
        Assert.Equal(F.Lift.Ore, stop.Site.Ore, 6);
        // Its chute is over the track, its lever and headframe off it, none of them in the winch's sled run or on the crates.
        var spur = stop.Train.Line.Branches[stop.Site.Spur];
        Assert.InRange(stop.Site.LiftAlong, 0, spur.End - spur.Toe);
        foreach (var p in new[] { stop.Site.LiftLever, stop.Site.Headframe })
            Assert.All(stop.Site.CrateStack, c => Assert.True(((c - p) with { Y = 0 }).Length > 3, $"{p} is on the crate stack"));
    }

    /// <summary>A mine head with the first cargo car under the chute, a hand on the ground at the lever and a driver in the cab.</summary>
    static (FacilityTests.Stop Stop, List<Vehicle> Cars) AtTheLift(int under = 0)
    {
        var stop = new FacilityTests.Stop(FacilityKind.MineHead);
        var cars = stop.Train.Dynamics.Consist.Vehicles.Where(v => v.Kind == VehicleKind.Cargo).ToList();
        stop.StandAt(stop.FrontFor(cars[under].Id, stop.Site.LiftAlong));
        stop.Crew.Add(stop.OnTheGround(stop.Site.LiftLever - Double3.Up * 0.9));
        stop.Crew.Add(PlayerMotor.SpawnInCab(stop.Train, Tuning.Player));
        return (stop, cars);
    }

    [Fact]
    public void ItWindsOnlyWithAHandOnTheLeverAndTheEngineVentingIntoIt()
    {
        var (stop, cars) = AtTheLift();
        var run = stop.World.Run!;
        var site = stop.Site;
        Assert.Same(cars[0], run.CarUnderChute(stop.Train, site));
        Assert.True(run.EngineInReach(stop.Train, site));
        Assert.Same(site, run.LiftLeverInReach(stop.Crew[0], stop.Train));
        // The lever alone: no steam, nothing.
        stop.Step(2, [Hold, default]);
        Assert.False(site.Winding);
        Assert.Equal(0, site.Wind);
        // The vent alone: the steam's into the air.
        double before = stop.Train.Boiler.Pressure;
        stop.Step(1, [default, Vent]);
        Assert.False(site.Winding);
        Assert.Equal(0, site.Wind);
        Assert.True(stop.Train.Boiler.Pressure < before - 5, "venting didn't cost pressure");
        // Both: it winds, it's loud, and every skip wound tips its ore into the car under the chute.
        stop.Train.Boiler.Pressure = 90;
        stop.Step(0.5, [Hold, Vent]);
        Assert.True(site.Winding);
        Assert.True(run.Machinery);
        Assert.True(site.Wind > 0);
        stop.Step(F.Lift.SteamPerSkip / Tuning.Boiler.VentRate, [Hold, Vent]);
        Assert.Equal(F.Lift.PerSkip, cars[0].Load, 6);
        Assert.Equal(CargoKind.Ore, cars[0].Cargo);
        Assert.Equal(F.Lift.Ore - F.Lift.PerSkip, site.Ore, 6);
        Assert.All(cars.Skip(1), c => Assert.Equal(0, c.Load));
    }

    [Fact]
    public void TheBoilerPaysForEverySkipAndTheLiftStopsWhenItsEmpty()
    {
        // "You're at zero pressure while it runs": with the fire out, the steam in the boiler is all the skips there are.
        var (stop, cars) = AtTheLift();
        stop.Train.Boiler.Firebox = 0;
        stop.Train.Boiler.Pressure = 2.5 * F.Lift.SteamPerSkip;
        stop.Step(20, [Hold, Vent]);
        Assert.Equal(2 * F.Lift.PerSkip, cars[0].Load, 6);
        Assert.True(stop.Train.Boiler.Pressure < 1, $"the boiler kept {stop.Train.Boiler.Pressure:0.0}");
        Assert.False(stop.Site.Winding);
        // The engine can't pull off on what's left (boiler.json powerFloor).
        Assert.True(stop.Train.Boiler.Pressure < Tuning.Boiler.PowerFloor);
        // Fired again, it winds again.
        stop.Train.Boiler.Pressure = 60;
        stop.Step(F.Lift.SteamPerSkip / Tuning.Boiler.VentRate + 0.2, [Hold, Vent]);
        Assert.Equal(3 * F.Lift.PerSkip, cars[0].Load, 6);
    }

    [Fact]
    public void WithTheGaugeAtNothingItWindsAtTheFiresPace()
    {
        // "You're at zero pressure while it runs": vented with a full fire, the gauge sits at nothing and the lift takes all
        // the fire makes; the train can't pull off, and the moment the vent's shut the pressure comes back.
        var (stop, cars) = AtTheLift();
        var bt = Tuning.Boiler;
        stop.Train.Boiler.Pressure = 0;
        stop.Train.Boiler.Firebox = bt.FireboxCapacity;
        double ore = stop.Site.Ore;
        stop.Step(30, [Hold, Vent]);
        Assert.True(stop.Train.Boiler.Pressure < 1, $"the gauge read {stop.Train.Boiler.Pressure:0.0}");
        double skips = (ore - stop.Site.Ore) / F.Lift.PerSkip + stop.Site.Wind;
        // A standing fire, full: (firebox / fireTimeConstant) x idleDraft units a second, steamPerUnit each. Unfed, it burns down
        // (to about 0.78 of that over the 30 s), and the engine's own heating and auxiliaries are served first (about a third).
        double made = bt.FireboxCapacity / bt.FireTimeConstant * bt.IdleDraft * bt.SteamPerUnit * 30;
        Assert.InRange(skips * F.Lift.SteamPerSkip, made * 0.4, made);
        Assert.True(cars[0].Load > 0);
        stop.Step(5, [Hold, default]);
        Assert.True(stop.Train.Boiler.Pressure > 3, "shut, and the pressure didn't come back");
    }

    [Fact]
    public void OnlyAnEngineNearItsSteamLineWindsIt()
    {
        // "The locomotive coupled nearby": the first three cargo cars come under the chute with the engine in reach.
        for (int k = 0; k < 3; k++)
        {
            var (near, _) = AtTheLift(k);
            Assert.True(near.World.Run!.EngineInReach(near.Train, near.Site), $"car {k} under the chute, and the engine out of reach");
        }
        // The engine further off: lever held, vent open, nothing winds and the ore stays down.
        var (stop, _) = AtTheLift();
        var spur = stop.Train.Line.Branches[stop.Site.Spur];
        stop.StandAt(Math.Min(spur.End - 0.5, spur.Toe + stop.Site.LiftAlong + F.Lift.SteamReach + Tuning.Train.Geometry.EngineLength / 2 + 5));
        Assert.False(stop.World.Run!.EngineInReach(stop.Train, stop.Site));
        stop.Crew[1] = PlayerMotor.SpawnInCab(stop.Train, Tuning.Player);
        stop.Train.Boiler.Pressure = 90;
        stop.Step(4, [Hold, Vent]);
        Assert.False(stop.Site.Winding);
        Assert.Equal(F.Lift.Ore, stop.Site.Ore, 6);
    }

    [Fact]
    public void WithNoCarUnderTheChuteTheOreGoesOnTheBallast()
    {
        var (stop, cars) = AtTheLift();
        stop.StandAt(stop.FrontFor(cars[0].Id, stop.Site.LiftAlong) + 7);
        stop.Crew[1] = PlayerMotor.SpawnInCab(stop.Train, Tuning.Player);
        Assert.Null(stop.World.Run!.CarUnderChute(stop.Train, stop.Site));
        stop.Train.Boiler.Pressure = 90;
        stop.Step(F.Lift.SteamPerSkip / Tuning.Boiler.VentRate + 0.2, [Hold, Vent]);
        Assert.Equal(F.Lift.Ore - F.Lift.PerSkip, stop.Site.Ore, 6);
        Assert.All(cars, c => Assert.Equal(0, c.Load));
    }

    [Fact]
    public void AClientSeesTheLift()
    {
        var (stop, _) = AtTheLift();
        stop.Train.Boiler.Pressure = 90;
        stop.Step(F.Lift.SteamPerSkip / Tuning.Boiler.VentRate * 1.5, [Hold, Vent]);
        var site = stop.Site;
        Assert.True(site.Winding);
        var client = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 0)), stop.Train.Line, 1000, Tuning.Boiler));
        client.EnableRun(Tuning.Run, stop.World.Run!.Route, 600, authority: false, F);
        var controls = new TrainControls();
        WorldRecords.Apply(WorldRecords.Capture(stop.World, controls, []), client, ref controls, []);
        var mirrored = client.Run!.Sites[site.Index]!;
        Assert.True(mirrored.Winding);
        Assert.Equal(site.Ore, mirrored.Ore, 3);
        Assert.Equal(site.Wind, mirrored.Wind, 3);
        Assert.Equal(site.LiftChute, mirrored.LiftChute);
    }
}
