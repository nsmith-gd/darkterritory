namespace DarkTerritory.Sim.Net;

/// <summary>
/// How much of a stream of numbered messages is getting through (netcode-audit.md gap 3; spec E "ping visibility is
/// load-bearing"): of the last <see cref="Window"/> numbers, the share that never came, or came after a newer one had
/// (stale: no use by then, so lost as far as the game's concerned). The host's snapshots to a client are numbered by its
/// tick, one each tick; a client's inputs by its own sequence, one each tick. Statistics only: nothing in the world reads
/// it, so it can't move player or train state.
/// </summary>
public sealed class LinkLoss
{
    readonly bool[] _heard;
    uint _newest, _first;
    bool _any;

    /// <param name="window">How many of the newest numbers to count over (10 s of ticks, hud.json <c>lossWindowSeconds</c>).</param>
    public LinkLoss(int window)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(window, 1);
        _heard = new bool[window];
    }

    public int Window => _heard.Length;

    /// <summary>How many numbers the share is over: the window, or fewer while the stream's younger than it.</summary>
    public int Span => !_any ? 0 : (int)Math.Min(_heard.Length, (long)_newest - _first + 1);

    /// <summary>Message <paramref name="number"/> arrived.</summary>
    public void Heard(uint number)
    {
        int n = _heard.Length;
        // A stream that starts again from low (a client reconnecting counts its inputs from 1) starts the count again.
        if (!_any || (long)number + n < _newest)
        {
            Array.Clear(_heard);
            _any = true;
            _first = _newest = number;
            _heard[number % n] = true;
            return;
        }
        if (number <= _newest)
            return; // stale or a duplicate: the newer one's already in
        // Everything between the last newest and this one didn't come (yet: it'd be stale if it did).
        for (long s = (long)number - 1; s > _newest && s > (long)number - n; s--)
            _heard[s % n] = false;
        _newest = number;
        _heard[number % n] = true;
    }

    /// <summary>
    /// <see cref="Loss"/> once there's a second of the stream to count over (or the whole window, if that's shorter): one
    /// missed snapshot in the first few isn't a bad link.
    /// </summary>
    public double? Settled => Span >= Math.Min(Window, SimConstants.TickRate) ? Loss : null;

    /// <summary>The share lost over the <see cref="Span"/> (0..1), or null before anything's arrived.</summary>
    public double? Loss
    {
        get
        {
            int span = Span;
            if (span == 0)
                return null;
            int heard = 0;
            for (long s = _newest; s > (long)_newest - span; s--)
                if (_heard[s % _heard.Length])
                    heard++;
            return 1 - (double)heard / span;
        }
    }
}
