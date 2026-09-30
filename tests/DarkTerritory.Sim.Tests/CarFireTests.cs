using Ballast;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Physics;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Sim.Tests;

/// <summary>Fire and firefighting (GDD v1.1 App. C.5): every car's extinguisher, off its mount and sprayed, puts a fire out.</summary>
public class CarFireTests
{
    static readonly PlayerTuning P = Tuning.Player;

    [Fact]
    public void TakeTheCarsExtinguisherOffItsMountAndSprayTheFireOut()
    {
        var n = new Night(4, speed: 8);
        n.World.MountExtinguishers();
        int car = 2;
        var room = n.Train.Frames[car].Shape.Interior!.Value;
        var ext = Assert.Single(n.World.Bodies.All, b => b.Kind == BodyKind.Extinguisher && b.Parent == car);
        Assert.Equal(car, ext.Home);
        var fire = n.World.AddEnemy(id => CarFire.In(id, n.Train, car, 2, Tuning.Enemies.CarFire));
        // Stood by the extinguisher: Use takes it.
        n.Crew[1] = new PlayerState { Parent = car, Position = ext.Centre with { Y = room.Min.Y, Z = ext.Centre.Z + 0.5 }, Surface = Surface.Deck, Health = P.Health, Pitch = -0.6 };
        n.Run(0.2, _ => new PlayerIntent { Buttons = PlayerButtons.Use });
        n.Run(0.2);
        Assert.Equal(1, ext.Carrier);
        // Beside the fire, facing it, Fire held: it's knocked back until it's out.
        n.Crew[1] = n.Crew[1] with { Position = new Double3(room.Centre.X, room.Min.Y, fire.Local.Z + 1.2), Yaw = 0 };
        n.Run(Tuning.Enemies.CarFire.ChargeSeconds, _ => new PlayerIntent { Buttons = PlayerButtons.Fire });
        Assert.True(fire.Gone, $"{fire.Phase} at {fire.Extra:0.00}");
        Assert.True(ext.Charge < 1);
    }
}
