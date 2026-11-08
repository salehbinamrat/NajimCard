using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

/// <summary>One projected RouterOS6 read for currently connected PPP/PPPoE sessions.</summary>
public sealed partial class RouterGateway
{
    public async Task<IReadOnlyList<PppActiveSession>> ReadPppActiveSessionsAsync(
        CancellationToken ct = default)
    {
        var rows=await client.CommandAsync("/ppp/active/print",
            new Dictionary<string,string>
            {
                [".proplist"]=".id,name,service,address,caller-id,uptime"
            },ct);
        if(rows.Any(row => string.IsNullOrWhiteSpace(Get(row,"name"))))
            throw new InvalidDataException(
                "استجابة جلسات PPP لا تتضمن اسم المشترك؛ لا يمكن اعتماد العداد.");
        return rows.Select(row=>new PppActiveSession(
            Get(row,".id"),Get(row,"name"),Get(row,"service"),
            Get(row,"address"),Get(row,"caller-id"),Get(row,"uptime"))).ToArray();
    }
}
