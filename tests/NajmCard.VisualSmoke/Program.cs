using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
            root.FlowDirection=window.FlowDirection;
            window.Content=null;

            root.Measure(new Size(width,height));
            root.Arrange(new Rect(0,0,width,height));
            root.UpdateLayout();

            var workspace=(DockPanel)creator.Content;
            var footer=(StackPanel)workspace.Children[0];
            var footerBounds=footer.TransformToAncestor(root).TransformBounds(new Rect(footer.RenderSize));
            if(footer.ActualHeight<=0 || footerBounds.Bottom>height)
                throw new InvalidDataException("The creator action bar is outside the window.");

            VerifyPasswordPreview(root);
            root.UpdateLayout();

            Capture(root,width,height,destination);

            var dashboard=new DashboardView(offline,store,_=>{},"192.168.88.1");
            ((ListBox)window.FindName("NavList")).SelectedIndex=0;
            pageHost.Margin=new Thickness(12,7,12,7);
            pageHost.Content=dashboard;
            root.UpdateLayout();
            VerifyDashboardPanels(dashboard);
            Capture(root,width,height,Path.Combine(Path.GetDirectoryName(destination)!,"dashboard-1200x780.png"));
            var activity=new ActivityView(store);
            ((ListBox)window.FindName("NavList")).SelectedIndex=7;
            ((RowDefinition)window.FindName("WorkspaceHeaderRow")).Height=new GridLength(88);
            ((TextBlock)window.FindName("PageTitle")).Text="النشاط";
            ((TextBlock)window.FindName("PageSubtitle")).Text="سجل العمليات المحلية";
            pageHost.Margin=new Thickness(12,10,12,10);
            pageHost.Content=activity;
            root.UpdateLayout();
            var activityGrid=VisualChildren<DataGrid>(activity).Single();
            activityGrid.ItemsSource=Enumerable.Range(1,20).Select(i=>new VoucherActivityRow(
                "2026-11-10 10:00","CARDS","إنشاء دفعة","SUCCESS","دفعة اختبار "+i)).ToArray();
            activityGrid.SelectedIndex=1;
            root.Measure(new Size(width,height));
            root.Arrange(new Rect(0,0,width,height));
            root.UpdateLayout();
            root.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                new Action(()=>{}));
            root.UpdateLayout();
            if(activityGrid.Columns.Sum(column=>column.ActualWidth)<activityGrid.ActualWidth*0.8)
                throw new InvalidDataException("Activity table columns do not fill the available width.");
            if(!VisualChildren<System.Windows.Controls.Primitives.ScrollBar>(activityGrid)
                .Any(bar=>bar.Visibility==Visibility.Visible && bar.ActualHeight>0 &&
                    bar.Template.FindName("PART_Track",bar) is System.Windows.Controls.Primitives.Track))
                throw new InvalidDataException("The activity table did not render a scrolling track.");
            Capture(root,width,height,Path.Combine(Path.GetDirectoryName(destination)!,"activity-1200x780.png"));
            return 0;
        }
        catch(Exception ex)
        {
            Console.Error.WriteLine("WPF visual smoke test failed: "+ex);
            return 1;
        }
    }

    private static void Capture(Grid root,int width,int height,string destination)
    {
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder=new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using(var stream=File.Create(destination))encoder.Save(stream);
        if(new FileInfo(destination).Length<10_000)
            throw new InvalidDataException("Rendered UI PNG appears empty or incomplete.");
        Console.WriteLine("WPF screenshot: "+destination);
    }

    private static void VerifyDashboardPanels(DashboardView dashboard)
    {
        var panels=VisualChildren<Border>(dashboard)
            .Where(panel=>Equals(panel.Tag,"DashboardPanel")).ToArray();
        if(panels.Length!=10)
            throw new InvalidDataException("Dashboard panels are missing.");
        var cards=panels.Skip(1).ToArray();
        var bounds=cards.Select(card=>card.TransformToAncestor(dashboard)
            .TransformBounds(new Rect(card.RenderSize))).ToArray();
        if(bounds.Any(card=>card.Width<=0 || card.Height<=0 || card.Left<0 ||
            card.Right>dashboard.ActualWidth+1 || card.Bottom>dashboard.ActualHeight+1))
            throw new InvalidDataException("A dashboard card is clipped.");
        if(bounds.Max(card=>card.Width)-bounds.Min(card=>card.Width)>2)
            throw new InvalidDataException("Dashboard card columns are not aligned.");
    }

    private static void VerifyPasswordPreview(FrameworkElement root)
    {
        var mode=VisualChildren<ComboBox>(root).Single(box=>
            box.Items.Contains("اسم مستخدم وكلمة مرور"));
        var password=VisualChildren<TextBlock>(root).Single(text=>text.Name=="CardPasswordPreview");
        if(password.Visibility!=Visibility.Collapsed)
            throw new InvalidDataException("Username-only cards must hide the password.");
        mode.SelectedIndex=1;
        root.UpdateLayout();
        if(password.Visibility!=Visibility.Visible || password.Text.Length==0 ||
            password.ActualWidth<=0)
            throw new InvalidDataException("The separate password preview did not render.");
        var initialSize=password.FontSize;
        password.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,0,120)
        {
            RoutedEvent=Mouse.PreviewMouseWheelEvent
        });
        if(password.FontSize<=initialSize)
            throw new InvalidDataException("The password did not resize with the mouse wheel.");
        mode.SelectedIndex=0;
        if(password.Visibility!=Visibility.Collapsed || password.Text.Length!=0)
            throw new InvalidDataException("The preview retained the previous password.");
    }

    private static IEnumerable<T> VisualChildren<T>(DependencyObject parent) where T:DependencyObject
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {
            var child=VisualTreeHelper.GetChild(parent,i);
            if(child is T match)yield return match;
            foreach(var descendant in VisualChildren<T>(child))yield return descendant;
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
