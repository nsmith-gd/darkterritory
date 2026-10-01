using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// T106 (bots brake and vent as a pair): with steam driving (T97) pressure is speed, so a board ahead wants the gauge down,
/// not just the brake held against the steam (held, it fades). The driver brakes; the fireman goes out to the blow-off on the
/// left running board and vents it down to what the board allows, then comes back in.
/// </summary>
public class VentTests
{
    static readonly TrainTuning T = Tuning.Train;
    static readonly PlayerTuning P = Tuning.Player;
    static readonly SightTuning S = DataFile.Load<SightTuning>(Path.Combine(DataFile.FindContentRoot(), SightTuning.File));

    [Fact]
    public void ForABoardAheadTheFiremanVentsWhileTheDriverBrakes()
    {
        // Cruising at 14 m/s on a straight with a weak bridge's board (7 m/s) a kilometre on, and a long way past it.
        var route = new Route.Route("test", RouteTier.Frontier, 1, new LineDefinition("test", [new TrackSegment(8000)]),
            [new RouteFeature(FeatureKind.Bridge, 1500, 2700, MaxCars: 10)], new RouteWeather(0.01, false, 0, 0), 3600);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 3, 1)), route.Build(), 500, Tuning.Boiler);
        var world = new World(train);
        world.EnableLineside(S, route);
        train.Dynamics.Velocity = 14;
        train.Boiler.Pressure = Boiler.PressureFor(Tuning.Boiler, 15, train.Dynamics.Tuning.MaxSpeed);
        var calls = new CrewCalls();
        var driver = new ConductorBot(calls, 0);
        var fireman = new ConductorBot(calls, 1) { Fireman = true };
        var d = PlayerMotor.SpawnInCab(train, P);
        var f = d with { Position = d.Position + new Double3(-1.2, 0, 0) };
        var c = new TrainControls { Reverser = 1 };
        bool vented = false, driverOut = false, back = false;
        for (uint tick = 0; tick < 240 * SimConstants.TickRate && train.Dynamics.Distance < 2600; tick++)
        {
            var di = driver.Decide(d, world, tick, out _);
            var fi = fireman.Decide(f, world, tick, out _);
            if (CabControls.Clears(c, train, CabControls.ReleasesBrake(di, d, train) || CabControls.ReleasesBrake(fi, f, train)))
                c.Brake = 0;
            CabControls.Apply(ref c, di, d, train);
            CabControls.Apply(ref c, fi, f, train);
            world.BeginTick();
            world.CrewAct(ref d, di, 1);
            world.CrewAct(ref f, fi, 2);
            world.Step(c);
            PlayerMotor.Step(ref d, di, train, P, T, SimConstants.TickSeconds, applyLook: false);
            PlayerMotor.Step(ref f, fi, train, P, T, SimConstants.TickSeconds, applyLook: false);
            vented |= train.Boiler.Vented;
            driverOut |= !PlayerMotor.InCab(d, train);
            back |= vented && PlayerMotor.InCab(f, train) && !fireman.Venting;
        }
        Assert.True(vented, "the fireman blew it down");
        Assert.False(driverOut, "the driver stayed at the controls");
        Assert.True(f.Alive && back, $"and the fireman came back in ({f.Position})");
        Assert.False(world.Derailed, world.DerailCause);
        // On the span, the gauge is down near what makes its speed, so the steam isn't fighting the brake.
        double target = Boiler.PressureFor(Tuning.Boiler, S.WeakBridgeLimit, train.Dynamics.Tuning.MaxSpeed);
        Assert.True(train.Boiler.Pressure < target + 12, $"pressure {train.Boiler.Pressure:0} for a {S.WeakBridgeLimit} m/s board (makes it at {target:0})");
        Assert.InRange(train.Dynamics.Speed, 3, S.WeakBridgeLimit + S.LurchOver);
    }
}
