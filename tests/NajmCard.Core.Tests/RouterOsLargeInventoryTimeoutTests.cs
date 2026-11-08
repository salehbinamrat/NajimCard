using NajmCard.Core.Services;
using Xunit;

namespace NajmCard.Core.Tests;

/// <summary>
/// Legacy RouterOS 6 User Manager may stream 24,000+ rows.
/// Ordinary writes must retain short bounded timeouts.
/// </summary>
public sealed class RouterOsLargeInventoryTimeoutTests
{
    [Fact]
    public void Bulk_UserManager_V6_Read_Has_Long_Bounded_Deadline_And_Idle_Timeout()
    {
        var settings=RouterOsCommandTimeoutPolicy.For("/tool/user-manager/user/print");
        Assert.True(settings.Total>=TimeSpan.FromMinutes(10));
        Assert.True(settings.Idle>=TimeSpan.FromMinutes(2));
        Assert.True(settings.MaxRows>=250000);
        Assert.InRange(settings.MaxResponseBytes,1L,128L*1024*1024);
    }

    [Fact]
    public void Writes_And_Ordinary_Reads_Retain_Short_Bounded_Timeouts()
    {
        var write=RouterOsCommandTimeoutPolicy.For("/tool/user-manager/user/add");
        var normal=RouterOsCommandTimeoutPolicy.For("/ip/hotspot/user/print");
        Assert.True(write.Total<=TimeSpan.FromSeconds(30));
        Assert.True(normal.Total<=TimeSpan.FromSeconds(30));
        Assert.True(write.Idle<=write.Total);
        Assert.True(normal.Idle<=normal.Total);
    }
}
