using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Bots;

public sealed partial class CrewCalls
{
    /// <summary>
    /// Note 1021 (the director's playtest of 23 Oct, line 5: "I've been captured ... the bots aren't coming to save me ... A
    /// Whistler got me ... they didn't try to rescue me"): the grab <paramref name="me"/> goes to break, or null. A crewmate
    /// carried off the train by a Whistler (to its nest, out from its gap), or taken by a Climber in a car (under a roof's
    /// swing), is out of the generic rescue's reach (<see cref="Heed.Rescue"/>: a strike from where a walker stands on the
    /// roofs), so nobody went. Now one bot goes, not the whole crew: the nearest to the grabber within
    /// <c>crewBots.rescueWithin</c>, alive, not held itself and with <c>crewBots.rescueFit</c> health or more (ties to the
    /// lower id). The driver's one of them only with the train standing (a Whistler grabs only then; a moving train's
    /// driver keeps the controls). Every bot works it out from the same crew, so they agree. A bot's <paramref name="crew"/>
    /// is the others; <paramref name="me"/> and <paramref name="self"/> put it among them.
    /// </summary>
    public Enemy? Rescue(World world, IReadOnlyList<(int Id, PlayerState State)> crew, int me, in PlayerState self)
    {
        if (world.Enemies is not { } et || Heed.TakenAway(world) is not { } grab)
            return null;
        var train = world.Train;
        var b = et.CrewBots;
        var at = grab.WorldPosition(train);
        bool standing = train.Dynamics.Speed < Net.CabControls.StandingBelow;
        (int Id, PlayerState State) mine = (me, self);
        var nearest = crew.Where(c => c.Id != me).Append(mine)
            .Where(c => IsBot(c.Id) && c.Id != grab.Holding && c.State.Alive && !c.State.Has(PlayerFlags.Held) && c.State.Health >= b.RescueFit
                && (standing || _roles.GetValueOrDefault(c.Id, BotRole.Walker) != BotRole.Driver))
            .Select(c => (c.Id, Away: (PlayerMotor.WorldPosition(c.State, train) - at).Length))
            .Where(c => c.Away <= b.RescueWithin)
            .OrderBy(c => c.Away).ThenBy(c => c.Id).Select(c => (int?)c.Id).FirstOrDefault();
        return nearest == me ? grab : null;
    }
}
