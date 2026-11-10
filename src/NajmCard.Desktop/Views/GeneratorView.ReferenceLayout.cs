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
        Border basicsSurface,Border designSurface)
    {
        // Hadramout's original ui/network.py uses two INDEPENDENT columns
        // (42% left, 58% right). A shared 2x2 grid forced the short routing
        // panel to occupy the credentials panel's much taller first row.
        // Separate column row-heights eliminate that genuine blank panel.
        var split=new Grid{FlowDirection=FlowDirection.LeftToRight};
        split.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(42,GridUnitType.Star)});
        split.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(58,GridUnitType.Star)});
        split.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});

        var leftColumn=new Grid{FlowDirection=FlowDirection.RightToLeft};
        leftColumn.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        leftColumn.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        leftColumn.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        var rightColumn=new Grid{FlowDirection=FlowDirection.RightToLeft};
        rightColumn.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        rightColumn.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});

        routeSurface.FlowDirection=formatSurface.FlowDirection=
            basicsSurface.FlowDirection=designSurface.FlowDirection=FlowDirection.RightToLeft;
        foreach(var surface in new[]{routeSurface,formatSurface,basicsSurface,designSurface})
        {
            surface.Margin=new Thickness(1,1,1,2);
            surface.HorizontalAlignment=HorizontalAlignment.Stretch;
            surface.VerticalAlignment=VerticalAlignment.Stretch;
        }
        Grid.SetRow(routeSurface,0);
        Grid.SetRow(formatSurface,0);
        Grid.SetRow(designSurface,1);

        var secondary=new Grid{FlowDirection=FlowDirection.RightToLeft};
        secondary.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        secondary.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        _embeddedPrintHost=new Grid{Margin=new Thickness(1,1,1,0)};
        Grid.SetRow(_embeddedPrintHost,0);
        secondary.Children.Add(_embeddedPrintHost);
        _status.FontSize=12;
        _status.Margin=new Thickness(2,0,2,1);
        _status.Foreground=(Brush)new BrushConverter().ConvertFrom("#F8E6C7")!;
        Grid.SetRow(_status,1);
        secondary.Children.Add(_status);
        Grid.SetRow(basicsSurface,1);
        Grid.SetRow(secondary,2);

        leftColumn.Children.Add(formatSurface);
        leftColumn.Children.Add(basicsSurface);
        leftColumn.Children.Add(secondary);
        rightColumn.Children.Add(routeSurface);
        rightColumn.Children.Add(designSurface);
        Grid.SetColumn(leftColumn,0);
        Grid.SetColumn(rightColumn,1);
        split.Children.Add(leftColumn);
        split.Children.Add(rightColumn);
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
