namespace Ballast.Voice;

/// <summary>
/// Puts one speaker's packets back in order. Network jitter swaps neighbouring 20 ms frames; decoding them
/// as they arrive would conceal one and drop the other. A packet is held until the one before it arrives, or
/// until it has waited <see cref="HoldSeconds"/>, at which point the gap is given up as lost.
/// </summary>
public sealed class JitterBuffer<T>(double holdSeconds = 0.06)
{
    /// <summary>
    /// How far behind what's been played a packet can be and still be a late one (a second of 20 ms frames). Farther
    /// "behind" is a speaker this listener hasn't heard for half the sequence's wrap (about 11 minutes: out of range, or
    /// silent), whose numbers have come round: a new talk spurt, not an old packet. Dropping those went on for the next
    /// 11 minutes (the director, 24 Oct: a guest's voice gone after they'd been out of earshot).
    /// </summary>
    public const int MaxLate = 50;

    readonly SortedDictionary<ushort, (T Item, double Arrived)> _held = new(Comparer<ushort>.Create(static (a, b) => (short)(a - b)));
    ushort? _next;

    public double HoldSeconds { get; } = holdSeconds;
    public int Held => _held.Count;

    public void Push(ushort sequence, T item, double now)
    {
        if (_next is { } next && (short)(sequence - next) < 0)
        {
            if ((ushort)(next - sequence) <= MaxLate)
                return; // older than what's already been played
            // Come round since this speaker was last heard: start again from here.
            _held.Clear();
            _next = null;
        }
        _held.TryAdd(sequence, (item, now));
    }

    /// <summary>Releases packets in sequence order: every one that's next, plus any that have waited long enough.</summary>
    public void Drain(double now, Action<ushort, T> release)
    {
        while (_held.Count > 0)
        {
            var (seq, (item, arrived)) = _held.First();
            bool isNext = _next is null || seq == _next;
            if (!isNext && now - arrived < HoldSeconds)
                return;
            _held.Remove(seq);
            _next = (ushort)(seq + 1);
            release(seq, item);
        }
    }
}
