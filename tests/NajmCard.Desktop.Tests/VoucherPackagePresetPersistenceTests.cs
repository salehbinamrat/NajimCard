using System.IO;
using NajmCard.Core.Models;
using NajmCard.Core.Services;
using NajmCard.Desktop.Services;
using Xunit;

namespace NajmCard.Desktop.Tests;

/// <summary>Actual SQLite persistence and return-after-relaunch, not static file checks.</summary>
public sealed class VoucherPackagePresetPersistenceTests : IDisposable
{
    private readonly string _root=Path.Combine(Path.GetTempPath(),"najm-package-"+Guid.NewGuid().ToString("N"));
    public VoucherPackagePresetPersistenceTests()=>Directory.CreateDirectory(_root);
    public void Dispose()=>Directory.Delete(_root,true);

    private static VoucherPackagePreset Preset(string router="192.168.1.1:8728",
        string service="hotspot",string profile="30 Day",string customer="",
        string title="نجوم الانترنت",int count=60) =>
        new(router,service,profile,customer,count,"NC",10,"0123456789",
            false,6,"abcdef",true,
            new CardPrintOptions(Rows:15,Columns:4,Paper:"A4",Order:"right_to_left"),
            new VoucherCardAppearance(NetworkTitle:title,
                FontFamily:"Tahoma",UsernameFontSize:20,
                BackgroundColor:"#42152F",UsernameColor:"#FFE2A3",BorderColor:"#D8B576",
                BorderWidth:2));

    [Fact]
    public void Saves_Independent_Package_Per_Router_Service_Profile_And_Owner_Across_Restart()
    {
        using(var store=new VoucherStore(_root))
        {
            store.SavePackagePreset(Preset());
            store.SavePackagePreset(Preset(profile:"7 Day",title:"اشتراك اسبوعي",count:15));
            store.SavePackagePreset(Preset(router:"192.168.1.2:8728",title:"شبكة اخرى"));
            store.SavePackagePreset(Preset(service:"userman",customer:"admin",title:"مدير المستخدمين"));
            store.SavePackagePreset(Preset(service:"userman",customer:"customer2",title:"موزع اخر"));
            store.SavePackagePreset(Preset(service:"pppoe",title:"برودباند"));
        }
        using var reopened=new VoucherStore(_root);
        Assert.Equal(60,reopened.LoadPackagePreset("192.168.1.1:8728","hotspot","30 Day")!.Quantity);
        Assert.Equal("اشتراك اسبوعي",reopened.LoadPackagePreset("192.168.1.1:8728","hotspot","7 Day")!.Appearance.NetworkTitle);
        Assert.Equal("شبكة اخرى",reopened.LoadPackagePreset("192.168.1.2:8728","hotspot","30 Day")!.Appearance.NetworkTitle);
        Assert.Equal("مدير المستخدمين",reopened.LoadPackagePreset("192.168.1.1:8728","userman","30 Day","admin")!.Appearance.NetworkTitle);
        Assert.Equal("موزع اخر",reopened.LoadPackagePreset("192.168.1.1:8728","userman","30 Day","customer2")!.Appearance.NetworkTitle);
        Assert.Equal("برودباند",reopened.LoadPackagePreset("192.168.1.1:8728","pppoe","30 Day")!.Appearance.NetworkTitle);
        Assert.Null(reopened.LoadPackagePreset("192.168.1.1:8728","userman","30 Day","other"));
    }

    [Fact]
    public void ReSaving_One_Package_Never_Mutates_Another_Or_Global_Style()
    {
        using var store=new VoucherStore(_root);
        store.SavePackagePreset(Preset(title:"الأولى"));
        store.SavePackagePreset(Preset(profile:"premium",title:"الثانية"));
        store.SavePackagePreset(Preset(title:"الأولى بعد التعديل",count:100));
        Assert.Equal(100,store.LoadPackagePreset("192.168.1.1:8728","hotspot","30 Day")!.Quantity);
        Assert.Equal("الثانية",store.LoadPackagePreset("192.168.1.1:8728","hotspot","premium")!.Appearance.NetworkTitle);
        Assert.Equal("نجم نت",store.LoadCardAppearance().NetworkTitle);
    }

    [Fact]
    public void Invalid_Preset_Does_Not_Corrupt_Previously_Saved_Snapshot()
    {
        using var store=new VoucherStore(_root);
        store.SavePackagePreset(Preset());
        Assert.ThrowsAny<ArgumentException>(()=>store.SavePackagePreset(Preset() with
        {
            Appearance=Preset().Appearance with {UsernameColor="#33142X"}
        }));
        Assert.Equal("#FFE2A3",store.LoadPackagePreset("192.168.1.1:8728","hotspot","30 Day")!.Appearance.UsernameColor);
    }

    [Fact]
    public void Card_Positions_And_Scales_Reload_Only_For_The_Saved_Package()
    {
        var preset=Preset();
        var moved=preset.Appearance with
        {
            TitlePosition=new CardElementPosition(0.2,0.3),
            CredentialsPosition=new CardElementPosition(0.8,0.5),
            ProfilePosition=new CardElementPosition(1,0),
            PasswordPosition=new CardElementPosition(0.4,0.75),
            TitleScale=1.2,PasswordScale=0.8,ProfileScale=1.3
        };
        using(var store=new VoucherStore(_root))
        {
            store.SavePackagePreset(preset with {Appearance=moved});
            store.SavePackagePreset(Preset(profile:"7 Day"));
            Assert.Throws<ArgumentException>(()=>store.SavePackagePreset(preset with
            {
                Appearance=moved with {ProfilePosition=new CardElementPosition(-0.1,0)}
            }));
            Assert.Throws<ArgumentException>(()=>store.SavePackagePreset(preset with
            {
                Appearance=moved with {PasswordScale=double.NaN}
            }));
        }

        using var reopened=new VoucherStore(_root);
        Assert.Equal(moved,reopened.LoadPackagePreset("192.168.1.1:8728","hotspot","30 Day")!.Appearance);
        var other=reopened.LoadPackagePreset("192.168.1.1:8728","hotspot","7 Day")!.Appearance;
        Assert.Null(other.PasswordPosition);
        Assert.Equal(1,other.PasswordScale);
    }

    [Fact]
    public void Printer_And_Additional_Exports_Save_Per_Profile_And_Stay_After_Restart()
    {
        using(var store=new VoucherStore(_root))
        {
            store.SavePackagePreset(Preset(profile:"100RY") with
            {
                Output=new VoucherOutputPreferences("HP LaserJet",true,true,true)
            });
            store.SavePackagePreset(Preset(profile:"200RY") with
            {
                Output=new VoucherOutputPreferences("الافتراضية",false,false,false)
            });
        }
        using var opened=new VoucherStore(_root);
        var a=opened.LoadPackagePreset("192.168.1.1:8728","hotspot","100RY")!;
        var b=opened.LoadPackagePreset("192.168.1.1:8728","hotspot","200RY")!;
        Assert.Equal("HP LaserJet",a.Output!.PrinterName);
        Assert.True(a.Output.AdditionalExport);
        Assert.True(a.Output.ExportCsv);
        Assert.True(a.Output.ExportExcel);
        Assert.Equal("الافتراضية",b.Output!.PrinterName);
        Assert.False(b.Output.AdditionalExport);
        Assert.ThrowsAny<ArgumentException>(()=>opened.SavePackagePreset(
            a with {Output=new VoucherOutputPreferences("Bad" + (char)10)}));
        Assert.Equal("HP LaserJet",opened.LoadPackagePreset(
            "192.168.1.1:8728","hotspot","100RY")!.Output!.PrinterName);
    }

    [Theory]
    [InlineData("unknown","30 Day")]
    [InlineData("hotspot","")]
    public void Invalid_Keys_Are_Rejected_Before_Database_Changes(string service,string profile)
    {
        using var store=new VoucherStore(_root);
        Assert.ThrowsAny<ArgumentException>(()=>
            store.SavePackagePreset(Preset(service:service,profile:profile)));
    }

    [Fact]
    public void Package_Saves_A_Portable_Template_Image_Not_An_External_Path()
    {
        var outside=Path.Combine(Path.GetTempPath(),"najm-package-art-"+Guid.NewGuid().ToString("N")+".png");
        File.WriteAllBytes(outside,Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/X/0AAAAASUVORK5CYII="));
        try
        {
            var preset=Preset() with
            {Appearance=Preset().Appearance with {TemplateImagePath=outside}};
            using(var store=new VoucherStore(_root))
                store.SavePackagePreset(preset);
            File.Delete(outside);
            using var reopened=new VoucherStore(_root);
            var loaded=reopened.LoadPackagePreset(
                "192.168.1.1:8728","hotspot","30 Day");
            Assert.NotNull(loaded);
            Assert.NotEqual(outside,loaded.Appearance.TemplateImagePath);
            Assert.True(File.Exists(loaded.Appearance.TemplateImagePath));
        }
        finally{if(File.Exists(outside))File.Delete(outside);}
    }


    [Fact]
    public void Cached_Card_Artwork_Is_Repaired_After_Truncation_From_Valid_Source()
    {
        var outside=Path.Combine(_root,"source-image.png");
        var pixels=Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/X/0AAAAASUVORK5CYII=");
        File.WriteAllBytes(outside,pixels);
        using var store=new VoucherStore(_root);
        var preset=Preset() with
        {Appearance=Preset().Appearance with {TemplateImagePath=outside}};
        store.SavePackagePreset(preset);
        var cached=store.LoadPackagePreset("192.168.1.1:8728","hotspot","30 Day")!
            .Appearance.TemplateImagePath;
        Assert.Equal(pixels,File.ReadAllBytes(cached));

        // A truncated cache file must not be silently trusted by the next save.
        File.WriteAllBytes(cached,[1,2,3]);
        store.SavePackagePreset(preset);
        Assert.Equal(pixels,File.ReadAllBytes(cached));
        Assert.Equal(cached,store.LoadPackagePreset(
            "192.168.1.1:8728","hotspot","30 Day")!.Appearance.TemplateImagePath);
    }

    [Fact]
    public void Modified_Cache_Cannot_Be_Saved_As_A_Different_Artwork_By_Accident()
    {
        var outside=Path.Combine(_root,"owner-artwork.png");
        File.WriteAllBytes(outside,Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/X/0AAAAASUVORK5CYII="));
        using var store=new VoucherStore(_root);
        var preset=Preset() with
        {Appearance=Preset().Appearance with {TemplateImagePath=outside}};
        store.SavePackagePreset(preset);
        var cached=store.LoadPackagePreset("192.168.1.1:8728","hotspot","30 Day")!
            .Appearance.TemplateImagePath;
        File.WriteAllBytes(cached,[9,8,7,6]);

        Assert.Throws<InvalidDataException>(()=>
            store.SavePackagePreset(preset with
            {Appearance=preset.Appearance with {TemplateImagePath=cached}}));
        // The previous package record remains intact, even though its image
        // now needs to be repaired from the original file.
        Assert.Equal(cached,store.LoadPackagePreset(
            "192.168.1.1:8728","hotspot","30 Day")!.Appearance.TemplateImagePath);
    }

    [Fact]
    public void Imported_Hash_Named_Image_Can_Be_Saved_With_Package_And_Reloaded()
    {
        var bytes=Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/X/0AAAAASUVORK5CYII=");
        var templateFolder=Path.Combine(_root,"CardTemplates");
        Directory.CreateDirectory(templateFolder);
        var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        var imported=Path.Combine(templateFolder,hash+".img");
        File.WriteAllBytes(imported,bytes);

        using(var store=new VoucherStore(_root))
        {
            var preset=Preset() with
            {Appearance=Preset().Appearance with {TemplateImagePath=imported}};
            store.SavePackagePreset(preset);
            var saved=store.LoadPackagePreset("192.168.1.1:8728","hotspot","30 Day")!;
            Assert.NotEqual(imported,saved.Appearance.TemplateImagePath);
            Assert.Equal(bytes,File.ReadAllBytes(saved.Appearance.TemplateImagePath));
            File.Delete(imported);
            Assert.Equal(bytes,File.ReadAllBytes(saved.Appearance.TemplateImagePath));

            // The same portable extension is not a free pass for arbitrary
            // replacement bytes. A mismatched SHA filename fails closed.
            File.WriteAllBytes(imported,[5,4,3,2,1]);
            Assert.Throws<InvalidDataException>(()=>store.SavePackagePreset(
                preset with {Profile="modified"}));
            Assert.Null(store.LoadPackagePreset(
                "192.168.1.1:8728","hotspot","modified"));
        }
    }



    [Fact]
    public void Username_Only_And_Same_Password_Are_Stored_Separately_Per_Package()
    {
        using(var store=new VoucherStore(_root))
        {
            store.SavePackagePreset(Preset(profile:"username-only") with
            {
                UsernameOnly=true,SamePassword=false,PasswordCharacters="INVALID!",
                PasswordLength=8
            });
            store.SavePackagePreset(Preset(profile:"matching") with
            {
                UsernameOnly=false,SamePassword=true
            });
            store.SavePackagePreset(Preset(profile:"separate") with
            {
                UsernameOnly=false,SamePassword=false,PasswordCharacters="abcdef",
                PasswordLength=6
            });
        }
        using var reopened=new VoucherStore(_root);
        var a=reopened.LoadPackagePreset("192.168.1.1:8728","hotspot","username-only")!;
        var b=reopened.LoadPackagePreset("192.168.1.1:8728","hotspot","matching")!;
        var c=reopened.LoadPackagePreset("192.168.1.1:8728","hotspot","separate")!;
        Assert.True(a.UsernameOnly);
        Assert.False(a.SamePassword);
        Assert.False(b.UsernameOnly);
        Assert.True(b.SamePassword);
        Assert.False(c.UsernameOnly);
        Assert.False(c.SamePassword);
    }


}
