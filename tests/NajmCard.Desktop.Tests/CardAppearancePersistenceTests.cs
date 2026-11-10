using System.IO;
using NajmCard.Core.Models;
using NajmCard.Desktop.Services;
using Xunit;

namespace NajmCard.Desktop.Tests;
public sealed class CardAppearancePersistenceTests : IDisposable
{
    private readonly string folder=Path.Combine(Path.GetTempPath(),"najm-style-"+Guid.NewGuid().ToString("N"));
    public CardAppearancePersistenceTests()=>Directory.CreateDirectory(folder);
    public void Dispose()=>Directory.Delete(folder,true);

    [Fact]
    public void Saves_And_Reloads_Custom_Style_From_Independent_Sqlite()
    {
        using(var store=new VoucherStore(folder))
        {
            Assert.Equal("نجم نت",store.LoadCardAppearance().NetworkTitle);
            store.SaveCardAppearance(new VoucherCardAppearance(
                NetworkTitle:"نجمة الشبكة",FontFamily:"Tahoma",UsernameFontSize:21,
                BackgroundColor:"#2E0F23",UsernameColor:"#FAD88C",
                BorderColor:"#E5B86F",BorderWidth:2,
                ShowPassword:false,ShowProfile:true));
        }
        using var reopened=new VoucherStore(folder);
        var design=reopened.LoadCardAppearance();
        Assert.Equal("نجمة الشبكة",design.NetworkTitle);
        Assert.False(design.ShowPassword);
        Assert.Equal("#FAD88C",design.UsernameColor);
        Assert.Equal(2,design.BorderWidth);
    }

    [Fact]
    public void Failed_Validation_Does_Not_Destroy_Last_Saved_Style()
    {
        using var store=new VoucherStore(folder);
        store.SaveCardAppearance(new VoucherCardAppearance(NetworkTitle:"My Valid Title"));
        Assert.ThrowsAny<ArgumentException>(()=>
            store.SaveCardAppearance(new VoucherCardAppearance(BackgroundColor:"invalid")));
        Assert.Equal("My Valid Title",store.LoadCardAppearance().NetworkTitle);
    }
}
