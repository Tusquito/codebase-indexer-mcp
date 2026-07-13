using System.Text;

namespace CodebaseIndexer.Infrastructure.Embedding;

internal static class Bm25MurmurHash3
{
    public static int ComputeTokenId(string token) =>
        Math.Abs(Hash32(Encoding.UTF8.GetBytes(token), seed: 0));

    private static int Hash32(ReadOnlySpan<byte> data, uint seed)
    {
        const uint c1 = 0xcc9e2d51;
        const uint c2 = 0x1b873593;

        var hash = seed;
        var offset = 0;
        var remaining = data.Length;

        while (remaining >= 4)
        {
            var k = BitConverter.ToUInt32(data.Slice(offset, 4));
            k *= c1;
            k = RotateLeft(k, 15);
            k *= c2;

            hash ^= k;
            hash = RotateLeft(hash, 13);
            hash = (hash * 5) + 0xe6546b64;

            offset += 4;
            remaining -= 4;
        }

        uint tail = 0;
        switch (remaining)
        {
            case 3:
                tail ^= (uint)data[offset + 2] << 16;
                goto case 2;
            case 2:
                tail ^= (uint)data[offset + 1] << 8;
                goto case 1;
            case 1:
                tail ^= data[offset];
                tail *= c1;
                tail = RotateLeft(tail, 15);
                tail *= c2;
                hash ^= tail;
                break;
        }

        hash ^= (uint)data.Length;
        hash = FMix(hash);
        return unchecked((int)hash);
    }

    private static uint RotateLeft(uint value, int bits) => (value << bits) | (value >> (32 - bits));

    private static uint FMix(uint hash)
    {
        hash ^= hash >> 16;
        hash *= 0x85ebca6b;
        hash ^= hash >> 13;
        hash *= 0xc2b2ae35;
        hash ^= hash >> 16;
        return hash;
    }
}
