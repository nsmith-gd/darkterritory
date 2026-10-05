using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// The Deep sweep (T81, ARCHITECTURE §8 note 228): with steam driving (T97) the engine pulls against the brake while its steam
/// would make more than the train's speed, so the brake alone, faded on a descent (spec B.5), can't take the speed off. The
/// bots vent as well. And a walker stepping off a roof into a gap does it over the plate.
/// </summary>
public class SteamAgainstTheBrakeTests
{
    static readonly PlayerTuning P = Tuning.Player;

    /// <summary>A lone driver (a crew under the fireman's size) in the cab of ten cars on a straight at a grade.</summary>
    sealed class Alone
    {
        public readonly World World;
        public readonly TrainOnLine Train;
        public readonly ConductorBot Driver = new(new CrewCalls(), 0);
        public PlayerState Cab;
        TrainControls _controls = new() { Reverser = 1 };

        public Alone(double speed, double pressure, double brakeEfficiency, double gradePercent = 0)
        {
            var line = new RailLine(new LineDefinition("t", [new TrackSegment(40_000, 0, gradePercent)]));
            Train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 10, 1)), line, 2_000, Tuning.Boiler);
            Train.Dynamics.Restore(2_000, speed, brakeEfficiency);
            Train.Boiler.Pressure = pressure;
            Train.Boiler.Firebox = 3;
            World = new World(Train, Tuning.Combat);
            World.EnableEnemies(Tuning.Enemies, null, 1, crew: 2, authority: true);
            Cab = PlayerMotor.SpawnInCab(Train, P);
        }

        /// <summary>The driver deciding, the host working the cab controls from its intent, and the world stepping.</summary>
        public void Drive(double seconds, Func<bool> until)
        {
            for (uint t = 0; t < seconds * SimConstants.TickRate && !until(); t++)
            {
                var intent = Driver.Decide(Cab, World, t, out _);
                if (CabControls.Clears(_controls, Train, CabControls.ReleasesBrake(intent, Cab, Train)))
                    _controls.Brake = 0;
                CabControls.Apply(ref _controls, intent, Cab, Train);
                World.BeginTick();
                World.CrewAct(ref Cab, intent, 1);
                World.Step(_controls);
                PlayerMotor.Step(ref Cab, intent, Train, P, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
            }
        }
    }

    [Fact]
    public void ADriverAloneVentsForTheSleepersAsWellAsBraking()
    {
        // deepTerritory:4: 12 m/s, the gauge at 75 (steam for 16 m/s), the brake faded to 0.45 on the descent before. Braking
        // alone it lost under 1 m/s from the lamp finding them to the Sleepers, and ran onto them at 43 km/h.
        var a = new Alone(speed: 12.2, pressure: 75, brakeEfficiency: 0.45);
        var sleepers = a.World.AddEnemy(id => new Sleepers(id) { LineDistance = a.Train.Dynamics.Distance + 125, Height = 0.2 });
        double atThem = 0;
        a.Drive(30, () =>
        {
            if (a.Train.Dynamics.Distance >= sleepers.LineDistance - 1)
                atThem = Math.Max(atThem, a.Train.Dynamics.Speed);
            return a.World.Derailed || sleepers.Gone;
        });
        Assert.False(a.World.Derailed, a.World.DerailCause);
        Assert.InRange(atThem, 0, Tuning.Enemies.Sleepers.DerailAbove);
        Assert.True(a.Train.Boiler.Pressure < 70, $"the gauge at {a.Train.Boiler.Pressure:0}");
    }

    [Fact]
    public void ADriverAloneVentsWhenTheBrakesFadingDownAGrade()
    {
        // deepTerritory:1: held at 14 m/s down the grade on a brake fading towards its floor, steam for 17.7 m/s; with the
        // brake gone it ran away at 16 m/s and took a 45 km/h bend at 57. (Off a plan the driver lets it run 1.5 over before braking.)
        var a = new Alone(speed: 14, pressure: 80, brakeEfficiency: 0.6, gradePercent: -2);
        double fastest = 0;
        a.Drive(60, () =>
        {
            fastest = Math.Max(fastest, a.Train.Dynamics.Speed);
            return a.World.Derailed;
        });
        Assert.True(fastest < 15.9, $"ran away at {fastest:0.0} m/s, the gauge at {a.Train.Boiler.Pressure:0}, the brake at {a.Train.Dynamics.BrakeEfficiency:0.00}");
        Assert.True(a.Train.Boiler.Pressure < 75, $"the gauge at {a.Train.Boiler.Pressure:0}");
    }

    [Fact]
    public void AWalkerSteppingOffTheRoofIntoAGapSquaresUpOverThePlateFirst()
    {
        // deepTerritory:3: the gunner stepped off the guard van's front end 7 cm right of its middle, and the plate (on the end
        // doors' line, left of centre) ends 5 cm right of it: down to the ballast at 14 m/s, left on 1 hp. At the end of the
        // van's roof, in for a fire in the car ahead, a hand's breadth right of the middle: not off it yet, but over to the plate.
        var n = new Night(10, speed: 14);
        n.World.MountExtinguishers();
        int van = n.Train.Dynamics.Consist.Vehicles[^1].Id;
        var bot = new RoofWalkerBot(3, P.Cold) { Me = 1 };
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, van, -n.Train.Frames[van].Shape.HalfLength + 0.3, P, localX: 0.12);
        n.World.AddEnemy(id => CarFire.In(id, n.Train, van - 1, 0, Tuning.Enemies.CarFire));
        var first = bot.Decide(n.Crew[1], n.World, n.World.Tick, out _);
        Assert.Equal("ToEnd", bot.WarmUpStep);
        Assert.Equal(0, first.MoveZ);
        Assert.True(first.MoveX < 0, $"steering {first.MoveX:0.00} across");
        // And then off it, onto the plate.
        bool onThePlate = false;
        for (int t = 0; t < 10 * SimConstants.TickRate && !onThePlate && (n.Crew[1].Parent != PlayerState.World || n.Crew[1].Surface == Surface.Air); t++)
        {
            n.Run(SimConstants.TickSeconds, id => bot.Decide(n.Crew[id], n.World, n.World.Tick, out _));
            onThePlate = n.Crew[1].Surface == Surface.Coupler;
        }
        Assert.True(onThePlate, $"{n.Crew[1].Surface} on {n.Crew[1].Parent} at {n.Crew[1].Position}, hp {n.Crew[1].Health}");
        Assert.Equal(van - 1, n.Crew[1].Parent);
        Assert.Equal(P.Health, n.Crew[1].Health);
    }
}
