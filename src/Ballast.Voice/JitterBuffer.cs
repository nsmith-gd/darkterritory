namespace Ballast.Voice;

/// <summary>
/// Puts one speaker's packets back in order. Network jitter swaps neighbouring 20 ms frames; decoding them
/// as they arrive would conceal one and drop the other. A packet is held until the one before it arrives, or
/// until it has waited <see cref="HoldSeconds"/>, at which point the gap is given up as lost.
/// </summary>
public sealed class JitterBuffer<T>(double holdSeconds = 0.06)
{
    readonly SortedDictionary<ushort, (T Item, double Arrived)> _held = new(Comparer<ushort>.Create(static (a, b) => (short)(a - b)));
    ushort? _next;

    public double HoldSeconds { get; } = holdSeconds;
    public int Held => _held.Count;

    public void Push(ushort sequence, T item, double now)
    {
        if (_next is { } next && (short)(sequence - next) < 0)
            return; // older than what's already been played
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
