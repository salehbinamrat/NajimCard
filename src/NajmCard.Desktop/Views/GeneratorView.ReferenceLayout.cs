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
    private static Border ReferenceHeader(string title, string icon = "IconPackages")
    {
        var cream = new SolidColorBrush(Color.FromRgb(255, 245, 230));
        var heading = new DockPanel { FlowDirection = FlowDirection.RightToLeft };
        var glyph = new System.Windows.Shapes.Path
        {
            Data = (Geometry)Application.Current.FindResource(icon),
            Stroke = cream, StrokeThickness = 1.7, Stretch = Stretch.Uniform,
            Width = 15, Height = 15, Margin = new Thickness(6, 0, 0, 0)
        };
        DockPanel.SetDock(glyph, Dock.Left);
        heading.Children.Add(glyph);
        heading.Children.Add(new TextBlock
        {
            Text = title, Tag = "SectionTitle", FontFamily = new FontFamily("Arial"),
            FontSize = 14, FontWeight = FontWeights.Bold, Foreground = cream,
            TextAlignment = TextAlignment.Right
        });
        return new Border
        {
            Background = new LinearGradientBrush(
                Color.FromRgb(132, 15, 55), Color.FromRgb(69, 9, 27), 90),
            CornerRadius = new CornerRadius(5, 5, 0, 0),
            Padding = new Thickness(7, 3, 7, 3),
            Margin = new Thickness(0, 0, 0, 2),
            Child = heading
        };
    }

    private Grid CreateReferenceLayout(Border routeSurface, Border formatSurface,
        Border basicsSurface, Border designSurface)
    {
        var layout = new Grid { FlowDirection = FlowDirection.LeftToRight };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        foreach (var surface in new[] { routeSurface, formatSurface, basicsSurface, designSurface })
        {
            surface.FlowDirection = FlowDirection.RightToLeft;
            surface.Margin = new Thickness(1, 1, 1, 3);
            surface.VerticalAlignment = VerticalAlignment.Top;
        }
        Grid.SetColumnSpan(routeSurface, 2);
        Grid.SetRow(routeSurface, 0);
        Grid.SetColumnSpan(formatSurface, 2);
        Grid.SetRow(formatSurface, 1);
        layout.Children.Add(routeSurface);
        layout.Children.Add(formatSurface);

        var printColumn = PageUi.Column();
        printColumn.FlowDirection = FlowDirection.RightToLeft;
        printColumn.Children.Add(basicsSurface);
        _embeddedPrintHost = new Grid { Margin = new Thickness(1, 0, 1, 0) };
        printColumn.Children.Add(_embeddedPrintHost);
        _status.FontSize = 11;
        _status.Margin = new Thickness(3, 2, 3, 0);
        _status.Foreground = new SolidColorBrush(Color.FromRgb(84, 32, 55));
        printColumn.Children.Add(_status);
        Grid.SetColumn(printColumn, 0);
        Grid.SetRow(printColumn, 2);
        layout.Children.Add(printColumn);

        Grid.SetColumn(designSurface, 1);
        Grid.SetRow(designSurface, 2);
        layout.Children.Add(designSurface);
        return layout;
    }

    private static Grid WithPlaceholder(ComboBox selector, string prompt)
    {
        var host = new Grid();
        var label = new TextBlock
        {
            Text = prompt, FontSize = 11, IsHitTestVisible = false,
            Margin = new Thickness(8, 0, 25, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        void Update() => label.Visibility = selector.SelectedIndex < 0
            ? Visibility.Visible : Visibility.Collapsed;
        selector.SelectionChanged += (_, _) => Update();
        System.Windows.Automation.AutomationProperties.SetName(selector, prompt);
        host.Children.Add(selector);
        host.Children.Add(label);
        Update();
        return host;
    }

    private void ShowCreatorToast(UIElement anchor, string message, string color)
    {
        _creatorToast?.SetCurrentValue(Popup.IsOpenProperty, false);
        var popup = new Popup
        {
            PlacementTarget = anchor, Placement = PlacementMode.Bottom,
            AllowsTransparency = true, IsHitTestVisible = false,
            Child = new Border
            {
                Background = (Brush)new BrushConverter().ConvertFrom(color)!,
                CornerRadius = new CornerRadius(7), Padding = new Thickness(12, 8, 12, 8),
                Child = new TextBlock
                {
                    Text = message, Foreground = Brushes.FloralWhite,
                    FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 320, FlowDirection = FlowDirection.RightToLeft
                }
            }
        };
        _creatorToast = popup;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += (_, _) => popup.IsOpen = false;
        popup.Closed += (_, _) => timer.Stop();
        popup.IsOpen = true;
        timer.Start();
    }

    private Popup? _creatorToast;

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
