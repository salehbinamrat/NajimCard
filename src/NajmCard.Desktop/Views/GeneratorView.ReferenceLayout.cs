using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace NajmCard.Desktop.Views;

public partial class GeneratorView
{
    private static Border ReferenceHeader(string title)=>new()
    {
        Background=(Brush)new BrushConverter().ConvertFrom(
            title=="بيانات ومسار الإرسال"?"#720F30":"#57213E")!,
        BorderBrush=(Brush)new BrushConverter().ConvertFrom("#D2AA61")!,
        BorderThickness=new Thickness(0,0,0,1),
        CornerRadius=new CornerRadius(5,5,0,0),
        Padding=new Thickness(8,4,8,4),
        Margin=new Thickness(0,0,0,2),
        Child=new TextBlock
        {
            Text=title,Tag="SectionTitle",FontFamily=new FontFamily("Arial"),FontSize=15,
            FontWeight=FontWeights.ExtraBold,Foreground=Brushes.White,
            TextAlignment=TextAlignment.Right
        }
    };

    private Grid CreateReferenceLayout(Border routeSurface,Border formatSurface,
        Border designSurface)
    {
        // Same 2x2 geometry as Hadramout screenshot, without fixed reference width.
        // First row: credentials on the left, package/server on the right.
        // Second row: generation/printing on the left, live designer on the right.
        var split=new Grid{FlowDirection=FlowDirection.LeftToRight};
        // Align the reference photograph's almost equal widths. The editor
        // leaves only a thin geometric separator; no outer fixed-width canvas.
        split.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(42,GridUnitType.Star)});
        split.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(58,GridUnitType.Star)});
        split.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        split.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
        routeSurface.FlowDirection=formatSurface.FlowDirection=
            designSurface.FlowDirection=FlowDirection.RightToLeft;
        foreach(var surface in new[]{routeSurface,formatSurface,designSurface})
            surface.Margin=new Thickness(1,1,1,2);
        routeSurface.VerticalAlignment=VerticalAlignment.Stretch;
        formatSurface.VerticalAlignment=VerticalAlignment.Stretch;
        designSurface.VerticalAlignment=VerticalAlignment.Stretch;
        Grid.SetRow(routeSurface,0);
        Grid.SetColumn(routeSurface,1);
        Grid.SetRow(formatSurface,0);
        Grid.SetColumn(formatSurface,0);
        Grid.SetRow(designSurface,1);
        Grid.SetColumn(designSurface,1);
        // The print area stretches down to the designer's bottom edge.
        // A StackPanel here left an unpainted strip under the printing form.
        var secondary=new Grid{FlowDirection=FlowDirection.RightToLeft};
        // The lower-left quadrant is exclusively for the printing form.
        // Generation controls are placed inside the upper-left credentials area.
        secondary.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
        secondary.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        _embeddedPrintHost=new Grid{Margin=new Thickness(1,1,1,0)};
        Grid.SetRow(_embeddedPrintHost,0);
        secondary.Children.Add(_embeddedPrintHost);
        _status.FontSize=12;
        _status.Margin=new Thickness(2,0,2,1);
        _status.Foreground=(Brush)new BrushConverter().ConvertFrom("#F8E6C7")!;
        Grid.SetRow(_status,1);
        secondary.Children.Add(_status);
        Grid.SetRow(secondary,1);
        Grid.SetColumn(secondary,0);
        split.Children.Add(formatSurface);
        split.Children.Add(routeSurface);
        split.Children.Add(designSurface);
        split.Children.Add(secondary);
        return split;
    }

    private void ChooseTemplateImage()
    {
        var picker=new OpenFileDialog
        {
            Title="صورة القالب الخاصة بالباقة",
            Filter="ملفات الصور الشائعة|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.ico|كل الملفات|*.*",
            CheckFileExists=true,Multiselect=false
        };
        if(picker.ShowDialog()!=true)return;
        try
        {
            var info=new FileInfo(picker.FileName);
            if(info.Length<=0 || info.Length>20*1024*1024)
                throw new InvalidDataException("حجم الصورة يجب أن يكون بين 1 بايت و20 MB.");
            byte[] bytes=File.ReadAllBytes(info.FullName);
            using(var stream=new MemoryStream(bytes,false))
            {
                var decoded=BitmapDecoder.Create(stream,
                    BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
                if(decoded.Frames.Count==0 ||
                   decoded.Frames[0].PixelWidth<=0 || decoded.Frames[0].PixelHeight<=0)
                    throw new InvalidDataException("ملف صورة غير صالح.");
            }
            var folder=Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),"NajmCard","CardTemplates");
            Directory.CreateDirectory(folder);
            var ext=Path.GetExtension(info.Name).ToLowerInvariant();
            if(ext.Length>8 || ext.Length<2 ||
               ext.Skip(1).Any(c=>!char.IsAsciiLetter(c)))ext=".img";
            var filename=Convert.ToHexString(SHA256.HashData(bytes))+ext;
            var destination=Path.Combine(folder,filename);
            if(!File.Exists(destination))
            {
                var temp=destination+"."+Guid.NewGuid().ToString("N")+".tmp";
                try{File.WriteAllBytes(temp,bytes);File.Move(temp,destination,true);}
                finally{if(File.Exists(temp))File.Delete(temp);}
            }
            _templateImagePath=destination;
            ApplyCardAppearance(ReadCurrentAppearance());
            _status.Text="تم استيراد صورة الكرت محليا. اضغط حفظ إعدادات هذه الباقة "+
                "لتبقى الصورة محفوظة ومختارة بعد إغلاق البرنامج.";
        }
        catch(Exception ex) when(ex is IOException or ArgumentException or
             NotSupportedException or InvalidOperationException or FormatException
             or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            _status.Text="لا يمكن تحميل هذه الصورة: "+ex.Message+
                " | بعض الصيغ تحتاج برنامج ترميز صور يدعمه Windows.";
        }
    }
}
