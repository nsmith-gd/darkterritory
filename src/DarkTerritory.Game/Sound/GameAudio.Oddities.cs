using DarkTerritory.Sim;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// The odd outcomes heard (queue #464, note 730; K1's): read off the host's replicated happenings (<see cref="World.OddEvents"/>),
/// each heard once by its id. Each has a cue of its own for the audio chat (AU1) to give a sound (world-odd.*, the town's bell
/// place-town.bell); until one's installed, the nearest sound there is stands in where there is one (a cannonball's plunge,
/// quietly, for the well's splash; a toy dropped for the can and the coin; something far off out there for what answers the
/// can, which only the living hear).
/// </summary>
public sealed partial class GameAudio
{
    readonly HashSet<int> _oddHeard = new();
    World? _oddWorld;

    void OddSounds(World world)
    {
        if (!ReferenceEquals(world, _oddWorld))
        {
            // A new night: the first sight of a session's happenings isn't news.
            _oddWorld = world;
            _oddHeard.Clear();
            foreach (var e in world.OddEvents)
                _oddHeard.Add(e.Id);
            return;
        }
        float outside = Occlusion(PlayerMotor.Outside);
        // Whoever's listening, alive: the dead don't hear what answers the can (only the living, note 730).
        bool living = true;
        foreach (var (id, s) in CrewStates)
            if (id == OwnId)
                living = s.Alive;
        foreach (var e in world.OddEvents)
        {
            if (!_oddHeard.Add(e.Id))
                continue;
            switch (e.Kind)
            {
                case OddKind.Bell:
                    Cue("place-town.bell", e.At, outside);
                    break;
                case OddKind.Splash:
                    _ = Cue("world-odd.well-splash", e.At, outside) ?? Cue("crew-cannon-impact.water", e.At, outside, 0.3f);
                    break;
                case OddKind.Coin:
                    _ = Cue("world-odd.coin", e.At, outside) ?? Cue("crew-carry.toy-drop.ground", e.At, outside);
                    break;
                case OddKind.Wash:
                    Cue("world-odd.wash", e.At, outside);
                    break;
                case OddKind.Kick:
                    _ = Cue("world-odd.can-kick", e.At, outside) ?? Cue("crew-carry.toy-drop.ground", e.At, outside);
                    break;
                case OddKind.Answer when living:
                    _ = Cue("world-odd.bark", e.At, outside) ?? Cue("world-night.far", e.At, outside);
                    break;
                case OddKind.Turned:
                    Cue("world-odd.scarecrow", e.At, outside);
                    break;
                case OddKind.Letter:
                    Cue("world-odd.letter", e.At, outside);
                    break;
                case OddKind.Painting:
                    _ = Cue("world-odd.painting", e.At, outside) ?? Cue("crew-search.found", e.At, outside);
                    break;
                case OddKind.Sooted:
                    _ = Cue("world-odd.sooted", e.At, outside) ?? Cue("place-town.cough", e.At, outside);
                    break;
                case OddKind.Lurch:
                    _ = Cue("world-odd.lurch", e.At, outside) ?? Cue("crew-horseplay.bump", e.At, outside);
                    break;
            }
        }
    }
}
