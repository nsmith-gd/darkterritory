namespace Ballast;

/// <summary>
/// PCG32 random number generator (O'Neill, pcg-random.org). Used for anything generated from a seed
/// (routes, spawns, loot) because its output is fixed by definition: the same seed gives the same
/// run on every machine, OS and .NET version, which System.Random does not promise.
/// </summary>
public struct Pcg32
{
    ulong _state;
    readonly ulong _inc;

    public Pcg32(ulong seed, ulong stream = 0xDA3E39CB94B95BDBUL)
    {
        _state = 0;
        _inc = (stream << 1) | 1;
        NextUInt();
        _state += seed;
        NextUInt();
    }

    public uint NextUInt()
    {
        ulong old = _state;
        _state = old * 6364136223846793005UL + _inc;
        uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
        int rot = (int)(old >> 59);
        return (xorShifted >> rot) | (xorShifted << (-rot & 31));
    }

    /// <summary>Uniform in [0, 1).</summary>
    public double NextDouble() => NextUInt() * (1.0 / 4294967296.0);

    /// <summary>Uniform in [min, max).</summary>
    public double Range(double min, double max) => min + (max - min) * NextDouble();

    /// <summary>Uniform integer in [min, max] inclusive.</summary>
    public int RangeInclusive(int min, int max) => min + (int)(NextDouble() * (max - min + 1));

    public bool Chance(double p) => NextDouble() < p;

    public T Pick<T>(IReadOnlyList<T> items) => items[(int)(NextDouble() * items.Count)];

    /// <summary>An independent generator for a sub-system, so adding draws in one place doesn't reshuffle another.</summary>
    public Pcg32 Fork(ulong stream) => new(NextUInt() | ((ulong)NextUInt() << 32), stream);
}
