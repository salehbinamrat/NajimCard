using System.Buffers.Binary;
using System.Text;

namespace NajmCard.Core.Services;

/// <summary>MikroTik RouterOS API binary word framing; independent of transport.</summary>
public static class RouterOsProtocol
{
    private const uint MaxWord = 0xFFFFFFFF;
    public static byte[] EncodeLength(uint value)
    {
        if (value < 0x80) return [(byte)value];
        if (value < 0x4000) return [(byte)((value >> 8) | 0x80), (byte)value];
        if (value < 0x200000) return [(byte)((value >> 16) | 0xC0), (byte)(value >> 8), (byte)value];
        if (value < 0x10000000)
            return [(byte)((value >> 24) | 0xE0), (byte)(value >> 16), (byte)(value >> 8), (byte)value];
        return [0xF0, (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];
    }

    public static async Task<uint> ReadLengthAsync(Stream stream, CancellationToken token)
    {
        byte first = (await ReadExactAsync(stream, 1, token))[0];
        if ((first & 0x80) == 0) return first;
        if ((first & 0xC0) == 0x80)
        {
            byte[] b = await ReadExactAsync(stream, 1, token);
            return (uint)((first & 0x3F) << 8 | b[0]);
        }
        if ((first & 0xE0) == 0xC0)
        {
            byte[] b = await ReadExactAsync(stream, 2, token);
            return (uint)((first & 0x1F) << 16 | b[0] << 8 | b[1]);
        }
        if ((first & 0xF0) == 0xE0)
        {
            byte[] b = await ReadExactAsync(stream, 3, token);
            return (uint)((first & 0x0F) << 24 | b[0] << 16 | b[1] << 8 | b[2]);
        }
        if (first != 0xF0) throw new InvalidDataException("Unsupported RouterOS word length prefix");
        byte[] tail = await ReadExactAsync(stream, 4, token);
        return BinaryPrimitives.ReadUInt32BigEndian(tail);
    }

    public static async Task WriteSentenceAsync(Stream stream, IReadOnlyList<string> words, CancellationToken token)
    {
        foreach (var word in words)
        {
            var bytes = Encoding.UTF8.GetBytes(word);
            await stream.WriteAsync(EncodeLength((uint)bytes.Length), token);
            await stream.WriteAsync(bytes, token);
        }
        await stream.WriteAsync(new byte[] { 0 }, token);
        await stream.FlushAsync(token);
    }

    public static async Task<IReadOnlyList<string>> ReadSentenceAsync(Stream stream, CancellationToken token)
    {
        var words = new List<string>();
        while (true)
        {
            uint length = await ReadLengthAsync(stream, token);
            if (length == 0) return words;
            if (length > 8 * 1024 * 1024) throw new InvalidDataException("Oversized RouterOS API response");
            var bytes = await ReadExactAsync(stream, (int)length, token);
            words.Add(new UTF8Encoding(false, true).GetString(bytes));
            if (words.Count > 5000) throw new InvalidDataException("Excessive RouterOS API words");
        }
    }

    private static async Task<byte[]> ReadExactAsync(Stream stream, int count, CancellationToken token)
    {
        var buf = new byte[count];
        int cursor = 0;
        while (cursor < count)
        {
            int bytes = await stream.ReadAsync(buf.AsMemory(cursor), token);
            if (bytes == 0) throw new EndOfStreamException("RouterOS disconnected unexpectedly");
            cursor += bytes;
        }
        return buf;
    }
}
