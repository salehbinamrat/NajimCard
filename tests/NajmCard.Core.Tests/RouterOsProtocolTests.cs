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

    private sealed class CountingWriteStream : MemoryStream
    {
        public int Writes { get; private set; }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            Writes++;
            await base.WriteAsync(buffer, cancellationToken);
        }
    }

    [Fact]
    public async Task Voucher_Command_Uses_Exactly_One_Tcp_Write_Regardless_Of_Word_Count()
    {
        string[] words =
        [
            "/ip/hotspot/user/add",
            "=name=12345678",
            "=password=12345678",
            "=profile=gold",
            "=comment=NajmCard batch 20261111",
            "=.tag=job-7",
            "=unusual=Unicode نجم كارد"
        ];
        using var stream=new CountingWriteStream();
        await RouterOsProtocol.WriteSentenceAsync(stream,words,default);
        Assert.Equal(1,stream.Writes);
        stream.Position=0;
        Assert.Equal(words,await RouterOsProtocol.ReadSentenceAsync(stream,default));
        Assert.Equal(stream.Length,stream.Position);
    }

    [Fact]
    public async Task Two_Thousand_Vouchers_Do_Not_Trigger_Tiny_Writes_Per_Field()
    {
        using var stream=new CountingWriteStream();
        for (int i=0;i<2000;i++)
            await RouterOsProtocol.WriteSentenceAsync(stream,
                ["/ip/hotspot/user/add","=name="+i.ToString("D8"),"=profile=gold",
                 "=password=","=comment=batch-2000"],default);
        Assert.Equal(2000,stream.Writes);
        stream.Position=0;
        for(int i=0;i<2000;i++)
        {
            var words=await RouterOsProtocol.ReadSentenceAsync(stream,default);
            Assert.Equal("/ip/hotspot/user/add",words[0]);
            Assert.Equal("=name="+i.ToString("D8"),words[1]);
        }
        Assert.Equal(stream.Length,stream.Position);
    }
}
