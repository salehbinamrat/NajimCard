using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        // Print-card artwork has its own contrast and font sizes. Never apply
        // form-label cream text to the light paper preview underneath.
        if(node is FrameworkElement element && Equals(element.Tag,"CardArtwork"))return;
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
    private readonly TextBlock _previewPassword=new()
    {
        Name="CardPasswordPreview",FontFamily=new FontFamily("Consolas"),FontSize=13,FontWeight=FontWeights.Bold,
        FlowDirection=FlowDirection.LeftToRight,TextAlignment=TextAlignment.Center,
        MaxWidth=280,TextTrimming=TextTrimming.CharacterEllipsis,Visibility=Visibility.Collapsed
    };
    private CardElementPanel? _previewElements;

    private Grid BuildSampleVoucherVisual()
    {
        // The default card is graphic artwork (not a placeholder sentence):
        // a purple geometric print design with independently overlaid data.
        // A selected per-package PNG/JPG/... replaces the base artwork.
        var card=new Grid{
            Tag="CardArtwork",
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

        var elements=new CardElementPanel{ClipToBounds=true};
        _previewElements=elements;
        _previewNetworkName.Text="نجم كارد ✦";
        _previewNetworkName.FontFamily=new FontFamily("Arial");
        _previewNetworkName.Foreground=(Brush)new BrushConverter().ConvertFrom("#210B47")!;
        _previewNetworkName.FontSize=20;
        _previewNetworkName.FontWeight=FontWeights.ExtraBold;
        _previewNetworkName.TextAlignment=TextAlignment.Center;

        var usernamePlate=new Border{
            Background=new SolidColorBrush(Color.FromArgb(225,255,250,237)),
            BorderBrush=(Brush)new BrushConverter().ConvertFrom("#8A50CF")!,
            BorderThickness=new Thickness(1),
            CornerRadius=new CornerRadius(8),
            Padding=new Thickness(4,2,4,2),
            Width=280,
            Child=new Viewbox
            {
                Stretch=Stretch.Uniform,
                StretchDirection=StretchDirection.DownOnly,
                Child=_cardPreview
            }
        };
        _cardPreview.Margin=new Thickness(0);
        _cardPreview.FontWeight=FontWeights.ExtraBold;
        _cardPreview.TextAlignment=TextAlignment.Center;
        _cardPreview.Foreground=(Brush)new BrushConverter().ConvertFrom("#220A46")!;
        _previewUsernamePlate=usernamePlate;
        elements.Add(_previewNetworkName,CardElementPanel.DefaultTitle);
        elements.Add(usernamePlate,CardElementPanel.DefaultCredentials);
        elements.Add(_previewPassword,CardElementPanel.PasswordBelow(null));
        _previewPackageName.MaxWidth=100;
        elements.Add(_previewPackageName,CardElementPanel.DefaultProfile);
        EnableCardDrag(elements,_previewNetworkName,(style,position)=>style with {TitlePosition=position});
        EnableCardDrag(elements,usernamePlate,(style,position)=>style with
        {
            CredentialsPosition=position,
            PasswordPosition=style.PasswordPosition??CardElementPanel.PasswordBelow(style.CredentialsPosition)
        });
        EnableCardDrag(elements,_previewPassword,(style,position)=>style with {PasswordPosition=position});
        EnableCardDrag(elements,_previewPackageName,(style,position)=>style with {ProfilePosition=position});
        card.Children.Add(elements);
        return card;
    }

    private void EnableCardDrag(CardElementPanel panel,FrameworkElement element,
        Func<VoucherCardAppearance,CardElementPosition,VoucherCardAppearance> change)
    {
        element.Cursor=Cursors.SizeAll;
        element.ToolTip="اسحب العنصر لتغيير موضعه داخل الكرت.";
        Point? grabOffset=null;
        Point start=new();
        element.MouseLeftButtonDown+=(_,args)=>
        {
            if(_preparing || _sending || !element.CaptureMouse())return;
            start=args.GetPosition(panel);
            grabOffset=args.GetPosition(element);
            args.Handled=true;
        };
        CardElementPosition Position(Point pointer,Point offset)
        {
            var travelX=panel.ActualWidth-element.ActualWidth;
            var travelY=panel.ActualHeight-element.ActualHeight;
            return new CardElementPosition(
                travelX>0?Math.Clamp((pointer.X-offset.X)/travelX,0,1):0,
                travelY>0?Math.Clamp((pointer.Y-offset.Y)/travelY,0,1):0);
        }
        element.MouseMove+=(_,args)=>
        {
            if(grabOffset is Point offset && args.LeftButton==MouseButtonState.Pressed)
                panel.Place(element,Position(args.GetPosition(panel),offset));
        };
        element.MouseLeftButtonUp+=(_,args)=>
        {
            if(grabOffset is not Point offset)return;
            var pointer=args.GetPosition(panel);
            grabOffset=null;
            element.ReleaseMouseCapture();
            if((pointer-start).Length>0)
            {
                ApplyCardAppearance(change(_currentAppearance,Position(pointer,offset)),refreshArtwork:false);
                _designStatus.Text="تم تغيير الموضع. احفظ إعدادات القالب لهذه الباقة للاحتفاظ به.";
            }
            args.Handled=true;
        };
        element.LostMouseCapture+=(_,_) =>
        {
            if(grabOffset is null)return;
            grabOffset=null;
            PlacePreviewElements(_currentAppearance);
        };
    }

    private void PlacePreviewElements(VoucherCardAppearance appearance)
    {
        if(_previewElements is null || _previewUsernamePlate is null)return;
        _previewElements.Place(_previewNetworkName,appearance.TitlePosition??CardElementPanel.DefaultTitle);
        _previewElements.Place(_previewUsernamePlate,appearance.CredentialsPosition??CardElementPanel.DefaultCredentials);
        _previewElements.Place(_previewPassword,appearance.PasswordPosition??
            CardElementPanel.PasswordBelow(appearance.CredentialsPosition));
        _previewElements.Place(_previewPackageName,appearance.ProfilePosition??CardElementPanel.DefaultProfile);
    }

    private void SubscribeLiveCardAppearance()
    {
        void Update(object sender,RoutedEventArgs args)
        {
            if(_restoringPreset)return;
            try
            {
                ApplyCardAppearance(ReadCurrentAppearance(),refreshArtwork:false);
                _appearancePreviewBorder?.ClearValue(ToolTipProperty);
            }
            catch(Exception ex) when(ex is ArgumentException or FormatException or OverflowException)
            {
                // Keep the last valid preview while the user is typing.
                if(_appearancePreviewBorder is not null)
                    _appearancePreviewBorder.ToolTip=ex.Message;
            }
        }

        foreach(var field in new[]{_designFontSize,_designTextColor,_designBorderColor,_designBorderWidth})
            field.TextChanged+=Update;
        _designFontFamily.SelectionChanged+=Update;
        _designShowPassword.Checked+=Update;
        _designShowPassword.Unchecked+=Update;
        _designShowProfile.Checked+=Update;
        _designShowProfile.Unchecked+=Update;
    }

    private void ApplyCardAppearance(VoucherCardAppearance appearance,bool refreshArtwork=true)
    {
        appearance.Validate();
        var changed=_currentAppearance!=appearance;
        _currentAppearance=appearance;
        if(changed)CardAppearanceChanged?.Invoke();
        PlacePreviewElements(appearance);
        _previewNetworkName.Text=appearance.NetworkTitle+" ✦";
        _previewPackageName.Text=_profile.SelectedItem?.ToString()??"الباقة";
        _previewPackageName.Visibility=appearance.ShowProfile?Visibility.Visible:Visibility.Collapsed;
        var hasPicture=!string.IsNullOrWhiteSpace(appearance.TemplateImagePath);
        var ink=(Brush)new BrushConverter().ConvertFromInvariantString(appearance.UsernameColor)!;
        var backing=new SolidColorBrush(PrintingView.ChooseArtworkTextBacking(appearance.UsernameColor));
        _previewNetworkName.Foreground=ink;
        _previewNetworkName.Background=hasPicture || appearance.TitlePosition is not null?backing:null;
        _previewPackageName.Foreground=hasPicture || appearance.ProfilePosition is not null?ink:Brushes.White;
        _previewPackageName.Background=hasPicture || appearance.ProfilePosition is not null?backing:null;
        foreach(var shape in _sampleArtwork)
            shape.Visibility=hasPicture?Visibility.Collapsed:Visibility.Visible;
        _cardPreview.FontFamily=new FontFamily(appearance.FontFamily);
        _cardPreview.FontSize=appearance.UsernameFontSize;
        _cardPreview.Foreground=ink;
        _previewPassword.Foreground=ink;
        _previewPassword.Background=backing;
        _templateImagePath=appearance.TemplateImagePath;
        if(_previewUsernamePlate is not null)
            _previewUsernamePlate.Background=backing;
        if(_appearancePreviewBorder is not null)
        {
            _appearancePreviewBorder.BorderBrush=(Brush)new BrushConverter()
                .ConvertFromInvariantString(appearance.BorderColor)!;
            _appearancePreviewBorder.BorderThickness=new Thickness(appearance.BorderWidth);
            if(!refreshArtwork)return;
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
        }
    }
}
