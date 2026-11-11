using System.IO;
using NajmCard.Core.Models;

namespace NajmCard.Desktop.Services;

public static class VoucherBatchArchive
{
    public static string CreateDirectory(string parent,IReadOnlyList<Voucher> cards)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parent);
        if(cards.Count==0)throw new ArgumentException("الدفعة فارغة.",nameof(cards));
        var first=cards[0];
        if(cards.Any(c=>c.Profile!=first.Profile||c.Service!=first.Service||c.BatchId!=first.BatchId))
            throw new ArgumentException("اختر دفعة واحدة للحفظ.",nameof(cards));
        var type=first.Service switch {"hotspot"=>"Hotspot","userman"=>"UserManager-v6",
            "pppoe"=>"PPPoE",_=>throw new ArgumentException("نوع الكرت غير صالح.")};
        var name=new string(first.Profile.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c)
            .Take(50).ToArray()).Trim().TrimEnd('.');
        if(string.IsNullOrWhiteSpace(name))name="كروت";
        var path=Path.Combine(Path.GetFullPath(parent),name+"-"+type+"-"+cards.Count+"-كرت-"+
            DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(path);
        return path;
    }
}
