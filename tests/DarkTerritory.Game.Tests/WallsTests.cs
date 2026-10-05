using Ballast;
using DarkTerritory.Game.Sound;
using DarkTerritory.Sim.Rail;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// The train's walls between an ear and a sound (spec A.5 "car walls −12 dB", A.7 "occlusion via raycast against car
/// geometry"; note 248): a wall in the way occludes, an open door, the open hatch or a breach in the way doesn't, and a
/// car with something open leaks round it.
/// </summary>
public class WallsTests
{
    static readonly string Content = DataFile.FindContentRoot();
    static readonly TrainTuning T = DataFile.Load<TrainTuning>(Path.Combine(Content, TrainTuning.File));
    static readonly WallsTuning W = DataFile.Load<WallsTuning>(Path.Combine(Content, WallsTuning.File));

    static TrainOnLine Train() =>
        new(new TrainDynamics(Consist.Uniform(T, 4, 1)), new RailLine(new LineDefinition("t", [new TrackSegment(20_000)])), 5_000);

    /// <summary>A car with a room and a side door, its room's middle, and a point a few metres out of that door (all world).</summary>
    static (int Car, Door Door, Double3 Inside, Double3 OutOfTheDoor, Double3 OtherSide) Car(TrainOnLine train)
    {
        for (int i = 1; i < train.Frames.Count; i++)
        {
            var shape = train.Frames[i].Shape;
            if (shape.Interior is not { } room)
                continue;
            // A side door: in a side wall (its middle at the room's edge in X).
            foreach (var door in shape.DoorList)
            {
                var c = door.Box.Centre;
                double side = Math.Sign(c.X - room.Centre.X);
                if (Math.Abs(Math.Abs(c.X - room.Centre.X) - room.HalfSize.X) > 0.3)
                    continue;
                var f = train.Frames[i];
                var inside = room.Centre with { Y = c.Y };
                return (i, door, f.ToWorld(inside), f.ToWorld(c + new Double3(side * 3, 0, 0)), f.ToWorld(inside + new Double3(-side * (room.HalfSize.X + 3), 0, 0)));
            }
        }
        throw new InvalidOperationException("no car with a side door");
    }

    [Fact]
    public void InTheSameRoomNothingsBetween()
    {
        var train = Train();
        var (car, _, inside, _, _) = Car(train);
        var room = train.Frames[car].Shape.Interior!.Value;
        var corner = train.Frames[car].ToWorld(room.Min + new Double3(0.3, 0.5, 0.3));
        Assert.Equal(0, Walls.Between(train, inside, corner, W));
    }

    [Fact]
    public void AShutCarsWallsAreAWallFromOutsideAndFromInside()
    {
        var train = Train();
        var (_, _, inside, outside, _) = Car(train);
        Assert.Equal(1, Walls.Between(train, outside, inside, W));
        Assert.Equal(1, Walls.Between(train, inside, outside, W));
    }

    [Fact]
    public void ThroughAnOpenDoorItsClearAndRoundTheOtherSideItLeaks()
    {
        var train = Train();
        var (car, door, inside, outside, otherSide) = Car(train);
        train.Vehicles[car].ToggleDoor(door.Index);
        // In line with the door: nothing between.
        Assert.Equal(0, Walls.Between(train, outside, inside, W));
        // From the far side of the car, through the wall, with the door open across the room: a leaky wall, not a whole one.
        Assert.Equal((float)W.OpenWall, Walls.Between(train, otherSide, inside, W), 3);
    }

    [Fact]
    public void ABreachIsAHoleInTheWall()
    {
        var train = Train();
        var (car, _, inside, _, otherSide) = Car(train);
        var f = train.Frames[car];
        // The hole where the far side's line meets the wall (Vehicle.BreachAt is in the car's frame).
        var a = f.ToLocal(otherSide);
        var b = f.ToLocal(inside);
        var room = f.Shape.Interior!.Value;
        double wallX = a.X > room.Centre.X ? room.Max.X : room.Min.X;
        var hole = a + (b - a) * ((wallX - a.X) / (b.X - a.X));
        train.Vehicles[car].Breach(hole);
        Assert.Equal(0, Walls.Between(train, otherSide, inside, W));
    }

    [Fact]
    public void FromOneShutCarIntoAnotherItsWalls()
    {
        var train = Train();
        var rooms = Enumerable.Range(1, train.Frames.Count - 1).Where(i => train.Frames[i].Shape.Interior is not null).Take(2).ToArray();
        Assert.Equal(2, rooms.Length);
        Double3 Middle(int i) => train.Frames[i].ToWorld(train.Frames[i].Shape.Interior!.Value.Centre);
        Assert.Equal(1, Walls.Between(train, Middle(rooms[0]), Middle(rooms[1]), W));
    }

    [Fact]
    public void OnTheRoofOfTheCarYouAreInItsTheRoof()
    {
        var train = Train();
        var (car, _, inside, _, _) = Car(train);
        var room = train.Frames[car].Shape.Interior!.Value;
        var roof = train.Frames[car].ToWorld(room.Centre with { Y = room.Max.Y + 0.6 });
        Assert.Equal(1, Walls.Between(train, inside, roof, W));
    }
}
