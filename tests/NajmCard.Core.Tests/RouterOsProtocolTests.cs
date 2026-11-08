using System.Text;
using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

public sealed class RouterOsProtocolTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(127, 1)]
    [InlineData(128, 2)]
    [InlineData(16383, 2)]
    [InlineData(16384, 3)]
    [InlineData(2097151, 3)]
    [InlineData(2097152, 4)]
    [InlineData(268435455, 4)]
    [InlineData(268435456, 5)]
    public async Task LengthEncodingRoundtrips(uint value, int count)
    {
        byte[] encoded = RouterOsProtocol.EncodeLength(value);
        Assert.Equal(count, encoded.Length);
        using var bytes = new MemoryStream(encoded);
        Assert.Equal(value, await RouterOsProtocol.ReadLengthAsync(bytes, default));
    }

    [Fact]
    public async Task SentencesRoundtripUtf8IncludingArabic()
    {
        string[] values = ["/ip/hotspot/user/add", "=name=1234", "=comment=نجم كارد", "=password=abcDEF111"];
        using var stream = new MemoryStream();
        await RouterOsProtocol.WriteSentenceAsync(stream, values, default);
        stream.Position = 0;
        Assert.Equal(values, await RouterOsProtocol.ReadSentenceAsync(stream, default));
    }

    [Fact]
    public async Task TruncatedSentenceThrowsInsteadOfHanging()
    {
        using var stream = new MemoryStream(new byte[] { 4, (byte)'A', (byte)'B' });
        await Assert.ThrowsAsync<EndOfStreamException>(async () => await RouterOsProtocol.ReadSentenceAsync(stream, default));
    }

    [Fact]
    public async Task ExcessiveWordIsRejectedBeforeAllocating()
    {
        using var stream = new MemoryStream(RouterOsProtocol.EncodeLength(9_000_000));
        await Assert.ThrowsAsync<InvalidDataException>(async () => await RouterOsProtocol.ReadSentenceAsync(stream, default));
    }
}
