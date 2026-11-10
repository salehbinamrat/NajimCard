using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using NajmCard.Core.Models;
using NajmCard.Desktop.Services;
using Microsoft.Win32;

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
        surface.Background=(Brush)new BrushConverter().ConvertFrom("#FFF4DF")!;
        surface.BorderBrush=(Brush)new BrushConverter().ConvertFrom("#75203E")!;
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
            label.Foreground=(Brush)new BrushConverter().ConvertFrom("#48192A")!;
            label.FontWeight=FontWeights.SemiBold;
            // PageUi.Label has 12pt top padding for spacious pages; inside the
            // single Hadramout-style voucher editor this creates tall empty gaps.
            label.Margin=new Thickness(1,2,1,2);
            label.FontSize=Math.Min(label.FontSize,13);
        }
        if(node is CheckBox checkbox)
            checkbox.Foreground=(Brush)new BrushConverter().ConvertFrom("#48192A")!;
        if(node is RadioButton radio)
            radio.Foreground=(Brush)new BrushConverter().ConvertFrom("#48192A")!;
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
    private FrameworkElement? _defaultArtworkLayer;
    private Border? _previewUsernamePlate;
    private Border? _previewPasswordPlate;
    private Grid? _previewCardCanvas;
    private static double MmToDip(double millimeters)=>millimeters*96d/25.4d;

    private void RefreshPreviewCardGeometry()
    {
        if(_previewCardCanvas is null || _previewUsernamePlate is null ||
           _previewPasswordPlate is null || PrintOptionsProvider is null)return;
        try
        {
            var options=PrintOptionsProvider();
            _previewCardCanvas.Width=MmToDip(options.CardWidthMm);
            _previewCardCanvas.Height=MmToDip(options.CardHeightMm);
            CardCredentialPlate.Resize(_previewUsernamePlate,
                options.CardWidthMm,options.CardHeightMm);
            CardCredentialPlate.Resize(_previewPasswordPlate,
                options.CardWidthMm,options.CardHeightMm);
        }
        catch(ArgumentException) { /* The user might still be editing width/height. */ }
        catch(FormatException) { /* Keep last valid preview geometry. */ }
    }
    private readonly TextBlock _previewPassword=new()
    {
        Name="CardPasswordPreview",FontFamily=new FontFamily("Consolas"),FontSize=13,FontWeight=FontWeights.Bold,
        FlowDirection=FlowDirection.LeftToRight,TextAlignment=TextAlignment.Center,
        Background=Brushes.Transparent,
        TextTrimming=TextTrimming.CharacterEllipsis,Visibility=Visibility.Collapsed
    };
    private readonly TextBlock _previewCapacity=OverlayText();
    private readonly TextBlock _previewPhone=OverlayText();
    private readonly TextBlock _previewTime=OverlayText();
    private readonly TextBlock _previewDate=OverlayText();
    private readonly TextBlock _previewSequence=OverlayText();
    private readonly TextBlock _previewRoute=OverlayText();
    private readonly Image _previewQr=new() {Stretch=Stretch.Uniform,Visibility=Visibility.Collapsed};
    private static TextBlock OverlayText()=>new()
    {
        FontFamily=new FontFamily("Arial"),FontSize=9,FontWeight=FontWeights.Bold,
        TextAlignment=TextAlignment.Center,FlowDirection=FlowDirection.LeftToRight,
        Background=Brushes.Transparent,Visibility=Visibility.Collapsed
    };
    private CardElementPanel? _previewElements;

    private Grid BuildSampleVoucherVisual()
    {
        // The default card is graphic artwork (not a placeholder sentence):
        // a purple geometric print design with independently overlaid data.
        // A selected per-package PNG/JPG/... replaces the base artwork.
        var card=new Grid{
            Tag="CardArtwork",
            Width=MmToDip(50),Height=MmToDip(19),
            HorizontalAlignment=HorizontalAlignment.Center,
            VerticalAlignment=VerticalAlignment.Center,
            FlowDirection=FlowDirection.LeftToRight,ClipToBounds=true
        };
        _previewCardCanvas=card;
        // Shared vector drawing exactly matches the print output when the
        // built-in Najm Net appearance is selected; no external picture needed.
        _defaultArtworkLayer=NajmNetCardArtwork.Create(MmToDip(50),MmToDip(19));
        card.Children.Add(_defaultArtworkLayer);

        var elements=new CardElementPanel{ClipToBounds=true};
        _previewElements=elements;
        _previewNetworkName.Text="نجم نت";
        _previewNetworkName.FontFamily=new FontFamily("Arial");
        _previewNetworkName.Foreground=(Brush)new BrushConverter().ConvertFrom("#210B47")!;
        _previewNetworkName.FontSize=20;
        _previewNetworkName.FontWeight=FontWeights.ExtraBold;
        _previewNetworkName.TextAlignment=TextAlignment.Center;

        var usernamePlate=CardCredentialPlate.Build(_cardPreview,50,19);
        var passwordPlate=CardCredentialPlate.Build(_previewPassword,50,19);
        passwordPlate.Visibility=Visibility.Collapsed;
        _previewPasswordPlate=passwordPlate;
        _cardPreview.Margin=new Thickness(0);
        _cardPreview.FontWeight=FontWeights.ExtraBold;
        _cardPreview.TextAlignment=TextAlignment.Center;
        _cardPreview.Foreground=(Brush)new BrushConverter().ConvertFrom("#220A46")!;
        _previewUsernamePlate=usernamePlate;
        elements.Add(_previewNetworkName,CardElementPanel.DefaultTitle);
        elements.Add(usernamePlate,CardElementPanel.DefaultCredentials);
        elements.Add(passwordPlate,CardElementPanel.PasswordBelow(null));
        _previewPackageName.MaxWidth=90;
        elements.Add(_previewPackageName,CardElementPanel.DefaultProfile);
        elements.Add(_previewCapacity,new CardElementPosition(0.75,0.13));
        elements.Add(_previewPhone,new CardElementPosition(0.08,0.91));
        elements.Add(_previewQr,new CardElementPosition(0.85,0.45));
        elements.Add(_previewTime,new CardElementPosition(0.74,0.90));
        elements.Add(_previewDate,new CardElementPosition(0.48,0.90));
        elements.Add(_previewSequence,new CardElementPosition(0.08,0.57));
        elements.Add(_previewRoute,new CardElementPosition(0.08,0.13));
        EnableCardEditing(elements,_previewNetworkName,
            (style,position)=>style with {TitlePosition=position},
            (style,step)=>style with {TitleScale=ResizeScale(style.TitleScale,step)});
        EnableCardEditing(elements,usernamePlate,(style,position)=>style with
        {
            CredentialsPosition=position,
            PasswordPosition=style.PasswordPosition??CardElementPanel.PasswordBelow(style.CredentialsPosition)
        },(style,step)=>style with {UsernameFontSize=Math.Clamp(style.UsernameFontSize+step,8,40)});
        EnableCardEditing(elements,passwordPlate,
            (style,position)=>style with {PasswordPosition=position},
            (style,step)=>style with {PasswordScale=ResizeScale(style.PasswordScale,step)});
        EnableCardEditing(elements,_previewPackageName,
            (style,position)=>style with {ProfilePosition=position},
            (style,step)=>style with {ProfileScale=ResizeScale(style.ProfileScale,step)});
        EnableCardEditing(elements,_previewCapacity,
            (style,position)=>style with {CapacityPosition=position},(style,_)=>style);
        EnableCardEditing(elements,_previewPhone,
            (style,position)=>style with {PhonePosition=position},(style,_)=>style);
        EnableCardEditing(elements,_previewQr,
            (style,position)=>style with {QrPosition=position},
            (style,step)=>style with {QrSize=Math.Clamp(style.QrSize+step*2,12,72)});
        EnableCardEditing(elements,_previewTime,
            (style,position)=>style with {TimePosition=position},(style,_)=>style);
        EnableCardEditing(elements,_previewDate,
            (style,position)=>style with {DatePosition=position},(style,_)=>style);
        EnableCardEditing(elements,_previewSequence,
            (style,position)=>style with {SequencePosition=position},(style,_)=>style);
        EnableCardEditing(elements,_previewRoute,
            (style,position)=>style with {RoutePosition=position},(style,_)=>style);
        card.Children.Add(elements);
        return card;
    }

    private static double ResizeScale(double scale,int step) =>
        Math.Clamp(Math.Round(scale+step*0.1,2),0.4,4);

    private void EnableCardEditing(CardElementPanel panel,FrameworkElement element,
        Func<VoucherCardAppearance,CardElementPosition,VoucherCardAppearance> change,
        Func<VoucherCardAppearance,int,VoucherCardAppearance> resize)
    {
        element.Cursor=Cursors.SizeAll;
        element.ToolTip="اسحب لتغيير الموضع، واستخدم عجلة الماوس لتغيير الحجم.";
        element.PreviewMouseWheel+=(_,args)=>
        {
            if(_preparing || _sending || element.IsMouseCaptured || args.Delta==0)return;
            args.Handled=true;
            try
            {
                var before=ReadCurrentAppearance();
                var after=resize(before,Math.Sign(args.Delta));
                if(before==after)return;
                ApplyCardAppearance(after,refreshArtwork:false);
                if(before.UsernameFontSize!=after.UsernameFontSize)
                    _designFontSize.Text=after.UsernameFontSize.ToString(CultureInfo.InvariantCulture);
                if(before.QrSize!=after.QrSize)
                    _designQrSize.Text=after.QrSize.ToString(CultureInfo.InvariantCulture);
                _designStatus.Text="تم تغيير الحجم. احفظ إعدادات الباقة من الزر العلوي للاحتفاظ به.";
            }
            catch(Exception ex) when(ex is ArgumentException or FormatException or OverflowException)
            {
                _designStatus.Text=ex.Message;
            }
        };
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
        _previewElements.Place(_previewPasswordPlate!,appearance.PasswordPosition??
            CardElementPanel.PasswordBelow(appearance.CredentialsPosition));
        _previewElements.Place(_previewPackageName,appearance.ProfilePosition??CardElementPanel.DefaultProfile);
        _previewElements.Place(_previewCapacity,appearance.CapacityPosition??new CardElementPosition(0.75,0.13));
        _previewElements.Place(_previewPhone,appearance.PhonePosition??new CardElementPosition(0.08,0.91));
        _previewElements.Place(_previewQr,appearance.QrPosition??new CardElementPosition(0.85,0.45));
        _previewElements.Place(_previewTime,appearance.TimePosition??new CardElementPosition(0.74,0.90));
        _previewElements.Place(_previewDate,appearance.DatePosition??new CardElementPosition(0.48,0.90));
        _previewElements.Place(_previewSequence,appearance.SequencePosition??new CardElementPosition(0.08,0.57));
        _previewElements.Place(_previewRoute,appearance.RoutePosition??new CardElementPosition(0.08,0.13));
    }

    private void RefreshQrPreview(string username)
    {
        if(_currentAppearance.ShowQr && !string.IsNullOrWhiteSpace(username))
        {
            try{_previewQr.Source=CardQrRenderer.Render(username);}
            catch(ArgumentException ex){_previewQr.Source=null;_designStatus.Text=ex.Message;}
        }
        else _previewQr.Source=null;
    }

    private static string TemplateImageFolder => Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData),"NajmCard","CardTemplates");

    private const string PortableTemplateFilter="قالب نجم كارد (*.najmtemplate)|*.najmtemplate";

    private void ExportPortableTemplate()
    {
        var dialog=new SaveFileDialog
        {
            Title="تصدير قالب الكرت",Filter=PortableTemplateFilter,
            DefaultExt=".najmtemplate",AddExtension=true,FileName="قالب_نجم_كارد"
        };
        if(dialog.ShowDialog()!=true)return;
        try
        {
            PortableCardTemplate.Export(dialog.FileName,ReadCurrentAppearance());
            _designStatus.Text="تم تصدير التصميم وصورته في ملف القالب.";
        }
        catch(Exception ex) when(ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or
            NotSupportedException or FormatException or InvalidOperationException or
            System.Runtime.InteropServices.COMException)
        {
            _designStatus.Text="تعذر تصدير القالب: "+ex.Message;
        }
    }

    private void ImportPortableTemplate()
    {
        var dialog=new OpenFileDialog
        {
            Title="استيراد قالب الكرت",Filter=PortableTemplateFilter,CheckFileExists=true
        };
        if(dialog.ShowDialog()!=true)return;
        try
        {
            var appearance=PortableCardTemplate.Import(dialog.FileName,TemplateImageFolder);
            _restoringPreset=true;
            try{LoadCardAppearanceFields(appearance);}
            finally{_restoringPreset=false;UpdateLiveCardPreview();}
            _designStatus.Text="تم استيراد القالب. احفظ إعدادات القالب لهذه الباقة للاحتفاظ به.";
        }
        catch(Exception ex) when(ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or
            NotSupportedException or FormatException or InvalidOperationException or
            System.Runtime.InteropServices.COMException)
        {
            _designStatus.Text="تعذر استيراد القالب: "+ex.Message;
        }
    }

    private void LoadCardAppearanceFields(VoucherCardAppearance appearance)
    {
        _designTitle.Text=appearance.NetworkTitle;
        _designFontFamily.SelectedItem=appearance.FontFamily;
        _designFontSize.Text=appearance.UsernameFontSize.ToString(CultureInfo.InvariantCulture);
        _designBackground.Text=appearance.BackgroundColor;
        _designTextColor.Text=appearance.UsernameColor;
        _designBorderColor.Text=appearance.BorderColor;
        _designBorderWidth.Text=appearance.BorderWidth.ToString(CultureInfo.InvariantCulture);
        _designShowPassword.IsChecked=appearance.ShowPassword;
        _designShowProfile.IsChecked=appearance.ShowProfile;
        _designShowCapacity.IsChecked=appearance.ShowCapacity;
        _designShowPhone.IsChecked=appearance.ShowPhone;
        _designShowQr.IsChecked=appearance.ShowQr;
        _designShowTime.IsChecked=appearance.ShowTime;
        _designShowDate.IsChecked=appearance.ShowDate;
        _designShowTitle.IsChecked=appearance.ShowTitle;
        _designShowSequence.IsChecked=appearance.ShowSequence;
        _designShowRoute.IsChecked=appearance.ShowRoute;
        _designCapacityText.Text=appearance.CapacityText;
        _designPhoneText.Text=appearance.PhoneText;
        _designQrSize.Text=appearance.QrSize.ToString(CultureInfo.InvariantCulture);
        ApplyCardAppearance(appearance);
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
        foreach(var check in new[]{_designShowCapacity,_designShowPhone,_designShowQr,
            _designShowTime,_designShowDate,_designShowTitle,_designShowSequence,_designShowRoute})
        {
            check.Checked+=Update;
            check.Unchecked+=Update;
        }
        foreach(var field in new[]{_designCapacityText,_designPhoneText,_designQrSize})
            field.TextChanged+=Update;
    }

    private void ApplyCardAppearance(VoucherCardAppearance appearance,bool refreshArtwork=true)
    {
        appearance.Validate();
        var changed=_currentAppearance!=appearance;
        _currentAppearance=appearance;
        if(changed)CardAppearanceChanged?.Invoke();
        PlacePreviewElements(appearance);
        _previewNetworkName.Text=appearance.NetworkTitle;
        _previewNetworkName.Visibility=appearance.ShowTitle?Visibility.Visible:Visibility.Collapsed;
        // Demonstration labels belong ONLY to the on-screen sample. The actual
        // PDF draws capacity/price/phones strictly from explicit per-package
        // settings, never from these illustrative numbers.
        var builtInSample=NajmNetCardArtwork.IsDefault(
            appearance.NetworkTitle,appearance.TemplateImagePath,
            appearance.BackgroundColor) &&
            !appearance.ShowCapacity && !appearance.ShowPhone && !appearance.ShowTime;
        _previewCapacity.Text=builtInSample?"200 ريال · 2 جيجا":appearance.CapacityText;
        _previewPhone.Text=builtInSample?"780757658 · 775321455":appearance.PhoneText;
        _previewTime.Text=builtInSample?"30 يوم":
            DateTime.Now.ToString("HH:mm",CultureInfo.InvariantCulture);
        _previewDate.Text=DateTime.Now.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
        _previewSequence.Text="0001";
        _previewRoute.Text=SelectedService;
        _previewCapacity.Visibility=builtInSample ||
            appearance.ShowCapacity && appearance.CapacityText.Length>0?
            Visibility.Visible:Visibility.Collapsed;
        _previewPhone.Visibility=builtInSample ||
            appearance.ShowPhone && appearance.PhoneText.Length>0?
            Visibility.Visible:Visibility.Collapsed;
        _previewTime.Visibility=builtInSample || appearance.ShowTime?
            Visibility.Visible:Visibility.Collapsed;
        if(_appearancePreviewBorder is not null && builtInSample)
            _appearancePreviewBorder.ToolTip="صورة نجم نت الافتراضية — الأسعار والسعة "+
                "والمدة والهواتف في المعاينة أمثلة فقط. لا تطبع إلا القيم التي تحفظها للباقة.";
        _previewDate.Visibility=appearance.ShowDate?Visibility.Visible:Visibility.Collapsed;
        _previewSequence.Visibility=appearance.ShowSequence?Visibility.Visible:Visibility.Collapsed;
        _previewRoute.Visibility=appearance.ShowRoute?Visibility.Visible:Visibility.Collapsed;
        _previewQr.Width=appearance.QrSize;_previewQr.Height=appearance.QrSize;
        _previewQr.Visibility=appearance.ShowQr?Visibility.Visible:Visibility.Collapsed;
        RefreshQrPreview(_cardPreview.Text);
        _previewPackageName.Text=(_profile.SelectedItem?.ToString()??"الباقة")+" · "+SelectedService;
        _previewPackageName.Visibility=appearance.ShowProfile?Visibility.Visible:Visibility.Collapsed;
        var hasPicture=!string.IsNullOrWhiteSpace(appearance.TemplateImagePath);
        var ink=(Brush)new BrushConverter().ConvertFromInvariantString(appearance.UsernameColor)!;
        var backing=new SolidColorBrush(PrintingView.ChooseArtworkTextBacking(appearance.UsernameColor));
        _previewNetworkName.Foreground=ink;
        _previewNetworkName.Background=Brushes.Transparent;
        _previewPackageName.Foreground=ink;
        _previewPackageName.Background=Brushes.Transparent;
        if(_defaultArtworkLayer is not null)
            _defaultArtworkLayer.Visibility=NajmNetCardArtwork.IsDefault(
                appearance.NetworkTitle,appearance.TemplateImagePath,
                appearance.BackgroundColor)?Visibility.Visible:Visibility.Collapsed;
        _cardPreview.FontFamily=new FontFamily(appearance.FontFamily);
        _previewNetworkName.FontSize=10*appearance.TitleScale;
        _previewPassword.FontSize=10*appearance.PasswordScale;
        _previewPackageName.FontSize=9*appearance.ProfileScale;
        _cardPreview.FontSize=appearance.UsernameFontSize;
        _cardPreview.Foreground=ink;
        _previewPassword.Foreground=ink;
        _previewPassword.Background=Brushes.Transparent;
        foreach(var label in new[]{_previewCapacity,_previewPhone,_previewTime,
            _previewDate,_previewSequence,_previewRoute})
            label.Foreground=ink;
        _templateImagePath=appearance.TemplateImagePath;
        if(_previewUsernamePlate is not null)
            _previewUsernamePlate.Background=Brushes.Transparent;
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
