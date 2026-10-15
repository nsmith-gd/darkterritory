using Ballast;
using DarkTerritory.Sim;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Game;

/// <summary>
/// The conga (ARCHITECTURE §8 note 714; upgrades.md §4: "everyone who joins follows the leader"): the crew who've picked it,
/// in the order they did, a line; each but the first walks after the one before. It's each player's own steering, sent as
/// their intent like any walk (clients send intent, never state), and only while their own stick's still: steer off, and
/// you're out of the line's step (the emote's still on, the line closes up behind you).
/// </summary>
public static class Conga
{
    /// <summary>How far behind the one ahead each in the line keeps (m).</summary>
    public const double Gap = 0.8;

    /// <summary>
    /// <paramref name="me"/>'s step in the line this tick: toward the one who started before them, or null if they're not in
    /// one, they lead it, or the one ahead isn't on the same floor.
    /// </summary>
    public static PlayerIntent? Follow(int me, in PlayerState self, World world, IReadOnlyList<(int Id, PlayerState State)> crew)
    {
        if (!self.Alive)
            return null;
        var line = world.Emotes.Where(e => e.Kind == Emote.Conga && (world.Tick - e.Tick) * SimConstants.TickSeconds < world.EmoteTuning.Conga
            && crew.Any(c => c.Id == e.By && c.State.Alive)).OrderBy(e => e.Tick).ThenBy(e => e.By).Select(e => e.By).ToList();
        int at = line.IndexOf(me);
        if (at <= 0)
            return null;
        var ahead = crew.First(c => c.Id == line[at - 1]).State;
        if (ahead.Parent != self.Parent)
            return null;
        var off = self.Position - ahead.Position;
        off = new Double3(off.X, 0, off.Z);
        if (off.Length <= Gap + 0.05)
            return null;
        var target = ahead.Position + off * (Gap / off.Length);
        var to = ahead.Position - self.Position;
        return Sim.Bots.WarmUp.Steer(self, target, DMath.Atan2(-to.X, -to.Z)).Step;
    }
}
