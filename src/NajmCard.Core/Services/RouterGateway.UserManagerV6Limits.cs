using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

/// <summary>
/// Legacy User Manager v6 profile/limitation relation inventory.
/// Three small RouterOS6 tables; all links must resolve to avoid trusted but
/// misleading zero-limit results on partial or corrupted responses.
/// </summary>
public sealed partial class RouterGateway
{
    public async Task<IReadOnlyList<UserManagerV6ProfileLimitSummary>>
        ReadUserManagerV6ProfileLimitSummaryAsync(CancellationToken ct = default)
    {
        var profileRows=await client.CommandAsync(
            "/tool/user-manager/profile/print",cancellationToken:ct);
        var limitationRows=await client.CommandAsync(
            "/tool/user-manager/profile/limitation/print",cancellationToken:ct);
        var relationRows=await client.CommandAsync(
            "/tool/user-manager/profile/profile-limitation/print",cancellationToken:ct);

        var profiles=new List<(string Id,string Name)>();
        var profileAliases=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var row in profileRows)
        {
            var id=Get(row,".id");
            var name=Get(row,"name");
            var identity=name.Length>0?name:id;
            if(identity.Length==0)
                throw new InvalidDataException("تعذر تحديد هوية باقة User Manager.");
            profiles.Add((id,identity));
            foreach(var alias in new[] {id,name})
                if(alias.Length>0)
                    profileAliases[alias]=identity;
        }

        var limitations=new Dictionary<string,(string Name,long Bytes)>(
            StringComparer.OrdinalIgnoreCase);
        foreach(var row in limitationRows)
        {
            var id=Get(row,".id");
            var name=Get(row,"name");
            var label=name.Length>0?name:id;
            if(label.Length==0)
                throw new InvalidDataException("تعذر تحديد هوية محدد User Manager.");
            var bytes=UserManagerV6Usage.ParseBytes(Get(row,"transfer-limit"));
            foreach(var alias in new[] {id,name})
                if(alias.Length>0) limitations[alias]=(label,bytes);
        }

        var buckets=new Dictionary<string,List<(string Name,long Bytes)>>(
            StringComparer.OrdinalIgnoreCase);
        foreach(var (_,name) in profiles) buckets[name]=[];
        foreach(var link in relationRows)
        {
            if(!profileAliases.TryGetValue(Get(link,"profile"),out var key) ||
                !limitations.TryGetValue(Get(link,"limitation"),out var limitation))
                throw new InvalidDataException(
                    "رابط باقة User Manager يشير إلى باقة أو محدد مفقود؛ لم تُعرض حدود مضللة.");
            buckets[key].Add(limitation);
        }

        var results=new List<UserManagerV6ProfileLimitSummary>(profiles.Count);
        foreach(var (id,name) in profiles)
        {
            var linked=buckets[name];
            var positive=linked.Select(x=>x.Bytes).Where(x=>x>0).ToArray();
            results.Add(new UserManagerV6ProfileLimitSummary(
                id,name,positive.Length==0?0:positive.Min(),
                linked.Count,string.Join("، ",linked.Select(x=>x.Name))));
        }
        return results.OrderBy(x=>x.ProfileName,StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
