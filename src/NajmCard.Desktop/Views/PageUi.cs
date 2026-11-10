using System.Windows.Controls.Primitives;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Win32;
using NajmCard.Core.Models;
using NajmCard.Core.Services;
using NajmCard.Desktop.Services;

namespace NajmCard.Desktop.Views;

/// <summary>Shared visual system: geometric burgundy panels, colorful vector icons and readable RTL grids.</summary>
internal static class PageUi
{
    internal static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(88, 37, 58));
    internal static Brush Gold => new SolidColorBrush(Color.FromRgb(243, 207, 148));
    internal static Border Surface(UIElement content,string accent="#80593D")
    {
        var surface=new Border
        {
            Child=content,CornerRadius=new CornerRadius(12),Padding=new Thickness(12),
            Margin=new Thickness(0,0,0,8),BorderThickness=new Thickness(1),
            Background=new SolidColorBrush(Color.FromRgb(255,243,223)),
            BorderBrush=new SolidColorBrush(Color.FromRgb(114,32,58))
        };
        // Scope dark default labels to cream panels without affecting the
        // burgundy application header, login, sidebar or card print artwork.
        var ordinaryText=new Style(typeof(TextBlock),
            (Style)Application.Current.FindResource(typeof(TextBlock)));
        ordinaryText.Setters.Add(new Setter(TextBlock.ForegroundProperty,
            new SolidColorBrush(Color.FromRgb(75,27,50))));
        surface.Resources[typeof(TextBlock)]=ordinaryText;
        return surface;
    }
    internal static StackPanel Column() => new() { Orientation = Orientation.Vertical };
    internal static TextBlock Title(string text) => new() { Text = text, FontSize = 23, FontWeight = FontWeights.Bold, FontFamily=new FontFamily("Arial"),
        Foreground = Brushes.White,
        Background = new SolidColorBrush(Color.FromRgb(114, 32, 58)),
        Padding = new Thickness(8,5,8,5),Margin = new Thickness(0, 0, 0, 6) };
    internal static TextBlock Hint(string text) => new() { Text = text, Foreground = Muted, FontSize = 14, FontWeight = FontWeights.Medium,
        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
    internal static TextBlock Label(string text) => new() { Text = text, Foreground = new SolidColorBrush(Color.FromRgb(74, 25, 46)), FontSize = 15, FontWeight=FontWeights.SemiBold,
        Margin = new Thickness(0, 3, 0, 2) };
    internal static Button Button(string name,bool primary=true)
    {
        var button=new Button
        {
            Content=name,Style=(Style)Application.Current.FindResource(
                primary?"PrimaryButton":"OutlineButton"),
            Margin=new Thickness(0,0,5,0),MinWidth=112,MinHeight=34,
            Padding=new Thickness(9,6,9,6),FontFamily=new FontFamily("Arial"),
            FontWeight=FontWeights.Bold,FontSize=13
        };
        // A consistent role-based palette across every screen, unless the
        // caller explicitly overrides the action with a more specific color.
        string background=name switch
        {
            var n when n.Contains("حذف",StringComparison.Ordinal) ||
                n.Contains("تعطيل",StringComparison.Ordinal) ||
                n.Contains("إيقاف",StringComparison.Ordinal) ||
                n.Contains("خروج",StringComparison.Ordinal)=>"#982E46",
            var n when n.Contains("استعادة",StringComparison.Ordinal) ||
                n.Contains("تفعيل",StringComparison.Ordinal) ||
                n.Contains("حفظ",StringComparison.Ordinal) ||
                n.Contains("إنشاء",StringComparison.Ordinal)=>"#197854",
            var n when n.Contains("معاينة",StringComparison.Ordinal) ||
                n.Contains("بحث",StringComparison.Ordinal)=>"#197E9A",
            var n when n.Contains("طباعة",StringComparison.Ordinal) ||
                n.Contains("كروت",StringComparison.Ordinal)=>"#285E9C",
            var n when n.Contains("تصدير",StringComparison.Ordinal) ||
                n.Contains("تقارير",StringComparison.Ordinal)=>"#8050A5",
            var n when n.Contains("تحديث",StringComparison.Ordinal) ||
                n.Contains("قراءة",StringComparison.Ordinal)=>"#216C80",
            var n when n.Contains("إعادة تشغيل",StringComparison.Ordinal)=>"#A16618",
            _=>primary?"#E6BC64":"#68233D"
        };
        button.Background=(Brush)new BrushConverter().ConvertFrom(background)!;
        button.Foreground=(Brush)new BrushConverter().ConvertFrom(
            background=="#E6BC64"?"#52152F":"#FFF5E8")!;
        return button;
    }
    internal static TextBlock Status() => new() { TextWrapping = TextWrapping.Wrap,
        Foreground = Muted, Margin = new Thickness(0, 3, 0, 3), FontSize = 12, FontWeight=FontWeights.SemiBold };
    internal static TextBox Input(string value = "") => new() { Text = value, FlowDirection = FlowDirection.LeftToRight };
    // Bounded numeric presets, with the original TextBox kept as the single
    // source of truth for saved package settings and form validation.
    internal static ComboBox CompactNumberChoice(TextBox value,params string[] choices)
    {
        var picker=new ComboBox
        {
            MinWidth=0,MinHeight=30,IsEditable=false,
            FontFamily=new FontFamily("Arial"),FontSize=13,
            FontWeight=FontWeights.Bold,FlowDirection=FlowDirection.LeftToRight,
            HorizontalContentAlignment=HorizontalAlignment.Center
        };
        foreach(var choice in choices.Distinct(StringComparer.Ordinal))
            picker.Items.Add(choice);
        bool syncing=false;
        void FromValue()
        {
            if(syncing)return;
            syncing=true;
            try
            {
                var current=value.Text.Trim().Replace(',','.');
                // Old saved profiles may use a custom value. Display it without
                // discarding the existing preset or expanding the normal list.
                if(current.Length>0 && !picker.Items.Contains(current))
                    picker.Items.Add(current);
                picker.SelectedItem=current;
                picker.IsEnabled=value.IsEnabled;
            }
            finally{syncing=false;}
        }
        picker.SelectionChanged+=(_,_) =>
        {
            if(syncing)return;
            if(picker.SelectedItem is string selected && value.Text!=selected)
                value.Text=selected;
        };
        value.TextChanged+=(_,_)=>FromValue();
        value.IsEnabledChanged+=(_,_)=>picker.IsEnabled=value.IsEnabled;
        FromValue();
        return picker;
    }

    internal static DataGrid Table(params (string Header, string Binding)[] columns)
    {
        var grid = new DataGrid { CanUserResizeColumns = true, CanUserSortColumns = true,
            MinHeight = 235, MaxHeight = 495, AutoGenerateColumns = false,
            FontFamily=new FontFamily("Arial"),FontWeight=FontWeights.Medium,
            AlternationCount=2,EnableRowVirtualization=true,EnableColumnVirtualization=true,
            ColumnWidth = new DataGridLength(1, DataGridLengthUnitType.Star) };
        var textStyle=new Style(typeof(TextBlock));
        textStyle.Setters.Add(new Setter(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis));
        textStyle.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty,VerticalAlignment.Center));
        textStyle.Setters.Add(new Setter(TextBlock.TextAlignmentProperty,TextAlignment.Center));
        foreach(var (header,binding) in columns)
            grid.Columns.Add(new DataGridTextColumn {
                Header=header,Binding=new System.Windows.Data.Binding(binding)
                {
                    ConverterCulture=CultureInfo.GetCultureInfo("en-US"),
                    Converter=new AsciiDigitDisplayConverter()
                },
                ElementStyle=textStyle,MinWidth=84
            });
        return grid;
    }
    internal static ScrollViewer Wrap(string title, string subtitle, UIElement body)
    {
        // MainWindow already owns the only top-level page heading.
        // Keep the explanatory subtitle as tooltip rather than a second title block.
        var stack=Column();
        stack.ToolTip=subtitle;
        stack.Children.Add(body);
        return new ScrollViewer { FlowDirection = FlowDirection.RightToLeft,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = stack };
    }
    internal static bool Confirm(string text) => MessageBox.Show(text, "نجم كارد | تأكيد عملية", MessageBoxButton.YesNo,
        MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
}

