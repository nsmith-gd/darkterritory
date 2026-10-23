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
    readonly Dictionary<int, (int Ping, PingKind Kind, int Target, uint Since, StopJob Part, bool Was)> _orders = [];
    readonly HashSet<int> _pingsTaken = [];
    /// <summary>How many bots have taken each ping (the crane's two, note 739 part two).</summary>
    readonly Dictionary<int, int> _pingHands = [];
    readonly Dictionary<int, BotRole> _roles = [];

    /// <summary>What a bot is to the pings (note 739): it says each tick.</summary>
    public void Role(int bot, BotRole role) => _roles[bot] = role;

    /// <summary>
    /// Whether a bot can take a ping of this kind (note 739): a fire is a walker's; the controls are the driver bot's while one
    /// is alive (it comes back to them the moment they're let go), else a walker's, and a gunner's with no walker left (note 741).
    /// Note 936 (the director's playtest of 20 Oct, one bot crewmate, the driver: "I wish there was a way I could just send this
    /// guy out to go and throw the spur for me"): a walker's jobs are the driver's with no walker alive, it being the crew's
    /// only pair of hands. Before, a crew of one bot never took any ping but the controls.
    /// </summary>
    bool Can(int bot, PingKind kind, IReadOnlyList<(int Id, PlayerState State)> crew, int me, bool meAlive)
    {
        var role = _roles.GetValueOrDefault(bot, BotRole.Walker);
        bool Alive(int id) => id == me ? meAlive : crew.Any(c => c.Id == id && c.State.Alive);
        bool any(BotRole r) => _roles.Any(x => x.Value == r && Alive(x.Key));
        bool walkers = role == BotRole.Walker || role == BotRole.Driver && !any(BotRole.Walker);
        return kind switch
        {
            PingKind.Fire => walkers,
            // Note 739 part two: a find is a walker's to fetch (the stop's hands carry it home, as the village's).
            PingKind.Loot => walkers,
            // The abandoned town: a walker's to search (the village errand, whatever the crates).
            PingKind.Town => walkers,
            // The crane: two walkers', its operator and its rigger.
            PingKind.Crane => walkers,
            // Note 936: the points thrown, the stop's cargo loaded: a walker's, as the rest.
            PingKind.Points or PingKind.Cargo => walkers,
            PingKind.Cab => any(BotRole.Driver) ? role == BotRole.Driver
                : role == BotRole.Walker || role == BotRole.Gunner && !any(BotRole.Walker),
            _ => false,
        };
    }

    /// <summary>
    /// How many bots take a ping of this kind (note 739 part two): the crane two, its operator and its rigger (spec D.2's
    /// gantry crane, "2 mandatory"); the rest one.
    /// </summary>
    static int Hands(PingKind kind) => kind == PingKind.Crane ? 2 : 1;

    /// <summary>
    /// The crane part a bot's ping gives it (note 739 part two): the controls (<see cref="StopJob.Winch0"/>, the pair's first,
    /// whose part the crane's operator is) or the rigging (<see cref="StopJob.Winch1"/>); None with no crane order.
    /// </summary>
    public StopJob CranePart(int bot) => _orders.TryGetValue(bot, out var o) && o.Kind == PingKind.Crane ? o.Part : StopJob.None;

    /// <summary>Whether a bot's been sent to that part of the crane (note 739 part two): whoever has it otherwise stands aside.</summary>
    public bool CranePartOrdered(StopJob part) => part != StopJob.None && _orders.Values.Any(o => o.Kind == PingKind.Crane && o.Part == part);

    /// <summary>Whether any bot's been sent to the crane (note 739 part two): the driver waits for its loading.</summary>
    public bool CraneOrdered => _orders.Values.Any(o => o.Kind == PingKind.Crane);

    /// <summary>
    /// Note 936: whether a bot's been sent to a job off the train by a ping, and isn't done with it (anything but the
    /// controls): the driver doesn't take the train away from under it.
    /// </summary>
    public bool JobsOrdered => _orders.Values.Any(o => o.Kind != PingKind.Cab);

    /// <summary>Note 936: the ping (its id) <paramref name="bot"/>'s on, to answer it ("ON THE SPUR"); null with none.</summary>
    public int? Answering(int bot) => _orders.TryGetValue(bot, out var o) ? o.Ping : null;

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
        if (_orders.TryGetValue(me, out var mine) && (!self.Alive || OrderDone(world, mine.Kind, mine.Target, mine.Was)
            || (world.Tick - mine.Since) * SimConstants.TickSeconds > (mine.Kind == PingKind.Crane ? world.PingTuning.CraneOrderSeconds : world.PingTuning.OrderSeconds)))
            _orders.Remove(me);
        if (!self.Alive || _orders.ContainsKey(me))
            return;
        var at = PlayerMotor.WorldPosition(self, world.Train);
        foreach (var p in world.Pings.OrderBy(p => p.Tick).ThenBy(p => p.Id))
        {
            if (_pingsTaken.Contains(p.Id) || !can(me, p.Kind) || !Workable(world, p.Kind, p.Target) || OrderDone(world, p.Kind, p.Target, Was(world, p.Kind, p.Target))
                || _pingHands.GetValueOrDefault(p.Id) >= Hands(p.Kind))
                continue;
            double mineAway = (at - p.At).Length;
            // The nearest bot free for it: another free one nearer that can do it leaves it to them (ties to the lower id).
            bool nearer = crew.Any(c => c.Id != me && c.State.Alive && IsBot(c.Id) && !_orders.ContainsKey(c.Id) && can(c.Id, p.Kind)
                && ((PlayerMotor.WorldPosition(c.State, world.Train) - p.At).Length is var d && (d < mineAway || d == mineAway && c.Id < me)));
            if (nearer)
                continue;
            // The crane's two (note 739 part two): the first to take it at the controls, the second rigging; taken once each.
            var others = _orders.Values.Where(o => o.Ping == p.Id).ToList();
            var part = p.Kind != PingKind.Crane ? StopJob.None : others.Any(o => o.Part == StopJob.Winch0) ? StopJob.Winch1 : StopJob.Winch0;
            _orders[me] = (p.Id, p.Kind, p.Target, world.Tick, part, Was(world, p.Kind, p.Target));
            if (others.Count + 1 >= Hands(p.Kind))
                _pingsTaken.Add(p.Id);
            _pingHands[p.Id] = others.Count + 1;
            return;
        }
    }

    /// <summary>
    /// Note 1010 (the director's playtest of 23 Oct, line 8: "I pinged one of the towns and Dunmore said, I'll go fetch it ...
    /// and then he starts manning the cab"): whether a ping's job can be worked where the train is, so a bot only takes (and
    /// answers) one it can go and do. A find, the crane and the cargo are worked at a stop the train stands at (a hand's
    /// <see cref="StopHand"/> works them from the stop's plan), and the town at its own stop: a village pinged on the way in
    /// waits up for the train to stand there (<see cref="PingTuning.TownAheadSeconds"/>). Before, the lone driver took a find
    /// pinged from the line, stood the train for it, and then had nothing it could do but sit in the cab.
    /// </summary>
    public static bool Workable(World world, PingKind kind, int target) => kind switch
    {
        PingKind.Loot or PingKind.Crane or PingKind.Cargo => world.Run?.CurrentSite is not null,
        PingKind.Town => world.Run is { CurrentSite: { } site } run && run.StopOf(site.Feature).Stop == target,
        _ => true,
    };

    /// <summary>Note 936: how the pinged points lay when the ping was taken (thrown over is the job), for a points ping.</summary>
    static bool Was(World world, PingKind kind, int target) =>
        kind == PingKind.Points && target >= 0 && target < world.Train.Line.Branches.Count && world.Train.Diverging(target);

    /// <summary>
    /// A ping's job done (note 739): the car's fire out; the controls a bot's; the find aboard; the town searched; the crane's
    /// loading done (nothing stacked to lift or nowhere under it with room, nothing on the hook) or the crane gone or wrecked.
    /// Note 936: the points thrown over from how they lay (<paramref name="was"/>), or gone; the cargo all aboard, or the car
    /// pinged full, or the train away from the stop.
    /// </summary>
    static bool OrderDone(World world, PingKind kind, int target, bool was) => kind switch
    {
        PingKind.Fire => !world.ActiveEnemies.Any(e => e is CarFire && !e.Gone && e.Attached == target),
        PingKind.Cab => world.ControlsHolder >= 0 && world.ControlsHolderBot,
        // The find aboard a car and set down (or stowed), or gone.
        PingKind.Loot => world.Bodies.All.FirstOrDefault(b => b.Id == target) is not { } find
            || find.Carrier < 0 && (find.Stowed || find.Parent != PlayerState.World),
        // Every hiding spot in the stop's houses searched (or no run: nothing to search).
        PingKind.Town => world.Run is not { } run || !run.HidingSpots.Any(h => h.Stop == target && !run.Searched(h.Stop, h.Container.Index)),
        PingKind.Crane => world.Run?.CurrentSite is not { } site || target < 0 || target >= site.Cranes.Count || site.Cranes[target] is not { } crane
            || crane.Wrecked || crane.Hooked is null && StopHand.CraneTarget(crane, world.Train) is null,
        PingKind.Points => target < 0 || target >= world.Train.Line.Branches.Count || world.Train.Diverging(target) != was,
        PingKind.Cargo => world.Run?.CurrentSite is null || target > 0 && (target >= world.Train.Vehicles.Count || world.Train.Vehicles[target].Load >= 1 - 1e-6)
            || !world.Bodies.All.Any(b => b.Kind is Physics.BodyKind.Cargo or Physics.BodyKind.Heavy && !b.Stowed && (b.Carrier >= 0 || b.Parent == PlayerState.World)),
        _ => false,
    };
}
