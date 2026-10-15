using Ballast;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Bots;

public static partial class Heed
{
    /// <summary>
    /// Note 722 (emotes for two; the director, 14 Oct 2026, App. F.6): a high five or fist bump held up to a bot by someone
    /// within reach and facing it (<see cref="EmoteRules.OfferedTo"/>) is answered: it stands, turns to them, and once it faces
    /// them and emotes.botAnswer has gone by since the offer, picks the same, the intent a player's answer is. Bots never offer.
    /// </summary>
    public static PlayerIntent HighFive(PlayerIntent intent, in PlayerState self, World world, int me, IReadOnlyList<(int Id, PlayerState State)> crew)
    {
        var t = world.EmoteTuning;
        var train = world.Train;
        if (!EmoteRules.CanEmote(self) || crew.Count == 0 || EmoteRules.OfferedTo(world.Emotes, me, self, crew, train, t, world.Tick) is not { } offer)
            return intent;
        foreach (var e in world.Emotes)
            if (e.By == me && e.Kind == offer.Kind)
                return intent;
        PlayerState other = default;
        foreach (var (id, s) in crew)
            if (id == offer.By)
                other = s;
        var to = PlayerMotor.WorldPosition(other, train) - PlayerMotor.WorldPosition(self, train);
        // Faced in its own frame: the world's bearing less its frame's heading (PlayerMotor's yaw: 0 faces −Z).
        double heading = PlayerMotor.WorldYaw(self, train) - self.Yaw;
        double yaw = DMath.Atan2(-to.X, -to.Z) - heading;
        intent = intent with
        {
            MoveX = 0,
            MoveZ = 0,
            LookYaw = (float)Math.Clamp(Math.IEEERemainder(yaw - self.Yaw, 2 * Math.PI), -0.5, 0.5),
            Buttons = intent.Buttons & ~(PlayerButtons.Run | PlayerButtons.Jump),
        };
        if ((world.Tick - (double)offer.Tick) * SimConstants.TickSeconds >= t.BotAnswer && EmoteRules.Faces(self, other, train, t.FacingDegrees))
            intent.Emote = offer.Kind;
        return intent;
    }
}
