using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NajmCard.Core.Models;
using NajmCard.Desktop.Services;
using Xunit;

namespace NajmCard.Desktop.Tests;

public sealed class PortableCardTemplateTests
{
    [Fact]
    public void Portable_Design_And_Image_Reload_Without_The_Source_Image()
    {
        ExceptionDispatchInfo? failure=null;
        var thread=new Thread(()=>
        {
            try{VerifyRoundTrip();}
            catch(Exception ex){failure=ExceptionDispatchInfo.Capture(ex);}
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }

    private static void VerifyRoundTrip()
    {
        var directory=Directory.CreateTempSubdirectory("najm-template-").FullName;
        try
        {
            var original=Path.Combine(directory,"original.png");
            var encoder=new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(
                BitmapSource.Create(1,1,96,96,PixelFormats.Bgra32,null,new byte[]{40,50,60,255},4)));
            using(var stream=File.Create(original))encoder.Save(stream);
            var bytes=File.ReadAllBytes(original);
            var design=new VoucherCardAppearance(NetworkTitle:"شبكة اختبار",TemplateImagePath:original,
                PasswordPosition:new CardElementPosition(0.6,0.7),TitleScale:1.2);
            var template=Path.Combine(directory,"test.najmtemplate");

            PortableCardTemplate.Export(template,design);
            Assert.DoesNotContain("original.png",File.ReadAllText(template));
            File.Delete(original);

            var imported=PortableCardTemplate.Import(template,Path.Combine(directory,"receiver"));
            Assert.Equal(design with {TemplateImagePath=imported.TemplateImagePath},imported);
            Assert.Equal(bytes,File.ReadAllBytes(imported.TemplateImagePath));
            // A second import must repair an existing corrupted hash-named
            // .img rather than returning it as though it were valid.
            File.WriteAllBytes(imported.TemplateImagePath,[1,2,3,4]);
            var repaired=PortableCardTemplate.Import(
                template,Path.Combine(directory,"receiver"));
            Assert.Equal(imported.TemplateImagePath,repaired.TemplateImagePath);
            Assert.Equal(bytes,File.ReadAllBytes(repaired.TemplateImagePath));

            // The image output by .najmtemplate import must be accepted by
            // global design persistence and survive the imported file's loss.
            using(var store=new VoucherStore(Path.Combine(directory,"data")))
                store.SaveCardAppearance(repaired);
            var global=new VoucherStore(Path.Combine(directory,"data")).LoadCardAppearance();
            Assert.NotEqual(repaired.TemplateImagePath,global.TemplateImagePath);
            Assert.Equal(bytes,File.ReadAllBytes(global.TemplateImagePath));
            File.Delete(repaired.TemplateImagePath);
            Assert.Equal(bytes,File.ReadAllBytes(global.TemplateImagePath));

            var bitmap=new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption=BitmapCacheOption.OnLoad;
            bitmap.UriSource=new Uri(global.TemplateImagePath);
            bitmap.EndInit();
            Assert.Equal(1,bitmap.PixelWidth);
        }
        finally{Directory.Delete(directory,true);}
    }

    [Fact]
    public void Rejects_External_Image_Paths_And_Unknown_Versions()
    {
        var directory=Directory.CreateTempSubdirectory("najm-template-").FullName;
        try
        {
            var template=Path.Combine(directory,"bad.najmtemplate");
            var imageFolder=Path.Combine(directory,"images");
            File.WriteAllText(template,"""
                {"Format":"NajmCard.Template","Version":1,"Appearance":{"TemplateImagePath":"outside.png"}}
                """);
            Assert.Throws<InvalidDataException>(()=>PortableCardTemplate.Import(template,imageFolder));
            File.WriteAllText(template,"""
                {"Format":"NajmCard.Template","Version":2,"Appearance":{}}
                """);
            Assert.Throws<InvalidDataException>(()=>PortableCardTemplate.Import(template,imageFolder));
            Assert.False(Directory.Exists(imageFolder));
        }
        finally{Directory.Delete(directory,true);}
    }
}
