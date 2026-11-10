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

    private Border BuildDesignerPanel(bool embedded)
    {
        // The preview consumes the available height; fixed-height StackPanel used
        // to strand a large blank strip below the card design controls.
        var designer=new Grid();
        for(int i=0;i<7;i++)
            designer.RowDefinitions.Add(new RowDefinition{Height=i==2
                ?new GridLength(1,GridUnitType.Star):GridLength.Auto});
        void AddPanel(UIElement element,int row)
        {
            Grid.SetRow(element,row);
            designer.Children.Add(element);
        }
        AddPanel(ReferenceHeader("معاينة وتصميم الكرت"),0);
        var toolbar=new UniformGrid{Columns=3,Margin=new Thickness(0,0,0,2)};
        var templateImage=PageUi.Button("صورة القالب",false);
        var changeTemplate=PageUi.Button("تغيير القالب",false);
        var advanced=PageUi.Button("تعديلات متقدمة",false);
        foreach(var button in new[]{templateImage,changeTemplate,advanced})
        {
            button.MinWidth=0;button.Margin=new Thickness(2);
            button.Padding=new Thickness(4,5,4,5);
            button.FontSize=12;button.FontWeight=FontWeights.Bold;
            button.MinHeight=32;
            toolbar.Children.Add(button);
        }
        templateImage.Background=(Brush)new BrushConverter().ConvertFrom("#0D766A")!;
        changeTemplate.Background=(Brush)new BrushConverter().ConvertFrom("#175F9E")!;
        advanced.Background=(Brush)new BrushConverter().ConvertFrom("#663CAA")!;
        foreach(var button in new[]{templateImage,changeTemplate,advanced})
            button.Foreground=Brushes.White;
        AddPanel(toolbar,1);
        templateImage.Click+=(_,_)=>ChooseTemplateImage();

        var previewRow=_designPreviewRow;
        previewRow.FlowDirection=FlowDirection.LeftToRight;
        previewRow.Margin=new Thickness(0,0,0,2);
        previewRow.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        previewRow.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(168)});
        var options=PageUi.Column();
        options.FlowDirection=FlowDirection.RightToLeft;
        options.Children.Add(ReferenceHeader("خيارات مظهر الكرت"));
        void Option(string label,Control field)
        {
            field.MinWidth=0;field.Width=double.NaN;
            var line=new Grid{Margin=new Thickness(1,0,1,2)};
            line.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(83)});
            line.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
            var caption=new TextBlock{
                Text=label,FontFamily=new FontFamily("Arial"),FontSize=11,
                FontWeight=FontWeights.Bold,Foreground=
                    (Brush)new BrushConverter().ConvertFrom("#F8E6C7")!,
                VerticalAlignment=VerticalAlignment.Center,
                TextAlignment=TextAlignment.Right,Margin=new Thickness(2)};
            Grid.SetColumn(caption,0);
            Grid.SetColumn(field,1);
            line.Children.Add(caption);
            line.Children.Add(field);
            options.Children.Add(line);
        }
        Option("الخط",_designFontFamily);
        Option("حجم الخط",_designFontSize);
        Option("لون الرقم",_designTextColor);
        Option("لون الحدود",_designBorderColor);
        Option("سمك الحدود",_designBorderWidth);
        Grid.SetColumn(options,1);
        previewRow.Children.Add(options);
        AddPanel(previewRow,2);

        AddPanel(ReferenceHeader("عناصر الكرت"),3);
        var designChecks=new WrapPanel{Margin=new Thickness(0,1,0,1)};
        _designShowPassword.Content="كلمة المرور";
        _designShowProfile.Content="الباقة";
        designChecks.Children.Add(_designShowPassword);
        designChecks.Children.Add(_designShowProfile);
        AddPanel(designChecks,4);
        var saveAppearanceButton=PageUi.Button("حفظ إعدادات القالب لهذه الباقة");
        saveAppearanceButton.MinWidth=0;saveAppearanceButton.Padding=new Thickness(8,6,8,6);
        saveAppearanceButton.Margin=new Thickness(1,1,1,1);
        var designStatus=_designStatus;
        designStatus.Foreground=(Brush)new BrushConverter().ConvertFrom("#F8E6C7")!;
        designStatus.Margin=new Thickness(1,2,1,3);designStatus.FontSize=12;
        AddPanel(saveAppearanceButton,5);
        AddPanel(designStatus,6);
        var designSurface=embedded?CompactSurface(designer,"#D2AA61")
            :PageUi.Surface(designer,"#D2AA61");
        saveAppearanceButton.Click+=(_,_)=>{
            designStatus.Text=SaveCurrentPackage()
                ?"تم حفظ تصميم هذه الباقة بما فيه صورة القالب."
                :"تحقق من خصائص الباقة والتصميم والطباعة.";
        };

        int paletteIndex=0;
        changeTemplate.Click+=(_,_)=>{
            paletteIndex=(paletteIndex+1)%3;
            var colors=new[]{
                ("#FFF6EB","#4C1730","#B89159"),
                ("#FFFFFF","#31105E","#9435B6"),
                ("#102A47","#FFFFFF","#D2AA61")
            }[paletteIndex];
            _templateImagePath="";
            _designBackground.Text=colors.Item1;
            _designTextColor.Text=colors.Item2;
            _designBorderColor.Text=colors.Item3;
            try{ApplyCardAppearance(ReadCurrentAppearance());}
            catch(Exception ex){designStatus.Text=ex.Message;}
        };

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
            var save=PageUi.Button("تطبيق وحفظ التصميم",true);
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
            var borderColor=Editable("لون الحدود",_designBorderColor.Text);
            var borderWidth=Editable("سمك الحدود",_designBorderWidth.Text);

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
                _designBorderColor.Text=originalAppearance.BorderColor;
                _designBorderWidth.Text=originalAppearance.BorderWidth.ToString(
                    CultureInfo.InvariantCulture);
                _designShowPassword.IsChecked=originalAppearance.ShowPassword;
                _designShowProfile.IsChecked=originalAppearance.ShowProfile;
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
                    _designBorderColor.Text=borderColor.Text;
                    _designBorderWidth.Text=borderWidth.Text;
                    _designFontFamily.SelectedItem=font.SelectedItem?.ToString()??"Arial";
                    _designShowPassword.IsChecked=showPassword.IsChecked;
                    _designShowProfile.IsChecked=showProfile.IsChecked;
                    ApplyCardAppearance(ReadCurrentAppearance());
                    if(newPrint is not null)ApplyPrintOptions?.Invoke(newPrint);
                    if(SaveCurrentPackage())
                    {
                        savedSuccessfully=true;
                        popup.DialogResult=true;
                    }
                    else
                        MessageBox.Show(_status.Text,"تعذر حفظ تصميم الباقة",
                            MessageBoxButton.OK,MessageBoxImage.Warning);
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
