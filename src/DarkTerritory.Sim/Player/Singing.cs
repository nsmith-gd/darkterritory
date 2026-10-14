using Ballast;

namespace DarkTerritory.Sim.Player;

/// <summary>
/// Singing together (GDD §14; the director's calls of 14 Oct 2026, friendslop.md queue #454: "Singing does not fight the
/// choir"; ARCHITECTURE §8 note 720), heard on the host from the voice level every intent carries (App. C.7). Two or more
/// living crewmates within earshot of each other are singing when their voices rise and fall together over the window:
/// each held over the floor on average, each moving (a held drone isn't a song), and the two in time, their envelopes'
/// correlation over <see cref="SingingTuning.InTime"/>. Talk doesn't do that: two people talking at once take turns, and
/// their levels go against each other or past each other. Integer sums over the window's bytes, compared without a root, so
/// a replayed night hears the same song on the same tick.
/// </summary>
public sealed class Singing
{
    // Each crewmate's voice level over the last window, a ring all of them share the head of.
    readonly SortedDictionary<int, byte[]> _heard = [];
    readonly SortedSet<int> _singers = [];
    int _window, _head;

    /// <summary>Who's singing with someone, as of the last step.</summary>
    public IReadOnlySet<int> Singers => _singers;

    /// <summary>
    /// A tick heard: each crewmate's id, where they are, whether they're alive and their voice level. The dead, and anyone not
    /// in this tick's crew, are silent from here.
    /// </summary>
    public void Step(IReadOnlyList<(int Id, Double3 At, bool Alive, byte Voice)> crew, SingingTuning t)
    {
        int window = Math.Max(2, (int)Math.Round(t.WindowSeconds * SimConstants.TickRate));
        if (window != _window)
        {
            _heard.Clear();
            (_window, _head) = (window, 0);
        }
        foreach (int gone in _heard.Keys.Where(id => !crew.Any(c => c.Id == id)).ToList())
            _heard.Remove(gone);
        foreach (var c in crew)
        {
            if (!_heard.TryGetValue(c.Id, out var ring))
                _heard[c.Id] = ring = new byte[window];
            ring[_head] = c.Alive ? c.Voice : (byte)0;
        }
        _head = (_head + 1) % window;
        _singers.Clear();
        var living = crew.Where(c => c.Alive).OrderBy(c => c.Id).ToList();
        for (int i = 0; i < living.Count; i++)
            for (int j = i + 1; j < living.Count; j++)
                if ((living[i].At - living[j].At).Length <= t.Earshot && InTime(_heard[living[i].Id], _heard[living[j].Id], t))
                {
                    _singers.Add(living[i].Id);
                    _singers.Add(living[j].Id);
                }
    }

    /// <summary>A new night: nobody's been heard.</summary>
    public void Clear()
    {
        _heard.Clear();
        _singers.Clear();
        _head = 0;
    }

    /// <summary>
    /// Whether two voices over the same window are singing together: each over <see cref="SingingTuning.Floor"/> on average,
    /// each spread by at least <see cref="SingingTuning.Swing"/> (its standard deviation), and their correlation at least
    /// <see cref="SingingTuning.InTime"/>.
    /// </summary>
    public static bool InTime(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, SingingTuning t)
    {
        long n = Math.Min(a.Length, b.Length), sa = 0, sb = 0, saa = 0, sbb = 0, sab = 0;
        if (n < 2)
            return false;
        for (int k = 0; k < n; k++)
        {
            long x = a[k], y = b[k];
            sa += x;
            sb += y;
            saa += x * x;
            sbb += y * y;
            sab += x * y;
        }
        if (sa < t.Floor * n || sb < t.Floor * n)
            return false;
        // n² times each one's variance, and n² times their covariance.
        long va = n * saa - sa * sa, vb = n * sbb - sb * sb, cov = n * sab - sa * sb;
        long swing = (long)t.Swing * t.Swing * n * n;
        if (va < swing || vb < swing || va <= 0 || vb <= 0 || cov <= 0)
            return false;
        return (double)cov * cov >= t.InTime * t.InTime * ((double)va * vb);
    }
}
