using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using NajmCard.Core.Models;

namespace NajmCard.Desktop.Views;

/// <summary>Default graphical preview and appearance controls for the single
/// unified creator. Rendered cards and printed cards use the same appearance.</summary>
public partial class GeneratorView
{
    private static Border CompactSurface(UIElement content,string accent)
    {
        var surface=PageUi.Surface(content,accent);
        // Hadramout reference geometry with NajmCard burgundy/gold surfaces.
        surface.Padding=new Thickness(6);
        surface.Margin=new Thickness(0);
        surface.CornerRadius=new CornerRadius(9);
        surface.Background=(Brush)new BrushConverter().ConvertFrom("#321526")!;
        surface.BorderBrush=(Brush)new BrushConverter().ConvertFrom("#D2AA61")!;
        RestyleCompactText(content);
        return surface;
    }

    private static void RestyleCompactText(DependencyObject node)
    {
        if(node is TextBlock label && !Equals(label.Tag,"SectionTitle"))
        {
            label.Foreground=(Brush)new BrushConverter().ConvertFrom("#F8E6C7")!;
            label.FontWeight=FontWeights.SemiBold;
            // PageUi.Label has 12pt top padding for spacious pages; inside the
            // single Hadramout-style voucher editor this creates tall empty gaps.
            label.Margin=new Thickness(1,2,1,2);
            label.FontSize=Math.Min(label.FontSize,13);
        }
        if(node is CheckBox checkbox)
            checkbox.Foreground=(Brush)new BrushConverter().ConvertFrom("#F8E6C7")!;
        switch(node)
        {
            case Panel panel:
                foreach(UIElement child in panel.Children) RestyleCompactText(child);
                break;
            case Border border when border.Child is not null:
                RestyleCompactText(border.Child); break;
            case ContentControl holder when holder.Content is UIElement item:
                RestyleCompactText(item); break;
        }
    }

    private readonly TextBlock _previewNetworkName=new();
    private readonly TextBlock _previewPackageName=new();
    private readonly List<UIElement> _sampleArtwork=[];
    private Border? _previewUsernamePlate;

    private Grid BuildSampleVoucherVisual()
    {
        // The default card is graphic artwork (not a placeholder sentence):
        // a purple geometric print design with independently overlaid data.
        // A selected per-package PNG/JPG/... replaces the base artwork.
        var card=new Grid{
            Width=465,Height=178,
            HorizontalAlignment=HorizontalAlignment.Center,
            VerticalAlignment=VerticalAlignment.Center,
            FlowDirection=FlowDirection.LeftToRight,ClipToBounds=true
        };
        var rounded=new Border{
            Background=new LinearGradientBrush(
                (Color)ColorConverter.ConvertFromString("#FFF8FF")!,
                (Color)ColorConverter.ConvertFromString("#E3CAF9")!,0),
            CornerRadius=new CornerRadius(12),
            BorderThickness=new Thickness(1),
            BorderBrush=(Brush)new BrushConverter().ConvertFrom("#8749D5")!
        };
        card.Children.Add(rounded);_sampleArtwork.Add(rounded);

        var leftDisc=new Border{
            Width=98,Height=173,CornerRadius=new CornerRadius(0,78,78,0),
            HorizontalAlignment=HorizontalAlignment.Left,
            VerticalAlignment=VerticalAlignment.Center,
            Background=new LinearGradientBrush(
                (Color)ColorConverter.ConvertFromString("#320A78")!,
                (Color)ColorConverter.ConvertFromString("#A349F6")!,30)
        };
        var discContent=new StackPanel{VerticalAlignment=VerticalAlignment.Center};
        discContent.Children.Add(new TextBlock{
            Text="نجم",FontFamily=new FontFamily("Arial"),
            Foreground=Brushes.White,FontSize=25,FontWeight=FontWeights.ExtraBold,
            TextAlignment=TextAlignment.Center});
        _previewPackageName.FontFamily=new FontFamily("Arial");
        _previewPackageName.FontSize=16;
        _previewPackageName.Foreground=Brushes.White;
        _previewPackageName.FontWeight=FontWeights.ExtraBold;
        _previewPackageName.TextAlignment=TextAlignment.Center;
        _previewPackageName.TextWrapping=TextWrapping.Wrap;
        discContent.Children.Add(_previewPackageName);
        leftDisc.Child=discContent;
        card.Children.Add(leftDisc);_sampleArtwork.Add(leftDisc);

        var rightAccent=new Border{
            Width=82,Height=170,CornerRadius=new CornerRadius(74,0,0,74),
            HorizontalAlignment=HorizontalAlignment.Right,
            VerticalAlignment=VerticalAlignment.Center,
            Background=new LinearGradientBrush(
                (Color)ColorConverter.ConvertFromString("#D4A8FF")!,
                (Color)ColorConverter.ConvertFromString("#8B40E2")!,70)
        };
        card.Children.Add(rightAccent);_sampleArtwork.Add(rightAccent);

        var data=new StackPanel{
            Width=280,Margin=new Thickness(100,6,80,7),
            HorizontalAlignment=HorizontalAlignment.Center,
            VerticalAlignment=VerticalAlignment.Center
        };
        _previewNetworkName.Text="نجم كارد ✦";
        _previewNetworkName.FontFamily=new FontFamily("Arial");
        _previewNetworkName.Foreground=(Brush)new BrushConverter().ConvertFrom("#210B47")!;
        _previewNetworkName.FontSize=20;
        _previewNetworkName.FontWeight=FontWeights.ExtraBold;
        _previewNetworkName.TextAlignment=TextAlignment.Center;
        data.Children.Add(_previewNetworkName);
        var usernamePlate=new Border{
            Background=new SolidColorBrush(Color.FromArgb(225,255,250,237)),
            BorderBrush=(Brush)new BrushConverter().ConvertFrom("#8A50CF")!,
            BorderThickness=new Thickness(1),
            CornerRadius=new CornerRadius(8),
            Padding=new Thickness(4,2,4,2),
            Margin=new Thickness(1,6,1,3),
            Child=_cardPreview
        };
        _cardPreview.Margin=new Thickness(0);
        _cardPreview.FontWeight=FontWeights.ExtraBold;
        _cardPreview.TextAlignment=TextAlignment.Center;
        _cardPreview.Foreground=(Brush)new BrushConverter().ConvertFrom("#220A46")!;
        _previewUsernamePlate=usernamePlate;
        data.Children.Add(usernamePlate);
        data.Children.Add(new TextBlock{
            Text="معاينة القالب — بيانات غير مرسلة",
            Foreground=(Brush)new BrushConverter().ConvertFrom("#5A4074")!,
            FontSize=9,FontWeight=FontWeights.SemiBold,
            TextAlignment=TextAlignment.Center,
            FontFamily=new FontFamily("Arial")
        });
        card.Children.Add(data);
        return card;
    }

    private void ApplyCardAppearance(VoucherCardAppearance appearance)
    {
        appearance.Validate();
        _currentAppearance=appearance;
        _previewNetworkName.Text=appearance.NetworkTitle+" ✦";
        _previewPackageName.Text=_profile.SelectedItem?.ToString()??"الباقة";
        var hasPicture=!string.IsNullOrWhiteSpace(appearance.TemplateImagePath);
        foreach(var shape in _sampleArtwork)
            shape.Visibility=hasPicture?Visibility.Collapsed:Visibility.Visible;
        _cardPreview.FontFamily=new FontFamily(appearance.FontFamily);
        _cardPreview.FontSize=appearance.UsernameFontSize;
        _cardPreview.Foreground=(Brush)new BrushConverter()
            .ConvertFromInvariantString(appearance.UsernameColor)!;
        _templateImagePath=appearance.TemplateImagePath;
        if(_previewUsernamePlate is not null)
            _previewUsernamePlate.Background=new SolidColorBrush(
                PrintingView.ChooseArtworkTextBacking(appearance.UsernameColor));
        if(_appearancePreviewBorder is not null)
        {
            Brush background=(Brush)new BrushConverter()
                .ConvertFromInvariantString(appearance.BackgroundColor)!;
            if(!string.IsNullOrWhiteSpace(appearance.TemplateImagePath) &&
                File.Exists(appearance.TemplateImagePath))
            {
                // Native WIC bitmap decoding preserves the actual uploaded art.
                // No image paths or data leave this Windows installation.
                try
                {
                    var bitmap=new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption=BitmapCacheOption.OnLoad;
                    bitmap.UriSource=new Uri(appearance.TemplateImagePath,UriKind.Absolute);
                    bitmap.EndInit();
                    bitmap.Freeze();
                    background=new ImageBrush(bitmap){Stretch=Stretch.Fill};
                }
                catch(Exception ex) when(ex is IOException or NotSupportedException
                    or InvalidOperationException or ArgumentException
                    or FormatException or UnauthorizedAccessException
                    or System.Runtime.InteropServices.COMException)
                {
                    // The original settings stay intact; fail back to contrast-safe paper.
                }
            }
            _appearancePreviewBorder.Background=background;
            _appearancePreviewBorder.BorderBrush=(Brush)new BrushConverter()
                .ConvertFromInvariantString(appearance.BorderColor)!;
            _appearancePreviewBorder.BorderThickness=new Thickness(appearance.BorderWidth);
        }
    }
}
