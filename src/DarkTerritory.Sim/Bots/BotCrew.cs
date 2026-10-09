using DarkTerritory.Sim.Combat;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;

namespace DarkTerritory.Sim.Bots;

/// <summary>
/// Bot crewmates, each a client of the host like anyone else (CLAUDE.md: bots use the same path): the harness's crew, and a
/// solo player's (T89: someone playing alone, to see what the fuss is about, gets a crew to play with). Each tick every bot
/// reads what its own client can see, decides, heeds what the roster asks of a crew, and sends that as its intent.
/// </summary>
public sealed class BotCrew(CrewCalls? calls) : IDisposable
{
    readonly List<(ClientSession Session, IBot Bot)> _bots = [];
    readonly List<IDisposable> _owned = [];
    uint _tick;

    public IReadOnlyList<(ClientSession Session, IBot Bot)> Bots => _bots;
    public CrewCalls? Calls => calls;

    /// <summary>A bot and its client; <paramref name="owned"/> (its transport) goes when the crew does.</summary>
    public void Add(ClientSession session, IBot bot, IDisposable? owned = null)
    {
        _bots.Add((session, bot));
        if (owned is not null)
            _owned.Add(owned);
    }

    public void Dispose()
    {
        foreach (var o in _owned)
            o.Dispose();
        _owned.Clear();
    }

    /// <summary>
    /// The bot for crew place <paramref name="i"/> of <paramref name="count"/>: the driver first (first aboard takes the
    /// cab, and runs it alone: note 280, the director's "the whole cab being operable by one person"), the gunner second
    /// when there are guns, labourers the rest, each with its part at a stop (a shunter, the winch pair, then crates). The
    /// crew is the driver, gunners and labourers; there's no fireman (T75's went with note 280).
    /// </summary>
    /// <param name="express">A driver that runs hot at this speed and takes no stops (note 376, <see cref="ConductorBot.Express"/>); null as usual.</param>
    public static IBot Make(int i, int count, CrewCalls? calls, CombatTuning? combat, PlayerTuning player, int seed, double? express = null)
    {
        bool gunner = combat is not null;
        var hands = Enumerable.Range(1, Math.Max(0, count - 1)).OrderBy(h => h == 1 && gunner ? 1 : 0).ToList();
        StopJob job = hands.IndexOf(i) switch
        {
            0 => StopJob.Shunter,
            1 => StopJob.Winch0,
            2 => StopJob.Winch1,
            _ => i == 1 && gunner ? StopJob.None : StopJob.Crates,
        };
        StopHand? hand = calls is null ? null : new StopHand(job, calls, i, player.Cold);
        return i == 0 ? express is { } fast ? new ConductorBot(calls, i) { CruiseSpeed = fast, Express = true } : new ConductorBot(calls, i)
            : i == 1 && combat is { } c ? new GunnerBot(c.Guns, c.Choir, seed * 1000 + i, player.Cold, hand)
            // The engine's forward gun's (note 414), in a crew big enough: its last place.
            : i == count - 1 && combat is { Guns.ForwardGunnerFrom: > 0 } f && count >= f.Guns.ForwardGunnerFrom
                ? new GunnerBot(f.Guns, f.Choir, seed * 1000 + i, player.Cold, hand) { Forward = true }
            : new RoofWalkerBot(seed * 1000 + i, player.Cold, hand);
    }

    /// <summary>Every bot's tick: decide and send (its client steps with it).</summary>
    public void Step()
    {
        foreach (var (session, bot) in _bots)
            session.Step(session.Connected ? Think(session, bot, _tick, calls) : default);
        _tick++;
    }

    /// <summary>What a bot does this tick: its own part, then what the roster asks of any crewmate (v1.1).</summary>
    public static PlayerIntent Think(ClientSession session, IBot bot, uint t, CrewCalls? calls)
    {
        // Its part at a stop needs its own id for the heavy crates (T45); and it needs to know what's in its hands.
        if (((bot as GunnerBot)?.Job ?? (bot as RoofWalkerBot)?.Job) is { } part)
            part.PlayerId = session.PlayerId;
        if (bot is RoofWalkerBot rw)
        {
            rw.Me = session.PlayerId ?? -1;
            rw.Calls = calls; // the powder's carrier (note 377), the relief driver's claim (note 399)
        }
        else if (bot is GunnerBot gb)
        {
            gb.Me = session.PlayerId ?? -1;
            gb.Calls = calls;
        }
        // Note 258: it says it's a bot, so the driver can tell the crew playing from the bots.
        if (session.PlayerId is { } bid)
            calls?.Bot(bid);
        // Who else is aboard, and where (T96: the driver stops for a crewmate left behind); and which of them are people playing
        // (note 259: one in the cab minds it while the driver's out breaching a Holdout).
        if (bot is ConductorBot cb)
        {
            cb.Crewmates = [.. session.RemoteIds.Select(id => session.TryGetRemote(id, 1, out var s) ? s : default).Where(s => s.Health > 0 || s.Death != DeathCause.None)];
            cb.Players = calls is null ? null : [.. session.RemoteIds.Where(id => !calls.IsBot(id))
                .Select(id => session.TryGetRemote(id, 1, out var s) ? s : default).Where(s => s.Health > 0 || s.Death != DeathCause.None)];
        }
        // And for the walkers and the gunner, who's where by id: who goes for the repair kit (KitCarry).
        if (bot is RoofWalkerBot or GunnerBot)
        {
            List<(int, PlayerState)> crew = [.. session.RemoteIds.Select(id => (Id: (int)id, Seen: session.TryGetRemote(id, 1, out var s), State: s))
                .Where(c => c.Seen && (c.State.Health > 0 || c.State.Death != DeathCause.None)).Select(c => (c.Id, c.State))];
            if (bot is RoofWalkerBot walker)
                walker.Crew = crew;
            else
                ((GunnerBot)bot).Crew = crew;
        }
        var intent = bot is IWorldBot wb ? wb.Decide(session.Predicted, session.World, t, out _) : bot.Decide(session.Predicted, session.Train, t);
        int me = session.PlayerId ?? 0;
        // Note 551: too hurt for a hound pack that's about, it's in cover (the cab, or a car shut up): no errands out of it, to a
        // Holdout's door, a creature to club or a crewmate held. On frontier:7 seed 3 two hands on 4 and 11 in the cab were taken
        // down out of it at a stand by a stop's errand, and up car 1's ladder into the pack on its roof.
        bool hunted = (bot as RoofWalkerBot)?.Hunted ?? (bot as GunnerBot)?.Hunted ?? false;
        if (!hunted)
            intent = Heed.Holdouts(intent, session.Predicted, session.World, me, calls, (bot as RoofWalkerBot)?.Job ?? (bot as GunnerBot)?.Job);
        intent = Heed.HotBox(intent, session.Predicted, session.World);
        intent = Heed.Coupling(intent, session.Predicted, session.World);
        // Note 367: the six of 8 Oct, for a crewmate who isn't the driver (nor a walker gone forward to take the controls):
        // the Mourners off a body, Tower Jaw clubbed or its wreck cleared, the Freight Beetle clubbed, the Knotter clubbed
        // slack, a Hotbox prised out and its axle freed, the Brakeman cornered and his brakes unwound. A rescue (after) beats them.
        var others = (bot as RoofWalkerBot)?.Crew ?? (bot as GunnerBot)?.Crew ?? [];
        if (bot is RoofWalkerBot { Relieving: false } or GunnerBot && !hunted)
        {
            var hand = (bot as RoofWalkerBot)?.Job ?? (bot as GunnerBot)?.Job;
            intent = Heed.Mourners(intent, session.Predicted, session.World, me, hand);
            intent = Heed.Beetle(intent, session.Predicted, session.World, me);
            intent = Heed.TowerJaw(intent, session.Predicted, session.World, me, hand);
            intent = Heed.Knotter(intent, session.Predicted, session.World, me, hand);
            intent = Heed.Hotbox(intent, session.Predicted, session.World, me, hand, others);
            intent = HeedBrakeman(bot, intent, session.Predicted, session.World, me, others, calls);
        }
        // Note 463: not the driver at the controls of a moving train. Gone back along the hood for a walker grabbed on the
        // engine's roof (out of reach from inside it), the driver never came back, and the fire went out under it.
        if (!hunted && !(bot is ConductorBot { Driving: true } && session.Train.Dynamics.Speed > Net.CabControls.StandingBelow))
            intent = Heed.Rescue(intent, session.Predicted, session.World, me, others);
        intent = Heed.Hounds(intent, session.Predicted, session.World, me);
        intent = Heed.Backs(intent, session.Predicted, session.World, me, t);
        intent = Heed.Voice(intent, session.Predicted, session.World, me, t);
        intent = Heed.Gaps(intent, session.Predicted, session.World, me);
        intent = Heed.Flies(intent, session.Predicted, session.World, t);
        intent = Heed.Gutter(intent, session.Predicted, session.World, t);
        intent = Heed.Followers(intent, session.Predicted, session.World, me, calls, t);
        intent = Heed.Drift(intent, session.Predicted, session.World, me);
        intent = Heed.Gannet(intent, session.Predicted, session.World, me);
        intent = Heed.Heal(intent, session.Predicted, session.World, me);
        return Vote(intent, session, me);
    }

    /// <summary>
    /// GDD v1.4 App. D.11 (note 202): a dead bot is a crewmate like any other, so it has the vote. A while after the host
    /// offers it a ballot (<see cref="Enemies.VoteTuning.BotSeconds"/>, by the host's tick, so the same night votes the same),
    /// it casts it the way a player does: the option's number as the intent's hotbar choice, until the host says it's cast.
    /// Which option is its id's turn round the ballot: no dice, and a crew of bots spreads its votes.
    /// </summary>
    public static PlayerIntent Vote(PlayerIntent intent, ClientSession session, int me)
    {
        var self = session.Predicted;
        if (self.Alive || self.Death == DeathCause.Waiting || session.Ballot is not { Cast: null, Options.Count: > 0 } ballot)
            return intent;
        double wait = session.World.Enemies?.Director.Vote.BotSeconds ?? new Enemies.VoteTuning().BotSeconds;
        if (session.NewestSnapshotTick - session.BallotOfferedTick < wait * SimConstants.TickRate)
            return intent;
        intent.Select = (byte)(me % ballot.Options.Count + 1);
        return intent;
    }

    /// <summary>
    /// The Brakeman's pincer and unwind (<see cref="Heed.Brakeman"/>), for a crewmate not on its way in out of the cold (note 547:
    /// the warm-up's its legs then). Drafted every tick, frontier:7 seed 8's gunner never turned for the door, and froze on car
    /// 3's roof after 240 s of it.
    /// </summary>
    public static PlayerIntent HeedBrakeman(IBot bot, PlayerIntent intent, in PlayerState self, World world, int me,
        IReadOnlyList<(int Id, PlayerState State)> others, CrewCalls? calls) =>
        (bot as RoofWalkerBot)?.WarmUpStep is null or "Off" && (bot as GunnerBot)?.WarmUpStep is null or "Off"
            ? Heed.Brakeman(intent, self, world, me, others, calls)
            : intent;
}
