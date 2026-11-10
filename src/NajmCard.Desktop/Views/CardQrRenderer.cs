using System.IO;
using System.Windows.Media.Imaging;
using QRCoder;

namespace NajmCard.Desktop.Views;

/// <summary>
/// Encodes only the public voucher username. Never place a password in QR data.
/// The same renderer is used for the draggable sample and printed card.
/// </summary>
internal static class CardQrRenderer
{
    internal static BitmapSource Render(string username)
    {
        if(string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("رقم الكرت مطلوب لإنشاء QR.",nameof(username));
        using var generator=new QRCodeGenerator();
        using var data=generator.CreateQrCode(username,QRCodeGenerator.ECCLevel.Q);
        var bytes=new PngByteQRCode(data).GetGraphic(4);
        using var buffer=new MemoryStream(bytes,false);
        var image=new BitmapImage();
        image.BeginInit();
        image.CacheOption=BitmapCacheOption.OnLoad;
        image.StreamSource=buffer;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
