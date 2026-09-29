namespace DarkTerritory.Sim.Net;

/// <summary>
/// What the crew have said lately, as the host heard it (T40): each player's last few seconds of Opus frames, and when
/// they last spoke. The Soot Children listen here ("samples crew proximity voice", App. A.4) and the director checks
/// it before sending them: no voice lately, nothing to steal. Host only; nothing here is replicated.
/// </summary>
public sealed class VoiceMemory(int framesKept = 250)
{
    readonly Dictionary<int, LinkedList<(uint Tick, byte[] Opus)>> _heard = new();
    readonly Dictionary<int, uint> _lastSpoke = new();

    /// <summary>Remembers a frame someone said aloud (not on the radio alone: the Soot Children are outside, listening).</summary>
    public void Hear(int player, ReadOnlySpan<byte> opus, uint tick)
    {
        if (!_heard.TryGetValue(player, out var frames))
            _heard[player] = frames = new();
        frames.AddLast((tick, opus.ToArray()));
        while (frames.Count > framesKept)
            frames.RemoveFirst();
        _lastSpoke[player] = tick;
    }

    /// <summary>The tick someone last spoke, if they have.</summary>
    public uint? LastSpoke(int player) => _lastSpoke.TryGetValue(player, out var t) ? t : null;

    /// <summary>Who has spoken since <paramref name="tick"/>.</summary>
    public IEnumerable<int> SpokeSince(uint tick) => _lastSpoke.Where(kv => kv.Value >= tick).Select(kv => kv.Key);

    /// <summary>
    /// The last thing someone said: their most recent run of frames with no pause longer than
    /// <paramref name="gapTicks"/> between them, at most <paramref name="maxFrames"/> of it.
    /// </summary>
    public IReadOnlyList<byte[]> Utterance(int player, int maxFrames, uint gapTicks = 8)
    {
        if (!_heard.TryGetValue(player, out var frames) || frames.Count == 0)
            return [];
        var run = new List<byte[]>();
        uint? after = null;
        for (var node = frames.Last; node is not null && run.Count < maxFrames; node = node.Previous)
        {
            if (after is { } a && a - node.Value.Tick > gapTicks)
                break;
            run.Add(node.Value.Opus);
            after = node.Value.Tick;
        }
        run.Reverse();
        return run;
    }

    /// <summary>Forgets someone (they left).</summary>
    public void Forget(int player)
    {
        _heard.Remove(player);
        _lastSpoke.Remove(player);
    }
}
