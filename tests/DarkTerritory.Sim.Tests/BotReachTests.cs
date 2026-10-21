using Ballast;
using DarkTerritory.Sim.Bots;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Tests;

/// <summary>
/// Note 945 (the director's playtest of 20 Oct evening, line 17: "The bot is just standing on top of a car ... trying to hit one
/// of the little tick-looking things. But he's standing on top of the car, and it's on the outside of the car, so that's
/// certainly not going well"): a bot's swing (<see cref="Heed.Strike"/>) measured only the flat, so from a roof it swung at
/// what was down at the wheels or in the car under it, a swing that can never land. Now out of a swing's reach above or below,
/// it doesn't swing from there.
/// </summary>
public class BotReachTests
{
    static readonly PlayerTuning P = Tuning.Player;

    [Fact]
    public void FromARoofNoSwingAtWhatsDownAtTheWheels()
    {
        var n = new Night(4, speed: 0);
        const int car = 2;
        var frame = n.Train.Frames[car];
        var bot = PlayerMotor.SpawnOnRoof(n.Train, car, 3, P);
        // A Hotbox's place: out at the car's side, down at the axle box, almost under the bot's feet on the flat.
        var wheels = frame.ToWorld(new Double3(frame.Shape.HalfWidth - 0.2, 0.6, 3.5));
        Assert.Null(Heed.Strike(bot, n.Train, wheels, 1.6));
        // Up on the roof with it, as ever: a swing.
        var onTheRoof = frame.ToWorld(new Double3(0.4, frame.Shape.RoofHeight, 3.8));
        Assert.Equal(PlayerActions.Swing, Heed.Strike(bot, n.Train, onTheRoof, 1.6)!.Value.Actions);
    }

    [Fact]
    public void ABotOnTheRoofDoesntSwingAtATippyToesieInTheCarUnderIt()
    {
        // Heed.Backs goes for a Tippy Toesie after us or within 8 m; in the car under the roof, a swing through the roof.
        var n = new Night(4, speed: 0);
        const int car = 2;
        var room = n.Train.Frames[car].Shape.Interior!.Value;
        n.Crew[1] = PlayerMotor.SpawnOnRoof(n.Train, car, 0, P);
        var tippy = n.World.AddEnemy(id => TippyToesie.Hiding(id, Tuning.Enemies.TippyToesie));
        tippy.Restore(SpinePhase.Telegraph, 1, tippy.Health, car, new Double3(0, room.Min.Y, 0.5), 0, 0, 0, 1, 0);
        var intent = Heed.Backs(default, n.Crew[1], n.World, 1, n.World.Tick);
        Assert.NotEqual(PlayerActions.Swing, intent.Actions);
        // In the car with it, it's gone for, as ever.
        n.Crew[1] = new PlayerState { Parent = car, Position = new Double3(0, room.Min.Y, 1.6), Surface = Surface.Deck, Health = P.Health };
        Assert.Equal(PlayerActions.Swing, Heed.Backs(default, n.Crew[1], n.World, 1, n.World.Tick).Actions);
    }
}
