using System.IO;
using NajmCard.Desktop.Services;
using Xunit;

namespace NajmCard.Desktop.Tests;

public sealed class SavedRouterConnectionTests : IDisposable
{
    private readonly string folder=Path.Combine(Path.GetTempPath(),"najm-connection-tests-"+
        Guid.NewGuid().ToString("N"));
    public SavedRouterConnectionTests()=>Directory.CreateDirectory(folder);
    public void Dispose()=>Directory.Delete(folder,true);
    private static SavedRouterConnection Entry(string host="192.168.88.1",
        string pass="myPrivateRouterPassword!") =>
        new(host,8728,"admin",false,"local",pass);

    [Fact]
    public void Saves_Multiple_Profiles_And_Reloads_Metadata_Without_Password_By_Default()
    {
        var store=new SavedRouterConnectionStore(folder);
        store.Save(Entry(),rememberPassword:false);
        store.Save(new SavedRouterConnection("vpn.example",8729,"operator",true,"remote"),
            rememberPassword:false);
        var records=new SavedRouterConnectionStore(folder).Load();
        Assert.Equal(2,records.Count);
        Assert.Contains(records,x=>x.Mode=="remote" && x.UseTls && x.Host=="vpn.example");
        Assert.Equal("",Assert.Single(records.Where(x=>x.Mode=="local")).Password);
        Assert.DoesNotContain("myPrivateRouterPassword!",File.ReadAllText(Path.Combine(folder,"profiles.json")));
    }

    [Fact]
    public void OptIn_Password_Is_Dpapi_Encrypted_And_Can_Be_Reloaded()
    {
        var store=new SavedRouterConnectionStore(folder);
        store.Save(Entry(),rememberPassword:true);
        var raw=File.ReadAllText(Path.Combine(folder,"profiles.json"));
        Assert.DoesNotContain("myPrivateRouterPassword!",raw);
        var loaded=Assert.Single(new SavedRouterConnectionStore(folder).Load());
        Assert.Equal("myPrivateRouterPassword!",loaded.Password);
        store.Save(Entry(pass:"newSecret"),rememberPassword:false);
        Assert.Equal("",Assert.Single(store.Load()).Password);
        Assert.DoesNotContain("newSecret",File.ReadAllText(Path.Combine(folder,"profiles.json")));
    }

    [Fact]
    public void Updates_Identical_Profile_Instead_Of_Duplicating_And_Deletes_Only_Selected()
    {
        var store=new SavedRouterConnectionStore(folder);
        store.Save(Entry(),false);
        store.Save(Entry(pass:"another"),false);
        store.Save(Entry(host:"192.168.88.2"),false);
        Assert.Equal(2,store.Load().Count);
        store.Delete(Entry());
        Assert.Equal("192.168.88.2",Assert.Single(store.Load()).Host);
    }

    [Theory]
    [InlineData("",8728,"local")]
    [InlineData("192.168.88.1",0,"local")]
    [InlineData("192.168.88.1",8728,"bad-mode")]
    public void Invalid_Profiles_Do_Not_Write_Anything(string host,int port,string mode)
    {
        var store=new SavedRouterConnectionStore(folder);
        Assert.ThrowsAny<ArgumentException>(()=>store.Save(
            new SavedRouterConnection(host,port,"admin",false,mode),false));
        Assert.False(File.Exists(Path.Combine(folder,"profiles.json")));
    }

    [Theory]
    [InlineData("""[{"Host":"192.168.88.1","Port":8728,"Username":"admin","UseTls":false,"Mode":"local","PasswordBlob":null}]""")]
    [InlineData("""[{"Host":null,"Port":8728,"Username":"admin","UseTls":false,"Mode":"local","PasswordBlob":""}]""")]
    public void Malformed_Null_Fields_Fail_Closed_Without_Overwriting_History(string json)
    {
        var path=Path.Combine(folder,"profiles.json");
        File.WriteAllText(path,json);
        var store=new SavedRouterConnectionStore(folder);
        Assert.ThrowsAny<InvalidDataException>(()=>store.Load());
        Assert.ThrowsAny<InvalidDataException>(()=>store.Save(Entry(),false));
        Assert.Equal(json,File.ReadAllText(path));
    }

    [Fact]
    public void Damaged_Profile_File_Must_Not_Silently_Replace_User_Data()
    {
        var path=Path.Combine(folder,"profiles.json");
        File.WriteAllText(path,"{ broken JSON");
        var store=new SavedRouterConnectionStore(folder);
        Assert.ThrowsAny<InvalidDataException>(()=>store.Load());
        Assert.ThrowsAny<InvalidDataException>(()=>store.Save(Entry(),false));
        Assert.Equal("{ broken JSON",File.ReadAllText(path));
    }
}
