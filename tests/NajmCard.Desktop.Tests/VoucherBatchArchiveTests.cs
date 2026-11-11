using System.IO;
using NajmCard.Core.Models;
using NajmCard.Desktop.Services;
using Xunit;
namespace NajmCard.Desktop.Tests;
public sealed class VoucherBatchArchiveTests
{
    [Theory]
    [InlineData("hotspot","Hotspot")]
    [InlineData("userman","UserManager-v6")]
    [InlineData("pppoe","PPPoE")]
    public void Folder_Identifies_Profile_Service_And_Exact_Count_Without_Overwriting(string service,string label)
    {
        var root=Path.Combine(Path.GetTempPath(),"najm-archive-"+Guid.NewGuid());
        try
        {
            var cards=Enumerable.Range(0,3).Select(i=>new Voucher("00"+i,"","../200:RY", "batch",
                DateTimeOffset.UtcNow,Service:service)).ToArray();
            var first=VoucherBatchArchive.CreateDirectory(root,cards);
            var second=VoucherBatchArchive.CreateDirectory(root,cards);
            Assert.Equal(Path.GetFullPath(root),Path.GetDirectoryName(first));
            Assert.Contains(label+"-3-كرت",Path.GetFileName(first));
            Assert.True(Directory.Exists(first));Assert.NotEqual(first,second);
        }
        finally {if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
