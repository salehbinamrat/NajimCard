using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using NajmCard.Desktop.Services;

namespace NajmCard.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Preserve Arabic layout and labels; format numbers and dates with
        // Western (ASCII) digits throughout WPF bindings and worker threads.
        var numberCulture=CultureInfo.GetCultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentCulture=numberCulture;
        Thread.CurrentThread.CurrentCulture=numberCulture;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage("en-US")));
        EventManager.RegisterClassHandler(typeof(TextBox),TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler(NormalizeAsciiDigits));
        base.OnStartup(e);
    }

    private static void NormalizeAsciiDigits(object sender,TextChangedEventArgs e)
    {
        if(sender is not TextBox box || box.AcceptsReturn ||
           box.Name.Contains("Password",StringComparison.OrdinalIgnoreCase))
            return; // Do not rewrite template text or passwords.
        var normalized=AsciiDigitNormalizer.NormalizeAsciiDigits(box.Text);
        if(normalized==box.Text)return;
        var caret=box.SelectionStart;
        var length=box.SelectionLength;
        box.Text=normalized;
        box.Select(Math.Min(caret,normalized.Length),
            Math.Min(length,normalized.Length-Math.Min(caret,normalized.Length)));
    }
}
