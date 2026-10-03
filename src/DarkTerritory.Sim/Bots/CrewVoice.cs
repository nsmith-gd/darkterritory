using Ballast;

namespace DarkTerritory.Sim.Bots;

/// <summary>
/// How poor the crew's voice is (GDD §34 "Degraded comms: agents on lossy, laggy, restricted voice — simulating people
/// shouting over each other"; note 186). Mirror of a <c>comms</c> entry in content/tuning/balance.json; field docs there.
/// </summary>
public sealed record VoiceConditions(double Loss = 0, double LatencySeconds = 0, double JitterSeconds = 0, double TalkOver = 0,
    double TalkOverSeconds = 1.5)
{
    public static readonly VoiceConditions Clear = new();
}

/// <summary>What got through on the crew's voice over a night (the harness report's <c>voice</c>).</summary>
/// <param name="Calls">Things said that changed what the speaker was saying (a repeat of the same isn't a new call).</param>
/// <param name="Repeats">The same said again (bots say where they are every tick, as a crew keeps up a running commentary).</param>
/// <param name="Lost">Calls and repeats that never arrived: dropped, or talked over.</param>
/// <param name="TalkedOver">Of the lost, the calls drowned by someone else talking at once.</param>
/// <param name="Stale">Arrived after something newer from the same speaker about the same thing, so not heard as news.</param>
/// <param name="MeanDelaySeconds">From saying a call to the crew hearing it, over the calls that arrived.</param>
public sealed record VoiceReport(string Conditions, int Calls, int Repeats, int Lost, int TalkedOver, int Stale, double MeanDelaySeconds);

/// <summary>
/// The crew's voice between bots (note 186): what <see cref="CrewCalls"/> carries goes through this when it's set, late, or
/// not at all. Deterministic: one seeded generator, drawn in the order the bots speak (the harness's fixed order), and what
/// arrives is applied in the order it arrives (tick, then the order it was said). People hear each other; the bots' shared
/// board stands in for that, so a lost call is one nobody heard, the speaker included (they'll say it again next tick).
/// </summary>
public sealed class CrewVoice(VoiceConditions conditions, ulong seed)
{
    readonly struct Pending(uint at, long seq, string key, Action apply, uint said, bool call)
    {
        public uint At { get; } = at;
        public long Seq { get; } = seq;
        public string Key { get; } = key;
        public Action Apply { get; } = apply;
        public uint Said { get; } = said;
        public bool Call { get; } = call;
    }

    Pcg32 _rng = new(seed, 0x5EED_0F_70_1CEUL);
    readonly List<Pending> _queue = [];
    readonly Dictionary<string, object> _last = new(StringComparer.Ordinal);
    readonly Dictionary<string, long> _heard = new(StringComparer.Ordinal);
    readonly List<(uint Tick, int Speaker)> _talking = [];
    long _seq;
    int _calls, _repeats, _lost, _talkedOver, _stale, _arrived;
    double _delay;

    public VoiceConditions Conditions { get; } = conditions;

    /// <summary>
    /// <paramref name="speaker"/> says <paramref name="value"/> about <paramref name="key"/> at <paramref name="tick"/>;
    /// <paramref name="apply"/> is what hearing it does.
    /// </summary>
    public void Say(uint tick, int speaker, string key, object value, Action apply)
    {
        bool call = !(_last.TryGetValue(key, out var was) && Equals(was, value));
        _last[key] = value;
        if (call)
            _calls++;
        else
            _repeats++;
        var c = Conditions;
        uint window = (uint)Math.Round(c.TalkOverSeconds * SimConstants.TickRate);
        _talking.RemoveAll(t => tick - t.Tick > window);
        // Shouting over each other: a new call is lost the more others are calling at once (a repeat is a running mutter).
        int others = call ? _talking.Where(t => t.Speaker != speaker).Select(t => t.Speaker).Distinct().Count() : 0;
        if (call)
            _talking.Add((tick, speaker));
        double drop = _rng.NextDouble(), over = _rng.NextDouble(), jitter = _rng.NextDouble();
        if (drop < c.Loss)
        {
            _lost++;
            return;
        }
        if (others > 0 && over < c.TalkOver * others)
        {
            _lost++;
            _talkedOver++;
            return;
        }
        double delay = Math.Max(0, c.LatencySeconds + c.JitterSeconds * (2 * jitter - 1));
        uint at = tick + (uint)Math.Round(delay * SimConstants.TickRate);
        _queue.Add(new Pending(at, _seq++, key, apply, tick, call));
    }

    /// <summary>Everything due by <paramref name="tick"/> is heard, in the order it arrives; older news than what's been heard is stale.</summary>
    public void Deliver(uint tick)
    {
        if (_queue.Count == 0)
            return;
        var due = _queue.Where(p => p.At <= tick).OrderBy(p => p.At).ThenBy(p => p.Seq).ToList();
        if (due.Count == 0)
            return;
        _queue.RemoveAll(p => p.At <= tick);
        foreach (var p in due)
        {
            if (_heard.TryGetValue(p.Key, out long newest) && newest > p.Seq)
            {
                _stale++;
                continue;
            }
            _heard[p.Key] = p.Seq;
            p.Apply();
            if (p.Call)
            {
                _arrived++;
                _delay += (tick - p.Said) * SimConstants.TickSeconds;
            }
        }
    }

    public VoiceReport Report() => new(
        $"loss {Conditions.Loss:P0}, {Conditions.LatencySeconds * 1000:0} ms ±{Conditions.JitterSeconds * 1000:0}, talk-over {Conditions.TalkOver:0.##}",
        _calls, _repeats, _lost, _talkedOver, _stale, Math.Round(_arrived == 0 ? 0 : _delay / _arrived, 2));
}
