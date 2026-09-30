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
    /// cab), a fireman beside it in a crew big enough (T75), the gunner second when there are guns, walkers the rest, each
    /// with its part at a stop (the walkers first: a shunter, the winch pair, then crates).
    /// </summary>
    public static IBot Make(int i, int count, CrewCalls? calls, CombatTuning? combat, PlayerTuning player, int seed)
    {
        bool gunner = combat is not null;
        int fireman = count >= Harness.FiremanFrom ? count - 1 : -1;
        var hands = Enumerable.Range(1, Math.Max(0, count - 1)).Where(h => h != fireman).OrderBy(h => h == 1 && gunner ? 1 : 0).ToList();
        StopJob job = hands.IndexOf(i) switch
        {
            0 => StopJob.Shunter,
            1 => StopJob.Winch0,
            2 => StopJob.Winch1,
            _ => i == 1 && gunner ? StopJob.None : StopJob.Crates,
        };
        StopHand? hand = calls is null ? null : new StopHand(job, calls, i, player.Cold);
        return i == 0 ? new ConductorBot(calls, i)
            : i == fireman ? new ConductorBot(calls, i) { Fireman = true }
            : i == 1 && combat is { } c ? new GunnerBot(c.Guns, c.Choir, seed * 1000 + i, player.Cold, hand)
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
            rw.Me = session.PlayerId ?? -1;
        else if (bot is GunnerBot gb)
            gb.Me = session.PlayerId ?? -1;
        var intent = bot is IWorldBot wb ? wb.Decide(session.Predicted, session.World, t, out _) : bot.Decide(session.Predicted, session.Train, t);
        int me = session.PlayerId ?? 0;
        intent = Heed.Rescue(intent, session.Predicted, session.World, me);
        intent = Heed.Hounds(intent, session.Predicted, session.World, me);
        intent = Heed.Backs(intent, session.Predicted, session.World, me, t);
        intent = Heed.Voice(intent, session.Predicted, session.World, me, t);
        intent = Heed.Gaps(intent, session.Predicted, session.World, me);
        intent = Heed.Flies(intent, session.Predicted, session.World, t);
        intent = Heed.Followers(intent, session.Predicted, session.World, me, calls, t);
        intent = Heed.Drift(intent, session.Predicted, session.World, me);
        return intent;
    }
}
