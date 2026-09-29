using Ballast;

namespace DarkTerritory.Sim.LineGen;

/// <summary>
/// The generator's random streams (plan §4, §20.1): SplitMix64 for seeding, PCG32 for the streams. Every stage draws
/// from its own stream, sub-seeded from the run seed, the stage's name, the edge and an index, so changing one stage's
/// logic never reshuffles another's output. Names are hashed with FNV-1a over their UTF-16 code units: fixed by
/// definition, unlike <see cref="string.GetHashCode()"/>, which is seeded afresh every process.
/// </summary>
public static class Streams
{
    public static ulong SplitMix64(ulong x)
    {
        x += 0x9E3779B97F4A7C15UL;
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
        return x ^ (x >> 31);
    }

    public static ulong Hash(string text)
    {
        ulong h = 0xCBF29CE484222325UL;
        foreach (char c in text)
        {
            h ^= c;
            h *= 0x100000001B3UL;
        }
        return h;
    }

    /// <summary>Folds values into one seed: <c>hash(runSeed, stageName, edgeId, index)</c>.</summary>
    public static ulong Mix(ulong seed, string stage, string edge = "", long index = 0) =>
        SplitMix64(SplitMix64(SplitMix64(seed ^ Hash(stage)) ^ Hash(edge)) ^ (ulong)index);

    /// <summary>A stage's stream.</summary>
    public static Pcg32 Rng(ulong seed, string stage, string edge = "", long index = 0)
    {
        ulong s = Mix(seed, stage, edge, index);
        return new Pcg32(s, SplitMix64(s ^ 0xA5A5A5A5A5A5A5A5UL));
    }

    /// <summary>A lattice value in [−1, 1] from integer coordinates: the terrain's noise (plan §17.3: integer-hash noise).</summary>
    public static double Lattice(ulong seed, long x, long z)
    {
        ulong h = SplitMix64(seed ^ SplitMix64((ulong)x * 0x9E3779B97F4A7C15UL ^ (ulong)z * 0xC2B2AE3D27D4EB4FUL));
        return (h >> 11) * (2.0 / (1UL << 53)) - 1;
    }
}

/// <summary>Rolls and weighted picks on a stream.</summary>
public static class RngExtensions
{
    /// <summary>A count from a [min, max] range that may be fractional (a lerped tier range): each end rounds by chance.</summary>
    public static int Count(ref this Pcg32 rng, double min, double max)
    {
        int lo = rng.Round(min), hi = Math.Max(lo, rng.Round(max));
        return rng.RangeInclusive(lo, hi);
    }

    /// <summary>Rounds down or up by the fraction's chance.</summary>
    public static int Round(ref this Pcg32 rng, double value)
    {
        double f = Math.Floor(value);
        return (int)f + (rng.Chance(value - f) ? 1 : 0);
    }

    public static double Range(ref this Pcg32 rng, double[] range) => range.Length < 2 ? range.Length == 1 ? range[0] : 0 : rng.Range(range[0], range[1]);

    /// <summary>A weighted pick; items with no weight are never picked. Returns default when nothing has weight.</summary>
    public static T? Weighted<T>(ref this Pcg32 rng, IReadOnlyList<(T Item, double Weight)> items)
    {
        double total = 0;
        foreach (var (_, w) in items)
            total += Math.Max(0, w);
        if (total <= 0)
            return default;
        double pick = rng.NextDouble() * total;
        foreach (var (item, w) in items)
        {
            if (w <= 0)
                continue;
            if (pick < w)
                return item;
            pick -= w;
        }
        return items.Last(i => i.Weight > 0).Item;
    }
}
