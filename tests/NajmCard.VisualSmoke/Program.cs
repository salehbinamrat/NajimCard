using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NajmCard.Core.Services;
using NajmCard.Desktop;
using NajmCard.Desktop.Services;
using NajmCard.Desktop.Views;

namespace NajmCard.VisualSmoke;

/// <summary>
/// Renders the production WPF shell and card creator without a router.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var destination=Path.GetFullPath(args.Length>0
                ? args[0] : "out/creator-1200x780.png");
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
            var window=new MainWindow();
            var width=(int)window.Width;
            var height=(int)window.Height;
            if(width!=1200 || height!=780)
                throw new InvalidDataException("Unexpected default window size.");

            ((Grid)window.FindName("LoginLayer")).Visibility=Visibility.Collapsed;
            ((Grid)window.FindName("WorkspaceLayer")).Visibility=Visibility.Visible;
            ((ListBox)window.FindName("NavList")).SelectedIndex=1;
            ((RowDefinition)window.FindName("WorkspaceHeaderRow")).Height=new GridLength(0);
            var pageHost=(ContentControl)window.FindName("PageHost");
            pageHost.Margin=new Thickness(4);
            pageHost.Content=creator;
            var root=(Grid)window.Content;
            window.Content=null;

            root.Measure(new Size(width,height));
            root.Arrange(new Rect(0,0,width,height));
            root.UpdateLayout();

            var workspace=(DockPanel)creator.Content;
            var footer=(StackPanel)workspace.Children[0];
            var footerBounds=footer.TransformToAncestor(root).TransformBounds(new Rect(footer.RenderSize));
            if(footer.ActualHeight<=0 || footerBounds.Bottom>height)
                throw new InvalidDataException("The creator action bar is outside the window.");

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
