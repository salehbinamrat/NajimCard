using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using NajmCard.Desktop.Services;

namespace NajmCard.Desktop.Views;

/// <summary>
/// A pre-send archive and a preview contain locally generated numbers, not
/// RouterOS-confirmed credentials. Never display them as printable vouchers.
/// Document creation and PDF export must happen on the same WPF STA thread.
/// </summary>
public partial class PrintingView
{
    private static void AddDraftWatermark(FixedPage page,
        double cardWidth, double cardHeight, double left, double top)
    {
        var warning=new Border
        {
            Width=cardWidth,Height=cardHeight,
            Background=new SolidColorBrush(Color.FromArgb(125,255,245,235)),
            BorderBrush=Brushes.DarkRed,
            BorderThickness=new Thickness(2),
            Child=new TextBlock
            {
                Text="معاينة تجريبية"+Environment.NewLine+"غير مفعلة في الراوتر",
                Foreground=Brushes.DarkRed,FontWeight=FontWeights.ExtraBold,
                FontFamily=new FontFamily("Arial"),FontSize=15,
                TextAlignment=TextAlignment.Center,
                TextWrapping=TextWrapping.Wrap,
                HorizontalAlignment=HorizontalAlignment.Center,
                VerticalAlignment=VerticalAlignment.Center,
                FlowDirection=FlowDirection.RightToLeft
            }
        };
        FixedPage.SetLeft(warning,left);
        FixedPage.SetTop(warning,top);
        page.Children.Add(warning);
    }

    private static Task RenderDocumentPdfOnStaAsync(Func<FixedDocument> buildDocument,
        string path)
    {
        var completion=new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var worker=new Thread(()=>
        {
            try
            {
                VoucherPdfExporter.Export(buildDocument(),path);
                completion.SetResult();
            }
            catch(Exception ex){completion.SetException(ex);}
        }){IsBackground=true};
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        return completion.Task;
    }
}
