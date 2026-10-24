using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// ARCHITECTURE §8 note 504: a Follower aboard drops off its host's back to the floor under them (App. A.6 "drops off
/// aboard") before it crawls to its car, once they're inside a car (note 1016), falling in the host's car (so a moving train carries it), and still nests.
/// </summary>
public class FollowerDropTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    public void ItDropsOffItsHostsBackToTheirFeetThenCrawlsAndNests(double speed)
    {
        var t = Tuning.Enemies.Followers;
        var n = new Night(4, speed);
        // Inside car 1 (note 1016: on its roof it rides on).
        var room = n.Train.Frames[1].Shape.Interior!.Value;
        n.Crew[2] = PlayerMotor.SpawnOnRoof(n.Train, 1, 0, Tuning.Player) with { Position = new Double3(-0.6, room.Min.Y + 0.1, 0), Surface = Surface.Deck };
        var f = n.World.AddEnemy(id => Follower.On(id, n.Train, n.Crew[2], 2, t));
        for (int i = 0; i < 6000 && f.Phase != SpinePhase.Commit; i++)
            n.Run(Sim.SimConstants.TickSeconds);
        Assert.Equal(SpinePhase.Commit, f.Phase);
        // It lets go in its host's car, at their back.
        int car = n.Crew[2].Parent;
        Assert.Equal(car, f.Attached);
        var from = f.Local;
        double feet = n.Crew[2].Position.Y;
        Assert.InRange(from.Y - feet, Follower.BackUp - 0.05, Follower.BackUp + 0.05);
        // Half way through its drop it's in the air, straight down from where it let go, still in the car.
        n.Run(Follower.DropSeconds(t) * 0.5);
        Assert.Equal(car, f.Attached);
        Assert.InRange(f.Local.Y, feet + 0.05, from.Y - 0.05);
        Assert.Equal(from.X, f.Local.X, 6);
        Assert.Equal(from.Z, f.Local.Z, 6);
        // Down by the end of it, on the floor under them.
        n.Run(Follower.DropSeconds(t) * 0.5 + 0.1);
        double y = f.Attached == car ? f.Local.Y : n.Train.Frames[car].ToLocal(f.WorldPosition(n.Train)).Y;
        Assert.InRange(y, feet - 0.02, feet + 0.02);
        for (int s = 0; s < 400 && !f.Nested && !f.Gone; s++)
            n.Run(1);
        Assert.True(f.Nested, $"{f.Phase}");
    }

    [Fact]
    public void ItsDropTakesAboutHalfASecond() => Assert.InRange(Follower.DropSeconds(Tuning.Enemies.Followers), 0.45, 0.6);
}

/// <summary>
/// Note 1016 (the director's Look Review verdict of 23 Oct on the Follower: "It often spawns outside the car I've noticed
/// like on the stairs to the door or on couplings"): it lets go only once its host is inside a car's body, never on the
/// side door's steps, the coupling or the roof, and it crawls car to car inside the train (each car's frame, through the
/// end doors), never left out in the world beside a moving train.
/// </summary>
public class FollowerInsideTests
{
    static PlayerState Inside(TrainOnLine train, int car, double z)
    {
        var room = train.Frames[car].Shape.Interior!.Value;
        return PlayerMotor.SpawnOnRoof(train, car, z, Tuning.Player) with { Position = new Double3(-0.6, room.Min.Y + 0.1, z), Surface = Surface.Deck };
    }

    [Fact]
    public void ItRidesOnWhileItsHostStandsOnTheStepsAndDropsOnceTheyreInside()
    {
        var n = new Night(4, 0);
        var shape = n.Train.Frames[2].Shape;
        var steps = shape.Solids.Where(s => s.Part == PartKind.Steps).MaxBy(s => s.Box.Max.Y)!.Box;
        n.Crew[2] = PlayerMotor.SpawnOnRoof(n.Train, 2, 0, Tuning.Player) with { Position = steps.Centre with { Y = steps.Max.Y }, Surface = Surface.Deck };
        var f = n.World.AddEnemy(id => Follower.On(id, n.Train, n.Crew[2], 2, Tuning.Enemies.Followers));
        n.Run(15);
        Assert.Equal(2, f.Carrier);
        Assert.NotEqual(SpinePhase.Commit, f.Phase);
        // Onto the coupling's plate behind the car: still riding.
        var plate = shape.Solids.First(s => s.Part == PartKind.Coupler).Box;
        n.Crew[2] = n.Crew[2] with { Position = plate.Centre with { Y = plate.Max.Y } };
        n.Run(5);
        Assert.Equal(2, f.Carrier);
        // In through the door: it drops, in the car's body.
        n.Crew[2] = Inside(n.Train, 2, 1.0);
        for (int i = 0; i < 200 && f.Phase != SpinePhase.Commit; i++)
            n.Run(Sim.SimConstants.TickSeconds);
        Assert.Equal(SpinePhase.Commit, f.Phase);
        Assert.Equal(2, f.Attached);
        Assert.True(shape.Interior!.Value.Contains(f.Local), $"{f.Local}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    public void ItCrawlsCarToCarInsideTheTrainToItsNest(double speed)
    {
        var n = new Night(5, speed);
        // The loot's in car 4; the host comes aboard into car 1.
        foreach (var v in n.Train.Vehicles)
            if (v.Id != 4)
                v.Load = 0;
        n.Crew[2] = Inside(n.Train, 1, 0);
        var f = n.World.AddEnemy(id => Follower.On(id, n.Train, n.Crew[2], 2, Tuning.Enemies.Followers));
        var seen = new HashSet<int>();
        for (int i = 0; i < 400 * Sim.SimConstants.TickRate && !f.Nested && !f.Gone; i++)
        {
            n.Run(Sim.SimConstants.TickSeconds);
            if (f.Phase != SpinePhase.Commit)
                continue;
            Assert.True(f.Attached >= 0, $"out in the world at {f.WorldPosition(n.Train)}, tick {i}");
            var room = n.Train.Frames[f.Attached].Shape.Interior!.Value;
            Assert.True(room.Contains(f.Local), $"outside car {f.Attached}'s body at {f.Local}, tick {i}");
            seen.Add(f.Attached);
        }
        Assert.True(f.Nested, $"{f.Phase}");
        Assert.Equal(4, f.Attached);
        Assert.Equal([1, 2, 3, 4], seen.Order());
    }
}
