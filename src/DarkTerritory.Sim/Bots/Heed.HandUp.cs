using Ballast;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Bots;

public static partial class Heed
{
    /// <summary>
    /// Note 723 (bringing them home: "nobody gets left"): a bot on a moving car with a crewmate playing (not a bot) running
    /// beside it within a hand-up's reach (<see cref="Player.Rescue.HandUpTarget"/>, its aim aside) turns to them and holds Use, standing,
    /// till they're up: the same intent a player's hand-up is. It doesn't go to a door for them; one at a door, a platform or the
    /// steps when they come alongside does it.
    /// </summary>
    public static PlayerIntent HandUp(PlayerIntent intent, in PlayerState self, World world, int selfId, IReadOnlyList<(int Id, PlayerState State)> others,
        CrewCalls? calls, RescueTuning t)
    {
        if (calls is null || !self.Alive || self.Parent < 0 || others.Count == 0)
            return intent;
        var people = others.Where(o => !calls.IsBot(o.Id)).ToList();
        if (Player.Rescue.HandUpTarget(self, selfId, people, world.Train, t, aimed: false) is not { } runner)
            return intent;
        var train = world.Train;
        var eye = PlayerMotor.WorldPosition(self, train) + Double3.Up * 1.5;
        var to = PlayerMotor.WorldPosition(runner.State, train) + Double3.Up * Horseplay.Chest - eye;
        // Faced in its own frame: the world's bearing less the car's heading (PlayerMotor's yaw: 0 faces −Z).
        double yaw = DMath.Atan2(-to.X, -to.Z) - train.Frames[self.Parent].Heading;
        double pitch = DMath.Atan2(to.Y, Math.Sqrt(to.X * to.X + to.Z * to.Z));
        return intent with
        {
            MoveX = 0,
            MoveZ = 0,
            LookYaw = (float)Math.Clamp(Math.IEEERemainder(yaw - self.Yaw, 2 * Math.PI), -0.5, 0.5),
            LookPitch = (float)Math.Clamp(pitch - self.Pitch, -0.5, 0.5),
            Buttons = (intent.Buttons | PlayerButtons.Use) & ~(PlayerButtons.Run | PlayerButtons.Jump | PlayerButtons.Throw),
        };
    }
}
