using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media.Imaging;
using NajmCard.Core.Models;

namespace NajmCard.Desktop.Services;

public static class PortableCardTemplate
{
    private const int MaxImageBytes=20*1024*1024;
    private const int MaxTemplateBytes=30*1024*1024;
    private const string Format="NajmCard.Template";

    private sealed record Package(string Format,int Version,VoucherCardAppearance Appearance,byte[]? Image);

    public static void Export(string path,VoucherCardAppearance appearance)
    {
        appearance.Validate();
        byte[]? image=null;
        if(!string.IsNullOrEmpty(appearance.TemplateImagePath))
        {
            image=ReadBounded(appearance.TemplateImagePath,MaxImageBytes);
            ValidateImage(image);
        }

        var package=new Package(Format,1,appearance with {TemplateImagePath=""},image);
        WriteAtomically(Path.GetFullPath(path),stream=>JsonSerializer.Serialize(stream,package));
    }

    public static VoucherCardAppearance Import(string path,string imageFolder)
    {
        Package? package;
        try
        {
            package=JsonSerializer.Deserialize<Package>(ReadBounded(path,MaxTemplateBytes));
        }
        catch(JsonException ex)
        {
            throw new InvalidDataException("تعذر قراءة ملف قالب نجم كارد.",ex);
        }
        if(package is null || package.Format!=Format || package.Version!=1 || package.Appearance is null)
            throw new InvalidDataException("صيغة قالب نجم كارد أو إصداره غير مدعوم.");
        if(!string.IsNullOrEmpty(package.Appearance.TemplateImagePath))
            throw new InvalidDataException("يجب تضمين صورة القالب داخل الملف دون مسار خارجي.");
        var appearance=package.Appearance.Validate();
        return package.Image is null?appearance:
            appearance with {TemplateImagePath=StoreImage(package.Image,imageFolder)};
    }

    internal static string ImportImage(string path,string imageFolder) =>
        StoreImage(ReadBounded(path,MaxImageBytes),imageFolder);

    private static string StoreImage(byte[] image,string imageFolder)
    {
        ValidateImage(image);
        Directory.CreateDirectory(imageFolder);
        var path=Path.GetFullPath(Path.Combine(imageFolder,Convert.ToHexString(SHA256.HashData(image))+".img"));
        if(!File.Exists(path))WriteAtomically(path,stream=>stream.Write(image));
        return path;
    }

    private static void ValidateImage(byte[] image)
    {
        if(image.Length is 0 or > MaxImageBytes)
            throw new InvalidDataException("حجم صورة القالب يجب أن يكون بين 1 بايت و20 MB.");
        using var stream=new MemoryStream(image,false);
        var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnDemand);
        if(decoder.Frames.Count==0)
            throw new InvalidDataException("ملف صورة غير صالح.");
        var frame=decoder.Frames[0];
        if(frame.PixelWidth<=0 || frame.PixelHeight<=0 ||
           (long)frame.PixelWidth*frame.PixelHeight>40_000_000)
            throw new InvalidDataException("أبعاد صورة القالب غير صالحة أو كبيرة جدا.");
        stream.Position=0;
        var bitmap=new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption=BitmapCacheOption.OnLoad;
        bitmap.StreamSource=stream;
        bitmap.EndInit();
    }

    private static byte[] ReadBounded(string path,int maximum)
    {
        using var stream=File.OpenRead(path);
        if(stream.Length<=0 || stream.Length>maximum)
            throw new InvalidDataException("الملف فارغ أو يتجاوز الحجم المسموح.");
        var bytes=new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        if(stream.ReadByte()!=-1)
            throw new InvalidDataException("تغير حجم الملف أثناء القراءة.");
        return bytes;
    }

    private static void WriteAtomically(string path,Action<Stream> write)
    {
        var staging=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using(var stream=new FileStream(staging,FileMode.CreateNew,FileAccess.Write))
                write(stream);
            File.Move(staging,path,true);
        }
        finally
        {
            if(File.Exists(staging))File.Delete(staging);
        }
    }
}
