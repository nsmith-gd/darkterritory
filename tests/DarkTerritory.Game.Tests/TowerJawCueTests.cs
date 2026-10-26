using DarkTerritory.Game;
using DarkTerritory.Sim.Enemies;

namespace DarkTerritory.Game.Tests;

/// <summary>
/// Tower Jaw's lines on the HUD (note 1045; tower-jaw.md §5): its TELEGRAPH is two things, its rear at a crewmate or, still at
/// its gnawing, the structure's groan; read by its mode, as the Gannet's are. Its fall (COMMIT, then PUNISH) is the tower
/// down, which until note 1045 never came: the spine refused a punish straight from its gnawing.
/// </summary>
public class TowerJawCueTests
{
    static EnemyEvent To(SpinePhase from, SpinePhase to) => new(1, 7, EnemyKind.TowerJaw, from, to, 2);

    [Fact]
    public void TheGroanTheRearAndTheFallEachSayWhatTheyAre()
    {
        Assert.Equal("the tower groans and leans over the line: get clear of it",
            PrototypeSession.Cue(To(SpinePhase.Alert, SpinePhase.Telegraph), null, TowerJawMode.Gnaw));
        Assert.Equal("it rears and slaps its tail: four blows drive it off",
            PrototypeSession.Cue(To(SpinePhase.Alert, SpinePhase.Telegraph), null, TowerJawMode.Threat));
        // The fall: its commit is the same tick as its punish, which says it.
        Assert.Null(PrototypeSession.Cue(To(SpinePhase.Telegraph, SpinePhase.Commit), null, TowerJawMode.Wreck));
        Assert.Equal("the tower's down across the line: clear it by hand (Use)",
            PrototypeSession.Cue(To(SpinePhase.Commit, SpinePhase.Punish), null, TowerJawMode.Wreck));
        // Its lunge's commit says nothing new either.
        Assert.Null(PrototypeSession.Cue(To(SpinePhase.Telegraph, SpinePhase.Commit), null, TowerJawMode.Lunge));
    }
}
