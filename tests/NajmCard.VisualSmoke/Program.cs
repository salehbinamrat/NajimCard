using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NajmCard.Core.Services;
using NajmCard.Desktop.Services;
using NajmCard.Desktop.Views;

namespace NajmCard.VisualSmoke;

/// <summary>
/// Renders the actual WPF card creator at its owner-approved desktop size.
/// Only offline, read-only RouterOS queries are stubbed. Does not issue cards,
/// create a fake static screenshot, use a real router, or edit MainWindow.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        const int width=1648;
        const int height=928;
        try
        {
            var destination=Path.GetFullPath(args.Length>0
                ? args[0] : "out/creator-1648x928.png");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var application=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
            application.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source=new Uri("pack://application:,,,/NajmCard;component/Themes/BurgundyGold.xaml")
            });
            application.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source=new Uri("pack://application:,,,/NajmCard;component/Themes/VectorIcons.xaml")
            });
            var testData=Path.Combine(Path.GetTempPath(),
                "najmcard-ui-screenshot-"+Guid.NewGuid().ToString("N"));
            using var store=new VoucherStore(testData);
            var offline=new RouterGateway(new OfflineRouterClient());
            var creator=new VoucherWorkspaceView(offline,store,"offline-preview:8728");
            // The actual creator UI is rendered as an off-screen visual. This
            // excludes sidebar/login and contains no mock card graphics.
            var root=new Border
            {
                Width=width,Height=height,
                Background=(Brush)new BrushConverter().ConvertFrom("#1D0B19")!,
                Padding=new Thickness(4),
                Child=creator
            };
            root.Measure(new Size(width,height));
            root.Arrange(new Rect(0,0,width,height));
            root.UpdateLayout();

            var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);
            bitmap.Render(root);
            var encoder=new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using(var stream=File.Create(destination))encoder.Save(stream);
            if(new FileInfo(destination).Length<10_000)
                throw new InvalidDataException("Rendered UI PNG appears empty or incomplete.");
            Console.WriteLine("Actual WPF creator screenshot: "+destination+
                " ("+width+"x"+height+")");
            return 0;
        }
        catch(Exception ex)
        {
            Console.Error.WriteLine("WPF visual smoke test failed: "+ex);
            return 1;
        }
    }

    private sealed class OfflineRouterClient : IRouterCommandClient
    {
        public Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> CommandAsync(
            string command,IReadOnlyDictionary<string,string>? attributes=null,
            CancellationToken cancellationToken=default)
        {
            // Read-only fake: offscreen constructor cannot contact a router.
            return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string,string>>>(
                Array.Empty<IReadOnlyDictionary<string,string>>());
        }
    }
}
