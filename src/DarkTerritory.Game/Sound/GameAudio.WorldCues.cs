using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// The sound cues in the world (queue #466, note 732; the director, 14 Oct 2026: "I do like the idea of using sound-only cues
/// in the design of the environment"): the host's <see cref="World.Cues"/>, each heard once where it is. The sounds are AU1's;
/// each hook names its own cue, and where none is installed yet an existing sound stands in only where it's the same kind of
/// thing (a board under a weight is a step on wood; the Householder shifting is its own chair's creak). The far bell and the dog
/// have no stand-in: nothing installed is a bell or a dog, and a creature's tell standing in would teach the wrong thing (App.
/// A.1: tells distinguishable by ear).
/// </summary>
public sealed partial class GameAudio
{
    /// <summary>The cues already heard, by their id.</summary>
    readonly HashSet<int> _cuesHeard = [];

    /// <summary>A board under a weight where a house creature waits; quieter at the edge of its reach, full at its nearest.</summary>
    const float CreakFrom = 0.45f;

    void WorldCueSounds(World world)
    {
        foreach (var c in world.Cues)
        {
            if (!_cuesHeard.Add(c.Id))
                continue;
            float near = (float)(CreakFrom + (1 - CreakFrom) * Math.Clamp(c.Strength, 0, 1));
            switch (c.Kind)
            {
                // The Householder's own chair (AU1's, note 767) creaking under it as one of its things nears its door.
                case CueKind.Creak when (EnemyKind)c.Of == EnemyKind.Householder:
                    Cue("cs-householder.creak", c.At, OccludedInHouse(world, c.At), near);
                    break;
                // Boards in the dark of a house, or the roof over it, where the Lodger or the Hanger waits.
                case CueKind.Creak:
                    Cue(OwnOr("tell-house.creak", "crew-footsteps.walk.wood"), c.At, OccludedInHouse(world, c.At), near);
                    break;
                // The Householder shifting in its chair, one of its things lifted and nothing left to pay for it.
                case CueKind.Shift:
                    Cue(OwnOr("tell-householder.shift", "cs-householder.creak"), c.At, OccludedInHouse(world, c.At));
                    break;
                // A far bell from where something of the crew's has been lost.
                case CueKind.Bell:
                    Cue("world-bell.far", c.At, Occlusion(PlayerMotor.Outside));
                    break;
                // A dog far off, barking at what moves at the lamps' edge.
                case CueKind.Bark:
                    Cue("world-dog.bark", c.At, Occlusion(PlayerMotor.Outside));
                    break;
            }
        }
        if (_cuesHeard.Count > 64)
            _cuesHeard.RemoveWhere(id => !world.Cues.Any(c => c.Id == id));
    }

    /// <summary>The cue's own sound where it's installed, else the stand-in.</summary>
    string OwnOr(string own, string standIn) => HasCue(own) ? own : standIn;

    void EndNightWorldCues() => _cuesHeard.Clear();
}
