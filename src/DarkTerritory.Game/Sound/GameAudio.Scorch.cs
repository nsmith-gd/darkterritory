using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// Burned at a fire's edge, heard (queue #561, note 833; C1's note 821, the director's 14 Oct playtest, line 25: "I just died
/// because I wasn't getting any feedback that I was being burned"): a coat smouldering, held on whoever a car fire's heat
/// reaches from its first tick (CarFire.HeatOn, the burn's own measure, which every machine reckons alike from the
/// replicated fire), louder the hotter by the HUD's heat glow's own curve. So the one burning hears it on themselves before
/// the first point comes off, and their friends hear it on them. The hurt's own cry (crew-hurt.burned) still comes with each
/// point lost.
/// </summary>
public sealed partial class GameAudio
{
    const int ScorchOwner = 0x0D8000;

    void ScorchSounds(World world)
    {
        var train = world.Train;
        foreach (var (id, s) in CrewStates)
        {
            double heat = Sim.Enemies.CarFire.HeatOn(s, world);
            if (heat <= 0 || At(train, s) is not { } feet)
                continue;
            HoldLevel("crew-scorch.sizzle", ScorchOwner + id, feet + Double3.Up * 1.2, Occlusion(PlayerMotor.Space(s, train)),
                Hud.HeatStrengthOf(heat));
        }
    }
}
