using System.IO;
using System.Security.Cryptography;
using NajmCard.Core.Models;

namespace NajmCard.Desktop.Services;

/// <summary>
/// Hadramout stores the background bytes in its template snapshot. NajmCard
/// retains a compact JSON snapshot and a content-addressed local image, so
/// deleting an original image on USB or Desktop cannot break a saved batch.
/// Only the application's private artwork directory is ever written.
/// </summary>
public sealed partial class VoucherStore
{
    private VoucherCardAppearance EnsureStoredCardArtwork(VoucherCardAppearance original)
    {
        original.Validate();
        var source=original.TemplateImagePath;
        if(string.IsNullOrWhiteSpace(source))return original;
        var ext=Path.GetExtension(source).ToLowerInvariant();
        if(ext is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif"
            or ".tif" or ".tiff" or ".webp" or ".ico"))
            throw new InvalidDataException("امتداد صورة قالب الكرت غير مدعوم.");

        const long maxBytes=15L*1024*1024;
        var file=new FileInfo(source);
        if(!file.Exists)
            throw new FileNotFoundException(
                "صورة قالب الكرت غير موجودة؛ لم تُحفظ باقة بقالب مفقود.",source);
        if(file.Length<=0 || file.Length>maxBytes)
            throw new InvalidDataException("صورة قالب الكرت فارغة أو أكبر من 15 ميجابايت.");
        var bytes=File.ReadAllBytes(source);
        if(bytes.Length==0 || bytes.LongLength>maxBytes)
            throw new InvalidDataException("حجم صورة القالب تغير أثناء الحفظ.");

        var hash=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var folder=Path.Combine(DirectoryPath,"CardArtwork");
        Directory.CreateDirectory(folder);
        var destination=Path.Combine(folder,hash+ext);
        // Images already in our private cache must retain their original
        // content-addressed name. If a cached file was tampered with, do not
        // silently treat its modified contents as a new design.
        if(string.Equals(Path.GetDirectoryName(Path.GetFullPath(source)),
            Path.GetFullPath(folder),StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Path.GetFileName(source),hash+ext,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "تغير محتوى صورة القالب المحفوظة؛ استعد الصورة الأصلية قبل إعادة الحفظ.");

        static bool HasExpectedHash(string path,string expected)
        {
            if(!File.Exists(path))return false;
            using var stream=File.OpenRead(path);
            if(stream.Length==0 || stream.Length>15L*1024*1024)return false;
            return string.Equals(
                Convert.ToHexString(SHA256.HashData(stream)),expected,
                StringComparison.OrdinalIgnoreCase);
        }
        if(!HasExpectedHash(destination,hash))
        {
            // A previous copy may have been truncated or tampered with.
            // Repair only from verified source bytes and publish atomically.
            var temporary=Path.Combine(folder,hash+"-"+Guid.NewGuid().ToString("N")+".tmp");
            try
            {
                File.WriteAllBytes(temporary,bytes);
                if(File.Exists(destination))
                    File.Replace(temporary,destination,null);
                else
                {
                    try {File.Move(temporary,destination);}
                    catch(IOException) when(HasExpectedHash(destination,hash)) { }
                }
            }
            finally
            {
                if(File.Exists(temporary))File.Delete(temporary);
            }
            if(!HasExpectedHash(destination,hash))
                throw new InvalidDataException(
                    "تعذر التحقق من سلامة نسخة صورة قالب الكرت المحلية.");
        }
        return original with {TemplateImagePath=destination};
    }
}
