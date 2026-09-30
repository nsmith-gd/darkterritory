using DarkTerritory.Sim;
using DarkTerritory.Sim.Net;
using DarkTerritory.Sim.Player;
using DarkTerritory.Sim.Run;

namespace DarkTerritory.Game;

/// <summary>
/// What a player can do once they're dead or lobbied (GDD App. D.7, D.10, D.11), and at the run's end (D.12), as the keys
/// the HUD shows: each becomes a request to the host, which decides. Nothing here changes the world on this machine.
/// </summary>
/// <remarks>
/// Flat screen: left and right mouse cycle whom you watch (or, at the end, whom you'd commend), G calls out, M is the Live
/// Mic, N lets the next in the queue go first, 1-9 vote (or, at the end, give an award), B bookmarks.
/// </remarks>
public sealed class DeadPhaseControls
{
    /// <summary>At the run's end, the crewmate the award keys would commend: an index into the others in the session.</summary>
    public int CommendChoice { get; private set; }
    /// <summary>How many of this player's own commendations the profile has already taken from the report.</summary>
    public int CommendationsTaken { get; private set; }
    /// <summary>The bookmark stills saved on this machine this run (D.12: the moment and whose view, and the still).</summary>
    public List<string> Stills { get; } = new();

    /// <summary>Waiting: dead or lobbied, with the run still going.</summary>
    public static bool Waiting(IPlaySession s) => !s.Player.Alive && s.World.Run is not { Over: true };

    static List<int> Living(IPlaySession s) => [.. s.Everyone().Where(c => c.Id != s.PlayerId && c.State.Alive).Select(c => c.Id).Order()];

    /// <summary>D.10: watch the next (or the previous) living crewmate. Living crew only; there's no free camera.</summary>
    public void Cycle(IPlaySession s, int step)
    {
        var living = Living(s);
        if (!Waiting(s) || living.Count == 0)
            return;
        int at = living.IndexOf(s.Following);
        int next = at < 0 ? (step > 0 ? 0 : living.Count - 1) : ((at + step) % living.Count + living.Count) % living.Count;
        s.Request(new Request(RequestKind.Follow, living[next]));
    }

    /// <summary>
    /// The Holdout a Call Out would come from (D.7): the one this player's assigned to if it can, otherwise the nearest
    /// one to the eyes they're watching through that can (Occupied or Breaching, a living crewmate in range, its cooldown
    /// run down). Null when none can.
    /// </summary>
    public static Holdout? CallOutFrom(IPlaySession s)
    {
        if (!Waiting(s) || s.World.Holdouts is not { } holdouts)
            return null;
        var crew = s.Everyone().Select(c => c.State).ToList();
        var mine = holdouts.Queue.Of(s.PlayerId)?.Holdout is { } id ? holdouts.Of(id) : null;
        if (mine is not null && DeadPhase.CanCallOut(s.World, mine, crew))
            return mine;
        var eyes = PlayerMotor.WorldPosition(s.Viewpoint, s.Train);
        return holdouts.All.Where(h => DeadPhase.CanCallOut(s.World, h, crew)).MinBy(h => (h.Centre - eyes).Length);
    }

    public void CallOut(IPlaySession s)
    {
        if (CallOutFrom(s) is { } h)
            s.Request(new Request(RequestKind.CallOut, h.Index));
    }

    /// <summary>The Holdout this player's assigned to (D.5), if any: its Live Mic is theirs to switch.</summary>
    public static Holdout? Assigned(IPlaySession s) =>
        Waiting(s) && s.World.Holdouts is { } holdouts && holdouts.Queue.Of(s.PlayerId)?.Holdout is { } id ? holdouts.Of(id) : null;

    public void ToggleLiveMic(IPlaySession s)
    {
        if (Assigned(s) is { } h)
            s.Request(new Request(RequestKind.LiveMic, h.LiveMic ? 0 : 1));
    }

    /// <summary>D.6: let the next one in the queue go first (one place down; the host refuses it once a breach has you).</summary>
    public void Defer(IPlaySession s)
    {
        if (Waiting(s) && s.World.Holdouts?.Queue.PositionOf(s.PlayerId) is >= 0 and var at)
            s.Request(new Request(RequestKind.Defer, at + 1));
    }

    /// <summary>D.11: whether this player has a vote to cast now (dead, not lobbied, not yet voted this run, options on offer).</summary>
    public static bool CanVote(IPlaySession s) =>
        Waiting(s) && !s.Player.Has(PlayerFlags.Lobbied) && s.World.VoteOptions.Count > 0 && s.World.VoteLog.All(v => v.Player != s.PlayerId);

    /// <summary>D.11: vote for the <paramref name="option"/>th creature on offer (0-based).</summary>
    public void Vote(IPlaySession s, int option)
    {
        if (CanVote(s) && option >= 0 && option < s.World.VoteOptions.Count)
            s.Request(new Request(RequestKind.Vote, (int)s.World.VoteOptions[option]));
    }

    /// <summary>D.12: bookmark the moment, while watching someone. The app saves the still (the view as it is now).</summary>
    public bool Bookmark(IPlaySession s)
    {
        if (!Waiting(s) || s.Following < 0)
            return false;
        s.Request(new Request(RequestKind.Bookmark, 0));
        return true;
    }

    /// <summary>The others in the session at the end, whom this player could commend (D.12: never themselves).</summary>
    public static IReadOnlyList<int> Commendable(IPlaySession s) =>
        s.Report is { } r ? [.. r.Session.Where(id => id != s.PlayerId)] : [];

    /// <summary>Whether this player has given their commendation (D.12: one per player per run).</summary>
    public static bool Commended(IPlaySession s) => s.Report?.Commendations.Any(c => c.From == s.PlayerId) == true;

    public void ChooseCommend(IPlaySession s, int step)
    {
        int n = Commendable(s).Count;
        if (n > 0)
            CommendChoice = ((CommendChoice + step) % n + n) % n;
    }

    /// <summary>D.12: give the <paramref name="award"/>th award (0-based) to the chosen crewmate.</summary>
    public void Commend(IPlaySession s, int award)
    {
        var others = Commendable(s);
        if (others.Count == 0 || Commended(s) || s.World.HoldoutTuning is not { } t || award < 0 || award >= t.Commendations.Awards.Length)
            return;
        s.Request(new Request(RequestKind.Commend, others[Math.Clamp(CommendChoice, 0, others.Count - 1)], award));
    }

    /// <summary>
    /// The commendations given this player since the profile last took them (D.12: "stored on the player profile"): each
    /// report revision carries all of them, so only the new ones are the profile's to add.
    /// </summary>
    public IReadOnlyList<string> TakeCommendations(IPlaySession s)
    {
        if (s.Report is not { } r)
            return [];
        var mine = r.To(s.PlayerId).Select(c => c.Award).ToList();
        var fresh = mine.Skip(CommendationsTaken).ToList();
        CommendationsTaken = mine.Count;
        return fresh;
    }
}
