using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
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

    [Fact]
    public void RunningAwayDownhillOnItsSteamTheFiremanVentsWhileTheDriverBrakes()
    {
        // A long descent: the steam fired for 14 m/s and the grade together run the train over it, and the driver's holding
        // it on the brake (which fades). The fireman blows the gauge down to what makes the speed, then comes back in.
        var line = new LineDefinition("down", [new TrackSegment(400), new TrackSegment(4000, 0, -2.5), new TrackSegment(2000)]);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 6, 1)), new RailLine(line), 300, Tuning.Boiler);
        var world = new World(train);
        train.Dynamics.Velocity = 14;
        train.Boiler.Pressure = Boiler.PressureFor(Tuning.Boiler, 15, train.Dynamics.Tuning.MaxSpeed) + 12;
        double start = train.Boiler.Pressure;
        var calls = new CrewCalls();
        var driver = new ConductorBot(calls, 0);
        var fireman = new ConductorBot(calls, 1) { Fireman = true };
        var d = PlayerMotor.SpawnInCab(train, P);
        var f = d with { Position = d.Position + new Double3(-1.2, 0, 0) };
        var c = new TrainControls { Reverser = 1 };
        bool vented = false, driverOut = false, back = false;
        double fastest = 0;
        for (uint tick = 0; tick < 300 * SimConstants.TickRate && train.Dynamics.Distance < 4600; tick++)
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
            fastest = Math.Max(fastest, train.Dynamics.Speed);
        }
        Assert.True(vented, $"the fireman blew it down (gauge {train.Boiler.Pressure:0} from {start:0}, fastest {fastest:0.0} m/s)");
        Assert.False(driverOut, "the driver stayed at the controls");
        Assert.True(f.Alive && back, $"and the fireman came back in ({f.Position})");
        Assert.True(train.Boiler.Pressure < start, $"gauge {train.Boiler.Pressure:0} from {start:0}");
    }

    [Fact]
    public void WithSomeonePlayingAtTheControlsTheFiremanLeavesTheirSteamAloneTillItsInTheRed()
    {
        // Note 597 (the director's in-game note 2, 9 Oct 2026): the bot vented all the time while they drove. Someone playing
        // holds the controls (note 574), running the same descent hard on the regulator: the speed's theirs, so the fireman
        // doesn't go out and blow their steam down to what it reckons the line allows. Only with the gauge in the red (the
        // safety valve held shut) does it vent, to save the boiler.
        var line = new LineDefinition("down", [new TrackSegment(400), new TrackSegment(4000, 0, -2.5), new TrackSegment(2000)]);
        var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(T, 6, 1)), new RailLine(line), 300, Tuning.Boiler);
        var world = new World(train);
        train.Dynamics.Velocity = 14;
        train.Boiler.Pressure = Boiler.PressureFor(Tuning.Boiler, 15, train.Dynamics.Tuning.MaxSpeed) + 12;
        Assert.True(train.Boiler.Pressure < Tuning.Boiler.Redline);
        var calls = new CrewCalls();
        var fireman = new ConductorBot(calls, 1) { Fireman = true, Me = 2 };
        var d = PlayerMotor.SpawnInCab(train, P);
        var f = d with { Position = d.Position + new Double3(-1.2, 0, 0) };
        var c = new TrainControls { Reverser = 1 };
        world.HoldControls(1, false);
        bool vented = false;
        void Run(double seconds)
        {
            for (int i = 0; i < seconds * SimConstants.TickRate; i++)
            {
                var di = new PlayerIntent { ThrottleNotch = 4 };
                var fi = fireman.Decide(f, world, world.Tick, out _);
                CabControls.Apply(ref c, di, d, train);
                world.BeginTick();
                world.CrewAct(ref d, di, 1);
                world.CrewAct(ref f, fi, 2);
                world.Step(c);
                PlayerMotor.Step(ref d, di, train, P, T, SimConstants.TickSeconds, applyLook: false);
                PlayerMotor.Step(ref f, fi, train, P, T, SimConstants.TickSeconds, applyLook: false);
                vented |= train.Boiler.Vented;
            }
        }
        Run(60);
        Assert.True(train.Dynamics.Speed > 15, $"run hard at {train.Dynamics.Speed:0.0} m/s");
        Assert.False(vented, $"the fireman left their steam alone (gauge {train.Boiler.Pressure:0})");
        Assert.False(fireman.Venting);
        // In the red with the valve held shut: now it vents.
        var b = train.Boiler;
        (b.Pressure, b.SafetyValveJammed) = (Tuning.Boiler.Redline + 3, true);
        train.Boiler = b;
        Run(20);
        Assert.True(vented, $"vented in the red (gauge {train.Boiler.Pressure:0})");
    }
}
