using DarkTerritory.Sim;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Game;

/// <summary>
/// A Shy Thing's gaze on a flat screen (GDD v1.3 App. A.6): the mouse turns heavier the longer you've watched it, from full
/// to enemies.json's <c>turnFloor</c> of itself, <c>turnDrag</c> slower for every second under. Input only, on this machine:
/// what breaks it is the sim's look-away, held; and a headset's view is never turned or held for its player (ARCHITECTURE
/// §8 note 30's comfort rule), so a headset takes no drag.
/// </summary>
public static class Hypnosis
{
    /// <summary>How much of the mouse's turn gets through for this player now: 1 unless something has them.</summary>
    public static double TurnScale(World world, int player)
    {
        if (world.Enemies is not { } t)
            return 1;
        foreach (var e in world.ActiveEnemies)
            if (e is ShyThing { Gone: false } shy && shy.Victim == player && e.Phase is SpinePhase.Telegraph or SpinePhase.Commit or SpinePhase.Grab)
                return Math.Max(t.ShyThing.TurnFloor, 1 - t.ShyThing.TurnDrag * shy.Under);
        return 1;
    }
}
