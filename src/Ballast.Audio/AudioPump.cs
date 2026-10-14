using System.Diagnostics;

namespace Ballast.Audio;

/// <summary>
/// Keeps an output device fed from a <see cref="Mixer"/> on a thread of its own (note 667; the director, 13 Oct, of the
/// morning build: "I'm getting audio skips"). Fed from the game's frame, the device ran dry whenever a frame took longer
/// than what was queued (60 ms): every slow frame was a dropout as long as the frame, less the 60 ms. Here a block renders
/// whenever the device holds less than <see cref="Lead"/>, whatever the frame is doing, so a slow frame is a frame late on
/// screen and nothing in the ears. The game goes on changing the mix from its own thread: what the two share (the voices,
/// a voice's parameters, a voice chat's stream, the decoded takes) is locked where it's kept (<see cref="Mixer.Gate"/>), and
/// the rest (a voice's position and level) is read a block late at worst.
/// <para>
/// What it can't ride out is being kept from running for longer than the lead: a blocking garbage collection stops every
/// managed thread (the device's own thread is native, and plays what's queued until it runs dry), and a machine whose
/// cores are all busy can leave it waiting. So while <see cref="Adapt"/> is on (the game playing, not loading), each time
/// the device runs dry the lead grows by <see cref="Step"/>, up to <see cref="MaxLead"/>: a machine that never stalls keeps
/// the short lead, one that does hears a gap or two and then none, a little later. <see cref="Underruns"/> counts every
/// time it ran dry, for FrameWatch's lines (note 549), so a gap the player heard is on the log beside the frame it came in.
/// </para>
/// </summary>
public sealed class AudioPump : IDisposable
{
    readonly Mixer _mixer;
    readonly Func<double> _queued;
    readonly Action<float[]> _write;
    readonly float[] _block = new float[Audio.Block * 2];
    readonly Thread _thread;
    volatile bool _running = true;
    volatile bool _mute;
    volatile bool _adapt;
    double _lead;
    int _underruns;
    long _longestBlockTicks, _longestStallTicks;

    /// <param name="queuedSeconds">How much the device has queued and not yet played, in seconds.</param>
    /// <param name="write">Queues one block of interleaved stereo at the device (it copies it).</param>
    /// <param name="leadSeconds">How much to keep queued: the latency every sound has, and the longest the thread can be
    /// kept from rendering (by the gate, the scheduler or a collection) before the device runs dry.</param>
    /// <param name="maxLeadSeconds">The most <see cref="Adapt"/> grows the lead to.</param>
    public AudioPump(Mixer mixer, Func<double> queuedSeconds, Action<float[]> write, double leadSeconds, double maxLeadSeconds)
    {
        _mixer = mixer;
        _queued = queuedSeconds;
        _write = write;
        _lead = leadSeconds;
        MaxLead = Math.Max(leadSeconds, maxLeadSeconds);
        // Above the game's threads: the device's deadline is the one a player hears missed.
        _thread = new Thread(Run) { IsBackground = true, Name = "audio", Priority = ThreadPriority.Highest };
        _thread.Start();
    }

    /// <summary>How much more the lead grows each time the device runs dry while adapting.</summary>
    public const double Step = 0.02;

    /// <summary>How much it keeps queued now, in seconds.</summary>
    public double Lead => Volatile.Read(ref _lead);

    public double MaxLead { get; }

    /// <summary>
    /// The device running dry grows the lead. Off while the game loads (a night's first frames build its whole scene and
    /// collect most of a gigabyte, in silence): a gap nobody hears shouldn't cost every later sound its latency.
    /// </summary>
    public bool Adapt { get => _adapt; set => _adapt = value; }

    /// <summary>Silence to the device, the mix still running (the settings' mute: sounds go on, unheard).</summary>
    public bool Mute { get => _mute; set => _mute = value; }

    /// <summary>Times the device was found empty after it had been fed: each a gap the player heard.</summary>
    public int Underruns => Volatile.Read(ref _underruns);

    /// <summary>The longest one block has taken to render (waiting on the gate included), in seconds.</summary>
    public double LongestBlock => Volatile.Read(ref _longestBlockTicks) / (double)Stopwatch.Frequency;

    /// <summary>
    /// The longest the thread went between two looks at the device, in seconds: over <see cref="Lead"/>, it was kept from
    /// running (a collection that stopped every thread, or the machine) and the device ran dry; a long block counts too.
    /// </summary>
    public double LongestStall => Volatile.Read(ref _longestStallTicks) / (double)Stopwatch.Frequency;

    void Run()
    {
        bool fed = false;
        double blockSeconds = (double)Audio.Block / Audio.SampleRate;
        long last = Stopwatch.GetTimestamp();
        while (_running)
        {
            long now = Stopwatch.GetTimestamp();
            if (fed && now - last > _longestStallTicks)
                Volatile.Write(ref _longestStallTicks, now - last);
            last = now;
            double queued = _queued();
            if (fed && queued <= 0)
            {
                Interlocked.Increment(ref _underruns);
                if (_adapt)
                    Volatile.Write(ref _lead, Math.Min(MaxLead, _lead + Step));
            }
            double lead = _lead;
            if (queued >= lead)
            {
                // A millisecond (or the system timer's 15.6 ms, where nobody's asked for finer): either is well inside
                // the lead.
                Thread.Sleep(1);
                continue;
            }
            for (; queued < lead && _running; queued += blockSeconds)
            {
                long start = Stopwatch.GetTimestamp();
                _mixer.Render(_block);
                long took = Stopwatch.GetTimestamp() - start;
                if (took > _longestBlockTicks)
                    Volatile.Write(ref _longestBlockTicks, took);
                if (_mute)
                    Array.Clear(_block);
                _write(_block);
            }
            fed = true;
        }
    }

    public void Dispose()
    {
        _running = false;
        _thread.Join();
    }
}
