using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Controls;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace NajmCard.Desktop.Services;

/// <summary>Preserves WPF artwork/Arabic shaping while exporting credentials as
/// embedded Unicode text that can be selected and copied in a PDF reader.</summary>
public static class VoucherPdfExporter
{
    public const string CopyableCredentialTag="NajmCard.PdfCredential";

    private static IEnumerable<TextBlock> Credentials(DependencyObject parent)
    {
        if(parent is TextBlock text && Equals(text.Tag,CopyableCredentialTag))yield return text;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
            foreach(var child in Credentials(VisualTreeHelper.GetChild(parent,i)))yield return child;
    }

    private static void DrawCredential(XGraphics graphics,TextBlock text,FixedPage page)
    {
        var formatted=new FormattedText(text.Text,CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,new Typeface(text.FontFamily,text.FontStyle,
                text.FontWeight,text.FontStretch),text.FontSize,text.Foreground,1);
        var transform=text.TransformToAncestor(page);
        var origin=transform.Transform(new Point(text.Padding.Left,text.Padding.Top));
        var horizontal=transform.Transform(new Point(text.Padding.Left+1,text.Padding.Top));
        var vertical=transform.Transform(new Point(text.Padding.Left,text.Padding.Top+1));
        var style=text.FontWeight.ToOpenTypeWeight()>=700?XFontStyleEx.Bold:XFontStyleEx.Regular;
        if(text.FontStyle==FontStyles.Italic)style|=XFontStyleEx.Italic;
        var font=new XFont(text.FontFamily.Source,text.FontSize,style,
            new XPdfFontOptions(PdfFontEncoding.Unicode));
        if(text.Foreground is not SolidColorBrush brush)
            throw new InvalidDataException("لون رقم الكرت غير مدعوم لتصدير النص.");
        var color=brush.Color;
        var width=graphics.MeasureString(text.Text,font).Width;
        var state=graphics.Save();
        try
        {
            const double point=72d/96d;
            graphics.MultiplyTransform(new XMatrix(
                (horizontal.X-origin.X)*point,(horizontal.Y-origin.Y)*point,
                (vertical.X-origin.X)*point,(vertical.Y-origin.Y)*point,
                origin.X*point,origin.Y*point));
            // Match the real WPF glyph width, including Viewbox downscaling.
            if(width>0)graphics.ScaleTransform(formatted.WidthIncludingTrailingWhitespace/width,1);
            graphics.DrawString(text.Text,font,
                new XSolidBrush(XColor.FromArgb(color.A,color.R,color.G,color.B)),
                new XPoint(0,formatted.Baseline),XStringFormats.BaseLineLeft);
        }
        finally {graphics.Restore(state);}
    }

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
                    var credentials=Credentials(source).ToArray();
                    var originalBrushes=credentials.Select(t=>t.Foreground).ToArray();
                    try
                    {
                        // Hide glyphs only; preserve each credential's backing and geometry.
                        foreach(var text in credentials)text.Foreground=Brushes.Transparent;
                        bitmap.Render(source);
                    }
                    finally
                    {
                        for(int i=0;i<credentials.Length;i++)credentials[i].Foreground=originalBrushes[i];
                    }
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
                    foreach(var text in credentials)DrawCredential(graphics,text,source);
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
