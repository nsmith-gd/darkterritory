using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Game.Sound;

/// <summary>
/// Bringing them home heard (queue #512, note 782; K1's #1026, note 723): a friend hauled up into a moving car, heaved onto the
/// shoulder, put down, and thrown (the throw's own heave is the horseplay event's, <see cref="HorseplaySounds"/>). Off what
/// every machine has: the carried's flags (<see cref="PlayerFlags.Carried"/>) and the host's move that puts a hauled runner
/// in the car (<see cref="Rescue.HaulAboard"/>, a <see cref="PlayerState.Placed"/> from the ground into a moving car beside the
/// one who hauled them).
/// </summary>
public sealed partial class GameAudio
{
    // How close a hauled runner lands to the one who hauled them (rescue.handUp.landInboard 0.45 m, the crowd parting them),
    // and how fast the car must be going for it to be a hand-up (rescue.handUp.fromSpeed 1 m/s; a host move at a stand isn't).
    const double HauledBeside = 1.5, HauledFrom = 0.9;

    void CrewRescue(World world, int id, in PlayerState s, CrewMember c, Double3 feet, float occlusion, bool placed)
    {
        if (!s.Alive || !c.Alive)
            return;
        var train = world.Train;
        bool carried = s.Flags.HasFlag(PlayerFlags.Carried), was = c.Flags.HasFlag(PlayerFlags.Carried);
        // Heaved onto a friend's shoulder: the carrier's heave and the coats, where the two are now.
        if (carried && !was)
            Cue("crew-rescue.lift", feet, occlusion);
        // Off the shoulder on their feet: put down, wriggled free, or dropped as the carrier's taken. Thrown, they're off them
        // (Shoved, as a shove puts them), and the throw was heard.
        if (was && !carried && !s.Flags.HasFlag(PlayerFlags.Shoved))
            Cue("crew-rescue.set", feet, occlusion);
        int car = s.Parent;
        if (placed && c.Parent == PlayerState.World && car >= 0 && car < train.Frames.Count && train.Frames[car].Velocity.Length >= HauledFrom)
        {
            var at = s.Position;
            if (CrewStates.Any(o => o.Id != id && o.State.Alive && o.State.Parent == car && (o.State.Position - at).Length <= HauledBeside))
                Cue("crew-rescue.haul", feet, occlusion);
        }
    }
}
