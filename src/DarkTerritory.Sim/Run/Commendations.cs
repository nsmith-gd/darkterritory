namespace DarkTerritory.Sim.Run;

/// <summary>
/// GDD v1.4 App. D.12, commendations (note 180): on the run-end screen only, one award per player per run, to anyone except
/// yourself, from anyone in the session then (living, dead or still in the queue). Social only: no scrip, no progression, no
/// demerits. Kept in the player profile, not the character.
/// </summary>
public static class Commendations
{
    /// <summary>D.12's starter set, in its order (the wire's index; the HUD's badges are drawn in the same order).</summary>
    public static readonly string[] StarterSet = ["Came Back For Me", "Held the Switch", "Kept the Fire", "Brought Them Home", "Last One Standing"];

    /// <summary>
    /// Host: <paramref name="from"/> commends <paramref name="to"/>. Refused while the run's on, to themselves, to someone not
    /// in the session, a second time from the same player, or with no such award.
    /// </summary>
    public static bool Give(World world, int from, int to, byte which, IReadOnlyCollection<int> session)
    {
        if (world.Run is not { Over: true } || from == to || which >= StarterSet.Length || !session.Contains(to) || !session.Contains(from)
            || world.Commendations.Any(c => c.From == from))
            return false;
        world.Commendations.Add((from, to, which));
        return true;
    }
}
