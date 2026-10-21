using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Bots;

/// <summary>
/// What the crew does about a Car Hugger latched on a car (#633, note 933): GDD §21's "cut the caboose or kill it. A group kills
/// it fast; one player can, slowly". <paramref name="Car"/> is the car it holds. <paramref name="Party"/> are the two walkers who
/// go to it and club it off from its platform; <paramref name="Cutter"/> the walker who cuts the car loose instead (−1 for
/// nobody): when it has no platform, nobody fit is free to go to it, or it's still on after <see cref="CrewCalls.HuggerCutAfter"/>.
/// </summary>
public sealed record HuggerOrder(int Car, IReadOnlyList<int> Party, int Cutter);

public sealed partial class CrewCalls
{
    /// <summary>
    /// Seconds the Hugger may hold its car before the crew gives the car up and cuts it (#633). The party is on the platform
    /// in well under a minute on a twelve-car train, and two clubbing it kill it in seconds (16 blows, 0.15 back a second):
    /// a minute on means they aren't getting at it, and its grip has the train under 9 m/s.
    /// </summary>
    public const double HuggerCutAfter = 60;

    /// <summary>A walker free to answer the Hugger said so on this tick (a gun, a feed or a stop's work comes first).</summary>
    readonly Dictionary<int, uint> _huggerHands = [];
    /// <summary>Each latched Hugger and the tick the crew first saw it latched.</summary>
    readonly Dictionary<int, uint> _huggerSince = [];

    /// <summary>A walker is free for the Hugger this tick (it says so each tick it is).</summary>
    public void FreeForHugger(int bot, uint tick) => _huggerHands[bot] = tick;

    /// <summary>
    /// The order for the Car Hugger latched on the train, if one is (the first by id): who goes to it, or who cuts its car.
    /// Every walker works it out from the same crew and the same hands, so they agree: the party is the two fit free walkers
    /// nearest its car (by place in the train, then id), the cutter the nearest free walker of any health. A bot's
    /// <paramref name="crew"/> is the others; <paramref name="me"/> and <paramref name="self"/> put it among them.
    /// </summary>
    public HuggerOrder? Hugger(World world, IReadOnlyList<(int Id, PlayerState State)> crew, int me, in PlayerState self, uint tick)
    {
        var train = world.Train;
        var consist = train.Dynamics.Consist;
        if (world.ActiveEnemies.OfType<CarHugger>().Where(h => h.Latched && consist.IndexOf(h.Attached) > 0).OrderBy(h => h.Id).FirstOrDefault()
            is not { } hugger)
        {
            _huggerSince.Clear();
            return null;
        }
        if (!_huggerSince.ContainsKey(hugger.Id))
            _huggerSince[hugger.Id] = tick;
        int car = hugger.Attached, held = consist.IndexOf(car);
        // Free this tick or the last (bots step in turn, so a walker after this one hasn't said yet this tick).
        (int Id, PlayerState State) mine = (me, self);
        var hands = crew.Where(c => c.Id != me).Append(mine).Where(c => c.State.Alive && _huggerHands.TryGetValue(c.Id, out uint at) && at + 1 >= tick
                && _roles.GetValueOrDefault(c.Id, BotRole.Walker) == BotRole.Walker
                && c.State.Parent != PlayerState.World && consist.IndexOf(c.State.Parent) >= 0 && !PlayerMotor.InCab(c.State, train))
            .OrderBy(c => Math.Abs(consist.IndexOf(c.State.Parent) - held)).ThenBy(c => c.Id).ToList();
        var party = hands.Where(c => c.State.Health >= Heed.PackFightHealth).Take(2).Select(c => c.Id).ToList();
        double latched = (tick - _huggerSince[hugger.Id]) * SimConstants.TickSeconds;
        bool cut = train.Frames[car].Shape.Platform is null || party.Count < 2 || latched >= HuggerCutAfter;
        return new HuggerOrder(car, cut ? [] : party, cut && hands.Count > 0 ? hands[0].Id : -1);
    }
}
