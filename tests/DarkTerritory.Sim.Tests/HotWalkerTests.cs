using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 380 (queue #117): walkers who live through a hot run. The express driver (note 376) keeps the hounds' runs coming and
/// the Car Hugger's grip holds a car under them, so a car can be held and boarded both: the Hugger's rule keeps the crew off it,
/// the pack's sends the fit at it, and on frontier:3 the walkers went back and forth between the two while the pack mauled the
/// gunner. Both go with the car cut loose (App. A.3).
/// </summary>
public class HotWalkerTests
{
    static readonly PlayerTuning P = Tuning.Player;

    [Fact]
    public void AWalkerCutsLooseACarTheCarHuggerHoldsWithAPackAboard()
    {
        var n = new Night(6, speed: 8);
        int rear = n.Train.Dynamics.Consist.Vehicles[^1].Id, ahead = n.Train.VehicleAhead(rear);
        // The pack's ground runs a car ahead of the car it's on (note 472): the cut's ahead of that, and both cars go.
        int kept = n.Train.VehicleAhead(ahead);
        var hugger = n.World.AddEnemy(id => CarHugger.Lurking(id, n.Train.Dynamics.RearDistance + 1, 1, Tuning.Enemies.CarHugger));
        n.Run(0.5);
        Assert.True(hugger.Latched);
        var shape = n.Train.Frames[rear].Shape;
        var pack = new List<CinderHound>();
        for (int i = 0; i < 3; i++)
        {
            var h = n.World.AddEnemy(e => new CinderHound(e, 900) { Health = Tuning.Enemies.CinderHounds.Health });
            h.Restore(SpinePhase.Commit, 0, Tuning.Enemies.CinderHounds.Health, rear, new Double3(i % 2 == 0 ? 0.6 : -0.6, shape.RoofHeight, shape.HalfLength - 2.5 - i * 0.8), 0, 0, 0, 900, 0);
            pack.Add(h);
        }
        var bot = new RoofWalkerBot(5, P.Cold, new StopHand(StopJob.None, new CrewCalls(), 1, P.Cold)) { Me = 1 };
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, P);
        int cars = n.Train.Dynamics.Consist.Vehicles.Count;
        for (int i = 0; i < 60 && n.Train.Dynamics.Consist.Vehicles.Count == cars; i++)
        {
            bot.Crew = [(1, n.Crew[1])];
            n.Run(0.5, id => bot.Decide(n.Crew[id], n.World, n.World.Tick, out _));
        }
        var s = n.Crew[1];
        Assert.True(s.Alive, $"died of {s.Death}");
        Assert.True(n.Train.Dynamics.Consist.Vehicles.Count < cars, $"never cut it: walker on {s.Parent} {s.Surface}");
        Assert.Equal(kept, n.Train.Dynamics.Consist.Vehicles[^1].Id);
        n.Run(1);
        Assert.All(pack, h => Assert.True(h.Gone));
    }

    [Theory]
    [InlineData(15, true)]
    [InlineData(1, false)]
    public void DownALadderAtSpeedAWalkerHoldsOnAboveTheBallast(double speed, bool holds)
    {
        // frontier:3's hot run: a walker went down car 9's ladder at 21 m/s and the motor stepped it off the bottom rung
        // onto the ballast (dead of the landing). Faster than anyone runs, the last rungs stay climbed; slow, it's off.
        var n = new Night(4, speed);
        int car = 2;
        var shape = n.Train.Frames[car].Shape;
        var ladder = shape.Ladders.First(l => l.Foot.Y <= 0);
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, car, 0, P) with { Position = ladder.Foot with { Y = 2 }, Surface = Surface.Ladder };
        n.Run(4, id => RoofWalkerBot.HoldOn(n.Crew[id], n.Train, new PlayerIntent { MoveZ = -1 }));
        var s = n.Crew[1];
        Assert.True(s.Alive, $"died of {s.Death}");
        Assert.Equal(holds, s.Surface == Surface.Ladder && s.Parent == car);
    }

    [Fact]
    public void UpACarsRearLadderWithTheCarBehindItGoneAClimberTopsOutOntoTheRoof()
    {
        // Note 438 (queue #174): frontier:6's hot run. The forward gunner went down car 9's rear end ladder to grease its hot
        // box, car 10 was lost behind it, and the last car has no rear end ladder (there's no gap to go down to). The motor
        // climbed it by the nearest ladder left, a side ladder facing across: over the top sideways, past the car's end, onto
        // nothing at 20 m/s, and dead of the landing. The car's end face is still in its hands: over the end onto the roof.
        var n = new Night(4, 20.4);
        int car = 3;
        var ladder = n.Train.Frames[car].Shape.Ladders.Single(l => l.Foot.Z > n.Train.Frames[car].Shape.HalfLength);
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, car, 0, P) with { Position = ladder.Foot with { Y = 2.5 }, Surface = Surface.Ladder, Yaw = Math.PI };
        Assert.True(n.Train.Uncouple(car));
        Assert.DoesNotContain(n.Train.Frames[car].Shape.Ladders, l => l.Foot.Z > n.Train.Frames[car].Shape.HalfLength);
        // Facing back down the train, as the gunner was, Use held: up, and then nothing more asked of the legs.
        n.Run(1.2, _ => new PlayerIntent { MoveZ = 1, Buttons = PlayerButtons.Use });
        n.Run(2);
        var s = n.Crew[1];
        Assert.True(s.Alive, $"died of {s.Death}");
        Assert.Equal((Surface.Roof, car), (s.Surface, s.Parent));
        Assert.True(Math.Abs(s.Position.Z) < n.Train.Frames[car].Shape.HalfLength, $"on the roof's end at z {s.Position.Z:0.00}");
    }

    [Theory]
    [InlineData(15, true)]
    [InlineData(1, false)]
    public void WalkingAtAnOpenSideDoorAtSpeedACrewmateStaysIn(double speed, bool stays)
    {
        // frontier:6's hot run: the gunner went across car 2 to shut a side door left open at 20 m/s, walked on into the
        // doorway and out of it, and died of the landing. Faster than anyone runs, nothing more outward from the doorway.
        var n = new Night(4, speed);
        int car = 2;
        var shape = n.Train.Frames[car].Shape;
        int door = StopHand.SideDoor(shape, 1)!.Value;
        n.Train.Vehicles[car].ToggleDoor(door);
        var (at, yaw) = WarmUp.Inside(shape, door);
        n.Crew[1] = new PlayerState { Parent = car, Position = at with { Y = Tuning.Train.Geometry.Interior!.FloorHeight }, Yaw = yaw, Surface = Surface.Deck, Health = P.Health, LineHint = n.Train.Cars[car].FrontDistance };
        n.Run(3, id => RoofWalkerBot.HoldOn(n.Crew[id], n.Train, new PlayerIntent { MoveZ = 1 }));
        var s = n.Crew[1];
        Assert.True(s.Alive, $"died of {s.Death}");
        Assert.Equal(stays, s.Parent == car && s.Surface == Surface.Deck);
    }
}
