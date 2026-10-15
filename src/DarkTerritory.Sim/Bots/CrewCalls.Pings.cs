using DarkTerritory.Sim.Enemies;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Bots;

/// <summary>What a bot is, to the pings (note 739).</summary>
public enum BotRole : byte { Walker, Gunner, Driver }

/// <summary>
/// The crew's pings as the bots' orders (GDD §12 "Ping the bots", note 739; the director, 14 Oct 2026: "If I point at multiple
/// things, it should understand that the bots should then work as a team to do the thing"): each ping is taken by one bot,
/// the nearest that's free and can do it, and kept to until it's done, the bot's dead, or <see cref="PingTuning.OrderSeconds"/>.
/// A ping once taken is never taken again, so several pings go to several bots.
/// </summary>
public sealed partial class CrewCalls
{
    readonly Dictionary<int, (int Ping, PingKind Kind, int Target, uint Since)> _orders = [];
    readonly HashSet<int> _pingsTaken = [];
    readonly Dictionary<int, BotRole> _roles = [];

    /// <summary>What a bot is to the pings (note 739): it says each tick.</summary>
    public void Role(int bot, BotRole role) => _roles[bot] = role;

    /// <summary>
    /// Whether a bot can take a ping of this kind (note 739): a fire is a walker's; the controls are the driver bot's while one
    /// is alive (it comes back to them the moment they're let go), else a walker's, and a gunner's with no walker left (note 741).
    /// </summary>
    bool Can(int bot, PingKind kind, IReadOnlyList<(int Id, PlayerState State)> crew, int me, bool meAlive)
    {
        var role = _roles.GetValueOrDefault(bot, BotRole.Walker);
        bool Alive(int id) => id == me ? meAlive : crew.Any(c => c.Id == id && c.State.Alive);
        bool any(BotRole r) => _roles.Any(x => x.Value == r && Alive(x.Key));
        return kind switch
        {
            PingKind.Fire => role == BotRole.Walker,
            PingKind.Cab => any(BotRole.Driver) ? role == BotRole.Driver
                : role == BotRole.Walker || role == BotRole.Gunner && !any(BotRole.Walker),
            _ => false,
        };
    }

    /// <summary>The ping <paramref name="bot"/> is working on, or null.</summary>
    public (PingKind Kind, int Target)? OrderOf(int bot) => _orders.TryGetValue(bot, out var o) ? (o.Kind, o.Target) : null;

    /// <summary>Whoever's taken a ping (note 739), or null: nobody yet.</summary>
    public int? TakerOf(int ping) => _orders.Where(o => o.Value.Ping == ping).Select(o => (int?)o.Key).FirstOrDefault();

    /// <summary>Whether a ping's been taken (now or before, by a bot since done with it).</summary>
    public bool Taken(int ping) => _pingsTaken.Contains(ping);

    /// <summary>
    /// Note 739: the pings this bot can do and is nearest to of the free bots that can do them, taken; and its own order
    /// let go once it's done (the fire out, the controls held by a bot), it's dead, or its time is up. Each bot calls it
    /// for itself every tick, so the first nearest takes a ping and the rest see it gone.
    /// </summary>
    public void Pings(World world, int me, in PlayerState self, IReadOnlyList<(int Id, PlayerState State)> crew)
    {
        bool alive = self.Alive;
        bool can(int bot, PingKind kind) => Can(bot, kind, crew, me, alive);
        if (_orders.TryGetValue(me, out var mine) && (!self.Alive || OrderDone(world, me, mine.Kind, mine.Target)
            || (world.Tick - mine.Since) * SimConstants.TickSeconds > world.PingTuning.OrderSeconds))
            _orders.Remove(me);
        if (!self.Alive || _orders.ContainsKey(me))
            return;
        var at = PlayerMotor.WorldPosition(self, world.Train);
        foreach (var p in world.Pings.OrderBy(p => p.Tick).ThenBy(p => p.Id))
        {
            if (_pingsTaken.Contains(p.Id) || !can(me, p.Kind) || OrderDone(world, me, p.Kind, p.Target))
                continue;
            double mineAway = (at - p.At).Length;
            // The nearest bot free for it: another free one nearer that can do it leaves it to them (ties to the lower id).
            bool nearer = crew.Any(c => c.Id != me && c.State.Alive && IsBot(c.Id) && !_orders.ContainsKey(c.Id) && can(c.Id, p.Kind)
                && ((PlayerMotor.WorldPosition(c.State, world.Train) - p.At).Length is var d && (d < mineAway || d == mineAway && c.Id < me)));
            if (nearer)
                continue;
            _orders[me] = (p.Id, p.Kind, p.Target, world.Tick);
            _pingsTaken.Add(p.Id);
            return;
        }
    }

    /// <summary>A ping's job done (note 739): the car's fire out; the controls a bot's.</summary>
    static bool OrderDone(World world, int me, PingKind kind, int target) => kind switch
    {
        PingKind.Fire => !world.ActiveEnemies.Any(e => e is CarFire && !e.Gone && e.Attached == target),
        PingKind.Cab => world.ControlsHolder >= 0 && world.ControlsHolderBot,
        _ => false,
    };
}
