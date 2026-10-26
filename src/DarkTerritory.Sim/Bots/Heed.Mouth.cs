using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Route;

namespace DarkTerritory.Sim.Bots;

public static partial class Heed
{
    /// <summary>
    /// The tunnel's mouth this close (s, the train's front to it at its speed), or the train in it, and a crewmate still up on a
    /// roof: down on its haunches where it stands (note 1036).
    /// </summary>
    public const double MouthCrouchSeconds = 3;

    /// <summary>
    /// Note 1036 (queue #739): a crewmate still on a roof as a tunnel's mouth comes crouches under it, as someone playing
    /// does (note 664: sight.json <c>roofWarning.crouchClearsMouth</c>, "a crouch fits the body under low clearance"). The
    /// roof warning sends the walkers indoors (<see cref="RoofWalkerBot.RoofWarned"/>), and a gun's crew are down behind its
    /// shield, but from the engine's hood there's no car to go into: on frontier:7's wet rail with a crew of two, the guard
    /// van's gunner, bitten too hard for the hound run (note 551) and sent forward along the hood to the cab's hatch, walked
    /// the 16 m of it under the warning and was at the hatch the tick the mouth was, both seeds. Bots never crouched. The
    /// last <see cref="MouthCrouchSeconds"/> on a roof, whatever it was about, it crouches and goes on with it at a crouch's
    /// pace (no run, no jump). A bend's warning isn't for a crouch: that throws you off whatever you do.
    /// </summary>
    public static PlayerIntent Mouth(PlayerIntent intent, in PlayerState self, World world)
    {
        if (!self.Alive || self.Parent < 0 || self.Surface != Surface.Roof || world.Lineside is not { } lineside
            || !lineside.Tuning.RoofWarning.CrouchClearsMouth)
            return intent;
        if (lineside.Warning(world.Train) is not { Kind: SignKind.LowClearance } warning || warning.Seconds > MouthCrouchSeconds)
            return intent;
        if (world.Combat is { } c && Combat.Guns.MannedGun(self, world.Train, c.Guns) is not null)
            return intent; // down behind the shield already
        intent.Moves |= PlayerMoves.Crouch;
        intent.Buttons &= ~(PlayerButtons.Run | PlayerButtons.Jump);
        return intent;
    }
}
