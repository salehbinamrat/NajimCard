using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

/// <summary>
/// RouterOS 6 User Manager status only. The v7 optimize/rebuild/reboot
/// maintenance commands are deliberately not exposed through this reader.
/// </summary>
public sealed partial class RouterGateway
{
    public async Task<UserManagerV6DatabaseStatus> ReadUserManagerV6DatabaseStatusAsync(
        CancellationToken ct = default)
    {
        IReadOnlyList<IReadOnlyDictionary<string,string>> rows;
        try
        {
            rows=await client.CommandAsync("/tool/user-manager/database/print",
                cancellationToken:ct);
        }
        catch(RouterOsTrapException)
        {
            // A missing User Manager package, unsupported menu and insufficient
            // read permission are all "unavailable"; no fake successful size.
            // Router messages may contain local paths or other sensitive text.
            return new UserManagerV6DatabaseStatus(false,"غير معروف",
                "غير معروف",
                "تعذرت قراءة حالة User Manager v6؛ تحقق من تثبيت الحزمة وصلاحيات القراءة.");
        }
        if(rows.Count==0)
            return new UserManagerV6DatabaseStatus(false,"غير معروف",
                "غير معروف","لم يُعد الراوتر بيانات لقاعدة User Manager v6.");
        if(rows.Count!=1)
            throw new InvalidDataException(
                "أعاد RouterOS أكثر من سجل لحالة قاعدة User Manager؛ تعذّر اعتماد البيانات.");

        var row=rows[0];
        static string Field(IReadOnlyDictionary<string,string> data,string key)
        {
            string value=Get(data,key).Trim();
            return value.Length==0?"غير معروف":
                value.Length<=120?value:value[..120];
        }
        return new UserManagerV6DatabaseStatus(true,
            Field(row,"db-size"),Field(row,"free-disk-space"),
            "تمت قراءة حالة قاعدة User Manager v6 فقط؛ لم تُنفذ أي صيانة أو تعديل.");
    }
}
