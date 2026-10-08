using System.Globalization;
using Ballast.Net;
using Ballast.Online;
using DarkTerritory.Sim.Net;

namespace DarkTerritory.Game;

/// <summary>
/// Who a run is for, and what a player's looking for (note 450; the director, 8 Oct 2026: "a setting called 'Here for
/// Laughs' ... so that players who are feeling sociable but not super competitive can sort through", and "'Feeling
/// Competitive' so players can find servers that have players who want to go for the longest runs they can"). A host says
/// it of the run; on the join screen it puts runs of that mood first. Either: no preference, nearest first.
/// </summary>
public enum RunMood : byte { Either, Laughs, Competitive }

public static class Moods
{
    /// <summary>How a listing carries it (a lobby's value, a beacon's field): "" for either, so an older host's reads so.</summary>
    public static string Word(RunMood mood) => mood switch
    {
        RunMood.Laughs => "laughs",
        RunMood.Competitive => "competitive",
        _ => "",
    };

    public static RunMood Parse(string? word) => word switch
    {
        "laughs" => RunMood.Laughs,
        "competitive" => RunMood.Competitive,
        _ => RunMood.Either,
    };

    /// <summary>As the menus say it.</summary>
    public static string Label(RunMood mood) => mood switch
    {
        RunMood.Laughs => "HERE FOR LAUGHS",
        RunMood.Competitive => "FEELING COMPETITIVE",
        _ => "EITHER",
    };

    /// <summary>As the join list's column says it: short, so the row keeps to its width.</summary>
    public static string Tag(RunMood mood) => mood switch
    {
        RunMood.Laughs => "LAUGHS",
        RunMood.Competitive => "COMPETE",
        _ => "-",
    };

    /// <summary>EITHER, HERE FOR LAUGHS, FEELING COMPETITIVE, and round.</summary>
    public static RunMood Step(RunMood mood, int by) => (RunMood)((((int)mood + by) % 3 + 3) % 3);
}

/// <summary>A public game on the join screen's list: from the local network or a platform's lobby search.</summary>
/// <param name="Aboard">The crew aboard (the host's own count: joiners by address aren't in a platform lobby).</param>
/// <param name="Max">How many it takes (0: not said).</param>
/// <param name="Tier">The route tier's name, or "" for a hand-laid line.</param>
/// <param name="PingMs">Your round trip to the host: measured on the network, estimated by the platform; null, not yet.</param>
/// <param name="Where">For the detail line: how it was found and what the night is.</param>
public sealed record ListedGame(string Name, int Aboard, int Max, string Tier, double? PingMs, int Protocol, string Where, Launch Join)
{
    /// <summary>A private run (note 450): listed with a lock, and joined with its password.</summary>
    public bool Locked { get; init; }
    /// <summary>Who the run is for, as its host said (note 450).</summary>
    public RunMood Mood { get; init; }

    /// <summary>The crew's at its cap (note 254): shown FULL, greyed, not joinable.</summary>
    public bool Full => Max > 0 && Aboard >= Max;

    /// <summary>A game a LAN beacon described (its ping the browser's own round trip to it).</summary>
    public static ListedGame From(LanGame g) =>
        new(g.Name is { Length: > 0 } n ? n : $"{g.Host}'s run", g.Aboard, g.Max, g.Tier, g.PingMs, g.Protocol,
            $"On your network at {g.Address}: {g.Night}", new Launch.Join(g.Address.ToString()))
        { Locked = g.Locked, Mood = Moods.Parse(g.Mood) };

    /// <summary>A lobby a platform search found (its ping the platform's estimate from the host's published location).</summary>
    public static ListedGame From(LobbyListing l, string platform)
    {
        string host = l.Get(Lobby.HostKey);
        return new(l.Get(NetPlaySession.NameKey) is { Length: > 0 } n ? n : $"{(host.Length > 0 ? host : "someone")}'s run",
            int.TryParse(l.Get(NetPlaySession.AboardKey), NumberStyles.Integer, CultureInfo.InvariantCulture, out int aboard) ? aboard : l.Members,
            // The crew cap (note 254) as the host says it; an older host's lobby, its member limit.
            int.TryParse(l.Get(NetPlaySession.MaxKey), NumberStyles.Integer, CultureInfo.InvariantCulture, out int max) && max > 0 ? max : l.MaxMembers,
            l.Get(NetPlaySession.TierKey), l.PingMs,
            int.TryParse(l.Get(Lobby.ProtocolKey), NumberStyles.Integer, CultureInfo.InvariantCulture, out int protocol) ? protocol : -1,
            $"On {platform}, hosted by {host}: {l.Get(NetPlaySession.RunKey)}", new Launch.JoinLobby(l.Id))
        { Locked = l.Get(NetPlaySession.LockedKey) == "1", Mood = Moods.Parse(l.Get(NetPlaySession.MoodKey)) };
    }

    /// <summary>
    /// Both lists as one. A game on the network that's also in a platform lobby shows once, as the network's: the direct
    /// path, and a ping that was measured.
    /// </summary>
    public static IReadOnlyList<ListedGame> Merge(IEnumerable<LanGame> lan, IEnumerable<LobbyListing> online, string platform, RunMood looking = RunMood.Either)
    {
        var heard = lan.ToList();
        var also = heard.Select(g => g.Lobby).Where(l => l.Length > 0).ToHashSet();
        return Sort(heard.Select(From).Concat(online.Where(l => !also.Contains(l.Id.ToString())).Select(l => From(l, platform))), looking);
    }

    /// <summary>
    /// The join list's order (note 450): runs of the mood you're looking for first, then the rest; each nearest first, the
    /// unmeasured last. Looking for either, nearest first (note 169).
    /// </summary>
    public static IReadOnlyList<ListedGame> Sort(IEnumerable<ListedGame> games, RunMood looking) =>
        [.. games.OrderBy(g => looking != RunMood.Either && g.Mood != looking)
            .ThenBy(g => g.PingMs ?? double.MaxValue).ThenBy(g => g.Name, StringComparer.Ordinal)];
}

/// <summary>
/// The join screen's lobby browser (the user's playtest: "Join should work like Lethal Company, I see active lobbies I can
/// join and then what my ping is"): the public games on the local network, and the public lobbies a platform search finds
/// for this game and protocol, every <see cref="SearchSeconds"/> while it's looking or on REFRESH.
/// </summary>
public sealed class LobbyBrowser : IDisposable
{
    public const double SearchSeconds = 10;
    readonly LanBrowser? _lan;
    readonly IOnlineBackend? _online;
    readonly int _protocol;
    IReadOnlyList<LobbyListing> _listed = [];
    double _nextSearch;

    /// <param name="lan">The local network's listener (null: none, a test of the platform half).</param>
    /// <param name="online">The platform to search (null: no platform, LAN only).</param>
    public LobbyBrowser(LanBrowser? lan, IOnlineBackend? online, int protocol = Protocol.Version)
    {
        _lan = lan;
        _online = online;
        _protocol = protocol;
    }

    /// <summary>Why the network half can't listen, if it can't.</summary>
    public string? Error => _lan?.Error;

    /// <summary>
    /// Reads the network, and takes any search result out of <paramref name="events"/> (what the app polled from the
    /// platform this frame: it polls once, for invites too). <paramref name="search"/>: the join screen's showing, so
    /// look again when due.
    /// </summary>
    public void Poll(double now, IReadOnlyList<OnlineEvent> events, bool search)
    {
        _lan?.Poll(now);
        foreach (var e in events)
            if (e.Kind == OnlineEventKind.LobbyList && e.Listings is { } found)
                _listed = found;
        if (search && _online is not null && now >= _nextSearch)
        {
            _nextSearch = now + SearchSeconds;
            _online.RequestLobbyList(new LobbyFilter(NetPlaySession.Game, _protocol));
        }
    }

    /// <summary>Looks again now: a fresh platform search, and a ping to everyone on the network.</summary>
    public void Refresh()
    {
        _nextSearch = 0;
        _lan?.Refresh();
    }

    public IReadOnlyList<ListedGame> Games =>
        ListedGame.Merge((_lan?.Games ?? []).Where(g => g.Game == NetPlaySession.Game), _listed, _online?.Platform ?? "");

    public void Dispose() => _lan?.Dispose();
}
