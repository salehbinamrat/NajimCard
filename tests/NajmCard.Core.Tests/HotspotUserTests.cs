using NajmCard.Core.Models;
using Xunit;

namespace NajmCard.Core.Tests;

public sealed class HotspotUserTests
{
    [Theory]
    [InlineData(100, 0, 0, false)]
    [InlineData(100, 1, 99, true)]
    [InlineData(100, 20, 79, false)]
    [InlineData(0, 10_000, 15_000, false)]
    public void ExpiryUsesTransferLimitOnly(long limit, long inbound, long outbound, bool expected)
    {
        var card = new HotspotUser("*1", "1020", "pass", "default", false, inbound, outbound, limit, "");
        Assert.Equal(expected, card.IsExpiredByTransferLimit);
    }

    [Fact]
    public void RouterEndpointRejectsInvalidPortAndHost()
    {
        Assert.ThrowsAny<ArgumentException>(() => new RouterEndpoint("host invalid",8728,"admin","pwd").Validate());
        Assert.ThrowsAny<ArgumentException>(() => new RouterEndpoint("10.0.0.1",0,"admin","pwd").Validate());
    }
}
