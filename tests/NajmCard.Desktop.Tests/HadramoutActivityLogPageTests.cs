using System.IO;
using NajmCard.Core.Models;
using NajmCard.Desktop.Services;
using Xunit;
namespace NajmCard.Desktop.Tests;
public sealed class HadramoutActivityLogPageTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"najm-history-"+Guid.NewGuid().ToString("N"));
    public HadramoutActivityLogPageTests()=>Directory.CreateDirectory(root);
    public void Dispose()=>Directory.Delete(root,true);
    [Fact]
    public void Sqlite_Audit_Is_Filterable_Paginated_And_Durable_Without_Exposing_Passwords()
    {
        using(var db=new VoucherStore(root))
        {
            for(int i=0;i<5;i++)
                db.SaveBatch([new Voucher("USER"+i,"secret-"+i,"gold","batch-"+i,DateTimeOffset.UtcNow)]);
            var pg=db.ReadActivityPage(null,"إنشاء",1,2);
            Assert.Equal(5,pg.Total);
            Assert.Equal(2,pg.Items.Count);
            Assert.All(pg.Items,x=>Assert.DoesNotContain("secret-",x.Detail));
            Assert.Equal(0,db.ReadActivityPage("PRINT",null,0,25).Total);
            Assert.Equal(5,db.ReadActivityPage("CARDS",null,0,25).Total);
        }
        using var reopened=new VoucherStore(root);
        Assert.Equal(5,reopened.ReadActivityPage(null,null,0,10).Total);
        Assert.Throws<ArgumentOutOfRangeException>(()=>reopened.ReadActivityPage(null,null,0,1001));
    }
}
