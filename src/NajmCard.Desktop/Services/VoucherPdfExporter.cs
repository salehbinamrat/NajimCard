using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace NajmCard.Desktop.Services;

/// <summary>
/// PDF of the exact FixedDocument already used by card preview/printer.
/// WPF rasterizes the text before PDF embedding, preserving Arabic shaping,
/// font weight and custom design colors even on Windows without PDF fonts.
/// </summary>
public static class VoucherPdfExporter
{
    public static void Export(FixedDocument document,string path)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if(!string.Equals(Path.GetExtension(path),".pdf",StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("اختر مسارا لملف PDF فقط.",nameof(path));
        if(document.Pages.Count is <1 or >1000)
            throw new ArgumentException("يجب وجود صفحة إلى 1000 صفحة PDF.");
        var target=Path.GetFullPath(path);
        var dir=Path.GetDirectoryName(target) ??
            throw new ArgumentException("مجلد الحفظ غير صالح.",nameof(path));
        Directory.CreateDirectory(dir);
        var staged=Path.Combine(dir,".najm-pdf-"+Guid.NewGuid().ToString("N")+".tmp");
        try
        {
            using(var pdf=new PdfDocument())
            {
                pdf.Info.Title="NajmCard Voucher Printing";
                foreach(PageContent content in document.Pages)
                {
                    var source=content.GetPageRoot(false);
                    if(!double.IsFinite(source.Width)||!double.IsFinite(source.Height)||
                        source.Width<1||source.Height<1)
                        throw new InvalidDataException("مقاس ورقة الكروت غير صالح.");
                    var dipWidth=source.Width;var dipHeight=source.Height;
                    source.Measure(new Size(dipWidth,dipHeight));
                    source.Arrange(new Rect(0,0,dipWidth,dipHeight));
                    source.UpdateLayout();
                    const int dpi=144;
                    var bitmap=new RenderTargetBitmap(
                        (int)Math.Ceiling(dipWidth*dpi/96d),
                        (int)Math.Ceiling(dipHeight*dpi/96d),
                        dpi,dpi,PixelFormats.Pbgra32);
                    bitmap.Render(source);
                    var encoder=new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var memory=new MemoryStream();
                    encoder.Save(memory);
                    memory.Position=0;
                    using var image=XImage.FromStream(memory);
                    var page=pdf.AddPage();
                    page.Width=PdfSharp.Drawing.XUnit.FromPoint(dipWidth*72/96d);
                    page.Height=PdfSharp.Drawing.XUnit.FromPoint(dipHeight*72/96d);
                    using var graphics=XGraphics.FromPdfPage(page);
                    graphics.DrawImage(image,0,0,page.Width.Point,page.Height.Point);
                }
                // Stage the complete PDF before touching any existing report.
                pdf.Save(staged);
            }
            if(File.Exists(target))File.Replace(staged,target,null);
            else File.Move(staged,target);
        }
        finally {if(File.Exists(staged))File.Delete(staged);}
    }
}
