using DarkTerritory.Sim;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// A ping heard (queue #507, note 777; A1's #1024, note 739): each of the crew's pings, the order passed down the train
/// that everyone sees on the HUD and the world's mark, heard by everyone once as it's made, through the radio every uniform
/// has (note 749): flat, as the set in your hand. Off <see cref="World.Pings"/>, which every machine has (their own record
/// kind). An ear that joins mid-night doesn't hear the pings already standing.
/// </summary>
public sealed partial class GameAudio
{
    /// <summary>The pings already heard (or standing when this ear first looked), by their ids, unique for the night.</summary>
    readonly HashSet<int> _pingsHeard = [];

    void PingsHeard(World world, bool primed)
    {
        foreach (var p in world.Pings)
            if (_pingsHeard.Add(p.Id) && primed)
                Ui(UiCue.Ping);
    }
}
