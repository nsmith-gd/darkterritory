using Ballast.Net;

namespace Ballast.Dev;

/// <summary>
/// A host's transport with a recorder on it (ARCHITECTURE §8 note 515): what each <see cref="Poll"/> hands the host is
/// written to the log as it passes, and what the host sends between one poll and the next is digested and written when
/// the next poll comes (or the log closes). The host can't tell it's there: every call goes on to the transport it wraps.
/// </summary>
/// <param name="scrub">What a payload is recorded as (the game's: a voice frame's audio blanked, its length kept), or null
/// for as it came.</param>
/// <param name="digested">Whether a send counts in its step's digest (the game's: not the voice it forwards, which a scrubbed
/// recording can't send again the same), or null for all of them.</param>
public sealed class RecordingTransport(ITransport inner, TransportLogWriter log,
    Func<byte[], ReadOnlySpan<byte>>? scrub = null, Func<ReadOnlySpan<byte>, bool>? digested = null) : ITransport
{
    TransportLog.Digest _sent;
    bool _polled;
    long _spent, _step;
    // Each step's cost in whole microseconds, the last bucket everything from a millisecond up: the median a step costs,
    // which a garbage collection or a busy machine's preemption doesn't move the way it moves the mean.
    readonly int[] _steps = new int[1001];
    int _stepCount;

    /// <summary>Time spent recording (copying, digesting, writing records) over the night so far: what it costs the frame.</summary>
    public TimeSpan Spent => System.Diagnostics.Stopwatch.GetElapsedTime(0, _spent);

    /// <summary>What recording cost the median step (from one poll to the next), in microseconds; 0 before the second poll.</summary>
    public double MedianStepMicros
    {
        get
        {
            int seen = 0;
            for (int us = 0; us < _steps.Length; us++)
                if ((seen += _steps[us]) * 2 >= _stepCount && _stepCount > 0)
                    return us;
            return 0;
        }
    }

    void Spend(long since)
    {
        long d = System.Diagnostics.Stopwatch.GetTimestamp() - since;
        _spent += d;
        _step += d;
    }

    public ITransport Inner => inner;
    public TransportLogWriter Log => log;

    /// <summary>Called just before each poll is recorded, so the game can note what changed between steps outside them.</summary>
    public Action? BeforePoll { get; set; }

    public PeerId LocalId => inner.LocalId;

    public void Send(PeerId to, ReadOnlySpan<byte> payload, Delivery delivery)
    {
        long t = System.Diagnostics.Stopwatch.GetTimestamp();
        if (digested is null || digested(payload))
            _sent.Add(to, delivery, payload);
        Spend(t);
        inner.Send(to, payload, delivery);
    }

    public void Poll(List<TransportEvent> into)
    {
        long t = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_polled)
        {
            // The step since the last poll is whole: its sends and that poll's writing.
            _steps[Math.Min(_steps.Length - 1, (int)(_step * 1_000_000 / System.Diagnostics.Stopwatch.Frequency))]++;
            _stepCount++;
        }
        _step = 0;
        CloseStep();
        BeforePoll?.Invoke();
        Spend(t);
        int from = into.Count;
        inner.Poll(into);
        t = System.Diagnostics.Stopwatch.GetTimestamp();
        log.Polled(into, from, scrub);
        _sent = TransportLog.Digest.Start();
        _polled = true;
        Spend(t);
    }

    /// <summary>The last step's sends, written as its digest. Done by the next poll, and by whoever closes the log.</summary>
    public void CloseStep()
    {
        if (_polled)
            log.Sent(_sent);
        _polled = false;
    }

    public void Disconnect(PeerId peer) => inner.Disconnect(peer);

    /// <summary>Disposes the transport it wraps; the log is its owner's to close (<see cref="CloseStep"/> first).</summary>
    public void Dispose() => inner.Dispose();
}

/// <summary>
/// A recorded night played back to a host (note 515): each <see cref="Poll"/> hands it the next recorded poll's events,
/// and what it sends goes nowhere but into that step's digest, to compare with the one recorded. Nothing is connected.
/// </summary>
public sealed class ReplayTransport(Func<ReadOnlySpan<byte>, bool>? digested = null) : ITransport
{
    TransportLog.Digest _sent = TransportLog.Digest.Start();

    /// <summary>The events the next <see cref="Poll"/> hands over.</summary>
    public IReadOnlyList<TransportEvent> Next { get; set; } = [];

    /// <summary>What's been sent since the last poll.</summary>
    public TransportLog.Digest SentSincePoll => _sent;

    public PeerId LocalId => PeerId.Host;

    public void Send(PeerId to, ReadOnlySpan<byte> payload, Delivery delivery)
    {
        if (digested is null || digested(payload))
            _sent.Add(to, delivery, payload);
    }

    public void Poll(List<TransportEvent> into)
    {
        into.AddRange(Next);
        Next = [];
        _sent = TransportLog.Digest.Start();
    }

    public void Disconnect(PeerId peer)
    {
    }

    public void Dispose()
    {
    }
}
