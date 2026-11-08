namespace NajmCard.Core.Models;

/// <summary>Count-only RouterOS dashboard reading; null means unknown, never zero.</summary>
public sealed record DashboardLiveCounts(int? UserManagerTotal,int? HotspotOnline,
    int? PppOnline)
{
    public int? OnlineTotal => HotspotOnline is null && PppOnline is null
        ? null : (HotspotOnline??0)+(PppOnline??0);
    public bool IsPartial => HotspotOnline is null || PppOnline is null;
}
