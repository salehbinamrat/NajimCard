using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.Win32;
using NajmCard.Desktop.Services;

namespace NajmCard.Desktop.Views;

public partial class GeneratorView
{
    private static Border ReferenceHeader(string title)
    {
        var icon=title switch
        {
            "باقة وسيرفر الإرسال"=>"IconServer",
            "اسم المستخدم وكلمة المرور"=>"IconPeople",
            "معاينة وتصميم الكرت"=>"IconCard",
            "بيانات التوليد الأساسية"=>"IconPrinter",
            _=>"IconPackages"
        };
        var cream=Brushes.FloralWhite;
        var heading=new DockPanel { FlowDirection=FlowDirection.LeftToRight };
        var glyph=new System.Windows.Shapes.Path
        {
            Data=(Geometry)Application.Current.FindResource(icon),Stroke=cream,
            Stretch=Stretch.Uniform,StrokeThickness=1.6,
            Width=15,Height=15,Margin=new Thickness(6,0,0,0)
        };
        DockPanel.SetDock(glyph,Dock.Right);
        heading.Children.Add(glyph);
        heading.Children.Add(new TextBlock
        {
            Text=title,Tag="SectionTitle",FontFamily=new FontFamily("Arial"),FontSize=15,
            FontWeight=FontWeights.ExtraBold,Foreground=cream,TextAlignment=TextAlignment.Right
        });
        return new Border
        {
            Background=(Brush)new BrushConverter().ConvertFrom(
                title=="باقة وسيرفر الإرسال"?"#791B37":"#63223C")!,
            BorderBrush=(Brush)new BrushConverter().ConvertFrom("#9C4C60")!,
            BorderThickness=new Thickness(0,0,0,1),
            CornerRadius=new CornerRadius(5,5,0,0),
            Padding=new Thickness(8,3,8,3),
            Margin=new Thickness(0,0,0,2),
            Child=heading
        };
    }

    private Grid CreateReferenceLayout(Border routeSurface,Border formatSurface,
        Border basicsSurface,Border designSurface)
    {
        var full=new Grid{FlowDirection=FlowDirection.LeftToRight};
        full.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(41,GridUnitType.Star)});
        full.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(59,GridUnitType.Star)});
        full.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        full.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});

        foreach(var panel in new[]{routeSurface,formatSurface,basicsSurface,designSurface})
        {
            panel.FlowDirection=FlowDirection.RightToLeft;
            panel.Margin=new Thickness(1,1,1,3);
            panel.HorizontalAlignment=HorizontalAlignment.Stretch;
            panel.VerticalAlignment=VerticalAlignment.Stretch;
        }
        Grid.SetColumn(routeSurface,1);
        full.Children.Add(formatSurface);
        full.Children.Add(routeSurface);

        var printColumn=new StackPanel{FlowDirection=FlowDirection.RightToLeft};
        printColumn.Children.Add(basicsSurface);
        _embeddedPrintHost=new Grid{Margin=new Thickness(1,0,1,1)};
        printColumn.Children.Add(_embeddedPrintHost);
        _status.Foreground=(Brush)new BrushConverter().ConvertFrom("#542037")!;
        _status.Margin=new Thickness(3,0,3,2);
        _status.FontSize=11;
        printColumn.Children.Add(_status);
        Grid.SetRow(printColumn,1);
        full.Children.Add(printColumn);
        Grid.SetColumn(designSurface,1);
        Grid.SetRow(designSurface,1);
        full.Children.Add(designSurface);
        return full;
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
            var appearance=ReadCurrentAppearance();
            var image=PortableCardTemplate.ImportImage(picker.FileName,TemplateImageFolder);
            ApplyCardAppearance(appearance with {TemplateImagePath=image});
            _status.Text="تم استيراد صورة الكرت محليا. اضغط حفظ إعدادات الباقة من الأعلى "+
                "لتبقى الصورة محفوظة ومختارة بعد إغلاق البرنامج.";
        }
        catch(Exception ex) when(ex is IOException or InvalidDataException or ArgumentException or
             NotSupportedException or InvalidOperationException or FormatException
             or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            _status.Text="لا يمكن تحميل هذه الصورة: "+ex.Message+
                " | بعض الصيغ تحتاج برنامج ترميز صور يدعمه Windows.";
        }
    }
}
