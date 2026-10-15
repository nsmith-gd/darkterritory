using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Train;

namespace DarkTerritory.Game;

public static partial class Hud
{
    /// <summary>
    /// The handcar's prompt (GDD §24.1; note 729). At the handle, the stroke that's in time now: the car running, the one that
    /// drives it on the way it's going; standing, either way starts it. At an end of the handle, take it. Beside it (off it),
    /// what a held Use does there: lift it down onto the rails or back onto the van, and how far a lift has got. Null otherwise.
    /// </summary>
    static string? HandcarPrompt(Sim.World world, in PlayerState p, TrainOnLine train)
    {
        if (train.Handcar is not { } h || !p.Alive)
            return null;
        if (p.Has(PlayerFlags.Pumping))
        {
            var rake = train.HandcarRake!;
            int end = Handcar.EndAt(p.Position.Z);
            if (rake.Speed < 0.2)
                return "PUSH [W] OR PULL [S] TO START : BRAKE [B] : LET GO [SPACE]";
            return Handcar.InTime(h.Phase, end, Math.Sign(rake.Velocity)) > 0 ? "PUSH [W]" : "PULL [S]";
        }
        if (world.AtHandle(p) is not null)
            return "THE HANDLE : [E]";
        if (!world.CanLiftHandcar(p))
            return null;
        if (h.Lift > 0)
            return $"LIFTING ({h.Lift * 100:0}%)";
        if (!train.HandcarCanGo())
            return h.Carried ? "THE HANDCAR : AT A STAND" : null;
        return h.Carried ? "LIFT THE HANDCAR DOWN : HOLD [E]" : "PUT THE HANDCAR BACK : HOLD [E]";
    }
}
