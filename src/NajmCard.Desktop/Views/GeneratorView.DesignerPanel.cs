using System.Windows;
using System.Globalization;
using NajmCard.Core.Services;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace NajmCard.Desktop.Views;

public partial class GeneratorView
{
    private readonly Grid _designPreviewRow=new();
    private readonly TextBlock _designStatus=PageUi.Status();
    private ComboBox? _inlineWidth;
    private ComboBox? _inlineHeight;
    private bool _syncingInlineSize;

    public void RefreshInlineCardDimensions()
    {
        if(_syncingInlineSize || _inlineWidth is null || _inlineHeight is null ||
           PrintOptionsProvider is null)return;
        try
        {
            var options=PrintOptionsProvider();
            _syncingInlineSize=true;
            var cardWidth=options.CardWidthMm.ToString(CultureInfo.InvariantCulture);
            var cardHeight=options.CardHeightMm.ToString(CultureInfo.InvariantCulture);
            if(!_inlineWidth.Items.Contains(cardWidth))_inlineWidth.Items.Add(cardWidth);
            if(!_inlineHeight.Items.Contains(cardHeight))_inlineHeight.Items.Add(cardHeight);
            _inlineWidth.SelectedItem=cardWidth;
            _inlineHeight.SelectedItem=cardHeight;
        }
        catch(ArgumentException) { /* Do not replace valid input with a partially edited size. */ }
        catch(FormatException) { /* The left print fields may still be in progress. */ }
        finally {_syncingInlineSize=false;}
        RefreshPreviewCardGeometry();
    }

    private Button PaletteButton(string caption,TextBox target)
    {
        var button=PageUi.Button(caption,false);
        button.MinWidth=0;button.MinHeight=28;
        button.Padding=new Thickness(4,2,4,2);
        button.Margin=new Thickness(1);
        var popup=new Popup
        {
            PlacementTarget=button,Placement=PlacementMode.Bottom,
            StaysOpen=false,AllowsTransparency=true
        };
        var palette=new UniformGrid{Columns=6,Background=Brushes.White};
        var colors=new[]{"#4C1730","#74203D","#A33150","#D6263C","#F04438","#F29D38",
            "#F2C94C","#197C59","#2169A0","#543399","#111111","#FFFFFF"};
        foreach(var hex in colors)
        {
            var sample=new Button{Width=25,Height=25,Margin=new Thickness(2),
                Background=(Brush)new BrushConverter().ConvertFrom(hex)!,
                BorderBrush=(Brush)new BrushConverter().ConvertFrom("#74203D")!,
                BorderThickness=new Thickness(1),ToolTip=hex};
            sample.Click+=(_,_)=>{target.Text=hex;popup.IsOpen=false;};
            palette.Children.Add(sample);
        }
        popup.Child=new Border
        {
            Background=Brushes.White,BorderBrush=(Brush)new BrushConverter().ConvertFrom("#74203D")!,
            BorderThickness=new Thickness(2),Padding=new Thickness(5),
            Child=palette
        };
        button.Click+=(_,_)=>popup.IsOpen=!popup.IsOpen;
        button.ToolTip="اختر من شريط الألوان أو اكتب رمز اللون في الخانة المجاورة.";
        void Paint()
        {
            try
            {
                var color=(Color)ColorConverter.ConvertFromString(target.Text)!;
                button.Background=new SolidColorBrush(color);
                button.Content=new TextBlock
                {
                    Text=caption,Tag="ColorButtonLabel",Foreground=0.2126*color.R+0.7152*color.G+0.0722*color.B>135
                        ?Brushes.Black:Brushes.FloralWhite
                };
            }
            catch(FormatException){ /* Keep previous color until a complete hex code is entered. */ }
        }
        target.TextChanged+=(_,_)=>Paint();
        Paint();
        return button;
    }

    private Border BuildDesignerPanel(bool embedded)
    {
        // The card keeps its aspect ratio. Size the row to the artwork and
        // appearance controls so spare viewport height stays outside the panel.
        var designer=new Grid();
        for(int i=0;i<7;i++)
            designer.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        void AddPanel(UIElement element,int row)
        {
            Grid.SetRow(element,row);
            designer.Children.Add(element);
        }
        AddPanel(ReferenceHeader("معاينة وتصميم الكرت"),0);
        var toolbar=new UniformGrid{Columns=2,Margin=new Thickness(0,0,0,2)};
        var templateImage=PageUi.Button("تغيير الثيم",false);
        var advanced=PageUi.Button("تعديلات متقدمة",false);
        foreach(var button in new[]{templateImage,advanced})
        {
            button.MinWidth=0;button.Margin=new Thickness(2);
            button.Padding=new Thickness(3,1,3,1);
            button.FontSize=12;button.FontWeight=FontWeights.Bold;
            button.MinHeight=21;button.Foreground=Brushes.FloralWhite;
            button.Background=(Brush)new BrushConverter().ConvertFrom("#75203E")!;
            toolbar.Children.Add(button);
        }
        var themeMenu=new ContextMenu{PlacementTarget=templateImage,Placement=PlacementMode.Bottom};
        var restoreTheme=new MenuItem{Header="قالب نجم نت الافتراضي"};
        restoreTheme.Click+=(_,_)=>
        {
            try
            {
                var style=(ReadCurrentAppearance() with
                {
                    NetworkTitle="نجم نت",TemplateImagePath="",BackgroundColor="#FFF6EB"
                }).Validate();
                LoadCardAppearanceFields(style);
            }
            catch(ArgumentException ex){_designStatus.Text=ex.Message;}
        };
        var importTheme=new MenuItem{Header="اختيار صورة القالب"};
        importTheme.Click+=(_,_)=>ChooseTemplateImage();
        themeMenu.Items.Add(restoreTheme);
        themeMenu.Items.Add(importTheme);
        templateImage.Click+=(_,_)=>themeMenu.IsOpen=true;

        var previewRow=_designPreviewRow;
        previewRow.FlowDirection=FlowDirection.LeftToRight;
        previewRow.Margin=new Thickness(0,0,0,2);
        previewRow.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        previewRow.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(168)});
        var options=PageUi.Column();
        options.FlowDirection=FlowDirection.RightToLeft;
        options.Children.Add(toolbar);
        options.Children.Add(ReferenceHeader("خيارات مظهر الكرت"));
        void Option(string label,FrameworkElement field)
        {
            field.MinWidth=0;field.Width=double.NaN;
            var line=new Grid{Margin=new Thickness(1,0,1,2)};
            line.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(83)});
            line.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
            var caption=new TextBlock{
                Text=label,FontFamily=new FontFamily("Arial"),FontSize=11,
                FontWeight=FontWeights.Bold,Foreground=
                    (Brush)new BrushConverter().ConvertFrom("#48192A")!,
                VerticalAlignment=VerticalAlignment.Center,
                TextAlignment=TextAlignment.Right,Margin=new Thickness(2)};
            Grid.SetColumn(caption,0);
            Grid.SetColumn(field,1);
            line.Children.Add(caption);
            line.Children.Add(field);
            options.Children.Add(line);
        }
        Option("حجم الخط",PageUi.CompactNumberChoice(_designFontSize,
            "8","10","12","14","16","18","20","22","24","26","28","30","32","36","40"));
        Option("نوع الخط",_designFontFamily);
        Option("لون النص",PaletteButton("تغيير",_designElementColor));
        Option("لون رقم الكرت",PaletteButton("تغيير",_designTextColor));
        Grid.SetColumn(options,1);
        previewRow.Children.Add(options);
        AddPanel(previewRow,2);

        // Card size and styling are always visible directly beneath the preview.
        var shapeOptions=new UniformGrid{Columns=5,Margin=new Thickness(0,2,0,3)};
        FrameworkElement ShapeOption(string label,FrameworkElement field)
        {
            var holder=PageUi.Column();
            holder.Margin=new Thickness(2,0,2,0);
            holder.Children.Add(new TextBlock
            {
                Text=label,FontFamily=new FontFamily("Arial"),FontWeight=FontWeights.Bold,
                FontSize=11,TextAlignment=TextAlignment.Center,
                Foreground=(Brush)new BrushConverter().ConvertFrom("#48192A")!
            });
            field.MinWidth=0;
            holder.Children.Add(field);
            return holder;
        }
        _inlineWidth=new ComboBox{IsEditable=false};
        _inlineHeight=new ComboBox{IsEditable=false};
        foreach(var option in new[]{"35","40","45","50","55","60"})
            _inlineWidth.Items.Add(option);
        foreach(var option in new[]{"15","19","20","25","30"})
            _inlineHeight.Items.Add(option);
        _inlineWidth.SelectedItem="50";
        _inlineHeight.SelectedItem="19";
        void ApplyInlineSize()
        {
            if(_syncingInlineSize || PrintOptionsProvider is null || ApplyPrintOptions is null)return;
            try
            {
                var width=double.Parse((_inlineWidth.SelectedItem?.ToString()??_inlineWidth.Text).Trim().Replace(',','.'),
                    CultureInfo.InvariantCulture);
                var height=double.Parse((_inlineHeight.SelectedItem?.ToString()??_inlineHeight.Text).Trim().Replace(',','.'),
                    CultureInfo.InvariantCulture);
                var options=PrintOptionsProvider() with {CardWidthMm=width,CardHeightMm=height};
                CardPrintLayoutPlanner.Calculate(options,1);
                ApplyPrintOptions(options);
                _designStatus.Text="تم تحديث أبعاد الكرت؛ اضغط حفظ إعدادات الباقة من الأعلى.";
            }
            catch(Exception ex) when(ex is ArgumentException or FormatException or OverflowException)
            {
                _designStatus.Text="أبعاد الكرت غير صالحة: "+ex.Message;
            }
        }
        _inlineWidth.SelectionChanged+=(_,_)=>ApplyInlineSize();
        _inlineHeight.SelectionChanged+=(_,_)=>ApplyInlineSize();
        _inlineWidth.LostKeyboardFocus+=(_,_)=>ApplyInlineSize();
        _inlineHeight.LostKeyboardFocus+=(_,_)=>ApplyInlineSize();
        Loaded+=(_,_)=>RefreshInlineCardDimensions();
        shapeOptions.Children.Add(ShapeOption("العرض (مم)",_inlineWidth));
        shapeOptions.Children.Add(ShapeOption("الطول (مم)",_inlineHeight));
        var qrSizes=new ComboBox{ItemsSource=new[]{"16","20","24","28","32","40","48","60"},
            SelectedItem="24"};
        qrSizes.SelectionChanged+=(_,_)=>
        {
            if(qrSizes.SelectedItem is string size)_designQrSize.Text=size;
        };
        _designQrSize.TextChanged+=(_,_)=>qrSizes.SelectedItem=_designQrSize.Text;
        shapeOptions.Children.Add(ShapeOption("حجم QR",qrSizes));
        var borderSizes=new ComboBox{ItemsSource=new[]{"0","0.5","1","1.5","2","3","4","5"},
            SelectedItem="1"};
        borderSizes.SelectionChanged+=(_,_)=>
        {
            if(borderSizes.SelectedItem is string size)_designBorderWidth.Text=size;
        };
        _designBorderWidth.TextChanged+=(_,_)=>borderSizes.SelectedItem=_designBorderWidth.Text;
        shapeOptions.Children.Add(ShapeOption("سمك الحدود",borderSizes));
        shapeOptions.Children.Add(ShapeOption("لون الحدود",PaletteButton("تغيير",_designBorderColor)));
        AddPanel(shapeOptions,3);

        AddPanel(ReferenceHeader("عناصر الكرت"),4);
        var designChecks=new UniformGrid{Columns=5,Rows=2,Margin=new Thickness(0,1,0,1)};
        _designShowPassword.Content="كلمة المرور";
        _designShowProfile.Content="الباقة";
        foreach(var check in new[]{_designShowCapacity,_designShowPhone,_designShowQr,
            _designShowTime,_designShowDate,_designShowTitle,_designShowSequence,_designShowRoute,
            _designShowPassword,_designShowProfile})
        {
            // Explicit labels are independent of the global CheckBox control
            // template, which rendered the indicator but clipped its content.
            var caption=new TextBlock
            {
                Text=check.Content?.ToString()??"",
                FontFamily=new FontFamily("Arial"),FontWeight=FontWeights.Bold,
                FontSize=11,VerticalAlignment=VerticalAlignment.Center,
                TextTrimming=TextTrimming.CharacterEllipsis,
                Foreground=(Brush)new BrushConverter().ConvertFrom("#48192A")!
            };
            check.Content=null;
            check.Margin=new Thickness(2,0,2,0);
            caption.MouseLeftButtonUp+=(_,args)=>
            {
                if(check.IsEnabled)check.IsChecked=check.IsChecked!=true;
                args.Handled=true;
            };
            var option=new StackPanel
            {
                Orientation=Orientation.Horizontal,FlowDirection=FlowDirection.RightToLeft,
                Margin=new Thickness(2,3,2,3),ToolTip=caption.Text
            };
            option.Children.Add(check);
            option.Children.Add(caption);
            designChecks.Children.Add(option);
        }
        AddPanel(designChecks,5);
        var designStatus=_designStatus;
        designStatus.Foreground=(Brush)new BrushConverter().ConvertFrom("#542037")!;
        designStatus.Margin=new Thickness(1,1,1,2);designStatus.FontSize=11;
        if(embedded)designStatus.Margin=new Thickness(1,0,1,1);
        AddPanel(designStatus,6);
        var designSurface=embedded?CompactSurface(designer,"#75203E")
            :PageUi.Surface(designer,"#75203E");

        advanced.Click+=(_,_)=>{
            var originalAppearance=_currentAppearance;
            bool savedSuccessfully=false;
            var popup=new Window{
                Title="نجم كارد — التعديلات المتقدمة",
                Width=700,Height=480,MinWidth=590,MinHeight=360,
                WindowStartupLocation=WindowStartupLocation.CenterOwner,
                Owner=Window.GetWindow(this),FlowDirection=FlowDirection.RightToLeft,
                Background=(Brush)new BrushConverter().ConvertFrom("#200E1E")!
            };
            var pane=new DockPanel{Margin=new Thickness(16)};
            var save=PageUi.Button("تطبيق التعديلات",true);
            save.MinWidth=0;save.Margin=new Thickness(2,8,2,1);
            var fields=new UniformGrid{Columns=2};
            TextBox Editable(string label,string value)
            {
                var entry=new TextBox{
                    Text=value,FontFamily=new FontFamily("Arial"),
                    Foreground=(Brush)new BrushConverter().ConvertFrom("#FFF4E6")!,
                    Background=(Brush)new BrushConverter().ConvertFrom("#301529")!,
                    MinHeight=34,Margin=new Thickness(6,2,6,8)};
                var section=PageUi.Column();
                section.Children.Add(new TextBlock{
                    Text=label,Foreground=(Brush)new BrushConverter().ConvertFrom("#F8E6C7")!,
                    FontFamily=new FontFamily("Arial"),FontWeight=FontWeights.Bold});
                section.Children.Add(entry);
                fields.Children.Add(section);
                return entry;
            }
            var name=Editable("اسم الشبكة",_designTitle.Text);
            var background=Editable("لون خلفية البطاقة (#RRGGBB)",_designBackground.Text);
            var fontSize=Editable("حجم رقم الكرت",_designFontSize.Text);
            var numberColor=Editable("لون رقم الكرت",_designTextColor.Text);
            var elementColor=Editable("لون النص",_designElementColor.Text);
            var borderColor=Editable("لون الحدود",_designBorderColor.Text);
            var borderWidth=Editable("سمك الحدود",_designBorderWidth.Text);
            var capacity=Editable("نص السعة (اختياري)",_designCapacityText.Text);
            var phone=Editable("رقم الهاتف على الكرت (اختياري)",_designPhoneText.Text);
            var qrSize=Editable("حجم QR",_designQrSize.Text);

            // Advanced layout controls use the same live print model as the
            // inline editor; no shadow printer state or pretend dimensions.
            CardPrintOptions? livePrint=null;
            try{livePrint=PrintOptionsProvider?.Invoke();}
            catch(ArgumentException ex){_designStatus.Text=ex.Message;}
            // A dismissed modal must not commit the image picker or partial
            // field edits to the active card. Only a successful save commits.
            popup.Closed+=(_,_)=>{
                if(savedSuccessfully)return;
                _designTitle.Text=originalAppearance.NetworkTitle;
                _designFontFamily.SelectedItem=originalAppearance.FontFamily;
                _designFontSize.Text=originalAppearance.UsernameFontSize.ToString(
                    CultureInfo.InvariantCulture);
                _designBackground.Text=originalAppearance.BackgroundColor;
                _designTextColor.Text=originalAppearance.UsernameColor;
                _designElementColor.Text=originalAppearance.EffectiveElementColor;
                _designBorderColor.Text=originalAppearance.BorderColor;
                _designBorderWidth.Text=originalAppearance.BorderWidth.ToString(
                    CultureInfo.InvariantCulture);
                _designShowPassword.IsChecked=originalAppearance.ShowPassword;
                _designShowProfile.IsChecked=originalAppearance.ShowProfile;
                _designCapacityText.Text=originalAppearance.CapacityText;
                _designPhoneText.Text=originalAppearance.PhoneText;
                _designQrSize.Text=originalAppearance.QrSize.ToString(CultureInfo.InvariantCulture);
                ApplyCardAppearance(originalAppearance);
                if(livePrint is not null)ApplyPrintOptions?.Invoke(livePrint);
            };
            var width=Editable("عرض الكرت (مم)",
                livePrint?.CardWidthMm.ToString(CultureInfo.InvariantCulture)??"50");
            var height=Editable("ارتفاع الكرت (مم)",
                livePrint?.CardHeightMm.ToString(CultureInfo.InvariantCulture)??"19");
            width.IsEnabled=height.IsEnabled=ApplyPrintOptions is not null;
            var fontCell=PageUi.Column();
            fontCell.Children.Add(new TextBlock{
                Text="نوع الخط",Foreground=(Brush)new BrushConverter().ConvertFrom("#F8E6C7")!,
                FontFamily=new FontFamily("Arial"),FontWeight=FontWeights.Bold});
            var font=new ComboBox{
                ItemsSource=new[]{"Arial","Tahoma","Segoe UI","Calibri"},
                SelectedItem=_designFontFamily.SelectedItem?.ToString()??"Arial",
                Foreground=(Brush)new BrushConverter().ConvertFrom("#FFF4E6")!,
                Background=(Brush)new BrushConverter().ConvertFrom("#301529")!,
                MinHeight=34,Margin=new Thickness(6,2,6,8)};
            fontCell.Children.Add(font);
            fields.Children.Add(fontCell);
            var showPassword=new CheckBox{
                Content="إظهار كلمة المرور",IsChecked=_designShowPassword.IsChecked,
                Foreground=(Brush)new BrushConverter().ConvertFrom("#F8E6C7")!,
                Margin=new Thickness(6,10,6,8)};
            var showProfile=new CheckBox{
                Content="إظهار الباقة",IsChecked=_designShowProfile.IsChecked,
                Foreground=(Brush)new BrushConverter().ConvertFrom("#F8E6C7")!,
                Margin=new Thickness(6,10,6,8)};
            fields.Children.Add(showPassword);
            fields.Children.Add(showProfile);
            var chooseImage=PageUi.Button("اختيار صورة الكرت لهذه الباقة",false);
            chooseImage.MinWidth=0;chooseImage.Margin=new Thickness(6,6,6,8);
            chooseImage.Foreground=Brushes.White;
            chooseImage.Background=(Brush)new BrushConverter().ConvertFrom("#176D75")!;
            chooseImage.Click+=(_,_)=>ChooseTemplateImage();
            fields.Children.Add(chooseImage);
            save.Click+=(_,_)=>{
                try{
                    CardPrintOptions? newPrint=null;
                    if(ApplyPrintOptions is not null)
                    {
                        var current=PrintOptionsProvider?.Invoke()??new CardPrintOptions();
                        var w=double.Parse(width.Text.Trim().Replace(',','.'),
                            CultureInfo.InvariantCulture);
                        var h=double.Parse(height.Text.Trim().Replace(',','.'),
                            CultureInfo.InvariantCulture);
                        newPrint=current with{CardWidthMm=w,CardHeightMm=h};
                        CardPrintLayoutPlanner.Calculate(newPrint,1);
                    }
                    _designTitle.Text=name.Text;_designBackground.Text=background.Text;
                    _designFontSize.Text=fontSize.Text;_designTextColor.Text=numberColor.Text;
                    _designElementColor.Text=elementColor.Text;
                    _designBorderColor.Text=borderColor.Text;
                    _designBorderWidth.Text=borderWidth.Text;
                    _designCapacityText.Text=capacity.Text;
                    _designPhoneText.Text=phone.Text;
                    _designQrSize.Text=qrSize.Text;
                    _designFontFamily.SelectedItem=font.SelectedItem?.ToString()??"Arial";
                    _designShowPassword.IsChecked=showPassword.IsChecked;
                    _designShowProfile.IsChecked=showProfile.IsChecked;
                    ApplyCardAppearance(ReadCurrentAppearance());
                    if(newPrint is not null)ApplyPrintOptions?.Invoke(newPrint);
                    savedSuccessfully=true;
                    _designStatus.Text="تم تطبيق التعديلات. احفظ إعدادات الباقة من الزر العلوي.";
                    popup.DialogResult=true;
                }catch(Exception ex){
                    MessageBox.Show(ex.Message,"التحقق من التصميم",
                        MessageBoxButton.OK,MessageBoxImage.Warning);
                }
            };
            DockPanel.SetDock(save,Dock.Bottom);
            pane.Children.Add(save);
            pane.Children.Add(new ScrollViewer{
                VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=fields});
            popup.Content=pane;popup.ShowDialog();
        };


        return designSurface;
    }
}
