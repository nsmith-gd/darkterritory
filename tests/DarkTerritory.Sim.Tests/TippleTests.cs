using Ballast;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Run;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The mine head's tipple (queue #159, ARCHITECTURE §8 note 423; spec D.2: "Clamp the car, rotate it to load. 1 crew. Bad clamp
/// derails the car on the spur"; D.3: "tipple fails through precision — a sloppy clamp costs you").
/// </summary>
public class TippleTests
{
    static readonly FacilityTuning F = FacilityTests.F;
    static readonly TippleTuning Tp = F.Tipple;
    static readonly PlayerIntent Hold = new() { Buttons = PlayerButtons.Use };

    [Fact]
    public void TheMineHeadHasATippleClearOfItsLiftAndCrates()
    {
        Assert.Equal([ModuleKind.Lift, ModuleKind.Tipple, ModuleKind.Winch, ModuleKind.Crates], F.ModulesOf(FacilityKind.MineHead));
        var stop = new FacilityTests.Stop(FacilityKind.MineHead);
        var site = stop.Site;
        Assert.True(site.Has(ModuleKind.Tipple));
        Assert.Equal(Tp.Ore, site.TippleOre, 6);
        Assert.Equal(-1, site.Clamped);
        // The cradle's on the track a car's pitch behind the lift's chute, so with a car under the chute the one behind it is in the
        // cradle; the bin and its lever are out on the site's side, off the crates, the winch and the lift's lever.
        Assert.Equal(site.LiftAlong - Tp.BehindLift, site.TippleAlong, 6);
        var cars = stop.Train.Dynamics.Consist.Vehicles.Where(v => v.Kind == VehicleKind.Cargo).ToList();
        stop.StandAt(stop.FrontFor(cars[1].Id, site.TippleAlong));
        Assert.Same(cars[1], stop.World.Run!.CarInCradle(stop.Train, site));
        Assert.Same(cars[0], stop.World.Run.CarUnderChute(stop.Train, site));
        Assert.True(Lateral(stop, site.TippleBin) > 5);
        Assert.True(Lateral(stop, site.TippleLever) > 3);
        foreach (var p in site.CrateStack.Concat(site.HeavyStack).Append(site.Capstan).Append(site.LiftLever).Append(site.SledFrom).Append(site.SledTo))
            foreach (var q in new[] { site.Cradle, site.TippleBin with { Y = site.Cradle.Y }, site.TippleLever })
                Assert.True(Flat(p - q) > 3, $"{q} is {Flat(p - q):0.0} m from {p}");
        // Every car of the rake fits in the cradle with the train still on the spur.
        foreach (var car in stop.Train.Dynamics.Consist.Vehicles.Where(v => v.Kind == VehicleKind.Cargo))
        {
            stop.StandAt(stop.FrontFor(car.Id, site.TippleAlong));
            Assert.Same(car, stop.World.Run!.CarInCradle(stop.Train, site));
        }
    }

    /// <summary>A mine head with a cargo car stood in the tipple's cradle (this far off its middle), empty, a hand at the lever.</summary>
    static (FacilityTests.Stop Stop, Vehicle Car) AtTheTipple(double off = 0, int car = 1)
    {
        var stop = new FacilityTests.Stop(FacilityKind.MineHead);
        var cars = stop.Train.Dynamics.Consist.Vehicles.Where(v => v.Kind == VehicleKind.Cargo).ToList();
        foreach (var c in cars)
            c.Load = 0;
        stop.StandAt(stop.FrontFor(cars[car].Id, stop.Site.TippleAlong) + off);
        stop.Crew.Add(stop.OnTheGround(stop.Site.TippleLever - Double3.Up * 0.9));
        return (stop, cars[car]);
    }

    [Fact]
    public void AGoodClampRollsTheCarOverAndTipsOreIntoIt()
    {
        var (stop, car) = AtTheTipple();
        var run = stop.World.Run!;
        var site = stop.Site;
        Assert.Same(car, run.CarInCradle(stop.Train, site));
        Assert.Same(site, run.TippleLeverInReach(stop.Crew[0], stop.Train));
        // Let go short of the clamp shutting, and it starts over.
        stop.Step(Tp.ClampSeconds * 0.6, [Hold]);
        Assert.Equal(-1, site.Clamped);
        Assert.True(site.Clamp > 0);
        stop.Step(0.2, [default]);
        Assert.Equal(0, site.Clamp);
        // Held, it clamps: true, the car stood on the cradle's middle.
        stop.Step(Tp.ClampSeconds + 0.1, [Hold]);
        Assert.Equal(car.Id, site.Clamped);
        Assert.True(site.GoodClamp);
        // Held on, the cradle rolls it over; let go, it holds where it is.
        stop.Step(Tp.RollSeconds * 0.5, [Hold]);
        Assert.InRange(site.Roll, 0.4, 0.55);
        Assert.True(run.Machinery);
        double roll = site.Roll;
        stop.Step(2, [default]);
        Assert.Equal(roll, site.Roll, 9);
        Assert.Equal(0, car.Load);
        // Over at the top, the chute tips its share in; then it rolls back by itself and lets go.
        stop.Step(Tp.RollSeconds * 0.5 + 0.1, [Hold]);
        Assert.True(site.RollingBack);
        Assert.Equal(Tp.PerRoll, car.Load, 6);
        Assert.Equal(F.CargoOf(FacilityKind.MineHead), car.Cargo);
        Assert.Equal(Tp.Ore - Tp.PerRoll, site.TippleOre, 6);
        Assert.False(car.OffRails);
        stop.Step(Tp.BackSeconds + 0.1, [default]);
        Assert.Equal(-1, site.Clamped);
        Assert.Equal(0, site.Roll);
        // Twice over fills it; a full car isn't clamped again.
        stop.Step(Tp.ClampSeconds + Tp.RollSeconds + Tp.BackSeconds + 0.3, [Hold]);
        Assert.Equal(1, car.Load, 6);
        stop.Step(Tp.ClampSeconds + 0.5, [Hold]);
        Assert.Equal(-1, site.Clamped);
        Assert.Equal(Tp.Ore - 2 * Tp.PerRoll, site.TippleOre, 6);
    }

    [Fact]
    public void ACarStoodOffTheCradlesMiddleIsBadlyClampedAndComesOffItsRailsOnTheRoll()
    {
        var (stop, car) = AtTheTipple(off: (Tp.GoodClamp + Tp.Tolerance) / 2);
        var site = stop.Site;
        car.Load = 0.6;
        double integrity = car.Integrity;
        stop.Step(Tp.ClampSeconds + 0.1, [Hold]);
        Assert.Equal(car.Id, site.Clamped);
        Assert.False(site.GoodClamp);
        stop.Step(Tp.RollSeconds * Tp.BadAt * 0.8, [Hold]);
        Assert.False(car.OffRails);
        stop.Step(Tp.RollSeconds * Tp.BadAt * 0.4, [Hold]);
        Assert.True(car.OffRails);
        Assert.Equal(-1, site.Clamped);
        Assert.Equal(0.6 - Tp.Spill, car.Load, 6);
        Assert.Equal(integrity - Tp.DerailDamage, car.Integrity, 6);
        Assert.Equal(Tp.Ore, site.TippleOre, 6);
        Assert.Same(car, stop.World.Run!.OffRailsAt(stop.Train, site));
        // Off its rails it isn't clamped again, and its rake can't be pulled off the spot.
        stop.Step(Tp.ClampSeconds + 0.5, [Hold]);
        Assert.Equal(-1, site.Clamped);
        Assert.True(Pulls(stop) < 0.01, "the rake moved with a car off its rails");
    }

    [Fact]
    public void ARakeWithNoCarOffItsRailsPulls() =>
        Assert.True(Pulls(AtTheTipple().Stop) > 0.5);

    [Fact]
    public void AWrenchPutsACarBackOnItsRailsAndTwoDoItInHalfTheTime()
    {
        foreach (int hands in new[] { 1, 2 })
        {
            var (stop, car) = AtTheTipple(off: Tp.Tolerance * 0.9);
            var site = stop.Site;
            stop.Step(Tp.ClampSeconds + Tp.RollSeconds * Tp.BadAt + 0.2, [Hold]);
            Assert.True(car.OffRails);
            var beside = stop.Train.Frames[car.Id].Origin with { Y = site.Cradle.Y } + Across(stop) * 1.6;
            for (int i = 0; i < hands; i++)
                stop.Crew.Add(stop.OnTheGround(beside + Along(stop) * i) with { Kit = Kit.Of([Tool.Wrench]), HeldSlot = 0 });
            Assert.NotNull(stop.World.Run!.OffRailsInReach(stop.Crew[1], stop.Train));
            // Bare hands don't do it; the work keeps if the wrench comes off it.
            var bare = stop.OnTheGround(beside);
            Assert.False(Repairs.WrenchInHand(bare));
            PlayerIntent[] at = [default, .. Enumerable.Repeat(Hold, hands)], off = [default, .. Enumerable.Repeat(default(PlayerIntent), hands)];
            stop.Step(Tp.RerailSeconds / hands * 0.6, at);
            Assert.True(car.OffRails);
            double done = site.Rerail;
            Assert.InRange(done, Tp.RerailSeconds * 0.55, Tp.RerailSeconds * 0.65);
            stop.Step(2, off);
            Assert.Equal(done, site.Rerail, 9);
            stop.Step(Tp.RerailSeconds / hands * 0.45, at);
            Assert.False(car.OffRails);
            Assert.Equal(0, site.Rerail);
            Assert.True(Pulls(stop) > 0.5, "back on its rails, the rake still won't pull");
        }
    }

    [Fact]
    public void MovingTheTrainWithACarClampedTakesItOffItsRails()
    {
        var (stop, car) = AtTheTipple();
        var site = stop.Site;
        stop.Step(Tp.ClampSeconds + 0.1, [Hold]);
        Assert.Equal(car.Id, site.Clamped);
        Assert.True(site.GoodClamp);
        Pulls(stop, SimConstants.TickRate);
        Assert.True(car.OffRails);
        Assert.Equal(-1, site.Clamped);
    }

    [Fact]
    public void AClientSeesTheTippleAndACarOffItsRails()
    {
        var (stop, car) = AtTheTipple();
        var site = stop.Site;
        stop.Step(Tp.ClampSeconds + Tp.RollSeconds * 0.5, [Hold]);
        var client = new World(new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 0)), stop.Train.Line, 1000, Tuning.Boiler));
        client.EnableRun(Tuning.Run, stop.World.Run!.Route, 600, authority: false, F);
        var controls = new TrainControls();
        WorldRecords.Apply(WorldRecords.Capture(stop.World, controls, []), client, ref controls, []);
        var mirrored = client.Run!.Sites[site.Index]!;
        Assert.Equal(car.Id, mirrored.Clamped);
        Assert.True(mirrored.GoodClamp);
        Assert.Equal(site.Roll, mirrored.Roll, 3);
        Assert.Equal(site.TippleOre, mirrored.TippleOre, 3);
        Assert.Equal(site.Cradle, mirrored.Cradle);
        Assert.False(client.Train.Vehicles[car.Id].OffRails);
        // Moved with it clamped: the client sees it off its rails, the cradle let go.
        Pulls(stop, SimConstants.TickRate);
        WorldRecords.Apply(WorldRecords.Capture(stop.World, controls, []), client, ref controls, []);
        Assert.True(client.Train.Vehicles[car.Id].OffRails);
        Assert.Equal(-1, mirrored.Clamped);
    }

    /// <summary>
    /// Drives the engine back off the buffer stop on full regulator for a while (ticks), brakes off: how far its rake moved (m).
    /// </summary>
    static double Pulls(FacilityTests.Stop stop, int ticks = 3 * SimConstants.TickRate)
    {
        double from = stop.Train.Dynamics.Distance;
        for (int i = 0; i < ticks; i++)
        {
            stop.World.BeginTick();
            stop.World.Step(new TrainControls { Reverser = -1, Throttle = 1 });
            stop.World.StepRun(stop.Crew);
        }
        return Math.Abs(stop.Train.Dynamics.Distance - from);
    }

    static double Flat(Double3 v) => (v with { Y = 0 }).Length;

    /// <summary>How far a point is off the facility's track (m, flat).</summary>
    static double Lateral(FacilityTests.Stop stop, Double3 p)
    {
        var track = stop.Site.Track;
        double best = double.MaxValue;
        for (double d = 0; d <= track.Length; d += 0.5)
            best = Math.Min(best, Flat(track.Sample(d).Position - p));
        return best;
    }

    /// <summary>The flat direction across the track at the cradle, toward the site's side.</summary>
    static Double3 Across(FacilityTests.Stop stop) => ((stop.Site.TippleBin - stop.Site.Cradle) with { Y = 0 }).Normalized;

    /// <summary>The flat direction along the track at the cradle.</summary>
    static Double3 Along(FacilityTests.Stop stop) => Double3.Cross(Across(stop), Double3.Up).Normalized;
}
