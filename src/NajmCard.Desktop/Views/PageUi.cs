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
    internal static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(230, 210, 211));
    internal static Brush Gold => new SolidColorBrush(Color.FromRgb(243, 207, 148));
    internal static Border Surface(UIElement content, string accent = "#80593D") => new()
    {
        Child = content, CornerRadius = new CornerRadius(12), Padding = new Thickness(12),
        Margin = new Thickness(0, 0, 0, 8), BorderThickness = new Thickness(1),
        Background = new SolidColorBrush(Color.FromRgb(54, 16, 34)),
        BorderBrush = (Brush)new BrushConverter().ConvertFromInvariantString(accent)!
    };
    internal static StackPanel Column() => new() { Orientation = Orientation.Vertical };
    internal static TextBlock Title(string text) => new() { Text = text, FontSize = 23, FontWeight = FontWeights.Bold, FontFamily=new FontFamily("Arial"),
        Foreground = Gold, Margin = new Thickness(0, 0, 0, 6) };
    internal static TextBlock Hint(string text) => new() { Text = text, Foreground = Muted, FontSize = 14, FontWeight = FontWeights.Medium,
        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 17) };
    internal static TextBlock Label(string text) => new() { Text = text, Foreground = Gold, FontSize = 15, FontWeight=FontWeights.SemiBold,
        Margin = new Thickness(0, 6, 0, 3) };
    internal static Button Button(string name, bool primary = true) => new()
    {
        Content = name, Style = (Style)Application.Current.FindResource(primary ? "PrimaryButton" : "OutlineButton"),
        Margin = new Thickness(0, 0, 9, 0), MinWidth = 125, Padding = new Thickness(14, 10, 14, 10),
        FontFamily = new FontFamily("Arial"), FontWeight=FontWeights.Bold, FontSize=14
    };
    internal static TextBlock Status() => new() { TextWrapping = TextWrapping.Wrap,
        Foreground = Muted, Margin = new Thickness(0, 13, 0, 8), FontSize = 14, FontWeight=FontWeights.SemiBold };
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
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = stack };
    }
    internal static bool Confirm(string text) => MessageBox.Show(text, "نجم كارد | تأكيد عملية", MessageBoxButton.YesNo,
        MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
}

