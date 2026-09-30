namespace DarkTerritory.Sim.Run;

/// <summary>Who's waiting (GDD App. D.6): a dead player, or a lobbied one (joined mid-run, never yet in the crew).</summary>
public enum QueueKind : byte { Dead, Lobbied }

/// <summary>
/// One place in the respawn queue (D.6).
/// </summary>
/// <param name="Since">When they joined it: the time of death, or of joining the session (sim seconds).</param>
public sealed record QueueEntry(int Player, QueueKind Kind, double Since)
{
    /// <summary>
    /// D.5 eligibility: the Holdout sites whose zone held this player's most recent death. They're skipped there, and keep
    /// their place.
    /// </summary>
    public IReadOnlyList<string> DiedIn { get; init; } = [];
    /// <summary>The Holdout they're assigned to (its id), or null while they're waiting for one.</summary>
    public string? Holdout { get; init; }
    /// <summary>Their Holdout's breach has started (D.5 "assignment is now locked"): they can't defer out of it.</summary>
    public bool Locked { get; init; }
}

/// <summary>What happened to the queue, for the harness's integrity audit (D.14 "queue integrity").</summary>
public enum QueueOp : byte { Joined, Deferred, Assigned, Released, Freed, Removed }

/// <param name="Before">The queue's players in order before, and <paramref name="After"/> after.</param>
public sealed record QueueChange(QueueOp Op, int Player, IReadOnlyList<int> Before, IReadOnlyList<int> After);

/// <summary>
/// The respawn queue (GDD App. D.6): one shared queue of the dead, by time of death, and the lobbied, by time of joining.
/// Pure and engine-free: the host keeps it, the Holdouts take from it, and only the dead and lobbied ever see it.
/// <list type="bullet">
/// <item><b>Defer:</b> a player can move down, to any position behind them. <b>No jumping:</b> nobody moves themselves up;
/// positions only improve as the people ahead leave the queue (freed, or gone).</item>
/// <item><b>Assignment:</b> a Holdout takes the first eligible entry. Skipped entries keep their place, and so does the one
/// assigned: it stays where it stands, so a missed rescue (released) is back where it was, normally the front.</item>
/// <item><b>Disconnect:</b> the entry goes; rejoining is a new lobbied entry at the back.</item>
/// </list>
/// Every change is checked against the rule that orders only change by a deferrer moving back (<see cref="Violations"/>).
/// </summary>
public sealed class RespawnQueue
{
    readonly List<QueueEntry> _entries = new();

    public IReadOnlyList<QueueEntry> Entries => _entries;
    public int Count => _entries.Count;
    /// <summary>Every change, oldest first (the harness reads it; tests too).</summary>
    public List<QueueChange> History { get; } = new();
    /// <summary>Changes that moved someone ahead of a person who was ahead of them, other than by that person deferring.</summary>
    public int Violations { get; private set; }

    /// <summary>A player's place, from 0 at the front; −1 when they're not in the queue.</summary>
    public int PositionOf(int player) => _entries.FindIndex(e => e.Player == player);
    public QueueEntry? Of(int player) => _entries.Find(e => e.Player == player);
    public QueueEntry? AssignedTo(string holdout) => _entries.Find(e => e.Holdout == holdout);
    public IReadOnlyList<int> Order => [.. _entries.Select(e => e.Player)];

    /// <summary>D.6 "order": a death joins at the back (nobody who's waiting died after them).</summary>
    /// <param name="diedIn">The Holdout sites whose zones the death was in (D.5 eligibility).</param>
    public QueueEntry Died(int player, double seconds, IReadOnlyList<string>? diedIn = null) =>
        Join(new QueueEntry(player, QueueKind.Dead, seconds) { DiedIn = diedIn ?? [] });

    /// <summary>D.3 "mid-run join": to the back as a lobbied player (and D.6 "rejoining creates a new lobbied entry at the back").</summary>
    public QueueEntry Lobbied(int player, double seconds) => Join(new QueueEntry(player, QueueKind.Lobbied, seconds));

    QueueEntry Join(QueueEntry entry)
    {
        var before = Order;
        // One entry a player: a second would be a second way back. (Taking your own place again is only ever a move back.)
        _entries.RemoveAll(e => e.Player == entry.Player);
        _entries.Add(entry);
        Record(QueueOp.Joined, entry.Player, before, entry.Player);
        return entry;
    }

    /// <summary>
    /// D.6 "defer": down to <paramref name="toPosition"/>, which must be behind where they are. An assigned player gives
    /// up their Holdout by it (D.5 "reassign while Occupied"); one whose breach has started can't (it's locked).
    /// </summary>
    public bool Defer(int player, int toPosition)
    {
        int at = PositionOf(player);
        if (at < 0 || toPosition <= at || toPosition >= _entries.Count || _entries[at].Locked)
            return false;
        var before = Order;
        var entry = _entries[at] with { Holdout = null };
        _entries.RemoveAt(at);
        _entries.Insert(toPosition, entry);
        Record(QueueOp.Deferred, player, before, player);
        return true;
    }

    /// <summary>
    /// D.5 "assign": the first entry waiting (not already assigned) that <paramref name="eligible"/> passes takes the
    /// Holdout. The rest keep their places. Null when nobody's eligible.
    /// </summary>
    public QueueEntry? Assign(string holdout, Func<QueueEntry, bool> eligible)
    {
        if (AssignedTo(holdout) is { } already)
            return already;
        int i = _entries.FindIndex(e => e.Holdout is null && eligible(e));
        if (i < 0)
            return null;
        var before = Order;
        _entries[i] = _entries[i] with { Holdout = holdout, Locked = false };
        Record(QueueOp.Assigned, _entries[i].Player, before, null);
        return _entries[i];
    }

    /// <summary>D.5 "breach starts": the Holdout's assignment is locked (or unlocked again when the breach is interrupted).</summary>
    public void Lock(string holdout, bool locked)
    {
        int i = _entries.FindIndex(e => e.Holdout == holdout);
        if (i >= 0)
            _entries[i] = _entries[i] with { Locked = locked };
    }

    /// <summary>
    /// D.5 "release" / D.6 "missed rescue": the Holdout lets its player go; they're waiting again in the place they held.
    /// </summary>
    public QueueEntry? Release(string holdout)
    {
        int i = _entries.FindIndex(e => e.Holdout == holdout);
        if (i < 0)
            return null;
        var before = Order;
        _entries[i] = _entries[i] with { Holdout = null, Locked = false };
        Record(QueueOp.Released, _entries[i].Player, before, null);
        return _entries[i];
    }

    /// <summary>D.5 "freed": the Holdout's player is back in the crew, and out of the queue.</summary>
    public QueueEntry? Freed(string holdout)
    {
        int i = _entries.FindIndex(e => e.Holdout == holdout);
        if (i < 0)
            return null;
        var entry = _entries[i];
        var before = Order;
        _entries.RemoveAt(i);
        Record(QueueOp.Freed, entry.Player, before, null);
        return entry;
    }

    /// <summary>D.6 "disconnect": the entry is removed. Returns the Holdout it had, if any, for that to reassign.</summary>
    public string? Remove(int player)
    {
        int i = PositionOf(player);
        if (i < 0)
            return null;
        var entry = _entries[i];
        var before = Order;
        _entries.RemoveAt(i);
        Record(QueueOp.Removed, player, before, null);
        return entry.Holdout;
    }

    /// <summary>Client side: the host's queue, as it is (no history: the host audits its own).</summary>
    public void Mirror(IEnumerable<QueueEntry> entries)
    {
        _entries.Clear();
        _entries.AddRange(entries);
    }

    /// <summary>Run start (D.3): everyone spawns at the fortress, and the queue is empty when the gates open.</summary>
    public void Clear()
    {
        foreach (var e in _entries.ToList())
            Remove(e.Player);
    }

    void Record(QueueOp op, int player, IReadOnlyList<int> before, int? deferrer)
    {
        var after = Order;
        History.Add(new QueueChange(op, player, before, after));
        if (!Holds(before, after, deferrer))
            Violations++;
    }

    /// <summary>
    /// D.14 "queue integrity": nobody moves ahead of someone who was ahead of them, except by that person deferring behind
    /// them (or leaving); anyone new is at the back.
    /// </summary>
    public static bool Holds(IReadOnlyList<int> before, IReadOnlyList<int> after, int? deferrer)
    {
        var was = new Dictionary<int, int>();
        for (int i = 0; i < before.Count; i++)
            was[before[i]] = i;
        bool newcomers = false;
        for (int i = 0; i < after.Count; i++)
        {
            if (!was.ContainsKey(after[i]))
                newcomers = true;
            else if (newcomers)
                return false; // someone new ahead of someone who was already waiting
        }
        var stayed = after.Where(was.ContainsKey).ToList();
        for (int i = 0; i < stayed.Count; i++)
            for (int j = i + 1; j < stayed.Count; j++)
                // stayed[i] is now ahead of stayed[j]: fine if it was before, or if stayed[j] is the one who deferred.
                if (was[stayed[i]] > was[stayed[j]] && stayed[j] != deferrer)
                    return false;
        return true;
    }
}
