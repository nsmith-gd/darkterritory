namespace Ballast.Audio;

/// <summary>
/// A mono sample queue between something producing audio as it arrives (decoded voice) and the mixer.
/// It waits for <see cref="PrebufferSamples"/> before playing, and again after running dry, which absorbs
/// network jitter; past <see cref="MaxSamples"/> it drops the oldest to keep latency bounded. Written by the game and read
/// by the device's thread (note 667), so every touch of the queue is locked.
/// </summary>
public sealed class StreamBuffer(int prebufferSamples = 2880, int maxSamples = 14400)
{
    readonly Queue<float> _samples = new();
    bool _playing;

    public int PrebufferSamples { get; } = prebufferSamples;
    public int MaxSamples { get; } = maxSamples;
    public int Buffered
    {
        get
        {
            lock (_samples)
                return _samples.Count;
        }
    }
    public int Underruns { get; private set; }
    public bool Playing => _playing;

    public void Write(ReadOnlySpan<float> samples)
    {
        lock (_samples)
        {
            foreach (float s in samples)
                _samples.Enqueue(s);
            while (_samples.Count > MaxSamples)
                _samples.Dequeue();
        }
    }

    /// <summary>Drops everything waiting, at once: no tail, no fade (a voice cut off mid-word).</summary>
    public void Clear()
    {
        lock (_samples)
        {
            _samples.Clear();
            _playing = false;
        }
    }

    /// <summary>Fills <paramref name="into"/>; silence while prebuffering or dry.</summary>
    public void Read(Span<float> into)
    {
        lock (_samples)
            ReadLocked(into);
    }

    void ReadLocked(Span<float> into)
    {
        if (!_playing && _samples.Count >= PrebufferSamples)
            _playing = true;
        for (int i = 0; i < into.Length; i++)
        {
            if (_playing && _samples.TryDequeue(out float s))
            {
                into[i] = s;
                continue;
            }
            if (_playing)
            {
                _playing = false;
                Underruns++;
            }
            into[i] = 0;
        }
    }
}
