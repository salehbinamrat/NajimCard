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
    private static Border ReferenceHeader(string title)=>new()
    {
        Background=(Brush)new BrushConverter().ConvertFrom(
            title=="بيانات ومسار الإرسال"?"#791B37":"#63223C")!,
        BorderBrush=(Brush)new BrushConverter().ConvertFrom("#9C4C60")!,
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
        // Owner-approved reference: route and credential settings span the
        // ENTIRE editor width; only the lower print/design surfaces are split.
        // This prevents User Manager customer/profile selectors from vanishing
        // in the old 58%-width upper-right column.
        var full=new Grid{FlowDirection=FlowDirection.RightToLeft};
        full.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        full.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        full.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        foreach(var panel in new[]{routeSurface,formatSurface,basicsSurface,designSurface})
        {
            panel.FlowDirection=FlowDirection.RightToLeft;
            panel.Margin=new Thickness(1,1,1,3);
            panel.HorizontalAlignment=HorizontalAlignment.Stretch;
            panel.VerticalAlignment=VerticalAlignment.Top;
        }
        Grid.SetRow(routeSurface,0);
        Grid.SetRow(formatSurface,1);
        full.Children.Add(routeSurface);
        full.Children.Add(formatSurface);

        var lower=new Grid{FlowDirection=FlowDirection.LeftToRight,
            Margin=new Thickness(0,1,0,0)};
        lower.ColumnDefinitions.Add(new ColumnDefinition{
            Width=new GridLength(39,GridUnitType.Star)});
        lower.ColumnDefinitions.Add(new ColumnDefinition{
            Width=new GridLength(61,GridUnitType.Star)});
        var printColumn=new StackPanel{FlowDirection=FlowDirection.RightToLeft};
        printColumn.Children.Add(basicsSurface);
        _embeddedPrintHost=new Grid{Margin=new Thickness(1,0,1,1)};
        printColumn.Children.Add(_embeddedPrintHost);
        _status.Foreground=(Brush)new BrushConverter().ConvertFrom("#542037")!;
        _status.Margin=new Thickness(3,0,3,2);
        _status.FontSize=11;
        printColumn.Children.Add(_status);
        lower.Children.Add(printColumn);
        Grid.SetColumn(designSurface,1);
        lower.Children.Add(designSurface);
        Grid.SetRow(lower,2);
        full.Children.Add(lower);
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
