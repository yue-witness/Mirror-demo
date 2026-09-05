using System;

/// <summary>
/// Small deterministic generator whose cursor can be stored in a save file.
/// </summary>
public sealed class SessionRandom
{
    public SessionRandom(int seed, int step = 0)
    {
        Seed = seed;
        Step = Math.Max(0, step);
    }

    public int Seed { get; }

    public int Step { get; private set; }

    public int Next(int minimumInclusive, int maximumExclusive)
    {
        if (maximumExclusive <= minimumInclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumExclusive));
        }

        uint mixed = Mix(
            unchecked((uint)Seed)
            + (0x9E3779B9u * unchecked((uint)(Step + 1))));
        Step++;

        uint range = unchecked((uint)(maximumExclusive - minimumInclusive));
        return minimumInclusive + unchecked((int)(mixed % range));
    }

    public float NextSingle()
    {
        return Next(0, 1_000_000) / 1_000_000f;
    }

    private static uint Mix(uint value)
    {
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        value ^= value >> 16;
        return value;
    }
}
