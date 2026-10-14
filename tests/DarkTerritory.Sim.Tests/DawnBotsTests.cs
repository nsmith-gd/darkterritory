using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;
using DarkTerritory.Sim.Train;
using RunState = DarkTerritory.Sim.Run.Run;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 607 (queue #339; the director, 9 Oct: "the picking up of the train, especially with you in it ... And then the tension
/// before it is really, really important"): the bots know dawn. From the dawn line, the Wakers coming, a hand comes in off the
/// ground and goes forward to the cab, the last thing they eat; one in the last car when it's lifted goes forward too.
/// </summary>
public class DawnBotsTests
{
    static readonly PlayerTuning P = Tuning.Player;
    static readonly EnemyTuning E = Tuning.Enemies;
    static readonly WakersTuning W = E.Wakers;
    static readonly EnemyTuning Quiet = E with { Director = E.Director with { GraceMinSeconds = 1e9, GraceMaxSeconds = 1e9 } };
    // Dawn 3 s in: the dawn line is up from the first tick (wakers.stirSeconds before it).
    static readonly Route.Route Frontier = RouteGenerator.Generate(Tuning.Route, RouteTier.Frontier, 7) with { DawnSeconds = 3 };

    /// <summary>A standing train out on the line at dawn: the driver in the cab, a walker (a bot) where it's put.</summary>
    sealed class Dawn
    {
        public readonly World World;
        public readonly RunState Run;
        public PlayerState Driver, Walker;
        public readonly RoofWalkerBot Bot = new(7, P.Cold) { Me = 2 };
        readonly TrainControls _controls = new() { Reverser = 1, Brake = 1 };

        public Dawn(Func<TrainOnLine, PlayerState> walker)
        {
            var train = new TrainOnLine(new TrainDynamics(Consist.Uniform(Tuning.Train, 6, 1)), Frontier.Build(), 4000, Tuning.Boiler);
            World = new World(train, Tuning.Combat);
            World.EnableRun(Tuning.Run, Frontier, 600, authority: true);
            World.EnableEnemies(Quiet, null, 1, crew: 2, authority: true);
            Run = World.Run!;
            Driver = PlayerMotor.SpawnInCab(train, P);
            Walker = walker(train);
        }

        public TrainOnLine Train => World.Train;
        public Waker? Lead => World.ActiveEnemies.OfType<Waker>().FirstOrDefault(w => w.Lead);

        public void Step(double seconds)
        {
            for (int i = 0; i < seconds * SimConstants.TickRate && !Run.Over; i++)
            {
                var intent = Walker.Alive ? Bot.Decide(Walker, World, World.Tick, out _) : default;
                World.BeginTick();
                World.CrewAct(ref Driver, default, 1);
                World.CrewAct(ref Walker, intent, 2);
                World.Step(_controls);
                World.ApplyDamage(id => id == 1 ? Driver : Walker, (id, s) => { if (id == 1) Driver = s; else Walker = s; }, [1, 2]);
                PlayerMotor.Step(ref Driver, default, Train, P, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
                PlayerMotor.Step(ref Walker, intent, Train, P, Tuning.Train, SimConstants.TickSeconds, applyLook: false);
                World.StepRun([Driver, Walker]);
            }
        }
    }

    [Fact]
    public void OnTheGroundAtTheDawnLineItsAboardAndForwardBeforeTheyCatchTheTrain()
    {
        var n = new Dawn(train =>
        {
            int car = train.Dynamics.Consist.Vehicles[^2].Id;
            var t = train.Line.Sample(Rail.RailLine.MainPath, train.Cars[car].FrontDistance - 6);
            return PlayerMotor.SpawnOnGround(t.Position + Double3.Cross(t.Tangent, Double3.Up).Normalized * 2.8, train.Line, train.Cars[car].FrontDistance, P);
        });
        n.Step(0.5);
        Assert.True(RoofWalkerBot.AtDawn(n.World));
        double inHand = Waker.SecondsInHand(n.World);
        var path = new List<string>();
        for (double s = 0; s < inHand - 1; s += 0.5)
        {
            n.Step(0.5);
            string at = $"{n.Walker.Surface}@{n.Walker.Parent}";
            if (path.Count == 0 || path[^1] != at)
                path.Add(at);
        }
        Assert.False(n.Lead?.Holds ?? false);
        // Aboard, and up front: in the cab, or on the engine or the car ahead of it.
        Assert.True(n.Walker.Parent >= 0, string.Join(" > ", path));
        Assert.True(n.Train.Dynamics.Consist.IndexOf(n.Walker.Parent) <= 1, $"car {n.Walker.Parent}: {string.Join(" > ", path)}");
    }

    /// <summary>Inside a car, on its floor in the aisle, mid-car.</summary>
    static PlayerState InCar(TrainOnLine train, int car) => new()
    {
        Parent = car,
        Surface = Surface.Deck,
        Position = new Double3(train.Dynamics.Tuning.Geometry.Interior!.DoorX, train.Dynamics.Tuning.Geometry.Interior.FloorHeight, 0),
        Health = P.Health,
        LineHint = train.Cars[car].FrontDistance,
    };

    [Fact]
    public void InTheLastCarWhenItsLiftedItGoesForward()
    {
        // Inside it, where nothing else would move it (a walker in a car stays in it, out of the cold).
        var n = new Dawn(train => InCar(train, train.Dynamics.Consist.Vehicles[^1].Id));
        int last = n.Train.Dynamics.Consist.Vehicles[^1].Id;
        // Held there till the Waker takes the train (as if it had been busy at the back), then let go.
        while (!(n.Lead?.Holds ?? false) && n.Run.Seconds < 600)
        {
            n.Walker = InCar(n.Train, last);
            n.Step(0.2);
        }
        Assert.True(n.Lead!.Holds);
        n.Step(W.LiftSeconds * 2);
        Assert.True(n.Walker.Alive, $"{n.Walker.Death}");
        Assert.NotEqual(last, n.Walker.Parent);
        Assert.True(n.Train.Dynamics.Consist.IndexOf(n.Walker.Parent) < n.Train.Dynamics.Consist.IndexOf(last), $"on {n.Walker.Parent}");
    }
}
