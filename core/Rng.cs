namespace DryTown.Core;

/// <summary>
/// Small seeded PRNG (PCG32). The whole simulation draws from one instance so a
/// seed plus the same orders always replays the same city.
/// </summary>
public sealed class Rng
{
    private ulong _state;
    private const ulong Increment = 1442695040888963407UL;

    public Rng(ulong seed)
    {
        _state = 0;
        NextUInt();
        _state += seed;
        NextUInt();
    }

    public ulong State => _state;

    /// <summary>Resume a generator exactly where a saved game left it.</summary>
    public static Rng FromState(ulong state) => new(0) { _state = state };

    public uint NextUInt()
    {
        ulong old = _state;
        _state = old * 6364136223846793005UL + Increment;
        uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
        int rot = (int)(old >> 59);
        return (xorShifted >> rot) | (xorShifted << ((-rot) & 31));
    }

    /// <summary>Uniform integer in [min, max] inclusive.</summary>
    public int Range(int min, int max)
    {
        if (max < min) throw new ArgumentException("max < min");
        uint span = (uint)(max - min + 1);
        return min + (int)(NextUInt() % span);
    }

    /// <summary>Uniform double in [0, 1).</summary>
    public double NextDouble() => NextUInt() / 4294967296.0;

    public bool Chance(double p) => NextDouble() < p;

    public T Pick<T>(IReadOnlyList<T> items) => items[Range(0, items.Count - 1)];

    public void Shuffle<T>(IList<T> items)
    {
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = Range(0, i);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}
