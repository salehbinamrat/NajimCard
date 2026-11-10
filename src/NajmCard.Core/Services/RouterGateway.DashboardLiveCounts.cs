using System.Globalization;
using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

/// <summary>
/// Hadramout-style cheap live dashboard: a server-side count-only read for
/// RouterOS6 UM users and connected Hotspot/PPP clients. Never download a
/// full user table or passwords on normal page navigation.
/// </summary>
public sealed partial class RouterGateway
{
    public Task<DashboardLiveCounts> ReadDashboardLiveCountsAsync(CancellationToken ct=default)
        => ReadDashboardCountsAsync(includeUserManagerTotal:true,ct);

    // Second-by-second connectivity: do not re-read the costly total users counter.
    public Task<DashboardLiveCounts> ReadDashboardOnlineOnlyAsync(CancellationToken ct=default)
        => ReadDashboardCountsAsync(includeUserManagerTotal:false,ct);

    private async Task<DashboardLiveCounts> ReadDashboardCountsAsync(
        bool includeUserManagerTotal,CancellationToken ct)
    {
        if(client is not IRouterReceiptClient receipt)
            throw new InvalidOperationException(
                "قراءة العدادات المباشرة تتطلب بيانات !done المؤكدة من RouterOS API.");
        async Task<int?> Count(string path)
        {
            try
            {
                var result=await receipt.CommandWithReceiptAsync(path,
                    new Dictionary<string,string> {["count-only"]=""},ct);
                var value=result.Done.TryGetValue("ret",out var ret) ? ret :
                    result.Done.TryGetValue("count",out var c) ? c : null;
                return int.TryParse(value,NumberStyles.None,
                    CultureInfo.InvariantCulture,out var n) && n>=0 ? n : null;
            }
            catch(RouterOsTrapException)
            {
                // Fail closed. Full RouterOS6 inventories are slow and can include
                // credentials; a missing counter means unknown, not zero.
                return null;
            }
        }
        int? total=includeUserManagerTotal
            ?await Count("/tool/user-manager/user/print"):null;
        int? hotspot=await Count("/ip/hotspot/active/print");
        int? ppp=await Count("/ppp/active/print");
        return new DashboardLiveCounts(total,hotspot,ppp);
    }

    private readonly SemaphoreSlim _lifeCacheGate=new(1,1);
    private UserManagerV6Inventory? _lifeCache;
    private DateTimeOffset _lifeCacheAt;
    public async Task<UserManagerV6Inventory> ReadCachedUserManagerV6InventoryAsync(
        bool force=false,CancellationToken ct=default)
    {
        if(!force&&_lifeCache is {IsComplete:true} existing &&
            DateTimeOffset.UtcNow-_lifeCacheAt<TimeSpan.FromMinutes(5))
            return existing;
        await _lifeCacheGate.WaitAsync(ct);
        try
        {
            if(!force&&_lifeCache is {IsComplete:true} saved &&
                DateTimeOffset.UtcNow-_lifeCacheAt<TimeSpan.FromMinutes(5))
                return saved;
            var current=await ReadUserManagerV6InventoryAsync(ct);
            if(current.IsComplete)
            {
                _lifeCache=current;
                _lifeCacheAt=DateTimeOffset.UtcNow;
            }
            return current;
        }
        finally {_lifeCacheGate.Release();}
    }

    public void InvalidateUserManagerV6LifecycleCache()
    {
        _lifeCache=null;
        _lifeCacheAt=DateTimeOffset.MinValue;
    }
}
